namespace Synergos.Shared;

/// <summary>
/// Normaliza los tres parámetros que toda consulta por rango repite: <c>from</c>, <c>to</c> y
/// <c>limit</c>.
/// </summary>
/// <remarks>
/// <para><b>Por qué no se dejan crudos.</b> Los tres tienen la misma trampa: si llegan nulos y
/// cada endpoint improvisa su default, dos rutas del mismo servicio contestan ventanas distintas
/// y el tablero miente sin decirlo. Y un <c>limit</c> sin tope convierte una petición en una
/// descarga del almacén entero — que es denegación de servicio con formulario.</para>
///
/// <para>Salió de <c>Synergos.Api.Sessions</c>, donde estos tres <c>static</c> vivían sueltos al
/// final del <c>Program.cs</c>. Es fontanería de host y no menciona ningún sustantivo del
/// negocio: una ventana de tiempo no es un pedido ni un paciente.</para>
/// </remarks>
public static class QueryWindow
{
    /// <summary>Días que se asumen cuando no viene <c>from</c>.</summary>
    public const int DefaultDays = 7;

    /// <summary>Filas por defecto cuando no viene <c>limit</c>.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Tope duro de filas. Ninguna petición puede pedir más, venga como venga.</summary>
    public const int MaxLimit = 500;

    /// <summary>Inicio de la ventana en UTC. Sin valor, hace <see cref="DefaultDays"/> días.</summary>
    public static DateTime From(DateTime? value, DateTime nowUtc)
        => value?.ToUniversalTime() ?? nowUtc.AddDays(-DefaultDays);

    /// <inheritdoc cref="From(DateTime?, DateTime)"/>
    public static DateTime From(DateTime? value) => From(value, DateTime.UtcNow);

    /// <summary>Fin de la ventana en UTC. Sin valor, ahora.</summary>
    public static DateTime To(DateTime? value, DateTime nowUtc)
        => value?.ToUniversalTime() ?? nowUtc;

    /// <inheritdoc cref="To(DateTime?, DateTime)"/>
    public static DateTime To(DateTime? value) => To(value, DateTime.UtcNow);

    /// <summary>Filas pedidas, acotadas a <c>[1, <see cref="MaxLimit"/>]</c>.</summary>
    public static int Limit(int? value) => Math.Clamp(value ?? DefaultLimit, 1, MaxLimit);
}
