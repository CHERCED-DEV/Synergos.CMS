using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Salud.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El permiso de datos de salud por la puerta (003-salud-cita, T048, sobre NN-23/T099 del núcleo): <c>salud.consentimiento</c>
/// lo da el miembro de la sesión con la versión del texto que manda el CMS como negocio, y
/// <c>salud.consentimiento-retiro</c> lo retira cuando quiera. Contra el <c>Bff.Salud</c> real y <c>Api.Consent</c> real.
/// </summary>
public sealed class PermisoDeSaludTests
{
    private const string Version = "2026-11-borrador";

    [Fact]
    public async Task Otorgar_da_el_permiso_de_agenda_del_miembro_con_la_version_que_manda_el_CMS_y_la_afirmacion_de_la_sesion()
    {
        using var s = new PermisoDeSaludReal();

        var (estado, saga) = await s.Otorgar("u-1", "p-1", Version);
        var (otra, repetida) = await s.Otorgar("u-1", "p-1", Version);

        Assert.True(estado == HttpStatusCode.Created, saga.ToString());
        Assert.Equal("Completed", saga.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Created, otra);
        Assert.Equal(saga.GetProperty("id").GetString(), repetida.GetProperty("id").GetString());
        var permiso = Assert.Single(await s.Permisos("u-1"));
        Assert.Equal("salud.agenda", permiso.GetProperty("purpose").GetString());
        Assert.Equal(Version, permiso.GetProperty("policyVersion").GetString());
        Assert.Equal("CmsSession", permiso.GetProperty("grantedWith").GetString());
        Assert.True(permiso.GetProperty("active").GetBoolean());
        Assert.Empty(await s.Permisos("u-2"));
    }

    /// <summary>La versión la declara el servidor: la que mande el cuerpo no cuenta, y sin la del negocio no se da nada.</summary>
    [Fact]
    public async Task La_version_es_la_del_negocio_y_no_la_del_cuerpo_y_sin_ella_no_se_da_el_permiso()
    {
        using var s = new PermisoDeSaludReal();

        var (estado, _) = await s.Otorgar("u-1", "p-1", Version, cuerpo: new { version = "la-que-quiera" });
        var (sinVersion, rechazo) = await s.Otorgar("u-2", "p-2", version: null);

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.Equal(Version, Assert.Single(await s.Permisos("u-1")).GetProperty("policyVersion").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, sinVersion);
        Assert.StartsWith("salud.", rechazo.GetProperty("code").GetString());
        Assert.Empty(await s.Permisos("u-2"));
    }

    [Fact]
    public async Task Retirarlo_es_un_acto_propio_del_miembro_y_repetirlo_da_lo_mismo()
    {
        using var s = new PermisoDeSaludReal();
        Assert.Equal(HttpStatusCode.Created, (await s.Otorgar("u-1", "p-1", Version)).Estado);

        var (estado, saga) = await s.Retirar("u-1", "r-1");
        var (otra, _) = await s.Retirar("u-1", "r-2");

        Assert.True(estado == HttpStatusCode.Created, saga.ToString());
        Assert.Equal("Completed", saga.GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.Created, otra);
        var permiso = Assert.Single(await s.Permisos("u-1"));
        Assert.False(permiso.GetProperty("active").GetBoolean());
        Assert.Equal("CmsSession", permiso.GetProperty("revokedWith").GetString());
    }

    [Fact]
    public async Task Retirar_un_permiso_que_nunca_se_dio_es_el_no_de_la_capacidad()
    {
        using var s = new PermisoDeSaludReal();

        var (estado, cuerpo) = await s.Retirar("u-9", "r-1");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("consent.not_granted", cuerpo.GetProperty("code").GetString());
    }

    private sealed class PermisoDeSaludReal : IDisposable
    {
        private const string Llave = "llave-del-permiso-de-salud";
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "permiso-salud-" + Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<AppointmentResponse> _fabrica;
        private readonly HttpClient _orquestador;

        public PermisoDeSaludReal()
        {
            Capacidades = new ArnesDeCapacidades()
                .Levanta<Synergos.Api.Consent.Contracts.ConsentResponse>("consent")
                .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
                .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
                .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
                .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");
            _fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
            {
                b.UseSetting("salud:Storage:Root", _raiz);
                b.UseSetting("Salud:ApiKey", Llave);
                b.ConfigureTestServices(s => s.AddSingleton<IHttpClientFactory>(Capacidades));
            });
            _orquestador = _fabrica.CreateClient();
            _orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        }

        private ArnesDeCapacidades Capacidades { get; }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Otorgar(string miembro, string llave, string? version, object? cuerpo = null)
        {
            var peticion = DeLaPuerta("v1/flujos/salud.consentimiento", miembro, cuerpo ?? new { });
            peticion.Headers.Add("Idempotency-Key", llave);
            if (version is not null)
            {
                peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio, Base64(new { versionDelPermiso = version }));
            }
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string miembro, string llave)
        {
            var peticion = DeLaPuerta("v1/flujos/salud.consentimiento-retiro", miembro, new { });
            peticion.Headers.Add("Idempotency-Key", llave);
            return Enviar(peticion);
        }

        public async Task<IReadOnlyList<JsonElement>> Permisos(string miembro)
        {
            using var consent = Capacidades.CreateClient("consent");
            var pagina = await consent.GetFromJsonAsync<JsonElement>($"v1/grants?subjectKind=salud.paciente&subjectId={miembro}");
            return pagina.GetProperty("items").EnumerateArray().Select(e => e.Clone()).ToList();
        }

        public void Dispose()
        {
            _orquestador.Dispose();
            _fabrica.Dispose();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        private static HttpRequestMessage DeLaPuerta(string ruta, string miembro, object cuerpo)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"salud.paciente:{miembro}");
            return peticion;
        }

        private static string Base64(object valor) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(valor)));

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await _orquestador.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }
}
