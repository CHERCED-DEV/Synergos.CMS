using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Academy;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Republicar la oferta del catálogo entero de la academia (010-educacion-matricula, T026): sólo <c>admin</c> —un
/// instructor no—, y recorre todo el catálogo activo, la siembra incluida.
/// </summary>
public sealed class OfertaDeCursosControllerTests
{
    private readonly ICourseCatalogProvider _catalogo = Substitute.For<ICourseCatalogProvider>();
    private readonly IPublicadorDeOfertasDeCursos _ofertas = Substitute.For<IPublicadorDeOfertasDeCursos>();

    private OfertaDeCursosController Montar(bool sesion = true, string? rol = "admin")
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(sesion);
        gate.HasAnyRole(Arg.Any<string>()).Returns(c => rol is not null && c.Arg<string>().Split(',').Contains(rol));
        return new OfertaDeCursosController(_catalogo, _ofertas, gate, NullLogger<OfertaDeCursosController>.Instance);
    }

    private void Catalogo(params string[] ids)
    {
        _catalogo.SearchAsync(Arg.Any<CourseQuery>(), Arg.Any<CancellationToken>()).Returns(
            new CourseSearchResult(ids.Select(id => PublicadorDeOfertasDeCursosTests.Curso(id).Course).ToList(), ids.Length));
        _catalogo.GetCourseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => (CourseDetail?)PublicadorDeOfertasDeCursosTests.Curso(c.Arg<string>()));
    }

    [Fact]
    public async Task Sin_sesion_es_401_y_un_instructor_es_403_y_no_publica_nada()
    {
        Catalogo("gestion-agil");

        Assert.IsType<UnauthorizedObjectResult>(await Montar(sesion: false).Republicar(default));
        var prohibido = Assert.IsType<ObjectResult>(await Montar(rol: "instructor").Republicar(default));
        Assert.Equal(StatusCodes.Status403Forbidden, prohibido.StatusCode);
        await _ofertas.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default);
    }

    [Fact]
    public async Task Recorre_el_catalogo_entero_y_publica_cada_curso_una_vez()
    {
        Catalogo("gestion-agil", "datos-101", "intro-ia", "gestion-agil");
        _ofertas.PublicarAsync(Arg.Any<CourseDetail>(), Arg.Any<CancellationToken>()).Returns(EstadoDeLaOfertaDeCurso.Hecha);

        var r = Assert.IsType<OkObjectResult>(await Montar().Republicar(default));

        Assert.Equal(new OfertaDeCursosController.RepublicarOfertaResponse(3, 3, 0, 0), r.Value);
        await _ofertas.Received(3).PublicarAsync(Arg.Any<CourseDetail>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cuenta_lo_que_no_tiene_ficha_lo_que_falla_y_lo_que_no_tiene_destino()
    {
        Catalogo("sin-ficha", "falla", "sin-destino");
        _catalogo.GetCourseAsync("sin-ficha", Arg.Any<CancellationToken>()).Returns((CourseDetail?)null);
        _ofertas.PublicarAsync(Arg.Is<CourseDetail>(c => c.Course.Id == "falla"), Arg.Any<CancellationToken>()).Returns(EstadoDeLaOfertaDeCurso.Fallida);
        _ofertas.PublicarAsync(Arg.Is<CourseDetail>(c => c.Course.Id == "sin-destino"), Arg.Any<CancellationToken>()).Returns(EstadoDeLaOfertaDeCurso.SinDestino);

        var r = Assert.IsType<OkObjectResult>(await Montar().Republicar(default));

        Assert.Equal(new OfertaDeCursosController.RepublicarOfertaResponse(3, 0, 2, 1), r.Value);
    }
}
