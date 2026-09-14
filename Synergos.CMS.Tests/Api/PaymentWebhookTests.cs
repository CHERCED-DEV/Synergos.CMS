using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Storage;
using Synergos.Api.Payments.Transport;
using Synergos.Core;
using Synergos.Shared;
using Pagos = Synergos.Api.Payments.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El camino de vuelta: lo que la pasarela cuenta de un cobro suyo (HU #27).
/// </summary>
/// <remarks>
/// <para><b>Es el único endpoint de la capacidad que no va detrás de la llave compartida</b>,
/// porque quien lo llama es un tercero que no la tiene. Lo que lo protege es la firma, y nada
/// más: sin verificarla, cualquiera que sepa la URL marca un cobro como pagado y el pedido
/// sale.</para>
///
/// <para><b>Y lo segundo que importa acá es el ORDEN DE LLEGADA.</b> Un webhook se reentrega
/// hasta ver un 2xx y nada garantiza que el <c>PENDING</c> llegue antes que el desenlace. Un
/// motor que aplique siempre lo último que le cuenten retrocede un cobro ya capturado — y eso no
/// falla: se guarda.</para>
/// </remarks>
public sealed class PaymentWebhookTests
{
    private sealed class RelojFalso : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public RelojFalso(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MemoriaStore : IPaymentStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Pagos.Payment> _p = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _k = new(StringComparer.Ordinal);

        public Pagos.Payment? Find(string id) => _p.GetValueOrDefault(id);

        public Pagos.Payment? FindByProviderReference(string providerReference)
            => _p.Values.FirstOrDefault(x => x.ProviderReference == providerReference);

        public IReadOnlyList<Pagos.Payment> ForSubject(Ref forWhat) => _p.Values.Where(x => x.For == forWhat).ToList();
        public void Put(Pagos.Payment payment) => _p[payment.Id] = payment;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");
        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;
    }

    private const string Secreto = "test_events_ABC123";
    private static readonly DateTimeOffset Ahora = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly Ref Compra = Ref.Create("tienda.compra", "c-1");
    private static readonly Ref Pagador = Ref.Create("identity.member", "m-1");

    private static WebhookVerifier Verificador(string? secreto = Secreto, DateTimeOffset? ahora = null)
        => new(Options.Create(new WompiOptions { EventsSecret = secreto }), new RelojFalso(ahora ?? Ahora));

    /// <summary>Un evento con la forma que manda Wompi, ya firmado con <paramref name="secreto"/>.</summary>
    private static (string Cuerpo, WebhookHeaders Cabeceras) Evento(
        string estado = "APPROVED", long centavos = 11900000, string referencia = "syn-1",
        DateTimeOffset? cuando = null, string secreto = Secreto)
    {
        var ts = (cuando ?? Ahora).ToUnixTimeSeconds();
        var cuerpo =
            $$"""
            {
              "event": "transaction.updated",
              "data": {
                "transaction": {
                  "id": "tx-1",
                  "status": "{{estado}}",
                  "reference": "{{referencia}}",
                  "amount_in_cents": {{centavos}},
                  "currency": "COP"
                }
              },
              "timestamp": {{ts}},
              "signature": {
                "properties": ["transaction.id", "transaction.status", "transaction.amount_in_cents"],
                "checksum": "no-se-mira"
              },
              "environment": "test"
            }
            """;

        // Se firma lo MISMO que el verificador va a recalcular, resolviendo las rutas del propio
        // evento. Quemar un checksum probaría que el test sabe copiar, no que el cálculo cuadra.
        var esperado = WompiSignature.EventChecksum(
            new[] { "tx-1", estado, centavos.ToString() }, ts, secreto);

        return (cuerpo, new WebhookHeaders(esperado));
    }

    private static (Pagos.PaymentService Svc, MemoriaStore Store, Pagos.Payment Cobro) Autorizado(
        Pagos.PaymentStatus estado = Pagos.PaymentStatus.Authorized, decimal monto = 119000m)
    {
        var store = new MemoriaStore();
        var cobro = new Pagos.Payment(
            "p-1", Compra, Pagador, Money.Of(monto, "COP"), estado, "wompi", "syn-1",
            Array.Empty<Pagos.Refund>(), Ahora);
        store.Put(cobro);

        var proveedor = new LoggingPaymentProvider(NullLogger<LoggingPaymentProvider>.Instance);
        return (new Pagos.PaymentService(store, proveedor, store, new RelojFalso(Ahora)), store, cobro);
    }

    // ── La firma ────────────────────────────────────────────────────────────

    [Fact]
    public void Un_evento_bien_firmado_pasa()
    {
        var (cuerpo, cabeceras) = Evento();

        Assert.Null(Verificador().Verify(cabeceras, cuerpo));
    }

    [Fact]
    public void Un_cuerpo_alterado_despues_de_firmar_NO_pasa()
    {
        // Es el ataque entero: un endpoint público, sin llave compartida, que mueve plata. Sin
        // verificar la firma, cualquiera marcaría como pagado un cobro que nunca ocurrió y el
        // pedido saldría.
        var (_, cabeceras) = Evento(estado: "DECLINED");
        var (otroCuerpo, _) = Evento(estado: "APPROVED");

        var malo = Verificador().Verify(cabeceras, otroCuerpo);

        Assert.Equal("payments.webhook_signature_invalid", malo!.Code);
    }

    [Fact]
    public void Una_firma_de_OTRO_secreto_no_pasa()
    {
        var (cuerpo, cabeceras) = Evento(secreto: "test_events_DEL_ATACANTE");

        Assert.Equal("payments.webhook_signature_invalid", Verificador().Verify(cabeceras, cuerpo)!.Code);
    }

    [Fact]
    public void Una_peticion_LEGITIMA_pero_vieja_no_se_acepta()
    {
        // Sin ventana, un evento capturado sirve para siempre: no hace falta falsificar nada,
        // basta con repetir uno legítimo que se grabó — y repetir un APPROVED es, a ojos del
        // sistema, cobrar dos veces.
        var (cuerpo, cabeceras) = Evento(cuando: Ahora.AddHours(-2));

        Assert.Equal("payments.webhook_replay", Verificador().Verify(cabeceras, cuerpo)!.Code);
    }

    [Fact]
    public void Sin_secreto_configurado_NO_se_acepta_ningun_evento()
    {
        // La alternativa —aceptar todo mientras no se configure— convierte un olvido de
        // despliegue en un endpoint abierto que da cobros por buenos.
        var (cuerpo, cabeceras) = Evento();

        Assert.Equal("payments.webhook_not_configured", Verificador(secreto: null).Verify(cabeceras, cuerpo)!.Code);
    }

    [Fact]
    public void Un_evento_SIN_cabecera_de_firma_no_se_acepta()
    {
        var (cuerpo, _) = Evento();

        Assert.Equal("payments.webhook_unsigned", Verificador().Verify(new WebhookHeaders(null), cuerpo)!.Code);
    }

    [Fact]
    public void El_checksum_QUE_VIENE_EN_EL_CUERPO_no_sirve_de_prueba()
    {
        // El cuerpo entero lo escribe quien llama, así que cotejarlo consigo mismo no prueba
        // nada. Lo que prueba algo es la cabecera, que solo se puede calcular con el secreto.
        // El fixture EXIGE la regla: el cuerpo y la cabecera traen EL MISMO valor inventado, así
        // que un verificador que cotejara el cuerpo consigo mismo pasaría en verde. Con valores
        // distintos, cualquiera de las dos comparaciones rechazaría y este test no probaría nada.
        const string Inventado = "a3f1c0";
        var ts = Ahora.ToUnixTimeSeconds();
        var cuerpo =
            $$"""
            {
              "data": { "transaction": { "id": "tx-1", "status": "APPROVED", "reference": "syn-1", "amount_in_cents": 1 } },
              "timestamp": {{ts}},
              "signature": { "properties": ["transaction.id"], "checksum": "{{Inventado}}" }
            }
            """;

        var malo = Verificador().Verify(new WebhookHeaders(Inventado), cuerpo);

        Assert.Equal("payments.webhook_signature_invalid", malo!.Code);
    }

    [Fact]
    public void Un_cuerpo_que_no_es_JSON_se_rechaza_sin_reventar()
        => Assert.Equal("payments.webhook_unreadable",
            Verificador().Verify(new WebhookHeaders("loquesea"), "no soy json")!.Code);

    [Fact]
    public void La_lista_de_propiedades_firmadas_se_LEE_del_evento()
    {
        // Wompi advierte que varía por tipo de evento. Un verificador que asuma las tres de hoy
        // empieza a rechazar todo el día que Wompi añada un tipo, y el síntoma —«la firma no
        // corresponde»— apunta al secreto y no a la lista.
        var ts = Ahora.ToUnixTimeSeconds();
        var cuerpo =
            $$"""
            {
              "data": { "transaction": { "id": "tx-9", "status": "APPROVED", "reference": "syn-1", "amount_in_cents": 500 } },
              "timestamp": {{ts}},
              "signature": { "properties": ["transaction.reference", "transaction.amount_in_cents"], "checksum": "x" }
            }
            """;
        var firma = WompiSignature.EventChecksum(new[] { "syn-1", "500" }, ts, Secreto);

        Assert.Null(Verificador().Verify(new WebhookHeaders(firma), cuerpo));
    }

    // ── Lo que el evento significa ──────────────────────────────────────────

    [Theory]
    [InlineData("APPROVED", Pagos.PaymentStatus.Captured)]
    [InlineData("DECLINED", Pagos.PaymentStatus.Failed)]
    [InlineData("ERROR", Pagos.PaymentStatus.Failed)]
    [InlineData("VOIDED", Pagos.PaymentStatus.Voided)]
    public void Cada_estado_de_la_pasarela_se_traduce_al_nuestro(string wompi, Pagos.PaymentStatus esperado)
    {
        var (cuerpo, _) = Evento(estado: wompi);

        var r = ProviderEventReader.Read(cuerpo);

        Assert.Equal(esperado, r.Value.Status);
        Assert.Equal("syn-1", r.Value.Reference);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("ALGO_QUE_NO_EXISTIA_AYER")]
    public void Un_evento_que_no_resuelve_nada_se_lee_SIN_error(string wompi)
    {
        // No puede ser un rechazo: el proveedor reintentaría durante días algo que decidimos
        // ignorar a propósito. Y dar por perdida una compra viva porque llegó un estado
        // desconocido es el modo de fallo que no se ve — el pedido simplemente no sale.
        var (cuerpo, _) = Evento(estado: wompi);

        var r = ProviderEventReader.Read(cuerpo);

        Assert.True(r.IsOk);
        Assert.Null(r.Value.Status);
    }

    // ── Lo que se anota ─────────────────────────────────────────────────────

    [Fact]
    public async Task Un_APPROVED_captura_el_cobro_que_lleva_esa_referencia()
    {
        var (svc, _, _) = Autorizado();

        var r = await svc.RecordProviderEventAsync("syn-1", Pagos.PaymentStatus.Captured, 11900000);

        Assert.True(r.IsOk);
        Assert.Equal(Pagos.PaymentStatus.Captured, r.Value.Status);
        Assert.Equal(Ahora, r.Value.CapturedAtUtc);
    }

    [Fact]
    public async Task El_MISMO_evento_reentregado_deja_el_cobro_donde_estaba()
    {
        // No hay llave de idempotencia que exigirle a un proveedor: la hace el estado. Un webhook
        // se reentrega hasta ver un 2xx, así que esto pasa siempre, no es un caso raro.
        var (svc, store, _) = Autorizado();

        var primero = await svc.RecordProviderEventAsync("syn-1", Pagos.PaymentStatus.Captured, 11900000);
        var segundo = await svc.RecordProviderEventAsync("syn-1", Pagos.PaymentStatus.Captured, 11900000);

        Assert.True(segundo.IsOk);
        Assert.Equal(primero.Value.CapturedAtUtc, segundo.Value.CapturedAtUtc);
        Assert.Equal(Pagos.PaymentStatus.Captured, store.Find("p-1")!.Status);
    }

    [Fact]
    public async Task Un_evento_TARDIO_no_retrocede_un_cobro_ya_capturado()
    {
        // Nada garantiza que el PENDING se entregue antes que el desenlace. Un motor que aplique
        // siempre lo último que le cuenten deja un cobro capturado en Failed — y eso no falla: se
        // guarda, y lo descubre quien reclame su pedido.
        var (svc, store, _) = Autorizado(Pagos.PaymentStatus.Captured);

        var r = await svc.RecordProviderEventAsync("syn-1", Pagos.PaymentStatus.Failed, 11900000);

        Assert.True(r.IsOk);
        Assert.Equal(Pagos.PaymentStatus.Captured, store.Find("p-1")!.Status);
    }

    [Fact]
    public async Task Un_APPROVED_por_OTRO_monto_NO_captura()
    {
        // La firma de integridad cubre el monto TAL COMO SE ENVIÓ, así que un error de centavos
        // produce una transacción impecable por la cifra equivocada. Dejarla pasar sería despachar
        // un pedido cobrando cien veces menos, y se descubriría cuadrando la caja.
        var (svc, store, _) = Autorizado();

        var r = await svc.RecordProviderEventAsync("syn-1", Pagos.PaymentStatus.Captured, 119000);

        Assert.Equal("payments.provider_amount_mismatch", r.Rejection!.Code);
        Assert.False(r.Rejection.IsTransient);
        Assert.Equal(Pagos.PaymentStatus.Authorized, store.Find("p-1")!.Status);
    }

    [Fact]
    public async Task Una_referencia_que_no_es_de_este_despliegue_NO_es_un_error_del_proveedor()
    {
        // Pasa con transacciones de otra cuenta o de un despliegue anterior. Contestarle un error
        // lo dejaría reintentando durante días.
        var (svc, _, _) = Autorizado();

        var r = await svc.RecordProviderEventAsync("syn-DE-OTRO", Pagos.PaymentStatus.Captured, 11900000);

        Assert.Equal("payments.unknown_provider_reference", r.Rejection!.Code);
    }

    [Fact]
    public async Task Un_evento_sin_referencia_no_se_anota_a_ciegas()
    {
        var (svc, _, _) = Autorizado();

        Assert.Equal("payments.webhook_unreadable",
            (await svc.RecordProviderEventAsync(null, Pagos.PaymentStatus.Captured, 1)).Rejection!.Code);
    }
}
