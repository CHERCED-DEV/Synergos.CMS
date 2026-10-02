using System.Globalization;
using Synergos.CMS.Interfaces.SynHost;

namespace Synergos.CMS.Web.Services.Listados;

/// <summary>
/// Una fuente de filas para un listado (<c>&lt;synergos-data-grid&gt;</c>): el editor la elige por
/// su <see cref="Clave"/> y el resolver la consulta en el servidor (#196, tanda D).
/// </summary>
/// <remarks>
/// Es la pieza que se enchufa: una fuente nueva es una clase más que se registra, no un
/// <c>if</c> en el resolver. Cada una lee de la MISMA fuente que su app (el catálogo de cursos, de
/// eventos, de inmuebles) o del árbol de contenido, y arma filas ya formateadas para la cultura de
/// la petición: el elemento no sabe de monedas ni de fechas.
/// </remarks>
public interface IFuenteDeListado
{
    /// <summary>La clave con que el editor la elige (el valor del desplegable <c>fuente</c>).</summary>
    string Clave { get; }

    /// <summary>Las filas para esta petición, filtradas por su consulta si la trae.</summary>
    IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion);
}

/// <summary>Lo que una fuente necesita saber de la petición.</summary>
/// <param name="Consulta">El <c>?q</c> de la página, o <c>null</c> sin búsqueda.</param>
/// <param name="Cultura">La cultura en que se formatean precios y fechas.</param>
/// <param name="Zona">La zona horaria del sitio, en la que se dice el día de una fecha.</param>
/// <param name="Rotulo">El rótulo de un dato, del diccionario (clave, texto de respaldo).</param>
public sealed record PeticionDelListado(
    string? Consulta,
    CultureInfo Cultura,
    TimeZoneInfo Zona,
    Func<string, string, string> Rotulo);

/// <summary>Cómo se escriben los datos de una fila: una sola regla para todas las fuentes.</summary>
public static class FormatoDelListado
{
    /// <summary>
    /// Un precio con su moneda. En la moneda de la cultura, con su símbolo y sin decimales
    /// (<c>$ 480.000</c> en es-CO); en otra, con su código ISO delante, para no confundirla.
    /// </summary>
    public static string Precio(decimal monto, string moneda, CultureInfo cultura)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        var codigo = moneda?.Trim().ToUpperInvariant() ?? string.Empty;
        var deLaCultura = MonedaDe(cultura);
        return codigo.Length == 0 || string.Equals(codigo, deLaCultura, StringComparison.Ordinal)
            ? monto.ToString("C0", cultura)
            : $"{codigo} {monto.ToString("N0", cultura)}";
    }

    /// <summary>
    /// El día de un instante en la zona del sitio, en la forma corta de la cultura. Los catálogos
    /// guardan la hora en UTC: un evento a las 00:00 UTC del 5 de octubre es el 4 en Bogotá.
    /// </summary>
    public static string Fecha(DateTimeOffset instante, CultureInfo cultura, TimeZoneInfo zona)
        => TimeZoneInfo.ConvertTime(instante, zona).ToString("d MMM yyyy", cultura);

    /// <summary>
    /// Un valor de catálogo como etiqueta: con su primera letra en mayúscula («apartamento» →
    /// «Apartamento»). Los catálogos guardan el tipo como clave en minúscula.
    /// </summary>
    public static string? Etiqueta(string? valor, CultureInfo cultura)
    {
        ArgumentNullException.ThrowIfNull(cultura);
        return string.IsNullOrWhiteSpace(valor)
            ? null
            : cultura.TextInfo.ToUpper(valor.Trim()[0]) + valor.Trim()[1..];
    }

    /// <summary>Una duración en horas y minutos («8 h», «45 min», «1 h 30 min»).</summary>
    public static string Duracion(int minutos)
    {
        var (h, m) = (minutos / 60, minutos % 60);
        return (h, m) switch
        {
            (0, _) => $"{m} min",
            (_, 0) => $"{h} h",
            _ => $"{h} h {m} min",
        };
    }

    /// <summary>
    /// ¿La fila responde a la consulta? Por cualquiera de sus textos, sin distinguir mayúsculas
    /// ni tildes («educacion» encuentra «Educación»).
    /// </summary>
    public static bool Responde(string? consulta, params string?[] textos)
    {
        if (string.IsNullOrWhiteSpace(consulta))
        {
            return true;
        }

        var aguja = SinTildes(consulta.Trim());
        return textos.Any(t => t is not null && SinTildes(t).Contains(aguja, StringComparison.OrdinalIgnoreCase));
    }

    private static string SinTildes(string texto)
        => new(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

    private static string MonedaDe(CultureInfo cultura)
    {
        try
        {
            return new RegionInfo(cultura.Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
