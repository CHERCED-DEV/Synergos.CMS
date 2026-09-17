using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La raíz del CDN de disco: ni un default de UNA máquina, ni un <c>FileSystem</c> que arranque
/// sin poder servir nada (#132).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>appsettings.Development.json</c> traía <c>C:\LOCAL_CDN</c> en los dos
/// sitios que nombran la carpeta del CDN, y <c>appsettings.Docker.json</c> declaraba
/// <c>Mode=FileSystem</c> contra un <c>/cdn</c> que la imagen <b>no monta</b> (lo dice la ADR del
/// defecto #126 y lo confirma el compose: cinco volúmenes y ninguno es ése). En las dos, un
/// arranque fuera de la máquina del arquitecto corría <c>FileSystem</c> contra nada.</para>
///
/// <para><b>Y eso NO fallaba.</b> Medido el 2026-09-16 con la misma portada sembrada, cambiando
/// sólo <c>LocalPath</c>: hacia <c>Synergos.UI/public</c> salen <b>21 577 bytes</b> con import map
/// de <b>23</b> entradas y <b>2</b> <c>&lt;script type="module"&gt;</c>; hacia
/// <c>C:\LOCAL_CDN</c>, <b>19 785 bytes</b>, <b>sin</b> import map y con <b>cero</b> scripts — y
/// los dos <c>&lt;synergos-*&gt;</c> pintados por el SSR en los dos casos. El defecto #126 exacto,
/// en el modo que se usa para desarrollar. Son los mismos dos números que midió
/// <c>humo-conectado</c> para el nombre de variable equivocado.</para>
///
/// <para><b>Por qué gate y no sólo el arreglo.</b> Ningún test lee los <c>appsettings</c>: los del
/// cliente construyen el settings a mano con un <c>_tempRoot</c> que siempre existe, y los del
/// probe usaban <c>C:\LOCAL_CDN</c> <b>como literal de fixture</b>, o sea que el defecto estaba
/// escrito en los tests como si fuera lo normal. Es la enfermedad que el repo hermano vigila con
/// su gate <c>rutas-hermanas</c>, sin nadie vigilándola de este lado.</para>
/// </remarks>
public sealed class RaizDelCdnTests
{
    private static string RutaDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    private static string Web() => Path.Combine(RutaDelRepo(), "Synergos.CMS.Web");

