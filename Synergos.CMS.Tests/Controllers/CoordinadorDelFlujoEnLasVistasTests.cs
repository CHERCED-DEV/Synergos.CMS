using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El CMS coloca <c>&lt;synergos-flujo&gt;</c> desde una vista (ADR 0140 F4): cada clave de flujo que
/// escribe una vista la expone la puerta y está abierta en el sitio, y la funcionalidad de Eventos se
/// pinta siempre dentro de su coordinador.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> En el piloto la clave no es un campo de un record (no hay colocable
/// contenedor: 0 de 52 records lo son, y el emisor sólo saca hojas), sino un literal en
/// <c>blockgrid/Components/elementSynEventos.cshtml</c>. Un literal que nadie cruza no lo ve ningún
/// build: renombrar el flujo en el orquestador, o cerrarlo en <c>Synergos:Puerta:Flujos</c>, dejaría la
/// compra diciendo «no disponible» en cada página con el SSR en verde.</para>
///
/// <para><b>Y el enlace del aviso.</b> El correo de la compra lleva <c>Aviso:Ruta</c> de este mismo
/// <c>appsettings.json</c>, y el participante abre la compra leyendo <c>?compra=</c>. El validador de la
/// puerta acepta cualquier ruta del sitio, así que un <c>/?id={id}</c> arrancaba y el enlace abría la
/// cartelera sin decir nada. Acá se fija el nombre; el cruce con lo que lee el UI
/// (<c>AVISOS_DE_LOS_FLUJOS</c>) es <c>gate:avisos</c> (G-15), del lado del UI.</para>
///
/// <para><b>Un flujo dentro de otro, sólo si es una operación posterior suya</b> (NR-10 de 008): la cancelación de una
/// visita dentro de la visita. El coordinador ya deja subir lo ajeno —el de adentro atiende su flujo y el de afuera el
/// suyo (<c>flujos.spec.ts</c> del UI)—, así que no hace falta tocarlo; lo que se decide es qué anidamiento es un
/// error de la vista. La relación es la <c>posterior_a</c> de las definiciones de los orquestadores.</para>
///
/// <para><b>Lo que NO mira</b>: que el bundle defina la etiqueta ni que el participante la encuentre por
/// ancestro (eso es del UI y del navegador), ni los <c>appsettings.&lt;Entorno&gt;.json</c>: la base es lo
/// que reciben todos los entornos.</para>
/// </remarks>
public sealed class CoordinadorDelFlujoEnLasVistasTests
{
    private static readonly Regex Coordinador = new(@"<synergos-flujo\b([^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Clave = new(@"\sflujo=""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SinLayout = new(@"\sstyle=""display:\s*contents;?""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PintaEventos = new(@"PartialAsync\(\s*""SynHost/Eventos""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Comentarios = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    private static readonly Regex Etiqueta = new(@"<synergos-flujo\b[^>]*>|</synergos-flujo>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>La clave elegida por el editor (núcleo N3): <c>var x = Colocables.Elegir(…, porDefecto: "clave");</c>.</summary>
    private static readonly Regex Elegida = new(
        @"var\s+(?<var>\w+)\s*=\s*Colocables\.Elegir\([^;]*?porDefecto:\s*""(?<clave>[^""]+)""\s*\)\s*;",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private const string FlujoDeEventos = "eventos.compra";

    /// <summary>Lo que lee el participante (<c>AVISOS_DE_LOS_FLUJOS</c> en <c>vitals/core/src/flujos/avisos.ts</c> del UI).</summary>
    private const string ParametroDelAviso = "compra={id}";

    [Fact]
    public void Cada_flujo_que_coloca_una_vista_lo_expone_la_puerta_y_esta_abierto_en_el_sitio()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var abiertos = FlujosAbiertos();
        var colocados = Vistas()
            .SelectMany(v => Coordinador.Matches(v.Texto).Select(m => (v.Ruta, v.Texto, Atributos: m.Groups[1].Value)))
            .ToList();

        Assert.True(colocados.Count > 0,
            "Ninguna vista coloca <synergos-flujo>: el descubrimiento está roto, o la funcionalidad de Eventos "
            + "dejó de envolverse y su compra ya no encuentra a quién pedirle (ADR 0140 F4).");

        var malos = new List<string>();
        foreach (var (ruta, texto, atributos) in colocados)
        {
            var clave = ClaveColocada(texto, atributos, out var porque);
            if (clave is null)
            {
                malos.Add($"{ruta}: {porque}");
                continue;
            }
            if (tabla.DelFlujo(clave).Count == 0)
            {
                malos.Add($"{ruta}: flujo=\"{clave}\" no lo expone la puerta (contratos incrustados: "
                    + string.Join(", ", tabla.Todas.Select(o => o.Flujo).Distinct(StringComparer.Ordinal)) + ")");
            }
            if (!abiertos.Contains(clave))
            {
                malos.Add($"{ruta}: flujo=\"{clave}\" no está abierto en Synergos:Puerta:Flujos de appsettings.json");
            }
            if (!SinLayout.IsMatch(atributos))
            {
                malos.Add($"{ruta}: <synergos-flujo flujo=\"{clave}\"> sin style=\"display:contents\": antes de que el "
                    + "bundle lo defina es un elemento inline y cambia el layout de lo que envuelve");
            }
        }

        Assert.True(malos.Count == 0, string.Join(Environment.NewLine, malos));
    }

    [Fact]
    public void Toda_vista_que_pinta_la_funcionalidad_de_Eventos_la_pinta_dentro_de_su_coordinador()
    {
        var vistas = Vistas()
            .Where(v => !v.Ruta.EndsWith(Path.Combine("SynHost", "Eventos.cshtml"), StringComparison.OrdinalIgnoreCase))
            .Where(v => PintaEventos.IsMatch(v.Texto))
            .ToList();

        Assert.True(vistas.Count > 0, "Ninguna vista pinta SynHost/Eventos: el descubrimiento está roto.");

        var fuera = new List<string>();
        foreach (var (ruta, texto) in vistas)
        {
            foreach (Match llamada in PintaEventos.Matches(texto))
            {
                var antes = texto[..llamada.Index];
                var abre = antes.LastIndexOf("<synergos-flujo", StringComparison.Ordinal);
                var cierraAntes = antes.LastIndexOf("</synergos-flujo>", StringComparison.Ordinal);
                var dentro = abre >= 0 && abre > cierraAntes
                    && ClaveColocada(texto, Coordinador.Match(texto, abre).Groups[1].Value, out _) == FlujoDeEventos
                    && texto.IndexOf("</synergos-flujo>", llamada.Index, StringComparison.Ordinal) >= 0;
                if (!dentro) fuera.Add(ruta);
            }
        }

        Assert.True(fuera.Count == 0,
            "Pintan la funcionalidad de Eventos fuera de <synergos-flujo flujo=\"eventos.compra\">: "
            + string.Join(", ", fuera) + ". Sin coordinador arriba, su compra dice «no disponible» y no toca la red.");
    }

    [Fact]
    public void El_enlace_del_aviso_de_la_compra_trae_el_parametro_que_lee_el_participante()
    {
        var ruta = RutaDelAviso(FlujoDeEventos);
        Assert.False(string.IsNullOrWhiteSpace(ruta),
            $"{FlujoDeEventos} no declara Aviso:Ruta en appsettings.json: el correo de la compra no lleva enlace.");

        var consulta = ruta!.Contains('?', StringComparison.Ordinal) ? ruta[(ruta.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        Assert.True(consulta.Split('&').Contains(ParametroDelAviso, StringComparer.Ordinal),
            $"Aviso:Ruta de {FlujoDeEventos} es «{ruta}» y el participante abre la compra con ?{ParametroDelAviso}: "
            + "el enlace del correo abriría la página sin abrir la compra. Si el nombre cambia, cambia también "
            + "AVISOS_DE_LOS_FLUJOS del UI (gate:avisos, G-15).");
    }

    /// <summary>
    /// El resolver de Eventos declara el camino de compra preguntando a la puerta por un flujo (ADR 0140): tiene
    /// que ser el MISMO que la vista coloca alrededor del elemento, o declararía «puerta» por un flujo y el
    /// elemento pediría la compra a otro.
    /// </summary>
    [Fact]
    public void El_camino_de_compra_pregunta_por_el_flujo_que_coloca_la_vista()
    {
        Assert.Equal(FlujoDeEventos, Synergos.CMS.Web.Services.SynHost.EventosResolutor.FlujoDeLaCompra);
    }

    /// <summary>
    /// La colocación por declaración del editor (núcleo N3, FR-033): lo que el desplegable ofrece es la lista
    /// cerrada —cada opción la expone la puerta y el sitio la declara—, y cada vista que deja elegir lee una
    /// propiedad que su bloque tiene, por <c>compFlujo</c>.
    /// </summary>
    [Fact]
    public void Lo_que_elige_el_editor_es_de_la_lista_cerrada_y_su_bloque_tiene_la_propiedad()
    {
        var uSync = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "uSync", "v9");
        var tabla = TablaDeLaPuerta.Incrustada();
        var abiertos = FlujosAbiertos();

        var config = System.Xml.Linq.XDocument.Load(Path.Combine(uSync, "DataTypes", "DTSelectFlujo.config"));
        using var opciones = JsonDocument.Parse(config.Root!.Element("Config")!.Value);
        var valores = opciones.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("value").GetString()!).ToList();
        Assert.NotEmpty(valores);
        foreach (var valor in valores)
        {
            Assert.True(tabla.DelFlujo(valor).Count > 0 && abiertos.Contains(valor),
                $"DTSelectFlujo ofrece «{valor}», que la puerta no expone o appsettings.json no declara: el editor elegiría un flujo que la vista descartaría.");
        }

        var composicion = System.Xml.Linq.XDocument.Load(Path.Combine(uSync, "ContentTypes", "compflujo.config"));
        var propiedad = composicion.Descendants("GenericProperty").Single(p => (string?)p.Element("Alias") == "flujo");
        Assert.Equal((string?)config.Root.Attribute("Key"), (string?)propiedad.Element("Definition"));
        var clave = (string?)composicion.Root!.Attribute("Key");

        var eligen = Vistas().Where(v => Elegida.IsMatch(v.Texto)).ToList();
        Assert.NotEmpty(eligen);
        foreach (var (ruta, _) in eligen)
        {
            var elemento = Path.Combine(uSync, "ContentTypes", Path.GetFileNameWithoutExtension(ruta).ToLowerInvariant() + ".config");
            Assert.True(File.Exists(elemento), $"{ruta} deja elegir el flujo y no se encontró su bloque en uSync ({elemento}).");
            var compone = System.Xml.Linq.XDocument.Load(elemento).Descendants("Composition").Any(c => (string?)c.Attribute("Key") == clave);
            Assert.True(compone, $"{ruta} lee la propiedad «flujo» y su bloque no compone compFlujo: el editor no tendría dónde elegir.");
        }
    }

    [Fact]
    public void Un_flujo_solo_va_dentro_de_otro_si_es_una_operacion_posterior_suya()
    {
        var posteriores = PosterioresDeclarados();
        var malos = Vistas()
            .SelectMany(v => AnidadosQueNoSonPosteriores(v.Texto, posteriores).Select(m => $"{v.Ruta}: {m}"))
            .ToList();

        Assert.True(malos.Count == 0,
            "Una vista coloca un flujo dentro de otro que no es su original: " + string.Join("; ", malos)
            + ". Sólo una operación posterior va dentro de lo que la origina (posterior_a).");
    }

    [Fact]
    public void La_regla_del_anidamiento_admite_solo_una_posterior_dentro_de_su_original()
    {
        var posteriores = new Dictionary<string, string>(StringComparer.Ordinal) { ["p.cancelacion"] = "p.visita" };
        static string Abre(string clave) => $"<synergos-flujo flujo=\"{clave}\" style=\"display:contents\">";
        const string Cierra = "</synergos-flujo>";

        Assert.Empty(AnidadosQueNoSonPosteriores(Abre("p.visita") + Abre("p.cancelacion") + Cierra + Cierra, posteriores));
        Assert.Empty(AnidadosQueNoSonPosteriores(Abre("p.visita") + Cierra + Abre("p.otra") + Cierra, posteriores));
        Assert.Single(AnidadosQueNoSonPosteriores(Abre("p.cancelacion") + Abre("p.visita") + Cierra + Cierra, posteriores));
        Assert.Single(AnidadosQueNoSonPosteriores(Abre("p.otra") + Abre("p.cancelacion") + Cierra + Cierra, posteriores));
        Assert.Single(AnidadosQueNoSonPosteriores(Abre("p.visita") + Abre("p.visita") + Cierra + Cierra, posteriores));

        // Después de un hermano cerrado, el de afuera sigue siendo el que envuelve: se cuenta con una pila.
        Assert.Single(AnidadosQueNoSonPosteriores(
            Abre("p.otra") + Abre("p.cancelacion") + Cierra + Abre("p.otra2") + Cierra + Cierra,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["p.cancelacion"] = "p.otra" }));
        Assert.Empty(AnidadosQueNoSonPosteriores(
            Abre("p.visita") + Abre("p.cancelacion") + Cierra + Abre("p.reprogramacion") + Cierra + Cierra,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["p.cancelacion"] = "p.visita", ["p.reprogramacion"] = "p.visita" }));
    }

    /// <summary>
    /// Los <c>&lt;synergos-flujo&gt;</c> que una vista coloca dentro de otro que no es su original, en orden. Recorre
    /// las etiquetas con una pila: un hermano ya cerrado no cuenta como el que envuelve.
    /// </summary>
    internal static IReadOnlyList<string> AnidadosQueNoSonPosteriores(string texto, IReadOnlyDictionary<string, string> posteriorA)
    {
        var malos = new List<string>();
        var abiertos = new Stack<string?>();
        foreach (Match etiqueta in Etiqueta.Matches(texto))
        {
            if (etiqueta.Value.StartsWith("</", StringComparison.Ordinal))
            {
                if (abiertos.Count > 0) abiertos.Pop();
                continue;
            }

            var clave = ClaveColocada(texto, Coordinador.Match(texto, etiqueta.Index).Groups[1].Value, out _);
            if (abiertos.Count > 0)
            {
                var afuera = abiertos.Peek();
                if (clave is null || afuera is null || posteriorA.GetValueOrDefault(clave) != afuera)
                {
                    malos.Add($"<synergos-flujo flujo=\"{clave}\"> dentro de <synergos-flujo flujo=\"{afuera}\">");
                }
            }
            abiertos.Push(clave);
        }
        return malos;
    }

    /// <summary>Lo que cada definición de los orquestadores declara <c>posterior_a</c>: clave → la de su original.</summary>
    private static IReadOnlyDictionary<string, string> PosterioresDeclarados()
    {
        var separador = Path.DirectorySeparatorChar;
        var definiciones = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "backend", "orquestadores"), "*.json", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "flujos", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{separador}bin{separador}", StringComparison.Ordinal) && !f.Contains($"{separador}obj{separador}", StringComparison.Ordinal))
            .ToList();
        Assert.True(definiciones.Count >= 3, $"Se leyeron {definiciones.Count} definiciones de flujo: el descubrimiento está roto.");

        var posteriores = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definicion in definiciones)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(definicion));
            if (doc.RootElement.TryGetProperty("posterior_a", out var original))
            {
                posteriores[doc.RootElement.GetProperty("clave").GetString()!] = original.GetString()!;
            }
        }
        return posteriores;
    }

    /// <summary>
    /// La clave que coloca un <c>&lt;synergos-flujo&gt;</c>: el literal del atributo, o —si el atributo es una
    /// variable— el por defecto de la <c>Colocables.Elegir</c> que la asigna. Cualquier otra expresión no sale
    /// de la lista cerrada, y es un defecto.
    /// </summary>
    private static string? ClaveColocada(string texto, string atributos, out string porque)
    {
        porque = string.Empty;
        var valor = Clave.Match(atributos) is { Success: true } m ? m.Groups[1].Value : null;
        if (string.IsNullOrEmpty(valor))
        {
            porque = "<synergos-flujo> sin flujo=\"…\"";
            return null;
        }
        if (!valor.StartsWith('@')) return valor;

        var variable = valor.TrimStart('@').Trim('(', ')');
        var asignada = Elegida.Matches(texto).FirstOrDefault(x => x.Groups["var"].Value == variable);
        if (asignada is null)
        {
            porque = $"<synergos-flujo flujo=\"{valor}\">: la clave no sale de Colocables.Elegir(…, porDefecto: \"…\"), la lista cerrada";
            return null;
        }
        return asignada.Groups["clave"].Value;
    }

    /// <summary>Las vistas, sin sus comentarios de Razor ni de HTML: lo que se nombra en un comentario no se coloca.</summary>
    private static IReadOnlyList<(string Ruta, string Texto)> Vistas()
    {
        var vistas = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Ruta: Path.GetRelativePath(RepoRoot(), f), Texto: Comentarios.Replace(File.ReadAllText(f), string.Empty)))
            .ToList();
        Assert.True(vistas.Count > 100, $"Se leyeron {vistas.Count} vistas: el descubrimiento está roto.");
        return vistas;
    }

    private static IReadOnlySet<string> FlujosAbiertos()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var flujos = doc.RootElement.GetProperty("Synergos").GetProperty("Puerta").GetProperty("Flujos");
        return flujos.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string? RutaDelAviso(string flujo)
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var flujos = doc.RootElement.GetProperty("Synergos").GetProperty("Puerta").GetProperty("Flujos");
        return flujos.TryGetProperty(flujo, out var config)
            && config.TryGetProperty("Aviso", out var aviso)
            && aviso.TryGetProperty("Ruta", out var ruta)
            ? ruta.GetString()
            : null;
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
