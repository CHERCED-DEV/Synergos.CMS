using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El CMS publica la oferta de un producto en <c>Bff.Tienda</c> (004-tienda-compra, R-9), y un orquestador caído
/// o ausente no tumba a quien publica.
/// </summary>
/// <remarks>
/// Con un orquestador falso que graba lo que le llega: lo que se mira es el cuerpo que SALE. Que esa forma sea
/// la del contrato del orquestador lo cruza <c>ConsumidorCmsDeBffTiendaTests</c>.
/// </remarks>
public sealed class HttpTiendaOfferPublisherTests
{
    /// <summary>Un producto sin variantes, como los del contenido.</summary>
    internal static ProductDetail Taza(int existencias = 5) => new(
        new CatalogProductSummary("TAZA-001", "Taza", 20_000m, "COP", "Casa", "Hogar", null, 0d, 0, existencias),
        "", [], [], [], []);

    /// <summary>Un producto con variantes, como los de la siembra; la verde está en otra moneda.</summary>
    internal static ProductDetail Camiseta() => new(
        new CatalogProductSummary("CAMI-001", "Camiseta", 35_000m, "COP", "Casa", "Ropa", null, 0d, 0, 9),
        "", [],
        [
            new ProductVariant("azul", "Azul", 35_000m, "COP", 3),
            new ProductVariant("rojo", "Rojo", 30_000m, "cop", -2),
            new ProductVariant("verde", "Verde", 9m, "USD", 4),
        ],
        [], []);

    internal sealed class OrquestadorFalso : HttpMessageHandler
    {
        public List<(string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpResponseMessage> Responde { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"productId":"TAZA-001","variants":[]}""", Encoding.UTF8, "application/json"),
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
            Assert.Equal(HttpTiendaOfferPublisher.ClientName, name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-tienda.local/") };
        }
    }

    private sealed class LogQueGuarda : ILogger<HttpTiendaOfferPublisher>
    {
        public List<(LogLevel Nivel, string Texto)> Lineas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lineas.Add((logLevel, formatter(state, exception)));
    }

    private static (HttpTiendaOfferPublisher Publicador, OrquestadorFalso Orquestador, LogQueGuarda Log) Montar(bool hayDestino = true)
    {
        var orquestador = new OrquestadorFalso();
        var log = new LogQueGuarda();
        return (new HttpTiendaOfferPublisher(new Fabrica(orquestador), log, hayDestino), orquestador, log);
    }

    private static List<JsonElement> Variantes(string cuerpo)
        => JsonDocument.Parse(cuerpo).RootElement.GetProperty("variants").EnumerateArray().Select(v => v.Clone()).ToList();

    [Fact]
    public async Task Un_producto_sin_variantes_sale_como_una_sola_con_su_precio_y_sus_existencias()
    {
        var (publicador, orquestador, _) = Montar();

        Assert.Equal(TiendaOfferOutcome.Published, await publicador.PublishAsync(Taza()));

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas", ruta);
        Assert.False(string.IsNullOrWhiteSpace(llave));
        using var doc = JsonDocument.Parse(cuerpo);
        Assert.Equal("TAZA-001", doc.RootElement.GetProperty("productId").GetString());
        Assert.Equal("COP", doc.RootElement.GetProperty("currency").GetString());
        var unica = Assert.Single(Variantes(cuerpo));
        Assert.Equal(JsonValueKind.Null, unica.GetProperty("variantId").ValueKind);
        Assert.Equal(20_000m, unica.GetProperty("price").GetDecimal());
        Assert.Equal(5, unica.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Con_variantes_sale_cada_una_y_la_de_otra_moneda_no_entra_y_queda_en_el_log()
    {
        var (publicador, orquestador, log) = Montar();

        await publicador.PublishAsync(Camiseta());

        var variantes = Variantes(Assert.Single(orquestador.Llamadas).Cuerpo);
        Assert.Equal(["azul", "rojo"], variantes.Select(v => v.GetProperty("variantId").GetString()));
        Assert.Equal(30_000m, variantes[1].GetProperty("price").GetDecimal());
        // Unas existencias negativas son «no hay», no un error que tumbe la oferta entera.
        Assert.Equal(0, variantes[1].GetProperty("stock").GetInt32());
        Assert.Contains(log.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("verde", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_nada_que_vender_en_su_moneda_retira_la_oferta_en_vez_de_mandar_una_vacia()
    {
        var (publicador, orquestador, _) = Montar();
        var soloDolares = Camiseta() with { Variants = [new ProductVariant("verde", "Verde", 9m, "USD", 4)] };

        await publicador.PublishAsync(soloDolares);

        Assert.Equal("/v1/ofertas/CAMI-001/retirar", Assert.Single(orquestador.Llamadas).Ruta);
    }

    [Fact]
    public async Task Cada_publicacion_lleva_su_propia_llave()
    {
        var (publicador, orquestador, _) = Montar();

        await publicador.PublishAsync(Taza());
        await publicador.PublishAsync(Taza());

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

        Assert.Equal(TiendaOfferOutcome.Failed, await publicador.PublishAsync(Taza()));
        Assert.Contains(log.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("inventory.unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_orquestador_que_no_responde_no_lanza_al_publicar_ni_al_retirar()
    {
        var (publicador, orquestador, log) = Montar();
        orquestador.Responde = () => throw new HttpRequestException("Connection refused");

        Assert.Equal(TiendaOfferOutcome.Failed, await publicador.PublishAsync(Taza()));
        Assert.Equal(TiendaOfferOutcome.Failed, await publicador.RetireAsync("TAZA-001"));
        Assert.Contains(log.Lineas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("retirar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sin_destino_no_intenta_nada()
    {
        var (publicador, orquestador, _) = Montar(hayDestino: false);

        Assert.Equal(TiendaOfferOutcome.NoDestination, await publicador.PublishAsync(Taza()));
        Assert.Equal(TiendaOfferOutcome.NoDestination, await publicador.RetireAsync("TAZA-001"));
        Assert.Empty(orquestador.Llamadas);
    }

    [Fact]
    public async Task Retirar_manda_el_retiro_del_producto_con_su_propia_llave_y_sin_cuerpo()
    {
        var (publicador, orquestador, _) = Montar();

        Assert.Equal(TiendaOfferOutcome.Published, await publicador.RetireAsync("TAZA-001"));
        await publicador.RetireAsync("TAZA-001");

        Assert.All(orquestador.Llamadas, l => Assert.Equal("/v1/ofertas/TAZA-001/retirar", l.Ruta));
        Assert.All(orquestador.Llamadas, l => Assert.Equal("", l.Cuerpo));
        Assert.Equal(2, orquestador.Llamadas.Select(l => l.Llave).Distinct().Count());
    }
}
