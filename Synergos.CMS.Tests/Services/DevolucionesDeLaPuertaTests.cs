using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La historia 7 en el CMS (004-tienda-compra, T040/T041): una devolución de una compra de la PUERTA que llega a
/// «reembolsada» devuelve la plata por el orquestador —el flujo posterior <c>tienda.devolucion</c> del personal, por
/// el reenvío de la puerta—, y una de la ruta directa, por el proveedor del CMS como siempre.
/// </summary>
public sealed class DevolucionesDeLaPuertaTests
{
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Rosa = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Tomas = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private const string Compra = "pta-compra-1";

    private static CompraDeLaPuerta DeAna() => new(
        Compra, "COMPRA01", Ana, "Ana", "ana@ejemplo.co",
        [new LineaDeLaCompra("p-1", null, "Taza", 2, 20_000m), new LineaDeLaCompra("p-2", "azul", "Camiseta — Azul", 1, 35_000m)],
        75_000m, "COP");

    // ── La pieza ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_personal_devuelve_por_la_puerta_con_su_sujeto_y_la_llave_de_la_devolucion()
    {
        var orquestador = new Orquestador();
        var pieza = Pieza(orquestador, Personal(Rosa));

        var r = await pieza.DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal(40_000m, r.Devuelto);
        var (metodo, ruta, cabeceras, cuerpo) = Assert.Single(orquestador.Pedidos);
        Assert.Equal(("POST", "/v1/flujos/tienda.devolucion"), (metodo, ruta));
        Assert.Equal($"tienda.personal:{Rosa:n}", cabeceras["X-Synergos-Sujeto"]);
        Assert.Equal(DevolucionesDeLaPuerta.Llave(Compra, "RMA rma-1"), cabeceras["Idempotency-Key"]);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(Compra, json.RootElement.GetProperty("compra").GetString());
        Assert.Equal(40_000m, json.RootElement.GetProperty("monto").GetProperty("amount").GetDecimal());
        Assert.Equal("COP", json.RootElement.GetProperty("monto").GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Otra_persona_que_reintenta_la_misma_devolucion_manda_la_misma_llave()
    {
        var orquestador = new Orquestador();

        await Pieza(orquestador, Personal(Rosa)).DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);
        await Pieza(orquestador, Personal(Tomas)).DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);
        await Pieza(orquestador, Personal(Tomas)).DevolverAsync(Compra, 40_000m, "COP", "RMA rma-2", CancellationToken.None);

        var llaves = orquestador.Pedidos.Select(p => p.Cabeceras["Idempotency-Key"]).ToList();
        Assert.Equal(llaves[0], llaves[1]);
        Assert.NotEqual(llaves[0], llaves[2]);
        Assert.NotEqual(orquestador.Pedidos[0].Cabeceras["X-Synergos-Sujeto"], orquestador.Pedidos[1].Cabeceras["X-Synergos-Sujeto"]);
    }

