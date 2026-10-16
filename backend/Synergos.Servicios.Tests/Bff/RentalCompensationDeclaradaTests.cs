using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Shared;
using static Synergos.CMS.Tests.Bff.DobleDeAlquiler;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El oráculo de la reserva (<see cref="RentalReservaBase"/>) por el camino DECLARADO (línea 005, T012):
/// <c>flujos/alquiler.renta.json</c> servido por el <c>Program</c> de verdad de <c>Bff.Alquiler</c>, por las
/// rutas de la puerta, contra el MISMO doble que el imperativo.
/// </summary>
/// <remarks>
/// <para><b>Reservar es <c>abrir</c> y <c>cerrar</c></b>, como lo hará la puerta. Lo único que el doble suma
/// para este motor es la cotización: el total del alquiler y el de la garantía los calcula el servidor con la
/// oferta publicada, y aquí los pone el guion de <c>Api.Pricing</c> con los mismos montos del imperativo.</para>
///
/// <para><b>Hereda la devolución</b> (T017: declarar y confirmar con el daño, por las operaciones posteriores al
/// cierre, <c>/v1/alquileres/{id}/…</c>). Cancelar lo confirmado llega con T020, sin <c>Skip</c>: entonces
/// hereda <see cref="RentalLiquidacionBase"/>.</para>
/// </remarks>
public sealed class RentalCompensationDeclaradaTests : RentalDevolucionBase, IDisposable
{
    private readonly List<ConductorDeclarado> _conductores = [];

    protected override ConductorDeRenta Nuevo(CapacidadesFalsas caps)
    {
        var c = new ConductorDeclarado(caps);
        _conductores.Add(c);
        return c;
    }

    public void Dispose()
    {
        foreach (var c in _conductores) c.Dispose();
    }

