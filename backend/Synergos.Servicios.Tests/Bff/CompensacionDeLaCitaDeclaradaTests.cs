using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Salud.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El oráculo de la cita (<see cref="OraculoDeLaCita"/>) por el camino DECLARADO (línea 003, T017):
/// <c>flujos/salud.cita.json</c> servido por el <c>Program</c> de verdad de <c>Bff.Salud</c>, por las rutas de la
/// puerta, contra el MISMO guion que el imperativo.
/// </summary>
/// <remarks>
/// <para><b>Agendar es <c>abrir</c> y confirmar es <c>cerrar</c></b>, con lo que la puerta pone en cabeceras: el
/// paciente como sujeto y el servicio como negocio. Lo único que el guion suma para este motor es dónde está la
/// agenda del profesional (<c>booking.hallar</c>): el imperativo la recibe ya resuelta.</para>
///
/// <para><b>Cerrar va sin contacto</b>: el aviso al paciente no tiene a quién y sigue, así que cada
/// <c>/v1/deliveries</c> que el oráculo cuenta es el de la guardia, como en el imperativo.</para>
///
/// <para><b>El barrido y el reintento de la persona van al motor</b> del host (<c>SagaEngine&lt;SagaDeFlujo&gt;</c>),
/// como en el imperativo, donde <c>AppointmentFlow.RetryStuckAsync</c> es una línea sobre el suyo.</para>
/// </remarks>
public sealed class CompensacionDeLaCitaDeclaradaTests : OraculoDeLaCita, IDisposable
{
    private readonly List<Anfitrion> _anfitriones = [];

    private protected override Contexto Nuevo(CapacidadesFalsas caps, AlertOptions? alertas = null)
    {
        var a = new Anfitrion(caps, alertas ?? Guardia());
        _anfitriones.Add(a);
        return new Contexto(a, caps, a.Motor.Find, a.Reloj, a);
    }

    public void Dispose()
    {
        foreach (var a in _anfitriones) a.Dispose();
    }

    /// <summary>
    /// El host de <c>salud.cita</c> entero en proceso: las capacidades del guion, el reloj del test y los barridos de
    /// fondo apagados (sus vueltas llamarían al guion y ensuciarían lo que se cuenta).
    /// </summary>
    private sealed class Anfitrion : IFlujoDeLaCita, IMotorDeLaCita, IDisposable
    {
        private const string Llave = "llave-del-oraculo";
        private const string Flujo = "v1/flujos/salud.cita";

        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "cita-declarada-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<AppointmentResponse> _fabrica;
        private readonly HttpClient _cliente;

