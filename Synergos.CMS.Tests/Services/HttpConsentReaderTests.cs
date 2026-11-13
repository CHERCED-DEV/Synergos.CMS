using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El lector de <c>Api.Consent</c> (003-salud-cita, I2 T054): un permiso con id es vigente, un rechazo <c>consent.*</c> no
/// lo es, y todo lo demás es «no se pudo leer», que la guardia convierte en una negativa. El sujeto va en el cuerpo.
/// </summary>
public sealed class HttpConsentReaderTests
{
    private sealed class Capacidad(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Pedido { get; private set; }
        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Pedido = req;
            Cuerpo = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return responder(req);
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("http://consent.local/") };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Task<PermisoLeido> Leer(HttpMessageHandler h) =>
        new HttpConsentReader(new Fabrica(h), NullLogger<HttpConsentReader>.Instance)
            .LeerAsync("salud.paciente", "abc123", "salud.historia", CancellationToken.None);

    [Fact]
    public async Task Un_permiso_con_id_es_vigente_y_el_sujeto_viaja_en_el_cuerpo_no_en_la_url()
    {
        var capacidad = new Capacidad(_ => Json(HttpStatusCode.OK, """{"id":"g-77","purpose":"salud.historia"}"""));

        var leido = await Leer(capacidad);

        Assert.Equal(new PermisoLeido(EstadoDelPermiso.Vigente, "g-77"), leido);
        Assert.Equal(HttpMethod.Post, capacidad.Pedido!.Method);
        Assert.Equal("/v1/grants/check", capacidad.Pedido.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("abc123", capacidad.Pedido.RequestUri.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"subjectKind\":\"salud.paciente\"", capacidad.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("\"subjectId\":\"abc123\"", capacidad.Cuerpo, StringComparison.Ordinal);
        Assert.Contains("\"purpose\":\"salud.historia\"", capacidad.Cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("consent.not_granted", HttpStatusCode.NotFound)]
    [InlineData("consent.revoked", HttpStatusCode.Forbidden)]
    [InlineData("consent.expired", HttpStatusCode.Forbidden)]
    public async Task Un_rechazo_de_la_capacidad_es_que_no_hay_permiso(string codigo, HttpStatusCode status)
    {
        var leido = await Leer(new Capacidad(_ => Json(status, $$"""{"title":"x","status":{{(int)status}},"code":"{{codigo}}"}""")));

        Assert.Equal(EstadoDelPermiso.NoVigente, leido.Estado);
    }

    public static TheoryData<Func<HttpRequestMessage, HttpResponseMessage>> LoQueNoSePuedeLeer() => new()
    {
        _ => Json(HttpStatusCode.ServiceUnavailable, """{"code":"consent.unavailable"}"""),
        _ => Json(HttpStatusCode.Unauthorized, """{"code":"auth.bad_key"}"""),
        _ => Json(HttpStatusCode.OK, """{"purpose":"salud.historia"}"""),
        _ => Json(HttpStatusCode.OK, "no es json"),
        _ => throw new HttpRequestException("caída"),
        _ => throw new TaskCanceledException("plazo"),
    };

    [Theory, MemberData(nameof(LoQueNoSePuedeLeer))]
    public async Task Lo_demas_no_se_pudo_leer_y_nunca_lanza(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Assert.Equal(EstadoDelPermiso.NoSePudoLeer, (await Leer(new Capacidad(responder))).Estado);
    }
}
