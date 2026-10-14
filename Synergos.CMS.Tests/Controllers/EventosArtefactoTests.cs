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
/// El artefacto de una compra por la puerta (ADR 0140 F3): los asistentes antes de cerrar y las entradas
/// con su QR después, siempre de quien compró, y lo confirmado sin tocar la red.
/// </summary>
/// <remarks>
/// Contra un orquestador falso que se porta como <c>Bff.Eventos</c> en lo que el artefacto mira: una
/// compra es de su dueño —otro sujeto recibe 404— y tiene un estado y lo apartado. Lo de verdad es el
/// registro de entradas con su firmante: un QR emitido acá tiene que abrir la puerta.
/// </remarks>
public sealed class EventosArtefactoTests : IDisposable
{
    private static readonly Guid Ana = Guid.Parse("8c0b1a52-7f1d-4c5e-9d6e-3a2b1c0d9e8f");
    private static readonly Guid Beto = Guid.Parse("1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9");
    private const string Compra = "pta-0123456789abcdef0123456789abcdef01234567";

    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IEventTicketingService _ticketing = Substitute.For<IEventTicketingService>();
    private readonly EventTicketLedger _ledger = new(signer: new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-del-artefacto")));
    private readonly Orquestador _orquestador = new();

    public EventosArtefactoTests()
    {
        Sesion(Ana);
        _ticketing.GetTicketsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => _ledger.TicketsOfAsync(ci.ArgAt<string>(0), default));
        _orquestador.Sagas[Compra] = new Saga($"eventos.comprador:{Ana:n}", "Running", [("GEN", null, 2)]);
    }

    public void Dispose() => _orquestador.Dispose();

    private void Sesion(Guid? miembro)
    {
        _gate.IsAuthenticated.Returns(miembro is not null);
        _gate.CurrentMemberKey.Returns(miembro);
        _gate.CurrentMemberEmail.Returns(miembro == Ana ? "ana@ejemplo.co" : miembro == Beto ? "beto@ejemplo.co" : null);
        _gate.CurrentMemberDisplayName.Returns(miembro == Ana ? "Ana María" : "Beto");
    }

    internal sealed record Saga(string Dueno, string Estado, (string Tier, string? Seat, int Cantidad)[] Apartado);

    internal sealed class Orquestador : HttpMessageHandler
    {
        public Dictionary<string, Saga> Sagas { get; } = new(StringComparer.Ordinal);

        private int _llamadas;

        /// <summary>Cuántas peticiones llegaron; el artefacto lee varias a la vez.</summary>
        public int Llamadas => Volatile.Read(ref _llamadas);

        public bool Caido { get; set; }

        /// <summary>Si contesta un estado de error sin rechazo que leer, como un proxy.</summary>
        public HttpStatusCode? SinRechazo { get; set; }

        /// <summary>Acepta la conexión y no contesta nunca.</summary>
        public bool Colgado { get; set; }

