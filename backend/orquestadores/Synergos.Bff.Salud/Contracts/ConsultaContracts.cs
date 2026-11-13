namespace Synergos.Bff.Salud.Contracts;

// La consulta (003-salud-cita, I3): la agenda del día de un profesional, la llegada de cada cita y el sello de la nota.
// Sin datos clínicos: el paciente sale como el sujeto con que agendó; su nombre lo pone el CMS desde la historia, con la
// guardia. La nota tampoco viaja: el CMS manda su huella.

/// <summary>Una cita de la agenda del día, con en qué va su llegada.</summary>
/// <param name="Llegada"><c>agendada</c> mientras nadie la marca; después, el estado de su instancia en Workflow.</param>
public sealed record CitaDeLaAgenda(
    string ReservaId, DateTimeOffset Inicio, DateTimeOffset Fin, string PacienteKind, string PacienteId, string Llegada);

public sealed record AgendaDelDiaResponse(IReadOnlyList<CitaDeLaAgenda> Citas);

/// <summary>Marcar un paso de la llegada: <c>llegar</c>, <c>pasar</c>, <c>atender</c> o <c>no-vino</c>.</summary>
public sealed record PasoDeLlegadaRequest(string? Paso = null, string? ActorKind = null, string? ActorId = null);

public sealed record LlegadaResponse(string ReservaId, string Llegada);

/// <summary>Sellar la huella de una nota con la llave del consultorio.</summary>
public sealed record SellarRequest(string? Llave = null, string? Contenido = null);

public sealed record SelloResponse(string Sello, string LlaveId);

public sealed record ComprobarSelloRequest(string? Llave = null, string? Contenido = null, string? Sello = null);

public sealed record SelloComprobadoResponse(string LlaveId);
