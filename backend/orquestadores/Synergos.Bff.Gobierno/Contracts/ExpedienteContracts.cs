namespace Synergos.Bff.Gobierno.Contracts;

// Lo que cruza el cable de la lectura del expediente (009-gobierno-tramite T040, data-model §2): la instancia del proceso
// tal como la guarda la capacidad de procesos, que es la fuente de verdad.

/// <summary>Un paso de la historia del expediente: qué transición, de dónde a dónde, quién actuó, con qué nota y cuándo.</summary>
/// <param name="PorKind">La clase del sujeto que actuó: el funcionario o el ciudadano de la puerta (su seudónimo).</param>
public sealed record PasoDelExpedienteResponse(
    string Transicion, string Desde, string Hacia, string PorKind, string PorId, string? Nota, DateTimeOffset En);

/// <summary>El expediente de una radicación: el proceso que sigue, dónde está y cómo llegó ahí.</summary>
/// <param name="Radicacion">La radicación: la identidad del expediente por la puerta.</param>
/// <param name="Expediente">La instancia en la capacidad de procesos.</param>
/// <param name="Proceso">La clave de la definición con que nació.</param>
public sealed record ExpedienteDeGobiernoResponse(
    string Radicacion, string Expediente, string Proceso, string Estado, IReadOnlyList<PasoDelExpedienteResponse> Historia);
