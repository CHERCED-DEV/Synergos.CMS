using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// <c>POST /api/academy/instructor/devolver</c> (lote 8, 010-P2 B, T036): sólo <c>admin</c> —un instructor ve a sus
/// alumnos, no devuelve plata—, del mismo origen, con la matrícula. Lo que hace la pieza lo prueba
/// <c>DevolucionesDeMatriculasTests</c>; aquí, la puerta de la ruta.
/// </summary>
public sealed class DevolucionesDeMatriculasControllerTests
{
    private static readonly Guid Rosa = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static DevolucionesDeMatriculasController Montar(Guid? quien, string? rol, string? sitio = "same-origin")
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        gate.HasAnyRole(Arg.Any<string?>()).Returns(c => rol is not null && (c.Arg<string?>() ?? "").Split(',').Contains(rol));

        // Sin destino del orquestador: lo que pase la puerta de la ruta llega a la pieza y se queda ahí, sin red.
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(new PuertaSettings());
        var pieza = new DevolucionesDeMatriculas(
            tabla, new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta([])),
            new ReenvioDeLaPuerta(Substitute.For<IHttpClientFactory>(), NullLogger<ReenvioDeLaPuerta>.Instance), gate, monitor,
            Substitute.For<IMatriculasDeLaPuerta>(), NullLogger<DevolucionesDeMatriculas>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Post;
        if (sitio is not null) http.Request.Headers["Sec-Fetch-Site"] = sitio;
        return new DevolucionesDeMatriculasController(pieza, gate) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static (int Estado, string Codigo) Rechazo(IActionResult r)
    {
        var c = Assert.IsType<ContentResult>(r);
        using var json = JsonDocument.Parse(c.Content!);
        return (c.StatusCode!.Value, json.RootElement.GetProperty("code").GetString()!);
    }

    private static readonly DevolucionesDeMatriculasController.DevolverMatriculaRequest Pedido = new("mat-ana-1");

    [Fact]
    public async Task Sin_sesion_es_401_y_un_instructor_o_un_alumno_es_403()
    {
        Assert.Equal((401, "puerta.sesion_requerida"), Rechazo(await Montar(null, null).Devolver(Pedido, default)));
        Assert.Equal((403, "puerta.personal_requerido"), Rechazo(await Montar(Rosa, "instructor").Devolver(Pedido, default)));
        Assert.Equal((403, "puerta.personal_requerido"), Rechazo(await Montar(Rosa, null).Devolver(Pedido, default)));
    }

    [Fact]
    public async Task Desde_otro_sitio_es_403_y_sin_matricula_es_400()
    {
        Assert.Equal((403, "puerta.origen_no_permitido"), Rechazo(await Montar(Rosa, "admin", sitio: "cross-site").Devolver(Pedido, default)));
        Assert.Equal((400, "academy.devolucion_invalida"), Rechazo(await Montar(Rosa, "admin").Devolver(new(" "), default)));
        Assert.Equal((400, "academy.devolucion_invalida"), Rechazo(await Montar(Rosa, "admin").Devolver(null, default)));
    }

    [Fact]
    public async Task Un_admin_pasa_a_la_pieza_y_sin_la_devolucion_abierta_en_el_sitio_es_503()
    {
        Assert.Equal((503, "academy.devolucion_no_disponible"), Rechazo(await Montar(Rosa, "admin").Devolver(Pedido, default)));
    }
}