    /// <summary>Los <c>appsettings</c> que la aplicación lee. Los <c>-schema</c> no son config.</summary>
    private static IEnumerable<string> Appsettings()
        => Directory.EnumerateFiles(Web(), "appsettings*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("appsettings-schema", StringComparison.Ordinal))
            .OrderBy(f => f);

    /// <summary>
    /// Una ruta que sólo existe en la máquina de quien la escribió: letra de unidad de Windows,
    /// un directorio personal, o una variable de entorno de usuario.
    /// </summary>
    /// <remarks>
    /// <para><b>No se rechaza toda ruta absoluta</b>, y es deliberado: <c>appsettings.Docker.json</c>
    /// dice <c>/cdn</c>, que es absoluta y es de la IMAGEN — un contrato, no una máquina. Lo que
    /// distingue a las malas es que nombran un usuario o un volumen concreto.</para>
    /// </remarks>
    private static readonly Regex DeUnaMaquina = new(
        @"^[A-Za-z]:[\\/]|^/(home|Users)/|%USERPROFILE%|\$HOME", RegexOptions.Compiled);

    /// <summary>
    /// Lo que HOY nombra la máquina del arquitecto a conciencia, con su razón. Se vigila en los
    /// dos sentidos: una entrada nueva sin declarar rompe el build, y una declarada que ya no
    /// está, también — una excepción que sobra deja de leerse.
    /// </summary>
    /// <remarks>
    /// Las tres son de <b>Development</b> y ninguna tiene la forma del defecto que este gate
    /// existe para cazar: las dos del certificado hacen que <b>Kestrel no arranque</b> si faltan,
    /// y el buzón de correo de desarrollo hace que <b>el envío falle</b>. Fallar a la vista es
    /// justo lo contrario de servir una página muerta. Quedan anotadas porque siguen siendo rutas
    /// de una máquina y alguien tendrá que decidir dónde viven — ver #132.
    /// </remarks>
    private static readonly Dictionary<string, string> DeclaradasDeUnaMaquina = new()
    {
        ["Kestrel:Endpoints:Https:Certificate:Path"] =
            "certificado de desarrollo del arquitecto; sin él Kestrel no arranca — falla a la vista",
        ["Kestrel:Endpoints:Https:Certificate:KeyPath"] =
            "la llave del anterior, mismo trato",
        ["Umbraco:CMS:Global:Smtp:PickupDirectoryLocation"] =
            "buzón de correo de desarrollo; sin él el envío falla — falla a la vista",
    };

    /// <summary>Recorre un JSON de configuración devolviendo (clave, valor) de cada cadena.</summary>
    private static IEnumerable<(string Clave, string Valor)> Cadenas(JsonElement nodo, string prefijo = "")
    {
        switch (nodo.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in nodo.EnumerateObject())
                    foreach (var r in Cadenas(p.Value, prefijo.Length == 0 ? p.Name : $"{prefijo}:{p.Name}"))
                        yield return r;
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var e in nodo.EnumerateArray())
                {
                    foreach (var r in Cadenas(e, $"{prefijo}:{i}")) yield return r;
                    i++;
                }
                break;
            case JsonValueKind.String:
                yield return (prefijo, nodo.GetString() ?? string.Empty);
                break;
        }
    }

    private static IEnumerable<(string Fichero, string Clave, string Valor)> TodasLasCadenas()
    {
        foreach (var f in Appsettings())
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(f),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var (clave, valor) in Cadenas(doc.RootElement))
                yield return (Path.GetFileName(f), clave, valor);
        }
    }

    [Fact]
    public void Ningun_appsettings_apunta_por_defecto_a_una_ruta_de_UNA_maquina()
    {
        var todas = TodasLasCadenas().ToList();

        // Red de seguridad: si el descubrimiento deja de ver, el cruce pasaría en verde sin mirar
        // nada — el mismo hueco que #118 dejó escrito para el cruce de DocTypes.
        Assert.True(todas.Count > 50, $"sólo se leyeron {todas.Count} valores de configuración");

        var intrusas = todas
            .Where(t => DeUnaMaquina.IsMatch(t.Valor))
            .Where(t => !DeclaradasDeUnaMaquina.ContainsKey(t.Clave))
            .Select(t => $"{t.Fichero} → {t.Clave} = {t.Valor}")
            .ToList();

        Assert.True(intrusas.Count == 0,
            "Hay rutas de UNA máquina en la configuración por defecto:\n  " + string.Join("\n  ", intrusas)
            + "\n\nUna ruta así no falla: el CDN de disco sirvió una portada de 19 785 bytes sin un "
            + "solo <script type=\"module\"> y con el SSR entero (#132). Si de verdad hace falta, "
            + "declarala en DeclaradasDeUnaMaquina con su razón.");
    }

    [Fact]
    public void Lo_declarado_de_una_maquina_sigue_estando()
    {
        var claves = TodasLasCadenas()
            .Where(t => DeUnaMaquina.IsMatch(t.Valor))
            .Select(t => t.Clave)
            .ToHashSet(StringComparer.Ordinal);

        var sobran = DeclaradasDeUnaMaquina.Keys.Where(k => !claves.Contains(k)).ToList();

        Assert.True(sobran.Count == 0,
            "Estas excepciones ya no corresponden a ninguna ruta de una máquina y hay que quitarlas: "
            + string.Join(", ", sobran)
            + ". Una excepción que sobra deja de leerse, y la siguiente entra detrás de ella.");
    }

    [Fact]
    public void Un_appsettings_solo_declara_FileSystem_con_una_raiz_RELATIVA()
    {
        // Declarar el modo por fichero es legítimo; declarar DÓNDE está el CDN con una ruta
        // absoluta, no. Una relativa (`../../Synergos.UI/public`) es una afirmación sobre la
        // DISPOSICIÓN DEL REPO, cierta en Windows, en Linux y en CI, y se resuelve contra la raíz
        // de contenido. Una absoluta es una afirmación sobre una máquina o sobre un volumen que
        // alguien tiene que acordarse de montar — y `appsettings.Docker.json` decía FileSystem
        // contra `/cdn`, que la imagen NO monta. Lo tapaba que el compose pasa
        // `Mode=${SYNERGOS_CDN_MODE:-Stub}` por encima: un arranque de la misma imagen SIN compose
        // —`humo-portada`, `usync-rebuild-check`— corría FileSystem contra nada.
        var porFichero = TodasLasCadenas()
            .Where(t => t.Clave.StartsWith("Synergos:BundleRegistry:", StringComparison.Ordinal))
            .GroupBy(t => t.Fichero)
            .ToDictionary(g => g.Key, g => g.ToDictionary(t => t.Clave, t => t.Valor, StringComparer.Ordinal));

        var malos = new List<string>();
        foreach (var (fichero, claves) in porFichero)
        {
            if (!claves.TryGetValue("Synergos:BundleRegistry:Mode", out var modo)) continue;
            if (!string.Equals(modo, "FileSystem", StringComparison.OrdinalIgnoreCase)) continue;

            claves.TryGetValue("Synergos:BundleRegistry:LocalPath", out var raiz);
            if (string.IsNullOrWhiteSpace(raiz) || Path.IsPathRooted(raiz) || DeUnaMaquina.IsMatch(raiz))
                malos.Add($"{fichero} → Mode=FileSystem con LocalPath = {raiz ?? "(ausente)"}");
        }

        Assert.True(malos.Count == 0,
            "Estos appsettings encienden FileSystem contra una raíz que no es del repo:\n  "
            + string.Join("\n  ", malos)
            + "\n\nCon una ruta absoluta el modo depende de la máquina o de un volumen montado, y "
            + "cuando falta NO falla: sirve la página en 200 sin import map (#132). O la raíz es "
            + "relativa al repo, o el modo se enciende por entorno.");
    }

    // ── El cableado ─────────────────────────────────────────────────────────────

    private static string SinComentarios(string fuente)
    {
        var sinBloque = Regex.Replace(fuente, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(sinBloque, @"//[^\n]*", string.Empty);
    }

    [Fact]
    public void La_rama_FileSystem_del_composer_LLAMA_a_la_guarda()
    {
        // Se recorta EL CUERPO de la rama y se busca ahí la LLAMADA, no la mención en el fichero.
        // Es el addendum #14 de `feedback_an_exemption_needs_a_signature_behind_it`: un `Contains`
        // sobre el fichero entero se pone verde con la guarda escrita veinte líneas más abajo y
        // desconectada — que es literalmente el defecto que esto vigila.
        var fuente = SinComentarios(File.ReadAllText(
            Path.Combine(Web(), "Composers", "SeamComposer.Platform.cs")));

        var abre = fuente.IndexOf("if (string.Equals(bundleRegistryMode, \"FileSystem\"", StringComparison.Ordinal);
        Assert.True(abre >= 0, "no se encontró la rama FileSystem — ¿cambió el cableado del registry?");

        var siguiente = fuente.IndexOf("else if (string.Equals(bundleRegistryMode, \"Http\"", abre, StringComparison.Ordinal);
        Assert.True(siguiente > abre, "no se encontró el final de la rama FileSystem");

        var cuerpo = fuente[abre..siguiente];

        Assert.Contains("RaizDelCdnLocal.Exigir(", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_normaliza_TODAS_las_claves_de_raiz_del_CDN()
    {
        // Las claves se DERIVAN de los appsettings y se cruzan en los dos sentidos: una tercera
        // raíz de CDN que nadie normalice quedaría relativa al directorio de trabajo —que cambia
        // entre `dotnet run` y `dotnet exec bin/…`—, y una normalizada que ya nadie declara es
        // ruido que deja de leerse.
        var declaradas = TodasLasCadenas()
            .Select(t => t.Clave)
            .Where(k => k.EndsWith(":LocalPath", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(declaradas);

        var program = SinComentarios(File.ReadAllText(Path.Combine(Web(), "Program.cs")));
        var normalizadas = Regex.Matches(program, @"""(Synergos:[A-Za-z]+:LocalPath)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(declaradas.OrderBy(k => k), normalizadas.OrderBy(k => k));
    }
}
