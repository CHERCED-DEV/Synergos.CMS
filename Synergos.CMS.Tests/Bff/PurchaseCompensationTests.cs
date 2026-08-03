using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Clients;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Cubre el segundo orquestador — y con él, si la máquina de sagas promovida sirve de verdad.
/// </summary>
/// <remarks>
/// <para><b>Estos tests valen por lo que NO repiten.</b> El retroceso exponencial, los ocho
/// intentos, la guarda de «armada no es pendiente» y el aviso una-sola-vez están probados una vez
/// en <see cref="CompensationTests"/> sobre Salud, y son el mismo código. Lo que se prueba acá es
/// lo que Tienda tiene y Salud no: que el <b>número de compensaciones no es fijo</b> —una por
/// línea de la canasta—, que hay <b>cuatro kinds</b> en vez de tres, y que el motor no necesitó
/// saber nada de eso.</para>
///
/// <para>Si la promoción hubiera estado mal cortada, estos tests serían imposibles de escribir
/// sin tocar <c>Bff.Core</c> — y ese, no otro, es el resultado que interesa.</para>
/// </remarks>
public sealed class PurchaseCompensationTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    private sealed class CapacidadesFalsas : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _rutas = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas[patron] = responde;
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });

        /// <summary>Responde distinto en cada llamada — para que dos líneas den dos apartados.</summary>
        public CapacidadesFalsas Secuencia(string patron, params string[] jsons)
        {
            var i = 0;
            return Cuando(patron, _ =>
            {
                var json = jsons[Math.Min(i++, jsons.Length - 1)];
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };
            });
        }

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""",
                    System.Text.Encoding.UTF8, "application/problem+json"),
            });

        public CapacidadesFalsas Caida(string patron)
            => Cuando(patron, _ => throw new HttpRequestException("guionado: caída"));

        public int Veces(string method, string pathContiene)
            => Llamadas.Count(l => l.Method == method && l.Path.Contains(pathContiene, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path,
                    request.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                    cuerpo));
            }

            foreach (var (patron, responde) in _rutas)
            {
                if (Coincide(patron, clave)) return responde(request);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"code":"stub.no_route","detail":"sin guion"}""",
                    System.Text.Encoding.UTF8, "application/problem+json"),
            };
        }

        private static bool Coincide(string patron, string clave)
            => patron.EndsWith('*')
                ? clave.StartsWith(patron[..^1], StringComparison.Ordinal)
                : string.Equals(patron, clave, StringComparison.Ordinal);
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadesFalsas _handler;
        public FabricaFalsa(CapacidadesFalsas h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private sealed class MemoriaSagas : ISagaStore<PurchaseSaga>
    {
        private readonly Dictionary<string, PurchaseSaga> _s = new(StringComparer.Ordinal);
        public PurchaseSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<PurchaseSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public void Put(PurchaseSaga saga) => _s[saga.Id] = saga;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly object Direccion = new { line1 = "Cra 7 # 1-2", city = "Bogotá", country = "CO" };

    private sealed record Contexto(
        PurchaseFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas, RelojFalso Reloj,
        SagaEngine<PurchaseSaga> Motor);

    /// <summary>Una canasta de TRES líneas: el punto entero de este dominio.</summary>
    private const string CanastaDeTres = """
        {"id":"c1","ownerKind":"tienda.comprador","ownerId":"u-1","checkedOut":false,"open":true,
         "lines":[{"subjectKind":"tienda.producto","subjectId":"p-1","quantity":2},
                  {"subjectKind":"tienda.producto","subjectId":"p-2","quantity":1},
                  {"subjectKind":"tienda.producto","subjectId":"p-3","quantity":4}]}
        """;

    private static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("GET /v1/carts/c1", CanastaDeTres)
        .Ok("POST /v1/carts/c1/checkout", """{"id":"c1","ownerKind":"tienda.comprador","ownerId":"u-1","checkedOut":true,"open":false,"lines":[]}""")
        .Ok("POST /v1/quotes", """{"subtotal":{"amount":100000,"currency":"COP"},"tax":{"amount":19000,"currency":"COP"},"total":{"amount":119000,"currency":"COP"}}""")
        // Tres productos, tres ítems de existencias, tres apartados distintos.
        .Secuencia("GET /v1/items",
            """{"id":"i-1","subjectKind":"tienda.producto","subjectId":"p-1","onHand":10,"available":10}""",
            """{"id":"i-2","subjectKind":"tienda.producto","subjectId":"p-2","onHand":10,"available":10}""",
            """{"id":"i-3","subjectKind":"tienda.producto","subjectId":"p-3","onHand":10,"available":10}""")
        .Secuencia("POST /v1/items/i-1/holds", """{"id":"sh-1","quantity":2,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Secuencia("POST /v1/items/i-2/holds", """{"id":"sh-2","quantity":1,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Secuencia("POST /v1/items/i-3/holds", """{"id":"sh-3","quantity":4,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/sh-1/release", """{"id":"sh-1","quantity":2,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":true}""")
        .Ok("POST /v1/holds/sh-2/release", """{"id":"sh-2","quantity":1,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":true}""")
        .Ok("POST /v1/holds/sh-3/release", """{"id":"sh-3","quantity":4,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":true}""")
        // Deshacer un consumo es un AJUSTE, no una liberación: Api.Inventory lo dice y por eso
        // el flujo reescribe la compensación al consumir. Sin estos dos guiones, el test estaría
        // probando el comportamiento viejo.
        .Ok("GET /v1/items/i-1", """{"id":"i-1","subjectKind":"tienda.producto","subjectId":"p-1","onHand":8,"available":8}""")
        .Ok("GET /v1/items/i-2", """{"id":"i-2","subjectKind":"tienda.producto","subjectId":"p-2","onHand":9,"available":9}""")
        .Ok("GET /v1/items/i-3", """{"id":"i-3","subjectKind":"tienda.producto","subjectId":"p-3","onHand":6,"available":6}""")
        .Ok("POST /v1/items/i-1/adjust", """{"id":"i-1","subjectKind":"tienda.producto","subjectId":"p-1","onHand":10,"available":10}""")
        .Ok("POST /v1/items/i-2/adjust", """{"id":"i-2","subjectKind":"tienda.producto","subjectId":"p-2","onHand":10,"available":10}""")
        .Ok("POST /v1/items/i-3/adjust", """{"id":"i-3","subjectKind":"tienda.producto","subjectId":"p-3","onHand":10,"available":10}""")
        .Ok("POST /v1/holds/sh-1/consume", """{"id":"sh-1","quantity":2,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/sh-2/consume", """{"id":"sh-2","quantity":1,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/sh-3/consume", """{"id":"sh-3","quantity":4,"expiresAtUtc":"2026-03-02T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/orders", """{"id":"o-1","status":"Placed","total":{"amount":119000,"currency":"COP"}}""")
        .Ok("GET /v1/orders/o-1", """{"id":"o-1","status":"Placed","total":{"amount":119000,"currency":"COP"}}""")
        .Ok("POST /v1/orders/o-1/cancel", """{"id":"o-1","status":"Cancelled","total":{"amount":119000,"currency":"COP"}}""")
        .Ok("POST /v1/orders/o-1/fulfill", """{"id":"o-1","status":"Fulfilled","total":{"amount":119000,"currency":"COP"}}""")
        .Ok("POST /v1/payments", """{"id":"pg1","status":"Authorized","amount":{"amount":119000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/capture", """{"id":"pg1","status":"Captured","amount":{"amount":119000,"currency":"COP"},"refundable":{"amount":119000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/void", """{"id":"pg1","status":"Voided","amount":{"amount":119000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/refund", """{"id":"pg1","status":"Captured","amount":{"amount":119000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":119000,"currency":"COP"},"refundable":{"amount":119000,"currency":"COP"}}""")
        .Ok("POST /v1/shipments", """{"id":"env-1","status":"Created"}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    private static AlertOptions Guardia() => new()
    {
        ToKind = "tienda.guardia",
        ToId = "operaciones",
        Address = "guardia@ejemplo.co",
        TemplateKey = "tienda.compensacion.colgada",
    };

    private static Contexto Nuevo(CapacidadesFalsas caps, AlertOptions? alertas = null)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(caps);
        var api = new TiendaCapabilities(fabrica);
        var vocabulario = new SagaVocabulary("tienda", "la compra");
        var comp = new Compensator<PurchaseSaga>(new TiendaCompensationExecutor(api), reloj, NullLogger<Compensator<PurchaseSaga>>.Instance);
        var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(alertas ?? Guardia()));
        var motor = new SagaEngine<PurchaseSaga>(sagas, comp, aviso, vocabulario, reloj,
            NullLogger<SagaEngine<PurchaseSaga>>.Instance);
        return new Contexto(
            new PurchaseFlow(api, motor, reloj, NullLogger<PurchaseFlow>.Instance),
            caps, sagas, reloj, motor);
    }

    private static Task<Result<PurchaseSaga>> Comprar(PurchaseFlow flow, string id = "compra-1")
        => flow.BuyAsync("c1", id, CancellationToken.None);

    private static Task<Result<PurchaseSaga>> Confirmar(PurchaseFlow flow, string id = "compra-1")
        => flow.ConfirmAsync(id, Direccion, "servientrega", CancellationToken.None);

    // ── El camino feliz, para tener contra qué contrastar ────────────────────

    [Fact]
    public async Task El_camino_feliz_deja_la_compra_despachada_y_nada_pendiente()
    {
        var ctx = Nuevo(Feliz());

        var comprada = await Comprar(ctx.Flow);
        var confirmada = await Confirmar(ctx.Flow);

        Assert.Equal(SagaStatus.Completed, confirmada.Value.Status);
        Assert.Equal("env-1", confirmada.Value.ShipmentId);
        Assert.Empty(confirmada.Value.Pending());
        Assert.Equal(3, comprada.Value.Holds.Count);
        Assert.Equal(3, ctx.Caps.Veces("POST", "/consume"));
    }

    // ── Lo que Tienda estresa y Salud no ────────────────────────────────────

    [Fact]
    public async Task El_numero_de_compensaciones_NO_es_fijo_y_al_motor_no_le_importa()
    {
        // Es la razón de haber elegido Tienda como segundo: una cita tiene como mucho dos
        // compensaciones, y una máquina de sagas moldeada sobre eso lo habría disimulado.
        var ctx = Nuevo(Feliz());

        var comprada = await Comprar(ctx.Flow);

        // Tres apartados de existencias + el pedido + la autorización.
        Assert.Equal(5, comprada.Value.Pending().Count);
        Assert.Equal(3, comprada.Value.Pending().Count(c => c.Kind == TiendaCompensations.ReleaseStockHold));
        Assert.Single(comprada.Value.Pending(), c => c.Kind == TiendaCompensations.CancelOrder);
        Assert.Single(comprada.Value.Pending(), c => c.Kind == TiendaCompensations.VoidPayment);
    }

    [Fact]
    public async Task Si_una_linea_no_tiene_existencias_se_sueltan_las_YA_apartadas()
    {
        // El caso que un dominio de dos pasos no puede tener: fallar a mitad de una lista.
        // Sin esto, dos apartados quedan colgados en Inventory bloqueando mercancía de otros
        // compradores hasta que venzan por TTL.
        var caps = Feliz().Falla("POST /v1/items/i-3/holds", HttpStatusCode.Conflict, "inventory.out_of_stock");
        var ctx = Nuevo(caps);

        var r = await Comprar(ctx.Flow);

        Assert.Equal("inventory.out_of_stock", r.Rejection!.Code);
        Assert.Equal(1, caps.Veces("POST", "/holds/sh-1/release"));
        Assert.Equal(1, caps.Veces("POST", "/holds/sh-2/release"));
        Assert.Equal(SagaStatus.Compensated, ctx.Flow.Get("compra-1").Value.Status);
        // Y no se llegó a tocar plata: es para lo que sirve apartar antes de cobrar.
        Assert.Equal(0, caps.Veces("POST", "/v1/payments"));
    }

    [Fact]
    public async Task Si_el_despacho_falla_DESPUES_de_capturar_se_devuelve_y_se_anula_el_pedido()
    {
        // El escenario entero de este dominio: se cobró, la mercancía salió del inventario y no
        // hay envío. Cinco compensaciones de tres tipos distintos, todas en la misma vuelta.
        var caps = Feliz().Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        var r = await Confirmar(ctx.Flow);

        Assert.False(r.IsOk);
        Assert.Equal("fulfillment.no_carrier", r.Rejection!.Code);
        Assert.Equal(1, caps.Veces("POST", "/refund"));
        Assert.Equal(0, caps.Veces("POST", "/void"));       // ya se capturó: liberar no aplica
        Assert.Equal(3, caps.Veces("POST", "/adjust"));     // ya se consumió: devolver, no soltar
        Assert.Equal(0, caps.Veces("POST", "/release"));
        // Y el pedido SÍ se puede anular, porque cerrarlo va después de despachar.
        Assert.Equal(1, caps.Veces("POST", "/orders/o-1/cancel"));
        Assert.Equal(0, caps.Veces("POST", "/orders/o-1/fulfill"));
        Assert.Equal(SagaStatus.Compensated, ctx.Flow.Get("compra-1").Value.Status);
    }

    [Fact]
    public async Task Un_pedido_YA_anulado_se_da_por_compensado_y_no_se_anula_dos_veces()
    {
        // El reintento donde la anulación sí había salido pero no llegó a anotarse. Sin esto,
        // Api.Orders lo rechazaría por conflicto y la compensación quedaría colgada sobre algo
        // que ya estaba resuelto.
        var caps = Feliz()
            .Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier")
            .Ok("GET /v1/orders/o-1", """{"id":"o-1","status":"Cancelled","total":{"amount":119000,"currency":"COP"}}""");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);

        Assert.Equal(0, caps.Veces("POST", "/orders/o-1/cancel"));
        Assert.Equal(SagaStatus.Compensated, ctx.Flow.Get("compra-1").Value.Status);
    }

    [Fact]
    public async Task Cada_apartado_avanza_a_su_ritmo_cuando_solo_UNA_capacidad_esta_caida()
    {
        // Inventory caída y Payments viva: la devolución sale al primer intento y los tres
        // apartados se quedan reintentando. Que no se esperen entre sí es lo que hace que una
        // capacidad caída no bloquee la recuperación de las demás.
        var caps = Feliz()
            .Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier")
            .Caida("POST /v1/items/i-1/adjust")
            .Caida("POST /v1/items/i-2/adjust")
            .Caida("POST /v1/items/i-3/adjust");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);

        var saga = ctx.Flow.Get("compra-1").Value;
        Assert.Equal(SagaStatus.Compensating, saga.Status);
        Assert.Equal(3, saga.Pending().Count);
        Assert.All(saga.Pending(), c => Assert.Equal(TiendaCompensations.RestockItem, c.Kind));
        Assert.Equal(1, caps.Veces("POST", "/refund"));
        Assert.Equal(1, caps.Veces("POST", "/orders/o-1/cancel"));
    }

    [Fact]
    public async Task El_barrido_COMPLETA_los_tres_apartados_cuando_Inventory_vuelve()
    {
        var caps = Feliz()
            .Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier")
            .Caida("POST /v1/items/i-1/adjust")
            .Caida("POST /v1/items/i-2/adjust")
            .Caida("POST /v1/items/i-3/adjust");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);

        // Vuelve Inventory.
        foreach (var i in new[] { "i-1", "i-2", "i-3" })
        {
            caps.Ok($"POST /v1/items/{i}/adjust", $$"""{"id":"{{i}}","subjectKind":"tienda.producto","subjectId":"p","onHand":10,"available":10}""");
        }
        ctx.Reloj.Avanzar(Compensator<PurchaseSaga>.Backoff(1) + TimeSpan.FromSeconds(1));
        await ctx.Motor.CompensateAsync("compra-1", "barrido", CancellationToken.None);

        Assert.Equal(SagaStatus.Compensated, ctx.Flow.Get("compra-1").Value.Status);
        Assert.Empty(ctx.Flow.PendingCompensations());
    }

    [Fact]
    public async Task Se_CAPTURA_antes_de_consumir_las_existencias()
    {
        // Es la decisión del dominio, y va en la dirección contraria a la intuición. Al revés
        // —consumir y luego capturar— un cobro rechazado dejaría mercancía fuera del inventario
        // sin nadie que la pagara, y devolverla al stock exige que alguien la cuente a mano. Con
        // este orden el fallo deja plata cobrada sin mercancía, que se devuelve sola.
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);

        var orden = ctx.Caps.Llamadas.Select((l, i) => (l.Path, i)).ToList();
        var captura = orden.First(x => x.Path.Contains("/capture", StringComparison.Ordinal)).i;
        var primerConsumo = orden.First(x => x.Path.Contains("/consume", StringComparison.Ordinal)).i;
        var despacho = orden.First(x => x.Path == "/v1/shipments").i;
        var cierre = orden.First(x => x.Path.Contains("/checkout", StringComparison.Ordinal)).i;

        Assert.True(captura < primerConsumo, "capturar tiene que ir antes de consumir existencias");
        var cierraPedido = orden.First(x => x.Path.Contains("/fulfill", StringComparison.Ordinal)).i;
        Assert.True(primerConsumo < despacho, "consumir tiene que ir antes de despachar");
        Assert.True(despacho < cierraPedido,
            "cerrar el pedido va DESPUÉS de despachar: cerrarlo antes deja CancelOrder condenada a fallar");
        Assert.True(cierraPedido < cierre, "cerrar la canasta va de última: es lo irreversible para el comprador");
    }

    // ── Que la máquina promovida se comporta igual acá ───────────────────────

    [Fact]
    public async Task Una_compra_sana_NO_es_trabajo_para_el_barrido()
    {
        // La misma guarda que en Salud, sobre un dominio que el motor nunca vio. Si esto se
        // rompiera, toda compra sin confirmar soltaría su mercancía y se anularía sola.
        var ctx = Nuevo(Feliz());

        var comprada = await Comprar(ctx.Flow);

        Assert.Equal(SagaStatus.Running, comprada.Value.Status);
        Assert.Equal(5, comprada.Value.Pending().Count);
        Assert.False(comprada.Value.NeedsSweep());
        Assert.Empty(ctx.Flow.PendingCompensations());
    }

    [Fact]
    public async Task Rendirse_avisa_a_la_guardia_UNA_vez_con_el_origen_del_dominio()
    {
        // El aviso llega de Bff.Core, pero tiene que decir de qué sistema viene: una guardia
        // que atiende Salud y Tienda con la misma dirección necesita saber dónde mirar.
        var caps = Feliz()
            .Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier")
            .Caida("POST /v1/items/i-1/adjust")
            .Caida("POST /v1/items/i-2/adjust")
            .Caida("POST /v1/items/i-3/adjust");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);
        for (var i = 1; i <= Compensator<PurchaseSaga>.MaxAttempts; i++)
        {
            ctx.Reloj.Avanzar(Compensator<PurchaseSaga>.Backoff(i) + TimeSpan.FromSeconds(1));
            await ctx.Motor.CompensateAsync("compra-1", "barrido", CancellationToken.None);
        }

        var saga = ctx.Flow.Get("compra-1").Value;
        Assert.Equal(SagaStatus.CompensationFailed, saga.Status);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/deliveries"));

        var aviso = ctx.Caps.Llamadas.Single(l => l.Path == "/v1/deliveries").Body!;
        Assert.Contains("\"origen\":\"tienda\"", aviso, StringComparison.Ordinal);
        Assert.Contains("compra-1", aviso, StringComparison.Ordinal);
        foreach (var marcador in CompensationAlert.Placeholders)
        {
            Assert.Contains($"\"{marcador}\":", aviso, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task El_reintento_manual_rearma_los_TRES_apartados_rendidos()
    {
        var caps = Feliz()
            .Falla("POST /v1/shipments", HttpStatusCode.Conflict, "fulfillment.no_carrier")
            .Caida("POST /v1/items/i-1/adjust")
            .Caida("POST /v1/items/i-2/adjust")
            .Caida("POST /v1/items/i-3/adjust");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        await Confirmar(ctx.Flow);
        for (var i = 1; i <= Compensator<PurchaseSaga>.MaxAttempts; i++)
        {
            ctx.Reloj.Avanzar(Compensator<PurchaseSaga>.Backoff(i) + TimeSpan.FromSeconds(1));
            await ctx.Motor.CompensateAsync("compra-1", "barrido", CancellationToken.None);
        }

        // Alguien arregla la causa.
        foreach (var i in new[] { "i-1", "i-2", "i-3" })
        {
            caps.Ok($"POST /v1/items/{i}/adjust", $$"""{"id":"{{i}}","subjectKind":"tienda.producto","subjectId":"p","onHand":10,"available":10}""");
        }

        var r = await ctx.Flow.RetryStuckAsync("compra-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
    }

    // ── Lo que la canasta rechaza sola ──────────────────────────────────────

    [Fact]
    public async Task Una_canasta_ya_cerrada_no_se_compra_otra_vez()
    {
        // Sin esto, refrescar la página de confirmación dispararía una segunda compra con una
        // llave distinta: mismo comprador, misma mercancía, dos cobros.
        var caps = Feliz().Ok("GET /v1/carts/c1",
            """{"id":"c1","ownerKind":"tienda.comprador","ownerId":"u-1","checkedOut":true,"open":false,"lines":[]}""");
        var ctx = Nuevo(caps);

        var r = await Comprar(ctx.Flow);

        Assert.Equal("tienda.cart_closed", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("POST", "/holds"));
    }

    [Fact]
    public async Task Una_canasta_vacia_no_se_compra()
    {
        var caps = Feliz().Ok("GET /v1/carts/c1",
            """{"id":"c1","ownerKind":"tienda.comprador","ownerId":"u-1","checkedOut":false,"open":true,"lines":[]}""");
        var ctx = Nuevo(caps);

        var r = await Comprar(ctx.Flow);

        Assert.Equal("tienda.cart_empty", r.Rejection!.Code);
        Assert.Equal(0, caps.Veces("POST", "/v1/orders"));
    }

    // ── Idempotencia ────────────────────────────────────────────────────────

    [Fact]
    public async Task Las_llaves_de_los_apartados_llevan_el_ITEM_y_no_la_posicion()
    {
        // Si llevaran el índice de la línea, un comprador que reordena su canasta entre dos
        // intentos apartaría dos veces la misma mercancía: la llave dejaría de reconocer nada.
        var ctx = Nuevo(Feliz());

        await Comprar(ctx.Flow, "compra-fija");

        var llaves = ctx.Caps.Llamadas.Where(l => l.Path.Contains("/holds", StringComparison.Ordinal))
            .Select(l => l.Key).ToList();

        Assert.Contains(IdempotencyKey.From("compra-fija", "hold:i-1").Value, llaves);
        Assert.Contains(IdempotencyKey.From("compra-fija", "hold:i-2").Value, llaves);
        Assert.Contains(IdempotencyKey.From("compra-fija", "hold:i-3").Value, llaves);
    }

    [Fact]
    public async Task Repetir_COMPRAR_con_el_mismo_id_no_aparta_dos_veces()
    {
        var ctx = Nuevo(Feliz());

        await Comprar(ctx.Flow, "misma");
        await Comprar(ctx.Flow, "misma");

        Assert.Equal(3, ctx.Caps.Veces("POST", "/holds"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/orders"));
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_captura_ni_consume_dos_veces()
    {
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        await Confirmar(ctx.Flow);
        var segunda = await Confirmar(ctx.Flow);

        Assert.True(segunda.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(3, ctx.Caps.Veces("POST", "/consume"));
    }

    [Fact]
    public async Task La_canasta_se_cierra_de_ULTIMA_y_su_fallo_no_deshace_la_compra()
    {
        // La mercancía salió, el pedido está cerrado y el envío existe. Deshacer todo eso
        // porque una canasta no cerró sería desproporcionado — y su propio TTL la barre.
        var caps = Feliz().Falla("POST /v1/carts/c1/checkout", HttpStatusCode.Conflict, "cart.already_closed");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);
        var r = await Confirmar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Equal(0, caps.Veces("POST", "/refund"));
    }
}
