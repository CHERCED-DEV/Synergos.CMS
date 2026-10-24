using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// El oráculo del viaje (<see cref="OraculoDelViaje"/>) por el camino DECLARADO (línea 006, T011):
/// <c>flujos/viajes.reserva.json</c> servido por el <c>Program</c> de verdad de <c>Bff.Viajes</c>, por las rutas de la
/// puerta, contra el MISMO guion que el imperativo.
/// </summary>
/// <remarks>
/// <para><b>Reservar es <c>abrir</c>, confirmar es <c>cerrar</c> y cancelar es <c>cancelar</c></b>, con quien viaja en la
/// cabecera que pone la puerta. Lo único que el guion suma para este motor es dónde pregunta por el recurso de cada
/// producto: el adaptador común de la agenda va a <c>/v1/resources/por-sujeto</c> con el mismo sujeto.</para>
///
/// <para><b>Cancelar va sin penalidad</b>: por la puerta, quien viaja sólo cancela lo que no confirmó (006-P3); cancelar
/// lo confirmado con la penalidad de quien vende es la operación posterior <c>viajes.cancelacion</c> (T027), y sus casos
/// siguen en <c>TripCompensationTests</c> hasta entonces.</para>
/// </remarks>
public sealed class TripCompensationDeclaradaTests : OraculoDelViaje, IDisposable
{
    private readonly List<ConductorDeclarado> _conductores = [];

    private protected override Contexto Nuevo(CapacidadesFalsas caps)
    {
        var c = new ConductorDeclarado(caps);
        _conductores.Add(c);
        return new Contexto(c, caps, c.Reloj);
    }

    public void Dispose()
    {
        foreach (var c in _conductores) c.Dispose();
    }

    /// <summary>
    /// El host de <c>viajes.reserva</c> entero en proceso: las capacidades del guion, el reloj del test y los barridos de
    /// fondo apagados (sus vueltas llamarían al guion y ensuciarían lo que se cuenta).
    /// </summary>
    private sealed class ConductorDeclarado : IConductorDelViaje, IDisposable
    {
        private const string Llave = "llave-del-oraculo";
        private const string Flujo = "v1/flujos/viajes.reserva";

        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "viaje-declarado-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<TripResponse> _fabrica;
        private readonly HttpClient _cliente;

        public ConductorDeclarado(CapacidadesFalsas caps)
        {
            Reloj = new RelojFalso(Ahora);
            caps.Alias("GET /v1/resources/por-sujeto", "GET /v1/resources");

            _fabrica = new WebApplicationFactory<TripResponse>().WithWebHostBuilder(b =>
            {
                b.UseSetting("viajes:Storage:Root", _raiz);
                b.UseSetting("Viajes:ApiKey", Llave);
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

        public string Soltar => "booking.liberar";
        public string Anular => "payments.anular";
        public string Devolver => "payments.devolver";
        public string NoExiste => "viajes.saga_not_found";
        public string NoConfirmable => "viajes.not_continuable";
        public string VentanaMala => "viajes.entrada_invalida";

        private ISagaStore<SagaDeFlujo> Sagas => _fabrica.Services.GetRequiredService<ISagaStore<SagaDeFlujo>>();

        public Task<Result<VistaDelViaje>> BookAsync(Ref viajero, IReadOnlyList<TripItem> items, string id, CancellationToken ct)
        {
            var peticion = DeLaPuerta(HttpMethod.Post, Flujo, viajero, new
            {
                items = items.Select(i => new
                {
                    producto = i.ProductRef,
                    etiqueta = i.ProductLabel,
                    ventana = new { start = i.Start, end = i.End },
                }),
            });
            peticion.Headers.Add("Idempotency-Key", id);
            return ViajeAsync(peticion, ct);
        }

        public Task<Result<VistaDelViaje>> ConfirmAsync(string id, CancellationToken ct)
            => ViajeAsync(DeLaPuerta(HttpMethod.Post, $"{Flujo}/{Uri.EscapeDataString(id)}/cerrar", Viajero), ct);

        public Task<Result<VistaDelViaje>> CancelAsync(string id, Money? retener, CancellationToken ct)
            => ViajeAsync(DeLaPuerta(HttpMethod.Post, $"{Flujo}/{Uri.EscapeDataString(id)}/cancelar", Viajero), ct);

        public Result<VistaDelViaje> Get(string id)
            => ViajeAsync(DeLaPuerta(HttpMethod.Get, $"{Flujo}/{Uri.EscapeDataString(id)}", Viajero), CancellationToken.None)
                .GetAwaiter().GetResult();

        public VistaDelViaje? Find(string id) => Sagas.Find(id) is { } s ? Vista(s) : null;

        private static HttpRequestMessage DeLaPuerta(HttpMethod metodo, string ruta, Ref sujeto, object? cuerpo = null)
        {
            var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"{sujeto.Kind}:{sujeto.Id}");
            return peticion;
        }

        /// <summary>El viaje que dejó la operación, leído de la saga; o el rechazo, con el código de la respuesta.</summary>
        private async Task<Result<VistaDelViaje>> ViajeAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            using (peticion)
            {
                using var r = await _cliente.SendAsync(peticion, ct);
                var texto = await r.Content.ReadAsStringAsync(ct);
                var cuerpo = string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone();
                if ((int)r.StatusCode is >= 200 and < 300) return Result.Ok(Find(cuerpo.GetProperty("id").GetString()!)!);
                return Rechazo(r.StatusCode, cuerpo);
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

        /// <summary>
        /// De la saga genérica, lo que el imperativo guarda en campos: cada apartado con la reserva que dejó su cierre, el
        /// total cotizado y el error.
        /// </summary>
        private static VistaDelViaje Vista(SagaDeFlujo s)
            => new(s,
                (s.Reservas.GetValueOrDefault("apartar") ?? []).Select(r => new ItemDeLaVista(r.HoldId, r.Cerrada, false)).ToList(),
                s.Datos.TryGetValue("total", out var t) && t.Valor is { ValueKind: JsonValueKind.Object } total
                    ? Money.Of(total.GetProperty("amount").GetDecimal(), total.GetProperty("currency").GetString()!)
                    : default,
                s.LastError);

        public void Dispose()
        {
            _cliente.Dispose();
            _fabrica.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }
}
