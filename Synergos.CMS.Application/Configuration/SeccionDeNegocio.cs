namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Una sección de configuración de negocio que se valida al arrancar (ADR 0137).
/// </summary>
public interface ISeccionDeNegocio
{
    /// <summary>
    /// Lo que no sirve de los valores que rigen en los valores base y en cada sitio, una línea por
    /// problema. Vacío si todo sirve.
    /// </summary>
    IReadOnlyList<string> Problemas();
}

/// <summary>Una sección de negocio que da, para un sitio, los valores que rigen.</summary>
/// <typeparam name="TNegocio">Los valores ya fusionados.</typeparam>
public interface ISeccionDeNegocio<out TNegocio> : ISeccionDeNegocio
    where TNegocio : class
{
    /// <summary>Los valores que rigen en <paramref name="sitio"/>; sin sitio, los base.</summary>
    TNegocio Para(Guid? sitio);
}

/// <summary>
/// La forma de toda sección <c>Synergos:Features:&lt;X&gt;</c> (ADR 0137): los valores base en la
/// sección, y lo que cambia cada sitio en <see cref="Sitios"/>, nombrado por la <c>Key</c> de su
/// siteRoot.
/// </summary>
/// <typeparam name="TSitio">Lo que un sitio puede cambiar: las mismas claves, todas opcionales.</typeparam>
/// <typeparam name="TNegocio">Los valores ya fusionados, que es lo que leen el resolver y los motores.</typeparam>
/// <remarks>
/// <para><b>Un sitio cambia claves sueltas, no la sección.</b> La <c>Key</c> del siteRoot es la que
/// viaja en uSync y no cambia al renombrar el sitio; lo que un sitio no cambia lo hereda de los
/// valores base (<see cref="Fusionar"/>).</para>
/// <para><b>La validación es parte de la forma</b>, no un extra de cada funcionalidad: una clave que
/// nadie lee la caza el validador de Web; una <c>Key</c> que no es GUID y un valor fuera de rango, en
/// cualquier sitio, los caza <see cref="Problemas"/>.</para>
/// </remarks>
public abstract class SeccionDeNegocio<TSitio, TNegocio> : ISeccionDeNegocio<TNegocio>
    where TSitio : class
    where TNegocio : class
{
    /// <summary>Lo que cambia cada sitio, por la <c>Key</c> de su siteRoot.</summary>
    public Dictionary<string, TSitio> Sitios { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public TNegocio Para(Guid? sitio) => Fusionar(sitio is { } key ? DelSitio(key) : null);

    /// <inheritdoc />
    public IReadOnlyList<string> Problemas()
    {
        var problemas = ProblemasDe(Para(null)).Select(p => $"{p} (en los valores base)").ToList();

        foreach (var clave in Sitios.Keys)
        {
            if (!Guid.TryParse(clave, out var sitio))
            {
                problemas.Add($"Sitios:{clave}: un sitio se nombra por la Key (GUID) de su siteRoot.");
                continue;
            }

            problemas.AddRange(ProblemasDe(Para(sitio)).Select(p => $"{p} (en el sitio {clave})"));
        }

        return problemas;
    }

    /// <summary>
    /// Los valores base con lo que cambia el sitio encima, clave por clave. <paramref name="propio"/>
    /// es nulo cuando no hay sitio o el sitio no cambia nada.
    /// </summary>
    protected abstract TNegocio Fusionar(TSitio? propio);

    /// <summary>Lo que no sirve de <paramref name="negocio"/>, una línea por problema.</summary>
    protected abstract IEnumerable<string> ProblemasDe(TNegocio negocio);

    /// <summary>
    /// Lo que cambia <paramref name="sitio"/>. La clave se compara como GUID y no como texto: con
    /// llaves o en mayúsculas sigue siendo la misma <c>Key</c>.
    /// </summary>
    private TSitio? DelSitio(Guid sitio)
    {
        foreach (var (clave, valores) in Sitios)
        {
            if (Guid.TryParse(clave, out var key) && key == sitio)
            {
                return valores;
            }
        }

        return null;
    }
}

/// <summary>
/// Las reglas que se repiten entre secciones de negocio. Cada una devuelve el problema, o <c>null</c>
/// si el valor sirve.
/// </summary>
public static class ReglasDeNegocio
{
    /// <summary>
    /// Una ruta del propio sitio o una URL http(s) absoluta. <c>//host</c> y <c>/\host</c> no: el
    /// navegador los lee como otro origen disfrazado de ruta, y si va a otro origen se escribe entero.
    /// </summary>
    public static string? ApiBase(string clave, string? valor)
    {
        var sirve = !string.IsNullOrWhiteSpace(valor) && !valor.Any(char.IsWhiteSpace)
            && (valor.StartsWith('/')
                ? valor.Length == 1 || (valor[1] != '/' && valor[1] != '\\')
                : Uri.TryCreate(valor, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));

        return sirve
            ? null
            : $"{clave}: «{valor}» tiene que ser una ruta del sitio («/api/x») o una URL http(s) absoluta.";
    }

    /// <summary>
    /// De 0 a 100 y con dos decimales como mucho: con eso la UI y el servidor calculan con
    /// aritmética exacta y les da lo mismo.
    /// </summary>
    public static string? Porcentaje(string clave, decimal valor)
        => valor is < 0m or > 100m || decimal.Round(valor, 2) != valor
            ? $"{clave}: {valor} tiene que ir de 0 a 100 con dos decimales como mucho."
            : null;
}
