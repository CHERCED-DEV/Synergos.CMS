using Synergos.Api.Pricing.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Pricing.Domain;

/// <summary>Compone las reglas de <see cref="PricingRules"/> con los almacenes.</summary>
public sealed class PricingService
{
    private readonly IPriceStore _prices;
    private readonly IPromotionStore _promotions;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public PricingService(IPriceStore prices, IPromotionStore promotions, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _prices = prices;
        _promotions = promotions;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>Publica o actualiza el precio de un sujeto.</summary>
    /// <remarks>
    /// Reescribe si ese sujeto ya tenía precio: subir el precio de un producto no es crear otro
    /// producto, y rechazarlo obligaría a cada dominio a llevar la cuenta de qué ya publicó.
    /// </remarks>
    public Result<Price> SetPrice(Ref subject, Money amount, int taxBasisPoints, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("price", key) is { } yaEra)
            {
                return _prices.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{PricingRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el precio no está.");
            }

            var motivo = PricingRules.CheckPrice(amount, taxBasisPoints);
            if (motivo is not null) return Result.Rejected<Price>(motivo);

            var id = _prices.FindBySubject(subject)?.Id ?? Guid.NewGuid().ToString("n");
            var price = new Price(id, subject, amount, taxBasisPoints, Now);
            _prices.Put(price);
            _idempotency.Remember("price", key, id);
            return Result.Ok(price);
        }
    }

    public Result<Price> GetPrice(string id)
        => _prices.Find(id) is { } p
            ? Result.Ok(p)
            : Rejection.NotFound($"{PricingRules.CodePrefix}.price_not_found", $"No existe el precio {id}.");

    public Result<Promotion> SavePromotion(string? code, DiscountKind kind, decimal value, string currency, TimeWindow validity, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("promotion", key) is { } yaEra)
            {
                return _promotions.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{PricingRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la promoción no está.");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return Rejection.Invalid($"{PricingRules.CodePrefix}.code_required", "La promoción necesita un código.");
            }
            if (value <= 0)
            {
                return Rejection.Invalid($"{PricingRules.CodePrefix}.bad_discount", "El descuento tiene que ser mayor que cero.");
            }
            if (kind == DiscountKind.Percentage && value > PricingRules.FullBasisPoints)
            {
                return Rejection.Invalid($"{PricingRules.CodePrefix}.discount_over_100",
                    "Un descuento porcentual no puede pasar del 100 %.");
            }
            if (_promotions.FindByCode(code!) is not null)
            {
                return Rejection.Conflict($"{PricingRules.CodePrefix}.code_taken", $"Ya hay una promoción '{code}'.");
            }

            var id = Guid.NewGuid().ToString("n");
            var promo = new Promotion(id, code!.Trim(), kind, value, currency, validity);
            _promotions.Put(promo);
            _idempotency.Remember("promotion", key, id);
            return Result.Ok(promo);
        }
    }

    /// <summary>
    /// Cotiza unas líneas, con promoción opcional.
    /// </summary>
    /// <remarks>
    /// <para><b>El descuento se aplica ANTES del impuesto.</b> Al revés se cobraría impuesto
    /// sobre plata que nadie pagó, y eso no es un redondeo: es un cargo indebido que aparece en
    /// cada factura con promoción.</para>
    ///
    /// <para><b>Cotizar no guarda nada.</b> Una cotización es una lectura: si persistiera, cada
    /// vez que alguien mira un carrito quedaría un registro, y el almacén crecería con ruido que
    /// nadie consulta. Lo que se guarda es el pedido, y eso es de <c>Api.Orders</c>.</para>
    /// </remarks>
    public Result<Quote> Quote(IReadOnlyList<(Ref Subject, int Quantity)> items, string? promoCode)
    {
        if (items.Count == 0)
        {
            return Rejection.Invalid($"{PricingRules.CodePrefix}.empty_quote", "Una cotización sin líneas no cotiza nada.");
        }
        if (items.Count > PricingRules.MaxLines)
        {
            return Rejection.Invalid($"{PricingRules.CodePrefix}.too_many_lines", $"Hasta {PricingRules.MaxLines} líneas.");
        }

        var lineas = new List<QuoteLine>(items.Count);
        string? moneda = null;

        foreach (var (subject, cantidad) in items)
        {
            var motivo = PricingRules.CheckQuantity(cantidad);
            if (motivo is not null) return Result.Rejected<Quote>(motivo);

            if (_prices.FindBySubject(subject) is not { } price)
            {
                return Rejection.NotFound($"{PricingRules.CodePrefix}.price_not_found", $"No hay precio publicado para {subject}.");
            }

            moneda ??= price.Amount.Currency;
            if (!string.Equals(moneda, price.Amount.Currency, StringComparison.Ordinal))
            {
                // Sin esto, Money lanzaría al sumar — y una excepción es un 500. Acá el llamador
                // recibe un rechazo con código y sabe exactamente qué mezcló.
                return Rejection.Invalid($"{PricingRules.CodePrefix}.mixed_currencies",
                    $"La cotización mezcla {moneda} y {price.Amount.Currency}.");
            }

            var subtotal = price.Amount * cantidad;
            lineas.Add(new QuoteLine(subject, cantidad, price.Amount, subtotal,
                PricingRules.Tax(subtotal, price.TaxRateBasisPoints)));
        }

        var baseAntes = Money.Sum(lineas.Select(l => l.Subtotal), moneda!);
        var descuento = Money.Zero(moneda!);

        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var promo = _promotions.FindByCode(promoCode!);
            var motivo = PricingRules.CheckPromotion(promo, Now);
            if (motivo is not null) return Result.Rejected<Quote>(motivo);

            var calculado = PricingRules.Discount(promo!, baseAntes);
            if (!calculado.IsOk) return Result.Rejected<Quote>(calculado.Rejection!);
            descuento = calculado.Value;
        }

        // El impuesto se recalcula sobre la base YA descontada, proporcionalmente por línea:
        // usar el impuesto de las líneas sin descontar cobraría de más.
        var baseDespues = baseAntes - descuento;
        var factor = baseAntes.IsZero ? 0m : baseDespues.Amount / baseAntes.Amount;
        var impuesto = Money.Of(lineas.Sum(l => l.Tax.Amount) * factor, moneda!);

        return Result.Ok(new Quote(lineas, baseAntes, descuento, impuesto, baseDespues + impuesto));
    }
}
