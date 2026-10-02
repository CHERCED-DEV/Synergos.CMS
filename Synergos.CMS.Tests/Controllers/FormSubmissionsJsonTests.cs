using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La API de formularios contesta JSON a quien lo pide y redirige al formulario SSR (#196, tanda D).
/// </summary>
/// <remarks>
/// El formulario por pasos envía por <c>fetch</c>: un redirect a la página no le dice si el envío
/// entró, y sin eso solo podría decir «enviada» adivinando —que es el defecto que se cierra—.
/// El formulario SSR (<c>elementFormContainer</c>) no pide JSON y sigue con su PRG.
/// </remarks>
public sealed class FormSubmissionsJsonTests
{
    private readonly IFormSubmissionHandler _handler = Substitute.For<IFormSubmissionHandler>();
    private readonly IFormDefinitionReader _definiciones = Substitute.For<IFormDefinitionReader>();
    private readonly FormsSettings _ajustes = new();

    private FormSubmissionsController Controlador(string? accept, params (string Nombre, string Valor)[] campos)
    {
        var opciones = Options.Create(_ajustes);
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Headers.Referer = "/booking/reservar/";
        if (accept is not null)
        {
            http.Request.Headers.Accept = accept;
        }

        http.Request.Form = new FormCollection(campos.ToDictionary(c => c.Nombre, c => new StringValues(c.Valor)));

        return new FormSubmissionsController(
            _handler,
            new InMemoryFormRateLimiter(opciones),
            opciones,
            NullLogger<FormSubmissionsController>.Instance,
            Substitute.For<IAnalyticsTracker>(),
            Substitute.For<IFormSubmissionNotifier>(),
            _definiciones)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    [Fact]
    public async Task Un_envio_que_entra_contesta_200_con_submitted_si_se_pide_json()
    {
        _handler.SubmitAsync(Arg.Any<FormSubmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FormSubmissionResult(true));

        var respuesta = await Controlador("application/json", ("nombre", "Ana")).Submit("reserva-cita", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(respuesta);
        Assert.Contains("submitted", ok.Value!.ToString(), StringComparison.Ordinal);
        await _handler.Received(1).SubmitAsync(
            Arg.Is<FormSubmissionRequest>(r => r.FormKey == "reserva-cita" && r.Fields["nombre"] == "Ana"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_obligatorio_vacio_contesta_422_con_su_codigo_y_no_guarda_nada()
    {
        _definiciones.GetByKey("reserva-cita").Returns(new FormDefinition("reserva-cita",
            [new FormFieldDefinition("email", "Email", Required: true)]));

        var respuesta = await Controlador("application/json", ("nombre", "Ana")).Submit("reserva-cita", CancellationToken.None);

        var fallo = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, fallo.StatusCode);
        Assert.Contains("missing-required", fallo.Value!.ToString(), StringComparison.Ordinal);
        await _handler.DidNotReceive().SubmitAsync(Arg.Any<FormSubmissionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_el_almacen_falla_contesta_500_y_no_un_exito()
    {
        _handler.SubmitAsync(Arg.Any<FormSubmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FormSubmissionResult(false, ErrorCode: "storage-failed"));

        var respuesta = await Controlador("application/json", ("nombre", "Ana")).Submit("reserva-cita", CancellationToken.None);

        var fallo = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(StatusCodes.Status500InternalServerError, fallo.StatusCode);
        Assert.Contains("storage-failed", fallo.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_formulario_SSR_sigue_con_su_redirect_PRG()
    {
        _handler.SubmitAsync(Arg.Any<FormSubmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new FormSubmissionResult(true));

        var respuesta = await Controlador(accept: "text/html", ("nombre", "Ana")).Submit("contacto", CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(respuesta);
        Assert.Equal($"/booking/reservar/?{_ajustes.SuccessQueryParam}=1", redirect.Url);
    }
}
