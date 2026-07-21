using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Siembra una orden PAGADA para un miembro existente, para poder ejercitar el gate de
/// comprador verificado de T10 (ADR 0114) con una sesión real. Tooling dev tras el flag
/// <c>Synergos:DevSeed:Enabled</c> — nada corre en boot (ADR 0013).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pasa por el FLUJO REAL</b> (<c>CheckoutAsync</c> → <c>ConfirmAsync</c>) en vez de
/// escribir una orden `Paid` a mano en el store. Fabricar el registro sería más corto y
/// probaría menos: una orden que no nació del checkout puede tener una forma que el resto
/// del sistema no produce nunca, y entonces el gate quedaría "verificado" contra un dato que
/// no existe en la vida real.
/// </para>
/// <para>
/// <b>NO crea miembros ni toca credenciales.</b> Exige que el miembro ya exista: sembrar
/// identidades es otra cosa, y una herramienta que las fabrique tras un flag es justo lo que
/// ADR 0108 señala como la superficie que no debe poder desplegarse.
/// </para>
/// </remarks>
public sealed class DevPaidOrderSeeder
{
    private readonly IMemberService _memberService;
    private readonly IShopOrderService _orders;
    private readonly IShopQuery _shopQuery;
    private readonly ILogger<DevPaidOrderSeeder> _logger;

    public DevPaidOrderSeeder(
        IMemberService memberService,
        IShopOrderService orders,
        IShopQuery shopQuery,
        ILogger<DevPaidOrderSeeder> logger)
    {
        _memberService = memberService;
        _orders = orders;
        _shopQuery = shopQuery;
        _logger = logger;
    }

    public sealed record SeedResult(bool Ok, string? Error, string? OrderRef, string? Sku, Guid MemberKey);

    public async Task<SeedResult> SeedAsync(string email, string sku, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(sku))
        {
            return new SeedResult(false, "email y sku son requeridos.", null, null, Guid.Empty);
        }

        var member = _memberService.GetByEmail(email);
        if (member is null)
        {
            // NO se crea. Que falte el miembro es un hecho del entorno, no algo que esta
            // herramienta deba resolver inventándolo.
            return new SeedResult(false, $"No existe un miembro con email '{email}'.", null, null, Guid.Empty);
        }

        var product = _shopQuery.GetProductBySku(sku);
        if (product is null)
        {
            return new SeedResult(false, $"No existe un producto con SKU '{sku}'.", null, null, member.Key);
        }

        // La identidad va en el ShopCustomer.MemberKey: es lo que liga la orden a su dueño y
        // lo que después mira el gate de reseñas (T2 — ownership por memberKey, no por email).
        var checkout = await _orders.CheckoutAsync(
            new[] { new ShopCartItem(product.Sku, null, 1) },
            new ShopCustomer(member.Name ?? member.Email, member.Email, member.Key),
            cancellationToken).ConfigureAwait(false);

        await _orders.ConfirmAsync(checkout.OrderRef, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "DevPaidOrderSeeder: orden {OrderRef} PAGADA para {Email} sobre {Sku}.",
            checkout.OrderRef, member.Email, product.Sku);

        return new SeedResult(true, null, checkout.OrderRef, product.Sku, member.Key);
    }
}
