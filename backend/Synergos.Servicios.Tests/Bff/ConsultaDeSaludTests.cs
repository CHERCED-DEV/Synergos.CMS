using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Salud.Contracts;
using Synergos.CMS.Tests.Architecture;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La consulta en el orquestador de Salud (003-salud-cita, I3): la agenda del día de un profesional desde Booking (P9), la
/// llegada de cada cita como instancia del proceso que aprovisiona el repo (P10) y el sello de la nota (P6). Contra el
/// <c>Bff.Salud</c> real y Booking, Workflow y Signing reales.
/// </summary>
public sealed class ConsultaDeSaludTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTimeOffset Lunes = new(2027, 3, 15, 0, 0, 0, TimeSpan.Zero);
    private readonly ConsultaReal _s = new();

    public async Task InitializeAsync()
    {
        await _s.PublicarLaLlegada();
        await _s.Capacidad("booking", "v1/resources", new { subjectKind = "salud.profesional", subjectId = "dra-1", capacity = 1, timeZoneId = "UTC" }, "agenda");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task La_agenda_del_dia_trae_las_citas_del_profesional_en_esa_ventana_en_orden_y_sin_marcar()
    {
        var tarde = await _s.Reservar("dra-1", Lunes.AddHours(16), "u-2");
        var manana = await _s.Reservar("dra-1", Lunes.AddHours(14), "u-1");
        await _s.Reservar("dra-1", Lunes.AddDays(1).AddHours(14), "u-3");

        var agenda = await _s.Agenda("dra-1", Lunes, Lunes.AddDays(1));

        Assert.Equal(new[] { manana, tarde }, agenda.Citas.Select(c => c.ReservaId));
        Assert.Equal(new[] { "u-1", "u-2" }, agenda.Citas.Select(c => c.PacienteId));
        Assert.All(agenda.Citas, c => Assert.Equal(("salud.paciente", "agendada"), (c.PacienteKind, c.Llegada)));
    }

    [Fact]
    public async Task La_llegada_avanza_paso_a_paso_repetir_un_paso_da_lo_mismo_y_saltarse_uno_es_el_no_del_proceso()
    {
        var cita = await _s.Reservar("dra-1", Lunes.AddHours(14), "u-1");

        var saltado = await _s.Marcar(cita, "atender");
        var llego = await _s.Marcar(cita, "llegar");
        var otraVez = await _s.Marcar(cita, "llegar");
        var enSala = await _s.Marcar(cita, "pasar");
        var atendido = await _s.Marcar(cita, "atender");

        Assert.NotEqual(HttpStatusCode.OK, saltado.Estado);
        Assert.Equal(new[] { "llego", "llego", "en-sala", "atendido" },
            new[] { llego, otraVez, enSala, atendido }.Select(r => r.Cuerpo.GetProperty("llegada").GetString()));
        Assert.Equal("atendido", Assert.Single((await _s.Agenda("dra-1", Lunes, Lunes.AddDays(1))).Citas).Llegada);
    }

    [Fact]
    public async Task El_sello_de_una_nota_se_comprueba_con_la_misma_huella_y_no_con_otra()
    {
        await _s.CrearLlave("salud.consultorio");

        var sello = await _s.Orquestador.PostAsJsonAsync("v1/sellos", new { llave = "salud.consultorio", contenido = "huella-1" });
        var dado = (await sello.Content.ReadFromJsonAsync<SelloResponse>())!;
        var bien = await _s.Orquestador.PostAsJsonAsync("v1/sellos/comprobar", new { llave = "salud.consultorio", contenido = "huella-1", sello = dado.Sello });
        var mal = await _s.Orquestador.PostAsJsonAsync("v1/sellos/comprobar", new { llave = "salud.consultorio", contenido = "huella-2", sello = dado.Sello });

        Assert.Equal(HttpStatusCode.OK, sello.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, mal.StatusCode);
    }

    private sealed class ConsultaReal : IDisposable
    {
        private const string Llave = "llave-de-la-consulta";
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "consulta-salud-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<AppointmentResponse> _fabrica;
        private int _n;

        public ConsultaReal()
        {
            Capacidades = new ArnesDeCapacidades()
                .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
                .Levanta<Synergos.Api.Workflow.Contracts.InstanceResponse>("workflow")
                .Levanta<Synergos.Api.Signing.Contracts.SealResponse>("signing");
            _fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
            {
                b.UseSetting("salud:Storage:Root", _raiz);
                b.UseSetting("Salud:ApiKey", Llave);
                b.ConfigureTestServices(s => s.AddSingleton<IHttpClientFactory>(Capacidades));
            });
            Orquestador = _fabrica.CreateClient();
            Orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        }

        public ArnesDeCapacidades Capacidades { get; }

        public HttpClient Orquestador { get; }

        /// <summary>El proceso de la llegada, el que aprovisiona el repo.</summary>
        public Task PublicarLaLlegada()
        {
            var procesos = JsonNode.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.procesos.json")))!.AsArray();
            var llegada = procesos.Single(p => (string?)p?["key"] == "salud.llegada.v1")!;
            return Capacidad("workflow", "v1/definitions", llegada, "def-llegada");
        }

        public Task CrearLlave(string proposito) => Capacidad("signing", "v1/keys", new { purpose = proposito }, "llave-" + proposito);

        public async Task<string> Reservar(string profesional, DateTimeOffset inicio, string paciente)
        {
            using var booking = Capacidades.CreateClient("booking");
            var recurso = await booking.GetFromJsonAsync<JsonElement>($"v1/resources?subjectKind=salud.profesional&subjectId={profesional}");
            var n = Interlocked.Increment(ref _n);
            var apartado = await Enviar(booking, "v1/holds", new
            {
                resourceId = recurso.GetProperty("id").GetString(),
                start = inicio,
                end = inicio.AddMinutes(30),
                heldForKind = "salud.paciente",
                heldForId = paciente,
            }, $"hold-{n}");
            var reserva = await Enviar(booking, $"v1/holds/{apartado.GetProperty("id").GetString()}/confirm", null, $"confirm-{n}");
            return reserva.GetProperty("id").GetString()!;
        }

        public async Task<AgendaDelDiaResponse> Agenda(string profesional, DateTimeOffset desde, DateTimeOffset hasta)
        {
            var url = $"v1/agenda?profesional={profesional}&desde={Uri.EscapeDataString(desde.ToString("O"))}&hasta={Uri.EscapeDataString(hasta.ToString("O"))}";
            using var r = await Orquestador.GetAsync(url);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            return (await r.Content.ReadFromJsonAsync<AgendaDelDiaResponse>())!;
        }

        public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Marcar(string reserva, string paso)
        {
            using var r = await Orquestador.PostAsJsonAsync($"v1/agenda/{reserva}/llegada",
                new { paso, actorKind = "salud.personal", actorId = "recepcion-1" });
            return (r.StatusCode, JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone());
        }

        public async Task Capacidad(string capacidad, string ruta, object cuerpo, string llave)
        {
            using var cliente = Capacidades.CreateClient(capacidad);
            await Enviar(cliente, ruta, cuerpo, llave);
        }

        private static async Task<JsonElement> Enviar(HttpClient cliente, string ruta, object? cuerpo, string llave)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            using var r = await cliente.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, texto);
            return JsonDocument.Parse(texto).RootElement.Clone();
        }

        public void Dispose()
        {
            Orquestador.Dispose();
            _fabrica.Dispose();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }
}
