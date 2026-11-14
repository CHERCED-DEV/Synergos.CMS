namespace Synergos.Bff.Salud.Contracts;

// El estado de cuenta del paciente (003-salud-cita, I7 P17): sólo cifras de Payments y el número de cada cita; nada
// clínico (el profesional y el motivo no viajan).

/// <summary>Una cita cobrada: su número, cuándo es, y lo que Payments dice que se cobró y se devolvió.</summary>
public sealed record LineaDeCuenta(
    string CitaId, string Numero, DateTimeOffset? Inicio, decimal Cobrado, decimal Devuelto, string Moneda);

public sealed record EstadoDeCuentaResponse(IReadOnlyList<LineaDeCuenta> Lineas);
