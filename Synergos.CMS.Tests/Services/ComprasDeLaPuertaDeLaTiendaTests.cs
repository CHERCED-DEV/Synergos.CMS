using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La historia 6 de 004-tienda-compra: una compra hecha por la puerta queda en el registro de órdenes del CMS, de
/// su dueño y de nadie más, con lo que cobró el orquestador; «Mis compras» la ve con el motor que sea, el motor en
/// proceso no la confirma ni la reembolsa, y lo que el registro lee existe en el contrato publicado.
/// </summary>
public sealed class ComprasDeLaPuertaDeLaTiendaTests
{
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Beto = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private const string Compra = "pta-compra-1";

    private static CompraDeLaPuerta Puerta(Guid miembro, string id = Compra) => new(
        id, "COMPRA01", miembro, "Ana", "ana@ejemplo.co",
        [new LineaDeLaCompra("p-1", null, "Taza", 2, 20_000m), new LineaDeLaCompra("p-2", "azul", "Camiseta — Azul", 1, 35_000m)],
        75_000m, "COP");

    // ── El libro ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anotar_deja_una_orden_pendiente_de_la_puerta_con_lo_que_cobro_el_orquestador()
    {
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore());

        var orden = await libro.AnotarPendienteAsync(Puerta(Ana));

