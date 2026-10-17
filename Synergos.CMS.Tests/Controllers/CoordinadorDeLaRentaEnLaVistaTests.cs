using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La funcionalidad de Alquiler se pinta dentro de <c>&lt;synergos-flujo flujo="alquiler.renta"&gt;</c> (ADR 0140;
/// línea 005, T028): la clave la pone el servidor, la expone la puerta y está abierta en el sitio.
/// </summary>
/// <remarks>
/// Es el gate propio de la línea mientras <c>CoordinadorDelFlujoEnLasVistasTests</c> nombre sólo a Eventos en lo que
/// pinta (NN-12): aquél ya exige que todo flujo colocado lo exponga la puerta y esté abierto; éste, que la renta no se
/// pinte FUERA de su coordinador, que la clave sea la que pregunta el resolutor y que el aviso abra el alquiler.
/// </remarks>
public sealed class CoordinadorDeLaRentaEnLaVistaTests
{
    private static readonly Regex Coordinador = new(@"<synergos-flujo\b([^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Clave = new(@"\sflujo=""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SinLayout = new(@"\sstyle=""display:\s*contents;?""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PintaAlquiler = new(@"PartialAsync\(\s*""SynHost/Alquiler""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Comentarios = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    private const string FlujoDeLaRenta = "alquiler.renta";

    [Fact]
    public void Toda_vista_que_pinta_el_alquiler_lo_pinta_dentro_de_su_coordinador()
    {
        var vistas = Vistas()
            .Where(v => !v.Ruta.EndsWith(Path.Combine("SynHost", "Alquiler.cshtml"), StringComparison.OrdinalIgnoreCase))
            .Where(v => PintaAlquiler.IsMatch(v.Texto))
            .ToList();
        Assert.True(vistas.Count > 0, "Ninguna vista pinta SynHost/Alquiler: el descubrimiento está roto.");

        var fuera = new List<string>();
        foreach (var (ruta, texto) in vistas)
        {
            foreach (Match llamada in PintaAlquiler.Matches(texto))
            {
                var antes = texto[..llamada.Index];
                var abre = antes.LastIndexOf("<synergos-flujo", StringComparison.Ordinal);
                var cierraAntes = antes.LastIndexOf("</synergos-flujo>", StringComparison.Ordinal);
                var atributos = abre >= 0 ? Coordinador.Match(texto, abre).Groups[1].Value : string.Empty;
                var dentro = abre >= 0 && abre > cierraAntes
                    && Clave.Match(atributos).Groups[1].Value == FlujoDeLaRenta
                    && SinLayout.IsMatch(atributos)
                    && texto.IndexOf("</synergos-flujo>", llamada.Index, StringComparison.Ordinal) >= 0;
                if (!dentro) fuera.Add(ruta);
            }
        }

        Assert.True(fuera.Count == 0,
            $"Pintan la funcionalidad de Alquiler fuera de <synergos-flujo flujo=\"{FlujoDeLaRenta}\" style=\"display:contents\">: "
            + string.Join(", ", fuera) + ". Sin coordinador arriba, la renta por la puerta no encuentra a quién pedirle.");
    }

    [Fact]
    public void La_clave_es_la_del_resolutor_la_expone_la_puerta_y_esta_abierta_en_el_sitio()
    {
        Assert.Equal(FlujoDeLaRenta, AlquilerResolutor.FlujoDeLaRenta);
        Assert.NotEmpty(TablaDeLaPuerta.Incrustada().DelFlujo(FlujoDeLaRenta));
        Assert.True(Flujos().TryGetProperty(FlujoDeLaRenta, out _),
            $"{FlujoDeLaRenta} no está abierto en Synergos:Puerta:Flujos de appsettings.json.");
    }

    [Fact]
    public void El_enlace_del_aviso_abre_el_alquiler()
    {
        var ruta = Flujos().GetProperty(FlujoDeLaRenta).GetProperty("Aviso").GetProperty("Ruta").GetString();

        Assert.False(string.IsNullOrWhiteSpace(ruta), "El correo del alquiler no lleva enlace.");
        var consulta = ruta!.Contains('?', StringComparison.Ordinal) ? ruta[(ruta.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        Assert.Contains("alquiler={id}", consulta.Split('&'));
    }

    private static IReadOnlyList<(string Ruta, string Texto)> Vistas()
    {
        var vistas = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views"), "*.cshtml", SearchOption.AllDirectories)
            .Select(f => (Ruta: Path.GetRelativePath(RepoRoot(), f), Texto: Comentarios.Replace(File.ReadAllText(f), string.Empty)))
            .ToList();
        Assert.True(vistas.Count > 100, $"Se leyeron {vistas.Count} vistas: el descubrimiento está roto.");
        return vistas;
    }

    private static JsonElement Flujos()
    {
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.GetProperty("Synergos").GetProperty("Puerta").GetProperty("Flujos").Clone();
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
