namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Una lectura genérica que implementa un vertical (014-despiece-tienda, I4): la forma es neutra y la fuente es del
/// vertical. Las rutas <c>api/lecturas/{vertical}/…</c> eligen la implementación por <see cref="Vertical"/>.
/// </summary>
public interface ILecturaDeUnVertical
{
    /// <summary>La clave del vertical en la ruta (<c>tienda</c>, <c>eventos</c>…), en minúsculas.</summary>
    string Vertical { get; }
}

/// <summary>Cómo se elige, entre las lecturas registradas, la del vertical que pide la ruta.</summary>
public static class LecturasDeLosVerticales
{
    /// <summary>La lectura de <paramref name="vertical"/>, o <c>null</c> si ningún vertical la declara.</summary>
    public static T? DelVertical<T>(this IEnumerable<T> lecturas, string? vertical)
        where T : class, ILecturaDeUnVertical
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        return string.IsNullOrWhiteSpace(vertical)
            ? null
            : lecturas.FirstOrDefault(l => string.Equals(l.Vertical, vertical.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Un importe con su moneda y como se lee en el sitio (es-CO).</summary>
public sealed record Precio(decimal Monto, string Moneda, string Texto);

/// <summary>El promedio de las calificaciones y cuántas lo forman.</summary>
public sealed record Calificacion(double Promedio, int Cantidad);

/// <summary>
/// Cómo terminó una lectura de algo que se pide por su referencia (un pedido): leída, inexistente o ajena a quien lee.
/// </summary>
public enum ResultadoDeLaLectura
{
    /// <summary>Se leyó.</summary>
    Leida = 0,

    /// <summary>No existe.</summary>
    NoExiste = 1,

    /// <summary>Existe y es de otra persona.</summary>
    Ajena = 2,
}

/// <summary>Lo leído por referencia, o por qué no se leyó.</summary>
public sealed record LecturaPorReferencia<T>(ResultadoDeLaLectura Resultado, T? Valor)
    where T : class;

/// <summary>Cómo se arma una <see cref="LecturaPorReferencia{T}"/>.</summary>
public static class LecturaPorReferencia
{
    /// <summary>Lo leído.</summary>
    public static LecturaPorReferencia<T> Leida<T>(T valor)
        where T : class
        => new(ResultadoDeLaLectura.Leida, valor);

    /// <summary>No existe.</summary>
    public static LecturaPorReferencia<T> NoExiste<T>()
        where T : class
        => new(ResultadoDeLaLectura.NoExiste, null);

    /// <summary>Es de otra persona.</summary>
    public static LecturaPorReferencia<T> Ajena<T>()
        where T : class
        => new(ResultadoDeLaLectura.Ajena, null);
}
