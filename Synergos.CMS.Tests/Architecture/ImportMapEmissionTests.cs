using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El <c>&lt;script type="importmap"&gt;</c> sale de la seam del registry y de ningún otro sitio
/// (defecto #126).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>_SynHostRuntime.cshtml</c> armaba el mapa leyendo
/// <c>{LocalPath}/{ns}/runtime/angular/latest/import-map.json</c> con <c>File.ReadAllText</c>. En
/// la imagen de producción no hay <c>/cdn</c> montado —el compose monta cinco volúmenes y ninguno
/// es ése—, así que con <c>Mode=Http</c> el fichero no existía y no se emitía mapa. Los
/// <c>&lt;script type="module"&gt;</c> que arma <c>DefaultSynHostEmitter</c> se escribían igual y
/// el navegador no podía resolver <c>@angular/core</c>: ningún <c>&lt;synergos-*&gt;</c> se
/// registraba, con la página en 200 y el SSR entero.</para>
///
/// <para><b>Por qué hay gate y no sólo un test.</b> Las vistas de este repo <b>no se compilan en
/// el build</b> —<c>ModelsMode=InMemoryAuto</c> fuerza <c>RazorCompileOnBuild=false</c>— así que
/// nada en verde dice nada sobre lo que un Razor hace. Y el arreglo es reversible en una línea:
/// basta con que alguien «resuelva» un caso local volviendo a leer del disco. Se vigila la fuente,
/// que es lo único que existe antes de que arranque el sitio.</para>
///
/// <para><b>Y mira el HTML que recibe el navegador, que es la superficie que no miraba nadie.</b>
/// Los tests del cliente miraban el cliente, los del cableado miraban el cableado, y el hueco
/// quedó justo en medio — las dos mitades en verde y el sitio sin hidratar.</para>
/// </remarks>
public sealed class ImportMapEmissionTests
{
    private static string RutaDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private static string Vista()
        => Path.Combine(RutaDelRepo(), "Synergos.CMS.Web", "Views", "Partials", "_SynHostRuntime.cshtml");

    /// <summary>La fuente de la vista, SIN comentarios: lo que se juzga es lo que ejecuta.</summary>
    /// <remarks>
    /// Sin esto el gate se engaña con su propia explicación —este fichero y la vista nombran
    /// <c>File.ReadAllText</c> para contar qué pasó— y es el defecto que
    /// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c> documenta: un corte que
    /// no quita los comentarios mide la prosa.
    /// </remarks>
    private static string CodigoDeLaVista()
    {
        var texto = File.ReadAllText(Vista());
        texto = Regex.Replace(texto, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        texto = Regex.Replace(texto, @"//.*?$", string.Empty, RegexOptions.Multiline);
        return texto;
    }

    [Fact]
    public void La_vista_del_import_map_le_pregunta_a_la_seam()
    {
        Assert.True(File.Exists(Vista()), $"No está {Vista()}. Si la vista se renombró, este gate se mueve con ella.");

        var codigo = CodigoDeLaVista();

        Assert.Contains("IBundleRegistryClient", codigo);
        Assert.Contains("TryGetImportMapAsync", codigo);
    }

    [Theory]
    [InlineData("File.", "leer del disco")]
    [InlineData("Directory.", "mirar el disco")]
    [InlineData("Path.Combine", "armar una ruta del disco")]
    [InlineData("LocalPath", "depender de un directorio que la imagen de producción no monta")]
    [InlineData("IConfiguration", "saltarse la seam yendo a la configuración")]
    public void La_vista_del_import_map_NO_toca_el_disco(string prohibido, string porque)
    {
        var codigo = CodigoDeLaVista();

        Assert.False(
            codigo.Contains(prohibido, StringComparison.Ordinal),
            $"_SynHostRuntime.cshtml vuelve a «{porque}» ({prohibido}). Eso es el defecto #126: en "
            + "el contenedor no hay /cdn montado, así que el mapa no sale, ningún <synergos-*> "
            + "hidrata, y la página contesta 200 con el SSR entero. El mapa lo da "
            + "IBundleRegistryClient.TryGetImportMapAsync — ADR 0012: el contrato del CDN se "
            + "consume, no se relee del disco.");
    }

    /// <summary>
    /// El mapa va en el <c>&lt;head&gt;</c> de todo layout que pueda pintar un
    /// <c>&lt;synergos-*&gt;</c>, y eso se cuenta del disco.
    /// </summary>
    /// <remarks>
    /// <b>Se deriva, no se enumera</b> (<c>feedback_a_named_list_beats_a_count</c>): un layout que
    /// alguien añada mañana entra solo. El criterio es «renderiza bloques del Layout Composer», que
    /// es lo que hace que una página pueda llevar elementos de la CDN — no que el fichero se llame
    /// <c>_Layout</c>.
    /// </remarks>
    [Fact]
    public void Todo_layout_que_pinta_bloques_incluye_el_runtime()
    {
        var views = Path.Combine(RutaDelRepo(), "Synergos.CMS.Web", "Views");
        var sinRuntime = new List<string>();

        foreach (var archivo in Directory.EnumerateFiles(views, "*.cshtml", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(archivo);

            // Un layout raíz: abre el documento él mismo. Los parciales no.
            if (!texto.Contains("<head>", StringComparison.OrdinalIgnoreCase)) continue;
            if (!texto.Contains("<!DOCTYPE html>", StringComparison.OrdinalIgnoreCase)) continue;

            // Y de esos, los que pintan el Block Grid del Layout Composer — los únicos por donde
            // puede entrar un <synergos-*>. Una pantalla de login no necesita el runtime, y
            // exigírselo haría que el gate se relajara con una lista de excepciones.
            var pintaBloques = texto.Contains("blockgrid", StringComparison.OrdinalIgnoreCase)
                               || texto.Contains("GetBlockGridHtml", StringComparison.Ordinal)
                               || texto.Contains("sections", StringComparison.OrdinalIgnoreCase);
            if (!pintaBloques) continue;

            if (!texto.Contains("_SynHostRuntime", StringComparison.Ordinal))
            {
                sinRuntime.Add(Path.GetRelativePath(views, archivo));
            }
        }

        Assert.True(
            sinRuntime.Count == 0,
            "Estos layouts pintan bloques del Layout Composer y no incluyen _SynHostRuntime, así "
            + "que cualquier <synergos-*> que caiga ahí se emite sin import map y no hidrata — la "
            + "página se ve bien y no funciona (#126):\n  " + string.Join("\n  ", sinRuntime));
    }
}
