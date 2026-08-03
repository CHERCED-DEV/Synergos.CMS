using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="HttpSearchAnalyticsStore"/> — la analítica de búsqueda servida por el
/// servicio de sesión (ADR 0130).
/// </summary>
/// <remarks>
/// <para>Lo que se fija aquí no es "el HTTP funciona" —eso se verificó levantando los dos
/// procesos— sino <b>cómo se comporta cuando el otro lado NO está</b>. Es la única parte que
/// importa de verdad: un servicio auxiliar caído no puede convertirse en un CMS caído.</para>
/// </remarks>
public sealed class HttpSearchAnalyticsStoreTests
{
    /// <summary>Handler que responde lo que se le diga, o revienta si se le pide.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Seen { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Seen) { Seen.Add(request); }
            return Task.FromResult(_respond(request));
        }
    }

    private static (HttpSearchAnalyticsStore Store, StubHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null)
    {
        var handler = new StubHandler(respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
        }));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ =>
            new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://sesiones.local/") });
        return (new HttpSearchAnalyticsStore(factory, NullLogger<HttpSearchAnalyticsStore>.Instance), handler);
    }

    [Fact]
    public void Registrar_NO_hace_la_llamada_en_el_hilo_del_usuario()
    {
        // El contrato: Record encola y vuelve. Si hiciera el POST en línea, cada búsqueda de
        // un visitante esperaría un salto de red para guardar una métrica.
        var (store, handler) = Build();

        store.Record("camisa", 4, 12);

        lock (handler.Seen) Assert.Empty(handler.Seen);
    }

    [Fact]
    public void Registrar_NUNCA_lanza_aunque_el_destino_sea_imposible()
    {
        // Lo exige la interfaz: una búsqueda que funcionó no puede fallar porque su métrica
        // no se pudo guardar.
        var (store, _) = Build(_ => throw new HttpRequestException("sin ruta"));

        var boom = Record.Exception(() => store.Record("camisa", 4, 12));

        Assert.Null(boom);
    }

    [Fact]
    public void La_cola_tiene_TOPE_y_descarta_en_vez_de_comerse_la_memoria()
    {
        // Con el servicio caído y sin lazo drenando, una cola sin tope crecería sin límite:
        // la analítica habría tumbado justo al CMS que venía a descargar.
        var (store, _) = Build();

        for (var i = 0; i < HttpSearchAnalyticsStore.QueueCapacity + 250; i++)
        {
            store.Record($"q{i}", 1, 1);
        }

        Assert.True(store.Dropped > 0, "con la cola llena tiene que descartar y contarlo");
    }

    [Fact]
    public async Task Si_el_servicio_no_responde_se_sirve_VACIO_y_no_se_propaga_el_fallo()
    {
        // Un dashboard sin datos es un inconveniente. Un dashboard que revienta porque un
        // servicio auxiliar está caído convierte un fallo de analítica en uno de operación.
        var (store, _) = Build(_ => throw new HttpRequestException("caído"));

        var top = await store.GetTopQueriesAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 10);
        var huecos = await store.GetTopNoResultQueriesAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, 10);

        Assert.Empty(top);
        Assert.Empty(huecos);
    }

    [Fact]
    public async Task Un_500_del_servicio_tampoco_tumba_la_lectura()
    {
        var (store, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Empty(await store.GetTopQueriesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5));
    }

    [Fact]
    public async Task Las_dos_lecturas_pegan_a_RUTAS_distintas()
    {
        // Si ambas apuntaran al mismo sitio, "queries sin resultado" mostraría el top general
        // y el equipo editorial trabajaría sobre una lista equivocada sin notarlo.
        var (store, handler) = Build();

        await store.GetTopQueriesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5);
        await store.GetTopNoResultQueriesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5);

        List<string> paths;
        lock (handler.Seen) paths = handler.Seen.Select(r => r.RequestUri!.AbsolutePath).ToList();

        Assert.Equal(2, paths.Count);
        Assert.Contains("/v1/search/top", paths);
        Assert.Contains("/v1/search/no-results", paths);
    }

    [Fact]
    public async Task La_ventana_y_el_tope_viajan_al_servicio()
    {
        // Si no viajaran, el servicio aplicaría SUS defaults y el dashboard mostraría una
        // ventana distinta de la que el usuario pidió, sin decirlo.
        var (store, handler) = Build();
        var from = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc);

        await store.GetTopQueriesAsync(from, to, 42);

        string query;
        lock (handler.Seen) query = handler.Seen.Single().RequestUri!.Query;

        Assert.Contains("2026-03-01", query, StringComparison.Ordinal);
        Assert.Contains("2026-03-08", query, StringComparison.Ordinal);
        Assert.Contains("limit=42", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_cuerpo_null_se_sirve_como_lista_vacia_y_no_como_null()
    {
        // "null" es JSON válido. Sin este cuidado, el llamador recibiría null donde el
        // contrato promete una lista y reventaría al iterarla.
        var (store, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        });

        Assert.Empty(await store.GetTopQueriesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, 5));
    }
}
