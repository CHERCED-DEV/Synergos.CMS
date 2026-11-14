namespace Synergos.CMS.Interfaces;

/// <summary>
/// La consulta en el orquestador de Salud (003-salud-cita, I3): la agenda del día de un profesional (P9), la llegada de
/// cada cita (P10) y el sello de una nota (P6). Nada clínico cruza: el paciente sale como el sujeto con que agendó y la
/// nota viaja como su huella. Quién puede pedir cada cosa lo decide la guardia del CMS antes de llamar aquí.
/// </summary>
public interface IConsultaDeSalud
{
    /// <summary>Las citas de un profesional entre dos instantes. <c>null</c> si el orquestador no contestó.</summary>
    Task<IReadOnlyList<CitaDelDia>?> AgendaAsync(string profesional, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancellationToken);

    /// <summary>Marca un paso de la llegada (<c>llegar</c>, <c>pasar</c>, <c>atender</c>, <c>no-vino</c>).</summary>
    Task<ResultadoDeLaConsulta> MarcarLlegadaAsync(string reservaId, string paso, Guid quien, CancellationToken cancellationToken);

    /// <summary>Sella la huella de una nota con la llave del consultorio.</summary>
    Task<ResultadoDeLaConsulta> SellarAsync(string llave, string huella, CancellationToken cancellationToken);

    /// <summary>Si el sello es el de esa huella con esa llave. El valor es la llave que cuadró.</summary>
    Task<ResultadoDeLaConsulta> ComprobarSelloAsync(string llave, string huella, string sello, CancellationToken cancellationToken);

    /// <summary>
    /// El estado de cuenta de un paciente (I7 P17): lo cobrado y lo devuelto de cada cita suya, leído en Payments. <c>null</c>
    /// si el orquestador no contestó o no pudo leer algún pago: una cuenta a medias no se muestra.
    /// </summary>
    Task<EstadoDeCuenta?> EstadoDeCuentaAsync(string pacienteKind, string pacienteId, CancellationToken cancellationToken);
}

/// <summary>Una cita cobrada en la cuenta del paciente, con lo que dice Payments de su cobro.</summary>
public sealed record LineaDeLaCuenta(
    string CitaId, string Numero, DateTimeOffset? Inicio, decimal Cobrado, decimal Devuelto, string Moneda);

/// <summary>La cuenta del paciente, una línea por cita cobrada.</summary>
public sealed record EstadoDeCuenta(IReadOnlyList<LineaDeLaCuenta> Lineas);

/// <summary>Una cita del día como la da el orquestador.</summary>
public sealed record CitaDelDia(
    string ReservaId, DateTimeOffset Inicio, DateTimeOffset Fin, string PacienteKind, string PacienteId, string Llegada);

/// <summary>Lo que contestó el orquestador: el valor, o el no con su código, o que no contestó.</summary>
/// <param name="Valor">El estado de la llegada; el sello; o la llave que cuadró.</param>
/// <param name="Extra">La llave con que se selló, cuando se sella.</param>
public sealed record ResultadoDeLaConsulta(EstadoDeLaConsulta Estado, string? Valor = null, string? Extra = null, string? Codigo = null)
{
    public static ResultadoDeLaConsulta SinRespuesta { get; } = new(EstadoDeLaConsulta.SinRespuesta);
}

public enum EstadoDeLaConsulta
{
    Hecho,
    Rechazado,
    SinRespuesta,
}
