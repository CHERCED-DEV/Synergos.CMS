namespace Synergos.CMS.Web.Services.Diccionario;

/// <summary>
/// Las secciones de diccionario que piden los elementos de la página que se está renderizando
/// (ADR 0136 §1): la unión de lo que declara el record de cada elemento emitido.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> El bridge publicaba en TODAS las páginas las mismas 176 claves,
/// filtradas por once prefijos fijos de <c>HostBridgeSettings</c>, y ningún elemento las leía (0
/// llamadores de <c>t()</c>). La lista no decía quién necesitaba qué. Ahora la página publica lo
/// que piden sus elementos y nada más: cada elemento lo declara en su record
/// (<c>[ElementoSynHost(..., Diccionario = [...])]</c>), <c>SolicitudSynHost.Para</c> lo pone en la
/// solicitud de emisión, el emitter lo anota acá, y el bridge —que se escribe al FINAL del
/// <c>&lt;body&gt;</c>, cuando ya se emitió todo— publica la unión una sola vez.</para>
///
/// <para><b>Vive en el <c>HttpContext</c> de la petición</b> y no en un servicio con ámbito porque
/// quien anota es el emitter, que es un singleton. Sin petición (un test, un servicio en segundo
/// plano) no anota nada y no hay nada que leer: no lanza.</para>
/// </remarks>
public sealed class SeccionesDeLaPagina
{
    private static readonly object Clave = new();

    private readonly IHttpContextAccessor _http;

    public SeccionesDeLaPagina(IHttpContextAccessor http) => _http = http;

    /// <summary>Suma <paramref name="secciones"/> a las de la página. Repetirlas no las duplica.</summary>
    public void Anotar(IEnumerable<string>? secciones)
    {
        if (secciones is null)
        {
            return;
        }

        var conjunto = Conjunto(crear: true);
        if (conjunto is null)
        {
            return;
        }

        foreach (var seccion in secciones)
        {
            if (!string.IsNullOrWhiteSpace(seccion))
            {
                conjunto.Add(seccion.Trim());
            }
        }
    }

    /// <summary>Las secciones anotadas hasta ahora en esta petición, ordenadas.</summary>
    public IReadOnlyList<string> Declaradas
        => Conjunto(crear: false)?.Order(StringComparer.OrdinalIgnoreCase).ToList() ?? (IReadOnlyList<string>)[];

    /// <summary>Tope de secciones que acepta la consulta de <c>/synergos-bridge.js</c>.</summary>
    public const int MaximoEnLaConsulta = 64;

    /// <summary>
    /// Las secciones como valor de la consulta de <c>/synergos-bridge.js</c> (modo CSP estricto):
    /// <c>Rating,Slider</c>.
    /// </summary>
    /// <remarks>
    /// En modo CSP estricto el bridge lo sirve OTRA petición, que no ve qué emitió la página: la
    /// página se lo dice en la URL. Es el mismo payload que el inline (ADR 0087).
    /// </remarks>
    public static string ALaConsulta(IEnumerable<string> secciones) => string.Join(',', secciones);

    /// <summary>
    /// Las secciones de la consulta de <c>/synergos-bridge.js</c>: separadas por coma, cada una con
    /// forma de alias de diccionario (<c>Seccion</c> o <c>Seccion.Sub</c>), como mucho
    /// <see cref="MaximoEnLaConsulta"/>. Lo que no tiene esa forma se descarta.
    /// </summary>
    public static IReadOnlyList<string> DeLaConsulta(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return [];
        }

        return valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(EsSeccion)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximoEnLaConsulta)
            .ToList();
    }

    private static bool EsSeccion(string s)
        => s.Length <= 128
        && s.Split('.').All(parte => parte.Length > 0 && char.IsAsciiLetter(parte[0]) && parte.All(char.IsAsciiLetterOrDigit));

    private HashSet<string>? Conjunto(bool crear)
    {
        var contexto = _http.HttpContext;
        if (contexto is null)
        {
            return null;
        }

        if (contexto.Items.TryGetValue(Clave, out var guardado) && guardado is HashSet<string> conjunto)
        {
            return conjunto;
        }

        if (!crear)
        {
            return null;
        }

        conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        contexto.Items[Clave] = conjunto;
        return conjunto;
    }
}