        Assert.NotNull(orden);
        Assert.Equal(OrderStatus.Pending, orden!.Status);
        Assert.True(orden.ViaGate);
        Assert.Equal(Ana, orden.OwnerMemberKey);
        Assert.Equal("COMPRA01", orden.OrderNumber);
        Assert.Equal(Compra, orden.PaymentSessionId);
        Assert.Equal(75_000m, orden.Total);
        Assert.Equal(40_000m, orden.Lines.Single(l => l.ProductId == "p-1").LineTotal);
        Assert.Equal("azul", orden.Lines.Single(l => l.ProductId == "p-2").VariantId);
    }

    [Fact]
    public async Task Una_compra_tiene_UNA_orden_y_confirmarla_dos_veces_avanza_el_seguimiento_y_la_venta_una_vez()
    {
        var seguimiento = Substitute.For<IOrderTrackingService>();
        var ventas = Substitute.For<ICheckoutRecorder>();
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore(), seguimiento, ventas);

        await libro.AnotarPendienteAsync(Puerta(Ana));
        await libro.AnotarPendienteAsync(Puerta(Ana));
        var primera = await libro.ConfirmarAsync(Puerta(Ana));
        var segunda = await libro.ConfirmarAsync(Puerta(Ana));

        Assert.Equal(OrderStatus.Paid, primera!.Status);
        Assert.Equal(OrderStatus.Paid, segunda!.Status);
        Assert.Single(await libro.DelMiembroAsync(Ana));
        await seguimiento.Received(1).AdvanceAsync(Compra, StubOrderTrackingService.StagePaid, Arg.Any<string>(), Arg.Any<CancellationToken>());
        ventas.Received(1).Record(Arg.Is<CheckoutCompleted>(c => c.OrderId == Compra && c.Subtotal == 75_000m));
    }

    [Fact]
    public async Task La_compra_de_otro_miembro_no_se_anota_ni_se_confirma_ni_se_descarta()
    {
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore());
        await libro.AnotarPendienteAsync(Puerta(Ana));

        Assert.Null(await libro.AnotarPendienteAsync(Puerta(Beto)));
        Assert.Null(await libro.ConfirmarAsync(Puerta(Beto)));
        Assert.False(await libro.DescartarAsync(Compra, Beto));
        Assert.Empty(await libro.DelMiembroAsync(Beto));
        Assert.Equal(OrderStatus.Pending, (await libro.LeerAsync(Compra))!.Status);
    }

    [Fact]
    public async Task Descartar_cancela_una_pendiente_y_no_toca_una_pagada()
    {
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore());
        await libro.AnotarPendienteAsync(Puerta(Ana, "pta-a"));
        await libro.ConfirmarAsync(Puerta(Ana, "pta-b"));

        Assert.True(await libro.DescartarAsync("pta-a", Ana));
        Assert.False(await libro.DescartarAsync("pta-b", Ana));
        Assert.Equal(OrderStatus.Cancelled, (await libro.LeerAsync("pta-a"))!.Status);
        Assert.Equal(OrderStatus.Paid, (await libro.LeerAsync("pta-b"))!.Status);
    }

    [Fact]
    public async Task El_motor_en_proceso_ve_la_orden_de_la_puerta_y_no_la_confirma_ni_la_reembolsa_por_su_proveedor()
    {
        var almacen = new InMemoryJsonEntityStore();
        var pagos = Substitute.For<IPaymentProvider>();
        var motor = new StubShopOrderService(
            Substitute.For<IProductCatalogProvider>(), Substitute.For<IReservationService>(), pagos, null, almacen, now: null);
        await new LibroDeComprasDeLaPuerta(almacen).ConfirmarAsync(Puerta(Ana));

        var mias = await motor.GetOrdersByMemberAsync(Ana);

        Assert.True(Assert.Single(mias).ViaGate);
        await Assert.ThrowsAsync<InvalidOperationException>(() => motor.ConfirmAsync(Compra));
        await Assert.ThrowsAsync<InvalidOperationException>(() => motor.RefundAsync(Compra, 1m, "COP", "prueba"));
        await pagos.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default, default);
        await pagos.DidNotReceiveWithAnyArgs().RefundAsync(default!, default, default);
    }

    // ── El registro: lo que lee al orquestador ───────────────────────────────────────────────

    [Fact]
    public async Task Anotar_lee_la_compra_con_el_sujeto_del_miembro_y_la_deja_pendiente()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Running");
        var (registro, libro) = Registro(orquestador);

        var r = await registro.AnotarAsync(Compra, Ana, "Ana", "ana@ejemplo.co", CancellationToken.None);

        Assert.True(r.Bien, r.Mensaje);
        Assert.Equal(OrderStatus.Pending, r.Orden!.Status);
        Assert.Equal($"GET /v1/flujos/tienda.compra/{Compra} tienda.comprador:{Ana:n}", Assert.Single(orquestador.Pedidos));
        var linea = (await libro.LeerAsync(Compra))!.Lines.Single(l => l.ProductId == "p-2");
        Assert.Equal(("azul", 35_000m, 1), (linea.VariantId, linea.UnitPrice, linea.Quantity));
        // Sin el producto en el catálogo, la orden lleva su id: no se inventa un nombre.
        Assert.Equal("p-2", linea.ProductName);
    }

    [Fact]
    public async Task Leer_la_compra_completada_la_paga_y_la_deshecha_la_descarta()
    {
        var orquestador = new OrquestadorFalso().Contesta("pta-a", "Completed").Contesta("pta-b", "Running");
        var (registro, libro) = Registro(orquestador);
        await registro.AnotarAsync("pta-b", Ana, null, null, CancellationToken.None);
        orquestador.Contesta("pta-b", "Compensated");

        var pagada = await registro.CompraAsync("pta-a", Ana, null, null, CancellationToken.None);
        var deshecha = await registro.CompraAsync("pta-b", Ana, null, null, CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, pagada.Orden!.Status);
        Assert.Equal("tienda.compra_deshecha", deshecha.Codigo);
        Assert.Equal(OrderStatus.Cancelled, (await libro.LeerAsync("pta-b"))!.Status);
    }

    [Fact]
    public async Task Una_compra_que_el_orquestador_no_tiene_para_ese_miembro_es_un_404_y_no_se_anota()
    {
        var orquestador = new OrquestadorFalso();
        var (registro, libro) = Registro(orquestador);

        var r = await registro.AnotarAsync("ajena", Ana, null, null, CancellationToken.None);

        Assert.Equal(404, r.Estado);
        Assert.Equal("tienda.purchase_not_found", r.Codigo);
        Assert.Null(await libro.LeerAsync("ajena"));
    }

    [Fact]
    public async Task Sin_destino_el_registro_contesta_503_y_no_sale_a_la_red()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, _) = Registro(orquestador, hayDestino: false);

        var r = await registro.CompraAsync(Compra, Ana, null, null, CancellationToken.None);

        Assert.Equal(503, r.Estado);
        Assert.Empty(orquestador.Pedidos);
    }

    [Fact]
    public async Task Lo_pagado_se_lee_del_libro_sin_tocar_la_red()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, _) = Registro(orquestador);
        await registro.CompraAsync(Compra, Ana, null, null, CancellationToken.None);
        orquestador.Pedidos.Clear();

        var r = await registro.CompraAsync(Compra, Ana, null, null, CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, r.Orden!.Status);
        Assert.Empty(orquestador.Pedidos);
    }

    // ── «Mis compras»: el motor más las de la puerta ────────────────────────────────────────

    [Fact]
    public async Task Mis_compras_reconcilia_y_suma_las_de_la_puerta_una_vez_aunque_el_motor_tambien_las_vea()
    {
        var almacen = new InMemoryJsonEntityStore();
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, libro) = Registro(orquestador, almacen: almacen);
        await registro.AnotarAsync(Compra, Ana, null, null, CancellationToken.None);
        // El motor en proceso lee el MISMO almacén: también la ve.
        var motor = new StubShopOrderService(
            Substitute.For<IProductCatalogProvider>(), Substitute.For<IReservationService>(), Substitute.For<IPaymentProvider>(), null, almacen, now: null);
        var ordenes = new OrdenesConLasDeLaPuerta(motor, libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance);

        var mias = await ordenes.GetOrdersByMemberAsync(Ana);

        var unica = Assert.Single(mias);
        Assert.Equal(OrderStatus.Paid, unica.Status);
        Assert.Empty(await ordenes.GetOrdersByMemberAsync(Beto));
    }

    [Fact]
    public async Task Con_el_orquestador_de_motor_el_historial_ya_no_sale_vacio()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, libro) = Registro(orquestador);
        await registro.CompraAsync(Compra, Ana, null, null, CancellationToken.None);
        var motorBff = Substitute.For<IShopOrderService>();
        motorBff.GetOrdersByMemberAsync(Ana, Arg.Any<CancellationToken>()).Returns(Array.Empty<ShopOrder>());
        var ordenes = new OrdenesConLasDeLaPuerta(motorBff, libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance);

        Assert.Single(await ordenes.GetOrdersByMemberAsync(Ana));
        Assert.True((await ordenes.GetOrderAsync(Compra))!.ViaGate);
    }

    [Fact]
    public async Task Una_orden_de_la_puerta_no_se_reembolsa_por_el_motor_y_una_directa_si()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, libro) = Registro(orquestador);
        await registro.CompraAsync(Compra, Ana, null, null, CancellationToken.None);
        var motor = Substitute.For<IShopOrderService>();
        motor.RefundAsync("ord_directa", 10m, "COP", "x", Arg.Any<CancellationToken>()).Returns(10m);
        var ordenes = new OrdenesConLasDeLaPuerta(motor, libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ordenes.RefundAsync(Compra, 10m, "COP", "x"));
        Assert.Equal(10m, await ordenes.RefundAsync("ord_directa", 10m, "COP", "x"));
        await motor.DidNotReceive().RefundAsync(Compra, Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── La tienda entera: la conciliación que corre al leer la bandeja (013-seller-operaciones, T008) ──

    [Fact]
    public async Task La_tienda_entera_lee_cada_pendiente_con_el_sujeto_de_SU_dueno_y_la_completada_queda_pagada()
    {
        var orquestador = new OrquestadorFalso().Contesta("pta-ana", "Completed").Contesta("pta-beto", "Completed", Beto);
        var (registro, libro) = Registro(orquestador);
        await libro.AnotarPendienteAsync(Puerta(Ana, "pta-ana"));
        await libro.AnotarPendienteAsync(Puerta(Beto, "pta-beto"));

        var sinMirar = await registro.ReconciliarLaTiendaAsync(CancellationToken.None);

        Assert.Equal(0, sinMirar);
        Assert.Equal(OrderStatus.Paid, (await libro.LeerAsync("pta-ana"))!.Status);
        Assert.Equal(OrderStatus.Paid, (await libro.LeerAsync("pta-beto"))!.Status);
        Assert.Contains($"GET /v1/flujos/tienda.compra/pta-beto tienda.comprador:{Beto:n}", orquestador.Pedidos);
        Assert.Empty(await libro.PendientesDeLaTiendaAsync());
    }

    [Fact]
    public async Task La_deshecha_se_descarta_la_que_sigue_en_curso_se_queda_y_lo_pagado_no_se_mira()
    {
        var orquestador = new OrquestadorFalso().Contesta("pta-deshecha", "Compensated").Contesta("pta-en-curso", "Running")
            .Contesta(Compra, "Completed");
        var (registro, libro) = Registro(orquestador);
        await libro.AnotarPendienteAsync(Puerta(Ana, "pta-deshecha"));
        await libro.AnotarPendienteAsync(Puerta(Ana, "pta-en-curso"));
        await libro.ConfirmarAsync(Puerta(Ana));

        var sinMirar = await registro.ReconciliarLaTiendaAsync(CancellationToken.None);

        Assert.Equal(0, sinMirar);
        Assert.Equal(OrderStatus.Cancelled, (await libro.LeerAsync("pta-deshecha"))!.Status);
        Assert.Equal(OrderStatus.Pending, (await libro.LeerAsync("pta-en-curso"))!.Status);
        Assert.DoesNotContain(orquestador.Pedidos, p => p.Contains(Compra, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Con_doce_pendientes_mira_diez_y_dice_que_dos_quedaron_sin_mirar()
    {
        var orquestador = new OrquestadorFalso();
        var (registro, libro) = Registro(orquestador);
        for (var i = 0; i < 12; i++)
        {
            orquestador.Contesta($"pta-{i:00}", "Running");
            await libro.AnotarPendienteAsync(Puerta(Ana, $"pta-{i:00}"));
        }

        var sinMirar = await registro.ReconciliarLaTiendaAsync(CancellationToken.None);

        Assert.Equal(2, sinMirar);
        Assert.Equal(RegistroDeComprasDeLaTienda.TopeDeReconciliacion, orquestador.Pedidos.Count);
    }

    [Fact]
    public async Task Lo_que_no_alcanza_el_presupuesto_cuenta_como_sin_mirar_y_la_bandeja_no_espera()
    {
        var orquestador = new OrquestadorFalso { Retraso = TimeSpan.FromSeconds(30) };
        var (registro, libro) = Registro(orquestador, presupuesto: TimeSpan.FromMilliseconds(200));
        for (var i = 0; i < 3; i++)
        {
            orquestador.Contesta($"pta-{i}", "Completed");
            await libro.AnotarPendienteAsync(Puerta(Ana, $"pta-{i}"));
        }

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var sinMirar = await registro.ReconciliarLaTiendaAsync(CancellationToken.None);

        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(20), $"esperó {reloj.Elapsed}");
        Assert.Equal(3, sinMirar);
        Assert.Equal(3, (await libro.PendientesDeLaTiendaAsync()).Count);
    }

    [Fact]
    public async Task Sin_destino_no_sale_a_la_red_y_todas_las_pendientes_quedan_sin_mirar()
    {
        var orquestador = new OrquestadorFalso().Contesta(Compra, "Completed");
        var (registro, libro) = Registro(orquestador, hayDestino: false);
        await libro.AnotarPendienteAsync(Puerta(Ana));

        Assert.Equal(1, await registro.ReconciliarLaTiendaAsync(CancellationToken.None));
        Assert.Empty(orquestador.Pedidos);
    }

    // ── Lo que lee el registro existe en el contrato publicado ─────────────────────────────

    /// <summary>
    /// Lo que el registro deserializa de una compra (<c>RegistroDeComprasDeLaTienda.CompraLeida</c> y los suyos)
    /// existe en la respuesta de <c>TiendaCompraConsultar</c>, y la cabecera del sujeto que manda está declarada:
    /// un campo renombrado en el orquestador se quedaría en nulo en silencio y la orden saldría sin líneas.
    /// </summary>
    [Fact]
    public void Lo_que_lee_el_registro_de_una_compra_existe_en_TiendaCompraConsultar()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Raiz(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Tienda.json")));
        var raiz = doc.RootElement;
        var op = raiz.GetProperty("paths").GetProperty("/v1/flujos/tienda.compra/{id}").GetProperty("get");
        Assert.Equal("TiendaCompraConsultar", op.GetProperty("operationId").GetString());
        Assert.Contains(op.GetProperty("parameters").EnumerateArray(), p =>
            p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "X-Synergos-Sujeto");

        var malas = new List<string>();
        Lee(raiz, typeof(RegistroDeComprasDeLaTienda.CompraLeida),
            op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "$", malas);

        Assert.True(malas.Count == 0, "El registro lee campos que el contrato no publica: " + string.Join(" | ", malas));
    }

    private static void Lee(JsonElement raiz, Type tipo, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(raiz, esquema);
        var propiedades = esquema.GetProperty("properties");
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var nombre = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!propiedades.TryGetProperty(nombre, out var sub))
            {
                malas.Add($"{donde}.{nombre}");
                continue;
            }
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var (dentro, esq) = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? (t.GetGenericArguments()[0], Resolver(raiz, sub).GetProperty("items"))
                : (t, sub);
            if (dentro.DeclaringType == typeof(RegistroDeComprasDeLaTienda)) Lee(raiz, dentro, esq, $"{donde}.{nombre}", malas);
        }
    }

    private static JsonElement Resolver(JsonElement raiz, JsonElement esquema)
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

    private static (RegistroDeComprasDeLaTienda Registro, LibroDeComprasDeLaPuerta Libro) Registro(
        OrquestadorFalso orquestador, bool hayDestino = true, IJsonEntityStore? almacen = null, TimeSpan? presupuesto = null)
    {
        var libro = new LibroDeComprasDeLaPuerta(almacen ?? new InMemoryJsonEntityStore());
        var puerta = new PuertaSettings();
        puerta.Flujos["tienda.compra"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "tienda.comprador" };
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(puerta);
        var registro = new RegistroDeComprasDeLaTienda(
            orquestador, libro, monitor, Substitute.For<IProductCatalogProvider>(),
            NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino, presupuesto);
        return (registro, libro);
    }

    /// <summary>
    /// El orquestador: contesta cada compra con su estado, como la ve su dueño (Ana, salvo que se diga otro); a
    /// cualquier otro sujeto, 404. Puede tardar en contestar.
    /// </summary>
    private sealed class OrquestadorFalso : IHttpClientFactory
    {
        private readonly Dictionary<string, (string Estado, Guid Dueno)> _estados = new(StringComparer.Ordinal);

        public List<string> Pedidos { get; } = [];

        /// <summary>Cuánto tarda cada lectura en contestar.</summary>
        public TimeSpan Retraso { get; set; } = TimeSpan.Zero;

        public OrquestadorFalso Contesta(string id, string estado, Guid? dueno = null)
        {
            _estados[id] = (estado, dueno ?? Ana);
            return this;
        }

        public HttpClient CreateClient(string name) => new(new Manejador(this)) { BaseAddress = new Uri("http://tienda.local/") };

        private sealed class Manejador(OrquestadorFalso o) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var sujeto = r.Headers.TryGetValues("X-Synergos-Sujeto", out var v) ? v.Single() : "";
                lock (o.Pedidos) o.Pedidos.Add($"{r.Method} {r.RequestUri!.AbsolutePath} {sujeto}");
                if (o.Retraso > TimeSpan.Zero) await Task.Delay(o.Retraso, ct);
                var id = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath.Split('/').Last());
                if (!o._estados.TryGetValue(id, out var compra) || sujeto != $"tienda.comprador:{compra.Dueno:n}")
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                var estado = compra.Estado;
                var json = $$$"""
                {"id":"{{{id}}}","flujo":"tienda.compra","numero":"COMPRA01","estado":"{{{estado}}}","compensacionesPendientes":0,"error":null,"deshaciendo":false,
                 "datos":{"total":{"amount":75000,"currency":"COP"},
                          "lineasDelPedido":[{"sujeto":{"kind":"tienda.producto","id":"p-1"},"cantidad":2,"precioUnitario":{"amount":20000,"currency":"COP"}},
                                             {"sujeto":{"kind":"tienda.producto","id":"p-2:azul"},"cantidad":1,"precioUnitario":{"amount":35000,"currency":"COP"}}]}}
                """;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }
    }
}
