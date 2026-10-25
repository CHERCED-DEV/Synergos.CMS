using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El portal de viajes y el asistente de hotel se pintan dentro de <c>&lt;synergos-flujo flujo="viajes.reserva"&gt;</c>
/// (ADR 0140; línea 006, T016): la clave la pone el servidor, la expone la puerta y está abierta en el sitio.
/// </summary>
/// <remarks>
/// Es el gate propio de la línea mientras <c>CoordinadorDelFlujoEnLasVistasTests</c> nombre sólo a Eventos en lo que pinta
/// (NN-12), como el de Alquiler: aquél exige que todo flujo colocado lo exponga la puerta y esté abierto; éste, que
/// ninguno de los dos elementos se pinte FUERA de su coordinador, que la clave sea la que pregunta el resolutor y que el
/// aviso abra el viaje.
/// </remarks>
public sealed class CoordinadorDeLaReservaDeViajeEnLasVistasTests
{
    private static readonly Regex Coordinador = new(@"<synergos-flujo\b([^>]*)>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Clave = new(@"\sflujo=""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SinLayout = new(@"\sstyle=""display:\s*contents;?""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Comentarios = new(@"@\*.*?\*@|<!--.*?-->", RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));

    private const string FlujoDeLaReserva = "viajes.reserva";

    [Theory]
    [InlineData("TravelShell")]
    [InlineData("BookingWizard")]
    public void Toda_vista_que_pinta_el_elemento_lo_pinta_dentro_de_su_coordinador(string elemento)
    {
        var pinta = new Regex($@"PartialAsync\(\s*""SynHost/{elemento}""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var vistas = Vistas()
            .Where(v => !v.Ruta.EndsWith(Path.Combine("SynHost", elemento + ".cshtml"), StringComparison.OrdinalIgnoreCase))
            .Where(v => pinta.IsMatch(v.Texto))
            .ToList();
        Assert.True(vistas.Count > 0, $"Ninguna vista pinta SynHost/{elemento}: el descubrimiento está roto.");

        var fuera = new List<string>();
        foreach (var (ruta, texto) in vistas)
        {
            foreach (Match llamada in pinta.Matches(texto))
            {
                var antes = texto[..llamada.Index];
                var abre = antes.LastIndexOf("<synergos-flujo", StringComparison.Ordinal);
                var cierraAntes = antes.LastIndexOf("</synergos-flujo>", StringComparison.Ordinal);
                var atributos = abre >= 0 ? Coordinador.Match(texto, abre).Groups[1].Value : string.Empty;
                var dentro = abre >= 0 && abre > cierraAntes
                    && Clave.Match(atributos).Groups[1].Value == FlujoDeLaReserva
                    && SinLayout.IsMatch(atributos)
                    && texto.IndexOf("</synergos-flujo>", llamada.Index, StringComparison.Ordinal) >= 0;
                if (!dentro) fuera.Add(ruta);
            }
        }

        Assert.True(fuera.Count == 0,
            $"Pintan {elemento} fuera de <synergos-flujo flujo=\"{FlujoDeLaReserva}\" style=\"display:contents\">: "
            + string.Join(", ", fuera) + ". Sin coordinador arriba, la reserva por la puerta no encuentra a quién pedirle.");
    }

    [Fact]
    public void La_clave_es_la_del_resolutor_la_expone_la_puerta_y_esta_abierta_en_el_sitio()
    {
        Assert.Equal(FlujoDeLaReserva, TravelShellResolutor.FlujoDeLaReserva);
        Assert.NotEmpty(TablaDeLaPuerta.Incrustada().DelFlujo(FlujoDeLaReserva));
        Assert.True(Flujos().TryGetProperty(FlujoDeLaReserva, out _),
            $"{FlujoDeLaReserva} no está abierto en Synergos:Puerta:Flujos de appsettings.json.");
    }

    [Fact]
    public void El_enlace_del_aviso_abre_el_viaje()
    {
        var ruta = Flujos().GetProperty(FlujoDeLaReserva).GetProperty("Aviso").GetProperty("Ruta").GetString();

        Assert.False(string.IsNullOrWhiteSpace(ruta), "El correo de la reserva no lleva enlace.");
        var consulta = ruta!.Contains('?', StringComparison.Ordinal) ? ruta[(ruta.IndexOf('?', StringComparison.Ordinal) + 1)..] : string.Empty;
        Assert.Contains("viaje={id}", consulta.Split('&'));
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
