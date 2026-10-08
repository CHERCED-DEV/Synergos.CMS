using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El CMS publica la oferta de un evento en <c>Bff.Eventos</c> (ADR 0140 F3), y un orquestador caído o
/// ausente no tumba a quien publica.
/// </summary>
/// <remarks>
/// Con un orquestador falso que graba lo que le llega: lo que se mira es el cuerpo que SALE —el inicio
/// del que el orquestador corta la venta, las butacas como pozos de 1— y que nada lance. Que esa forma
/// sea la del contrato del orquestador lo cruza <c>ConsumidorCmsDeBffEventosTests</c>.
/// </remarks>
public sealed class HttpEventOfferPublisherTests
{
    private static readonly DateTimeOffset Empieza = new(2026, 12, 5, 20, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>Un evento con una localidad general y otra con mapa: dos butacas libres y una vendida.</summary>
    internal static EventDetail Evento(int maxPorCompra = 4) => new(
        new EventSummary("evt-oferta", "evt-oferta", "Concierto", "Música", "Bogotá", "Teatro", Empieza, "", 50_000m, "COP", "reserved"),
        "", "",
        [
            new EventTier("GEN", "General", 50_000m, "COP", Capacity: 100, Remaining: 80, MaxPerOrder: maxPorCompra,
                SaleOpensUtc: Empieza.AddDays(-30), SaleClosesUtc: Empieza.AddDays(1)),
            new EventTier("PLATEA", "Platea", 120_000m, "COP", Capacity: 3, Remaining: 2, MaxPerOrder: 2, ZoneId: "platea"),
        ],
        new EventSeatMap("Teatro",
        [
            new EventZone("platea", "Platea", 120_000m, "COP", "PLATEA",
            [
                new EventRow("A", [new EventSeat("seat-A1", "A1", "free"), new EventSeat("seat-A2", "A2", "sold"), new EventSeat("seat-A3", "A3", "hold")]),
            ]),
        ]));

    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpResponseMessage> Responde { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"eventId":"evt-oferta","tiers":[]}""", Encoding.UTF8, "application/json"),
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
            Assert.Equal(HttpEventOfferPublisher.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
        }
    }

    private sealed class LogQueGuarda : ILogger<HttpEventOfferPublisher>
    {
        public List<(LogLevel Nivel, string Texto)> Lineas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lineas.Add((logLevel, formatter(state, exception)));
    }

    private static (HttpEventOfferPublisher Publicador, OrquestadorFalso Orquestador, LogQueGuarda Log) Montar(bool hayDestino = true)
    {
        var orquestador = new OrquestadorFalso();
        var log = new LogQueGuarda();
        return (new HttpEventOfferPublisher(new Fabrica(orquestador), log, hayDestino), orquestador, log);
    }

    private static JsonElement Localidad(string cuerpo, string codigo)
        => JsonDocument.Parse(cuerpo).RootElement.GetProperty("tiers").EnumerateArray()
            .Single(t => t.GetProperty("code").GetString() == codigo);

    [Fact]
    public async Task Manda_el_inicio_y_la_ventana_que_calculo_el_contenido_para_que_el_orquestador_corte_ahi()
    {
        var (publicador, orquestador, _) = Montar();

        Assert.Equal(EventOfferOutcome.Published, await publicador.PublishAsync(Evento()));

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        using var doc = JsonDocument.Parse(cuerpo);
        Assert.Equal("evt-oferta", doc.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("COP", doc.RootElement.GetProperty("currency").GetString());
        Assert.Equal(Empieza, doc.RootElement.GetProperty("startsAtUtc").GetDateTimeOffset());

        // Cierra al otro día del inicio: el corte en el inicio lo hace el orquestador, con este dato.
        var gen = Localidad(cuerpo, "GEN");
        Assert.Equal(Empieza.AddDays(-30), gen.GetProperty("saleOpensUtc").GetDateTimeOffset());
        Assert.Equal(Empieza.AddDays(1), gen.GetProperty("saleClosesUtc").GetDateTimeOffset());
        Assert.Equal(50_000m, gen.GetProperty("price").GetDecimal());
        Assert.Equal(4, gen.GetProperty("maxPerOrder").GetInt32());
    }

    [Fact]
    public async Task Una_localidad_con_mapa_sale_como_sus_butacas_libres_y_una_general_con_lo_que_ofrece()
    {
        var (publicador, orquestador, _) = Montar();

        await publicador.PublishAsync(Evento());

        var cuerpo = Assert.Single(orquestador.Llamadas).Cuerpo;
        var platea = Localidad(cuerpo, "PLATEA");
        Assert.Equal(["seat-A1", "seat-A3"], platea.GetProperty("seats").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(2, platea.GetProperty("capacity").GetInt32());

        var gen = Localidad(cuerpo, "GEN");
        Assert.Equal(JsonValueKind.Null, gen.GetProperty("seats").ValueKind);
        Assert.Equal(80, gen.GetProperty("capacity").GetInt32());
    }

    [Fact]
    public async Task Un_tope_en_cero_sale_como_ausente_y_no_como_un_tope_que_el_orquestador_rechaza()
    {
        var (publicador, orquestador, _) = Montar();

        await publicador.PublishAsync(Evento(maxPorCompra: 0));

        Assert.Equal(JsonValueKind.Null, Localidad(Assert.Single(orquestador.Llamadas).Cuerpo, "GEN").GetProperty("maxPerOrder").ValueKind);
    }

    [Fact]
    public async Task Cada_publicacion_lleva_su_propia_llave()
    {
        var (publicador, orquestador, _) = Montar();

        await publicador.PublishAsync(Evento());
        await publicador.PublishAsync(Evento());

        Assert.Equal(2, orquestador.Llamadas.Select(l => l.Llave).Distinct().Count());
    }

    [Fact]
    public async Task Un_rechazo_del_orquestador_no_lanza_y_queda_en_el_log_con_su_codigo()
    {
        var (publicador, orquestador, log) = Montar();
        orquestador.Responde = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"code":"inventory.unreachable","detail":"caída","transient":true}""",
                Encoding.UTF8, "application/problem+json"),
        };

        Assert.Equal(EventOfferOutcome.Failed, await publicador.PublishAsync(Evento()));
        Assert.Contains(log.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("inventory.unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_orquestador_que_no_responde_no_lanza()
    {
        var (publicador, orquestador, log) = Montar();
        orquestador.Responde = () => throw new HttpRequestException("Connection refused");

        Assert.Equal(EventOfferOutcome.Failed, await publicador.PublishAsync(Evento()));
        Assert.Contains(log.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("evt-oferta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_destino_no_intenta_nada()
    {
        var (publicador, orquestador, _) = Montar(hayDestino: false);

        Assert.Equal(EventOfferOutcome.NoDestination, await publicador.PublishAsync(Evento()));
        Assert.Empty(orquestador.Llamadas);
    }
}