    [Fact]
    public async Task Sin_el_rol_del_flujo_o_sin_sesion_no_sale_nada()
    {
        var orquestador = new Orquestador();

        var sinRol = await Pieza(orquestador, Miembro(Ana)).DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);
        var sinSesion = await Pieza(orquestador, Miembro(null)).DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);

        Assert.Equal("puerta.personal_requerido", sinRol.Codigo);
        Assert.Equal("puerta.sesion_requerida", sinSesion.Codigo);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Sin_la_devolucion_abierta_en_el_sitio_no_sale_nada()
    {
        var orquestador = new Orquestador();

        var sinDestino = await Pieza(orquestador, Personal(Rosa), conDestino: false)
            .DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);
        var sinEntrada = await Pieza(orquestador, Personal(Rosa), declarada: false)
            .DevolverAsync(Compra, 40_000m, "COP", "RMA rma-1", CancellationToken.None);

        Assert.Equal("tienda.devolucion_no_disponible", sinDestino.Codigo);
        Assert.Equal("tienda.devolucion_no_disponible", sinEntrada.Codigo);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Un_rechazo_del_orquestador_pasa_con_su_codigo_y_no_devuelve_nada()
    {
        var orquestador = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Conflict,
                """{"code":"payments.refund_over_captured","detail":"Se pasaría de lo cobrado.","transient":false}""", "application/problem+json"),
        };

        var r = await Pieza(orquestador, Personal(Rosa)).DevolverAsync(Compra, 90_000m, "COP", "RMA rma-1", CancellationToken.None);

        Assert.False(r.Bien);
        Assert.Equal(("payments.refund_over_captured", 0m), (r.Codigo, r.Devuelto));
    }

    // ── El decorador de órdenes y la RMA ─────────────────────────────────────────────────────

    [Fact]
    public async Task Una_orden_de_la_puerta_se_reembolsa_por_el_orquestador_y_una_directa_por_el_motor()
    {
        var orquestador = new Orquestador();
        var (ordenes, motor) = await Ordenes(Pieza(orquestador, Personal(Rosa)));
        motor.RefundAsync("ord_directa", 10m, "COP", "x", Arg.Any<CancellationToken>()).Returns(10m);

        Assert.Equal(40_000m, await ordenes.RefundAsync(Compra, 40_000m, "COP", "RMA rma-1"));
        Assert.Equal(10m, await ordenes.RefundAsync("ord_directa", 10m, "COP", "x"));

        Assert.Single(orquestador.Pedidos);
        await motor.DidNotReceive().RefundAsync(Compra, Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_RMA_de_una_compra_de_la_puerta_llega_a_reembolsada_por_el_orquestador_con_el_monto_de_su_linea()
    {
        var orquestador = new Orquestador();
        var (ordenes, motor) = await Ordenes(Pieza(orquestador, Personal(Rosa)));
        var devoluciones = new StubReturnService(ordenes);
        var rma = await devoluciones.RequestAsync(Compra, "p-1", "Llegó rota");

        await devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Approved);
        await devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received);
        var reembolsada = await devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Refunded);

        Assert.Equal(ShopReturnStatus.Refunded, reembolsada.Status);
        var pedido = Assert.Single(orquestador.Pedidos);
        using var cuerpo = JsonDocument.Parse(pedido.Cuerpo);
        Assert.Equal(40_000m, cuerpo.RootElement.GetProperty("monto").GetProperty("amount").GetDecimal());
        Assert.Equal(DevolucionesDeLaPuerta.Llave(Compra, $"RMA {rma.RmaId}"), pedido.Cabeceras["Idempotency-Key"]);
        await motor.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default!, default!, default);
    }

    [Fact]
    public async Task Si_el_orquestador_no_devuelve_la_RMA_se_queda_recibida_con_el_motivo()
    {
        var orquestador = new Orquestador
        {
            Responde = _ => Orquestador.Json(HttpStatusCode.Conflict,
                """{"code":"payments.refund_over_captured","detail":"Se pasaría de lo cobrado.","transient":false}""", "application/problem+json"),
        };
        var (ordenes, _) = await Ordenes(Pieza(orquestador, Personal(Rosa)));
        var devoluciones = new StubReturnService(ordenes);
        var rma = await devoluciones.RequestAsync(Compra, "p-1", "Llegó rota");
        await devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Approved);
        await devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Received);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => devoluciones.AdvanceAsync(rma.RmaId, ShopReturnStatus.Refunded));

        Assert.Contains("payments.refund_over_captured", error.Message, StringComparison.Ordinal);
        Assert.Equal(ShopReturnStatus.Received, (await devoluciones.GetForOrderAsync(Compra)).Single().Status);
    }

    // ── Lo que manda y lee la pieza existe en el contrato publicado ───────────────────────────

    /// <summary>
    /// La operación que abre la devolución está en el contrato del orquestador como la manda la pieza: marcada para la
    /// puerta con el acceso del personal, con la llave y el sujeto requeridos, el cuerpo con <c>compra</c> y
    /// <c>monto</c>, y la respuesta con el <c>estado</c> y el <c>datos.monto.amount</c> que la pieza lee. Un campo
    /// renombrado en el orquestador dejaría la devolución sin monto, y la RMA no sabría cuánto salió.
    /// </summary>
    [Fact]
    public void Lo_que_manda_y_lee_la_devolucion_existe_en_el_contrato_publicado()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Raiz(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Tienda.json")));
        var raiz = doc.RootElement;
        var op = raiz.GetProperty("paths").GetProperty("/v1/flujos/tienda.devolucion").GetProperty("post");

        var marca = op.GetProperty(TablaDeLaPuerta.MarcaDelFlujo);
        Assert.Equal((DevolucionesDeLaPuerta.Flujo, DevolucionesDeLaPuerta.Operacion, TablaDeLaPuerta.AccesoDelPersonal),
            (marca.GetProperty("flujo").GetString(), marca.GetProperty("operacion").GetString(), marca.GetProperty("acceso").GetString()));
        var cabeceras = op.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("required").GetBoolean())
            .Select(p => p.GetProperty("name").GetString()).ToList();
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDelSujeto, cabeceras);
        Assert.Contains(LoQuePoneLaPuerta.CabeceraDeLaLlave, cabeceras);

        var entrada = Esquema(raiz, op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.True(entrada.GetProperty("properties").TryGetProperty("compra", out _));
        var monto = Esquema(raiz, entrada.GetProperty("properties").GetProperty("monto")).GetProperty("properties");
        Assert.True(monto.TryGetProperty("amount", out _) && monto.TryGetProperty("currency", out _));

        var respuesta = Esquema(raiz, op.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        Assert.True(respuesta.GetProperty("properties").TryGetProperty("estado", out _));
        var datos = Esquema(raiz, respuesta.GetProperty("properties").GetProperty("datos")).GetProperty("properties");
        Assert.True(Esquema(raiz, datos.GetProperty("monto")).GetProperty("properties").TryGetProperty("amount", out _));
    }

    private static JsonElement Esquema(JsonElement raiz, JsonElement esquema)
    {
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            esquema = ramas.EnumerateArray().First(r => !(r.TryGetProperty("type", out var t) && t.GetString() == "null"));
        }
        while (esquema.TryGetProperty("$ref", out var referencia))
        {
            esquema = raiz.GetProperty("components").GetProperty("schemas").GetProperty(referencia.GetString()!.Split('/').Last());
        }
        return esquema;
    }

    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── Los dobles ───────────────────────────────────────────────────────────────────────────

    private static IMemberAccessGate Personal(Guid quien)
    {
        var gate = Miembro(quien);
        gate.HasAnyRole(Arg.Is<string?>(r => r != null && r.Split(',', StringSplitOptions.None).Contains("admin"))).Returns(true);
        return gate;
    }

    private static IMemberAccessGate Miembro(Guid? quien)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(quien is not null);
        gate.CurrentMemberKey.Returns(quien);
        return gate;
    }

    private static DevolucionesDeLaPuerta Pieza(Orquestador orquestador, IMemberAccessGate miembro, bool conDestino = true, bool declarada = true)
    {
        var tabla = TablaDeLaPuerta.Incrustada();
        var puerta = new PuertaSettings();
        if (declarada)
        {
            puerta.Flujos[DevolucionesDeLaPuerta.Flujo] = new FlujoDeLaPuertaSettings
            {
                Acceso = AccesoDeLaPuerta.Personal, Roles = ["admin"], SujetoKind = "tienda.personal",
            };
        }
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var destinos = new DestinosDeLaPuerta(conDestino ? ["Tienda"] : []);
        return new DevolucionesDeLaPuerta(
            tabla, new PuertaDeLosFlujos(tabla, monitor, destinos),
            new ReenvioDeLaPuerta(new Fabrica(orquestador), NullLogger<ReenvioDeLaPuerta>.Instance), miembro, monitor);
    }

    /// <summary>Las órdenes del sitio con una compra de la puerta de Ana, pagada, y el motor de la ruta directa.</summary>
    private static async Task<(OrdenesConLasDeLaPuerta Ordenes, IShopOrderService Motor)> Ordenes(DevolucionesDeLaPuerta pieza)
    {
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore());
        await libro.ConfirmarAsync(DeAna());
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(new PuertaSettings());
        var registro = new RegistroDeComprasDeLaTienda(
            Substitute.For<IHttpClientFactory>(), libro, monitor, Substitute.For<IProductCatalogProvider>(),
            NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino: false);
        var motor = Substitute.For<IShopOrderService>();
        return (new OrdenesConLasDeLaPuerta(motor, libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance, pieza), motor);
    }

    /// <summary>El orquestador de la tienda: por defecto, la devolución completada con el monto pedido.</summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, Dictionary<string, string> Cabeceras, byte[] Cuerpo)> Pedidos { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage>? Responde { get; init; }

        public static HttpResponseMessage Json(HttpStatusCode estado, string json, string tipo = "application/json")
            => new(estado) { Content = new StringContent(json, Encoding.UTF8, tipo) };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var cuerpo = r.Content is null ? [] : await r.Content.ReadAsByteArrayAsync(ct);
            Pedidos.Add((r.Method.Method, r.RequestUri!.AbsolutePath,
                r.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase), cuerpo));
            if (Responde is not null) return Responde(r);

            using var pedido = JsonDocument.Parse(cuerpo);
            var monto = pedido.RootElement.GetProperty("monto").GetRawText();
            var compra = pedido.RootElement.GetProperty("compra").GetString();
            return Json(HttpStatusCode.Created,
                $$"""{"flujo":"tienda.devolucion","id":"dev-1","numero":"DEV00001","estado":"Completed","datos":{"compra":"{{compra}}","monto":{{monto}}},"error":null}""");
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(ReenvioDeLaPuerta.Cliente("Tienda"), name);
            return new HttpClient(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-tienda.local/") };
        }
    }
}
