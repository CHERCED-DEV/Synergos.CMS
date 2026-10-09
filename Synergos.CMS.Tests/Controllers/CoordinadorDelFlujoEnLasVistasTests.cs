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

    private const string FlujoDeEventos = "eventos.compra";

    [Fact]
    public void Cada_flujo_que_coloca_una_vista_lo_expone_la_puerta_y_esta_abierto_en_el_sitio()
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var abiertos = FlujosAbiertos();
        var colocados = Vistas()
            .SelectMany(v => Coordinador.Matches(v.Texto).Select(m => (v.Ruta, Atributos: m.Groups[1].Value)))
            .ToList();

        Assert.True(colocados.Count > 0,
            "Ninguna vista coloca <synergos-flujo>: el descubrimiento está roto, o la funcionalidad de Eventos "
            + "dejó de envolverse y su compra ya no encuentra a quién pedirle (ADR 0140 F4).");

        var malos = new List<string>();
        foreach (var (ruta, atributos) in colocados)
        {
            var clave = Clave.Match(atributos) is { Success: true } m ? m.Groups[1].Value : null;
            if (string.IsNullOrEmpty(clave))
            {
                malos.Add($"{ruta}: <synergos-flujo> sin flujo=\"…\"");
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
                    && Clave.Match(Coordinador.Match(texto, abre).Groups[1].Value).Groups[1].Value == FlujoDeEventos
                    && texto.IndexOf("</synergos-flujo>", llamada.Index, StringComparison.Ordinal) >= 0;
                if (!dentro) fuera.Add(ruta);
            }
        }

        Assert.True(fuera.Count == 0,
            "Pintan la funcionalidad de Eventos fuera de <synergos-flujo flujo=\"eventos.compra\">: "
            + string.Join(", ", fuera) + ". Sin coordinador arriba, su compra dice «no disponible» y no toca la red.");
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
