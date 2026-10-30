using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Contracts;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Synergos.Shared;
using TiendaCapabilities = Synergos.Bff.Tienda.Clients.TiendaCapabilities;

namespace Synergos.Bff.Tienda.Endpoints;

/// <summary>El ruteo del orquestador de Tienda.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO y su nombre con <c>WithName</c> (el molde F2): de ahí
/// sale el contrato publicado (<c>docs/contracts/openapi/Synergos.Bff.Tienda.json</c>). Son las rutas de la compra
/// imperativa (<see cref="PurchaseFlow"/>), las que usa el CMS con <c>Synergos:Tienda:Mode=Bff</c>, y las de la oferta
/// del catálogo (<see cref="OfertaDeTienda"/>), que el CMS llama al publicar; las de la puerta las pone el flujo
/// declarado (<c>flujos/tienda.compra.json</c>) en <c>/v1/flujos/tienda.compra</c>.
/// </remarks>
public static class TiendaEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "tienda";

    public static IEndpointRouteBuilder MapTiendaEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        // Por eso acepta una llave más corta que la de una capacidad: de ella cuelgan las de cada
        // paso, y una que no cupiera rompería a mitad de la saga (ConLlaveDeSaga).
        app.MapPost("/v1/purchases", async Task<Results<Created<PurchaseResponse>, ProblemHttpResult>> (
            BuyRequest req, HttpRequest http, PurchaseFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            if (string.IsNullOrWhiteSpace(req.CartId)) return Invalid("bad_cart", "Hace falta cartId.");

            var r = await flow.BuyAsync(req.CartId!, key.Value, ct);

            return r.Map(PurchaseResponse.From).ToCreated(s => $"/v1/purchases/{s.Id}");
        }).WithName("BuyCart").ConLlaveDeSaga();

        app.MapGet("/v1/purchases/{id}", Results<Ok<PurchaseResponse>, ProblemHttpResult> (string id, PurchaseFlow flow) =>
            flow.Get(id).Map(PurchaseResponse.From).ToHttp()).WithName("GetPurchase");

        app.MapPost("/v1/purchases/{id}/confirm", async Task<Results<Ok<PurchaseResponse>, ProblemHttpResult>> (
            string id, ConfirmRequest req, PurchaseFlow flow, CancellationToken ct) =>
        {
            if (req.To is null || string.IsNullOrWhiteSpace(req.To.Line1) || string.IsNullOrWhiteSpace(req.To.City))
            {
                // Se comprueba ANTES de capturar. Descubrir que falta la dirección después de
                // mover plata costaría una devolución por un campo de formulario.
                return Invalid("bad_address", "Hace falta una dirección de entrega con línea y ciudad.");
            }
            if (string.IsNullOrWhiteSpace(req.Carrier)) return Invalid("bad_carrier", "Hace falta la transportadora.");

            return (await flow.ConfirmAsync(id, req.To, req.Carrier!, ct)).Map(PurchaseResponse.From).ToHttp();
        }).WithName("ConfirmPurchase");

        app.MapPost("/v1/purchases/{id}/cancel", async Task<Results<Ok<PurchaseResponse>, ProblemHttpResult>> (
            string id, PurchaseFlow flow, CancellationToken ct) =>
            (await flow.CancelAsync(id, ct)).Map(PurchaseResponse.From).ToHttp()).WithName("CancelPurchase");

        // Devolver una PARTE de lo cobrado, por una compra que SÍ se cumplió (#57).
        //
        // No es una compensación: aquélla deshace lo que quedó a medias y la ordena la máquina.
        // Esto lo ordena quien vendió, sobre una compra completa, y no deshace nada — el pedido
        // siguió existiendo y el cliente devolvió una caja.
        //
        // Hace falta porque LA PLATA LA TIENE ESTE LADO. El expediente del RMA vive en el CMS,
        // pero el id de pago de Api.Payments no sale de acá a propósito: lo que el CMS conoce es
        // el id de la SAGA. Sin esta puerta le pedía el reembolso a su proveedor local, que no
        // conoce esa llave, y el RMA no llegaba nunca a reembolsado.
        //
        // EXIGE llave, y no por ceremonia: devolver plata es un movimiento RELATIVO, así que un
        // reintento tras un timeout sin llave devolvería dos veces. Es la misma razón por la que
        // el ajuste relativo de Api.Inventory la exige y el absoluto no (#30).
        app.MapPost("/v1/purchases/{id}/refund", async Task<Results<Ok<PurchaseResponse>, ProblemHttpResult>> (
            string id, RefundPurchaseRequest req, HttpRequest http, PurchaseFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            // El cuerpo es requerido en el contrato (lo que genera el UI no admite uno opcional); sin él
            // contesta el framework, antes de mirar la llave. Lo que falte DENTRO lo dice este rechazo.
            if (req.Amount is not { } m)
            {
                return Invalid("bad_refund", "Hace falta el monto a devolver.");
            }
            if (!TryMoney(m, out var monto, out var mala)) return mala!;

            return (await flow.RefundAsync(id, monto, req.Reason, key, ct)).Map(PurchaseResponse.From).ToHttp();
        }).WithName("RefundPurchase").ConLlaveDeIdempotencia();

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, "se rinde a los ocho intentos" sería "se abandona", y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/purchases/{id}/retry", async Task<Results<Ok<PurchaseResponse>, ProblemHttpResult>> (
            string id, PurchaseFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(PurchaseResponse.From).ToHttp()).WithName("RetryPurchase");

        // La oferta de un producto: el precio y las existencias de cada variante (004-tienda-compra, R-9).
        // La llama el CMS al publicar, no un navegador, así que no lleva marca de la puerta ni cabeceras
        // suyas. Repetirla no duplica nada; ver OfertaDeTienda.
        app.MapPost("/v1/ofertas", async Task<Results<Ok<ProductOfferResponse>, ProblemHttpResult>> (
            PublishProductOfferRequest req, HttpRequest http, OfertaDeTienda ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var oferta = new OfertaDeProducto(req.ProductId, req.Currency, req.Variants?
                .Select(v => new VarianteOfertada(v.VariantId, v.Price, v.Stock))
                .ToList());
            return (await ofertas.PublicarAsync(oferta, key, ct)).Map(ProductOfferResponse.From).ToHttp();
        }).WithName("PublishProductOffer").ConLlaveDeIdempotencia();

        // Retirar la oferta de un producto que se despublicó, fue a la papelera o se borró: ninguna variante
        // se vende más. Lo llama el CMS, como publicarla; sin marca de la puerta.
        app.MapPost("/v1/ofertas/{productId}/retirar", async Task<Results<Ok<ProductOfferResponse>, ProblemHttpResult>> (
            string productId, HttpRequest http, OfertaDeTienda ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            return (await ofertas.RetirarAsync(productId, key, ct)).Map(ProductOfferResponse.From).ToHttp();
        }).WithName("RetireProductOffer").ConLlaveDeIdempotencia();

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió
        // solo existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", Ok<PageResponse<PendingCompensationResponse>> (int? offset, int? limit, PurchaseFlow flow) =>
        {
            var todas = flow.PendingCompensations()
                .SelectMany(s => s.Pending().Select(c => new PendingCompensationResponse(
                    s.Id, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError,
                    c.IsStuck, s.AlertedAtUtc)))
                .ToList();

            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            return TypedResults.Ok(new PageResponse<PendingCompensationResponse>(
                todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
        }).WithName("ListPurchaseCompensations");

        return app;
    }

    /// <summary>El flujo cuyas compras lee <see cref="MapLecturaDelEnvio"/>.</summary>
    public const string FlujoDeLaCompra = "tienda.compra";

    /// <summary>Lo que contesta la lectura del envío por una compra que no es una completada con envío.</summary>
    public const string CompraNoEncontrada = "tienda.compra_no_encontrada";

    /// <summary>
    /// La lectura del envío de una compra de la puerta (013-seller-operaciones, T009, R3): el interino de NS-2. Sólo con
    /// el orquestador declarado, que es quien guarda las sagas de <see cref="FlujoDeLaCompra"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Sin marca de la puerta</b>: el navegador no la pide. La pide el CMS, con la llave compartida, después de
    /// comprobar el rol del personal. La dirección sólo la guarda el envío (004, R-11), y quien despacha la necesita.</para>
    ///
    /// <para><b>Sólo de una compra completada con envío</b>: una en curso todavía no despachó, y una deshecha no tiene
    /// nada que despachar. Las dos, una que no existe y una de otro flujo contestan lo mismo.</para>
    ///
    /// <para><b>No escribe nada.</b> Con NS-2 se vuelve una lectura del personal declarada en el flujo, y esta ruta se
    /// conserva hasta que nadie la pida.</para>
    /// </remarks>
    public static IEndpointRouteBuilder MapLecturaDelEnvio(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/envios/{compra}", async Task<Results<Ok<EnvioDeLaCompraResponse>, ProblemHttpResult>> (
            string compra, ISagaStore<SagaDeFlujo> sagas, TiendaCapabilities caps, CancellationToken ct) =>
        {
            var saga = sagas.Find(compra);
            if (saga is null || saga.Flujo != FlujoDeLaCompra || saga.Status != SagaStatus.Completed
                || saga.Datos.GetValueOrDefault("envio") is not { Valor: { ValueKind: JsonValueKind.String } envio })
            {
                return Rejection.NotFound(CompraNoEncontrada, "No hay una compra completada y despachada con ese id.").ToProblem();
            }

            var leido = await caps.GetShipmentAsync(envio.GetString()!, ct);
            return leido.Map(e => new EnvioDeLaCompraResponse(compra, new EnvioResponse(
                e.Id,
                e.To is { } a ? new DestinoDelEnvioResponse(a.Line1, a.Line2, a.City, a.Region, a.PostalCode, a.Country, a.Contact) : null,
                e.Carrier,
                e.Status,
                (e.Events ?? []).Select(v => new EventoDelEnvioResponse(v.Status, v.Note, v.AtUtc)).ToList()))).ToHttp();
        }).WithName("LeerEnvioDeLaCompra");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();

    /// <summary>
    /// Lee un monto del cuerpo, o dice por qué no.
    /// </summary>
    /// <remarks>
    /// <c>Money.Of</c> lanza con una moneda vacía o mal formada, y una excepción en el borde sale
    /// como 500 — un problema del servidor por un cuerpo que mandó el cliente. Se valida la FORMA
    /// acá (CLAUDE.md §15) y se deja el negocio en <c>Domain/</c>.
    /// </remarks>
    private static bool TryMoney(MoneyDto dto, out Money monto, out ProblemHttpResult? mala)
    {
        monto = default;
        mala = null;

        if (string.IsNullOrWhiteSpace(dto.Currency) || dto.Currency.Trim().Length != 3)
        {
            mala = Invalid("bad_refund", $"Moneda inválida: '{dto.Currency}'.");
            return false;
        }

        monto = Money.Of(dto.Amount, dto.Currency.Trim().ToUpperInvariant());
        return true;
    }
}
