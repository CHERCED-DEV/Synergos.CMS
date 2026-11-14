using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces.Moderacion;
using Synergos.CMS.Web.Services.Moderacion;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La moderación en Api (012-blogs-social, I5): lo que el CMS le pide a <c>Api.Moderation</c> y cómo lo lee. La
/// capacidad abre un caso por denuncia; la cola del CMS es un caso por objetivo, y decidir decide todos los abiertos.
/// </summary>
public sealed class ModeracionEnApiTests
{
    private const string Llave = "a1b2c3d4e5f60718";

    /// <summary>Una capacidad de mentira: contesta por ruta y apunta lo que le llega.</summary>
    internal sealed class Capacidad : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        public Func<HttpRequestMessage, (HttpStatusCode Estado, string Json)?>? Respuesta { get; set; }

        public bool Caida { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (Caida)
            {
                throw new HttpRequestException("connection refused");
            }

            r.Headers.TryGetValues("Idempotency-Key", out var llave);
            Llamadas.Add((r.Method.Method, r.RequestUri!.PathAndQuery, llave?.FirstOrDefault(),
                r.Content is null ? string.Empty : await r.Content.ReadAsStringAsync(ct)));
            var (estado, json) = Respuesta?.Invoke(r) ?? (HttpStatusCode.NotFound, "{}");
            return new HttpResponseMessage(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    internal sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("http://moderacion.local/") };
    }

    internal static string Caso(string id, string objetivo, string estado = "Open", string? decision = null, int minuto = 0)
        => JsonSerializer.Serialize(new
        {
            id,
            targetKind = "blogs.publicacion",
            targetId = objetivo,
            reporterKind = "miembro",
            reporterId = Llave,
            reason = $"motivo de {id}",
            status = estado,
            decision,
            decidedByKind = (string?)null,
            decidedById = (string?)null,
            decisionNote = (string?)null,
            reportedAtUtc = new DateTimeOffset(2026, 11, 14, 10, minuto, 0, TimeSpan.Zero),
            decidedAtUtc = (DateTimeOffset?)null,
        });

    internal static string Pagina(params string[] casos)
        => $"{{\"items\":[{string.Join(',', casos)}],\"total\":{casos.Length},\"offset\":0,\"hasMore\":false}}";

