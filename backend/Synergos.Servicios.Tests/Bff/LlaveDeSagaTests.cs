using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Eventos.Domain;
using Synergos.CMS.Tests.Contratos;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La llave que abre una saga cabe en todo lo que la saga deriva de ella, y el orquestador rechaza
/// la que no cabría (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> <c>Bff.Eventos</c> publicaba y aceptaba una <c>Idempotency-Key</c> de
/// 128, y con 91 o más la compra moría en un 500: la llave es el identificador de la saga y de ella
/// cuelgan las de cada paso, <c>{saga}|hold:{item}</c> pasaba de 128 e <see cref="IdempotencyKey.Of"/>
/// lanzaba. Hoy el orquestador acepta <see cref="LlaveDeSaga.MaxLength"/>, y la sonda del contrato
/// comprueba que publique ese número.</para>
///
/// <para><b>Lo que la sonda no puede ver es que la cuenta sea VERDAD</b>, porque manda <c>{}</c> y
/// nunca llega a la saga. Eso lo mira el primer test, con la saga más larga que esa llave puede
/// abrir —la llave máxima con el <c>#100</c> del último reintento— y todos los pasos que la
/// estiran: apartar con un id de pozo de 32 caracteres (un <c>Guid</c> "n", como los da
/// <c>Api.Inventory</c>), autorizar, capturar y las dos compensaciones que llevan el id de la
/// compensación, devolver el aforo y la plata. Si un paso nuevo deriva un sufijo más largo que
/// <see cref="LlaveDeSaga.PasoMaximo"/>, aquí sale rojo.</para>
/// </remarks>
public sealed class LlaveDeSagaTests
{
    private static readonly string PozoButaca = new('a', 32);
    private static readonly string PozoGeneral = new('b', 32);
    private static readonly string ApartadoButaca = new('c', 32);
    private static readonly string ApartadoGeneral = new('d', 32);
    private static readonly string Pago = new('e', 32);

    [Fact]
    public async Task La_saga_mas_larga_que_abre_una_llave_maxima_deriva_todas_sus_llaves_sin_pasarse()
    {
        // La saga más larga que Abrir puede dar con una llave que el orquestador acepta. Pasarla tal
        // cual a BuyAsync es lo mismo que el centésimo intento: Abrir devuelve la llave si no existe.
        var sagaId = new string('k', LlaveDeSaga.MaxLength) + "#" + LlaveDeSaga.IntentosTrasDeshacer;
        var caps = new Capacidades();
        var (flow, sagas) = Nuevo(caps);

        var compra = await flow.BuyAsync("e1", Ref.Create("eventos.comprador", "u-1"),
            [new TicketLine("vip", "A-14", 1), new TicketLine("general", null, 2)], sagaId, CancellationToken.None);
        Assert.True(compra.IsOk, compra.Rejection?.Code);

        // Consumir la butaca sale y consumir el cupo general no: se devuelve el aforo ya consumido
        // con un ajuste y la plata ya capturada con una devolución, las dos llaves más largas.
        var confirmada = await flow.ConfirmAsync(sagaId, CancellationToken.None);
        Assert.False(confirmada.IsOk);

        var llaves = caps.Llaves();
        var esperadas = new[]
        {
            $"hold:{PozoButaca}", $"hold:{PozoGeneral}", "authorize", "capture", "restock:", "refund:",
        };
        foreach (var paso in esperadas)
        {
            Assert.True(llaves.Any(l => l.StartsWith($"{sagaId}|{paso}", StringComparison.Ordinal)),
                $"Ninguna llave de «{paso}» salió hacia una capacidad: o el paso no corrió, o lanzó al derivarla. " +
                $"Salieron: {string.Join(", ", llaves.Select(l => l[(sagaId.Length + 1)..]))}");
        }

        Assert.All(llaves, l => Assert.True(l.Length <= IdempotencyKey.MaxLength, $"{l.Length}: {l}"));
        Assert.Equal(IdempotencyKey.MaxLength, llaves.Max(l => l.Length));

        var saga = sagas.Find(sagaId)!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Empty(saga.Pending());
    }

