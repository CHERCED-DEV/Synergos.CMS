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

namespace Synergos.CMS.Tests.Bff.Avisos;

/// <summary>
/// El recordatorio (NN-26 de 003, P13 = A): <c>notifications.programar</c> y <c>notifications.retirar</c> en un orquestador
/// declarado, contra <c>Api.Notifications</c> REAL. Lo que se afirma se lee en ella: la entrega espera en la cola con su hora,
/// no sale antes, y se retira; y la dirección de quien la recibe sigue donde estaba, no en el orquestador.
/// </summary>
/// <remarks>La sesión y el contacto los pone la puerta del CMS: aquí se simulan con sus cabeceras, como los manda la puerta.</remarks>
public sealed class AvisoProgramadoTests
{
    private const string Llave = "llave-de-los-avisos";
    private const string Correo = "paciente@ejemplo.co";
    private static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(15), TimeSpan.Zero);

    private static object Ventana(DateTimeOffset inicio) => new { start = inicio, end = inicio.AddMinutes(30) };

    [Fact]
    public async Task Programar_deja_el_recordatorio_en_la_cola_hasta_su_hora_y_la_direccion_no_queda_en_el_orquestador()
    {
        await using var c = await Consultorio.Arrancar();

        var (estado, saga) = await c.Agendar("muestra.recordatorio", "u-1", "c-1", new { ventana = Ventana(Inicio), antelacion = 1440 });
        var (otra, repetida) = await c.Operar("muestra.recordatorio", saga.GetProperty("id").GetString()!, "agendar", "u-1", conContacto: true);

        Assert.True(estado == HttpStatusCode.OK, saga.ToString());
        var entrega = saga.GetProperty("datos").GetProperty("entregaId").GetString()!;
        Assert.Equal(HttpStatusCode.OK, otra);
        Assert.Equal(entrega, repetida.GetProperty("datos").GetProperty("entregaId").GetString());

        var leida = await c.Entrega(entrega);
        Assert.Equal("Queued", leida.GetProperty("status").GetString());
        Assert.Equal(Inicio.AddHours(-24), leida.GetProperty("notBeforeUtc").GetDateTimeOffset());
        Assert.Equal(Correo, leida.GetProperty("address").GetString());
        Assert.Equal(0, leida.GetProperty("attempts").GetInt32());
        Assert.DoesNotContain(await c.EnLaCola(), e => e.GetProperty("id").GetString() == entrega);
        Assert.Empty(c.FicherosQueContienen(Correo));
    }

    [Fact]
    public async Task Retirar_es_un_acto_propio_que_deja_la_entrega_sin_salir_y_repetirlo_da_lo_mismo()
    {
        await using var c = await Consultorio.Arrancar();
        var (_, saga) = await c.Agendar("muestra.recordatorio", "u-1", "c-1", new { ventana = Ventana(Inicio), antelacion = 1440 });
        var entrega = saga.GetProperty("datos").GetProperty("entregaId").GetString()!;

        var (estado, retiro) = await c.Abrir("muestra.recordatorio-retiro", "u-1", "r-1", new { entrega });
        var (otra, _) = await c.Abrir("muestra.recordatorio-retiro", "u-1", "r-2", new { entrega });

        Assert.True(estado == HttpStatusCode.Created, retiro.ToString());
        Assert.Equal(HttpStatusCode.Created, otra);
        Assert.Equal("GivenUp", (await c.Entrega(entrega)).GetProperty("status").GetString());
    }

    /// <summary>Una entrega que ya salió no tiene nada que retirar: retirarla sigue, y queda como salió.</summary>
    [Fact]
    public async Task Con_la_hora_pasada_sale_en_el_acto_y_retirarla_despues_la_deja_como_salio()
    {
        await using var c = await Consultorio.Arrancar();
        var (agendo, saga) = await c.Agendar("muestra.recordatorio", "u-1", "c-1", new { ventana = Ventana(DateTimeOffset.UtcNow.AddHours(2)), antelacion = 1440 });
        Assert.True(agendo == HttpStatusCode.OK, saga.ToString());
        var entrega = saga.GetProperty("datos").GetProperty("entregaId").GetString()!;
        var salio = (await c.Entrega(entrega)).GetProperty("status").GetString();

        var (estado, retiro) = await c.Abrir("muestra.recordatorio-retiro", "u-1", "r-1", new { entrega });

        Assert.NotEqual("Queued", salio);
        Assert.True(estado == HttpStatusCode.Created, retiro.ToString());
        Assert.Equal(salio, (await c.Entrega(entrega)).GetProperty("status").GetString());
    }

    /// <summary><c>notifications.retirar</c> es también la compensación de programar: cancelar el flujo abierto lo retira.</summary>
    [Fact]
    public async Task Cancelar_el_flujo_abierto_retira_el_recordatorio_y_confirmarlo_lo_deja()
    {
        await using var c = await Consultorio.Arrancar();

        var (agendo, una) = await c.Agendar("muestra.recordatorio-en-dos", "u-1", "c-1", new { ventana = Ventana(Inicio) });
        Assert.True(agendo == HttpStatusCode.OK, una.ToString());
        var (cancelo, cancelada) = await c.Operar("muestra.recordatorio-en-dos", una.GetProperty("id").GetString()!, "cancelar", "u-1");
        var (_, otra) = await c.Agendar("muestra.recordatorio-en-dos", "u-2", "c-2", new { ventana = Ventana(Inicio) });
        var (confirmo, confirmada) = await c.Operar("muestra.recordatorio-en-dos", otra.GetProperty("id").GetString()!, "confirmar", "u-2");

        Assert.True(cancelo == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Compensated", cancelada.GetProperty("estado").GetString());
        Assert.Equal("GivenUp", (await c.Entrega(una.GetProperty("datos").GetProperty("entregaId").GetString()!)).GetProperty("status").GetString());
        Assert.True(confirmo == HttpStatusCode.OK, confirmada.ToString());
        Assert.Equal("Queued", (await c.Entrega(otra.GetProperty("datos").GetProperty("entregaId").GetString()!)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sin_contacto_no_se_programa_nada()
    {
        await using var c = await Consultorio.Arrancar();

        var (estado, cuerpo) = await c.Agendar("muestra.recordatorio", "u-1", "c-1", new { ventana = Ventana(Inicio), antelacion = 1440 }, conContacto: false);

        Assert.True(estado == HttpStatusCode.BadRequest, cuerpo.ToString());
        Assert.Equal(PasoProgramarAviso.SinContacto, cuerpo.GetProperty("code").GetString());
        Assert.Empty(await c.EntregasDe("u-1"));
    }

    // ── El orquestador de la muestra ───────────────────────────────────────

    private sealed class Consultorio : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "aviso-programado-" + Guid.NewGuid().ToString("N"));

        /// <summary>Fuera del almacén del orquestador: los correos llevan la dirección, que no tiene que estar ahí.</summary>
        private readonly string _buzon = Path.Combine(Path.GetTempPath(), "aviso-programado-buzon-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; }

        private Consultorio()
        {
            // Notifications deja cada correo como .eml en el buzón de la prueba, como en la piloto.
            Capacidades = new ArnesDeCapacidades()
                .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications", ("Notifications:Pickup:Directory", _buzon))
                .Levanta<Synergos.Api.Audit.Contracts.AuditEntryResponse>("audit");
        }

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Consultorio> Arrancar()
        {
            var c = new Consultorio();
            try
            {
                await c.PublicarPlantilla();
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "Avisos", "flujos"),
                    ["muestra:Storage:Root"] = c._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(c.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(AvisoProgramadoTests).Assembly);

                c._app = builder.Build();
                c._app.UseCorrelation();
                c._app.UseSharedKeyAuth(c._app.Configuration["Muestra:ApiKey"]);
                c._app.MapFlujosDeclarados();
                await c._app.StartAsync();
                c.Cliente = c._app.GetTestClient();
                c.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return c;
            }
            catch
            {
                await c.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Cliente?.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            foreach (var carpeta in new[] { _raiz, _buzon })
            {
                try { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true); }
                catch (IOException) { /* un temporal */ }
            }
        }

        private async Task PublicarPlantilla()
        {
            using var notifications = Capacidades.CreateClient("notifications");
            using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/templates")
            {
                Content = JsonContent.Create(new
                {
                    key = "muestra.recordatorio", channel = "Email", subject = "Tu cita de mañana",
                    body = "Hola {{nombre}}, te esperamos el {{ventana}}.",
                }),
            };
            peticion.Headers.Add("Idempotency-Key", "plantilla-recordatorio");
            using var r = await notifications.SendAsync(peticion);
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string flujo, string miembro, string llave, object cuerpo)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}") { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{miembro}");
            return Enviar(peticion);
        }

        /// <summary>Abre la cita y agenda su recordatorio: el contacto llega con la fase que avisa, como en la cita de Salud.</summary>
        public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Agendar(
            string flujo, string miembro, string llave, object cuerpo, bool conContacto = true)
        {
            var (abrio, abierta) = await Abrir(flujo, miembro, llave, cuerpo);
            Assert.True(abrio == HttpStatusCode.Created, abierta.ToString());
            return await Operar(flujo, abierta.GetProperty("id").GetString()!, "agendar", miembro, conContacto);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Operar(
            string flujo, string saga, string operacion, string miembro, bool conContacto = false)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}/{saga}/{operacion}") { Content = JsonContent.Create(new { }) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{miembro}");
            if (conContacto)
            {
                var contacto = JsonSerializer.Serialize(new { correo = Correo, nombre = "Ana", enlace = "https://sitio.ejemplo/", sitio = "Consultorio de prueba" });
                peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(contacto)));
            }
            return Enviar(peticion);
        }

        public async Task<JsonElement> Entrega(string id)
        {
            using var notifications = Capacidades.CreateClient("notifications");
            return await notifications.GetFromJsonAsync<JsonElement>($"v1/deliveries/{id}");
        }

        public async Task<IReadOnlyList<JsonElement>> EnLaCola()
        {
            using var notifications = Capacidades.CreateClient("notifications");
            var pagina = await notifications.GetFromJsonAsync<JsonElement>("v1/deliveries/queued");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        public async Task<IReadOnlyList<JsonElement>> EntregasDe(string miembro)
        {
            using var notifications = Capacidades.CreateClient("notifications");
            var pagina = await notifications.GetFromJsonAsync<JsonElement>($"v1/deliveries?toKind=muestra.paciente&toId={miembro}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        /// <summary>Los ficheros del almacén del orquestador (sagas, arriendos) que contienen <paramref name="texto"/>.</summary>
        public IReadOnlyList<string> FicherosQueContienen(string texto)
            => Directory.Exists(_raiz)
                ? Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories)
                    .Where(f => File.ReadAllText(f).Contains(texto, StringComparison.Ordinal)).ToList()
                : [];

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await Cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(texto)) return (r.StatusCode, default);
                // Un cuerpo que no es JSON (un 500 del host) vuelve como texto, para que el mensaje del fallo lo diga.
                try { return (r.StatusCode, JsonDocument.Parse(texto).RootElement.Clone()); }
                catch (JsonException) { return (r.StatusCode, JsonSerializer.SerializeToElement(texto)); }
            }
        }
    }
}
