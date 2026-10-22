namespace Synergos.Bff.Reservas.Contracts;

// Lo que cruza el cable de la oferta (011-booking-reserva, contracts/oferta.md). Todo anulable con `= null`
// en lo que llega (molde F2 de la ADR 0140): el dominio dice qué falta, con su código.

/// <summary>Un bloque del horario semanal: <c>{ dia, desde, hasta }</c> («Monday», «09:00», «13:00»).</summary>
public sealed record BloqueDeHorarioDto(string? Dia = null, string? Desde = null, string? Hasta = null);

/// <summary>Publicar la oferta de un servicio reservable (la llama el CMS al publicar su ficha).</summary>
/// <param name="Servicio">La clave estable de la ficha del servicio.</param>
/// <param name="Nombre">El nombre, sólo para el rastro y los registros.</param>
/// <param name="DuracionMinutos">Lo que dura una franja del servicio.</param>
/// <param name="Horario">El horario semanal en el que se atiende.</param>
/// <param name="Cupos">Cuántas reservas admite la misma franja.</param>
/// <param name="PlazoCancelacionHoras">Hasta cuántas horas antes se cancela sin costo.</param>
/// <param name="Precio">Lo que vale una franja.</param>
/// <param name="Moneda">La moneda del precio (ISO 4217).</param>
/// <param name="Zona">La zona horaria del horario (IANA).</param>
public sealed record PublicarOfertaRequest(
    string? Servicio = null,
    string? Nombre = null,
    int? DuracionMinutos = null,
    IReadOnlyList<BloqueDeHorarioDto>? Horario = null,
    int? Cupos = null,
    int? PlazoCancelacionHoras = null,
    decimal? Precio = null,
    string? Moneda = null,
    string? Zona = null);

/// <summary>Cómo quedó publicada la oferta de un servicio.</summary>
/// <param name="ReservasFuera">Cuántas reservas vigentes ya no caben con el horario o los cupos revisados: la agenda no
/// las canceló, y el CMS se lo dice al editor.</param>
public sealed record OfertaDeServicioResponse(
    string Servicio,
    int DuracionMinutos,
    int Cupos,
    decimal Precio,
    string Moneda,
    bool Retirada,
    int ReservasFuera);
