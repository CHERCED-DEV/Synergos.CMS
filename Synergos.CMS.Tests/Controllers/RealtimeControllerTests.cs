using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests de AUTORIZACIÓN POR CANAL de <see cref="RealtimeController"/> (T7).
/// </summary>
/// <remarks>
/// Es lo que impide que el realtime sea una puerta trasera a lo que las olas anteriores
/// cerraron: un flujo que empuja el feed de check-in de un evento debe pedir el mismo
/// permiso que la consola que lo muestra. Y el <b>default es DENEGAR</b>: un canal que
/// nadie declaró no se puede leer, así que olvidar la política deja cerrado, no abierto.
/// ADR 0075, ADR 0111.
/// </remarks>
public sealed class RealtimeControllerTests
{
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly SseRealtimeHub _hub = new(NullLogger<SseRealtimeHub>.Instance);

    private RealtimeController BuildSut() =>
        new(_hub, _gate, NullLogger<RealtimeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private const string CheckinChannel = RealtimeController.EventosCheckinPrefix + "evt-1";

    /// <summary>El stream escribe en la respuesta: se corta ya para no bloquear el test.</summary>
    private static System.Threading.CancellationToken Cancelled()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    [Fact] // empty: sin canal no hay nada que abrir
    public async Task Stream_NoChannel_Returns400()
    {
        var sut = BuildSut();

        await sut.Stream(null, Cancelled());

        Assert.Equal(StatusCodes.Status400BadRequest, sut.Response.StatusCode);
    }

    [Fact] // El feed de check-in dice quién entra al evento: anónimo NO
    public async Task Stream_CheckinChannel_Anonymous_Returns401_AndDoesNotSubscribe()
    {
        _gate.IsAuthenticated.Returns(false);
        var sut = BuildSut();

        await sut.Stream(CheckinChannel, Cancelled());

        Assert.Equal(StatusCodes.Status401Unauthorized, sut.Response.StatusCode);
        Assert.Equal(0, _hub.SubscriberCount(CheckinChannel));
    }

    [Fact] // filter: logueado SIN rol de organizador tampoco — comprar no te hace staff
    public async Task Stream_CheckinChannel_WithoutRole_Returns403_AndDoesNotSubscribe()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
        var sut = BuildSut();

        await sut.Stream(CheckinChannel, Cancelled());

        Assert.Equal(StatusCodes.Status403Forbidden, sut.Response.StatusCode);
        Assert.Equal(0, _hub.SubscriberCount(CheckinChannel));
    }

    [Fact] // EL DEFAULT ES DENEGAR: un canal sin política declarada no se abre
    public async Task Stream_UnknownChannel_IsDeniedEvenForOrganizer()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true); // organizador de pleno derecho
        var sut = BuildSut();

        await sut.Stream("algo:que:nadie:declaro", Cancelled());

        Assert.Equal(StatusCodes.Status403Forbidden, sut.Response.StatusCode);
        Assert.Equal(0, _hub.SubscriberCount("algo:que:nadie:declaro"));
    }

    [Fact] // …y tampoco se cuela un canal que solo SE PAREZCA al permitido
    public async Task Stream_LookalikeChannel_IsDenied()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        var sut = BuildSut();

        // Sin los dos puntos finales del prefijo declarado.
        await sut.Stream("eventos:checkin", Cancelled());

        Assert.Equal(StatusCodes.Status403Forbidden, sut.Response.StatusCode);
    }

    [Fact] // happy: el organizador SÍ abre el flujo, con el content-type de SSE
    public async Task Stream_CheckinChannel_Organizer_OpensStream()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        var sut = BuildSut();

        await sut.Stream(CheckinChannel, Cancelled());

        Assert.Equal("text/event-stream", sut.Response.ContentType);
        // Y se desuscribió al terminar: no quedan suscriptores colgados.
        Assert.Equal(0, _hub.SubscriberCount(CheckinChannel));
    }

    [Fact] // El rol que se exige es el de ORGANIZADOR, no uno cualquiera
    public async Task Stream_CheckinChannel_ChecksOrganizerRole()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);

        await BuildSut().Stream(CheckinChannel, Cancelled());

        _gate.Received().HasAnyRole(Arg.Is<string?>(csv => csv != null && csv.Contains("organizador")));
    }
}
