using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El camino de un clon limpio a una portada publicada existe, está escrito, y lo que está
/// escrito se puede ejecutar (#119).
/// </summary>
/// <remarks>
/// <para><b>Qué vigila que no vigile el humo.</b> <c>tools/humo-portada.mjs</c> arranca la
/// aplicación y pide la página — es el gate de verdad, y tarda tres minutos. Éste corre en la
/// suite y caza barato las dos formas de romperlo que no hace falta arrancar nada para ver:
/// que la herramienta componga un ElementType que el schema no tiene, y que el documento de
/// despliegue mande correr un endpoint que ya no existe.</para>
///
/// <para><b>Y la segunda es la que ya pasó.</b> El tooling anterior
/// (<c>POST /dev/seed-synergos-identity</c>) llevaba roto quién sabe cuánto —contestaba
/// <c>200</c> con <c>success:false</c> y <c>platform-save-failed</c>— y no estaba nombrado en
/// ningún documento, así que nadie tenía por qué correrlo y descubrirlo. Un camino escrito
/// que nadie cruza contra el código es una hora perdida para quien lo siga.</para>
/// </remarks>
public sealed class PortadaDeArranqueTests
{
    private const string DocDespliegue = "docs/despliegue/00-montar-el-entorno.md";
    private const string DevController = "Synergos.CMS.Web/Controllers/DevController.cs";
    private const string SeederFile = "StarterPortadaSeeder.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir.FullName;
    }

    private static string Leer(string ruta) =>
        File.ReadAllText(Path.Combine(RepoRoot(), ruta.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Los ElementTypes que la portada compone existen en el schema.
    /// </summary>
    /// <remarks>
    /// Se leen del propio seeder —las constantes <c>*Alias</c>— y se cruzan contra los ficheros
    /// de <c>uSync/v9/ContentTypes/</c>. No es una lista escrita a mano: una lista y un código
    /// que se desincronizan dan un gate que se conforma con lo que ya no se usa.
    /// <para>Un alias que el schema no tiene <b>no revienta</b>: la herramienta contesta
    /// <c>missing-content-types</c> y la portada no se crea — o sea, exactamente el estado del
    /// que este ticket venía a sacar al repo, con un mensaje de error encima.</para>
    /// </remarks>
    [Fact]
    public void Los_element_types_que_compone_la_portada_existen_en_el_schema()
    {
        var aliases = Synergos.CMS.Web.Services.StarterPortadaSeeder.RequiredContentTypes;

        Assert.True(aliases.Count >= 5,
            $"La portada declara {aliases.Count} content types requeridos: eran cinco, así que "
            + "algo se cayó de la lista y este gate dejó de mirar lo que cree que mira.");

        var carpeta = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9", "ContentTypes");
        var declarados = Directory.EnumerateFiles(carpeta, "*.config")
            .Select(File.ReadAllText)
            .Select(t => Regex.Match(t, @"<ContentType[^>]*\sAlias=""([^""]+)""").Groups[1].Value)
            .Where(a => a.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var faltan = aliases.Where(a => !declarados.Contains(a)).ToList();
        Assert.True(faltan.Count == 0,
            $"La portada de arranque compone tipos que el schema no declara: {string.Join(", ", faltan)}. "
            + "Sin ellos la herramienta contesta missing-content-types y el sitio se queda en blanco.");
    }

    /// <summary>
    /// Todo endpoint <c>/dev/...</c> que el documento de despliegue manda correr existe.
    /// </summary>
    /// <remarks>
    /// La dirección importa: se leen las rutas <b>del documento</b> y se exigen en el
    /// controller, no al revés. Hay catorce endpoints de dev y sólo los que el documento
    /// nombra son un camino que alguien va a seguir a ciegas.
    /// </remarks>
    [Fact]
    public void El_documento_de_despliegue_no_manda_correr_endpoints_que_no_existen()
    {
        var doc = Leer(DocDespliegue);
        var rutasDelDoc = Regex.Matches(doc, @"/dev/([a-z0-9-]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(rutasDelDoc.Count > 0,
            $"{DocDespliegue} no nombra ningún endpoint /dev/…: el paso del contenido volvió a "
            + "quedarse sin herramienta escrita, que es el hueco de #119.");

        var controller = SinComentarios(Leer(DevController));
        var declaradas = Regex.Matches(controller, @"\[Http(?:Post|Get|Delete)\(""([^""]+)""\)\]")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var huerfanas = rutasDelDoc.Where(r => !declaradas.Contains(r)).ToList();
        Assert.True(huerfanas.Count == 0,
            $"{DocDespliegue} manda correr endpoints que DevController no declara: "
            + $"{string.Join(", ", huerfanas)}. Quien siga el documento se queda mirando un 404.");
    }

    /// <summary>
    /// El documento de despliegue nombra la herramienta de la portada.
    /// </summary>
    /// <remarks>
    /// Es la otra mitad del gate de arriba y la que cierra el defecto original: durante tres
    /// documentos el paso del contenido decía «alguien tiene que autorarlo a mano» sin nombrar
    /// con qué. Junto con el cruce de rutas, borrar el endpoint pone rojo un gate y borrar el
    /// párrafo pone rojo el otro.
    /// </remarks>
    [Fact]
    public void El_documento_de_despliegue_nombra_la_herramienta_de_la_portada()
    {
        var ruta = Regex.Match(SinComentarios(Leer(DevController)),
            @"\[HttpPost\(""(seed-portada)""\)\]").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(ruta),
            "DevController ya no declara POST /dev/seed-portada — si la herramienta se renombró, "
            + $"{DocDespliegue} §5.bis.2 hay que reescribirlo en el mismo commit.");

        Assert.Contains($"/dev/{ruta}", Leer(DocDespliegue), StringComparison.Ordinal);
    }

    /// <summary>
    /// Los catorce endpoints de dev siguen detrás del flag.
    /// </summary>
    /// <remarks>
    /// <para>El flag es la salvaguarda, no la autenticación (#113): los endpoints son
    /// <c>[AllowAnonymous]</c>, así que uno al que se le caiga el <c>if (!_settings.Enabled)</c>
    /// queda alcanzable desde internet — y <c>clear-all-content</c> borra el árbol entero.</para>
    /// <para>Se cuenta en vez de enumerar: una lista escrita a mano se queda corta el día que
    /// alguien añade el decimoquinto, que es exactamente cuando hace falta.</para>
    /// </remarks>
    [Fact]
    public void Todo_endpoint_de_dev_comprueba_el_flag_antes_de_hacer_nada()
    {
        var fuente = SinComentarios(Leer(DevController));
        var acciones = Regex.Matches(fuente,
            @"\[Http(?:Post|Get|Delete)\(""(?<ruta>[^""]+)""\)\][\s\S]*?\{(?<cuerpo>[\s\S]*?)\n    \}");

        Assert.True(acciones.Count >= 10,
            $"Se leyeron {acciones.Count} acciones en DevController: la forma cambió y este gate "
            + "dejó de mirar lo que cree que mira.");

        var sinFlag = acciones
            .Where(m => !m.Groups["cuerpo"].Value.Contains("_settings.Enabled", StringComparison.Ordinal))
            .Select(m => m.Groups["ruta"].Value)
            .ToList();

        Assert.True(sinFlag.Count == 0,
            $"Estos endpoints de /dev no miran Synergos:DevSeed:Enabled: {string.Join(", ", sinFlag)}. "
            + "Son [AllowAnonymous]: sin el flag quedan abiertos en producción (#113).");
    }

    /// <summary>
    /// Nada siembra la portada en el arranque.
    /// </summary>
    /// <remarks>
    /// ADR 0013, dicho sobre esta herramienta en concreto: el único sitio que la nombra fuera de
    /// sus tests es el controller que vive detrás del flag y el composer que la registra en el
    /// contenedor. Un <c>IHostedService</c>, un <c>INotificationHandler</c> de arranque o una
    /// llamada desde un composer la convertirían en un seeder de boot — y con
    /// <c>ContentHandler</c> encendido (ADR 0129) eso revertiría en cada reinicio lo que un
    /// editor publicó.
    /// </remarks>
    [Fact]
    public void La_portada_no_se_siembra_en_el_arranque()
    {
        var web = Path.Combine(RepoRoot(), "Synergos.CMS.Web");
        var llamadores = Directory
            .EnumerateFiles(web, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
            .Where(f => !f.EndsWith(SeederFile, StringComparison.Ordinal))
            .Where(f => SinComentarios(File.ReadAllText(f))
                .Contains("StarterPortadaSeeder", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            new[] { "DevController.cs", "SeamComposer.ModerationDevNotifications.cs" },
            llamadores);
    }

    /// <summary>
    /// La fuente sin comentarios: un gate que lee código no puede confundir una mención con
    /// una llamada (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </summary>
    private static string SinComentarios(string fuente)
    {
        var sinBloque = Regex.Replace(fuente, @"/\*[\s\S]*?\*/", string.Empty);
        var sinLinea = Regex.Replace(sinBloque, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
        return Regex.Replace(sinLinea, @"^\s*///.*$", string.Empty, RegexOptions.Multiline);
    }
}
