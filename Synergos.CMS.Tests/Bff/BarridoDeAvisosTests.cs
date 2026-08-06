using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El barrido de los avisos que quedaron colgados (HU #29).
/// </summary>
/// <remarks>
/// <para><b>Lo que estos tests fijan es el reparto, no el mecanismo.</b> El CUÁNDO se reintenta y
/// el CUÁNTAS VECES antes de rendirse viven acá; el QUÉ está colgado y el CÓMO se reintenta viven
/// en <c>Api.Notifications</c>. Por eso lo que se afirma abajo son <i>las llamadas que salen</i>:
/// si alguna vez el techo se decidiera del otro lado, estos tests seguirían pasando y el gate
/// <c>El_barrido_de_avisos_NO_vive_dentro_de_la_capacidad</c> sería lo único que lo vería.</para>
///
/// <para>Se prueba <see cref="DeliverySweeper.UnaVueltaAsync"/> y no el <c>BackgroundService</c>:
/// esperar a que un lazo de fondo dé una vuelta es un test que a veces falla por lento. Lo que
/// tiene reglas es la vuelta.</para>
/// </remarks>
public sealed class BarridoDeAvisosTests
{
    /// <summary>La capacidad, guionada: contesta lo que se le diga y anota lo que le piden.</summary>
    private sealed class CapacidadFalsa : HttpMessageHandler
    {
        public string ColaJson { get; set; } = """{"items":[],"total":0,"offset":0,"hasMore":false}""";

        /// <summary>Con qué contesta cada reintento, por id de envío. Por defecto, acepta.</summary>
        public Dictionary<string, HttpStatusCode> Reintentos { get; } = new(StringComparer.Ordinal);

        public List<(string Method, string Path, string? Body)> Llamadas { get; } = new();

        public IEnumerable<string> Ids(string sufijo)
            => Llamadas.Where(l => l.Path.EndsWith(sufijo, StringComparison.Ordinal))
                       .Select(l => l.Path.Split('/')[3]);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Llamadas.Add((request.Method.Method, path, cuerpo));

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(ColaJson, Encoding.UTF8, "application/json"),
                };
            }

            if (path.EndsWith("/retry", StringComparison.Ordinal))
            {
                var id = path.Split('/')[3];
                var codigo = Reintentos.GetValueOrDefault(id, HttpStatusCode.OK);
                return new HttpResponseMessage(codigo)
                {
                    Content = new StringContent("""{"code":"guionado"}""", Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"GivenUp"}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Fabrica : IHttpClientFactory
    {
        private readonly CapacidadFalsa _handler;
        public Fabrica(CapacidadFalsa h) => _handler = h;
        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://notifications.local/") };
    }

    private static string Cola(params (string Id, int Intentos, string? Causa)[] envios)
    {
        var items = envios.Select(e =>
            $$"""{"id":"{{e.Id}}","status":"Queued","attempts":{{e.Intentos}},"lastError":{{(e.Causa is null ? "null" : $"\"{e.Causa}\"")}}}""");

        return $$"""{"items":[{{string.Join(",", items)}}],"total":{{envios.Length}},"offset":0,"hasMore":false}""";
    }

    private static (DeliverySweeper Barrido, CapacidadFalsa Capacidad) Nuevo(int techo = 8)
    {
        var capacidad = new CapacidadFalsa();
        var opciones = new TestOptionsMonitor(new SweepOptions { DeliveryRetryCeiling = techo });
        return (new DeliverySweeper(new Fabrica(capacidad), opciones, NullLogger<DeliverySweeper>.Instance), capacidad);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<SweepOptions>
    {
        public TestOptionsMonitor(SweepOptions valor) => CurrentValue = valor;
        public SweepOptions CurrentValue { get; }
        public SweepOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<SweepOptions, string?> listener) => null;
    }

    // ── Reintentar ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sin_nada_colgado_no_se_toca_nada()
    {
        var (barrido, capacidad) = Nuevo();

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Single(capacidad.Llamadas);   // solo la consulta
        Assert.Equal("GET", capacidad.Llamadas[0].Method);
    }

    [Fact]
    public async Task Lo_colgado_se_REINTENTA()
    {
        var (barrido, capacidad) = Nuevo();
        capacidad.ColaJson = Cola(("d-1", 0, null), ("d-2", 3, "timeout"));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Equal(new[] { "d-1", "d-2" }, capacidad.Ids("/retry"));
    }

    [Fact]
    public async Task Un_reintento_que_falla_NO_corta_el_barrido()
    {
        // Es el caso ESPERADO, no la excepción: lo que está colgado suele estarlo porque el
        // proveedor no contesta. Si el primer fallo cortara la vuelta, un envío atascado
        // bloquearía a todos los que van detrás — y serían justo los más viejos los que pasan.
        var (barrido, capacidad) = Nuevo();
        capacidad.ColaJson = Cola(("d-1", 0, null), ("d-2", 0, null), ("d-3", 0, null));
        capacidad.Reintentos["d-1"] = HttpStatusCode.ServiceUnavailable;

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Equal(new[] { "d-1", "d-2", "d-3" }, capacidad.Ids("/retry"));
    }

    // ── Rendirse ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Al_llegar_al_techo_se_ABANDONA_y_no_se_reintenta()
    {
        // El techo se comprueba ANTES de reintentar. Al revés se haría un intento de más —el que
        // sobrepasa el techo— contra un proveedor que ya dijo que no ocho veces.
        var (barrido, capacidad) = Nuevo(techo: 8);
        capacidad.ColaJson = Cola(("agotado", 8, "timeout"));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Empty(capacidad.Ids("/retry"));
        Assert.Equal(new[] { "agotado" }, capacidad.Ids("/give-up"));
    }

    [Fact]
    public async Task Un_intento_ANTES_del_techo_todavia_se_reintenta()
    {
        // El borde por el otro lado: rendirse un intento antes regala una entrega que iba a salir.
        var (barrido, capacidad) = Nuevo(techo: 8);
        capacidad.ColaJson = Cola(("casi", 7, "timeout"));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Equal(new[] { "casi" }, capacidad.Ids("/retry"));
        Assert.Empty(capacidad.Ids("/give-up"));
    }

    [Fact]
    public async Task El_abandono_lleva_la_CAUSA()
    {
        // «Se rindió tras ocho intentos» sin decir por qué no le sirve a nadie: la diferencia
        // entre una credencial vencida y un dominio mal escrito es lo único accionable acá.
        var (barrido, capacidad) = Nuevo(techo: 2);
        capacidad.ColaJson = Cola(("agotado", 2, "el proveedor no pudo atender el envío: timeout"));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        var cuerpo = capacidad.Llamadas.Single(l => l.Path.EndsWith("/give-up", StringComparison.Ordinal)).Body;
        Assert.Contains("timeout", cuerpo, StringComparison.Ordinal);
        Assert.Contains("2", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_causa_conocida_el_abandono_lo_dice_en_vez_de_callarse()
    {
        var (barrido, capacidad) = Nuevo(techo: 1);
        capacidad.ColaJson = Cola(("agotado", 1, null));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        var cuerpo = capacidad.Llamadas.Single(l => l.Path.EndsWith("/give-up", StringComparison.Ordinal)).Body;
        Assert.Contains("sin detalle", cuerpo, StringComparison.Ordinal);
    }

    // ── El interruptor ──────────────────────────────────────────────────────

    [Fact]
    public async Task En_cero_el_barrido_no_pregunta_siquiera()
    {
        // Un despliegue puede decidir que prefiere no reintentar nada automáticamente. Que exista
        // el interruptor es lo que hace que el default sea una opinión y no una imposición.
        //
        // Y no pregunta: apagarlo tiene que dejar de gastar peticiones contra la capacidad, no
        // solo dejar de actuar sobre la respuesta.
        var (barrido, capacidad) = Nuevo(techo: 0);
        capacidad.ColaJson = Cola(("d-1", 0, null));

        await barrido.UnaVueltaAsync(CancellationToken.None);

        Assert.Empty(capacidad.Llamadas);
    }

    [Fact]
    public void El_techo_por_defecto_es_el_MISMO_que_el_de_las_compensaciones()
    {
        // Son la misma clase de decisión —cuánto insistir contra un tercero que no contesta— y dos
        // números distintos obligarían a explicar por qué. Si algún día tienen que diferir, que
        // sea porque alguien lo decidió.
        Assert.Equal(CompensationLimits.MaxAttempts, new SweepOptions().DeliveryRetryCeiling);
    }
}
