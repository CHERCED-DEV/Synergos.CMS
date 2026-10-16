using Synergos.Bff.Core;
using Synergos.Bff.Reservas.Contracts;
using Synergos.Core;

namespace Synergos.Bff.Reservas.Clients;

/// <summary>
/// Lo que la OFERTA necesita de la agenda y de precios: registrar el recurso de un servicio y fijar su precio.
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

    /// <summary>Registra el recurso de un servicio con su horario, sus cupos y su plazo de aviso.</summary>
    public Task<Result<RecursoDto>> RegistrarRecursoAsync(
        Ref sujeto, int cupos, string zona, IReadOnlyList<BloqueDeHorarioDto> horario, int avisoMinutos,
        IdempotencyKey llave, CancellationToken ct)
        => Post<RecursoDto>(Booking, "v1/resources", new
        {
            subjectKind = sujeto.Kind,
            subjectId = sujeto.Id,
            capacity = cupos,
            timeZoneId = zona,
            opening = horario.Select(b => new { day = b.Dia, from = b.Desde, to = b.Hasta }).ToList(),
            cancellationNoticeMinutes = avisoMinutos,
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
}

/// <summary>Un recurso de la agenda, en lo que la oferta lee.</summary>
public sealed record RecursoDto(
    string Id, int Capacity, string TimeZoneId, IReadOnlyList<FranjaDeAperturaDto> Opening, int CancellationNoticeMinutes);

/// <summary>Un bloque de apertura de la agenda: el contrato no promete ninguna de sus tres partes.</summary>
public sealed record FranjaDeAperturaDto(string? Day, string? From, string? To);

/// <summary>Un precio publicado, en lo que la oferta lee.</summary>
public sealed record PrecioDto(string Id, DateTimeOffset? ValidTo);
