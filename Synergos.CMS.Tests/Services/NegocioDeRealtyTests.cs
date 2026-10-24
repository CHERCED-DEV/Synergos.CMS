using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La configuración de negocio de Propiedades (ADR 0137, escala #196): la tasa con que arranca el
/// simulador de hipoteca era <c>DEFAULT_RATE = 12</c> compilada en el bundle.
/// </summary>
public sealed class NegocioDeRealtyTests
{
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");

    private static RealtyFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<RealtyFeatureSettings>()!;

    [Fact]
    public void Sin_sitio_rigen_los_valores_que_traia_el_bundle()
        => Assert.Equal(new NegocioDeRealty("/api/realty", 12m), new RealtyFeatureSettings().Para(null));

    [Fact]
    public void Un_sitio_cambia_la_tasa_y_hereda_la_API()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:DefaultRatePercent"] = "10.5" });

        Assert.Equal(new NegocioDeRealty("/api/realty", 10.5m), seccion.Para(Bogota));
    }

    [Theory]
    [InlineData("DefaultRatePercent", "120", "0 a 100")]
    [InlineData("DefaultRatePercent", "9.125", "dos decimales")]
    [InlineData("ApiBase", "//evil.example", "ApiBase")]
    public void Una_tasa_o_una_API_que_no_sirven_son_un_problema(string clave, string valor, string seDice)
    {
        var problemas = Seccion(new() { [clave] = valor }).Problemas();

        Assert.Contains(problemas, p => p.Contains(seDice, StringComparison.Ordinal));
    }

    [Fact]
    public void El_resolver_copia_lo_del_editor_y_la_configuracion_del_sitio()
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeRealty>>();
        negocio.Actual().Returns(new NegocioDeRealty("/api/realty", 10.5m));
        var resolutor = Resolutor(negocio, Caminos());

        var props = resolutor.Resolver(ElementoFalso.Con(
            ("heading", "Tu próximo hogar"), ("subheading", "En lista y en mapa"),
            // Lo que el editor podía escribir antes: ya no viaja.
            ("apiBase", "/otra/api"), ("config", """{"defaultRate":3}"""))).Props;

        Assert.Equal(new RealtyProps("Tu próximo hogar", "En lista y en mapa", "/api/realty", 10.5m, "directa", "America/Bogota"), props);
    }

    // ── El camino de la visita (008-propiedades-visita, 008-P1) ───────────────────────────────────

    [Fact]
    public void Sin_declaracion_la_visita_va_por_la_directa()
        => Assert.Equal(CaminoDelFlujo.Directa, new RealtyFeatureSettings().Para(null).VisitPath);

    [Theory]
    [InlineData("puerta", "puerta")]
    [InlineData(" Puerta ", "puerta")]
    [InlineData("directa", "directa")]
    [InlineData("", "directa")]
    public void La_funcionalidad_declara_el_camino_normalizado(string valor, string camino)
        => Assert.Equal(camino, Seccion(new() { ["VisitPath"] = valor }).Para(null).VisitPath);

    [Fact]
    public void Un_sitio_declara_la_puerta_y_los_demas_siguen_por_la_directa()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:VisitPath"] = "puerta" });

        Assert.Equal(CaminoDelFlujo.Puerta, seccion.Para(Bogota).VisitPath);
        Assert.Equal(CaminoDelFlujo.Directa, seccion.Para(Guid.NewGuid()).VisitPath);
    }

    [Fact]
    public void Un_sitio_vuelve_a_la_directa_aunque_la_funcionalidad_declare_la_puerta()
    {
        var seccion = Seccion(new() { ["VisitPath"] = "puerta", [$"Sitios:{Bogota}:VisitPath"] = "directa" });

        Assert.Equal(CaminoDelFlujo.Directa, seccion.Para(Bogota).VisitPath);
        Assert.Equal(CaminoDelFlujo.Puerta, seccion.Para(null).VisitPath);
    }

    [Fact]
    public void Un_camino_mal_escrito_es_un_problema_y_dice_la_regla()
    {
        var problemas = Seccion(new() { [$"Sitios:{Bogota}:VisitPath"] = "puerto" }).Problemas();

        Assert.Contains(problemas, p => p.Contains("VisitPath", StringComparison.Ordinal)
            && p.Contains("«puerto»", StringComparison.Ordinal) && p.Contains("directa", StringComparison.Ordinal));
    }

    [Fact]
    public void El_resolver_pregunta_por_el_flujo_de_la_visita_con_lo_que_declara_el_sitio()
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeRealty>>();
        negocio.Actual().Returns(new NegocioDeRealty("/api/realty", 12m, "puerta"));
        var caminos = Caminos();

        var props = Resolutor(negocio, caminos).Resolver(ElementoFalso.Con()).Props;

        Assert.Equal("puerta", props.VisitPath);
        caminos.Received(1).Decidir(RealtyResolutor.FlujoDeLaVisita, "puerta");
    }

    // ── FR-019: con el motor en proceso, la visita no se declara por la puerta ────────────────────

    [Fact]
    public void Con_el_motor_en_proceso_ningun_camino_por_la_puerta_deja_arrancar()
    {
        var validador = new LaVisitaPorLaPuertaPideLaAgenda();

        Assert.True(validador.Validate(null, new RealtyFeatureSettings()).Succeeded);
        Assert.True(validador.Validate(null, Seccion(new() { [$"Sitios:{Bogota}:VisitPath"] = "directa" })).Succeeded);

        var porElSitio = validador.Validate(null, Seccion(new() { [$"Sitios:{Bogota}:VisitPath"] = "puerta" }));
        Assert.True(porElSitio.Failed);
        Assert.Contains(Bogota.ToString(), porElSitio.FailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Synergos:Realty:Mode", porElSitio.FailureMessage, StringComparison.Ordinal);

        Assert.True(validador.Validate(null, Seccion(new() { ["VisitPath"] = "puerta" })).Failed);
    }

    private static RealtyResolutor Resolutor(INegocioDelSitio<NegocioDeRealty> negocio, ICaminoDeLosFlujos caminos)
        => new(ElementoFalso.Fallback, negocio, caminos, Options.Create(new ListadosSettings()), NullLogger<RealtyResolutor>.Instance);

    /// <summary>Una regla de caminos que devuelve lo que declara el sitio: la del núcleo se prueba en su sitio.</summary>
    private static ICaminoDeLosFlujos Caminos()
    {
        var caminos = Substitute.For<ICaminoDeLosFlujos>();
        caminos.Decidir(Arg.Any<string>(), Arg.Any<string?>()).Returns(c => c.ArgAt<string?>(1) ?? CaminoDelFlujo.Directa);
        return caminos;
    }
}
