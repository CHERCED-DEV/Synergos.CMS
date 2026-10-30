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
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Puerta;

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

    /// <summary>Las claves de un pedido de la bandeja (<c>contracts/cms-y-ui.md</c>), que es también lo que contesta el acto.</summary>
    private static readonly string[] PedidoDeLaBandeja =
        ["orderRef", "orderNumber", "fecha", "comprador", "camino", "etapa", "total", "totalFormatted", "moneda", "lineas", "actos", "motivoSinActos"];

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

        /// <summary>Si la puerta abre el despacho en el sitio (los dos flujos, T017).</summary>
        public bool PuertaAbierta { get; set; } = true;

        /// <summary>Lo que el controlador asentó en la auditoría.</summary>
        public List<AuditEvent> Asientos { get; } = [];

        public Tienda(bool sesion = true, string? roles = "admin", List<string>? rolesDelPersonal = null, string zona = "America/Bogota")
        {
            Seguimiento = new StubOrderTrackingService(null, () => T0, Almacen, "tracking-shop");
            Libro = new LibroDeComprasDeLaPuerta(Almacen, Seguimiento, null, () => T0);
            var puerta = Substitute.For<IPuertaDeLosFlujos>();
            puerta.Abre(Arg.Any<string>()).Returns(_ => PuertaAbierta);

            var ajustesDeLaPuerta = new PuertaSettings();
            ajustesDeLaPuerta.Flujos["tienda.compra"] = new FlujoDeLaPuertaSettings { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "tienda.comprador" };
            foreach (var flujo in new[] { DespachosDeLaPuerta.FlujoDelEnvio, DespachosDeLaPuerta.FlujoDeLaEntrega })
            {
                ajustesDeLaPuerta.Flujos[flujo] = new FlujoDeLaPuertaSettings
                {
                    Acceso = AccesoDeLaPuerta.Personal, Roles = ["admin"], SujetoKind = "tienda.personal",
                };
            }
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

            var despachos = new DespachosDeLaPuerta(
                TablaDeLaPuerta.Incrustada(), puerta, new ReenvioDeLaPuerta(Orquestador, NullLogger<ReenvioDeLaPuerta>.Instance), gate, monitor);
            var auditoria = Substitute.For<IAuditTrailWriter>();
            auditoria.WriteAsync(Arg.Do<AuditEvent>(e => Asientos.Add(e)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

            // Las devoluciones de verdad, sobre el mismo almacén: validan contra las órdenes que hay en él.
            var ordenes = new OrdenesDeLaTienda(Almacen);
            var motor = Substitute.For<IShopOrderService>();
            motor.GetOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(c => ordenes.UnaAsync(c.ArgAt<string>(0)));
            Devoluciones = new StubReturnService(motor, null, () => Reloj, Almacen);

            // El catálogo: el mismo seam que GET search, con los productos que diga cada prueba.
            Catalogo = Substitute.For<IProductCatalogProvider>();
            Catalogo.SearchAsync(Arg.Any<ProductQuery>(), Arg.Any<CancellationToken>()).Returns(c =>
            {
                var q = c.Arg<ProductQuery>();
                return new ProductSearchResult(Productos.Skip(q.Skip).Take(q.Take).ToList(), [], Productos.Count);
            });
            var enElContenido = Substitute.For<ITiendaEnElContenido>();
            enElContenido.SkusReservables().Returns(_ => Reservables);
            Precios = precios;
            Gate = gate;

            Controlador = new VendedorDeLaTiendaController(
                new PedidosDeLaTienda(ordenes, Seguimiento, puerta), registro, despachos, Seguimiento, auditoria, Devoluciones,
                ordenes, Catalogo, enElContenido, gate, precios, tienda, new RelojDeLaTienda(this),
                Options.Create(new ListadosSettings { ZonaHoraria = zona }), Options.Create(new CartSettings()));
        }

        /// <summary>El reloj del controlador: el mismo que el de las devoluciones.</summary>
        private sealed class RelojDeLaTienda(Tienda tienda) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => tienda.Reloj;
        }

        public IProductCatalogProvider Catalogo { get; }
        public IPriceFormatter Precios { get; }
        public IMemberAccessGate Gate { get; }

        /// <summary>Los productos del catálogo, en su orden.</summary>
        public List<CatalogProductSummary> Productos { get; } = [];

        /// <summary>Los SKU de las fichas reservables de la tienda.</summary>
        public HashSet<string> Reservables { get; } = new(StringComparer.Ordinal);

        public StubReturnService Devoluciones { get; }

        /// <summary>El reloj de las devoluciones: avanza a mano para ordenar la cola.</summary>
        public DateTimeOffset Reloj { get; set; } = T0;

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

        /// <summary>Lo que contesta un acto del despacho; sin él, el flujo completado con el estado que deja.</summary>
        public (HttpStatusCode Estado, string Cuerpo)? Despacho { get; set; }

        /// <summary>Los actos del despacho que le llegaron, con su cuerpo.</summary>
        public List<(string Flujo, string Cuerpo)> Despachos { get; } = [];

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
                if (r.Method == HttpMethod.Post && id is DespachosDeLaPuerta.FlujoDelEnvio or DespachosDeLaPuerta.FlujoDeLaEntrega)
                {
                    using var lector = new StreamReader(r.Content!.ReadAsStream(ct));
                    var cuerpo = lector.ReadToEnd();
                    lock (o.Despachos) o.Despachos.Add((id, cuerpo));
                    var estado = id == DespachosDeLaPuerta.FlujoDelEnvio ? "InTransit" : "Delivered";
                    var (codigo, respuesta) = o.Despacho ?? (HttpStatusCode.Created, $$$"""
                        {"id":"despacho-1","flujo":"{{{id}}}","numero":"ENV00001","estado":"Completed","error":null,
                         "datos":{"compra":"pta-1","estadoDelEnvio":"{{{estado}}}"}}
                        """);
                    return Responder(codigo, respuesta);
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
        Assert.Equal(PedidoDeLaBandeja, Claves(pedido));
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

    // ── El acto (T019, R4) ───────────────────────────────────────────────────────────────────

    private static Task<IActionResult> Acto(Tienda t, string orderRef, string? acto, string? guia = null)
        => t.Controlador.Avanzar(orderRef, new VendedorDeLaTiendaController.ActoDelPedidoDto(acto, guia));

    /// <summary>El estado y el cuerpo de una respuesta que no es 200.</summary>
    private static (int Estado, JsonElement Cuerpo) Fallo(IActionResult resultado)
    {
        var objeto = Assert.IsAssignableFrom<ObjectResult>(resultado);
        Assert.IsNotType<OkObjectResult>(resultado);
        return (objeto.StatusCode!.Value, JsonSerializer.SerializeToElement(objeto.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task<(string Etapa, string? Nota)> Seguimiento(Tienda t, string orderRef)
    {
        var linea = (await t.Seguimiento.GetTimelineAsync(orderRef))!;
        return (linea.CurrentStage, linea.Stages.Single(s => s.Stage == linea.CurrentStage).Note);
    }

    [Fact]
    public async Task Preparar_mueve_el_seguimiento_por_los_dos_caminos_con_su_asiento_y_sin_ir_al_orquestador()
    {
        var t = new Tienda();
        await t.Directa("dir-1", T0, Bogota);
        await t.DeLaPuerta("pta-1");

        var directa = Cuerpo(await Acto(t, "dir-1", "preparar"));
        var puerta = Cuerpo(await Acto(t, "pta-1", "preparar"));

        Assert.Equal(("preparing", "enviar"), (directa.GetProperty("etapa").GetString(), directa.GetProperty("actos")[0].GetString()));
        Assert.Equal(("preparing", "enviar"), (puerta.GetProperty("etapa").GetString(), puerta.GetProperty("actos")[0].GetString()));
        Assert.Equal(PedidoDeLaBandeja, Claves(puerta));
        Assert.Empty(t.Orquestador.Despachos);
        Assert.Equal(["dir-1", "pta-1"], t.Asientos.Select(a => a.Resource));
        Assert.All(t.Asientos, a => Assert.Equal(("shop.order-dispatch", "preparar: paid → preparing"), (a.Action, a.Detail)));
    }

    [Fact]
    public async Task Enviar_y_entregar_uno_de_la_puerta_van_por_el_orquestador_y_despues_el_seguimiento_con_la_guia()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-1");
        await Acto(t, "pta-1", "preparar");
        t.Asientos.Clear();

        var enviado = Cuerpo(await Acto(t, "pta-1", "enviar", "  GUIA-123  "));

        Assert.Equal(("shipped", "entregar"), (enviado.GetProperty("etapa").GetString(), enviado.GetProperty("actos")[0].GetString()));
        Assert.Equal(("shipped", "GUIA-123"), await Seguimiento(t, "pta-1"));
        var (flujo, cuerpo) = Assert.Single(t.Orquestador.Despachos);
        Assert.Equal(DespachosDeLaPuerta.FlujoDelEnvio, flujo);
        Assert.Equal("GUIA-123", JsonDocument.Parse(cuerpo).RootElement.GetProperty("guia").GetString());
        Assert.Contains(t.Orquestador.Pedidos, p => p == $"POST /v1/flujos/tienda.envio tienda.personal:{Personal:n}");

        var entregado = Cuerpo(await Acto(t, "pta-1", "entregar"));

        Assert.Equal("delivered", entregado.GetProperty("etapa").GetString());
        Assert.Equal(0, entregado.GetProperty("actos").GetArrayLength());
        Assert.Equal("entregado", entregado.GetProperty("motivoSinActos").GetString());
        Assert.Equal(DespachosDeLaPuerta.FlujoDeLaEntrega, t.Orquestador.Despachos[1].Flujo);
        // Lo que pasó por el orquestador tiene su rastro en la saga, que es del personal: no se asienta otra vez.
        Assert.Empty(t.Asientos);
    }

    [Fact]
    public async Task Enviar_y_entregar_uno_de_la_directa_solo_mueven_el_seguimiento_con_su_asiento_y_no_llaman_a_la_puerta()
    {
        var t = new Tienda();
        await t.Directa("dir-1", T0, Bogota);
        await Acto(t, "dir-1", "preparar");

        Cuerpo(await Acto(t, "dir-1", "enviar", "GUIA-9"));
        Assert.Equal(("shipped", "GUIA-9"), await Seguimiento(t, "dir-1"));
        var entregado = Cuerpo(await Acto(t, "dir-1", "entregar"));

        Assert.Equal("delivered", entregado.GetProperty("etapa").GetString());
        Assert.Empty(t.Orquestador.Despachos);
        Assert.Equal(
            ["preparar: paid → preparing", "enviar: preparing → shipped (guía GUIA-9)", "entregar: shipped → delivered"],
            t.Asientos.Select(a => a.Detail));
    }

    [Fact]
    public async Task Un_rechazo_del_orquestador_pasa_con_su_codigo_y_el_seguimiento_se_queda_donde_estaba()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-1");
        await Acto(t, "pta-1", "preparar");
        t.Orquestador.Despacho = (HttpStatusCode.Conflict,
            """{"code":"flow.original_sin_dato","detail":"La compra no tiene «envio».","transient":false}""");

        var (estado, cuerpo) = Fallo(await Acto(t, "pta-1", "enviar", "GUIA-123"));

        Assert.Equal((409, "flow.original_sin_dato", false),
            (estado, cuerpo.GetProperty("code").GetString(), cuerpo.GetProperty("transient").GetBoolean()));
        Assert.Equal(("preparing", null), await Seguimiento(t, "pta-1"));
    }

    [Fact]
    public async Task Una_caida_del_orquestador_es_503_transitorio_y_no_se_mueve_nada()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-1");
        await Acto(t, "pta-1", "preparar");
        t.Orquestador.Despacho = (HttpStatusCode.ServiceUnavailable,
            """{"code":"fulfillment.unreachable","detail":"fulfillment no respondió.","transient":true}""");

        var (estado, cuerpo) = Fallo(await Acto(t, "pta-1", "enviar"));

        Assert.Equal((503, true), (estado, cuerpo.GetProperty("transient").GetBoolean()));
        Assert.Equal("preparing", (await Seguimiento(t, "pta-1")).Etapa);
    }

    [Fact]
    public async Task Fuera_de_orden_es_409_con_la_etapa_y_repetir_no_sella_ni_despacha_dos_veces()
    {
        var t = new Tienda();
        await t.DeLaPuerta("pta-1");

        var (antes, saltado) = Fallo(await Acto(t, "pta-1", "enviar"));
        Assert.Equal((409, "tienda.acto_fuera_de_orden", "paid"),
            (antes, saltado.GetProperty("code").GetString(), saltado.GetProperty("etapa").GetString()));

        await Acto(t, "pta-1", "preparar");
        await Acto(t, "pta-1", "enviar", "GUIA-123");
        var (otraVez, repetido) = Fallo(await Acto(t, "pta-1", "enviar", "GUIA-999"));

        Assert.Equal((409, "shipped"), (otraVez, repetido.GetProperty("etapa").GetString()));
        Assert.Single(t.Orquestador.Despachos);
        Assert.Equal(("shipped", "GUIA-123"), await Seguimiento(t, "pta-1"));
        Assert.Single(t.Asientos);
    }

    [Fact]
    public async Task La_guia_es_de_una_linea_de_hasta_80_y_solo_al_enviar_y_el_acto_uno_de_los_tres()
    {
        var t = new Tienda();
        await t.Directa("dir-1", T0, Bogota);
        await Acto(t, "dir-1", "preparar");

        Assert.Equal(("tienda.guia_invalida", 400), Codigo(await Acto(t, "dir-1", "enviar", new string('x', 81))));
        Assert.Equal(("tienda.guia_invalida", 400), Codigo(await Acto(t, "dir-1", "enviar", "GUIA\n123")));
        Assert.Equal(("tienda.guia_invalida", 400), Codigo(await Acto(t, "dir-1", "entregar", "GUIA-123")));
        Assert.Equal(("tienda.acto_desconocido", 400), Codigo(await Acto(t, "dir-1", "despachar")));
        Assert.Equal(("tienda.acto_desconocido", 400), Codigo(await Acto(t, "dir-1", null)));
        Assert.Equal("preparing", (await Seguimiento(t, "dir-1")).Etapa);

        // Ochenta, recortada, sí.
        Assert.IsType<OkObjectResult>(await Acto(t, "dir-1", "enviar", "  " + new string('x', 80) + "  "));

        static (string?, int) Codigo(IActionResult r)
        {
            var (estado, cuerpo) = Fallo(r);
            return (cuerpo.GetProperty("code").GetString(), estado);
        }
    }

    [Fact]
    public async Task Con_el_despacho_cerrado_en_el_sitio_uno_de_la_puerta_no_se_despacha_y_la_directa_si()
    {
        var t = new Tienda { PuertaAbierta = false };
        await t.DeLaPuerta("pta-1");
        await t.Directa("dir-1", T0, Bogota);

        var (estado, cuerpo) = Fallo(await Acto(t, "pta-1", "preparar"));

        Assert.Equal((409, DespachosDeLaPuerta.NoDisponible), (estado, cuerpo.GetProperty("code").GetString()));
        Assert.Equal("paid", (await Seguimiento(t, "pta-1")).Etapa);
        Assert.IsType<OkObjectResult>(await Acto(t, "dir-1", "preparar"));
    }

    [Fact]
    public async Task El_acto_tiene_la_guarda_del_personal_y_no_mueve_nada_sin_ella()
    {
        var sinSesion = new Tienda(sesion: false);
        await sinSesion.DeLaPuerta("pta-1");
        var sinRol = new Tienda(roles: "editor");
        await sinRol.DeLaPuerta("pta-1");

        Assert.IsType<UnauthorizedObjectResult>(await Acto(sinSesion, "pta-1", "preparar"));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await Acto(sinRol, "pta-1", "preparar")).StatusCode);
        Assert.Equal("paid", (await Seguimiento(sinSesion, "pta-1")).Etapa);
        Assert.Equal("paid", (await Seguimiento(sinRol, "pta-1")).Etapa);
        Assert.Empty(sinSesion.Asientos.Concat(sinRol.Asientos));
    }

    [Fact]
    public async Task Un_pedido_que_no_existe_es_404()
        => Assert.IsType<NotFoundObjectResult>(await Acto(new Tienda(), "no-existe", "preparar"));

    // ── La cola de devoluciones (T024) ───────────────────────────────────────────────────────

    [Fact]
    public async Task La_cola_tiene_la_forma_del_contrato_con_el_pedido_su_camino_y_el_estado_exacto()
    {
        var t = new Tienda();
        await t.Directa("dir-1", T0, Bogota);
        await t.DeLaPuerta("pta-1");
        var directa = await t.Devoluciones.RequestAsync("dir-1", "p-1", "Llegó rota");
        t.Reloj = T0.AddMinutes(5);
        await t.Devoluciones.RequestAsync("pta-1", "p-2", "No era la talla");
        await t.Devoluciones.AdvanceAsync(directa.RmaId, ShopReturnStatus.Approved);

        var cuerpo = Cuerpo(await t.Controlador.Devoluciones());

        Assert.Equal(["devoluciones"], Claves(cuerpo));
        var cola = cuerpo.GetProperty("devoluciones");
        Assert.Equal(
            ["rmaId", "orderRef", "orderNumber", "camino", "producto", "cantidad", "motivo", "monto", "montoFormatted", "moneda",
             "estado", "nota", "solicitada", "actualizada"],
            Claves(cola[0]));
        // La pedida antes que la aprobada: lo que más espera, primero.
        Assert.Equal(("pta-1", "N-pta-1", "puerta", "Requested"),
            (cola[0].GetProperty("orderRef").GetString(), cola[0].GetProperty("orderNumber").GetString(),
             cola[0].GetProperty("camino").GetString(), cola[0].GetProperty("estado").GetString()));
        Assert.Equal(("dir-1", "directa", "Approved", 54_000m, "$ 54.000"),
            (cola[1].GetProperty("orderRef").GetString(), cola[1].GetProperty("camino").GetString(),
             cola[1].GetProperty("estado").GetString(), cola[1].GetProperty("monto").GetDecimal(),
             cola[1].GetProperty("montoFormatted").GetString()));
    }

    [Fact]
    public async Task El_estado_de_la_cola_es_el_exacto_y_no_el_rotulo_del_comprador()
    {
        // Para el comprador, aprobada y recibida son la misma «en revisión»; para el personal no: una espera la
        // mercancía y la otra el reembolso.
        var t = new Tienda();
        await t.Directa("dir-1", T0, Bogota);
        await t.Directa("dir-2", T0, Bogota);
        var aprobada = await t.Devoluciones.RequestAsync("dir-1", "p-1", "Llegó rota");
        var recibida = await t.Devoluciones.RequestAsync("dir-2", "p-1", "Llegó rota");
        await t.Devoluciones.AdvanceAsync(aprobada.RmaId, ShopReturnStatus.Approved);
        await t.Devoluciones.AdvanceAsync(recibida.RmaId, ShopReturnStatus.Approved);
        await t.Devoluciones.AdvanceAsync(recibida.RmaId, ShopReturnStatus.Received);

        var cola = Cuerpo(await t.Controlador.Devoluciones()).GetProperty("devoluciones");

        Assert.Equal(["Received", "Approved"], cola.EnumerateArray().Select(d => d.GetProperty("estado").GetString()!).ToArray());
        Assert.Equal([recibida.RmaId, aprobada.RmaId], cola.EnumerateArray().Select(d => d.GetProperty("rmaId").GetString()!).ToArray());
    }

    [Fact]
    public async Task La_cola_tiene_la_guarda_del_personal()
    {
        Assert.IsType<UnauthorizedObjectResult>(await new Tienda(sesion: false).Controlador.Devoluciones());
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await new Tienda(roles: "editor").Controlador.Devoluciones()).StatusCode);
        Assert.Equal(0, Cuerpo(await new Tienda().Controlador.Devoluciones()).GetProperty("devoluciones").GetArrayLength());
    }
    // ── Las publicaciones (T026) ─────────────────────────────────────────────────────────────

    private static CatalogProductSummary Producto(string id, string nombre, decimal precio, int existencias)
        => new(id, nombre, precio, "COP", "Marca", "Hogar", null, 4.5, 3, existencias);

    [Fact]
    public async Task Las_publicaciones_dicen_lo_mismo_que_la_busqueda_del_comprador()
    {
        var t = new Tienda();
        t.Productos.AddRange([Producto("taza", "Taza de cerámica", 27_000m, 12), Producto("funda", "Funda de silicona", 15_000m, 3)]);
        var busqueda = new ShopCatalogController(
            t.Catalogo, Substitute.For<IShopOrderService>(), t.Precios, Substitute.For<IUserCollection>(), t.Seguimiento,
            t.Devoluciones, Substitute.For<IMessagingService>(), t.Gate, Substitute.For<IShopQuery>(),
            Substitute.For<ICatalogSocialProof>(), null, Options.Create(new ListadosSettings()));

        var delComprador = Cuerpo(await busqueda.Search(null, null, null, null, null, null, null, default)).GetProperty("products");
        var delPersonal = Cuerpo(await t.Controlador.Publicaciones()).GetProperty("publicaciones");

        Assert.Equal(
            delComprador.EnumerateArray().Select(p => (p.GetProperty("id").GetString(), p.GetProperty("title").GetString(),
                p.GetProperty("priceFormatted").GetString(), p.GetProperty("stock").GetInt32())),
            delPersonal.EnumerateArray().Select(p => (p.GetProperty("id").GetString(), p.GetProperty("titulo").GetString(),
                p.GetProperty("precioFormatted").GetString(), p.GetProperty("existencias").GetInt32())));
    }

    [Fact]
    public async Task Cada_publicacion_dice_si_esta_a_la_venta_agotada_o_es_reservable()
    {
        var t = new Tienda();
        t.Productos.AddRange([Producto("taza", "Taza", 27_000m, 12), Producto("funda", "Funda", 15_000m, 0), Producto("asesoria", "Asesoría", 80_000m, 0)]);
        t.Reservables.Add("asesoria");

        var cuerpo = Cuerpo(await t.Controlador.Publicaciones());

        Assert.Equal(["publicaciones", "pagina", "porPagina", "total"], Claves(cuerpo));
        var lista = cuerpo.GetProperty("publicaciones");
        Assert.Equal(["id", "sku", "titulo", "precio", "precioFormatted", "moneda", "existencias", "estado"], Claves(lista[0]));
        Assert.Equal(["a-la-venta", "agotada", "reservable"], lista.EnumerateArray().Select(p => p.GetProperty("estado").GetString()!).ToArray());
        Assert.Equal(("taza", "$ 27.000", 12), (lista[0].GetProperty("sku").GetString(), lista[0].GetProperty("precioFormatted").GetString(),
            lista[0].GetProperty("existencias").GetInt32()));
    }

    [Fact]
    public async Task Las_publicaciones_van_de_a_50_y_sin_productos_la_lista_es_vacia()
    {
        var t = new Tienda();
        Assert.Equal(0, Cuerpo(await t.Controlador.Publicaciones()).GetProperty("publicaciones").GetArrayLength());

        t.Productos.AddRange(Enumerable.Range(1, 51).Select(i => Producto($"p-{i}", $"Producto {i}", 1_000m, 1)));
        var segunda = Cuerpo(await t.Controlador.Publicaciones(pagina: 2));

        Assert.Equal((2, 50, 51, 1),
            (segunda.GetProperty("pagina").GetInt32(), segunda.GetProperty("porPagina").GetInt32(), segunda.GetProperty("total").GetInt32(),
             segunda.GetProperty("publicaciones").GetArrayLength()));
        Assert.Equal("p-51", segunda.GetProperty("publicaciones")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Las_publicaciones_tienen_la_guarda_del_personal()
    {
        Assert.IsType<UnauthorizedObjectResult>(await new Tienda(sesion: false).Controlador.Publicaciones());
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await new Tienda(roles: "editor").Controlador.Publicaciones()).StatusCode);
    }
    // ── El panel (T028) ──────────────────────────────────────────────────────────────────────

    private static Dictionary<string, JsonElement> Indicadores(JsonElement resumen)
        => resumen.GetProperty("indicadores").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetString()!, i => i);

    [Fact]
    public async Task El_panel_cuenta_lo_vendido_lo_devuelto_y_lo_que_espera_sin_restar_las_devoluciones()
    {
        var t = new Tienda();
        // Octubre: dos pagadas (54.000 y 35.000); una de septiembre y una pendiente no cuentan.
        await t.Directa("dir-1", T0.AddDays(-5), Bogota);
        await t.Directa("dir-sep", new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), Bogota);
        await t.DeLaPuerta("pta-1");
        await t.DeLaPuerta("pta-pendiente", pagada: false);
        // Devuelto en octubre: los 54.000 de dir-1, reembolsados; vendido NO los resta.
        var reembolsada = await t.Devoluciones.RequestAsync("dir-1", "p-1", "Llegó rota");
        foreach (var paso in new[] { ShopReturnStatus.Approved, ShopReturnStatus.Received, ShopReturnStatus.Refunded })
        {
            await t.Devoluciones.AdvanceAsync(reembolsada.RmaId, paso);
        }
        // Por atender: una pedida y una recibida.
        await t.Devoluciones.RequestAsync("pta-1", "p-2", "No era la talla");
        var recibida = await t.Devoluciones.RequestAsync("dir-sep", "p-1", "Se rompió");
        await t.Devoluciones.AdvanceAsync(recibida.RmaId, ShopReturnStatus.Approved);
        await t.Devoluciones.AdvanceAsync(recibida.RmaId, ShopReturnStatus.Received);
        // Por despachar: las pagadas en «paid» o «preparing»; dir-sep ya salió.
        await t.Seguimiento.AdvanceAsync("dir-sep", "shipped");

        var cuerpo = Cuerpo(await t.Controlador.Resumen());

        Assert.Equal(["periodo", "indicadores"], Claves(cuerpo));
        var periodo = cuerpo.GetProperty("periodo");
        Assert.Equal(("2026-10-01", "2026-10-31", "America/Bogota"),
            (periodo.GetProperty("desde").GetString(), periodo.GetProperty("hasta").GetString(), periodo.GetProperty("zona").GetString()));
        var indicadores = Indicadores(cuerpo);
        Assert.Equal(["vendido", "devuelto", "por-despachar", "por-atender"], indicadores.Keys.ToArray());
        Assert.Equal((89_000m, "$ 89.000", "currency"),
            (indicadores["vendido"].GetProperty("valor").GetDecimal(), indicadores["vendido"].GetProperty("valorFormatted").GetString(),
             indicadores["vendido"].GetProperty("unidad").GetString()));
        Assert.Equal((54_000m, "$ 54.000"),
            (indicadores["devuelto"].GetProperty("valor").GetDecimal(), indicadores["devuelto"].GetProperty("valorFormatted").GetString()));
        Assert.Equal((2m, "count"), (indicadores["por-despachar"].GetProperty("valor").GetDecimal(), indicadores["por-despachar"].GetProperty("unidad").GetString()));
        Assert.Equal(2m, indicadores["por-atender"].GetProperty("valor").GetDecimal());
    }

    [Fact]
    public async Task El_mes_es_el_de_la_hora_del_sitio_y_no_el_de_UTC()
    {
        // Las 23:30 UTC del 30 de septiembre son la 01:30 del 1 de octubre en Madrid: es de octubre.
        var t = new Tienda(zona: "Europe/Madrid");
        await t.Directa("dir-borde", new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero), Bogota);

        var cuerpo = Cuerpo(await t.Controlador.Resumen());

        Assert.Equal(54_000m, Indicadores(cuerpo)["vendido"].GetProperty("valor").GetDecimal());
        Assert.Equal("Europe/Madrid", cuerpo.GetProperty("periodo").GetProperty("zona").GetString());
    }

    [Fact]
    public async Task El_panel_tiene_la_guarda_del_personal()
    {
        Assert.IsType<UnauthorizedObjectResult>(await new Tienda(sesion: false).Controlador.Resumen());
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await new Tienda(roles: "editor").Controlador.Resumen()).StatusCode);
    }
}
