using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Un rechazo TRANSITORIO de un paso con llave no deshace la saga (001-P10 = A): la saga queda viva y en duda en esa fase,
/// la puerta contesta el transitorio, y repetir la fase con la misma llave la retoma con las mismas llaves de paso. Contra
/// la agenda y los pagos reales; la caída se inyecta en el cable, ANTES de llegar a la capacidad o DESPUÉS de que la
/// capacidad hizo lo suyo (la respuesta perdida de #183).
/// </summary>
public sealed class FaseEnDudaTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Viajero = "muestra.viajero:u-1";
    private static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(12).AddHours(10), TimeSpan.Zero);

    [Fact]
    public async Task Si_la_respuesta_de_autorizar_se_pierde_la_saga_queda_en_duda_y_repetir_abrir_cobra_una_vez()
    {
        await using var a = await Agencia.Arrancar();
        a.Caer("POST /v1/payments", Caida.Despues);

        var (cayo, caido) = await a.Abrir();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cayo);
        Assert.True(caido.GetProperty("transient").GetBoolean(), caido.ToString());
        var enDuda = a.Saga();
        Assert.Equal(SagaStatus.Running, enDuda.Status);
        Assert.Equal("abrir", enDuda.FaseEnDuda);
        Assert.Equal(2, enDuda.Reservas["apartar"].Count);
        Assert.All(enDuda.Compensations, c => Assert.True(c.IsPending, "Nada se deshace en duda."));
        var perdido = Assert.Single(a.CobrosQueLlegaron);

        a.Levantar();
        var (abrio, abierta) = await a.Abrir();

        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("viaje-1", abierta.GetProperty("id").GetString());
        var retomada = a.Saga();
        Assert.Null(retomada.FaseEnDuda);
        Assert.Equal(2, retomada.Reservas["apartar"].Count);
        Assert.Equal(2, retomada.Compensations.Count(c => c.Kind == "booking.liberar"));
        Assert.Equal(perdido, Agencia.Cobro(retomada));
        // Es la MISMA saga, no otra sembrada encima: el plazo del barrido sigue contando desde que se abrió.
        Assert.Equal(enDuda.StartedAtUtc, retomada.StartedAtUtc);

        var (cerro, cerrada) = await a.Cerrar();
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        var cobro = await a.Leer("payments", $"v1/payments/{perdido}");
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(300_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Si_la_respuesta_de_capturar_se_pierde_repetir_cerrar_completa_sin_devolver_ni_capturar_dos_veces()
    {
        await using var a = await Agencia.Arrancar();
        await a.Abrir();
        a.Caer("/capture", Caida.Despues);

        var (cayo, _) = await a.Cerrar();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cayo);
        var enDuda = a.Saga();
        Assert.Equal("cerrar", enDuda.FaseEnDuda);
        Assert.Equal(SagaStatus.Running, enDuda.Status);
        var cobroId = Agencia.Cobro(enDuda);
        Assert.Equal("Captured", (await a.Leer("payments", $"v1/payments/{cobroId}")).GetProperty("status").GetString());

        a.Levantar();
        var (cerro, cerrada) = await a.Cerrar();

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        var cobro = await a.Leer("payments", $"v1/payments/{cobroId}");
        Assert.Equal(0m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Null(a.Saga().FaseEnDuda);
    }

    [Fact]
    public async Task Un_transitorio_al_confirmar_no_suelta_el_item_aunque_la_politica_sea_parcial()
    {
        await using var a = await Agencia.Arrancar();
        await a.Abrir("parcial");
        a.Caer("/confirm", Caida.Antes, vez: 2);

        var (cayo, _) = await a.Cerrar();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cayo);
        Assert.DoesNotContain(a.Saga().Compensations, c => c.Kind == ParcialEnElHostGenericoTests.DevolverParte.Clase);

        a.Levantar();
        var (cerro, cerrada) = await a.Cerrar();

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("completo", cerrada.GetProperty("datos").GetProperty("vista").GetString());
        Assert.All(a.Saga().Reservas["apartar"], r => Assert.NotNull(r.Cerrada));
    }

    [Fact]
    public async Task Otra_fase_no_corre_sobre_una_saga_en_duda_y_cancelarla_la_deshace()
    {
        await using var a = await Agencia.Arrancar();
        a.Caer("POST /v1/payments", Caida.Antes);
        await a.Abrir();
        a.Levantar();

        var (cerro, cerrada) = await a.Cerrar();

        Assert.Equal(HttpStatusCode.Conflict, cerro);
        Assert.Equal(FlowRunner<SagaDeFlujo>.EnDuda, cerrada.GetProperty("code").GetString());

        var (cancelo, _) = await a.Cancelar();

        Assert.True((int)cancelo is >= 200 and < 300, $"cancelar: {(int)cancelo}");
        Assert.Equal(SagaStatus.Compensated, a.Saga().Status);
        foreach (var r in a.Saga().Reservas["apartar"])
        {
            Assert.True((await a.Leer("booking", $"v1/holds/{r.HoldId}")).GetProperty("released").GetBoolean());
        }
    }

    [Fact]
    public async Task Repetir_mientras_sigue_caida_deja_la_misma_duda_sin_apartar_otra_vez()
    {
        await using var a = await Agencia.Arrancar();
        a.Caer("POST /v1/payments", Caida.Antes);

        await a.Abrir();
        var (otra, _) = await a.Abrir();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, otra);
        Assert.Equal("abrir", a.Saga().FaseEnDuda);
        Assert.Equal(2, a.Saga().Reservas["apartar"].Count);
        Assert.Equal(2, a.Saga().Compensations.Count);
    }

    [Fact]
    public async Task Un_transitorio_de_un_paso_SIN_llave_deshace_la_saga_como_siempre()
    {
        await using var a = await Agencia.Arrancar();
        a.Caer("/v1/resources/por-sujeto", Caida.Antes, vez: 2);

        var (cayo, _) = await a.Abrir();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, cayo);
        var saga = a.Saga();
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Null(saga.FaseEnDuda);
        Assert.True((await a.Leer("booking", $"v1/holds/{Assert.Single(saga.Reservas["apartar"]).HoldId}")).GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task Un_rechazo_que_no_es_transitorio_deshace_la_saga_como_siempre()
    {
        await using var a = await Agencia.Arrancar();
        a.Rechazar("POST /v1/payments", "payments.payment_declined");

        var (rechazo, cuerpo) = await a.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, rechazo);
        Assert.False(cuerpo.GetProperty("transient").GetBoolean());
        Assert.Equal(SagaStatus.Compensated, a.Saga().Status);
        Assert.Null(a.Saga().FaseEnDuda);
    }

    // ── El orquestador de la muestra, con caídas en el cable ────────────────

    private enum Caida
    {
        /// <summary>No llega a la capacidad: 503.</summary>
        Antes,

        /// <summary>Llega, la capacidad lo hace, y la respuesta se pierde: 503.</summary>
        Despues,
    }

    private sealed class Agencia : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "fase-en-duda-" + Guid.NewGuid().ToString("N"));
        private readonly Cable _cable;
        private WebApplication? _app;

        private Agencia() => _cable = new Cable(Capacidades);

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments");

        public HttpClient Cliente { get; private set; } = null!;

        /// <summary>El id de cada cobro que llegó a Payments, aunque su respuesta se perdiera.</summary>
        public IReadOnlyList<string> CobrosQueLlegaron => _cable.Cobros;

        public void Caer(string ruta, Caida cuando, int? vez = null) => _cable.Falla = (ruta, cuando, vez, null);

        public void Rechazar(string ruta, string codigo) => _cable.Falla = (ruta, Caida.Antes, null, codigo);

        public void Levantar() => _cable.Falla = null;

        public static async Task<Agencia> Arrancar()
        {
            var a = new Agencia();
            try
            {
                await a.Aprovisionar();
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "ParcialGenerica", "flujos"),
                    ["muestra:Storage:Root"] = a._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(a._cable);
                builder.Services.AddSingleton<IExtensionDelFlujo, ParcialEnElHostGenericoTests.ExtensionDeMuestra>();
                builder.Services.AddSingleton<IDeshaceUnTipo>(sp => new ParcialEnElHostGenericoTests.DevolverParte(
                    sp.GetRequiredService<IDeshacerCobros>(), sp.GetRequiredService<TiposDeValor>()));
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(FaseEnDudaTests).Assembly,
                    new ParcialEnElHostGenericoTests.PasoVista());

                a._app = builder.Build();
                a._app.UseCorrelation();
                a._app.UseSharedKeyAuth(a._app.Configuration["Muestra:ApiKey"]);
                a._app.MapFlujosDeclarados();
                await a._app.StartAsync();
                a.Cliente = a._app.GetTestClient();
                a.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return a;
            }
            catch
            {
                await a.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Cliente?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        /// <summary>La saga del viaje, leída del motor.</summary>
        public SagaDeFlujo Saga(string id = "viaje-1") => _app!.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>().Find(id)!;

        public static string Cobro(SagaDeFlujo saga) => saga.Datos["cobroId"].Valor!.Value.GetString()!;

        /// <summary>Abre el viaje de dos cuartos por la puerta, siempre con la MISMA llave.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string politica = "todo")
        {
            var ventana = new { start = Inicio, end = Inicio.AddHours(1) };
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.viaje")
            {
                Content = JsonContent.Create(new
                {
                    items = new[] { "c-1", "c-2" }.Select(c => new { recurso = new { kind = "muestra.cuarto", id = c }, ventana }).ToArray(),
                    total = new { amount = 300_000m, currency = "COP" },
                }),
            };
            peticion.Headers.Add("Idempotency-Key", "viaje-1");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Viajero);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio,
                Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { confirmacion = politica }))));
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar()
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.viaje/viaje-1/cerrar");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Viajero);
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar()
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.viaje/viaje-1/cancelar");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Viajero);
            return Enviar(peticion);
        }

        public async Task<JsonElement> Leer(string capacidad, string ruta)
        {
            using var http = Capacidades.CreateClient(capacidad);
            using var r = await http.GetAsync(ruta);
            var texto = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"{capacidad} {ruta}: {(int)r.StatusCode} {texto}");
            return JsonDocument.Parse(texto).RootElement.Clone();
        }

        private async Task Aprovisionar()
        {
            foreach (var cuarto in new[] { "c-1", "c-2" })
            {
                using var http = Capacidades.CreateClient("booking");
                using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/resources")
                {
                    Content = JsonContent.Create(new
                    {
                        subjectKind = "muestra.cuarto", subjectId = cuarto, capacity = 1, timeZoneId = "UTC", slotMinutes = 60,
                        opening = Enum.GetNames<DayOfWeek>().Select(d => new { day = d, from = "08:00", to = "20:00" }).ToArray(),
                    }),
                };
                peticion.Headers.Add("Idempotency-Key", "cuarto-" + cuarto);
                using var r = await http.SendAsync(peticion);
                Assert.True(r.StatusCode == HttpStatusCode.Created, $"booking v1/resources: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
            }
        }

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await Cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }

    /// <summary>
    /// El cable: deja pasar, o tira la llamada que coincide —antes de llegar a la capacidad o después de que la hizo— con un
    /// 503, o la rechaza con un 409. Anota el id de cada cobro que llegó a Payments.
    /// </summary>
    private sealed class Cable(IHttpClientFactory real) : IHttpClientFactory
    {
        private int _veces;
        private (string Ruta, Caida Cuando, int? Vez, string? Codigo)? _falla;

        /// <summary>Lo que se tira, o nada; cada falla nueva cuenta sus coincidencias desde cero.</summary>
        public (string Ruta, Caida Cuando, int? Vez, string? Codigo)? Falla
        {
            get => _falla;
            set
            {
                _falla = value;
                Interlocked.Exchange(ref _veces, 0);
            }
        }

        public List<string> Cobros { get; } = [];

        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Manejador(this, interno)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Manejador(Cable cable, HttpClient interno) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                (string Ruta, Caida Cuando, int? Vez, string? Codigo)? falla = null;
                if (cable.Falla is { } f && Coincide(r, f.Ruta) && (f.Vez is null || Interlocked.Increment(ref cable._veces) == f.Vez)) falla = f;
                if (falla is { Codigo: { } codigo })
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code = codigo, detail = "inyectado en el cable" }) };
                }
                if (falla is { Cuando: Caida.Antes }) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                var respuesta = await interno.SendAsync(copia, ct);

                if (r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath == "/v1/payments" && respuesta.IsSuccessStatusCode)
                {
                    var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
                    lock (cable.Cobros) cable.Cobros.Add(JsonDocument.Parse(cuerpo).RootElement.GetProperty("id").GetString()!);
                    respuesta.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
                }

                if (falla is { Cuando: Caida.Despues })
                {
                    respuesta.Dispose();
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return respuesta;
            }

            private static bool Coincide(HttpRequestMessage r, string ruta)
            {
                var partes = ruta.Split(' ', 2);
                return partes.Length == 2
                    ? r.Method.Method == partes[0] && r.RequestUri!.AbsolutePath == partes[1]
                    : r.RequestUri!.AbsolutePath.Contains(ruta, StringComparison.Ordinal);
            }
        }
    }
}