        public Anfitrion(CapacidadesFalsas caps, AlertOptions alertas)
        {
            Reloj = new RelojFalso(Ahora);
            caps.Ok("GET /v1/resources/por-sujeto", """{"id":"r1"}""");

            _fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
            {
                b.UseSetting("salud:Storage:Root", _raiz);
                b.UseSetting("Salud:ApiKey", Llave);
                b.UseSetting("salud:Alerts:ToKind", alertas.ToKind ?? string.Empty);
                b.UseSetting("salud:Alerts:ToId", alertas.ToId ?? string.Empty);
                b.UseSetting("salud:Alerts:Address", alertas.Address ?? string.Empty);
                b.UseSetting("salud:Alerts:TemplateKey", alertas.TemplateKey ?? string.Empty);
                b.ConfigureTestServices(s =>
                {
                    s.AddSingleton<IHttpClientFactory>(new FabricaFalsa(caps));
                    s.AddSingleton<TimeProvider>(Reloj);
                    foreach (var deFondo in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType is { } t
                                 && (t == typeof(DeliverySweeper) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(CompensationSweeper<>))))
                                 .ToList())
                    {
                        s.Remove(deFondo);
                    }
                });
            });
            _cliente = _fabrica.CreateClient();
            _cliente.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        }

        public RelojFalso Reloj { get; }

        public SagaEngine<SagaDeFlujo> Motor => _fabrica.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>();

        public string Devolver => TiposDeDeshacer.DevolverCobro;

        public Task<Result<CitaVista>> ScheduleAsync(
            Ref patient, Ref professional, string resourceId, Ref service, TimeWindow window, string sagaId, CancellationToken ct)
        {
            var peticion = DeLaPuerta(HttpMethod.Post, Flujo, patient, Pedido(professional, window), service);
            peticion.Headers.Add("Idempotency-Key", sagaId);
            return CitaAsync(peticion, ct);
        }

        public Task<Result<CitaVista>> ConfirmAsync(string sagaId, CancellationToken ct)
            => CitaAsync(DeLaPuerta(HttpMethod.Post, $"{Flujo}/{Uri.EscapeDataString(sagaId)}/cerrar", Paciente), ct);

        public Task<Result<CitaVista>> CancelAsync(string sagaId, CancellationToken ct)
            => CitaAsync(DeLaPuerta(HttpMethod.Post, $"{Flujo}/{Uri.EscapeDataString(sagaId)}/cancelar", Paciente), ct);

        public async Task<Result<CitaVista>> RetryStuckAsync(string sagaId, CancellationToken ct)
            => (await Motor.RetryStuckAsync(sagaId, ct)).Map(Vista);

        public async Task<Result<Money>> CopagoAsync(Ref service, CancellationToken ct)
        {
            var (estado, cuerpo) = await EnviarAsync(DeLaPuerta(HttpMethod.Post, $"{Flujo}/cotizar", Paciente, Pedido(Profesional, Cita), service), ct);
            return estado == HttpStatusCode.OK ? Dinero(cuerpo.GetProperty("datos").GetProperty("total")) : Rechazo(estado, cuerpo);
        }

        public Result<CitaVista> Get(string id) => Motor.Get(id).Map(Vista);

        public IReadOnlyList<ISaga> PendingCompensations() => Motor.PendingCompensations();

        public Task CompensateAsync(string sagaId, string reason, CancellationToken ct) => Motor.CompensateAsync(sagaId, reason, ct);

        private static object Pedido(Ref profesional, TimeWindow ventana) => new
        {
            profesional = new { kind = profesional.Kind, id = profesional.Id },
            ventana = new { start = ventana.Start, end = ventana.End },
        };

        private static HttpRequestMessage DeLaPuerta(HttpMethod metodo, string ruta, Ref sujeto, object? cuerpo = null, Ref? servicio = null)
        {
            var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"{sujeto.Kind}:{sujeto.Id}");
            if (servicio is not null)
            {
                peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { servicio = new { kind = servicio.Kind, id = servicio.Id } }))));
            }
            return peticion;
        }

        /// <summary>La cita que dejó la operación, leída de la saga; o el rechazo, con el código de la respuesta.</summary>
        private async Task<Result<CitaVista>> CitaAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            var (estado, cuerpo) = await EnviarAsync(peticion, ct);
            return (int)estado is >= 200 and < 300 ? Get(cuerpo.GetProperty("id").GetString()!) : Rechazo(estado, cuerpo);
        }

        private async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> EnviarAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            using (peticion)
            {
                using var r = await _cliente.SendAsync(peticion, ct);
                var texto = await r.Content.ReadAsStringAsync(ct);
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }

        private static Rejection Rechazo(HttpStatusCode estado, JsonElement problema)
        {
            var codigo = problema.ValueKind == JsonValueKind.Object && problema.TryGetProperty("code", out var c) ? c.GetString() : null;
            codigo ??= $"http.{(int)estado}";
            return estado switch
            {
                HttpStatusCode.ServiceUnavailable => Rejection.Unavailable(codigo, "por la puerta"),
                HttpStatusCode.NotFound => Rejection.NotFound(codigo, "por la puerta"),
                HttpStatusCode.Forbidden => Rejection.Forbidden(codigo, "por la puerta"),
                HttpStatusCode.BadRequest => Rejection.Invalid(codigo, "por la puerta"),
                _ => Rejection.Conflict(codigo, "por la puerta"),
            };
        }

        /// <summary>De la saga, la reserva y el copago: lo que el imperativo guarda en campos, éste en sus datos.</summary>
        private static CitaVista Vista(SagaDeFlujo s)
            => new(s,
                s.Datos.TryGetValue("reservaId", out var r) && r.Valor is { ValueKind: JsonValueKind.String } reserva ? reserva.GetString() : null,
                s.Datos.TryGetValue("total", out var t) && t.Valor is { } total ? Dinero(total) : default);

        private static Money Dinero(JsonElement valor)
            => Money.Of(valor.GetProperty("amount").GetDecimal(), valor.GetProperty("currency").GetString()!);

        public void Dispose()
        {
            _cliente.Dispose();
            _fabrica.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }
}
