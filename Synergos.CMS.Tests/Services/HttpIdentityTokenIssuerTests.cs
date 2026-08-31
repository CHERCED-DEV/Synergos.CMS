using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Consiguiendo una identidad verificable contra <c>Api.Identity</c> (HU #14, rebanada 4).
/// </summary>
/// <remarks>
/// Lo que se prueba no es «pide un token». Es lo que este camino tiene de delicado: que <b>no
/// tumbe nada cuando falla</b> —un trámite no se para porque la identidad esté caída—, que <b>no
/// dé de alta dos veces</b> al mismo funcionario, y que <b>no se quede con un token vencido</b>.
/// </remarks>
public sealed class HttpIdentityTokenIssuerTests
{
    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Path, string? Key, string? Body)> Llamadas { get; } = new();
        public HashSet<string> Caidas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public CapacidadFalsa Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public CapacidadFalsa Estado(string ruta, HttpStatusCode codigo)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo);
            return this;
        }

        public CapacidadFalsa Caida(string ruta) { Caidas.Add(ruta); return this; }

        public int Veces(string ruta) => Llamadas.Count(l => l.Path == ruta);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            Llamadas.Add((path,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null,
                req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            if (Caidas.Contains(path)) throw new HttpRequestException("guionado: caída");

            return Task.FromResult(_rutas.TryGetValue(path, out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public FabricaFalsa(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://identity.local/") };
    }

    private sealed class Monitor<T> : IOptionsMonitor<T>
    {
        public Monitor(T v) => CurrentValue = v;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);

    private static readonly IdentitySubject Funcionario =
        new("gov.funcionario", "ana@entidad.gov.co", new[] { "funcionario" });

    private static string TokenQueVence(int minutos) => $$"""
        {"token":"tok-{{minutos}}","expiresAtUtc":"{{Ahora.AddMinutes(minutos):O}}",
         "sessionEndsAtUtc":"{{Ahora.AddHours(8):O}}"}
        """;

    private static CapacidadFalsa Feliz() => new CapacidadFalsa()
        .Estado("/v1/principals", HttpStatusCode.Created)
        .Ok("/v1/tokens", TokenQueVence(15));

    private static (HttpIdentityTokenIssuer Emisor, CapacidadFalsa Cap) Nuevo(
        CapacidadFalsa? cap = null, Func<DateTimeOffset>? reloj = null)
    {
        var c = cap ?? Feliz();
        return (new HttpIdentityTokenIssuer(
            new FabricaFalsa(c),
            new Monitor<IdentitySettings>(new IdentitySettings { Mode = "Api" }),
            NullLogger<HttpIdentityTokenIssuer>.Instance,
            reloj ?? (() => Ahora)), c);
    }

    // ── El camino feliz ─────────────────────────────────────────────────────

    /// <summary>Da de alta al sujeto y consigue su token.</summary>
    /// <remarks>
    /// El alta va SIN credencial a propósito: quien actúa ya entró por la sesión del CMS, y
    /// fabricarle una contraseña que nadie usa sería inventar un secreto que hay que custodiar.
    /// </remarks>
    [Fact]
    public async Task Da_de_alta_y_emite()
    {
        var (emisor, cap) = Nuevo();

        var token = await emisor.IssueAsync(Funcionario);

        Assert.Equal("tok-15", token);
        Assert.Equal(1, cap.Veces("/v1/principals"));
        Assert.Equal(1, cap.Veces("/v1/tokens"));

        var alta = cap.Llamadas.First(l => l.Path == "/v1/principals");
        Assert.Contains("\"secret\":null", alta.Body, StringComparison.Ordinal);
        Assert.Contains("funcionario", alta.Body, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(alta.Key), "El alta sin llave crearía dos principales.");
    }

    /// <summary>
    /// El sujeto que ya existe no es un fallo: se sigue a pedir el token.
    /// </summary>
    /// <remarks>
    /// Es el caso NORMAL a partir de la segunda vez que alguien decide algo. Tratar el 409 como
    /// error dejaría a ventanilla sin identidad desde la segunda decisión — y lo peor es que la
    /// primera funcionaría, que es como se cuela a producción.
    /// </remarks>
    [Fact]
    public async Task Un_sujeto_ya_registrado_no_es_un_fallo()
    {
        var cap = Feliz().Estado("/v1/principals", HttpStatusCode.Conflict);
        var (emisor, _) = Nuevo(cap);

        Assert.Equal("tok-15", await emisor.IssueAsync(Funcionario));
    }

    /// <summary>El token se reusa mientras valga: no se pide uno por decisión.</summary>
    [Fact]
    public async Task El_token_se_reusa_mientras_valga()
    {
        var (emisor, cap) = Nuevo();

        await emisor.IssueAsync(Funcionario);
        await emisor.IssueAsync(Funcionario);
        await emisor.IssueAsync(Funcionario);

        Assert.Equal(1, cap.Veces("/v1/tokens"));
    }

    /// <summary>
    /// Y se renueva ANTES de que venza, no cuando ya venció.
    /// </summary>
    /// <remarks>
    /// Entre que se lee de la caché y llega al otro lado hay una red. Reusarlo hasta el último
    /// segundo garantiza que alguna petición salga con un token recién vencido, y el rechazo
    /// llegaría de una capacidad que hizo lo correcto.
    /// </remarks>
    [Fact]
    public async Task El_token_se_renueva_antes_de_vencer()
    {
        var cap = Feliz().Ok("/v1/tokens", TokenQueVence(1));   // vence en 60 s
        var (emisor, _) = Nuevo(cap);                            // margen por defecto: 60 s

        await emisor.IssueAsync(Funcionario);
        await emisor.IssueAsync(Funcionario);

        Assert.Equal(2, cap.Veces("/v1/tokens"));
    }

    // ── Lo que NO puede pasar ───────────────────────────────────────────────

    /// <summary>
    /// Con la capacidad CAÍDA no se lanza: se sigue sin token.
    /// </summary>
    /// <remarks>
    /// <b>Es la propiedad que define esta seam.</b> Lanzar convertiría a <c>Api.Identity</c> en el
    /// punto único de fallo de ventanilla — exactamente lo que la HU #14 evitó al verificar los
    /// tokens en local. Sin token se sigue declarando quién actúa, que es lo que se hacía antes.
    /// </remarks>
    [Fact]
    public async Task Con_la_capacidad_caida_no_se_lanza()
    {
        var cap = Feliz().Caida("/v1/tokens");
        var (emisor, _) = Nuevo(cap);

        Assert.Null(await emisor.IssueAsync(Funcionario));
    }

    /// <summary>Y un rechazo tampoco: 403, 500 o lo que sea terminan en «sin token».</summary>
    [Fact]
    public async Task Un_rechazo_de_la_capacidad_deja_sin_token_y_no_lanza()
    {
        var cap = Feliz().Estado("/v1/tokens", HttpStatusCode.Forbidden);
        var (emisor, _) = Nuevo(cap);

        Assert.Null(await emisor.IssueAsync(Funcionario));
    }

    /// <summary>Un sujeto a medias no se manda: no hay identidad que pedir.</summary>
    [Fact]
    public async Task Un_sujeto_incompleto_ni_sale_a_la_red()
    {
        var (emisor, cap) = Nuevo();

        Assert.Null(await emisor.IssueAsync(new IdentitySubject("gov.funcionario", "  ", Array.Empty<string>())));
        Assert.Empty(cap.Llamadas);
    }

    /// <summary>La llave de alta es la MISMA para el mismo sujeto, y distinta para otro.</summary>
    /// <remarks>
    /// Con una llave nueva cada vez, dos peticiones simultáneas del mismo funcionario crearían
    /// dos principales para la misma persona — y cada uno con sus roles.
    /// </remarks>
    [Fact]
    public void La_llave_de_alta_es_determinista_por_sujeto()
    {
        var a = HttpIdentityTokenIssuer.LlaveDe(Funcionario);
        var b = HttpIdentityTokenIssuer.LlaveDe(
            new IdentitySubject("gov.funcionario", "ana@entidad.gov.co", new[] { "otro-rol" }));
        var otro = HttpIdentityTokenIssuer.LlaveDe(
            new IdentitySubject("gov.funcionario", "bruno@entidad.gov.co", new[] { "funcionario" }));

        Assert.Equal(a, b);       // los roles no la cambian: el sujeto es el mismo
        Assert.NotEqual(a, otro);
    }
}