    public static TheoryData<string> Orquestadores() => new() { "Eventos", "Tienda", "Salud", "Viajes" };

    /// <summary>
    /// Los cuatro orquestadores abren su saga con la llave del llamador, así que los cuatro aceptan
    /// <see cref="LlaveDeSaga.MaxLength"/> y rechazan una más larga con su 400, no con un 500 a mitad
    /// de la saga.
    /// </summary>
    [Theory]
    [MemberData(nameof(Orquestadores))]
    public async Task El_orquestador_acepta_la_llave_maxima_y_rechaza_una_mas_larga(string orquestador)
    {
        var (pieza, ruta, prefijo) = orquestador switch
        {
            "Eventos" => ((PiezaPublicada)new PiezaPublicada<Synergos.Bff.Eventos.Contracts.TicketPurchaseResponse>("Eventos"), "/v1/ticket-purchases", "eventos"),
            "Tienda" => (new PiezaPublicada<Synergos.Bff.Tienda.Contracts.PurchaseResponse>("Tienda"), "/v1/purchases", "tienda"),
            "Salud" => (new PiezaPublicada<Synergos.Bff.Salud.Contracts.AppointmentResponse>("Salud"), "/v1/appointments", "salud"),
            _ => (new PiezaPublicada<Synergos.Bff.Viajes.Contracts.TripResponse>("Viajes"), "/v1/trips", "viajes"),
        };
        using var host = pieza.Levantar(conContrato: false);

        var justa = await Abrir(host, ruta, LlaveDeSaga.MaxLength);
        Assert.NotEqual($"{prefijo}.idempotency_key_required", justa.Code);
        Assert.True(justa.Status < 500, $"{orquestador}: la llave de {LlaveDeSaga.MaxLength} dio {justa.Status}.");

        var larga = await Abrir(host, ruta, LlaveDeSaga.MaxLength + 1);
        Assert.Equal(400, larga.Status);
        Assert.Equal($"{prefijo}.idempotency_key_required", larga.Code);
    }

    private static async Task<(int Status, string? Code)> Abrir(HostDeLaPieza host, string ruta, int largo)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(ruta, UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add(SharedKeyAuth.HeaderName, PiezaPublicada.Llave);
        req.Headers.Add(IdempotencyHeader.Name, new string('k', largo));

        using var r = await host.Cliente.SendAsync(req);
        var cuerpo = await r.Content.ReadAsStringAsync();
        string? code = null;
        try { code = (string?)System.Text.Json.Nodes.JsonNode.Parse(cuerpo)?["code"]; }
        catch (System.Text.Json.JsonException) { /* sin cuerpo JSON: no hay código */ }
        return ((int)r.StatusCode, code);
    }

    private static (TicketingFlow Flow, Memoria Sagas) Nuevo(Capacidades caps)
    {
        var sagas = new Memoria();
        var fabrica = new Fabrica(caps);
        var api = new EventosCapabilities(fabrica);
        var vocabulario = new SagaVocabulary("eventos", "la compra de entradas");
        var comp = new Compensator<TicketingSaga>(
            new EventosCompensationExecutor(api), TimeProvider.System, NullLogger<Compensator<TicketingSaga>>.Instance);
        var aviso = new CompensationAlert(fabrica, vocabulario, Options.Create(new AlertOptions()));
        var motor = new SagaEngine<TicketingSaga>(sagas, comp, aviso, ArriendoDePrueba.Nuevo(), vocabulario,
            TimeProvider.System, NullLogger<SagaEngine<TicketingSaga>>.Instance);
        return (new TicketingFlow(api, motor, TimeProvider.System, NullLogger<TicketingFlow>.Instance), sagas);
    }

