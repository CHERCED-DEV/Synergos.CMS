using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// El camino de la radicación que el CMS declara a <c>&lt;synergos-gov&gt;</c> (009-gobierno-tramite T030 y T033): lo que
/// declara el sitio gana; sin declaración, la entrada de la puerta del <c>appsettings.json</c> base («directa»); y la
/// puerta declarada sin orquestador es «no-disponible», nunca la directa a escondidas.
/// </summary>
/// <remarks>
/// La puerta es la de verdad, sobre la configuración del repo y con o sin destino para «Gobierno»: así el defecto
/// «directa» que pone la entrada del flujo se prueba donde vive, y quitarlo es rojo aquí.
/// </remarks>
public sealed class GovResolutorTests
{
    private static readonly Guid Bogota = Guid.Parse("7c2e9a41-3b5d-4f8e-9a1c-6d4b2e8f0a35");

    private static string Camino(string? delSitio, bool conDestino)
    {
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(PuertaDelRepo());
        var puerta = new PuertaDeLosFlujos(TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(conDestino ? ["Gobierno"] : []));
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeGov>>();
        negocio.Actual().Returns(new NegocioDeGov("/api/gov", delSitio));

        return new GovResolutor(
                ElementoFalso.Fallback, negocio, new CaminoDeLosFlujos(puerta, monitor, NullLogger<CaminoDeLosFlujos>.Instance),
                puerta, NullLogger<GovResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Trámites en línea"))).Props.Camino;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sin_declaracion_del_sitio_la_radicacion_va_por_la_directa_abra_o_no_la_puerta(bool conDestino)
        => Assert.Equal(CaminoDelFlujo.Directa, Camino(null, conDestino));

    [Fact]
    public void El_sitio_que_declara_la_puerta_la_tiene_cuando_la_puerta_abre()
        => Assert.Equal(CaminoDelFlujo.Puerta, Camino(CaminoDelFlujo.Puerta, conDestino: true));

    [Fact]
    public void El_sitio_que_declara_la_puerta_sin_orquestador_queda_no_disponible_y_no_cae_a_la_directa()
        => Assert.Equal(GovResolutor.NoDisponible, Camino(CaminoDelFlujo.Puerta, conDestino: false));

    [Fact]
    public void El_sitio_que_declara_la_directa_la_tiene_aunque_la_puerta_abra()
        => Assert.Equal(CaminoDelFlujo.Directa, Camino(CaminoDelFlujo.Directa, conDestino: true));

    [Fact]
    public void La_vista_del_bloque_coloca_el_mismo_flujo_por_el_que_pregunta_el_resolver()
    {
        var vista = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Views", "Partials", "blockgrid", "Components", "elementSynGov.cshtml"));

        Assert.Contains($"<synergos-flujo flujo=\"{GovResolutor.FlujoDeLaRadicacion}\"", vista, StringComparison.Ordinal);
    }

    // ── La declaración del sitio (Synergos:Features:Gov:FilingPath) ─────────

    private static GovFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<GovFeatureSettings>()!;

    [Fact]
    public void Sin_declaracion_la_seccion_no_dice_camino_y_deja_decidir_a_la_puerta()
        => Assert.Equal(new NegocioDeGov("/api/gov", null), new GovFeatureSettings().Para(null));

    [Theory]
    [InlineData("puerta", "puerta")]
    [InlineData(" Puerta ", "puerta")]
    [InlineData("directa", "directa")]
    [InlineData("", null)]
    public void La_funcionalidad_declara_el_camino_normalizado(string valor, string? camino)
        => Assert.Equal(camino, Seccion(new() { ["FilingPath"] = valor }).Para(null).FilingPath);

    [Fact]
    public void Un_sitio_declara_la_puerta_y_los_demas_no_declaran_nada()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:FilingPath"] = "puerta" });

        Assert.Equal(CaminoDelFlujo.Puerta, seccion.Para(Bogota).FilingPath);
        Assert.Null(seccion.Para(Guid.NewGuid()).FilingPath);
    }

    [Fact]
    public void Un_camino_mal_escrito_es_un_problema_y_dice_la_regla()
    {
        var problemas = Seccion(new() { [$"Sitios:{Bogota}:FilingPath"] = "puerto" }).Problemas();

        Assert.Contains(problemas, p => p.Contains("FilingPath", StringComparison.Ordinal)
            && p.Contains("«puerto»", StringComparison.Ordinal) && p.Contains("directa", StringComparison.Ordinal));
    }

    /// <summary><c>Synergos:Puerta</c> del <c>appsettings.json</c> base, enlazada como la enlaza el CMS.</summary>
    private static PuertaSettings PuertaDelRepo()
        => new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection("Synergos:Puerta")
            .Get<PuertaSettings>()!;

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
