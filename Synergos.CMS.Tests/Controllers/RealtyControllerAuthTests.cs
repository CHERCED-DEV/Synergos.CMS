using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests de AUTORIZACIÓN de <see cref="RealtyController"/> (T2-Propiedades). Las 13 rutas
/// eran anónimas y la identidad venía del cliente.
/// </summary>
/// <remarks>
/// <para><b>Por qué la mitad de estos tests miran el ARGUMENTO y no el status code.</b>
/// Al escribirlos apareció un bug propio: la primera versión del arreglo añadió los guards
/// —y el 401 anónimo pasaba, verificado en vivo— pero <b>dejó los cuerpos usando
/// <c>request.User</c></b>. O sea: cerrado para el anónimo, y un member logueado seguía
/// mutando los favoritos y las búsquedas de otro poniendo su id en el body. El comentario
/// que yo mismo había escrito decía "se IGNORA el User del body" mientras el código lo
/// usaba.</para>
/// <para>Por eso <b>un test de 401 no basta</b>: pasa igual con el hueco abierto. El que
/// muerde es el que loguea a alguien, le manda el id de OTRO, y exige que el seam reciba
/// el del gate. Es la misma trampa que ADR 0108 rechazó en su día ("solo exigir member
/// logueado no cierra el IDOR").</para>
/// ADR 0075, ADR 0103, ADR 0108.
/// </remarks>
public sealed class RealtyControllerAuthTests
{
    private const string Yo = "yo@correo.co";
    private const string Otro = "victima@correo.co";

    private readonly IPropertyCatalogProvider _catalog = Substitute.For<IPropertyCatalogProvider>();
    private readonly IVisitSchedulingService _visits = Substitute.For<IVisitSchedulingService>();
    private readonly IMortgageCalculator _mortgage = Substitute.For<IMortgageCalculator>();
    private readonly ILeadCaptureService _leads = Substitute.For<ILeadCaptureService>();
    private readonly IUserCollection _collections = Substitute.For<IUserCollection>();
    private readonly ISavedSearchService _savedSearches = Substitute.For<ISavedSearchService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private RealtyController BuildSut() => new(
        _catalog, _visits, _mortgage, _leads, _collections, _savedSearches, _priceFormatter, _gate);

    private void Anonimo()
    {
        SeamsConDatos();
        _gate.IsAuthenticated.Returns(false);
    }

    /// <summary>
    /// Los seams devuelven algo REAL: sin esto el sustituto da null, el mapeo a DTO
    /// revienta con NRE y el test moriría antes de poder afirmar nada sobre el guard.
    /// </summary>
    private void SeamsConDatos()
    {
        _savedSearches.SaveAsync(Arg.Any<string>(), Arg.Any<PropertyQuery>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SavedSearch("bus-1", Yo, "trampa", new PropertyQuery(), DateTimeOffset.UnixEpoch));
        _catalog.SearchAsync(Arg.Any<PropertyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PropertySearchResult(Array.Empty<PropertyListing>(), Array.Empty<PropertyFacet>()));
    }

    /// <summary>Member normal: busca casa, pero NO es agente inmobiliario.</summary>
    private void Usuario()
    {
        SeamsConDatos();
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private void Agente()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(Yo);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private static void AssertForbidden(IActionResult result)
        => Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);

    // ══════════════ Lo del USUARIO: el body no decide quién eres ══════════════

    [Fact] // empty: anónimo no lee lo guardado — ni se toca el seam
    public async Task Saved_Anonimo_Da401_YNoTocaElSeam()
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().Saved(default));
        await _savedSearches.DidNotReceive().GetForOwnerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // EL QUE MUERDE: logueado + id ajeno en el body → se guarda para MÍ
    public async Task SaveSearch_ConUsuarioAjenoEnElBody_GuardaParaElDelGate()
    {
        Usuario();

        await BuildSut().SaveSearch(
            new RealtyController.SaveSearchRequest(User: Otro, Criteria: null, Label: "trampa"),
            default);

        await _savedSearches.Received(1).SaveAsync(Yo, Arg.Any<PropertyQuery>(), "trampa", Arg.Any<CancellationToken>());
        await _savedSearches.DidNotReceive().SaveAsync(Otro, Arg.Any<PropertyQuery>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // idem en escritura de favoritos: no se ensucia la lista ajena
    public async Task AddFavorite_ConUsuarioAjenoEnElBody_AnadeALosMios()
    {
        Usuario();

        await BuildSut().AddFavorite(
            new RealtyController.FavoriteRequest(User: Otro, ListingId: "inm-1"), default);

        await _collections.Received(1).AddAsync(Yo, Arg.Any<string>(), "inm-1", Arg.Any<CancellationToken>());
        await _collections.DidNotReceive().AddAsync(Otro, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // y en BORRADO, que es lo destructivo: no se vacía la lista ajena
    public async Task RemoveFavorite_ConUsuarioAjenoEnElBody_BorraDeLosMios()
    {
        Usuario();

        await BuildSut().RemoveFavorite(
            new RealtyController.FavoriteRequest(User: Otro, ListingId: "inm-1"), default);

        await _collections.Received(1).RemoveAsync(Yo, Arg.Any<string>(), "inm-1", Arg.Any<CancellationToken>());
        await _collections.DidNotReceive().RemoveAsync(Otro, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // el body ya no necesita `user`: mandarlo vacío es válido, no un 400
    public async Task AddFavorite_SinUsuarioEnElBody_YaNoEsBadRequest()
    {
        Usuario();

        var result = await BuildSut().AddFavorite(
            new RealtyController.FavoriteRequest(User: "", ListingId: "inm-1"), default);

        Assert.IsNotType<BadRequestObjectResult>(result);
        await _collections.Received(1).AddAsync(Yo, Arg.Any<string>(), "inm-1", Arg.Any<CancellationToken>());
    }

    // ══════════════ La consola del AGENTE: leads = datos de personas ══════════════

    [Fact] // empty
    public async Task AgentLeads_Anonimo_Da401_YNoTocaElSeam()
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().AgentLeads(default));
        await _leads.DidNotReceive().GetForAgentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: estar logueado NO es ser agente
    public async Task AgentLeads_UsuarioSinRol_Da403_YNoTocaElSeam()
    {
        Usuario();

        AssertForbidden(await BuildSut().AgentLeads(default));
        await _leads.DidNotReceive().GetForAgentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy + el segundo que muerde: el agente ve LOS SUYOS, no los del colega
    public async Task AgentLeads_Agente_PideLosDelGate()
    {
        Agente();

        await BuildSut().AgentLeads(default);

        await _leads.Received(1).GetForAgentAsync(Yo, Arg.Any<CancellationToken>());
    }

    [Fact] // publicar al catálogo del sitio pide rol, no solo sesión
    public async Task PublishListing_UsuarioSinRol_Da403()
    {
        Usuario();

        AssertForbidden(await BuildSut().PublishListing(null, default));
    }

    // ══════════════ Lo PÚBLICO sigue público (regresión) ══════════════

    [Fact] // un portal donde hay que registrarse para ver un inmueble es inservible
    public async Task Listings_Anonimo_NoPideSesion()
    {
        Anonimo();

        var result = await BuildSut().Listings(
            null, null, null, null, null, null, default);

        Assert.IsNotType<UnauthorizedObjectResult>(result);
    }
}
