using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Las lecturas del vendedor (013-seller-operaciones, T011): la bandeja y el detalle, sólo para el personal, con la forma
/// de <c>contracts/cms-y-ui.md</c>; la bandeja concilia antes de leer, y el detalle trae el envío por el camino del
/// pedido (el orquestador para uno de la puerta, el <c>ShipTo</c> para uno de la directa).
/// </summary>
/// <remarks>
/// Los asserts de la guarda miran lo que NO pasó (el orquestador no recibió nada), no sólo el estado HTTP: un 401 sale
/// igual con el hueco abierto si la guarda niega y el método sigue leyendo.
/// </remarks>
public sealed class VendedorDeLaTiendaControllerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Personal = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static readonly ShopShippingAddress Bogota = new("Calle 1 # 2-3", "Apto 4", "Bogotá", "Cundinamarca", "110111", "CO", "Beto");

    private const string EnvioDelOrquestador = """
        {"compra":"pta-1","envio":{"id":"shp_1",
         "destino":{"line1":"Carrera 7 # 8-9","line2":null,"city":"Medellín","region":"Antioquia","postalCode":null,"country":"CO","contact":"Ana"},
         "transportadora":"servientrega","estado":"Ready",
         "eventos":[{"estado":"Ready","nota":null,"fecha":"2026-10-25T14:02:11+00:00"}]}}
        """;

    /// <summary>La tienda en memoria con el controlador, la sesión que se le diga y un orquestador de mentira.</summary>
    private sealed class Tienda
    {
        public InMemoryJsonEntityStore Almacen { get; } = new();
        public StubOrderTrackingService Seguimiento { get; }
        public Orquestador Orquestador { get; } = new();
        public LibroDeComprasDeLaPuerta Libro { get; }
        public VendedorDeLaTiendaController Controlador { get; }

        public Tienda(bool sesion = true, string? roles = "admin", List<string>? rolesDelPersonal = null)
        {
            Seguimiento = new StubOrderTrackingService(null, () => T0, Almacen, "tracking-shop");
            Libro = new LibroDeComprasDeLaPuerta(Almacen, Seguimiento, null, () => T0);
            var puerta = Substitute.For<IPuertaDeLosFlujos>();
            puerta.Abre(Arg.Any<string>()).Returns(true);

            var ajustesDeLaPuerta = new PuertaSettings();
            ajustesDeLaPuerta.Flujos["tienda.compra"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "tienda.comprador" };
            var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
            monitor.CurrentValue.Returns(ajustesDeLaPuerta);
            var registro = new RegistroDeComprasDeLaTienda(
                Orquestador, Libro, monitor, Substitute.For<IProductCatalogProvider>(), NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino: true);

            var gate = Substitute.For<IMemberAccessGate>();
            gate.IsAuthenticated.Returns(sesion);
            gate.CurrentMemberKey.Returns(sesion ? Personal : null);
            gate.HasAnyRole(Arg.Any<string>()).Returns(c => roles is not null && c.Arg<string>()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Intersect(roles.Split(','), StringComparer.OrdinalIgnoreCase).Any());

            var precios = Substitute.For<IPriceFormatter>();
            precios.Format(Arg.Any<decimal>(), Arg.Any<string>())
                .Returns(c => "$ " + c.ArgAt<decimal>(0).ToString("N0", CultureInfo.GetCultureInfo("es-CO")));

            IOptionsMonitor<TiendaSettings>? tienda = null;
            if (rolesDelPersonal is not null)
            {
                tienda = Substitute.For<IOptionsMonitor<TiendaSettings>>();
                tienda.CurrentValue.Returns(new TiendaSettings { RolesDelPersonal = rolesDelPersonal });
            }

            Controlador = new VendedorDeLaTiendaController(
                new PedidosDeLaTienda(new OrdenesDeLaTienda(Almacen), Seguimiento, puerta), registro, gate, precios, tienda);
        }

        public Task Directa(string orderRef, DateTimeOffset cuando, ShopShippingAddress? shipTo = null)
            => Almacen.WriteAsync(LibroDeComprasDeLaPuerta.ResourceType, orderRef, JsonSerializer.Serialize(new PersistedOrder(
                orderRef, $"N-{orderRef}", $"ps_{orderRef}", "Beto", "beto@ejemplo.co", 54_000m, "COP",
                [new PersistedOrderLine("p-1", null, "Taza", 2, 27_000m, 54_000m, "COP", "res_1")],
                cuando, OrderStatus.Paid, Ana, shipTo)));

        public async Task DeLaPuerta(string id, bool pagada = true)
        {
            var compra = new CompraDeLaPuerta(id, $"N-{id}", Ana, "Ana", "ana@ejemplo.co",
                [new LineaDeLaCompra("p-2", null, "Camiseta", 1, 35_000m)], 35_000m, "COP");
            if (pagada) await Libro.ConfirmarAsync(compra);
            else await Libro.AnotarPendienteAsync(compra);
        }
    }

    /// <summary>El orquestador: la compra de la puerta como la ve su dueña (Ana) y el envío de cada compra.</summary>
    private sealed class Orquestador : IHttpClientFactory
    {
        public List<string> Pedidos { get; } = [];
        public Dictionary<string, string> Completadas { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (HttpStatusCode Estado, string Cuerpo)> Envios { get; } = new(StringComparer.Ordinal);

        public HttpClient CreateClient(string name) => new(new Manejador(this)) { BaseAddress = new Uri("http://tienda.local/") };

        private sealed class Manejador(Orquestador o) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var ruta = r.RequestUri!.AbsolutePath;
                var sujeto = r.Headers.TryGetValues("X-Synergos-Sujeto", out var v) ? v.Single() : "";
                lock (o.Pedidos) o.Pedidos.Add($"{r.Method} {ruta} {sujeto}".TrimEnd());
                var id = Uri.UnescapeDataString(ruta.Split('/').Last());

                if (ruta.StartsWith("/v1/envios/", StringComparison.Ordinal) && o.Envios.TryGetValue(id, out var envio))
                {
                    return Responder(envio.Estado, envio.Cuerpo);
                }
                if (ruta.StartsWith("/v1/flujos/tienda.compra/", StringComparison.Ordinal) && o.Completadas.ContainsKey(id)
                    && sujeto == $"tienda.comprador:{Ana:n}")
                {
                    return Responder(HttpStatusCode.OK, $$$"""
                        {"id":"{{{id}}}","flujo":"tienda.compra","numero":"N-{{{id}}}","estado":"Completed","error":null,
                         "datos":{"total":{"amount":35000,"currency":"COP"},
                                  "lineasDelPedido":[{"sujeto":{"kind":"tienda.producto","id":"p-2"},"cantidad":1,"precioUnitario":{"amount":35000,"currency":"COP"}}]}}
                        """);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Responder(HttpStatusCode estado, string cuerpo)
                => Task.FromResult(new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") });
        }
    }

    /// <summary>Lo que el controlador contestó, como lo serializa MVC (camelCase).</summary>
    private static JsonElement Cuerpo(IActionResult resultado)
        => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(resultado).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string[] Claves(JsonElement objeto) => objeto.EnumerateObject().Select(p => p.Name).ToArray();

    // ── La guarda ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_sesion_las_dos_lecturas_contestan_401_y_no_leen_nada()
    {
        var t = new Tienda(sesion: false, roles: null);
        await t.DeLaPuerta("pta-pendiente", pagada: false);
        await t.DeLaPuerta("pta-1");

        var bandeja = await t.Controlador.Bandeja(null);
        var detalle = await t.Controlador.Detalle("pta-1");

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<UnauthorizedObjectResult>(bandeja).StatusCode);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<UnauthorizedObjectResult>(detalle).StatusCode);
        Assert.Empty(t.Orquestador.Pedidos);
    }

    [Fact]
    public async Task Con_sesion_y_sin_el_rol_403_sin_cuerpo_y_no_leen_nada()
    {
        var t = new Tienda(roles: "comprador");
        await t.DeLaPuerta("pta-pendiente", pagada: false);
        await t.DeLaPuerta("pta-1");

        var bandeja = await t.Controlador.Bandeja(null);
        var detalle = await t.Controlador.Detalle("pta-1");

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(bandeja).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(detalle).StatusCode);
        Assert.Empty(t.Orquestador.Pedidos);
    }

    [Fact]
    public async Task El_rol_es_el_que_declara_el_despliegue_y_no_un_admin_escrito_a_mano()
    {
        var vendedor = new Tienda(roles: "vendedor", rolesDelPersonal: ["vendedor"]);
        var admin = new Tienda(roles: "admin", rolesDelPersonal: ["vendedor"]);

        Assert.IsType<OkObjectResult>(await vendedor.Controlador.Bandeja(null));
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(await admin.Controlador.Bandeja(null)).StatusCode);
    }

    // ── La bandeja ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_bandeja_tiene_la_forma_del_contrato()
    {
        var t = new Tienda();
        await t.Directa("dir-1", T0);

        var cuerpo = Cuerpo(await t.Controlador.Bandeja(null));

        Assert.Equal(["pedidos", "pagina", "porPagina", "total", "cuentas", "sinConciliar"], Claves(cuerpo));
        Assert.Equal(["paid", "preparing", "shipped", "delivered"], Claves(cuerpo.GetProperty("cuentas")));
        var pedido = cuerpo.GetProperty("pedidos")[0];
        Assert.Equal(
            ["orderRef", "orderNumber", "fecha", "comprador", "camino", "etapa", "total", "totalFormatted", "moneda", "lineas", "actos", "motivoSinActos"],
            Claves(pedido));
        Assert.Equal(["title", "qty", "amount", "amountFormatted"], Claves(pedido.GetProperty("lineas")[0]));
        Assert.Equal(("dir-1", "directa", "paid", "Beto"),
            (pedido.GetProperty("orderRef").GetString(), pedido.GetProperty("camino").GetString(), pedido.GetProperty("etapa").GetString(),
             pedido.GetProperty("comprador").GetString()));
        Assert.Equal("$ 54.000", pedido.GetProperty("totalFormatted").GetString());
        Assert.Equal(["preparar"], pedido.GetProperty("actos").EnumerateArray().Select(a => a.GetString()!).ToArray());
        // El correo del comprador no sale en la bandeja.
        Assert.DoesNotContain("beto@ejemplo.co", cuerpo.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_filtro_la_pagina_y_una_etapa_desconocida()
    {
        var t = new Tienda();
        await t.Directa("a", T0);
        await t.Directa("b", T0.AddMinutes(1));
        await t.Seguimiento.AdvanceAsync("b", "shipped");

        var enviados = Cuerpo(await t.Controlador.Bandeja("shipped"));
        var segunda = Cuerpo(await t.Controlador.Bandeja(null, pagina: 2));
        var desconocida = await t.Controlador.Bandeja("enviado");

        Assert.Equal(["b"], enviados.GetProperty("pedidos").EnumerateArray().Select(p => p.GetProperty("orderRef").GetString()!).ToArray());
        Assert.Equal(1, enviados.GetProperty("cuentas").GetProperty("paid").GetInt32());
        Assert.Equal((2, 0, 2), (segunda.GetProperty("pagina").GetInt32(), segunda.GetProperty("pedidos").GetArrayLength(), segunda.GetProperty("total").GetInt32()));
        var rechazo = Assert.IsType<BadRequestObjectResult>(desconocida);
        Assert.Contains("tienda.etapa_desconocida", JsonSerializer.Serialize(rechazo.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_bandeja_concilia_antes_de_leer_y_dice_cuantas_quedaron_sin_mirar()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-cobrada", pagada: false);
        t.Orquestador.Completadas["pta-cobrada"] = "Completed";

        var cuerpo = Cuerpo(await t.Controlador.Bandeja(null));

        Assert.Equal(["pta-cobrada"], cuerpo.GetProperty("pedidos").EnumerateArray().Select(p => p.GetProperty("orderRef").GetString()!).ToArray());
        Assert.Equal("puerta", cuerpo.GetProperty("pedidos")[0].GetProperty("camino").GetString());
        Assert.Equal(0, cuerpo.GetProperty("sinConciliar").GetInt32());
    }

    // ── El detalle ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_detalle_de_uno_de_la_directa_trae_su_ShipTo_y_sin_ShipTo_lo_dice()
    {
        var t = new Tienda();
        await t.Directa("dir-con", T0, Bogota);
        await t.Directa("dir-sin", T0);

        var con = Cuerpo(await t.Controlador.Detalle("dir-con"));
        var sin = Cuerpo(await t.Controlador.Detalle("dir-sin"));

        var envio = con.GetProperty("envio");
        Assert.Equal(("Calle 1 # 2-3", "Bogotá", "CO"),
            (envio.GetProperty("destino").GetProperty("line1").GetString(), envio.GetProperty("destino").GetProperty("city").GetString(),
             envio.GetProperty("destino").GetProperty("country").GetString()));
        Assert.Equal(JsonValueKind.Null, envio.GetProperty("transportadora").ValueKind);
        Assert.Equal(0, envio.GetProperty("eventos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, con.GetProperty("motivoSinEnvio").ValueKind);
        Assert.Equal(JsonValueKind.Null, sin.GetProperty("envio").GetProperty("destino").ValueKind);
        Assert.Equal("sin_direccion", sin.GetProperty("motivoSinEnvio").GetString());
        Assert.Empty(t.Orquestador.Pedidos);
    }

    [Fact]
    public async Task El_detalle_de_uno_de_la_puerta_trae_el_envio_que_lee_el_orquestador()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-1");
        t.Orquestador.Envios["pta-1"] = (HttpStatusCode.OK, EnvioDelOrquestador);

        var detalle = Cuerpo(await t.Controlador.Detalle("pta-1"));

        Assert.Equal("puerta", detalle.GetProperty("camino").GetString());
        var envio = detalle.GetProperty("envio");
        Assert.Equal(["destino", "transportadora", "estado", "eventos"], Claves(envio));
        Assert.Equal(("Medellín", "servientrega", "Ready"),
            (envio.GetProperty("destino").GetProperty("city").GetString(), envio.GetProperty("transportadora").GetString(),
             envio.GetProperty("estado").GetString()));
        Assert.Equal(["estado", "nota", "fecha"], Claves(envio.GetProperty("eventos")[0]));
        Assert.Equal(JsonValueKind.Null, detalle.GetProperty("motivoSinEnvio").ValueKind);
    }

    [Fact]
    public async Task Con_la_lectura_caida_el_pedido_se_ve_con_envio_nulo_y_su_motivo()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-caida");
        await t.DeLaPuerta("pta-sin-envio");
        t.Orquestador.Envios["pta-caida"] = (HttpStatusCode.ServiceUnavailable, """{"code":"guion.caida","detail":"x","transient":true}""");

        var caida = Cuerpo(await t.Controlador.Detalle("pta-caida"));
        var sinEnvio = Cuerpo(await t.Controlador.Detalle("pta-sin-envio"));

        Assert.Equal(("pta-caida", JsonValueKind.Null, "envio_no_disponible"),
            (caida.GetProperty("orderRef").GetString(), caida.GetProperty("envio").ValueKind, caida.GetProperty("motivoSinEnvio").GetString()));
        Assert.Equal("sin_envio", sinEnvio.GetProperty("motivoSinEnvio").GetString());
    }

    [Fact]
    public async Task El_detalle_de_uno_que_no_existe_o_no_esta_pagado_es_404()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-pendiente", pagada: false);

        Assert.IsType<NotFoundObjectResult>(await t.Controlador.Detalle("no-existe"));
        Assert.IsType<NotFoundObjectResult>(await t.Controlador.Detalle("pta-pendiente"));
    }
}
