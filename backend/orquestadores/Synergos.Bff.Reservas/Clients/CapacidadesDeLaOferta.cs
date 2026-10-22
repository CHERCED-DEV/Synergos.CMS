using Synergos.Bff.Core;
using Synergos.Bff.Reservas.Contracts;
using Synergos.Core;

namespace Synergos.Bff.Reservas.Clients;

/// <summary>
/// Lo que la OFERTA necesita de la agenda y de precios: registrar o revisar el recurso de un servicio y fijar su precio.
/// </summary>
/// <remarks>
/// <para><b>Sólo la oferta</b>: la reserva usa los adaptadores comunes del núcleo (<c>PuertosHttp</c>) y no
/// pasa por aquí. Esto es código del vertical que la pieza genérica de la oferta (P12, NB-8) retirará, y lo
/// censa <c>ContratoConsumidorReservasTests</c> contra el contrato comiteado de cada capacidad.</para>
/// </remarks>
public sealed class CapacidadesDeLaOferta(IHttpClientFactory clientes) : CapabilityClients(clientes)
{
    public const string Booking = "booking";
    public const string Pricing = "pricing";

    /// <summary>El recurso de la agenda de <paramref name="sujeto"/>, si ya existe.</summary>
    /// <remarks>
    /// Por <c>/v1/resources/por-sujeto</c>, la ruta publicada: la combinada <c>/v1/resources?subject…</c> devuelve
    /// dos formas según la consulta y queda fuera del contrato (núcleo N1).
    /// </remarks>
    public Task<Result<RecursoDto>> BuscarRecursoAsync(Ref sujeto, CancellationToken ct)
        => Get<RecursoDto>(Booking,
            $"v1/resources/por-sujeto?subjectKind={Uri.EscapeDataString(sujeto.Kind)}&subjectId={Uri.EscapeDataString(sujeto.Id)}", ct);

    /// <summary>
    /// Registra el recurso de un servicio con su horario, sus cupos, su plazo de aviso y su franja (<c>slotMinutes</c>,
    /// NB-1): con ella la agenda rechaza una ventana de otro largo o fuera de la grilla del horario.
    /// </summary>
    public Task<Result<RecursoDto>> RegistrarRecursoAsync(
        Ref sujeto, int cupos, string zona, IReadOnlyList<BloqueDeHorarioDto> horario, int avisoMinutos, int franjaMinutos,
        IdempotencyKey llave, CancellationToken ct)
        => Post<RecursoDto>(Booking, "v1/resources", new
        {
            subjectKind = sujeto.Kind,
            subjectId = sujeto.Id,
            capacity = cupos,
            timeZoneId = zona,
            opening = Apertura(horario),
            cancellationNoticeMinutes = avisoMinutos,
            slotMinutes = franjaMinutos,
        }, llave, ct);

    /// <summary>
    /// Revisa el recurso ya registrado (NB-7): cupos, horario, plazo y franja, ENTEROS. La agenda no cancela nada y
    /// devuelve las reservas vigentes que ya no cabrían.
    /// </summary>
    public Task<Result<RevisionDto>> RevisarRecursoAsync(
        string recursoId, int cupos, IReadOnlyList<BloqueDeHorarioDto> horario, int avisoMinutos, int franjaMinutos,
        IdempotencyKey llave, CancellationToken ct)
        => Post<RevisionDto>(Booking, $"v1/resources/{Uri.EscapeDataString(recursoId)}/revise", new
        {
            capacity = cupos,
            opening = Apertura(horario),
            cancellationNoticeMinutes = avisoMinutos,
            slotMinutes = franjaMinutos,
        }, llave, ct);

    /// <summary>Fija el precio de un servicio; con <paramref name="hasta"/> lo deja de valer desde ese instante.</summary>
    public Task<Result<PrecioDto>> FijarPrecioAsync(Ref sujeto, Money precio, DateTimeOffset? hasta, IdempotencyKey llave, CancellationToken ct)
        => Post<PrecioDto>(Pricing, "v1/prices", new
        {
            subjectKind = sujeto.Kind,
            subjectId = sujeto.Id,
            amount = new { amount = precio.Amount, currency = precio.Currency },
            taxRateBasisPoints = 0,
            validTo = hasta,
        }, llave, ct);

    private static List<object> Apertura(IReadOnlyList<BloqueDeHorarioDto> horario)
        => horario.Select(b => (object)new { day = b.Dia, from = b.Desde, to = b.Hasta }).ToList();
}

/// <summary>Un recurso de la agenda, en lo que la oferta lee. <c>SlotMinutes</c> nulo: registrado sin franja.</summary>
public sealed record RecursoDto(
    string Id, int Capacity, string TimeZoneId, IReadOnlyList<FranjaDeAperturaDto> Opening, int CancellationNoticeMinutes,
    int? SlotMinutes = null);

/// <summary>El recurso revisado y las reservas vigentes que ya no cabrían (ninguna se canceló).</summary>
public sealed record RevisionDto(RecursoDto Resource, IReadOnlyList<ReservaFueraDto> Outside);

/// <summary>Una reserva vigente que con el recurso revisado ya no cabría, en lo que la oferta lee.</summary>
public sealed record ReservaFueraDto(string ReservationId, string ReasonCode);

/// <summary>Un bloque de apertura de la agenda: el contrato no promete ninguna de sus tres partes.</summary>
public sealed record FranjaDeAperturaDto(string? Day, string? From, string? To);

/// <summary>Un precio publicado, en lo que la oferta lee.</summary>
public sealed record PrecioDto(string Id, DateTimeOffset? ValidTo);
