using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La cita clínica agendando contra <c>Bff.Salud</c> (HU #25).
/// </summary>
/// <remarks>
/// Lo que se prueba es lo que el cupo compartido obliga a decidir: que dos pacientes sobre el
/// mismo horario den <b>uno ganador y un rechazo con motivo</b>, que reintentar no agende dos
/// veces, y que el sustantivo «médico» se traduzca a recurso <i>de este lado</i> — porque
/// <c>Api.Booking</c> no puede saber que el recurso es una persona.
/// </remarks>
public sealed class HttpClinicalSchedulingServiceTests
{
    private sealed class BffFalso : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Key, string? Body)> Llamadas { get; } = new();
        public HashSet<string> Caidas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public BffFalso Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public BffFalso Falla(string ruta, HttpStatusCode codigo, string code, string detail)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        public BffFalso Caida(string ruta) { Caidas.Add(ruta); return this; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var clave = $"{req.Method.Method} {path}";
            req.Headers.TryGetValues("Idempotency-Key", out var k);
            var body = req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            Llamadas.Add((req.Method.Method, path, k?.FirstOrDefault(), body));

            if (Caidas.Contains(clave)) throw new HttpRequestException("guionado: caída");

            return Task.FromResult(_rutas.TryGetValue(clave, out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa(BffFalso h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(h, disposeHandler: false) { BaseAddress = new Uri("http://salud.local/") };
    }

    private sealed class Mon<T>(T v) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = v;
        public T Get(string? n) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private static readonly DateTime Slot = new(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc);

    private const string CitaOk = """
        {"id":"cita-1","patientKind":"salud.paciente","patientId":"pac-1",
         "professionalKind":"salud.profesional","professionalId":"doc-3",
         "start":"2026-08-10T14:00:00+00:00","end":"2026-08-10T14:30:00+00:00",
         "status":"Completed","total":{"amount":50000,"currency":"COP"},
         "reservationId":"res-9","pendingCompensations":0,"lastError":null}
        """;

    private static BffFalso Feliz() => new BffFalso().Ok("POST /v1/appointments", CitaOk);

    private static HttpClinicalSchedulingService Nuevo(BffFalso bff, SaludSettings? s = null)
    {
        var pacientes = Substitute.For<IPatientRegistry>();
        pacientes.GetAsync("pac-1", Arg.Any<CancellationToken>())
            .Returns(new EhrPatient(
                "pac-1", "Ana Ruiz", "CC-1", "F", new DateOnly(1990, 1, 1), 36,
                "300", "ana@ejemplo.co", "Bogotá", "O+",
                Array.Empty<string>(), Array.Empty<string>(), null, null));

        var medicos = Substitute.For<IDoctorDirectory>();
        medicos.GetAsync("doc-3", Arg.Any<CancellationToken>())
            .Returns(new MedicalDoctor(
                "doc-3", "Dra. Paula Gómez", "Endocrinología", "MP-1", 4.8, 12, null,
                new[] { DayOfWeek.Monday }, 8, 17, SlotMinutes: 30));

        return new HttpClinicalSchedulingService(
            new FabricaFalsa(bff),
            new Mon<SaludSettings>(s ?? new SaludSettings { Mode = "Bff" }),
            pacientes, medicos,
            NullLogger<HttpClinicalSchedulingService>.Instance);
    }

    private static BookAppointmentRequest Pedido() => new("pac-1", "doc-3", Slot);

    // ── Agendar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agendar_manda_la_ventana_completa_y_compone_los_nombres_de_este_lado()
    {
        // El BFF exige start Y end; el CMS solo tiene el inicio del slot, así que la duración
        // sale del directorio (SlotMinutes). Y los nombres NO vienen del BFF: una saga no es un
        // directorio, y pedírselos sería meterle un sustantivo clínico.
        var bff = Feliz();

        var cita = await Nuevo(bff).BookAsync(Pedido());

        Assert.Equal("cita-1", cita.Id);
        Assert.Equal("Ana Ruiz", cita.PatientName);
        Assert.Equal("Dra. Paula Gómez", cita.DoctorName);
        Assert.Equal("Endocrinología", cita.Specialty);
        Assert.Equal(Slot.AddMinutes(30), cita.EndUtc);
        Assert.Equal("booked", cita.Status);

        var body = bff.Llamadas.Single(l => l.Path == "/v1/appointments").Body!;
        Assert.Contains("\"start\"", body, StringComparison.Ordinal);
        Assert.Contains("\"end\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Se_manda_el_MEDICO_y_NO_el_id_interno_de_Api_Booking()
    {
        // El `resourceId` lo genera Api.Booking al registrar el recurso, así que NADIE río
        // arriba puede conocerlo: el CMS solo tiene el id de su propio directorio. Mandarlo
        // obligaría a que un identificador interno de una capacidad viajara hasta la UI.
        //
        // Lo destapó agendar contra los procesos vivos: la primera versión inventaba una
        // convención (resourceId = doctorId) y Api.Booking contestaba «No existe el recurso
        // doc-3». Ahora el BFF lo resuelve desde el profesional.
        var bff = Feliz();

        await Nuevo(bff).BookAsync(Pedido());

        var body = bff.Llamadas.Single(l => l.Path == "/v1/appointments").Body!;
        Assert.DoesNotContain("resourceId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"professionalId\":\"doc-3\"", body.Replace(" ", ""), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reintentar_la_MISMA_reserva_lleva_la_misma_llave()
    {
        // Es lo que impide dos citas sobre el mismo cupo tras un timeout.
        var a = Feliz();
        var b = Feliz();

        await Nuevo(a).BookAsync(Pedido());
        await Nuevo(b).BookAsync(Pedido());

        var llaveA = a.Llamadas.Single(l => l.Path == "/v1/appointments").Key;
        Assert.False(string.IsNullOrWhiteSpace(llaveA));
        Assert.Equal(llaveA, b.Llamadas.Single(l => l.Path == "/v1/appointments").Key);
    }

    [Fact]
    public async Task Otro_slot_es_otra_cita()
    {
        var a = Feliz();
        var b = Feliz();

        await Nuevo(a).BookAsync(Pedido());
        await Nuevo(b).BookAsync(new BookAppointmentRequest("pac-1", "doc-3", Slot.AddMinutes(30)));

        Assert.NotEqual(
            a.Llamadas.Single(l => l.Path == "/v1/appointments").Key,
            b.Llamadas.Single(l => l.Path == "/v1/appointments").Key);
    }

    // ── Lo que puede salir mal ──────────────────────────────────────────────

    [Fact]
    public async Task El_cupo_tomado_llega_CON_SU_MOTIVO()
    {
        // «Ese horario ya no está» lleva a elegir otro; «error» no lleva a nada.
        var bff = Feliz().Falla("POST /v1/appointments", HttpStatusCode.Conflict,
            "booking.slot_taken", "Ese horario ya fue tomado.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Nuevo(bff).BookAsync(Pedido()));

        Assert.Contains("ya fue tomado", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_401_es_defecto_de_DESPLIEGUE_y_no_un_mensaje_para_el_paciente()
    {
        var bff = Feliz().Falla("POST /v1/appointments", HttpStatusCode.Unauthorized, "unauthorized", "llave invalida");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Nuevo(bff).BookAsync(Pedido()));

        Assert.DoesNotContain("llave", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_403_SI_llega_con_su_motivo_porque_NO_es_un_fallo_de_llave()
    {
        // Lo destapó levantar los procesos: agendar contra el BFF vivo devuelve
        // 403 consent.not_granted —«no hay consentimiento para salud.agenda»— y el cliente lo
        // trataba como defecto de despliegue, tragándose el motivo.
        //
        // `SharedKeyAuth` responde 401 cuando la llave falla, NUNCA 403. Así que un 403 acá es
        // siempre un rechazo de negocio, y su motivo lleva a una acción: pedir el consentimiento.
        var bff = Feliz().Falla("POST /v1/appointments", HttpStatusCode.Forbidden,
            "consent.not_granted", "No hay consentimiento para 'salud.agenda'.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Nuevo(bff).BookAsync(Pedido()));

        Assert.Contains("consentimiento", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_timeout_NO_se_traga_la_excepcion()
    {
        // Una cita a medias es peor que ninguna. Tiene que reventar con un mensaje cierto.
        var bff = new BffFalso().Caida("POST /v1/appointments");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Nuevo(bff).BookAsync(Pedido()));

        Assert.Contains("No quedó reservada", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ante_un_timeout_se_PREGUNTA_si_la_cita_existio()
    {
        // Un timeout dice «no sé», no «no se agendó». Como la llave ES el identificador de la
        // saga, se puede consultar antes de rendirse.
        var bff = new BffFalso().Caida("POST /v1/appointments");
        var llave = HttpClinicalSchedulingService.IdempotencyKeyFor("pac-1", "doc-3", Slot);
        bff.Ok($"GET /v1/appointments/{llave}", CitaOk);

        var cita = await Nuevo(bff).BookAsync(Pedido());

        Assert.Equal("cita-1", cita.Id);
    }

    [Fact]
    public async Task Un_medico_que_no_existe_se_rechaza_SIN_tocar_el_cupo_de_nadie()
    {
        var bff = Feliz();

        await Assert.ThrowsAsync<ArgumentException>(
            () => Nuevo(bff).BookAsync(new BookAppointmentRequest("pac-1", "doc-noexiste", Slot)));

        Assert.Empty(bff.Llamadas);
    }

    [Fact]
    public async Task La_agenda_por_fecha_sale_vacia_sin_reventar()
    {
        // Decisión anotada: Bff.Salud no lista por fecha, y no se abre la flecha del CMS a
        // Api.Booking para taparlo. Vacío degrada; una agenda equivocada miente.
        var citas = await Nuevo(Feliz()).GetByDateAsync(new DateOnly(2026, 8, 10));

        Assert.Empty(citas);
    }
}