    /// <summary>Con dos unidades, repetir <c>abrir</c> no aparta cuatro: la llave de cada unidad es la misma.</summary>
    [Fact]
    public async Task Repetir_abrir_con_dos_unidades_no_aparta_cuatro()
    {
        var c = (ConductorDeclarado)Nuevo(FelizEstricto());

        var primero = await c.AbrirAsync("alq-1", 2);
        var segundo = await c.AbrirAsync("alq-1", 2);

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Created, segundo.Estado);
        Assert.Equal(2, c.Caps.VecesEn("POST", "/v1/holds"));
    }

    /// <summary>Cancelar un alquiler pendiente anula las dos autorizaciones y suelta las unidades.</summary>
    [Fact]
    public async Task Cancelar_un_alquiler_pendiente_anula_las_dos_autorizaciones_y_suelta_las_unidades()
    {
        var c = (ConductorDeclarado)Nuevo(FelizEstricto());
        Assert.Equal(HttpStatusCode.Created, (await c.AbrirAsync("alq-1", 2)).Estado);

        var (estado, _) = await c.EnviarAsync(HttpMethod.Post, "v1/flujos/alquiler.renta/alq-1/cancelar");

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-ren/void"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/holds/h-1/release"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/holds/h-2/release"));
        Assert.Equal(0, c.Caps.Veces("POST", "/capture"));
        Assert.Equal(SagaStatus.Compensated, c.Estado("alq-1"));
    }

    /// <summary>
    /// El conductor de <c>alquiler.renta</c>: el host entero en proceso, con las capacidades del doble, el reloj
    /// del test y los barridos de fondo apagados (sus vueltas llamarían al doble y ensuciarían lo que se cuenta).
    /// </summary>
    internal sealed class ConductorDeclarado : ConductorDeRenta, IDisposable
    {
        private const string Llave = "llave-del-oraculo";
        private const string Sujeto = "alquiler.arrendatario:u-1";

        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "renta-declarada-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<RentalResponse> _fabrica;
        private readonly HttpClient _cliente;
        private decimal _garantia = 400000m;

        public ConductorDeclarado(CapacidadesFalsas caps) : base(caps)
        {
            Reloj = new RelojFalso(Ahora);

            // La oferta del andamio, como la habría dejado POST /v1/ofertas: aquí sólo importan sus límites, porque
            // los montos los contesta el guion de Pricing.
            new JsonCollectionStore<OfertaDeAlquilerRegistrada>(Path.Combine(_raiz, "ofertas"), "equipos", o => o.Equipo)
                .Put(new OfertaDeAlquilerRegistrada("andamio-6m", "COP", 50_000m, [], 400_000m,
                    MinDias: 1, MaxDias: 30, Unidades: 8, Retirada: false, Ahora));

            caps.Cuando("POST /v1/quotes", Cotizar);

            _fabrica = new WebApplicationFactory<RentalResponse>().WithWebHostBuilder(b =>
            {
                b.UseSetting("alquiler:Storage:Root", _raiz);
                b.UseSetting("Alquiler:ApiKey", Llave);
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

        private ISagaStore<SagaDeFlujo> Sagas => _fabrica.Services.GetRequiredService<ISagaStore<SagaDeFlujo>>();

        /// <summary>El guion de Pricing: el alquiler cuesta lo del imperativo y la garantía lo que pida el caso.</summary>
        private HttpResponseMessage Cotizar(HttpRequestMessage peticion)
        {
            var cuerpo = peticion.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var total = new { amount = cuerpo.Contains(RentalSubject.KindGarantiaUnidad, StringComparison.Ordinal) ? _garantia : 350000m, currency = "COP" };
            var cero = new { amount = 0m, currency = "COP" };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { lines = Array.Empty<object>(), subtotal = total, discount = cero, tax = cero, total }),
                    Encoding.UTF8, "application/json"),
            };
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> AbrirAsync(string llave, int unidades)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/alquiler.renta")
            {
                Content = JsonContent.Create(new
                {
                    equipo = "andamio-6m",
                    unidades = Enumerable.Range(1, unidades).Select(u => new { unidad = u }),
                    ventana = new { start = Semana.Start, end = Semana.End },
                }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            return EnviarAsync(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> EnviarAsync(HttpMethod metodo, string ruta)
            => EnviarAsync(new HttpRequestMessage(metodo, ruta));

        private async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> EnviarAsync(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto);
                using var r = await _cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }

        public override async Task<VistaDeRenta> ReservarAsync(string id = "alq-1", int unidades = 1, decimal garantia = 400000m)
        {
            _garantia = garantia;

            var (estado, cuerpo) = await AbrirAsync(id, unidades);
            if (estado != HttpStatusCode.Created) return Fallo(cuerpo);

            var sagaId = cuerpo.GetProperty("id").GetString()!;
            if (cuerpo.GetProperty("estado").GetString() == nameof(SagaStatus.Running))
            {
                var cerrar = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/alquiler.renta/{Uri.EscapeDataString(sagaId)}/cerrar");
                cerrar.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(new { correo = "ana@ejemplo.co", nombre = "Ana" }))));
                (estado, cuerpo) = await EnviarAsync(cerrar);
                if (estado != HttpStatusCode.OK) return Fallo(cuerpo);
            }

            return Vista(cuerpo);
        }

        /// <summary>Confirmar la devolución: lo que hace el personal, por la operación posterior al cierre.</summary>
        public override async Task<VistaDeRenta> DevolverAsync(string id, decimal danio)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/alquileres/{Uri.EscapeDataString(id)}/confirmar-devolucion")
            {
                Content = JsonContent.Create(new { damage = new { amount = danio, currency = "COP" }, staffId = "staff-1" }),
            };
            peticion.Headers.Add("Idempotency-Key", $"confirmar-{Guid.NewGuid():N}");
            return Liquidada(await EnviarAsync(peticion));
        }

        public override Task<VistaDeRenta> CancelarAsync(string id, decimal penalidad)
            => throw new NotSupportedException("Cancelar lo confirmado por el camino declarado llega con T020.");

        /// <summary>Declarar la devolución: lo que hace el inquilino, con su sujeto de la puerta.</summary>
        public override async Task<VistaDeRenta> DeclararAsync(string id)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/alquileres/{Uri.EscapeDataString(id)}/declarar-devolucion");
            peticion.Headers.Add("Idempotency-Key", $"declarar-{Guid.NewGuid():N}");
            return Liquidada(await EnviarAsync(peticion));
        }

        /// <summary>Lo que el oráculo mira de una liquidación, leído de <c>RentalSettlementResponse</c>.</summary>
        private static VistaDeRenta Liquidada((HttpStatusCode Estado, JsonElement Cuerpo) r)
        {
            if (r.Estado != HttpStatusCode.OK) return Fallo(r.Cuerpo);
            var c = r.Cuerpo;
            return new VistaDeRenta(true, null, c.GetProperty("rentalId").GetString()!, null, null, [],
                c.GetProperty("state").GetString(), c.GetProperty("depositHeld").GetDecimal(),
                c.GetProperty("damageCharged") is { ValueKind: JsonValueKind.Number } d ? d.GetDecimal() : 0m);
        }

        public override SagaStatus? Estado(string id) => Sagas.Find(id)?.Status;

        public override int Pendientes(string id) => Sagas.Find(id)?.Pending().Count ?? 0;

        public override void Avanzar(TimeSpan cuanto) => Reloj.Avanzar(cuanto);

        public override bool HayCandidatasAlBarrido()
            => Sagas.StartedBefore(Reloj.GetUtcNow()).Count > 0 || Sagas.WithPendingCompensations().Count > 0;

        private static VistaDeRenta Fallo(JsonElement problema)
            => new(false, problema.ValueKind == JsonValueKind.Object && problema.TryGetProperty("code", out var c) ? c.GetString() : null,
                string.Empty, null, null, [], null, null, 0m);

        /// <summary>
        /// Lo que el oráculo mira, leído de la respuesta de la puerta y de la saga. La saga no guarda el recurso
        /// de cada apartado (no le hace falta para cerrar): sale de lo que se le pidió a Booking, en orden.
        /// </summary>
        private VistaDeRenta Vista(JsonElement respuesta)
        {
            var id = respuesta.GetProperty("id").GetString()!;
            var datos = respuesta.GetProperty("datos");
            var recursos = Caps.Llamadas
                .Where(l => l.Method == "POST" && l.Path.EndsWith("/v1/holds", StringComparison.Ordinal))
                .Select(l => JsonDocument.Parse(l.Body!).RootElement.GetProperty("resourceId").GetString()!)
                .ToList();
            var apartados = Sagas.Find(id)!.Reservas["apartar"].Select((r, i) => (r.HoldId, recursos[i])).ToList();

            return new VistaDeRenta(true, null, id, Texto(datos, "garantiaId"), Texto(datos, "cobroId"), apartados,
                respuesta.GetProperty("estado").GetString(), null, 0m);
        }

        private static string? Texto(JsonElement datos, string nombre)
            => datos.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        public void Dispose()
        {
            _cliente.Dispose();
            _fabrica.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }
}
