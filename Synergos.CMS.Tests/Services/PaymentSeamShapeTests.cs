using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la forma nueva del seam de pagos (ADR 0116): las tres modalidades de
/// <see cref="PaymentAction"/>, la captura parcial y <c>VoidAsync</c>.
/// </summary>
/// <remarks>
/// <para>El par <c>RedirectUrl</c>/<c>ClientSecret</c> anterior solo sabía
/// expresar el redirect. En Colombia hacen falta tres formas incompatibles:
/// redirect (PSE), reto HTML embebido con polling (3DS2) y espera de aprobación
/// en otro dispositivo sin URL ni HTML (Nequi).</para>
///
/// <para><c>VoidAsync</c> no es cosmético: es la pieza que le faltaba a Salud
/// para soltar el cupo cuando el copago no capturó, y a Booking para cancelar
/// sin cobrar. Sin ella cada vertical improvisaba, y ninguno lo hacía bien.</para>
/// </remarks>
public sealed class PaymentSeamShapeTests
{
    private static IPaymentProvider Make(PaymentsSettings? settings = null)
        => new StubPaymentProvider(new InMemoryJsonEntityStore(), settings);

    private static PaymentSessionRequest Request(
        decimal amount = 100_000m,
        string? method = null)
        => new(
            OrderReference: "ord_test",
            Amount: amount,
            Currency: "COP",
            Items: new[] { new PaymentLineItem("SKU-1", "Producto", amount, 1) },
            PreferredMethod: method);

    // ── Las tres formas de "requiere acción" ─────────────────────────────

    [Theory]
    [InlineData("pse")]
    [InlineData("bancolombia")]
    public async Task PSE_y_Bancolombia_piden_REDIRECT(string method)
    {
        // PSE saca al cliente del sitio y el resultado NO vuelve en el retorno:
        // llega después por webhook. Modelarlo de otra forma cuelga el checkout.
        var sut = Make(new PaymentsSettings { SimulateRequiresAction = true });

        var session = await sut.CreateSessionAsync(Request(method: method));

        Assert.Equal(PaymentStatus.RequiresAction, session.Status);
        Assert.NotNull(session.Action);
        Assert.Equal(PaymentActionKind.Redirect, session.Action!.Kind);
        Assert.NotNull(session.Action.RedirectUrl);
    }

    [Fact]
    public async Task Nequi_pide_APROBACION_en_otro_dispositivo_sin_URL_ni_HTML()
    {
        // El caso que el modelo viejo no podía expresar: no hay nada que pintar
        // ni a dónde ir. La UI solo puede explicar y esperar.
        var sut = Make(new PaymentsSettings { SimulateRequiresAction = true });

        var session = await sut.CreateSessionAsync(Request(method: "nequi"));

        var action = Assert.IsType<PaymentAction>(session.Action);
        Assert.Equal(PaymentActionKind.AwaitApproval, action.Kind);
        Assert.Null(action.RedirectUrl);
        Assert.Null(action.InlineHtml);
        Assert.False(string.IsNullOrWhiteSpace(action.UserHint));
        Assert.NotNull(action.PollInterval);
    }

    [Fact]
    public async Task Tarjeta_con_3DS_pide_un_reto_EMBEBIDO_con_polling()
    {
        var sut = Make(new PaymentsSettings { SimulateRequiresAction = true });

        var session = await sut.CreateSessionAsync(Request(method: "card"));

        var action = Assert.IsType<PaymentAction>(session.Action);
        Assert.Equal(PaymentActionKind.InlineChallenge, action.Kind);
        Assert.False(string.IsNullOrWhiteSpace(action.InlineHtml));
        Assert.NotNull(action.PollInterval);
    }

    [Fact]
    public async Task Sin_accion_pendiente_no_se_inventa_una()
    {
        var session = await Make().CreateSessionAsync(Request());

        Assert.Equal(PaymentStatus.Authorized, session.Status);
        Assert.Null(session.Action);
    }

    // ── Captura parcial ──────────────────────────────────────────────────

    [Fact]
    public async Task Captura_parcial_cobra_solo_lo_pedido()
    {
        // El caso de negocio: cobrar por noche una reserva, o solo lo que se
        // llegó a despachar. Antes la firma no lo permitía, mientras Refund sí
        // aceptaba parcial — una asimetría que no respondía a nada del dominio.
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request(amount: 300_000m));

        var outcome = await sut.CaptureAsync(session.SessionId, amount: 100_000m);

        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.Equal(100_000m, outcome.AmountCaptured);
    }

    [Fact]
    public async Task No_se_captura_por_ENCIMA_de_lo_autorizado()
    {
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request(amount: 100_000m));

        var outcome = await sut.CaptureAsync(session.SessionId, amount: 999_999m);

        Assert.Equal(100_000m, outcome.AmountCaptured);
    }

    [Fact]
    public async Task Capturar_sin_monto_sigue_cobrando_el_total()
    {
        // Retrocompatibilidad de comportamiento: los 5 call sites que ya
        // existían no pasan monto y deben seguir cobrando todo.
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request(amount: 250_000m));

        var outcome = await sut.CaptureAsync(session.SessionId);

        Assert.Equal(250_000m, outcome.AmountCaptured);
    }

    // ── Void ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Void_libera_una_autorizacion_sin_cobrar()
    {
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request());

        var outcome = await sut.VoidAsync(session.SessionId);

        Assert.Equal(PaymentStatus.Cancelled, outcome.Status);
        Assert.Equal(0m, outcome.AmountCaptured);
    }

    [Fact]
    public async Task Void_es_idempotente()
    {
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request());

        await sut.VoidAsync(session.SessionId);
        var second = await sut.VoidAsync(session.SessionId);

        Assert.Equal(PaymentStatus.Cancelled, second.Status);
    }

    [Fact]
    public async Task Void_NO_puede_deshacer_un_cobro_ya_hecho()
    {
        // Lo capturado se devuelve con Refund. Confundirlos dejaría el libro
        // contable sin forma de saber si hubo cobro.
        var sut = Make();
        var session = await sut.CreateSessionAsync(Request());
        await sut.CaptureAsync(session.SessionId);

        var outcome = await sut.VoidAsync(session.SessionId);

        Assert.Equal(PaymentStatus.Captured, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureReason));
    }

    [Fact]
    public async Task Void_de_una_sesion_inexistente_no_revienta()
    {
        var outcome = await Make().VoidAsync("no_existe");

        Assert.Equal(PaymentStatus.Failed, outcome.Status);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureReason));
    }

    // ── Disputas ─────────────────────────────────────────────────────────

    [Fact]
    public void Disputed_y_ChargedBack_son_estados_DISTINTOS_de_Refunded()
    {
        // Un contracargo no lo decide el comercio: se lo imponen, llega meses
        // después y puede revertirse. Fundirlo con Refunded hace que la
        // conciliación mienta sobre quién decidió devolver la plata.
        Assert.NotEqual(PaymentStatus.Refunded, PaymentStatus.Disputed);
        Assert.NotEqual(PaymentStatus.Refunded, PaymentStatus.ChargedBack);
        Assert.NotEqual(PaymentStatus.Disputed, PaymentStatus.ChargedBack);
    }
}
