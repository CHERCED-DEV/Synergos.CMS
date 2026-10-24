using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cancelar lo confirmado de un alquiler de la puerta tiene dos caminos y el CMS elige (línea 005, T044; NN-8,
/// aclaración D-005-NN8): el inquilino por el flujo posterior <c>alquiler.cancelacion</c> o por la liquidación propia,
/// según el interruptor; el personal, siempre por la propia.
/// </summary>
/// <remarks>
/// Con un orquestador falso que graba lo que le llega y contesta lo que el test le diga. Que lo que sale quepa en el
/// contrato del orquestador lo cruza <c>ConsumidorCmsDeBffAlquilerTests</c>; que el interruptor llegue hasta aquí al
/// componer, <c>InterruptorDeLaCancelacionTests</c>.
/// </remarks>
public sealed class HttpAlquilerDeLaPuertaTests
{
    private const string Alquiler = "pta-1";

    private sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string? Sujeto, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Responde { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas.Add((req.RequestUri!.AbsolutePath,
                req.Headers.TryGetValues("Idempotency-Key", out var llave) ? llave.Single() : null,
                req.Headers.TryGetValues("X-Synergos-Sujeto", out var sujeto) ? sujeto.Single() : null,
                req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct)));
            return Responde(req);
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpAlquilerDeLaPuerta.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-alquiler.local/") };
        }
    }

    private static HttpAlquilerDeLaPuerta Puerta(OrquestadorFalso cable, bool cancelaPorElFlujo)
    {
        var ajustes = Substitute.For<IOptionsMonitor<AlquilerSettings>>();
        ajustes.CurrentValue.Returns(new AlquilerSettings());
        return new HttpAlquilerDeLaPuerta(new Fabrica(cable), ajustes, NullLogger<HttpAlquilerDeLaPuerta>.Instance,
            hayOrquestador: true, cancelaPorElFlujo);
    }

    private static RentalAgreement Contrato() => new(
        Alquiler, "u-1", "andamio-6m", "Andamio", 2, new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 17),
        532_000m, 600_000m, "COP", new DateTimeOffset(2026, 10, 17, 13, 50, 0, TimeSpan.Zero), "sello", CaminoDeCompra.Puerta);

    private static HttpResponseMessage Json(HttpStatusCode estado, string json)
        => new(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Cancelacion(string estado = "Completed", string alquiler = Alquiler)
        => Json(HttpStatusCode.Created, JsonSerializer.Serialize(new
        {
            id = $"cancelar-{Alquiler}",
            flujo = "alquiler.cancelacion",
            numero = "ANCELAR1",
            estado,
            datos = new { alquiler, total = new { amount = 532_000m, currency = "COP" } },
            compensacionesPendientes = 0,
            error = (string?)null,
            deshaciendo = false,
        }));

    [Fact]
    public async Task El_inquilino_cancela_por_el_flujo_con_la_llave_del_alquiler_y_queda_cancelado_sin_nada_retenido()
    {
        var cable = new OrquestadorFalso { Responde = _ => Cancelacion() };

        var r = await Puerta(cable, cancelaPorElFlujo: true).CancelAsync(Contrato(), RentalCanceller.Renter, 99_000m, null, "llave-del-navegador");

        Assert.Equal(RentalOutcome.Ok, r!.Outcome);
        Assert.Equal(RentalState.Cancelled, r.Rental!.State);
        Assert.Equal(0m, r.Rental.DepositHeld);
        Assert.Equal(0m, r.Rental.DamageCharged);
        var (ruta, llave, sujeto, cuerpo) = Assert.Single(cable.Llamadas);
        Assert.Equal("/v1/flujos/alquiler.cancelacion", ruta);
        Assert.Equal($"cancelar-{Alquiler}", llave);
        Assert.Equal("alquiler.arrendatario:u-1", sujeto);
        Assert.Equal(Alquiler, JsonDocument.Parse(cuerpo).RootElement.GetProperty("alquiler").GetString());
        Assert.Single(JsonDocument.Parse(cuerpo).RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("Compensated", Alquiler)]
    [InlineData("Running", Alquiler)]
    [InlineData("Completed", "pta-otro")]
    public async Task Una_respuesta_que_no_es_la_cancelacion_completada_de_ese_alquiler_no_se_da_por_cancelada(string estado, string alquiler)
    {
        var cable = new OrquestadorFalso { Responde = _ => Cancelacion(estado, alquiler) };

        var r = await Puerta(cable, cancelaPorElFlujo: true).CancelAsync(Contrato(), RentalCanceller.Renter, 0m, null, "llave");

        Assert.Equal(RentalOutcome.Unavailable, r!.Outcome);
        Assert.Null(r.Rental);
    }

    [Fact]
    public async Task El_rechazo_del_flujo_sube_con_su_codigo_y_un_alquiler_que_no_encuentra_no_existe()
    {
        var cable = new OrquestadorFalso
        {
            Responde = _ => Json(HttpStatusCode.Conflict,
                """{"code":"alquiler.already_started","detail":"ya empezó","transient":false}"""),
        };

        var rechazada = await Puerta(cable, cancelaPorElFlujo: true).CancelAsync(Contrato(), RentalCanceller.Renter, 0m, null, "llave");
        Assert.Equal(RentalOutcome.Rejected, rechazada!.Outcome);
        Assert.Equal("alquiler.already_started", rechazada.RejectionCode);

        cable.Responde = _ => Json(HttpStatusCode.NotFound, """{"code":"flow.original_no_encontrada"}""");
        Assert.Null(await Puerta(cable, cancelaPorElFlujo: true).CancelAsync(Contrato(), RentalCanceller.Renter, 0m, null, "llave"));
    }

    [Theory]
    [InlineData(false, RentalCanceller.Renter)]
    [InlineData(true, RentalCanceller.Staff)]
    [InlineData(false, RentalCanceller.Staff)]
    public async Task Con_el_interruptor_en_Propia_o_si_cancela_el_personal_va_a_la_liquidacion_propia(
        bool cancelaPorElFlujo, RentalCanceller quien)
    {
        var cable = new OrquestadorFalso();

        await Puerta(cable, cancelaPorElFlujo).CancelAsync(Contrato(), quien, 50_000m, "staff-1", "llave-del-navegador");

        var (ruta, llave, _, _) = Assert.Single(cable.Llamadas);
        Assert.Equal($"/v1/alquileres/{Alquiler}/cancelar-confirmado", ruta);
        Assert.Equal("llave-del-navegador", llave);
    }
}
