using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Contracts;
using Synergos.Bff.Eventos.Domain;
using Synergos.Bff.Pasos;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Eventos.Endpoints;

/// <summary>El ruteo del orquestador de Eventos.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO y su nombre con <c>WithName</c>: de ahí
/// sale el contrato publicado (<c>docs/contracts/openapi/Synergos.Bff.Eventos.json</c>, ADR 0140),
/// el que el UI convierte en tipos. Lo que pone la puerta del CMS (quién compra, la comisión, el
/// contacto del aviso) viaja en cabeceras declaradas con <c>.ConCabeceraDeLaPuerta</c> y se lee ANTES
/// que cualquier otra regla (ADR 0140 F3); el cuerpo es sólo lo del navegador. Consultar, confirmar y
/// cancelar exigen el sujeto y una compra de otro no existe para él. Las cuatro van marcadas con
/// <c>.EnLaPuerta</c>: es lo que el CMS abre al navegador, por <c>/api/flujos/eventos.compra/…</c>.
/// </remarks>
public static class EventosEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "eventos";

    /// <summary>
    /// La compra en la puerta del CMS (ADR 0140 F3): la clave de su definición, así la puerta y
    /// <c>flujos/eventos.compra.json</c> no pueden nombrar dos flujos distintos.
    /// </summary>
    /// <remarks>
    /// Se exponen abrir, consultar, cerrar y cancelar, y nada más. Reintentar, la vista de
    /// compensaciones y la oferta son de operación y del CMS, no del navegador: sin marca no existen
    /// para la puerta (lo vigila <c>LoQueExponeLaPuertaTests</c>).
    /// </remarks>
    private static string Flujo => EventosFlujos.Compra.Clave;

    public static IEndpointRouteBuilder MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        // Por eso acepta una llave más corta que la de una capacidad: de ella cuelgan las de cada
        // paso, y una que no cupiera rompería a mitad de la saga (ConLlaveDeSaga).
        app.MapPost("/v1/ticket-purchases", async Task<Results<Created<TicketPurchaseResponse>, ProblemHttpResult>> (
            BuyTicketsRequest req, HttpRequest http, TicketingFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            if (Sujeto(http, out var buyer) is { } sinSujeto) return sinSujeto;
            if (Negocio(http, out var negocio) is { } sinNegocio) return sinNegocio;
            if (string.IsNullOrWhiteSpace(req.EventId)) return Invalid("bad_event", "Hace falta eventId.");

            if (req.Lines is null || req.Lines.Count == 0) return Invalid("no_lines", "Hace falta al menos una línea.");

            var lineas = req.Lines
                .Select(l => new TicketLine(l.Tier ?? string.Empty, l.Seat, l.Quantity))
                .ToList();

            var r = await flow.BuyAsync(req.EventId!, buyer!, lineas, negocio!.FeePercent!.Value, key.Value, ct);

            return r.Map(TicketPurchaseResponse.From).ToCreated(s => $"/v1/ticket-purchases/{s.Id}");
        }).WithName("BuyTickets").ConLlaveDeSaga()
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Negocio, requerida: true)
          .EnLaPuerta(Flujo, "abrir");

        app.MapGet("/v1/ticket-purchases/{id}", Results<Ok<TicketPurchaseResponse>, ProblemHttpResult> (
            string id, HttpRequest http, TicketingFlow flow) =>
        {
            if (Sujeto(http, out var dueno) is { } sinSujeto) return sinSujeto;
            return flow.Get(id, dueno!).Map(TicketPurchaseResponse.From).ToHttp();
        }).WithName("GetTicketPurchase").ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .EnLaPuerta(Flujo, "consultar");

        // Confirmar NO recibe cuerpo, al revés que en Tienda: allá hacía falta la dirección de
        // entrega antes de capturar. Una entrada no se despacha, así que no hay nada que validar
        // antes de mover plata. Lo que sí puede traer es el CONTACTO del aviso, que pone la puerta
        // en una cabecera (ADR 0140 F3): se lee primero, y sin él el paso de aviso no manda nada.
        app.MapPost("/v1/ticket-purchases/{id}/confirm", async Task<Results<Ok<TicketPurchaseResponse>, ProblemHttpResult>> (
            string id, HttpRequest http, TicketingFlow flow, CancellationToken ct) =>
        {
            if (Sujeto(http, out var dueno) is { } sinSujeto) return sinSujeto;
            if (!CabecerasDeLaPuerta.TryLeerJson<ContactoDeLaPuerta>(http, CabecerasDeLaPuerta.Contacto, out var contacto)
                || contacto is { Correo: null or "" })
            {
                return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(CodePrefix, CabecerasDeLaPuerta.Contacto),
                    "El contacto del aviso no se puede leer: base64url de {correo, nombre, enlace, sitio}.").ToProblem();
            }

            var aviso = contacto is null ? null : new Contacto(contacto.Correo!, contacto.Nombre, contacto.Enlace, contacto.Sitio);
            return (await flow.ConfirmAsync(id, dueno, aviso, ct)).Map(TicketPurchaseResponse.From).ToHttp();
        }).WithName("ConfirmTicketPurchase")
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Contacto)
          .EnLaPuerta(Flujo, "cerrar");

        app.MapPost("/v1/ticket-purchases/{id}/cancel", async Task<Results<Ok<TicketPurchaseResponse>, ProblemHttpResult>> (
            string id, HttpRequest http, TicketingFlow flow, CancellationToken ct) =>
        {
            if (Sujeto(http, out var dueno) is { } sinSujeto) return sinSujeto;
            return (await flow.CancelAsync(id, dueno!, ct)).Map(TicketPurchaseResponse.From).ToHttp();
        }).WithName("CancelTicketPurchase").ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true)
          .EnLaPuerta(Flujo, "cancelar");

        // La oferta de un evento: el precio de cada localidad y sus pozos de aforo (ADR 0140 F3). La
        // llama el CMS al publicar, no un navegador, así que no lleva marca de la puerta ni cabeceras
        // suyas. Repetirla no duplica nada; ver OfertaDeEventos.
        app.MapPost("/v1/ofertas", async Task<Results<Ok<EventOfferResponse>, ProblemHttpResult>> (
            PublishEventOfferRequest req, HttpRequest http, OfertaDeEventos ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var oferta = new OfertaDeEvento(req.EventId, req.Currency, req.StartsAtUtc, req.Tiers?
                .Select(t => new LocalidadOfertada(t.Code, t.Price, t.MaxPerOrder, t.Capacity, t.Seats, t.SaleOpensUtc, t.SaleClosesUtc))
                .ToList());
            return (await ofertas.PublicarAsync(oferta, key, ct)).Map(EventOfferResponse.From).ToHttp();
        }).WithName("PublishEventOffer").ConLlaveDeIdempotencia();

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, «se rinde a los ocho intentos» sería «se abandona», y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/ticket-purchases/{id}/retry", async (string id, TicketingFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(TicketPurchaseResponse.From).ToHttp()).WithName("RetryTicketPurchase");

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió solo
        // existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", Ok<PageResponse<PendingCompensationResponse>> (int? offset, int? limit, TicketingFlow flow) =>
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
        }).WithName("ListCompensations");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();

    /// <summary>
    /// Quién compra, de <c>X-Synergos-Sujeto</c>: el rechazo si falta o no se lee; si no, nulo y el sujeto.
    /// </summary>
    /// <remarks>
    /// Sin sujeto no hay a quién atar la compra, así que no se sigue: un sujeto opcional dejaba leer,
    /// confirmar y cancelar lo de cualquiera (medido en el prototipo de la F3: sin cabecera, 200).
    /// </remarks>
    private static ProblemHttpResult? Sujeto(HttpRequest http, out Ref? sujeto)
    {
        if (!CabecerasDeLaPuerta.TryLeerSujeto(http, out sujeto))
        {
            return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(CodePrefix, CabecerasDeLaPuerta.Sujeto),
                "El sujeto no se puede leer: <kind>:<id>.").ToProblem();
        }
        return sujeto is null
            ? Rejection.Invalid(CabecerasDeLaPuerta.CodigoRequerido(CodePrefix, CabecerasDeLaPuerta.Sujeto),
                "Hace falta quién compra: lo pone la puerta del CMS.").ToProblem()
            : null;
    }

    /// <summary>
    /// La configuración de negocio, de <c>X-Synergos-Negocio</c>. Obligatoria al abrir: sin ella se
    /// cobraría sin la comisión que el carrito le muestra al comprador (ADR 0137).
    /// </summary>
    private static ProblemHttpResult? Negocio(HttpRequest http, out NegocioDeLaPuerta? negocio)
    {
        if (!CabecerasDeLaPuerta.TryLeerJson(http, CabecerasDeLaPuerta.Negocio, out negocio))
        {
            return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(CodePrefix, CabecerasDeLaPuerta.Negocio),
                "La configuración de negocio no se puede leer: base64url de {feePercent}.").ToProblem();
        }
        return negocio?.FeePercent is null
            ? Rejection.Invalid(CabecerasDeLaPuerta.CodigoRequerido(CodePrefix, CabecerasDeLaPuerta.Negocio),
                "Hace falta la comisión de servicio del sitio: la pone la puerta del CMS.").ToProblem()
            : null;
    }

    /// <summary>Lo que trae <c>X-Synergos-Negocio</c>: los campos de la sección del sitio que la operación declara.</summary>
    private sealed record NegocioDeLaPuerta(decimal? FeePercent);

    /// <summary>Lo que trae <c>X-Synergos-Contacto</c>, tal como lo escribe la puerta.</summary>
    private sealed record ContactoDeLaPuerta(string? Correo, string? Nombre, string? Enlace, string? Sitio);
}
