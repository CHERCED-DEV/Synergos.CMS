using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los tres sitios donde el bridge degrada lo DICEN (defecto #92).
/// </summary>
/// <remarks>
/// <para><b>Degradar está bien; no saberlo, no.</b> Que <c>window.synergos</c> no tumbe la página
/// está razonado en el propio fichero y degrada <i>cerrada</i> —<c>Member: null</c>, <c>Page.Id:
/// 0</c>, <c>Keys: {}</c>—, así que no se otorga nada de más. El problema es que degradaba cerrada
/// <b>para siempre y sin que nadie se enterara</b>: el sitio devuelve 200, <c>/health</c> contesta,
/// la prueba de humo pasa, y un miembro autenticado se ve anónimo para todos los web components.
/// Es la forma de fallo que <c>CLAUDE.md</c> §11 cataloga como la más cara de este repo.</para>
///
/// <para><b>Y el reparto era el chiste:</b> de los dos caminos que emiten el bridge, el que sí
/// registraba —el controller de CSP-strict— es el que <b>no corre</b>, porque
/// <c>CspStrictMode</c> es <c>false</c> por defecto. El camino por defecto, una vista Razor, se
/// callaba. Había un test para el caso «el builder lanza → devuelve el fallback»… sobre el hermano,
/// lo que daba la sensación de que estaba cubierto.</para>
///
/// <para><b>Por qué es un gate de texto y no un test de comportamiento.</b> Dos de los tres sitios
/// son una vista Razor y un <c>catch</c> interno que no se pueden ejercitar sin levantar Umbraco
/// entero. Lo que hay que impedir es que alguien vuelva a dejar un <c>catch</c> mudo ahí, y eso se
/// ve en el fichero. El gate dice lo que mira para no aparentar más alcance del que tiene.</para>
/// </remarks>
public sealed class BridgeDegradaHablandoTests
{
    /// <summary>
    /// Los tres <c>catch</c> del bridge registran. Ninguno se traga la excepción.
    /// </summary>
    /// <remarks>
    /// Van juntos a propósito: el defecto no fue que faltara uno, fue que **dos de tres** callaban
    /// y el que hablaba era el que no corre. Un gate por sitio dejaría volver a caer en el mismo
    /// reparto sin que nada lo nombrara.
    /// </remarks>
    [Theory]
    // El camino POR DEFECTO — CspStrictMode es false.
    [InlineData("Synergos.CMS.Web/Views/Shared/_SynergosBridge.cshtml", "la vista inline")]
    // El gemelo, que ya lo hacía; se vigila para que no se pierda.
    [InlineData("Synergos.CMS.Web/Controllers/SynergosBridgeController.cs", "el controller de CSP-strict")]
    // El más silencioso: devuelve un contexto que PARECE sano, con el diccionario vacío.
    [InlineData("Synergos.CMS.Web/Services/DefaultHostBridgeContextBuilder.cs", "el builder")]
    public void Todo_catch_del_bridge_registra(string ruta, string quien)
    {
        var texto = File.ReadAllText(Path.Combine(RepoRoot(), ruta.Replace('/', Path.DirectorySeparatorChar)));

        var catches = Regex.Matches(texto, @"catch\s*\(Exception(\s+\w+)?\s*\)\s*\{([\s\S]*?)\n(\s*)\}");
        Assert.True(catches.Count > 0,
            $"No se encontró ningún `catch (Exception)` en {quien} ({ruta}): cambió de forma y este "
            + "gate dejó de mirarlo, que es peor que no tenerlo.");

        var mudos = catches
            .Where(m => !m.Groups[2].Value.Contains("Log", StringComparison.Ordinal))
            .Select(m => m.Value.Split('\n')[0].Trim())
            .ToList();

        Assert.True(mudos.Count == 0,
            $"En {quien} hay {mudos.Count} `catch` que se traga la excepción sin registrarla (#92). "
            + "El bridge degrada CERRADA —member null, keys vacío— y eso está bien; lo que no puede "
            + "es que nadie se entere: el sitio devuelve 200, /health contesta, el humo pasa, y un "
            + "miembro autenticado se ve anónimo para todos los web components."
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", mudos));
    }

    /// <summary>
    /// Y el builder tiene con qué avisar.
    /// </summary>
    /// <remarks>
    /// No lo recibía en el constructor, así que su <c>catch</c> no podía hablar aunque quisiera —
    /// el gate de arriba sería imposible de cumplir ahí sin esto. Se vigila aparte porque quitarle
    /// el logger es una forma de romperlo que no deja un <c>catch</c> mudo a la vista: dejaría de
    /// compilar, sí, pero el día que alguien "simplifique" el constructor conviene que un test
    /// diga por qué estaba.
    /// </remarks>
    [Fact]
    public void El_builder_recibe_un_logger_para_poder_avisar()
    {
        var texto = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "DefaultHostBridgeContextBuilder.cs"));

        Assert.Contains("ILogger<DefaultHostBridgeContextBuilder> logger", texto, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
