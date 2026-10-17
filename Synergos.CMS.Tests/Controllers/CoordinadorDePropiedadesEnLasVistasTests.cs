using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El portal inmobiliario se pinta dentro del coordinador de la visita, y sólo él (008-propiedades-visita, T012 y
/// T024): la clave que escribe la vista es la que pregunta el resolver y la que abre la puerta, y el enlace del aviso
/// trae el parámetro que lee el participante.
/// </summary>
/// <remarks>
/// <para><b>Lo general lo mira <see cref="CoordinadorDelFlujoEnLasVistasTests"/></b>: que toda clave colocada exista en la
/// puerta y esté abierta. Acá va lo de la visita: un literal en <c>elementSynRealty.cshtml</c> que nadie cruza
/// dejaría el elemento sin a quién pedirle la visita con el SSR en verde.</para>
/// </remarks>
public sealed class CoordinadorDePropiedadesEnLasVistasTests
{
    private static readonly Regex Coordinador = new(@"<synergos-flujo\b([^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Clave = new(@"\sflujo=""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PintaElPortal = new(@"PartialAsync\(\s*""SynHost/Realty""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Comentarios = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    /// <summary>La vista del bloque: la única que coloca el flujo de la visita.</summary>
    private static readonly string VistaDelBloque = Path.Combine("Views", "Partials", "blockgrid", "Components", "elementSynRealty.cshtml");

    /// <summary>Lo que lee el participante (<c>AVISOS_DE_LOS_FLUJOS</c> del UI).</summary>
    private const string ParametroDelAviso = "visita={id}";

    [Fact]
    public void El_portal_se_pinta_siempre_dentro_de_su_coordinador_y_el_resolver_pregunta_por_ese_flujo()
    {
        var vistas = Vistas().Where(v => PintaElPortal.IsMatch(v.Texto)).ToList();
        Assert.True(vistas.Count > 0, "Ninguna vista pinta SynHost/Realty: el descubrimiento está roto.");

        var fuera = vistas.Where(v => PintaElPortal.Matches(v.Texto).Any(m => ClaveQueEnvuelve(v.Texto, m.Index) != RealtyResolutor.FlujoDeLaVisita))
            .Select(v => v.Ruta).ToList();
        Assert.True(fuera.Count == 0,
            $"Pintan el portal fuera de <synergos-flujo flujo=\"{RealtyResolutor.FlujoDeLaVisita}\">: {string.Join(", ", fuera)}.");
    }

    [Fact]
    public void El_flujo_de_la_visita_solo_envuelve_su_bloque_y_ninguna_vista_anida_dos_flujos()
    {
        var colocaciones = Vistas()
            .SelectMany(v => Coordinador.Matches(v.Texto).Select(m => (v.Ruta, v.Texto, m.Index, Clave: Clave.Match(m.Groups[1].Value).Groups[1].Value)))
            .ToList();

        var deLaVisita = colocaciones.Where(c => c.Clave == RealtyResolutor.FlujoDeLaVisita).Select(c => c.Ruta).Distinct().ToList();
        Assert.Equal(new[] { VistaDelBloque }, deLaVisita.Select(r => r[r.IndexOf("Views", StringComparison.Ordinal)..]));

        var anidadas = colocaciones
            .Where(c => ClaveQueEnvuelve(c.Texto, c.Index) is not null)
            .Select(c => c.Ruta)
            .ToList();
        Assert.True(anidadas.Count == 0, $"Una vista coloca un flujo dentro de otro: {string.Join(", ", anidadas)}.");
    }

    [Fact]
    public void El_enlace_del_aviso_de_la_visita_trae_el_parametro_que_lee_el_participante()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var ruta = doc.RootElement.GetProperty("Synergos").GetProperty("Puerta").GetProperty("Flujos")
            .GetProperty(RealtyResolutor.FlujoDeLaVisita).GetProperty("Aviso").GetProperty("Ruta").GetString();

        var consulta = ruta!.Contains('?', StringComparison.Ordinal) ? ruta[(ruta.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        Assert.Contains(ParametroDelAviso, consulta.Split('&'));
    }

    /// <summary>La clave del <c>&lt;synergos-flujo&gt;</c> abierto antes de <paramref name="posicion"/>, o <c>null</c> si no hay.</summary>
    private static string? ClaveQueEnvuelve(string texto, int posicion)
    {
        var antes = texto[..posicion];
        var abre = antes.LastIndexOf("<synergos-flujo", StringComparison.Ordinal);
        var cierra = antes.LastIndexOf("</synergos-flujo>", StringComparison.Ordinal);
        if (abre < 0 || abre < cierra || texto.IndexOf("</synergos-flujo>", posicion, StringComparison.Ordinal) < 0) return null;
        return Clave.Match(Coordinador.Match(texto, abre).Groups[1].Value).Groups[1].Value;
    }

    /// <summary>Las vistas, sin sus comentarios: lo que se nombra en un comentario no se coloca.</summary>
    private static IReadOnlyList<(string Ruta, string Texto)> Vistas()
    {
        var vistas = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Ruta: Path.GetRelativePath(Path.Combine(RepoRoot(), "Synergos.CMS.Web"), f), Texto: Comentarios.Replace(File.ReadAllText(f), string.Empty)))
            .ToList();
        Assert.True(vistas.Count > 100, $"Se leyeron {vistas.Count} vistas: el descubrimiento está roto.");
        return vistas;
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
