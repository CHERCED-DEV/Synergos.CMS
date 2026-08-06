using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Eventos.Domain;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra de entradas que se deshace (HU #35).
/// </summary>
/// <remarks>
/// <para><b>Lo que este dominio aporta y los dos anteriores no:</b> el aforo de un evento es un
/// pozo contable, igual que el stock de la tienda — pero con una granularidad que la tienda no
/// tiene. Una butaca nominada es un pozo de UNA unidad; el cupo general es un pozo de
/// cuatrocientas. <c>Api.Inventory</c> no distingue, y ése es justo el punto: si tuviera que
/// distinguir, dejaría de servirle a la tienda al día siguiente.</para>
///
/// <para><b>Y lo que comprueba de la máquina:</b> que el tercer orquestador no necesitó tocarla.
/// Las capacidades se simulan con un <see cref="HttpMessageHandler"/> guionado porque es la única
/// forma de provocar <i>a voluntad</i> que el cobro se capture y el consumo del aforo falle
/// justo después — el instante donde la compensación cambia de carácter.</para>
/// </remarks>
public sealed class TicketingCompensationTests
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
        private readonly Dictionary<string, Queue<string>> _secuencias = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Query, string? Key, string? Body)> Llamadas { get; } = new();

        public CapacidadesFalsas Cuando(string patron, Func<HttpRequestMessage, HttpResponseMessage> responde)
        {
            _rutas[patron] = responde;
            return this;
        }

        public CapacidadesFalsas Ok(string patron, string json)
            => Cuando(patron, _ => Json(HttpStatusCode.OK, json));

        /// <summary>Respuestas distintas para llamadas sucesivas a la misma ruta.</summary>
        public CapacidadesFalsas Secuencia(string patron, params string[] jsons)
        {
            _secuencias[patron] = new Queue<string>(jsons);
            return Cuando(patron, _ =>
            {
                var cola = _secuencias[patron];
                return Json(HttpStatusCode.OK, cola.Count > 1 ? cola.Dequeue() : cola.Peek());
            });
        }

        public CapacidadesFalsas Falla(string patron, HttpStatusCode codigo, string code)
            => Cuando(patron, _ => new HttpResponseMessage(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""",
                    Encoding.UTF8, "application/problem+json"),
            });

        public int Veces(string method, string pathContiene)
            => Llamadas.Count(l => l.Method == method && l.Path.Contains(pathContiene, StringComparison.Ordinal));

        private static HttpResponseMessage Json(HttpStatusCode c, string body)
            => new(c) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var clave = $"{request.Method.Method} {path}";
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);

            lock (Llamadas)
            {
                Llamadas.Add((request.Method.Method, path, request.RequestUri.Query,
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
                    Encoding.UTF8, "application/problem+json"),
            };
        }

        private static bool Coincide(string patron, string clave)
            => patron.EndsWith('*')
                ? clave.StartsWith(patron[..^1], StringComparison.Ordinal)
                : string.Equals(patron, clave, StringComparison.Ordinal);
    }

    /// <summary>
    /// El doble se comporta como <c>Api.Inventory</c>: <b>soltar un apartado ya consumido se
    /// rechaza</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Sin esto el test codifica la misma suposición equivocada que el código.</b> La
    /// primera versión del doble aceptaba cualquier <c>release</c>, así que mutar el flujo para
    /// que consumiera ANTES de capturar seguía en verde — y en producción esa mutación deja el
    /// aforo perdido: la compensación intenta soltar lo que ya se consumió, la capacidad contesta
    /// <c>hold_already_consumed</c>, y las butacas no vuelven.</para>
    ///
    /// <para>Es el patrón que ya mordió en este repo con otros disfraces: un doble más permisivo
    /// que la cosa real no prueba nada.</para>
    /// </remarks>
    private static CapacidadesFalsas ComoInventoryDeVerdad(CapacidadesFalsas caps, params string[] holds)
    {
        var consumidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in holds)
        {
            var id = h;
            caps.Cuando($"POST /v1/holds/{id}/consume", _ =>
            {
                consumidos.Add(id);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"id":"{{id}}","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""",
                        Encoding.UTF8, "application/json"),
                };
            });
            caps.Cuando($"POST /v1/holds/{id}/release", _ => consumidos.Contains(id)
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        """{"code":"inventory.hold_already_consumed","detail":"devolver existencias es un ajuste, no una liberación"}""",
                        Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"id":"{{id}}","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":true}""",
                        Encoding.UTF8, "application/json"),
                });
        }
        return caps;
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly CapacidadesFalsas _handler;
        public FabricaFalsa(CapacidadesFalsas h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private sealed class MemoriaSagas : ISagaStore<TicketingSaga>
    {
        private readonly Dictionary<string, TicketingSaga> _s = new(StringComparer.Ordinal);
        public TicketingSaga? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<TicketingSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public IReadOnlyList<TicketingSaga> StartedBefore(DateTimeOffset limite)
            => _s.Values.Where(x => x.Status == SagaStatus.Running && x.StartedAtUtc < limite).ToList();
        public void Put(TicketingSaga saga) => _s[saga.Id] = saga;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Comprador = Ref.Create("eventos.comprador", "u-1");

    private sealed record Contexto(TicketingFlow Flow, CapacidadesFalsas Caps, MemoriaSagas Sagas, RelojFalso Reloj);

    /// <summary>
    /// Dos butacas nominadas y un cupo general: las dos granularidades en la misma compra.
    /// </summary>
    /// <remarks>
    /// Es el caso que la HU #35 tenía sin decidir, y probarlo mezclado no es cosmética: si las
    /// dos formas no cupieran en la misma transacción, el vertical tendría que partir la compra
    /// en dos y el comprador vería dos cobros.
    /// </remarks>
    private static IReadOnlyList<TicketLine> Mezcla => new[]
    {
        new TicketLine("vip", "A-14", 1),
        new TicketLine("vip", "A-15", 1),
        new TicketLine("general", null, 3),
    };

    private static CapacidadesFalsas Feliz() => new CapacidadesFalsas()
        .Ok("POST /v1/quotes", """{"subtotal":{"amount":400000,"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":400000,"currency":"COP"}}""")
        // Tres pozos: dos butacas de UNA unidad y un cupo general de cuatrocientas.
        .Secuencia("GET /v1/items",
            """{"id":"po-a14","subjectKind":"eventos.aforo","subjectId":"e1/vip/A-14","onHand":1,"available":1}""",
            """{"id":"po-a15","subjectKind":"eventos.aforo","subjectId":"e1/vip/A-15","onHand":1,"available":1}""",
            """{"id":"po-gen","subjectKind":"eventos.aforo","subjectId":"e1/general","onHand":400,"available":400}""")
        .Ok("POST /v1/items/po-a14/holds", """{"id":"ah-1","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/items/po-a15/holds", """{"id":"ah-2","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/items/po-gen/holds", """{"id":"ah-3","quantity":3,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/ah-1/release", """{"id":"ah-1","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":true}""")
        .Ok("POST /v1/holds/ah-2/release", """{"id":"ah-2","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":true}""")
        .Ok("POST /v1/holds/ah-3/release", """{"id":"ah-3","quantity":3,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":true}""")
        .Ok("POST /v1/holds/ah-1/consume", """{"id":"ah-1","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/ah-2/consume", """{"id":"ah-2","quantity":1,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/holds/ah-3/consume", """{"id":"ah-3","quantity":3,"expiresAtUtc":"2026-08-05T10:15:00+00:00","released":false}""")
        .Ok("POST /v1/items/po-a14/adjust", """{"id":"po-a14","subjectKind":"eventos.aforo","subjectId":"e1/vip/A-14","onHand":1,"available":1}""")
        .Ok("POST /v1/items/po-a15/adjust", """{"id":"po-a15","subjectKind":"eventos.aforo","subjectId":"e1/vip/A-15","onHand":1,"available":1}""")
        .Ok("POST /v1/items/po-gen/adjust", """{"id":"po-gen","subjectKind":"eventos.aforo","subjectId":"e1/general","onHand":400,"available":400}""")
        .Ok("POST /v1/payments", """{"id":"pg1","status":"Authorized","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/capture", """{"id":"pg1","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/void", """{"id":"pg1","status":"Voided","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("POST /v1/payments/pg1/refund", """{"id":"pg1","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""")
        .Ok("GET /v1/payments/pg1", """{"id":"pg1","status":"Captured","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"}}""")
        .Ok("POST /v1/deliveries", """{"id":"d1","status":"Sent"}""");

    /// <summary>El guion feliz, pero con <c>Api.Inventory</c> comportándose como la de verdad.</summary>
    private static CapacidadesFalsas FelizEstricto() => ComoInventoryDeVerdad(Feliz(), "ah-1", "ah-2", "ah-3");

    private static Contexto Nuevo(CapacidadesFalsas caps)
    {
        var sagas = new MemoriaSagas();
        var reloj = new RelojFalso(Ahora);
        var fabrica = new FabricaFalsa(caps);
        var api = new EventosCapabilities(fabrica);
        var vocabulario = new SagaVocabulary("eventos", "la compra de entradas");
        var comp = new Compensator<TicketingSaga>(
            new EventosCompensationExecutor(api), reloj, NullLogger<Compensator<TicketingSaga>>.Instance);
        var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions
        {
            ToKind = "eventos.guardia",
            ToId = "operaciones",
            Address = "guardia@ejemplo.co",
            TemplateKey = "eventos.compensacion.colgada",
        }));
        var motor = new SagaEngine<TicketingSaga>(sagas, comp, aviso, vocabulario, reloj,
            NullLogger<SagaEngine<TicketingSaga>>.Instance);

        return new Contexto(
            new TicketingFlow(api, motor, reloj, NullLogger<TicketingFlow>.Instance), caps, sagas, reloj);
    }

    private static Task<Result<TicketingSaga>> Comprar(TicketingFlow flow, string id = "compra-1")
        => flow.BuyAsync("e1", Comprador, Mezcla, id, CancellationToken.None);

    // ── Las dos granularidades ──────────────────────────────────────────────

    [Fact]
    public async Task La_butaca_y_el_cupo_general_son_el_MISMO_pozo_contable()
    {
        // La decisión que traía la HU #35. Api.Inventory no distingue: la granularidad va en el
        // identificador del sujeto, y las dos formas caben en la misma compra.
        var ctx = Nuevo(Feliz());

        var r = await Comprar(ctx.Flow);

        Assert.True(r.IsOk);
        var sujetos = ctx.Caps.Llamadas
            .Where(l => l.Method == "GET" && l.Path.EndsWith("/v1/items", StringComparison.Ordinal))
            .Select(l => Uri.UnescapeDataString(l.Query ?? string.Empty))
            .ToList();

        Assert.Contains(sujetos, q => q.Contains("subjectId=e1/vip/A-14", StringComparison.Ordinal));
        Assert.Contains(sujetos, q => q.Contains("subjectId=e1/general", StringComparison.Ordinal));
        // Y el sujeto del cupo general NO lleva butaca: si la llevara, cada entrada general sería
        // su propio pozo y el aforo dejaría de ser una cuenta.
        Assert.DoesNotContain(sujetos, q => q.Contains("subjectId=e1/general/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_la_capacidad_NO_le_viaja_la_palabra_localidad_ni_butaca()
    {
        // CLAUDE.md §12: la capacidad es dueña del CUÁNTO, no del QUÉ. `tier` y `seat` son
        // sustantivos de este dominio y viven en la saga para poder contestarle al CMS — pero lo
        // que cruza el cable es un identificador de pozo y una cantidad.
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        var cuerpos = ctx.Caps.Llamadas.Where(l => l.Body is not null).Select(l => l.Body!).ToList();

        Assert.All(cuerpos, b => Assert.DoesNotContain("\"tier\"", b, StringComparison.Ordinal));
        Assert.All(cuerpos, b => Assert.DoesNotContain("\"seat\"", b, StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_precio_se_cotiza_por_LOCALIDAD_y_no_por_butaca()
    {
        // Dos butacas de la misma localidad valen lo mismo. Cotizar por butaca obligaría al
        // organizador a cargar un precio por asiento, que es lo que nadie hace.
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        var cotizacion = ctx.Caps.Llamadas.Single(l => l.Path.EndsWith("/v1/quotes", StringComparison.Ordinal)).Body!;

        // EXACTA y no «contiene»: con `Contains`, un sujeto `e1/vip/A-14` también pasaría — que
        // es justo lo que este test existe para impedir.
        Assert.Contains("\"subjectId\":\"e1/vip\"", cotizacion, StringComparison.Ordinal);
        Assert.DoesNotContain("A-14", cotizacion, StringComparison.Ordinal);
        // Las dos butacas VIP se agrupan en UNA línea de cotización con cantidad 2.
        Assert.Contains("\"quantity\":2", cotizacion, StringComparison.Ordinal);
    }

    // ── El camino feliz ─────────────────────────────────────────────────────

    [Fact]
    public async Task Comprar_aparta_y_AUTORIZA_sin_mover_plata()
    {
        var ctx = Nuevo(Feliz());

        var r = await Comprar(ctx.Flow);

        Assert.True(r.IsOk);
        Assert.Equal(3, r.Value.Holds.Count);
        Assert.Equal("pg1", r.Value.PaymentId);
        Assert.Equal(0, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/consume"));
    }

    [Fact]
    public async Task Confirmar_captura_y_CONSUME_el_aforo()
    {
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        var r = await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/capture"));
        Assert.Equal(3, ctx.Caps.Veces("POST", "/consume"));
        Assert.Empty(r.Value.Pending());
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_cobra_dos_veces()
    {
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);
        var otra = await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        Assert.True(otra.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/capture"));
    }

    [Fact]
    public async Task Comprar_dos_veces_con_la_MISMA_llave_no_aparta_dos_veces()
    {
        // La llave de idempotencia es el identificador de la saga: repetir la llamada devuelve
        // la misma compra sin tocar ninguna capacidad.
        var ctx = Nuevo(Feliz());

        var a = await Comprar(ctx.Flow);
        var b = await Comprar(ctx.Flow);

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Equal(3, ctx.Caps.Veces("POST", "/holds"));
    }

    // ── Lo que se deshace ───────────────────────────────────────────────────

    [Fact]
    public async Task Si_el_cobro_NO_autoriza_el_aforo_VUELVE()
    {
        // El caso más común: la tarjeta rechaza. Las tres butacas ya estaban apartadas, y si
        // nadie las soltara quedarían bloqueadas hasta que venciera su TTL — con el evento
        // apareciendo agotado mientras tanto.
        var caps = Feliz().Falla("POST /v1/payments", HttpStatusCode.Conflict, "payments.declined");
        var ctx = Nuevo(caps);

        var r = await Comprar(ctx.Flow);

        Assert.False(r.IsOk);
        Assert.Equal("payments.declined", r.Rejection!.Code);   // el motivo de la capacidad, no uno propio
        Assert.Equal(3, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(SagaStatus.Compensated, ctx.Sagas.Find("compra-1")!.Status);
    }

    [Fact]
    public async Task Si_una_butaca_NO_se_puede_apartar_se_sueltan_las_ANTERIORES()
    {
        // La tercera línea falla; las dos primeras ya estaban apartadas. «Armada» se convierte en
        // «pendiente» en el instante en que algo falló, no antes.
        var caps = Feliz().Falla("POST /v1/items/po-gen/holds", HttpStatusCode.Conflict, "inventory.out_of_stock");
        var ctx = Nuevo(caps);

        var r = await Comprar(ctx.Flow);

        Assert.False(r.IsOk);
        Assert.Equal("inventory.out_of_stock", r.Rejection!.Code);
        Assert.Equal(2, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/adjust"));   // nada se había consumido
    }

    [Fact]
    public async Task Si_la_CAPTURA_falla_se_libera_la_autorizacion_y_vuelve_el_aforo()
    {
        // Con el doble ESTRICTO: si el aforo se consumiera antes de capturar, soltarlo sería
        // imposible —Api.Inventory contesta hold_already_consumed— y las butacas no volverían.
        var caps = FelizEstricto().Falla("POST /v1/payments/pg1/capture", HttpStatusCode.Conflict, "payments.capture_failed");
        var ctx = Nuevo(caps);
        await Comprar(ctx.Flow);

        var r = await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal(3, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/void"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/refund"));   // no se movió plata: no hay qué devolver

        // Y LO QUE DE VERDAD IMPORTA: que quedara DESHECHA, no que se intentara deshacer.
        //
        // Contar llamadas no distingue «lo soltó» de «lo intentó soltar y la capacidad dijo que
        // no». Con el consumo movido antes de la captura, los tres release salen igual y los tres
        // se rechazan con hold_already_consumed — el contador seguía en 3 y el aforo perdido.
        // Esto lo destapó una mutación que pasó en verde.
        var saga = ctx.Sagas.Find("compra-1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(saga.Pending());
    }

    [Fact]
    public async Task Si_el_consumo_falla_DESPUES_de_capturar_se_DEVUELVE_la_plata()
    {
        // ES EL CASO QUE JUSTIFICA TODO ESTO. Ya se movió plata; soltar la autorización lo
        // rechaza Api.Payments, así que la compensación TIENE que haber cambiado de carácter al
        // capturar (feedback_compensation_changes_character). Sin ese cambio, la compensación
        // fallaría siempre y quedaría colgada para siempre por una razón que no tiene nada que
        // ver con el mundo real.
        var caps = Feliz().Falla("POST /v1/holds/ah-3/consume", HttpStatusCode.Conflict, "inventory.hold_expired");
        var ctx = Nuevo(caps);
        await Comprar(ctx.Flow);

        var r = await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/refund"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/void"));
    }

    [Fact]
    public async Task Lo_ya_CONSUMIDO_se_devuelve_con_un_ajuste_y_lo_no_consumido_se_SUELTA()
    {
        // La otra mitad del cambio de carácter, y la que se ve peor: en la misma compensación
        // conviven dos butacas ya consumidas —que hay que devolver al pozo con un ajuste— y una
        // que nunca llegó a consumirse —que basta con soltar—. Tratarlas igual falla la mitad de
        // las veces, y siempre en silencio.
        var caps = Feliz().Falla("POST /v1/holds/ah-3/consume", HttpStatusCode.Conflict, "inventory.hold_expired");
        var ctx = Nuevo(caps);
        await Comprar(ctx.Flow);

        await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        // Las dos primeras se consumieron → ajuste sobre su pozo.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/items/po-a14/adjust"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/items/po-a15/adjust"));
        // La tercera nunca se consumió → se suelta el apartado, no se ajusta el pozo.
        Assert.Equal(1, ctx.Caps.Veces("POST", "/holds/ah-3/release"));
        Assert.Equal(0, ctx.Caps.Veces("POST", "/items/po-gen/adjust"));
    }

    [Fact]
    public async Task La_devolucion_de_aforo_manda_un_DELTA_y_no_lee_el_total_antes()
    {
        // Defecto #30: era un leer-sumar-escribir, y dos devoluciones simultáneas sobre el mismo
        // pozo se pisaban. Que NO haya ninguna lectura del ítem antes del ajuste es lo que
        // distingue el arreglo de la versión vieja — con el guion respondiendo a las dos formas,
        // contar las llamadas es la única manera de verlo.
        var caps = Feliz().Falla("POST /v1/holds/ah-3/consume", HttpStatusCode.Conflict, "inventory.hold_expired");
        var ctx = Nuevo(caps);
        await Comprar(ctx.Flow);

        await ctx.Flow.ConfirmAsync("compra-1", CancellationToken.None);

        Assert.Equal(0, ctx.Caps.Veces("GET", "/items/po-a14"));
        var ajuste = ctx.Caps.Llamadas.First(l => l.Path.Contains("/items/po-a14/adjust", StringComparison.Ordinal));
        Assert.Contains("\"delta\":1", ajuste.Body!, StringComparison.Ordinal);
        Assert.NotNull(ajuste.Key);   // un relativo reintentado suma dos veces: la llave no es opcional
    }

    [Fact]
    public void El_cliente_NO_PUEDE_leer_un_pozo_por_su_id()
    {
        // La otra mitad del defecto #30, y la única que un test de comportamiento no alcanza: no
        // basta con que HOY la devolución no lea antes de ajustar — tiene que ser IMPOSIBLE
        // volver a escribirlo así. `EventosCapabilities` no expone ninguna lectura por
        // identificador, así que el leer-sumar-escribir no se puede reintroducir sin añadir
        // primero un método, que es un cambio que se ve en la revisión.
        var metodos = typeof(EventosCapabilities).GetMethods()
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain("GetAforoAsync", metodos);
        Assert.Contains("RestockAforoAsync", metodos);

        var fuente = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.Bff.Eventos", "Clients", "EventosCapabilities.cs"));
        var codigo = string.Join('\n', fuente.Split('\n').Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
        Assert.DoesNotContain("v1/items/{itemId}\"", codigo, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── Las guardas del dominio ─────────────────────────────────────────────

    [Fact]
    public async Task Una_butaca_nominada_es_UNA_entrada()
    {
        // Sin esta guarda se apartarían tres unidades de un pozo que tiene una, y el rechazo
        // diría «sin cupo» — que es cierto y no explica nada.
        var ctx = Nuevo(Feliz());

        var r = await ctx.Flow.BuyAsync("e1", Comprador,
            new[] { new TicketLine("vip", "A-14", 3) }, "c", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("eventos.seat_is_one", r.Rejection!.Code);
        Assert.Empty(ctx.Caps.Llamadas);   // ni se tocó ninguna capacidad
    }

    [Fact]
    public async Task La_MISMA_butaca_dos_veces_se_rechaza()
    {
        // Es sutil y caro: las dos líneas resolverían el mismo pozo y la MISMA llave de
        // idempotencia, así que el segundo apartado devolvería el primero. La compra parecería
        // tener dos butacas y solo habría una — y el segundo comprador se enteraría en la puerta.
        var ctx = Nuevo(Feliz());

        var r = await ctx.Flow.BuyAsync("e1", Comprador,
            new[] { new TicketLine("vip", "A-14", 1), new TicketLine("vip", "A-14", 1) }, "c", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("eventos.duplicate_seat", r.Rejection!.Code);
    }

    [Fact]
    public async Task Sin_lineas_no_se_compra()
    {
        var ctx = Nuevo(Feliz());

        var r = await ctx.Flow.BuyAsync("e1", Comprador, Array.Empty<TicketLine>(), "c", CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("eventos.no_lines", r.Rejection!.Code);
    }

    [Fact]
    public async Task Hay_TOPE_de_lineas_y_de_entradas_por_linea()
    {
        // Sin tope, una petición con mil líneas dispara mil apartados y mil compensaciones — y el
        // barrido de una sola saga rendida martillearía la capacidad mil veces por vuelta.
        var ctx = Nuevo(Feliz());

        var muchas = Enumerable.Range(0, TicketingFlow.MaxLines + 1)
            .Select(i => new TicketLine($"t{i}", null, 1)).ToList();
        var r1 = await ctx.Flow.BuyAsync("e1", Comprador, muchas, "c1", CancellationToken.None);
        Assert.Equal("eventos.too_many_lines", r1.Rejection!.Code);

        var r2 = await ctx.Flow.BuyAsync("e1", Comprador,
            new[] { new TicketLine("general", null, TicketingFlow.MaxPorLinea + 1) }, "c2", CancellationToken.None);
        Assert.Equal("eventos.bad_quantity", r2.Rejection!.Code);
    }

    // ── Lo que el motor aportó gratis ───────────────────────────────────────

    [Fact]
    public async Task Una_compra_VIVA_lleva_sus_compensaciones_ARMADAS_y_eso_NO_es_trabajo()
    {
        // «Armada no es pendiente» (feedback_compensation_is_data). Una compra en curso tiene
        // tres apartados anotados como deshacibles y va perfectamente: si el barrido los viera
        // como trabajo, soltaría butacas de una compra que el comprador está por confirmar.
        var ctx = Nuevo(Feliz());

        var r = await Comprar(ctx.Flow);

        Assert.NotEmpty(r.Value.Compensations);
        Assert.Empty(ctx.Sagas.WithPendingCompensations());
    }

    [Fact]
    public async Task Cancelar_antes_de_confirmar_devuelve_el_aforo()
    {
        var ctx = Nuevo(Feliz());
        await Comprar(ctx.Flow);

        var r = await ctx.Flow.CancelAsync("compra-1", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Compensated, r.Value.Status);
        Assert.Equal(3, ctx.Caps.Veces("POST", "/release"));
        Assert.Equal(1, ctx.Caps.Veces("POST", "/void"));
    }

    [Fact]
    public async Task Una_compensacion_que_NO_sale_se_reintenta_y_acaba_avisando()
    {
        // Los ocho intentos, el retroceso y el aviso vinieron gratis de Bff.Core: este dominio no
        // escribió ni una línea de eso. Que funcione sin haberlo tocado es la medida de si la
        // promoción valía la pena.
        var caps = Feliz()
            .Falla("POST /v1/payments", HttpStatusCode.Conflict, "payments.declined")
            .Falla("POST /v1/holds/ah-1/release", HttpStatusCode.ServiceUnavailable, "inventory.unavailable");
        var ctx = Nuevo(caps);

        await Comprar(ctx.Flow);

        // Se empuja el reloj hasta agotar el retroceso exponencial.
        for (var i = 0; i < CompensationLimits.MaxAttempts + 2; i++)
        {
            ctx.Reloj.Avanzar(TimeSpan.FromHours(2));
            await ctx.Flow.CancelAsync("compra-1", CancellationToken.None);
        }

        var saga = ctx.Sagas.Find("compra-1")!;
        Assert.Contains(saga.Compensations, c => c.IsStuck);
        Assert.NotNull(saga.AlertedAtUtc);
        Assert.Equal(1, ctx.Caps.Veces("POST", "/v1/deliveries"));   // se avisa UNA vez, no en cada vuelta
    }
}
