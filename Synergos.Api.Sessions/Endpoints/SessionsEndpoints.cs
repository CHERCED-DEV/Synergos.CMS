using Synergos.Api.Sessions.Contracts;
using Synergos.Api.Sessions.Domain;
using Synergos.Api.Sessions.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Sessions.Endpoints;

/// <summary>
/// El ruteo de la capacidad de señales de sesión.
/// </summary>
/// <remarks>
/// <para><b>La ingesta NO exige <c>Idempotency-Key</c>, y es una forma del molde, no una
/// excepción a él</b> (doc 08 §4.3, sexta forma). La regla de idempotencia existe porque cuando
/// una API no contesta el llamador no sabe si se ejecutó; acá el llamador <b>no reintenta por
/// diseño</b> (ADR 0130) y el dato es agregado, no individual: un evento duplicado corre un
/// conteo en uno, mientras que un cobro duplicado cobra dos veces. El criterio para aplicar esta
/// forma es exactamente ese, y está escrito para que no se estire.</para>
///
/// <para>Y por eso responde <b>202 y no 201</b>: un query vacío o un fallo de disco no son error
/// del llamador —el CMS ya sirvió su búsqueda— y devolverle un 4xx o un 5xx solo lograría que
/// reintentara algo que no debe reintentar.</para>
/// </remarks>
public static class SessionsEndpoints
{
    public static IEndpointRouteBuilder MapSessionsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/search-events", (RecordSearchEventRequest req, SearchEventStore store, TimeProvider clock) =>
        {
            // El origen NO decide el reloj. Si lo hiciera, un CMS con la hora corrida escribiría
            // en el fichero equivocado y las ventanas del tablero mentirían.
            var evt = new SearchEvent(req.Query ?? string.Empty, req.ResultCount, req.ElapsedMs, clock.GetUtcNow().UtcDateTime);
            return Results.Accepted(value: new IngestAcceptedResponse(store.Record(evt)));
        });

        app.MapGet("/v1/search/top", (DateTime? from, DateTime? to, int? limit, SearchEventStore store, TimeProvider clock) =>
            Consultar(from, to, limit, clock, w => store.TopQueries(w, QueryWindow.Limit(limit))));

        app.MapGet("/v1/search/no-results", (DateTime? from, DateTime? to, int? limit, SearchEventStore store, TimeProvider clock) =>
            Consultar(from, to, limit, clock, w => store.TopNoResultQueries(w, QueryWindow.Limit(limit))));

        return app;
    }

    /// <summary>
    /// Arma la ventana y consulta. Rechaza el rango invertido en vez de servir vacío.
    /// </summary>
    /// <remarks>
    /// Antes, un <c>to</c> anterior al <c>from</c> devolvía una lista vacía con 200 — y "no hay
    /// datos" es exactamente lo que NO había que contestarle a quien mandó el rango al revés:
    /// se lee como un hallazgo del negocio y se investiga como tal. El tipo
    /// <see cref="TimeWindow"/> ya no deja construirla, así que el borde solo tiene que traducir
    /// esa imposibilidad a un rechazo.
    /// </remarks>
    private static IResult Consultar(
        DateTime? from, DateTime? to, int? limit, TimeProvider clock,
        Func<TimeWindow, IReadOnlyList<QueryStat>> consulta)
    {
        var ahora = clock.GetUtcNow().UtcDateTime;
        var desde = QueryWindow.From(from, ahora);
        var hasta = QueryWindow.To(to, ahora);

        if (hasta <= desde)
        {
            return Rejection.Invalid("sessions.bad_window",
                $"La ventana no avanza: {desde:O} → {hasta:O}.").ToProblem();
        }

        return Results.Ok(consulta(TimeWindow.Of(desde, hasta)).Select(QueryStatResponse.From).ToList());
    }
}
