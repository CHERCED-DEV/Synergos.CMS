using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Pasa los <c>imports</c> de un import map PUBLICADO a URLs que el navegador resuelve contra el
/// CDN, escriba como las escriba el publicador. Es UNA pieza para los dos clientes del registry.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta</b> (#189). El CMS no sirve el import map desde su sitio: lo copia
/// DENTRO de la página, en un <c>&lt;script type="importmap"&gt;</c>. Y un mapa en línea resuelve
/// sus direcciones contra la página, no contra el fichero del que salió. Así que toda URL que no
/// lleve host propio apunta al ORIGEN DEL CMS, donde el runtime no está: 404, y nada hidrata.</para>
///
/// <para><b>Sólo se reubicaban las absolutas</b>, y el artefacto de producción no las escribe así.
/// <c>build:cdn</c> publica el runtime con <c>--base=/synergos</c> —a propósito: el mismo artefacto
/// sirve en <c>workers.dev</c>, en el dominio propio y en local— y el mapa sale con
/// <c>"/synergos/runtime/angular/…"</c>. Pasó desapercibido porque la CDN de la máquina del
/// arquitecto se publica sin <c>--base</c>, con el origen absoluto, que sí se reubicaba: el camino
/// que se probaba no era el que se despliega.</para>
///
/// <para><b>Las tres formas, y cada una con su regla</b>:</para>
/// <list type="bullet">
///   <item><b>Absoluta</b> (<c>https://host/x</c> o <c>//host/x</c>): se cambia el host de
///   publicación por la base pública y se conserva la ruta.</item>
///   <item><b>Relativa a la raíz</b> (<c>/x</c>): la raíz es la del CDN, no la de la página; se
///   antepone la base pública.</item>
///   <item><b>Relativa</b> (<c>./x</c>, <c>../x</c>, <c>x</c>): se resuelve contra el sitio del
///   propio mapa en el CDN —es lo que quiso decir quien la escribió— y se antepone la base. Antes se
///   dejaba tal cual con la nota «ya está servida desde donde toca», y no: en línea la resuelve la
///   página.</item>
/// </list>
/// <para>Lo que lleva otro esquema (<c>data:</c>, <c>blob:</c>) no tiene host que cambiar y se
/// conserva.</para>
///
/// <para><b>El orden de las comprobaciones no es cosmético.</b> En Linux,
/// <c>Uri.TryCreate("/x", UriKind.Absolute, …)</c> DEVUELVE <c>true</c> con esquema
/// <c>file</c>; en Windows, <c>false</c>. Preguntar primero por la absoluta dejaba las relativas a
/// la raíz sin reubicar sólo en CI. Por eso las dos formas con barra se reconocen por el texto,
/// antes de parsear.</para>
/// </remarks>
internal static class ReubicacionDelImportMap
{
    /// <summary>Un origen que no existe, sólo para que <see cref="Uri"/> resuelva rutas relativas.</summary>
    private static readonly Uri OrigenFicticio = new("https://cdn.invalid");

    /// <summary>
    /// Dónde vive el import map de un framework, relativo a la raíz del CDN. Lo usan los dos
    /// clientes para LEERLO, y esta pieza para resolver lo que el mapa escriba relativo a sí mismo.
    /// </summary>
    public static string RutaDelMapa(string bundlesNamespace, string framework, string slot)
        => $"/{bundlesNamespace}/runtime/{framework}/{slot}/import-map.json";

    /// <summary>Los <c>imports</c> de un mapa publicado, con cada URL reubicada.</summary>
    public static IReadOnlyDictionary<string, string> Reubicar(
        JsonElement imports, string publicBaseUrl, string rutaDelMapa)
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in imports.EnumerateObject())
        {
            mapa[prop.Name] = Reubicar(prop.Value.GetString() ?? string.Empty, publicBaseUrl, rutaDelMapa);
        }

        return mapa;
    }

    /// <summary>Una URL del mapa, reubicada a la base pública del CDN.</summary>
    public static string Reubicar(string url, string publicBaseUrl, string rutaDelMapa)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        var baseLimpia = (publicBaseUrl ?? string.Empty).TrimEnd('/');

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return baseLimpia + new Uri(OrigenFicticio.Scheme + ":" + url).PathAndQuery;
        }

        if (url.StartsWith('/'))
        {
            return baseLimpia + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluta))
        {
            return absoluta.Scheme == Uri.UriSchemeHttp || absoluta.Scheme == Uri.UriSchemeHttps
                ? baseLimpia + absoluta.PathAndQuery
                : url;
        }

        return baseLimpia + new Uri(new Uri(OrigenFicticio, rutaDelMapa), url).PathAndQuery;
    }
}
