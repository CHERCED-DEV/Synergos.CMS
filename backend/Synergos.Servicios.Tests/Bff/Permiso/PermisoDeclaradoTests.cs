using System.Net;
using System.Net.Http.Json;
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

namespace Synergos.CMS.Tests.Bff.Permiso;

/// <summary>
/// El permiso que se da y se retira por la puerta (NN-23 de 003): <c>consent.otorgar</c> y <c>consent.revocar</c> en un
/// orquestador declarado, contra <c>Api.Consent</c> REAL. Lo que se afirma se lee en Consent: de quién es el permiso, con
/// qué versión del texto, con qué identidad y si sigue vigente.
/// </summary>
/// <remarks>
/// La sesión del miembro la pone la puerta del CMS como sujeto (<c>X-Synergos-Sujeto</c>): aquí se simula con esa cabecera,
/// como la manda la puerta. Un miembro de prueba de verdad sólo lo autoriza el arquitecto.
/// </remarks>
public sealed class PermisoDeclaradoTests
{
    private const string Llave = "llave-del-permiso";
    private const string Proposito = "muestra.datos";

    [Fact]
    public async Task Otorgar_escribe_el_permiso_del_miembro_de_la_sesion_con_la_version_del_texto_y_la_afirmacion_de_la_sesion()
    {
        await using var h = await Hospital.Arrancar();

        var (estado, saga) = await h.Abrir("muestra.permiso", "u-1", "p-1", new { version = "texto-v3" });
        var (otra, repetida) = await h.Abrir("muestra.permiso", "u-1", "p-1", new { version = "texto-v3" });

        Assert.True(estado == HttpStatusCode.Created, saga.ToString());
        Assert.Equal("Completed", saga.GetProperty("estado").GetString());
        Assert.True(otra == HttpStatusCode.Created, repetida.ToString());
        Assert.Equal(saga.GetProperty("id").GetString(), repetida.GetProperty("id").GetString());

        var permiso = Assert.Single(await h.Permisos("u-1"));
        Assert.Equal(saga.GetProperty("datos").GetProperty("permisoId").GetString(), permiso.GetProperty("id").GetString());
        Assert.Equal(Proposito, permiso.GetProperty("purpose").GetString());
        Assert.Equal("texto-v3", permiso.GetProperty("policyVersion").GetString());
        Assert.Equal("CmsSession", permiso.GetProperty("grantedWith").GetString());
        Assert.True(permiso.GetProperty("active").GetBoolean());
        Assert.Empty(await h.Permisos("u-2"));
    }

    [Fact]
    public async Task Revocar_es_un_acto_propio_que_retira_el_permiso_del_miembro_y_repetirlo_da_lo_mismo()
    {
        await using var h = await Hospital.Arrancar();
        Assert.Equal(HttpStatusCode.Created, (await h.Abrir("muestra.permiso", "u-1", "p-1", new { version = "texto-v3" })).Estado);

        var (estado, saga) = await h.Abrir("muestra.permiso-retiro", "u-1", "r-1", new { });
        var (otra, _) = await h.Abrir("muestra.permiso-retiro", "u-1", "r-2", new { });

        Assert.True(estado == HttpStatusCode.Created, saga.ToString());
        Assert.Equal("Completed", saga.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Created, otra);
        var permiso = Assert.Single(await h.Permisos("u-1"));
        Assert.False(permiso.GetProperty("active").GetBoolean());
        Assert.Equal("CmsSession", permiso.GetProperty("revokedWith").GetString());
    }

    [Fact]
    public async Task Revocar_lo_que_nunca_se_dio_es_el_no_de_la_capacidad()
    {
        await using var h = await Hospital.Arrancar();

        var (estado, cuerpo) = await h.Abrir("muestra.permiso-retiro", "u-9", "r-1", new { });

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("consent.not_granted", cuerpo.GetProperty("code").GetString());
    }

