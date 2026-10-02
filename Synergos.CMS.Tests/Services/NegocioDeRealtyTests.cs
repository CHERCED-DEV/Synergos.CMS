using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
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
        var resolutor = new RealtyResolutor(ElementoFalso.Fallback, negocio, NullLogger<RealtyResolutor>.Instance);

        var props = resolutor.Resolver(ElementoFalso.Con(
            ("heading", "Tu próximo hogar"), ("subheading", "En lista y en mapa"),
            // Lo que el editor podía escribir antes: ya no viaja.
            ("apiBase", "/otra/api"), ("config", """{"defaultRate":3}"""))).Props;

        Assert.Equal(new RealtyProps("Tu próximo hogar", "En lista y en mapa", "/api/realty", 10.5m), props);
    }
}
