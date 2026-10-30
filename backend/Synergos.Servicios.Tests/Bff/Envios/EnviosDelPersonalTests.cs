using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Enviar y entregar como operaciones posteriores cortas del personal sobre lo que se despachó, y la lectura del personal
/// que dice a dónde va (núcleo, NS-1 y NS-2 de 013; 013-P2 = A). Contra la capacidad de envíos real.
/// </summary>
public sealed class EnviosDelPersonalTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Comprador = "muestra.comprador:u-1";
    private const string Personal = "muestra.personal:s-1";

    [Fact]
    public async Task El_detalle_del_personal_dice_a_donde_va_con_quien_y_en_que_estado()
    {
        await using var a = await Bodega.Arrancar();
        await a.Despachar("desp-1");

        var (estado, cuerpo) = await a.Detalle("desp-1");

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        var datos = cuerpo.GetProperty("datos");
        Assert.Equal("Calle 1 # 2-3", datos.GetProperty("destino").GetProperty("linea1").GetString());
        Assert.Equal("Bogotá", datos.GetProperty("destino").GetProperty("ciudad").GetString());
        Assert.Equal("servientrega", datos.GetProperty("transportadora").GetString());
        Assert.Equal("Ready", datos.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Array, datos.GetProperty("eventos").ValueKind);
    }

    [Fact]
    public async Task Enviar_y_entregar_mueven_el_envio_y_el_detalle_lo_cuenta_con_sus_eventos()
    {
        await using var a = await Bodega.Arrancar();
        await a.Despachar("desp-1");

        var (envio, enviada) = await a.Operar("muestra.envio", "desp-1", "env-1", nota: "sale en la ruta 3");
        var (entrega, entregada) = await a.Operar("muestra.entrega", "desp-1", "ent-1");

        Assert.True(envio == HttpStatusCode.Created, enviada.ToString());
        Assert.Equal("InTransit", enviada.GetProperty("datos").GetProperty("estado").GetString());
        Assert.True(entrega == HttpStatusCode.Created, entregada.ToString());
        Assert.Equal("Delivered", entregada.GetProperty("datos").GetProperty("estado").GetString());

        var datos = (await a.Detalle("desp-1")).Cuerpo.GetProperty("datos");
        Assert.Equal("Delivered", datos.GetProperty("estado").GetString());
        var eventos = datos.GetProperty("eventos").EnumerateArray().ToList();
        Assert.Contains(eventos, e => e.GetProperty("estado").GetString() == "InTransit" && e.GetProperty("nota").GetString() == "sale en la ruta 3");
        Assert.Contains(eventos, e => e.GetProperty("estado").GetString() == "Delivered");
    }

    [Fact]
    public async Task Repetir_enviar_con_otra_llave_no_choca_y_el_envio_sale_una_sola_vez()
    {
        await using var a = await Bodega.Arrancar();
        await a.Despachar("desp-1");

        await a.Operar("muestra.envio", "desp-1", "env-1");
        var (otra, repetida) = await a.Operar("muestra.envio", "desp-1", "env-2");

        Assert.True(otra == HttpStatusCode.Created, repetida.ToString());
        Assert.Equal("Completed", repetida.GetProperty("estado").GetString());
        Assert.Equal("InTransit", repetida.GetProperty("datos").GetProperty("estado").GetString());
        var eventos = (await a.Detalle("desp-1")).Cuerpo.GetProperty("datos").GetProperty("eventos").EnumerateArray();
        Assert.Single(eventos, e => e.GetProperty("estado").GetString() == "InTransit");
    }

    [Fact]
    public async Task Entregar_lo_que_no_salio_lo_rechaza_la_capacidad_con_su_codigo()
    {
        await using var a = await Bodega.Arrancar();
        await a.Despachar("desp-1");

        var (estado, cuerpo) = await a.Operar("muestra.entrega", "desp-1", "ent-1");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("fulfillment.transition_not_allowed", cuerpo.GetProperty("code").GetString());
        Assert.Equal("Ready", (await a.Detalle("desp-1")).Cuerpo.GetProperty("datos").GetProperty("estado").GetString());
    }

    // ── Lo que el arranque exige (NS-1: el estado; NS-2: el detalle) ───────

    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Validar no llama a nadie.");
    }

    private static readonly string Carpeta = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "Envios", "flujos");

    private static IReadOnlyList<string> Errores(string fichero, string antes, string despues)
    {
        var json = File.ReadAllText(Path.Combine(Carpeta, fichero));
        Assert.Contains(antes, json, StringComparison.Ordinal);
        var flujo = FlujoDef.Leer(json.Replace(antes, despues, StringComparison.Ordinal));
        var puertos = new PuertosHttp(new SinRed());
        var tipos = TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas(), new TipoEventosDeEnvio());
        // Como el arranque: leer la original va aparte, con el almacén de las sagas (validar no lo abre).
        var almacen = new FileSystemSagaStore<SagaDeFlujo>(Options.Create(new SagaStorageOptions { Root = Path.GetTempPath() }));
        var registro = PasosComunes.Registro(puertos, new PasoLeerOriginal(almacen, tipos));
        var binding = new BindingDeFlujo(flujo, tipos, registro, new DeshacerComun(puertos, puertos, puertos, puertos, []).Tipos);
        return FlowValidator.Validar(flujo, registro, ContratoDelFlujo.De(binding, flujo.Fases.Select(f => f.Nombre).ToList()));
    }

    private static void Dice(IReadOnlyList<string> errores, string mensaje)
        => Assert.True(errores.Any(e => e.Contains(mensaje, StringComparison.Ordinal)),
            $"Se esperaba «{mensaje}» y salió:{Environment.NewLine}{string.Join(Environment.NewLine, errores)}");

    [Fact]
    public void Las_definiciones_de_la_muestra_arrancan()
    {
        Assert.Empty(Errores("muestra.envio.json", "\"InTransit\"", "\"InTransit\""));
        Assert.Empty(Errores("muestra.entrega.json", "\"Delivered\"", "\"Delivered\""));
    }

    [Fact]
    public void Seguir_un_envio_sin_estado_o_a_uno_que_la_capacidad_no_tiene_no_arranca()
    {
        Dice(Errores("muestra.entrega.json", "\"estado\": \"Delivered\",", string.Empty),
            "el paso «entregar» es de tipo «fulfillment.seguir», que necesita «estado», y no lo declara.");
        Dice(Errores("muestra.entrega.json", "\"Delivered\"", "\"Entregado\""),
            "el paso «entregar» lleva a «Entregado», que no es un estado de «fulfillment.seguir»");
        Dice(Errores("muestra.entrega.json", "\"Delivered\"", "\"delivered\""),
            "el paso «entregar» lleva a «delivered», que no es un estado de «fulfillment.seguir»");
    }

    [Fact]
    public void Un_estado_en_un_paso_que_no_mueve_nada_no_arranca()
        => Dice(Errores("muestra.entrega.json", "\"escribe\": [\"envioId\"] }", "\"escribe\": [\"envioId\"], \"estado\": \"Ready\" }"),
            "el paso «original» declara «estado» y «flow.leer-original» no lo usa");

    [Fact]
    public void El_detalle_solo_lo_declara_un_flujo_del_personal()
        => Dice(Errores("muestra.envio.json", "\"acceso\": \"personal\",", string.Empty),
            "la lectura «detalle» la pide sólo el personal, y el flujo no es del personal");

    // ── La bodega de la muestra: el host genérico con la capacidad de envíos real ─────

    private sealed class Bodega : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "envios-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Fulfillment.Contracts.ShipmentResponse>("fulfillment");

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Bodega> Arrancar()
        {
            var b = new Bodega();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Carpeta,
                    ["muestra:Storage:Root"] = b._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(b.Capacidades);
                b._app = ArranqueDeLaMuestra.Construir(builder);
                await b._app.StartAsync();
                b.Cliente = b._app.GetTestClient();
                b.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return b;
            }
            catch
            {
                await b.DisposeAsync();
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

        /// <summary>Lo que hace la compra al cerrar: despacha el pedido a una dirección, con la transportadora del sitio.</summary>
        public async Task Despachar(string llave)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.despacho")
            {
                Content = JsonContent.Create(new { pedido = "pd-1", direccion = new { linea1 = "Calle 1 # 2-3", ciudad = "Bogotá", pais = "CO" } }),
            };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Comprador);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio,
                Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { carrier = "servientrega" }))));
            var (estado, cuerpo) = await Enviar(peticion);
            Assert.True(estado == HttpStatusCode.Created, $"No se despachó: {(int)estado} {cuerpo}");
        }

        /// <summary>Una operación posterior del personal sobre lo despachado, con su llave.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Operar(string flujo, string despacho, string llave, string? nota = null)
        {
            object cuerpo = flujo == "muestra.envio" ? new { despacho, nota } : new { despacho };
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}") { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Personal);
            return Enviar(peticion);
        }

        /// <summary>La lectura del personal: sin saga, sin llave y sin sujeto (el rol lo comprueba la puerta del CMS).</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Detalle(string despacho)
            => Enviar(new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.envio/detalle") { Content = JsonContent.Create(new { despacho }) });

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
}
