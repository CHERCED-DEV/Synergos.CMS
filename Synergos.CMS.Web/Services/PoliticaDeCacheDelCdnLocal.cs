using System.Text.RegularExpressions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Cuánto puede cachear un navegador lo que sirve el montaje local del CDN (ADR 0089, ADR 0140).
/// </summary>
/// <remarks>
/// <para><b>«immutable» es una promesa sobre el CONTENIDO, y sólo se hace donde algo lo prueba.</b>
/// Prometerla en una ruta que se mueve significa publicar algo nuevo y que el navegador que ya
/// tenía la ruta no lo pida durante un año: no falla, no avisa y no se arregla purgando nada,
/// porque la caché está en el navegador de cada visitante.</para>
///
/// <para><b>El runtime compartido es la excepción a «versión exacta = inmutable».</b> Su carpeta
/// (<c>runtime/angular/21.1.6/</c>) lleva la versión del FRAMEWORK, no la de su contenido:
/// <c>publish:runtime</c> la reescribe cada vez que cambia <c>libs/shared</c>, y con la regla de
/// antes salía <c>immutable</c> por tener forma de versión. Bajo <c>runtime</c> sólo es inmutable
/// el fichero que lleva la huella de sus bytes en el nombre
/// (<c>sg-shared.18a9decbae7fffb0.js</c>); el plano y su <c>import-map.json</c> se revalidan.</para>
///
/// <para>El orden de las reglas es la regla: (a) un puntero que se mueve (<c>latest</c>,
/// <c>vN</c>) se revalida esté donde esté; (b) bajo la carpeta reservada <c>runtime</c>, inmutable
/// sólo con huella en el nombre; (c) todo lo demás, inmutable como siempre.</para>
///
/// <para>Es gemela de <c>tools/lib/cdn-cache-policy.mjs</c> del UI (la política del Worker del
/// CDN): los dos specs llevan la misma tabla de casos del runtime.</para>
/// </remarks>
internal static class PoliticaDeCacheDelCdnLocal
{
    /// <summary>Lo que se revalida en cada petición (el servidor contesta 304 si no cambió).</summary>
    public const string Revalidar = "public, no-cache, must-revalidate";

    /// <summary>La carpeta del CDN que no es un elemento: el runtime compartido.</summary>
    public const string CarpetaDelRuntime = "runtime";

    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);

    /// <summary>Un alias mayor (<c>v0</c>, <c>v12</c>): sigue a la última compatible.</summary>
    private static readonly Regex AliasMayor = new(
        @"^v\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limite);

    /// <summary>
    /// Un fichero del runtime con su huella: <c>&lt;stem&gt;.&lt;16 hex en minúsculas&gt;.js</c>. Es
    /// el mismo patrón que <c>HUELLA_EN_NOMBRE</c> del UI; sin él no se promete nada.
    /// </summary>
    private static readonly Regex HuellaEnElNombre = new(
        @"^[a-z0-9-]+\.[0-9a-f]{16}\.m?js$", RegexOptions.CultureInvariant, Limite);

    /// <summary>La cabecera <c>Cache-Control</c> que le toca a una ruta del montaje local.</summary>
    /// <param name="path">La ruta de la petición, con el prefijo del montaje.</param>
    /// <param name="maxAge">Los segundos de la caché larga (<c>CacheControlMaxAgeSeconds</c>).</param>
    public static string CacheControl(string path, int maxAge)
    {
        var segmentos = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segmentos.Any(s => s.Equals("latest", StringComparison.OrdinalIgnoreCase) || AliasMayor.IsMatch(s)))
        {
            return Revalidar;
        }

        if (segmentos.Any(s => s.Equals(CarpetaDelRuntime, StringComparison.OrdinalIgnoreCase)))
        {
            return segmentos.Length > 0 && HuellaEnElNombre.IsMatch(segmentos[^1])
                ? Inmutable(maxAge)
                : Revalidar;
        }

        return Inmutable(maxAge);
    }

    private static string Inmutable(int maxAge) => $"public, max-age={maxAge}, immutable";
}
