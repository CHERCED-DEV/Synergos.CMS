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
/// La configuración de negocio de la academia y el camino de la matrícula (010-educacion-matricula T012): «directa» por
/// defecto, como 006-P5 y 008-P1, y «puerta» sólo donde un sitio la declara.
/// </summary>
public sealed class NegocioDeAcademyTests
{
    private static readonly Guid Bogota = Guid.Parse("4f1c7a52-9d3e-4b8a-a6c1-2e5d7f9b0c13");

    private static AcademyFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<AcademyFeatureSettings>()!;

    [Fact]
    public void Sin_declaracion_la_matricula_va_por_la_directa_con_la_api_de_siempre()
        => Assert.Equal(new NegocioDeAcademy("/api/academy", CaminoDelFlujo.Directa), new AcademyFeatureSettings().Para(null));

    [Theory]
    [InlineData("puerta", "puerta")]
    [InlineData(" Puerta ", "puerta")]
    [InlineData("directa", "directa")]
    [InlineData("", "directa")]
    public void La_funcionalidad_declara_el_camino_normalizado(string valor, string camino)
        => Assert.Equal(camino, Seccion(new() { ["EnrollmentPath"] = valor }).Para(null).EnrollmentPath);

    [Fact]
    public void Un_sitio_declara_la_puerta_y_los_demas_siguen_por_la_directa()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:EnrollmentPath"] = "puerta" });

        Assert.Equal(CaminoDelFlujo.Puerta, seccion.Para(Bogota).EnrollmentPath);
        Assert.Equal(CaminoDelFlujo.Directa, seccion.Para(Guid.NewGuid()).EnrollmentPath);
    }

    [Fact]
    public void Un_camino_mal_escrito_es_un_problema_y_dice_la_regla()
    {
        var problemas = Seccion(new() { [$"Sitios:{Bogota}:EnrollmentPath"] = "puerto" }).Problemas();

        Assert.Contains(problemas, p => p.Contains("EnrollmentPath", StringComparison.Ordinal)
            && p.Contains("«puerto»", StringComparison.Ordinal) && p.Contains("directa", StringComparison.Ordinal));
    }

    [Fact]
    public void El_resolver_pregunta_por_el_flujo_de_la_matricula_con_lo_que_declara_el_sitio()
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeAcademy>>();
        negocio.Actual().Returns(new NegocioDeAcademy("/api/academy", "puerta"));
        var caminos = Substitute.For<ICaminoDeLosFlujos>();
        caminos.Decidir(Arg.Any<string>(), Arg.Any<string?>()).Returns(c => c.ArgAt<string?>(1) ?? CaminoDelFlujo.Directa);

        var props = new AcademyResolutor(ElementoFalso.Fallback, negocio, caminos, NullLogger<AcademyResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Aprende con nosotros"), ("apiBase", "/otra/api"))).Props;

        Assert.Equal(new AcademyProps("Aprende con nosotros", null, "/api/academy", "puerta"), props);
        caminos.Received(1).Decidir(AcademyResolutor.FlujoDeLaMatricula, "puerta");
    }
}