        private static async Task<HttpResponseMessage> Colgarse(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("inalcanzable");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            if (Caido) throw new HttpRequestException("Connection refused");
            if (Colgado) return Colgarse(ct);
            if (SinRechazo is { } estado) return Task.FromResult(new HttpResponseMessage(estado));

            var id = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath.Split('/').Last());
            var sujeto = r.Headers.TryGetValues("X-Synergos-Sujeto", out var v) ? v.Single() : null;
            if (!Sagas.TryGetValue(id, out var saga) || saga.Dueno != sujeto)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"code":"eventos.purchase_not_found","detail":"no","transient":false}""",
                        Encoding.UTF8, "application/problem+json"),
                });
            }

            var corte = saga.Dueno.IndexOf(':', StringComparison.Ordinal);
            var json = JsonSerializer.Serialize(new
            {
                id,
                buyerKind = saga.Dueno[..corte],
                buyerId = saga.Dueno[(corte + 1)..],
                eventId = "evt-1",
                status = saga.Estado,
                total = new { amount = 200_000m, currency = "COP" },
                held = saga.Apartado.Select(a => new { tier = a.Tier, seat = a.Seat, quantity = a.Cantidad }),
                pendingCompensations = 0,
                lastError = saga.Estado == "Compensated" ? "payments.declined" : null,
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
    }

    private EventosController Controlador(
        string? cuerpo = null, string tipo = "application/json", bool mismoOrigen = true, bool hayDestino = true, bool abierto = true,
        TimeSpan? presupuesto = null)
    {
        var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        puerta.CurrentValue.Returns(new PuertaSettings
        {
            Flujos = abierto
                ? new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
                {
                    [ArtefactoDeEventos.Flujo] = new() { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "eventos.comprador" },
                }
                : new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal),
        });
        var artefacto = new ArtefactoDeEventos(
            new Fabrica(_orquestador), _ledger, puerta, NullLogger<ArtefactoDeEventos>.Instance, hayDestino: hayDestino,
            presupuesto: presupuesto);

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("sitio.test");
        if (cuerpo is not null)
        {
            http.Request.Method = "POST";
            var bytes = Encoding.UTF8.GetBytes(cuerpo);
            http.Request.Body = new MemoryStream(bytes);
            http.Request.ContentLength = bytes.Length;
            http.Request.ContentType = tipo;
            if (mismoOrigen) http.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        }

        return new EventosController(
            Substitute.For<IEventCatalogProvider>(), _ticketing, Substitute.For<IEventManagementService>(),
            Substitute.For<IPriceFormatter>(), _gate, Substitute.For<IRealtimeNotifier>(),
            NullLogger<EventosController>.Instance, artefacto: artefacto)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static (int Estado, string? Codigo, JsonElement Cuerpo) Problema(IActionResult r)
    {
        var c = Assert.IsType<ContentResult>(r);
        Assert.Equal("application/problem+json", c.ContentType);
        var cuerpo = JsonDocument.Parse(c.Content!).RootElement;
        return (c.StatusCode!.Value, cuerpo.GetProperty("code").GetString(), cuerpo);
    }

    private static (int Estado, string? Codigo) Fallo(IActionResult r)
    {
        var (estado, codigo, _) = Problema(r);
        return (estado, codigo);
    }

    private static EventosController.ConfirmResponse Entradas(IActionResult r)
        => Assert.IsType<EventosController.ConfirmResponse>(Assert.IsType<OkObjectResult>(r).Value);

    private const string DosAsistentes =
        """{"attendees":[{"name":"Luisa","email":"luisa@ejemplo.co","document":"CC 1"},{"name":"Pedro","email":"pedro@ejemplo.co"}]}""";

    [Fact]
    public async Task Sobre_una_compra_en_curso_no_se_emite_nada()
    {
        var (estado, codigo, cuerpo) = Problema(await Controlador().Entradas(Compra, default));

        Assert.Equal((409, "eventos.compra_en_curso"), (estado, codigo));
        Assert.Equal("Running", cuerpo.GetProperty("purchaseStatus").GetString());
        Assert.DoesNotContain(await _ledger.LoadAllAsync(), o => o.Status == EventOrderStatus.Confirmed);
    }

    [Fact]
    public async Task Una_compra_deshecha_es_409_con_su_estado()
    {
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Compensated" };

        var (estado, codigo, cuerpo) = Problema(await Controlador().Entradas(Compra, default));

        Assert.Equal((409, "eventos.compra_no_completada"), (estado, codigo));
        Assert.Equal("Compensated", cuerpo.GetProperty("purchaseStatus").GetString());
    }

    [Fact]
    public async Task Sin_asistentes_el_comprador_es_portador_de_todas_y_su_QR_abre_la_puerta()
    {
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };

        var r = Entradas(await Controlador().Entradas(Compra, default));

        Assert.Equal("Confirmed", r.Status);
        Assert.Equal(2, r.Tickets.Count);
        Assert.All(r.Tickets, t => Assert.Equal("ana@ejemplo.co", t.HolderEmail));
        Assert.Equal("valid", (await _ledger.CheckInAsync(r.Tickets[0].Qr)).Status);
    }

    [Fact]
    public async Task Los_asistentes_se_anotan_antes_de_cerrar_y_son_los_portadores()
    {
        var anotados = Assert.IsType<OkObjectResult>(await Controlador(DosAsistentes).AnotarAsistentes(Compra, default));
        Assert.Equal(2, Assert.IsType<EventosController.AsistentesAnotadosResponse>(anotados.Value).Asistentes);
        Assert.Equal("Pending", Assert.Single(await _ledger.LoadAllAsync()).Status.ToString());

        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };
        var r = Entradas(await Controlador().Entradas(Compra, default));

        Assert.Equal(["luisa@ejemplo.co", "pedro@ejemplo.co"], r.Tickets.Select(t => t.HolderEmail));
        var orden = Assert.Single(await _ledger.LoadAllAsync());
        Assert.Equal(("ana@ejemplo.co", $"{Ana:n}"), (orden.BuyerEmail, orden.BuyerId));

        // Cerrada, ya no se corrige la lista.
        Assert.Equal((409, "eventos.compra_ya_cerrada"), Fallo(await Controlador(DosAsistentes).AnotarAsistentes(Compra, default)));
    }

    [Fact]
    public async Task Los_asistentes_tienen_que_cuadrar_con_lo_apartado()
    {
        var uno = """{"attendees":[{"name":"Luisa","email":"luisa@ejemplo.co"}]}""";

        Assert.Equal((400, "eventos.asistentes_no_cuadran"), Fallo(await Controlador(uno).AnotarAsistentes(Compra, default)));
        Assert.Equal((400, "eventos.asistentes_invalidos"),
            Fallo(await Controlador("""{"attendees":[{"name":"","email":"x"}]}""").AnotarAsistentes(Compra, default)));
        Assert.Empty(await _ledger.LoadAllAsync());
    }

    [Fact]
    public async Task Otro_miembro_no_lee_ni_anota_la_compra_de_Ana_ni_siquiera_cuando_ya_se_emitio()
    {
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };
        Entradas(await Controlador().Entradas(Compra, default));

        Sesion(Beto);
        Assert.Equal((404, "eventos.purchase_not_found"), Fallo(await Controlador().Entradas(Compra, default)));
        Assert.Equal((404, "eventos.purchase_not_found"), Fallo(await Controlador(DosAsistentes).AnotarAsistentes(Compra, default)));
    }

    [Fact]
    public async Task Lo_confirmado_no_toca_la_red_ni_con_el_orquestador_caido()
    {
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };
        var primera = Entradas(await Controlador().Entradas(Compra, default));
        var llamadas = _orquestador.Llamadas;
        _orquestador.Caido = true;

        var otraVez = Entradas(await Controlador().Entradas(Compra, default));
        var mias = Assert.IsType<EventosController.TicketsResponse>(Assert.IsType<OkObjectResult>(await Controlador().Tickets(default)).Value);

        Assert.Equal(llamadas, _orquestador.Llamadas);
        Assert.Equal(primera.Tickets.Select(t => t.Qr), otraVez.Tickets.Select(t => t.Qr));
        Assert.Equal(2, mias.Tickets.Count);
    }

    [Fact]
    public async Task Mis_entradas_confirma_la_compra_que_el_orquestador_ya_cerro()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };
        Sesion(Ana);
        _gate.CurrentMemberEmail.Returns("luisa@ejemplo.co");

        var mias = Assert.IsType<EventosController.TicketsResponse>(Assert.IsType<OkObjectResult>(await Controlador().Tickets(default)).Value);

        Assert.Equal("luisa@ejemplo.co", Assert.Single(mias.Tickets).HolderEmail);
        Assert.Equal(EventOrderStatus.Confirmed, Assert.Single(await _ledger.LoadAllAsync()).Status);
    }

    // ── Las ramas de fallo ──────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Sin_destino_o_con_el_flujo_cerrado_el_artefacto_no_esta_disponible_y_no_sale_a_la_red(bool hayDestino, bool abierto)
    {
        var (estado, codigo, cuerpo) = Problema(await Controlador(hayDestino: hayDestino, abierto: abierto).Entradas(Compra, default));
        var anotar = Fallo(await Controlador(DosAsistentes, hayDestino: hayDestino, abierto: abierto).AnotarAsistentes(Compra, default));

        Assert.Equal((503, "eventos.artefacto_no_disponible"), (estado, codigo));
        Assert.False(cuerpo.GetProperty("transient").GetBoolean());
        Assert.Equal((503, "eventos.artefacto_no_disponible"), anotar);
        Assert.Equal(0, _orquestador.Llamadas);
    }

    [Fact]
    public async Task Con_el_orquestador_caido_y_la_compra_sin_confirmar_es_503_transitorio_y_no_se_emite_nada()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        _orquestador.Caido = true;

        var (estado, codigo, cuerpo) = Problema(await Controlador().Entradas(Compra, default));

        Assert.Equal((503, "eventos.orquestador_no_disponible"), (estado, codigo));
        Assert.True(cuerpo.GetProperty("transient").GetBoolean());
        Assert.Equal(EventOrderStatus.Pending, Assert.Single(await _ledger.LoadAllAsync()).Status);
    }

    [Fact]
    public async Task Un_error_del_orquestador_sin_rechazo_que_leer_es_502()
    {
        _orquestador.SinRechazo = HttpStatusCode.InternalServerError;

        var (estado, codigo, cuerpo) = Problema(await Controlador().Entradas(Compra, default));

        Assert.Equal((502, "eventos.respuesta_invalida"), (estado, codigo));
        Assert.False(cuerpo.GetProperty("transient").GetBoolean());
        Assert.Empty(await _ledger.LoadAllAsync());
    }

    [Fact]
    public async Task Lo_que_lleva_el_QR_o_los_asistentes_sale_sin_cache_tambien_el_rechazo()
    {
        // El QR firmado es la credencial de entrada al recinto: ni el navegador ni una caché intermedia lo guardan.
        var enCurso = Controlador();
        Problema(await enCurso.Entradas(Compra, default));
        var anotar = Controlador(DosAsistentes);
        await anotar.AnotarAsistentes(Compra, default);
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };
        var entradas = Controlador();
        Entradas(await entradas.Entradas(Compra, default));
        var mias = Controlador();
        await mias.Tickets(default);
        _ticketing.ConfirmAsync("evord_ajena", Arg.Any<CancellationToken>()).Returns<EventConfirmationResult>(_ => throw new ArgumentException("no"));
        var vieja = Controlador();
        await vieja.Confirm(new EventosController.ConfirmRequest("evord_ajena"), default);

        Assert.All(new[] { enCurso, anotar, entradas, mias, vieja },
            c => Assert.Equal("no-store", c.HttpContext.Response.Headers.CacheControl.ToString()));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    public async Task Un_segmento_de_punto_no_es_una_compra_y_no_sale_a_la_red(string id)
    {
        Assert.Equal((404, "eventos.purchase_not_found"), Fallo(await Controlador().Entradas(id, default)));
        Assert.Equal((404, "eventos.purchase_not_found"), Fallo(await Controlador(DosAsistentes).AnotarAsistentes(id, default)));
        Assert.Equal(0, _orquestador.Llamadas);
    }

    [Fact]
    public async Task Sin_sesion_es_401_y_de_otro_origen_o_sin_JSON_no_se_anota()
    {
        Sesion(null);
        Assert.Equal((401, "puerta.sesion_requerida"), Fallo(await Controlador().Entradas(Compra, default)));

        Sesion(Ana);
        Assert.Equal((403, "puerta.origen_no_permitido"),
            Fallo(await Controlador(DosAsistentes, mismoOrigen: false).AnotarAsistentes(Compra, default)));
        Assert.Equal((415, "puerta.tipo_no_soportado"),
            Fallo(await Controlador(DosAsistentes, tipo: "text/plain").AnotarAsistentes(Compra, default)));
        Assert.Empty(await _ledger.LoadAllAsync());
    }

    // ── Las compras sin acuse (ADR 0140, D2, fase D) ───────────────────────
    //
    // Antes de abrir con una llave NUEVA, el elemento pregunta si quien compra tiene otra compra del mismo
    // evento sin acuse: la cobrada cuya respuesta se perdió, o la que sigue en curso. Lo sabe este lado —la
    // orden Pending—, así que se ve desde otro dispositivo o con los datos del sitio borrados.

    private const string Evento = "evt-1";

    /// <summary>Una orden de este lado, sin pasar por el orquestador.</summary>
    private async Task Orden(string saga, Guid quien, string evento = Evento, int haceMinutos = 0,
        EventOrderStatus estado = EventOrderStatus.Pending)
        => await _ledger.SaveAsync(new PersistedEventOrder(
            "evord_" + Guid.NewGuid().ToString("n"), evento, saga, 200_000m, "COP", [], DateTimeOffset.UtcNow.AddMinutes(-haceMinutos))
        {
            BuyerKind = "eventos.comprador",
            BuyerId = quien.ToString("n"),
            ViaGate = true,
            Status = estado,
        });

    private static IReadOnlyList<EventosController.CompraPendienteDto> Pendientes(IActionResult r)
        => Assert.IsType<EventosController.ComprasPendientesResponse>(Assert.IsType<OkObjectResult>(r).Value).Compras;

    [Fact]
    public async Task Sin_ordenes_pendientes_la_lista_esta_vacia_y_no_sale_a_la_red()
    {
        Assert.Empty(Pendientes(await Controlador().ComprasPendientes(Evento, default)));
        Assert.Equal(0, _orquestador.Llamadas);
    }

    [Fact]
    public async Task Una_compra_en_curso_sin_acuse_se_informa_en_curso()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);

        var compra = Assert.Single(Pendientes(await Controlador().ComprasPendientes(Evento, default)));

        Assert.Equal((Compra, "Running"), (compra.Id, compra.Status));
    }

    [Fact]
    public async Task Una_compra_cobrada_sin_acuse_se_informa_sin_emitir_y_dos_lecturas_dicen_lo_mismo()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        _orquestador.Sagas[Compra] = _orquestador.Sagas[Compra] with { Estado = "Completed" };

        var primera = Pendientes(await Controlador().ComprasPendientes(Evento, default));
        var segunda = Pendientes(await Controlador().ComprasPendientes(Evento, default));

        Assert.Equal((Compra, "Completed"), (Assert.Single(primera).Id, primera[0].Status));
        Assert.Equal(primera, segunda);
        Assert.Equal(EventOrderStatus.Pending, Assert.Single(await _ledger.LoadAllAsync()).Status);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    public async Task Una_compra_que_no_se_puede_leer_sale_sin_leer_y_no_se_descarta(bool caido, HttpStatusCode? sinRechazo)
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        _orquestador.Caido = caido;
        _orquestador.SinRechazo = sinRechazo;

        var compra = Assert.Single(Pendientes(await Controlador().ComprasPendientes(Evento, default)));

        Assert.Equal((Compra, ArtefactoDeEventos.SinLeer), (compra.Id, compra.Status));
        Assert.Equal(EventOrderStatus.Pending, Assert.Single(await _ledger.LoadAllAsync()).Status);
    }

    [Fact]
    public async Task Con_el_orquestador_colgado_sale_sin_leer_al_llegar_al_techo()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        _orquestador.Colgado = true;

        var compra = Assert.Single(Pendientes(
            await Controlador(presupuesto: TimeSpan.FromMilliseconds(200)).ComprasPendientes(Evento, default)));

        Assert.Equal(ArtefactoDeEventos.SinLeer, compra.Status);
    }

    [Fact]
    public async Task La_compra_sin_acuse_de_otro_comprador_no_se_ve_ni_se_consulta()
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        var llamadas = _orquestador.Llamadas;

        Sesion(Beto);
        Assert.Empty(Pendientes(await Controlador().ComprasPendientes(Evento, default)));
        Assert.Equal(llamadas, _orquestador.Llamadas);
    }

    [Fact]
    public async Task Otro_evento_y_lo_ya_resuelto_no_salen_y_lo_deshecho_o_inexistente_se_descarta()
    {
        _orquestador.Sagas["pta-otro-evento"] = new Saga($"eventos.comprador:{Ana:n}", "Running", [("GEN", null, 1)]);
        _orquestador.Sagas["pta-confirmada"] = new Saga($"eventos.comprador:{Ana:n}", "Completed", [("GEN", null, 1)]);
        _orquestador.Sagas["pta-deshecha"] = new Saga($"eventos.comprador:{Ana:n}", "Compensated", [("GEN", null, 1)]);
        _orquestador.Sagas["pta-deshaciendose"] = new Saga($"eventos.comprador:{Ana:n}", "Compensating", [("GEN", null, 1)]);
        await Orden("pta-otro-evento", Ana, evento: "evt-2");
        await Orden("pta-confirmada", Ana, estado: EventOrderStatus.Confirmed);
        await Orden("pta-descartada", Ana, estado: EventOrderStatus.Discarded);
        await Orden("pta-deshecha", Ana);
        await Orden("pta-deshaciendose", Ana);
        await Orden("pta-inexistente", Ana);

        Assert.Empty(Pendientes(await Controlador().ComprasPendientes(Evento, default)));

        var estados = (await _ledger.LoadAllAsync()).ToDictionary(o => o.PaymentSessionId, o => o.Status);
        Assert.Equal(EventOrderStatus.Discarded, estados["pta-deshecha"]);
        Assert.Equal(EventOrderStatus.Discarded, estados["pta-inexistente"]);
        Assert.Equal(EventOrderStatus.Pending, estados["pta-deshaciendose"]);
        Assert.Equal(EventOrderStatus.Pending, estados["pta-otro-evento"]);
        Assert.Equal(EventOrderStatus.Confirmed, estados["pta-confirmada"]);
    }

    [Fact]
    public async Task Mira_como_mucho_el_tope_y_lo_que_no_alcanza_a_mirar_sale_sin_leer()
    {
        for (var i = 0; i <= ArtefactoDeEventos.TopeDeReconciliacion; i++)
        {
            var saga = $"pta-{i:D2}";
            _orquestador.Sagas[saga] = new Saga($"eventos.comprador:{Ana:n}", "Running", [("GEN", null, 1)]);
            await Orden(saga, Ana, haceMinutos: i);
        }

        var compras = Pendientes(await Controlador().ComprasPendientes(Evento, default));

        Assert.Equal(ArtefactoDeEventos.TopeDeReconciliacion, _orquestador.Llamadas);
        Assert.Equal(ArtefactoDeEventos.TopeDeReconciliacion + 1, compras.Count);
        Assert.Equal(($"pta-{ArtefactoDeEventos.TopeDeReconciliacion:D2}", ArtefactoDeEventos.SinLeer),
            (compras[^1].Id, compras[^1].Status));
        Assert.All(compras.SkipLast(1), c => Assert.Equal("Running", c.Status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Sin_evento_es_400_y_no_sale_a_la_red(string? evento)
    {
        await Controlador(DosAsistentes).AnotarAsistentes(Compra, default);
        var llamadas = _orquestador.Llamadas;

        var (estado, codigo, cuerpo) = Problema(await Controlador().ComprasPendientes(evento, default));

        Assert.Equal((400, "eventos.evento_requerido"), (estado, codigo));
        Assert.False(cuerpo.GetProperty("transient").GetBoolean());
        Assert.Equal(llamadas, _orquestador.Llamadas);
    }

    [Fact]
    public async Task Sin_sesion_es_401_sin_destino_503_y_todo_sale_sin_cache()
    {
        Sesion(null);
        var anonimo = Controlador();
        Assert.Equal((401, "puerta.sesion_requerida"), Fallo(await anonimo.ComprasPendientes(Evento, default)));

        Sesion(Ana);
        var sinDestino = Controlador(hayDestino: false);
        var (estado, codigo, cuerpo) = Problema(await sinDestino.ComprasPendientes(Evento, default));
        Assert.Equal((503, "eventos.artefacto_no_disponible"), (estado, codigo));
        Assert.False(cuerpo.GetProperty("transient").GetBoolean());

        var bien = Controlador();
        Pendientes(await bien.ComprasPendientes(Evento, default));

        Assert.All(new[] { anonimo, sinDestino, bien },
            c => Assert.Equal("no-store", c.HttpContext.Response.Headers.CacheControl.ToString()));
        Assert.Equal(0, _orquestador.Llamadas);
    }
}
