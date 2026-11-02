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

namespace Synergos.CMS.Tests.Bff.Registro;

/// <summary>
/// El rastro de quién hizo qué sobre qué (NN-12 de 003): <c>audit.anotar</c> en un orquestador declarado, contra
/// <c>Api.Audit</c> REAL. Lo que se afirma se lee en el registro: el actor, la acción, el objetivo, la identidad con que se
/// afirmó y que no hay datos personales en los detalles. Y que falla cerrado: sin registro, no pasa nada.
/// </summary>
/// <remarks>La sesión la pone la puerta del CMS como sujeto: aquí se simula con esa cabecera, como la manda la puerta.</remarks>
public sealed class RegistroDeclaradoTests
{
    private const string Llave = "llave-del-registro";

    [Fact]
    public async Task Anotar_deja_quien_hizo_que_sobre_que_con_la_sesion_y_sin_datos_personales_y_repetir_no_anota_otra_vez()
    {
        await using var h = await Consultorio.Arrancar();
        var cuerpo = new { paciente = new { kind = "muestra.paciente", id = "p-7" } };

        var (estado, saga) = await h.Abrir("muestra.historia", "muestra.personal:dr-1", "h-1", cuerpo);
        var (otra, _) = await h.Abrir("muestra.historia", "muestra.personal:dr-1", "h-1", cuerpo);

        Assert.True(estado == HttpStatusCode.Created, saga.ToString());
        Assert.Equal(HttpStatusCode.Created, otra);
        var asiento = Assert.Single(await h.Asientos("muestra.paciente", "p-7"));
        Assert.Equal(saga.GetProperty("datos").GetProperty("asientoId").GetString(), asiento.GetProperty("id").GetString());
        Assert.Equal(("muestra.personal", "dr-1"), (asiento.GetProperty("actorKind").GetString(), asiento.GetProperty("actorId").GetString()));
        Assert.Equal("muestra.historia.leer", asiento.GetProperty("action").GetString());
        Assert.Equal("CmsSession", asiento.GetProperty("actedWith").GetString());
        var detalles = asiento.GetProperty("details").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(new Dictionary<string, string?> { ["saga"] = "h-1", ["flujo"] = "muestra.historia" }, detalles);
    }

    /// <summary>Falla cerrado: con el registro caído, lo que viene detrás no se hace. Con el registro arriba, el control.</summary>
    [Fact]
    public async Task Sin_registro_lo_que_viene_detras_no_se_hace()
    {
        await using (var arriba = await Consultorio.Arrancar())
        {
            var (estado, _) = await arriba.Abrir("muestra.permiso-anotado", "muestra.paciente:u-1", "p-1", new { version = "texto-v3" });
            Assert.Equal(HttpStatusCode.Created, estado);
            Assert.Single(await arriba.Permisos("u-1"));
            Assert.Equal("muestra.permiso.otorgar", Assert.Single(await arriba.Asientos("muestra.paciente", "u-1")).GetProperty("action").GetString());
        }

        await using var caido = await Consultorio.Arrancar(registroCaido: true);
        var (sinRegistro, cuerpo) = await caido.Abrir("muestra.permiso-anotado", "muestra.paciente:u-1", "p-1", new { version = "texto-v3" });

        Assert.True(sinRegistro == HttpStatusCode.ServiceUnavailable, cuerpo.ToString());
        Assert.Empty(await caido.Permisos("u-1"));
    }

    // ── El orquestador de la muestra con los flujos del registro ───────────

    private sealed class Consultorio : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "registro-declarado-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Audit.Contracts.AuditEntryResponse>("audit")
            .Levanta<Synergos.Api.Consent.Contracts.ConsentResponse>("consent");

        public HttpClient Cliente { get; private set; } = null!;

        /// <param name="registroCaido">Si está, <c>Api.Audit</c> contesta 503, como una capacidad caída.</param>
        public static async Task<Consultorio> Arrancar(bool registroCaido = false)
        {
            var c = new Consultorio();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "Registro", "flujos"),
                    ["muestra:Storage:Root"] = c._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(registroCaido ? new RegistroCaido(c.Capacidades) : c.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(RegistroDeclaradoTests).Assembly);

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
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        /// <summary>Abre el flujo por la puerta con <paramref name="sujeto"/> (<c>clase:id</c>) como quien actúa.</summary>
        public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string flujo, string sujeto, string llave, object cuerpo)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}") { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
            using var r = await Cliente.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
        }

        /// <summary>Los asientos sobre el objetivo, leídos en <c>Api.Audit</c> real.</summary>
        public async Task<IReadOnlyList<JsonElement>> Asientos(string clase, string id)
        {
            using var audit = Capacidades.CreateClient("audit");
            var pagina = await audit.GetFromJsonAsync<JsonElement>($"v1/entries?targetKind={clase}&targetId={id}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        /// <summary>Los permisos del miembro, leídos en <c>Api.Consent</c> real.</summary>
        public async Task<IReadOnlyList<JsonElement>> Permisos(string miembro)
        {
            using var consent = Capacidades.CreateClient("consent");
            var pagina = await consent.GetFromJsonAsync<JsonElement>($"v1/grants?subjectKind=muestra.paciente&subjectId={miembro}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }
    }

    /// <summary>Las capacidades reales, con <c>Api.Audit</c> caída: contesta 503 a todo.</summary>
    private sealed class RegistroCaido(IHttpClientFactory real) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => name == Synergos.Bff.Pasos.Http.PuertosHttp.Audit
                ? new HttpClient(new Caido()) { BaseAddress = new Uri("http://audit.local/") }
                : real.CreateClient(name);

        private sealed class Caido : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("""{"code":"audit.caido","detail":"guionado","transient":true}""", Encoding.UTF8, "application/problem+json"),
                });
        }
    }
}
