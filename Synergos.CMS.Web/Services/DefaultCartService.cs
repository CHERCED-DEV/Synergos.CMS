using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ICartService"/> que persiste el cart en una
/// cookie HMAC-firmada del visitante. Sin DB, sin login required.
/// </summary>
/// <remarks>
/// Formato de cookie: <c>{base64(payloadJson)}.{base64(hmacSha256)}</c>.
/// La firma HMAC previene tampering del lado del cliente. Si la firma
/// no valida, el cart se trata como vacío (no se lanza excepción —
/// fail-open hacia "cart vacío").
///
/// Cada llamada a <see cref="GetCart"/> hidrata los items leyendo
/// <c>productPage</c> publicados por SKU. Cambios de precio en CMS
/// se reflejan al siguiente render del cart.
///
/// SKUs huérfanos (producto eliminado) se omiten silenciosamente y se
/// limpian de la cookie en el siguiente write.
/// </remarks>
public sealed class DefaultCartService : ICartService
{
    private const string ProductPageAlias = "productPage";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly CartSettings _settings;
    private readonly byte[] _secretKey;

    public DefaultCartService(
        IHttpContextAccessor httpContextAccessor,
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptions<CartSettings> settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings.Value;
        _secretKey = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(_settings.SecretKey)
            ? "synergos-dev-secret-change-me"
            : _settings.SecretKey);
    }

    public Cart GetCart() => Hydrate(ReadCookie());

    public Cart AddItem(string sku, int quantity = 1, string? variantSku = null)
    {
        if (string.IsNullOrWhiteSpace(sku) || quantity <= 0)
        {
            return GetCart();
        }
        var raw = ReadCookie();
        var key = MakeKey(sku, variantSku);
        var idx = raw.FindIndex(i => i.Key == key);
        if (idx >= 0)
        {
            raw[idx] = raw[idx] with { Quantity = raw[idx].Quantity + quantity };
        }
        else
        {
            if (raw.Count >= _settings.MaxItems)
            {
                return Hydrate(raw);
            }
            raw.Add(new RawCartLine(sku, variantSku, quantity, key));
        }
        WriteCookie(raw);
        return Hydrate(raw);
    }

    public Cart UpdateQuantity(string sku, int quantity, string? variantSku = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return GetCart();
        }
        if (quantity <= 0)
        {
            return RemoveItem(sku, variantSku);
        }
        var raw = ReadCookie();
        var key = MakeKey(sku, variantSku);
        var idx = raw.FindIndex(i => i.Key == key);
        if (idx >= 0)
        {
            raw[idx] = raw[idx] with { Quantity = quantity };
            WriteCookie(raw);
        }
        return Hydrate(raw);
    }

    public Cart RemoveItem(string sku, string? variantSku = null)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return GetCart();
        }
        var raw = ReadCookie();
        var key = MakeKey(sku, variantSku);
        var removed = raw.RemoveAll(i => i.Key == key);
        if (removed > 0)
        {
            WriteCookie(raw);
        }
        return Hydrate(raw);
    }

    public Cart Clear()
    {
        WriteCookie(new List<RawCartLine>());
        return new Cart(Array.Empty<CartLine>(), 0m, _settings.Currency, 0);
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private record RawCartLine(string Sku, string? VariantSku, int Quantity, string Key);

    private static string MakeKey(string sku, string? variantSku) =>
        string.IsNullOrWhiteSpace(variantSku) ? sku : $"{sku}|{variantSku}";

    private List<RawCartLine> ReadCookie()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return new List<RawCartLine>();
        if (!ctx.Request.Cookies.TryGetValue(_settings.CookieName, out var cookieValue) ||
            string.IsNullOrWhiteSpace(cookieValue))
        {
            return new List<RawCartLine>();
        }

        var parts = cookieValue.Split('.', 2);
        if (parts.Length != 2) return new List<RawCartLine>();

        try
        {
            var payloadBytes = Convert.FromBase64String(parts[0]);
            var sigBytes = Convert.FromBase64String(parts[1]);
            using var hmac = new HMACSHA256(_secretKey);
            var expected = hmac.ComputeHash(payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(expected, sigBytes))
            {
                return new List<RawCartLine>();
            }
            var json = Encoding.UTF8.GetString(payloadBytes);
            var items = JsonSerializer.Deserialize<List<RawCartItemDto>>(json) ?? new List<RawCartItemDto>();
            return items
                .Where(i => !string.IsNullOrWhiteSpace(i.Sku) && i.Quantity > 0)
                .Select(i => new RawCartLine(i.Sku, i.VariantSku, i.Quantity, MakeKey(i.Sku, i.VariantSku)))
                .ToList();
        }
        catch
        {
            return new List<RawCartLine>();
        }
    }

    private void WriteCookie(List<RawCartLine> items)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null) return;

        if (items.Count == 0)
        {
            ctx.Response.Cookies.Delete(_settings.CookieName);
            return;
        }

        var dto = items.Select(i => new RawCartItemDto(i.Sku, i.VariantSku, i.Quantity)).ToArray();
        var json = JsonSerializer.Serialize(dto);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(_secretKey);
        var sigBytes = hmac.ComputeHash(payloadBytes);
        var cookieValue = $"{Convert.ToBase64String(payloadBytes)}.{Convert.ToBase64String(sigBytes)}";

        ctx.Response.Cookies.Append(_settings.CookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(_settings.CookieLifetimeDays),
            Path = "/",
        });
    }

    private Cart Hydrate(List<RawCartLine> raw)
    {
        if (raw.Count == 0)
        {
            return new Cart(Array.Empty<CartLine>(), 0m, _settings.Currency, 0);
        }
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            return new Cart(Array.Empty<CartLine>(), 0m, _settings.Currency, 0);
        }

        // Index productPage by SKU once
        var products = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelfOfType(ProductPageAlias))
            .ToDictionary(
                p => p.Value<string>("productSku") ?? string.Empty,
                p => p,
                StringComparer.OrdinalIgnoreCase);

        var lines = new List<CartLine>(raw.Count);
        decimal subtotal = 0m;
        int itemCount = 0;
        foreach (var item in raw)
        {
            if (!products.TryGetValue(item.Sku, out var product))
            {
                continue;
            }
            var name = product.Value<string>("productName") ?? product.Name ?? item.Sku;
            var priceRaw = product.Value<string>("productPriceBase");
            if (!decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var unitPrice))
            {
                unitPrice = 0m;
            }
            // Apply variant priceDelta if applicable
            if (!string.IsNullOrWhiteSpace(item.VariantSku))
            {
                var variantsJson = product.Value<string>("productVariantsJson");
                unitPrice += GetVariantPriceDelta(variantsJson, item.VariantSku);
            }
            var lineTotal = unitPrice * item.Quantity;
            subtotal += lineTotal;
            itemCount += item.Quantity;
            var images = product.Value<IEnumerable<IPublishedContent>>("productImages");
            var imageUrl = images?.FirstOrDefault()?.Url();

            lines.Add(new CartLine(
                Sku: item.Sku,
                VariantSku: item.VariantSku,
                Quantity: item.Quantity,
                ProductName: name,
                UnitPrice: unitPrice,
                LineTotal: lineTotal,
                ImageUrl: imageUrl,
                ProductUrl: product.Url()));
        }

        return new Cart(lines, subtotal, _settings.Currency, itemCount);
    }

    private static decimal GetVariantPriceDelta(string? variantsJson, string variantSku)
    {
        if (string.IsNullOrWhiteSpace(variantsJson)) return 0m;
        try
        {
            using var doc = JsonDocument.Parse(variantsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0m;
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                if (!v.TryGetProperty("sku", out var skuEl) || skuEl.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(skuEl.GetString(), variantSku, StringComparison.OrdinalIgnoreCase)) continue;
                if (v.TryGetProperty("priceDelta", out var deltaEl) && deltaEl.TryGetDecimal(out var delta))
                {
                    return delta;
                }
                return 0m;
            }
        }
        catch { /* malformed JSON → ignore */ }
        return 0m;
    }

    private sealed record RawCartItemDto(string Sku, string? VariantSku, int Quantity);
}
