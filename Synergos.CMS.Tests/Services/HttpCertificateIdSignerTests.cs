using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El id del diploma lo sella <c>Api.Signing</c> (hallazgo #45, rebanada 2).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba es lo que este camino tiene de distinto: que el contenido sellado
/// <b>no viaja por la URL</b>, que los ids <b>anteriores al cableado siguen verificando</b>, y que
/// una capacidad que no contesta <b>no da por buena</b> una credencial.</para>
/// </remarks>
public sealed class HttpCertificateIdSignerTests
{
    private const string Sello = "AbCdEf0123456789AbCdEf0123456789AbCdEf01234";

    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        public List<(string Path, string? Query, string? Body)> Llamadas { get; } = new();

        /// <summary>Qué contesta <c>/v1/seals/verify</c>. Por defecto, que cuadra.</summary>
        public HttpStatusCode Verificacion { get; set; } = HttpStatusCode.OK;

        public HttpStatusCode Sellado { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            Llamadas.Add((path, req.RequestUri.Query, req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            var (codigo, cuerpo) = path.EndsWith("/verify", StringComparison.Ordinal)
                ? (Verificacion, $$"""{"keyId":"k1"}""")
                : (Sellado, $$"""{"seal":"{{Sello}}","keyId":"k1"}""");

            return Task.FromResult(new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    codigo == HttpStatusCode.OK
                        ? cuerpo
                        : $$"""{"title":"x","status":{{(int)codigo}},"detail":"no cuadra","code":"signing.seal_mismatch"}""",
                    Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://signing.local/") };
    }

    private static readonly CertificateSubject Sujeto = new("curso-1", "Juan@Ejemplo.CO");

    private static ICertificateIdSigner Local()
        => new HmacCertificateIdSigner(Encoding.UTF8.GetBytes("llave-local-vieja"));

    private static HttpCertificateIdSigner Armar(CapacidadFalsa capacidad, ICertificateIdSigner? heredado = null)
        => new(new FabricaFalsa(capacidad), Options.Create(new AcademySettings { Mode = "Api" }), heredado);

    // ── Sellar ──────────────────────────────────────────────────────────────

    [Fact] // happy: el id sale del sello y lleva el prefijo reconocible.
    public void El_id_sale_del_sello_de_la_capacidad()
    {
        var capacidad = new CapacidadFalsa();

        var id = Armar(capacidad).Sign(Sujeto);

        Assert.Equal(HttpCertificateIdSigner.Prefix + Sello, id);
        Assert.Single(capacidad.Llamadas);
        Assert.Equal("/v1/seals", capacidad.Llamadas[0].Path);
    }

    /// <summary>
    /// El contenido sellado NUNCA viaja por la URL.
    /// </summary>
    /// <remarks>
    /// Lo que se sella <b>es el sujeto</b>: curso y alumno. En la URL quedaría escrito en el log
    /// de cada proxy que lo vea, que es exactamente lo que el sello existe para no publicar.
    /// </remarks>
    [Fact]
    public void El_contenido_sellado_no_viaja_en_la_URL()
    {
        var capacidad = new CapacidadFalsa();
        var firmante = Armar(capacidad);

        firmante.Sign(Sujeto);
        firmante.Matches(HttpCertificateIdSigner.Prefix + Sello, Sujeto);

        foreach (var llamada in capacidad.Llamadas)
        {
            Assert.DoesNotContain("curso-1", llamada.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ejemplo", llamada.Query ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            // Y sí va en el cuerpo, normalizado.
            Assert.Contains("curso-1|juan@ejemplo.co", llamada.Body!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// El mismo alumno da el mismo id aunque escriba su correo distinto.
    /// </summary>
    /// <remarks>
    /// El motor de matrícula compara sin distinguir mayúsculas, así que sin normalizar el mismo
    /// alumno tendría DOS credenciales del mismo curso según cómo viniera escrito el correo — y
    /// la idempotencia que el contrato promete dependería de eso.
    /// </remarks>
    [Fact]
    public void El_contenido_se_normaliza_igual_que_la_llave_local()
    {
        var capacidad = new CapacidadFalsa();
        var firmante = Armar(capacidad);

        firmante.Sign(new CertificateSubject(" CURSO-1 ", "JUAN@EJEMPLO.CO"));
        firmante.Sign(new CertificateSubject("curso-1", "juan@ejemplo.co"));

        Assert.Equal(capacidad.Llamadas[0].Body, capacidad.Llamadas[1].Body);
    }

    // ── Verificar ───────────────────────────────────────────────────────────

    [Fact] // happy: la capacidad dice que cuadra.
    public void Un_sello_que_cuadra_verifica()
    {
        Assert.True(Armar(new CapacidadFalsa()).Matches(HttpCertificateIdSigner.Prefix + Sello, Sujeto));
    }

    [Fact] // que no cuadre es una respuesta, no un fallo: es el caso normal del público.
    public void Un_sello_que_no_cuadra_devuelve_false_y_no_revienta()
    {
        var capacidad = new CapacidadFalsa { Verificacion = HttpStatusCode.Forbidden };

        Assert.False(Armar(capacidad).Matches(HttpCertificateIdSigner.Prefix + Sello, Sujeto));
    }

    [Fact] // empty: sin id no hay nada que comprobar, y no se sale a la red.
    public void Sin_id_no_se_verifica_nada()
    {
        var capacidad = new CapacidadFalsa();

        Assert.False(Armar(capacidad).Matches(null, Sujeto));
        Assert.False(Armar(capacidad).Matches("   ", Sujeto));
        Assert.Empty(capacidad.Llamadas);
    }

    // ── La migración: los ids viejos siguen valiendo ────────────────────────

    /// <summary>
    /// Un id emitido ANTES del cableado sigue verificando, y sin salir a la red.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que hace que este cambio no rompa nada impreso. El sello y el HMAC local no
    /// producen el mismo valor —distinto algoritmo, distinta llave—, así que sin esto cada diploma
    /// ya emitido dejaría de valer el día del despliegue.</para>
    ///
    /// <para>Y no sale a la red <b>por forma</b>: un id viejo no puede cuadrar contra el sello, así
    /// que preguntárselo a la capacidad sería una llamada garantizada a fallar por cada diploma
    /// antiguo que alguien verifique.</para>
    /// </remarks>
    [Fact]
    public void Un_id_anterior_al_cableado_sigue_verificando()
    {
        var local = Local();
        var viejo = local.Sign(Sujeto);
        var capacidad = new CapacidadFalsa();

        Assert.True(Armar(capacidad, local).Matches(viejo, Sujeto));
        Assert.Empty(capacidad.Llamadas);
    }

    [Fact] // y un id viejo de OTRO sujeto sigue sin cuadrar.
    public void Un_id_viejo_de_otro_sujeto_no_cuadra()
    {
        var local = Local();
        var viejo = local.Sign(new CertificateSubject("curso-2", "otra@ejemplo.co"));

        Assert.False(Armar(new CapacidadFalsa(), local).Matches(viejo, Sujeto));
    }

    /// <summary>
    /// Sin firmante heredado, un id viejo no se da por bueno.
    /// </summary>
    /// <remarks>
    /// Es un despliegue que nunca emitió con llave local. Devolver <c>true</c> «por si acaso»
    /// sería aceptar cualquier cadena de 32 hex como credencial.
    /// </remarks>
    [Fact]
    public void Sin_firmante_heredado_un_id_viejo_no_se_da_por_bueno()
    {
        var viejo = Local().Sign(Sujeto);

        Assert.False(Armar(new CapacidadFalsa()).Matches(viejo, Sujeto));
    }

    // ── Lo que NO se da por bueno ───────────────────────────────────────────

    /// <summary>
    /// Con la capacidad caída NO se da por buena una credencial: se falla.
    /// </summary>
    /// <remarks>
    /// «No sé» no es «no cuadra» ni «cuadra». Darlo por bueno dejaría que quien consiga escribir
    /// en el almacén fabrique un diploma con el nombre que quiera —que es justo lo que
    /// <c>Matches</c> impide—; darlo por falso diría que un diploma bueno es falso.
    /// </remarks>
    [Fact]
    public void Con_la_capacidad_caida_no_se_da_por_buena_ni_por_falsa()
    {
        var firmante = new HttpCertificateIdSigner(
            new FabricaFalsa(new CaidaTotal()), Options.Create(new AcademySettings { Mode = "Api" }));

        var ex = Assert.Throws<InvalidOperationException>(
            () => firmante.Matches(HttpCertificateIdSigner.Prefix + Sello, Sujeto));

        Assert.Contains("no responde", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", ex.Message, StringComparison.Ordinal);
    }

    [Fact] // y tampoco se emite un id que mañana nadie reconocería.
    public void Con_la_capacidad_caida_no_se_emite()
    {
        var firmante = new HttpCertificateIdSigner(
            new FabricaFalsa(new CaidaTotal()), Options.Create(new AcademySettings { Mode = "Api" }));

        Assert.Throws<InvalidOperationException>(() => firmante.Sign(Sujeto));
    }

    /// <summary>
    /// Un despliegue sin llaves del propósito GRITA, no dice «no cuadra».
    /// </summary>
    /// <remarks>
    /// Confundir «despliegue a medio configurar» con «sello falso» manda a buscar un ataque donde
    /// falta un paso de despliegue.
    /// </remarks>
    [Fact]
    public void Sin_llaves_del_proposito_se_grita()
    {
        var capacidad = new CapacidadFalsa { Verificacion = HttpStatusCode.NotFound };

        var ex = Assert.Throws<InvalidOperationException>(
            () => Armar(capacidad).Matches(HttpCertificateIdSigner.Prefix + Sello, Sujeto));

        Assert.Contains("Api.Signing", ex.Message, StringComparison.Ordinal);
    }

    private sealed class CaidaTotal : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
            => throw new HttpRequestException("Connection refused (127.0.0.1:5218)");
    }
}
