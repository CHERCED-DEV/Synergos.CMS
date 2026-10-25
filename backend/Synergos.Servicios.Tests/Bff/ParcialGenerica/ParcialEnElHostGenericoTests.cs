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
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La confirmación parcial en el host genérico (núcleo, NV-13 de Viajes): un flujo GENÉRICO declara <c>al_fallar: soltar</c>
/// en su cierre por ítem y la extensión del vertical contesta, desde la política que la saga guardó al abrir, si suelta el
/// ítem y qué hace con él. Contra la agenda y los pagos reales; el cierre que falla se inyecta en el cable.
/// </summary>
public sealed class ParcialEnElHostGenericoTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Viajero = "muestra.viajero:u-1";
    private static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(10).AddHours(10), TimeSpan.Zero);

    [Fact]
    public async Task Con_la_politica_parcial_el_item_que_no_confirma_se_suelta_se_devuelve_su_parte_y_el_viaje_se_completa()
    {
        await using var a = await Agencia.Arrancar(fallaLaConfirmacion: 2);

        var (abrio, abierta) = await a.Abrir("parcial");
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await a.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("parcial", cerrada.GetProperty("datos").GetProperty("vista").GetString());
        Assert.Equal(0, cerrada.GetProperty("compensacionesPendientes").GetInt32());

        var saga = a.Saga(id);
        var (primero, segundo) = (saga.Reservas["apartar"][0], saga.Reservas["apartar"][1]);
        var uno = await a.Leer("booking", $"v1/holds/{primero.HoldId}");
        var dos = await a.Leer("booking", $"v1/holds/{segundo.HoldId}");
        Assert.NotEqual(JsonValueKind.Null, uno.GetProperty("reservationId").ValueKind);
        Assert.True(dos.GetProperty("released").GetBoolean(), "El cuarto que no confirmó tiene que quedar suelto.");
        Assert.Null(segundo.Cerrada);

        var cobro = await a.Leer("payments", $"v1/payments/{Agencia.Cobro(saga)}");
        Assert.Equal(300_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal(150_000m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Contains(saga.Compensations, c => c.Kind == DevolverParte.Clase && c.TargetId == segundo.HoldId && !c.IsPending);
    }

    [Fact]
    public async Task Con_la_politica_todo_o_nada_el_mismo_fallo_deshace_el_viaje_entero_como_siempre()
    {
        await using var a = await Agencia.Arrancar(fallaLaConfirmacion: 2);
        var (abrio, abierta) = await a.Abrir("todo");
        Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await a.Cerrar(id);

        Assert.NotEqual(HttpStatusCode.OK, cerro);
        var saga = a.Saga(id);
        Assert.NotEqual(SagaStatus.Completed, saga.Status);
        Assert.DoesNotContain(saga.Compensations, c => c.Kind == DevolverParte.Clase);
        var cobro = await a.Leer("payments", $"v1/payments/{Agencia.Cobro(saga)}");
        Assert.Equal(300_000m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.True(cerrada.ValueKind == JsonValueKind.Object, cerrada.ToString());
    }

    [Fact]
    public async Task Con_la_politica_parcial_y_todo_confirmado_no_se_suelta_nada()
    {
        await using var a = await Agencia.Arrancar(fallaLaConfirmacion: null);
        var (_, abierta) = await a.Abrir("parcial");
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await a.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("completo", cerrada.GetProperty("datos").GetProperty("vista").GetString());
        var cobro = await a.Leer("payments", $"v1/payments/{Agencia.Cobro(a.Saga(id))}");
        Assert.Equal(0m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Dos_extensiones_para_el_mismo_flujo_no_arrancan()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Agencia.Arrancar(fallaLaConfirmacion: null, extensiones: 2));

        Assert.Contains("«muestra.viaje» tiene 2 extensiones", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_extension_un_flujo_generico_que_declara_soltar_no_arranca()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Agencia.Arrancar(fallaLaConfirmacion: null, extensiones: 0));

        Assert.Contains("«confirmar» declara «al_fallar: soltar» y ningún vertical extiende el flujo", ex.ToString(), StringComparison.Ordinal);
    }

    // ── Lo que pone el vertical: la extensión, lo que deshace y un paso de vista ─────

    /// <summary>La extensión de la muestra: suelta si la política del sitio dice «parcial» y devuelve la parte del ítem.</summary>
    internal sealed class ExtensionDeMuestra : IExtensionDelFlujo
    {
        public string Flujo => "muestra.viaje";
        public IReadOnlyCollection<string> Lee => ["politica"];
        public IReadOnlyCollection<string> Escribe => ["vista"];
        public IReadOnlyCollection<string> Arma => [DevolverParte.Clase];

        public bool SueltaSiFalla(SagaDeFlujo saga, FlowContext ctx) => ctx.Get<string?>("politica") == "parcial";

        public SagaDeFlujo ConItemSoltado(SagaDeFlujo saga, FlowContext ctx, string reservante, HoldLeg soltada)
        {
            ctx.Set("vista", "parcial");
            return saga.WithCompensations([.. saga.Compensations, Compensation.For(DevolverParte.Clase, soltada.HoldId, "un cuarto no se confirmó")]);
        }
    }

    /// <summary>
    /// Devuelve la parte de un ítem soltado: el total entre los ítems, redondeado hacia abajo, leído de la saga. Sólo si la
    /// vista que reescribió la extensión ya está en la saga: lo que arma una extensión se ejecuta en el acto y se apoya en lo
    /// que ella escribió (en Viajes, el ítem no cumplido de su itinerario).
    /// </summary>
    internal sealed class DevolverParte(IDeshacerCobros cobros, TiposDeValor tipos) : IDeshaceUnTipo
    {
        public const string Clase = "muestra.devolver-parte";

        public string Tipo => Clase;

        public async Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
        {
            if ((string)Leer(saga, "vista") != "parcial")
            {
                return Rejection.Conflict("muestra.vista_sin_parcial", "La saga no dice todavía qué ítem se soltó.");
            }

            var total = (Money)Leer(saga, "total");
            var parte = Money.Of(Math.Floor(total.Amount / saga.Reservas["apartar"].Count), total.Currency);
            var hecho = await cobros.DevolverAsync((string)Leer(saga, "cobroId"), parte, pendiente.Reason, saga.KeyFor($"parte:{pendiente.Id}"), ct);
            return hecho.IsOk ? null : hecho.Rejection;
        }

        private object Leer(SagaDeFlujo saga, string nombre)
        {
            var guardado = saga.Datos[nombre];
            return tipos.Resolver(guardado.Tipo)!.Leer(guardado.Valor, nombre)!;
        }
    }

    /// <summary><c>muestra.vista</c>: la vista del viaje al abrir, «completo»; la extensión la reescribe si suelta un ítem.</summary>
    internal sealed class PasoVista : IPaso
    {
        public string Tipo => "muestra.vista";
        public int Lecturas => 1;
        public int Escrituras => 1;
        public LlaveRequerida Llave => LlaveRequerida.Ninguna;
        public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

        public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct) => Task.FromResult(SalidaDePaso.Sigue("completo"));
    }

    // ── El orquestador de la muestra con la extensión ───────────────────────

    private sealed class Agencia : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "parcial-generica-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments");

        public HttpClient Cliente { get; private set; } = null!;

        /// <param name="fallaLaConfirmacion">Qué confirmación de la agenda se rechaza en el cable (1, 2…), o ninguna.</param>
        /// <param name="extensiones">Cuántas extensiones del flujo registra el vertical.</param>
        public static async Task<Agencia> Arrancar(int? fallaLaConfirmacion, int extensiones = 1)
        {
            var a = new Agencia();
            try
            {
                await a.Aprovisionar();
                var confirmaciones = 0;
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "ParcialGenerica", "flujos"),
                    ["muestra:Storage:Root"] = a._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(new ConFallo(a.Capacidades, r =>
                    r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/confirm", StringComparison.Ordinal)
                    && ++confirmaciones == fallaLaConfirmacion ? "booking.hold_expired" : null));
                for (var i = 0; i < extensiones; i++) builder.Services.AddSingleton<IExtensionDelFlujo, ExtensionDeMuestra>();
                builder.Services.AddSingleton<IDeshaceUnTipo>(sp => new DevolverParte(sp.GetRequiredService<IDeshacerCobros>(), sp.GetRequiredService<TiposDeValor>()));
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(ParcialEnElHostGenericoTests).Assembly, new PasoVista());

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

        public SagaDeFlujo Saga(string id) => _app!.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>().Find(id)!;

        public static string Cobro(SagaDeFlujo saga) => saga.Datos["cobroId"].Valor!.Value.GetString()!;

        /// <summary>Abre el viaje de dos cuartos por la puerta, con la política del sitio en la cabecera de negocio.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string politica)
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

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/muestra.viaje/{id}/cerrar");
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

    private sealed class ConFallo(IHttpClientFactory real, Func<HttpRequestMessage, string?> falla) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Cable(interno, falla)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Cable(HttpClient interno, Func<HttpRequestMessage, string?> falla) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                if (falla(r) is { } codigo)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = JsonContent.Create(new { code = codigo, detail = "inyectado en el cable" }),
                    };
                }

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await interno.SendAsync(copia, ct);
            }
        }
    }
}