    private sealed class Memoria : ISagaStore<TicketingSaga>
    {
        private readonly Dictionary<string, TicketingSaga> _s = new(StringComparer.Ordinal);
        public TicketingSaga? Find(string id) => _s.GetValueOrDefault(id);
        public void Put(TicketingSaga saga) => _s[saga.Id] = saga;
        public void Invalidate() { }
        public IReadOnlyList<TicketingSaga> WithPendingCompensations()
            => _s.Values.Where(x => x.IsUnwinding() && x.Pending().Count > 0).ToList();
        public IReadOnlyList<TicketingSaga> StartedBefore(DateTimeOffset limite) => [];
    }

    private sealed class Fabrica(Capacidades caps) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(caps, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    /// <summary>
    /// Las tres capacidades, guionadas con identificadores del largo de los de verdad, y grabando la
    /// <c>Idempotency-Key</c> de cada llamada.
    /// </summary>
    private sealed class Capacidades : HttpMessageHandler
    {
        private readonly List<string> _llaves = [];

        public IReadOnlyList<string> Llaves()
        {
            lock (_llaves) return _llaves.ToList();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Headers.TryGetValues(IdempotencyHeader.Name, out var v))
            {
                lock (_llaves) _llaves.Add(v.Single());
            }

            var ruta = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}";
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            return Task.FromResult(ruta switch
            {
                "POST /v1/quotes" => Json(Total(300_000)),
                "GET /v1/items" when query.Contains("A-14", StringComparison.Ordinal) => Json(Pozo(PozoButaca, "e1/vip/A-14", 1)),
                "GET /v1/items" => Json(Pozo(PozoGeneral, "e1/general", 400)),
                _ when ruta == $"POST /v1/items/{PozoButaca}/holds" => Json(Apartado(ApartadoButaca, 1)),
                _ when ruta == $"POST /v1/items/{PozoGeneral}/holds" => Json(Apartado(ApartadoGeneral, 2)),
                _ when ruta == $"POST /v1/holds/{ApartadoButaca}/consume" => Json(Apartado(ApartadoButaca, 1)),
                _ when ruta == $"POST /v1/holds/{ApartadoGeneral}/consume" => Problema(HttpStatusCode.Conflict, "inventory.hold_expired"),
                _ when ruta == $"POST /v1/holds/{ApartadoGeneral}/release" => Json(Apartado(ApartadoGeneral, 2, liberado: true)),
                _ when ruta == $"POST /v1/items/{PozoButaca}/adjust" => Json(Pozo(PozoButaca, "e1/vip/A-14", 1)),
                "POST /v1/payments" => Json(Cobro("Authorized", 0)),
                _ when ruta == $"POST /v1/payments/{Pago}/capture" => Json(Cobro("Captured", 300_000)),
                _ when ruta == $"GET /v1/payments/{Pago}" => Json(Cobro("Captured", 300_000)),
                _ when ruta == $"POST /v1/payments/{Pago}/refund" => Json(Cobro("Refunded", 0)),
                _ => Problema(HttpStatusCode.NotFound, "stub.no_route"),
            });
        }

        private static string Total(decimal monto)
            => $$$"""{"subtotal":{"amount":{{{monto}}},"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":{{{monto}}},"currency":"COP"}}""";

        private static string Pozo(string id, string sujeto, int cuantos)
            => $$"""{"id":"{{id}}","subjectKind":"eventos.aforo","subjectId":"{{sujeto}}","onHand":{{cuantos}},"available":{{cuantos}}}""";

        private static string Apartado(string id, int cuantos, bool liberado = false)
            => $$"""{"id":"{{id}}","quantity":{{cuantos}},"expiresAtUtc":"2026-10-07T10:15:00+00:00","released":{{(liberado ? "true" : "false")}}}""";

        private static string Cobro(string estado, decimal devolvible)
            => $$$"""{"id":"{{{Pago}}}","status":"{{{estado}}}","amount":{"amount":300000,"currency":"COP"},"refundable":{"amount":{{{devolvible}}},"currency":"COP"}}""";

        private static HttpResponseMessage Json(string cuerpo)
            => new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Problema(HttpStatusCode codigo, string code)
            => new(codigo)
            {
                Content = new StringContent($$"""{"code":"{{code}}","detail":"guionado"}""", Encoding.UTF8, "application/problem+json"),
            };
    }
}
