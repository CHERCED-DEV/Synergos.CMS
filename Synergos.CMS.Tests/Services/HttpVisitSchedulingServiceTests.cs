using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La visita al inmueble apartada contra <c>Api.Booking</c>, sin orquestador (HU #33a).
/// </summary>
/// <remarks>
/// La capacidad se simula con un <see cref="HttpMessageHandler"/> guionado: es la única forma de
/// provocar a voluntad que conteste «sin cupo» en la franja exacta, o que no conteste.
/// </remarks>
public sealed class HttpVisitSchedulingServiceTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);

    private sealed class Reloj : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class Capacidad : HttpMessageHandler
    {
        public string RecursoJson { get; set; } = """{"id":"res-7","subjectKind":"realty.listado","subjectId":"L1"}""";
        public HttpStatusCode RecursoEstado { get; set; } = HttpStatusCode.OK;

        /// <summary>Franjas (por hora UTC de inicio) que la capacidad dice que NO están libres.</summary>
        public HashSet<int> Tomadas { get; } = new();

        public HttpStatusCode HoldEstado { get; set; } = HttpStatusCode.Created;
        public string HoldCodigo { get; set; } = "booking.insufficient_capacity";

        /// <summary>Si viene, la capacidad no contesta: se cae la conexión.</summary>
        public bool Caida { get; set; }

        public List<(string Method, string Uri, string? Idem, string? Body)> Llamadas { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Caida) throw new HttpRequestException("guionado: caída");

            var uri = req.RequestUri!.PathAndQuery;
            Llamadas.Add((req.Method.Method, uri,
                req.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                req.Content is null ? null : await req.Content.ReadAsStringAsync(ct)));

            if (uri.StartsWith("/v1/resources?", StringComparison.Ordinal))
            {
                return Json(RecursoEstado, RecursoEstado == HttpStatusCode.OK ? RecursoJson : """{"code":"booking.resource_not_found"}""");
            }

            if (uri.Contains("/availability", StringComparison.Ordinal))
            {
                // La hora de inicio viaja en `start`; basta con mirar si alguna tomada aparece.
                var libre = !Tomadas.Any(h => uri.Contains($"T{h:00}%3A00%3A00", StringComparison.OrdinalIgnoreCase));
                return Json(HttpStatusCode.OK,
                    $$"""{"available":{{(libre ? "true" : "false")}},"taken":0,"capacity":1}""");
            }

            if (uri.EndsWith("/confirm", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Created, """{"id":"resv-99","status":"Confirmed"}""");
            }

            if (uri.EndsWith("/v1/holds", StringComparison.Ordinal))
            {
                return HoldEstado == HttpStatusCode.Created
                    ? Json(HttpStatusCode.Created, """{"id":"hold-42"}""")
                    : Json(HoldEstado, $$"""{"code":"{{HoldCodigo}}","detail":"guionado"}""");
            }

            return Json(HttpStatusCode.NotFound, """{"code":"stub.no_route"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode code, string body)
            => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class Fabrica : IHttpClientFactory
    {
        private readonly Capacidad _h;
        public Fabrica(Capacidad h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://booking.local/") };
    }

    private static (HttpVisitSchedulingService Svc, Capacidad Cap) Nuevo()
    {
        var cap = new Capacidad();
        var svc = new HttpVisitSchedulingService(
            new Fabrica(cap),
            new OptionsMonitorFalso(new RealtySettings()),
            new Reloj(),
            NullLogger<HttpVisitSchedulingService>.Instance);
        return (svc, cap);
    }

    private sealed class OptionsMonitorFalso : IOptionsMonitor<RealtySettings>
    {
        public OptionsMonitorFalso(RealtySettings v) => CurrentValue = v;
        public RealtySettings CurrentValue { get; }
        public RealtySettings Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<RealtySettings, string?> l) => null;
    }

    private static VisitContact Ana => new("Ana Pérez", "Ana@Ejemplo.CO", "3001234567");

    // ── La agenda ───────────────────────────────────────────────────────────

    [Fact]
    public async Task La_agenda_la_deriva_el_CMS_y_el_cupo_lo_dice_la_capacidad()
    {
        var (svc, cap) = Nuevo();
        cap.Tomadas.Add(9);   // la capacidad dice que las de las 9 ya están

        var slots = await svc.GetSlotsAsync("L1");

        // Las franjas son las mismas que ofrece el motor en proceso: eso es lo que hace que el
        // vertical se vea igual en los dos modos.
        Assert.Equal(VisitAgenda.For("L1", Ahora).Select(s => s.Id), slots.Select(s => s.Id));
        Assert.All(slots.Where(s => s.StartUtc.Hour == 9), s => Assert.False(s.Available));
        Assert.All(slots.Where(s => s.StartUtc.Hour == 11), s => Assert.True(s.Available));
    }

    [Fact]
    public async Task Con_la_capacidad_CAIDA_la_ficha_se_sigue_viendo()
    {
        // Esconder el inmueble porque el servicio de cupo no contesta castiga al visitante por un
        // problema que no es suyo — y la ficha es lo que vende. Degrada: se ofrece la agenda y el
        // intento de agendar será el que falle, con su motivo.
        var (svc, cap) = Nuevo();
        cap.Caida = true;

        var slots = await svc.GetSlotsAsync("L1");

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s.Available));
    }

    [Fact]
    public async Task Sin_recurso_registrado_la_agenda_se_ve_pero_agendar_NO_pasa()
    {
        // Es un paso de DESPLIEGUE que falta, no un fallo del código: el inmueble no tiene recurso
        // en Api.Booking. Se ve la agenda, y agendar dice que no de una forma accionable.
        var (svc, cap) = Nuevo();
        cap.RecursoEstado = HttpStatusCode.NotFound;

        Assert.NotEmpty(await svc.GetSlotsAsync("L1"));

        var slot = VisitAgenda.For("L1", Ahora)[0].Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BookAsync("L1", slot, Ana));
    }

    // ── Agendar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agendar_aparta_y_CONFIRMA()
    {
        var (svc, cap) = Nuevo();
        var slot = VisitAgenda.For("L1", Ahora)[0];

        var visita = await svc.BookAsync("L1", slot.Id, Ana);

        Assert.Equal("visit_resv-99", visita.VisitId);
        Assert.Equal("Confirmed", visita.Status);
        Assert.Contains(cap.Llamadas, l => l.Method == "POST" && l.Uri.EndsWith("/v1/holds", StringComparison.Ordinal));
        Assert.Contains(cap.Llamadas, l => l.Method == "POST" && l.Uri.EndsWith("/confirm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_ventana_que_viaja_es_la_de_la_franja()
    {
        // Si viajara otra, la capacidad apartaría una hora distinta de la que el visitante eligió
        // — y nadie lo vería hasta que dos personas llegaran al mismo inmueble.
        var (svc, cap) = Nuevo();
        var slot = VisitAgenda.For("L1", Ahora)[0];

        await svc.BookAsync("L1", slot.Id, Ana);

        var cuerpo = cap.Llamadas.Single(l => l.Uri.EndsWith("/v1/holds", StringComparison.Ordinal)).Body!;
        Assert.Contains(slot.StartUtc.ToString("yyyy-MM-ddTHH:mm"), cuerpo, StringComparison.Ordinal);
        Assert.Contains((slot.StartUtc + VisitAgenda.Duracion).ToString("yyyy-MM-ddTHH:mm"), cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_la_capacidad_NO_le_viaja_el_correo()
    {
        // Api.Booking cuenta cupo; no necesita saber quién es. Lo que viaja es un seudónimo
        // estable — no es anonimato, es no esparcir lo que no hace falta esparcir.
        var (svc, cap) = Nuevo();
        var slot = VisitAgenda.For("L1", Ahora)[0];

        await svc.BookAsync("L1", slot.Id, Ana);

        var cuerpo = cap.Llamadas.Single(l => l.Uri.EndsWith("/v1/holds", StringComparison.Ordinal)).Body!;
        Assert.DoesNotContain("ejemplo.co", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana", cuerpo, StringComparison.Ordinal);
        Assert.Contains("realty.interesado", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reagendar_la_MISMA_franja_reusa_la_misma_llave()
    {
        // Es lo que hace que este cliente no necesite almacén propio: la idempotencia sale del par
        // listado+franja, y el registro de la visita es el de la capacidad, que es su dueña.
        var (svc, cap) = Nuevo();
        var slot = VisitAgenda.For("L1", Ahora)[0];

        await svc.BookAsync("L1", slot.Id, Ana);
        await svc.BookAsync("L1", slot.Id, Ana);

        var llaves = cap.Llamadas
            .Where(l => l.Uri.EndsWith("/v1/holds", StringComparison.Ordinal))
            .Select(l => l.Idem).Distinct(StringComparer.Ordinal).ToList();

        Assert.Single(llaves);
        Assert.NotNull(llaves[0]);
    }

    [Fact]
    public async Task Dos_franjas_distintas_NO_comparten_llave()
    {
        // El error espejo del anterior, y el caro: con una llave común, la segunda visita
        // devolvería la primera y el visitante creería tener una cita que no existe.
        var (svc, cap) = Nuevo();
        var agenda = VisitAgenda.For("L1", Ahora);

        await svc.BookAsync("L1", agenda[0].Id, Ana);
        await svc.BookAsync("L1", agenda[1].Id, Ana);

        var llaves = cap.Llamadas
            .Where(l => l.Uri.EndsWith("/v1/holds", StringComparison.Ordinal))
            .Select(l => l.Idem).Distinct(StringComparer.Ordinal).ToList();

        Assert.Equal(2, llaves.Count);
    }

    // ── Los noes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_franja_que_no_es_del_listado_no_llega_a_la_capacidad()
    {
        var (svc, cap) = Nuevo();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.BookAsync("L1", "inventada", Ana));
        Assert.DoesNotContain(cap.Llamadas, l => l.Method == "POST");
    }

    [Fact]
    public async Task Sin_cupo_el_visitante_sabe_QUE_hacer()
    {
        // «No disponible» para los tres motivos sería una regresión frente a lo que dice el motor
        // en proceso: cada no lleva a una acción distinta (HU #33 §3).
        var (svc, cap) = Nuevo();
        cap.HoldEstado = HttpStatusCode.Conflict;
        cap.HoldCodigo = "booking.insufficient_capacity";
        var slot = VisitAgenda.For("L1", Ahora)[0];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BookAsync("L1", slot.Id, Ana));

        Assert.Contains("ya lo tomaron", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fuera_de_agenda_y_en_el_pasado_NO_dicen_lo_mismo()
    {
        var slot = VisitAgenda.For("L1", Ahora)[0];

        foreach (var (codigo, esperado) in new[]
        {
            ("booking.outside_opening_hours", "fuera de la agenda"),
            ("booking.in_the_past", "ya pasó"),
        })
        {
            var (svc, cap) = Nuevo();
            cap.HoldEstado = HttpStatusCode.Conflict;
            cap.HoldCodigo = codigo;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BookAsync("L1", slot.Id, Ana));
            Assert.Contains(esperado, ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Un_403_NO_se_confunde_con_la_llave()
    {
        // SharedKeyAuth emite 401 y NUNCA 403. Tratar el 403 como «llave mala» manda a revisar una
        // configuración correcta mientras el rechazo real pasa desapercibido. Este defecto ya se
        // cometió dos veces en este repo, y lo destapó levantar los procesos.
        var (svc, cap) = Nuevo();
        cap.HoldEstado = HttpStatusCode.Forbidden;
        cap.HoldCodigo = "booking.insufficient_capacity";
        var slot = VisitAgenda.For("L1", Ahora)[0];

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BookAsync("L1", slot.Id, Ana));

        // El motivo de negocio sobrevive: no se lo tragó una rama de autenticación.
        Assert.Contains("ya lo tomaron", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_contacto_no_se_aparta_nada()
    {
        var (svc, cap) = Nuevo();
        var slot = VisitAgenda.For("L1", Ahora)[0];

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.BookAsync("L1", slot.Id, new VisitContact("", "")));
        Assert.Empty(cap.Llamadas);
    }
}