    internal static ModeracionEnApi Cliente(Capacidad capacidad)
    {
        var tipo = Substitute.For<ITipoModerable>();
        tipo.Tipo.Returns("blogs.publicacion");
        tipo.LeerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ContenidoDenunciable($"Lo de {ci.ArgAt<string>(0)}", null));
        return new ModeracionEnApi(new Fabrica(capacidad), [tipo], TimeProvider.System, NullLogger<ModeracionEnApi>.Instance);
    }

    /// <summary>
    /// Denunciar manda el objetivo, quien denuncia por su LLAVE (nunca el correo) y el motivo, con una llave de
    /// idempotencia que es la misma para la misma persona y lo mismo, y otra para otra persona.
    /// </summary>
    [Fact]
    public async Task Denunciar_manda_la_llave_publica_y_es_la_misma_peticion_para_la_misma_persona()
    {
        var capacidad = new Capacidad { Respuesta = _ => (HttpStatusCode.Created, Caso("c1", "p-1")) };
        var cliente = Cliente(capacidad);
        var denuncia = new DenunciaNueva(new ObjetivoModerable("blogs.publicacion", "p-1"), "Lo de p-1", null, Llave, "Es spam");

        var caso = await cliente.DenunciarAsync(denuncia);
        await cliente.DenunciarAsync(denuncia);
        await cliente.DenunciarAsync(denuncia with { Denunciante = "0f1e2d3c4b5a6978" });

        Assert.Equal("c1", caso.Id);
        using var cuerpo = JsonDocument.Parse(capacidad.Llamadas[0].Cuerpo);
        Assert.Equal("POST /v1/cases", $"{capacidad.Llamadas[0].Metodo} {capacidad.Llamadas[0].Ruta}");
        Assert.Equal(Llave, cuerpo.RootElement.GetProperty("reporterId").GetString());
        Assert.Equal("Es spam", cuerpo.RootElement.GetProperty("reason").GetString());
        Assert.DoesNotContain("@", capacidad.Llamadas[0].Cuerpo, StringComparison.Ordinal);
        Assert.Equal(capacidad.Llamadas[0].Llave, capacidad.Llamadas[1].Llave);
        Assert.NotEqual(capacidad.Llamadas[0].Llave, capacidad.Llamadas[2].Llave);
    }

    /// <summary>La cola agrupa por objetivo: el más antiguo da el id y el motivo, y cuenta las denuncias.</summary>
    [Fact]
    public async Task La_cola_es_un_caso_por_objetivo_con_cuantas_denuncias_tiene()
    {
        var capacidad = new Capacidad
        {
            Respuesta = _ => (HttpStatusCode.OK, Pagina(Caso("c2", "p-1", minuto: 5), Caso("c1", "p-1", minuto: 1), Caso("c3", "p-2", minuto: 3))),
        };

        var cola = await Cliente(capacidad).AbiertosAsync(50);

        Assert.Equal(new[] { "c1", "c3" }, cola.Select(c => c.Id));
        Assert.Equal(2, cola[0].Denuncias);
        Assert.Equal("motivo de c1", cola[0].Motivo);
        Assert.Equal("Lo de p-1", cola[0].Resumen);
    }

    /// <summary>Retirar decide TODOS los abiertos del objetivo, con la llave de quien decide y su motivo.</summary>
    [Fact]
    public async Task Decidir_decide_todos_los_abiertos_del_objetivo()
    {
        var capacidad = new Capacidad
        {
            Respuesta = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/v1/cases/c1") => (HttpStatusCode.OK, Caso("c1", "p-1")),
                ("GET", "/v1/cases") => (HttpStatusCode.OK, Pagina(Caso("c1", "p-1", minuto: 1), Caso("c2", "p-1", minuto: 5))),
                ("POST", _) => (HttpStatusCode.OK, Caso("cx", "p-1", "Decided", "Remove")),
                _ => null,
            },
        };

        var tomada = await Cliente(capacidad).DecidirAsync(
            "c1", DecisionDeModeracion.Retirar, new QuienModera(Llave, "Mónica Moderadora"), "Spam comercial");

        Assert.Equal(ResultadoDeDecidir.Decidido, tomada.Resultado);
        Assert.Equal("Mónica Moderadora", tomada.Caso!.Quien);
        var decisiones = capacidad.Llamadas.Where(l => l.Metodo == "POST").ToList();
        Assert.Equal(new[] { "/v1/cases/c1/decide", "/v1/cases/c2/decide" }, decisiones.Select(l => l.Ruta));
        using var cuerpo = JsonDocument.Parse(decisiones[0].Cuerpo);
        Assert.Equal("Remove", cuerpo.RootElement.GetProperty("decision").GetString());
        Assert.Equal(Llave, cuerpo.RootElement.GetProperty("decidedById").GetString());
        Assert.Equal("Spam comercial", cuerpo.RootElement.GetProperty("note").GetString());
    }

    /// <summary>Lo decidido al revés es un conflicto visible y no se decide nada encima.</summary>
    [Fact]
    public async Task Lo_decidido_al_reves_es_un_conflicto_y_no_se_toca()
    {
        var capacidad = new Capacidad
        {
            Respuesta = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("GET", "/v1/cases/c1") => (HttpStatusCode.OK, Caso("c1", "p-1", "Decided", "Keep")),
                ("GET", "/v1/cases") => (HttpStatusCode.OK, Pagina(Caso("c1", "p-1", "Decided", "Keep"))),
                _ => null,
            },
        };

        var tomada = await Cliente(capacidad).DecidirAsync(
            "c1", DecisionDeModeracion.Retirar, new QuienModera(Llave, "Mónica"), "Spam");

        Assert.Equal(ResultadoDeDecidir.YaDecididoDistinto, tomada.Resultado);
        Assert.DoesNotContain(capacidad.Llamadas, l => l.Metodo == "POST");
    }

    /// <summary>La capacidad caída, un 5xx o un 401 de llave: la moderación no contestó, y se dice.</summary>
    [Fact]
    public async Task Lo_que_no_contesta_es_moderacion_no_disponible()
    {
        var denuncia = new DenunciaNueva(new ObjetivoModerable("blogs.publicacion", "p-1"), "x", null, Llave, "Spam");

        await Assert.ThrowsAsync<ModeracionNoDisponibleException>(() => Cliente(new Capacidad { Caida = true }).DenunciarAsync(denuncia));
        await Assert.ThrowsAsync<ModeracionNoDisponibleException>(() => Cliente(new Capacidad
        {
            Respuesta = _ => (HttpStatusCode.ServiceUnavailable, "{}"),
        }).AbiertosAsync(50));
        await Assert.ThrowsAsync<ModeracionNoDisponibleException>(() => Cliente(new Capacidad
        {
            Respuesta = _ => (HttpStatusCode.Unauthorized, "{}"),
        }).DenunciarAsync(denuncia));
    }
}
