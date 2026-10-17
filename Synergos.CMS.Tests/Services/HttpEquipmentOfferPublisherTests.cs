using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El CMS publica la oferta de un equipo en <c>Bff.Alquiler</c> (línea 005, research R2, T029), y un orquestador
/// caído o ausente no tumba a quien publica.
/// </summary>
/// <remarks>
/// Con un orquestador falso que graba lo que le llega: lo que se mira es el cuerpo que SALE —la tarifa, los tramos, la
/// garantía por unidad y los límites de la ficha— y que nada lance. Que esa forma sea la del contrato del orquestador
/// lo cruza <c>ConsumidorCmsDeBffAlquilerTests</c>.
/// </remarks>
public sealed class HttpEquipmentOfferPublisherTests
{
    /// <summary>Un andamio: base 40 000 al día, desde 7 días 30 000, garantía 250 000 por unidad, tres unidades.</summary>
    internal static RentalEquipment Equipo(int unidades = 3) => new(
        "andamio-6m", "Andamio 6 m", "Andamios", "Seis metros", "", null, [], unidades, 40_000m, 250_000m, 1, 30,
        [], [], [new EquipmentRate("semana", "Semana", 7, 30_000m, "")], [], "COP");

    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpResponseMessage> Responde { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"equipmentId":"andamio-6m"}""", Encoding.UTF8, "application/json"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas.Add((req.RequestUri!.AbsolutePath,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.Single() : null,
                req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct)));
            return Responde();
        }
    }

    internal sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(HttpEquipmentOfferPublisher.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-alquiler.local/") };
        }
    }

    private static HttpEquipmentOfferPublisher Publicador(HttpMessageHandler cable, bool hayDestino = true)
        => new(new Fabrica(cable), NullLogger<HttpEquipmentOfferPublisher>.Instance, hayDestino);

    [Fact]
    public async Task Publica_la_oferta_con_lo_que_dice_la_ficha()
    {
        var orquestador = new OrquestadorFalso();

        var r = await Publicador(orquestador).PublishAsync(Equipo());

        Assert.Equal(EquipmentOfferOutcome.Published, r);
        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        using var json = JsonDocument.Parse(cuerpo);
        var o = json.RootElement;
        Assert.Equal(("andamio-6m", "COP"), (o.GetProperty("equipmentId").GetString(), o.GetProperty("currency").GetString()));
        Assert.Equal((40_000m, 250_000m), (o.GetProperty("dailyRate").GetDecimal(), o.GetProperty("depositPerUnit").GetDecimal()));
        Assert.Equal((1, 30, 3), (o.GetProperty("minDays").GetInt32(), o.GetProperty("maxDays").GetInt32(), o.GetProperty("units").GetInt32()));
        var tramo = Assert.Single(o.GetProperty("rates").EnumerateArray());
        Assert.Equal((7, 30_000m), (tramo.GetProperty("minDays").GetInt32(), tramo.GetProperty("perDay").GetDecimal()));
    }

    [Fact]
    public async Task Retirar_va_por_su_ruta_sin_cuerpo_y_un_equipo_sin_unidades_se_retira()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador);

        await publicador.RetireAsync("andamio-6m");
        await publicador.PublishAsync(Equipo(unidades: 0));

        Assert.All(orquestador.Llamadas, l => Assert.Equal(("/v1/ofertas/andamio-6m/retirar", ""), (l.Ruta, l.Cuerpo)));
        Assert.Equal(2, orquestador.Llamadas.Count);
        // Una llave por publicación: dos retiros son dos llaves.
        Assert.NotEqual(orquestador.Llamadas[0].Llave, orquestador.Llamadas[1].Llave);
    }

    [Fact]
    public async Task Sin_destino_no_sale_a_la_red_y_no_falla()
    {
        var orquestador = new OrquestadorFalso();
        var publicador = Publicador(orquestador, hayDestino: false);

        Assert.Equal(EquipmentOfferOutcome.NoDestination, await publicador.PublishAsync(Equipo()));
        Assert.Equal(EquipmentOfferOutcome.NoDestination, await publicador.RetireAsync("andamio-6m"));
        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Caido_o_rechazado_devuelve_fallo_sin_lanzar()
    {
        var orquestador = new OrquestadorFalso { Responde = () => throw new HttpRequestException("Connection refused") };
        Assert.Equal(EquipmentOfferOutcome.Failed, await Publicador(orquestador).PublishAsync(Equipo()));

        orquestador.Responde = () => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"title":"Conflict","status":409,"code":"alquiler.units_change_unsupported"}""",
                Encoding.UTF8, "application/problem+json"),
        };
        Assert.Equal(EquipmentOfferOutcome.Failed, await Publicador(orquestador).PublishAsync(Equipo()));
    }
}