    /// <summary>
    /// <c>consent.revocar</c> es también la compensación de <c>consent.otorgar</c>: cancelar un flujo abierto retira el permiso
    /// que dio, y uno confirmado lo deja dado.
    /// </summary>
    [Fact]
    public async Task Cancelar_un_flujo_abierto_retira_el_permiso_que_dio_y_confirmarlo_lo_deja()
    {
        await using var h = await Hospital.Arrancar();

        var (_, abierta) = await h.Abrir("muestra.permiso-en-dos", "u-1", "p-1", new { version = "texto-v3" });
        Assert.True(Assert.Single(await h.Permisos("u-1")).GetProperty("active").GetBoolean());
        var (cancelo, cancelada) = await h.Operar("muestra.permiso-en-dos", abierta.GetProperty("id").GetString()!, "cancelar", "u-1");

        Assert.True(cancelo == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Compensated", cancelada.GetProperty("estado").GetString());
        var retirado = Assert.Single(await h.Permisos("u-1"));
        Assert.False(retirado.GetProperty("active").GetBoolean());
        Assert.Equal("CmsSession", retirado.GetProperty("revokedWith").GetString());

        var (_, otra) = await h.Abrir("muestra.permiso-en-dos", "u-2", "p-2", new { version = "texto-v3" });
        var (confirmo, confirmada) = await h.Operar("muestra.permiso-en-dos", otra.GetProperty("id").GetString()!, "confirmar", "u-2");
        Assert.True(confirmo == HttpStatusCode.OK, confirmada.ToString());
        Assert.Equal("Completed", confirmada.GetProperty("estado").GetString());
        Assert.True(Assert.Single(await h.Permisos("u-2")).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task En_un_flujo_del_personal_no_se_da_el_permiso_de_nadie()
    {
        await using var h = await Hospital.Arrancar();

        var (estado, cuerpo) = await h.Abrir("muestra.permiso-del-personal", "staff-1", "p-1", new { version = "texto-v3" });

        Assert.Equal(HttpStatusCode.Forbidden, estado);
        Assert.Equal("consent.titular_requerido", cuerpo.GetProperty("code").GetString());
        Assert.Empty(await h.Permisos("staff-1"));
    }

    // ── El orquestador de la muestra con los flujos del permiso ────────────

    private sealed class Hospital : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "permiso-declarado-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Consent.Contracts.ConsentResponse>("consent");

        public HttpClient Cliente { get; private set; } = null!;

        public static async Task<Hospital> Arrancar()
        {
            var h = new Hospital();
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
                builder.WebHost.UseTestServer();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "Permiso", "flujos"),
                    ["muestra:Storage:Root"] = h._raiz,
                    ["Muestra:ApiKey"] = Llave,
                });
                builder.Services.AddSingleton<IHttpClientFactory>(h.Capacidades);
                builder.AddCorrelation();
                builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(PermisoDeclaradoTests).Assembly);

                h._app = builder.Build();
                h._app.UseCorrelation();
                h._app.UseSharedKeyAuth(h._app.Configuration["Muestra:ApiKey"]);
                h._app.MapFlujosDeclarados();
                await h._app.StartAsync();
                h.Cliente = h._app.GetTestClient();
                h.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
                return h;
            }
            catch
            {
                await h.DisposeAsync();
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

        /// <summary>Abre el flujo por la puerta, con el miembro de la sesión como sujeto.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string flujo, string miembro, string llave, object cuerpo)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}") { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{miembro}");
            return Enviar(peticion);
        }

        /// <summary>Una operación sobre una saga abierta, con el mismo miembro.</summary>
        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Operar(string flujo, string saga, string operacion, string miembro)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/{flujo}/{saga}/{operacion}") { Content = JsonContent.Create(new { }) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"muestra.paciente:{miembro}");
            return Enviar(peticion);
        }

        /// <summary>Los permisos del miembro, leídos en <c>Api.Consent</c> real.</summary>
        public async Task<IReadOnlyList<JsonElement>> Permisos(string miembro)
        {
            using var consent = Capacidades.CreateClient("consent");
            var pagina = await consent.GetFromJsonAsync<JsonElement>($"v1/grants?subjectKind=muestra.paciente&subjectId={miembro}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
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
}
