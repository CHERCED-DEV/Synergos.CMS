using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="RoutingPaymentProvider"/> (ADR 0116): el router que compone
/// N proveedores detrás del MISMO contrato, para que los ocho consumidores no se
/// enteren de que existe.
/// </summary>
public sealed class RoutingPaymentProviderTests
{
    private static IPaymentProvider Fake(string key)
    {
        var p = Substitute.For<IPaymentProvider>();
        p.ProviderKey.Returns(key);
        p.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PaymentSession($"{key}_native", PaymentStatus.Authorized, null, key));
        p.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PaymentOutcome(ci.Arg<string>(), PaymentStatus.Authorized));
        p.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PaymentOutcome(ci.ArgAt<string>(0), PaymentStatus.Captured, 42m));
        p.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PaymentOutcome(ci.Arg<string>(), PaymentStatus.Cancelled));
        p.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new PaymentOutcome(ci.ArgAt<string>(0), PaymentStatus.Refunded));
        return p;
    }

    private static PaymentSessionRequest Req(
        string? vertical = null, string? country = null,
        string? currency = "COP", string? method = null)
        => new("ord_1", 1000m, currency ?? "COP",
            new[] { new PaymentLineItem("SKU", "x", 1000m, 1) },
            Vertical: vertical, CountryCode: country, PreferredMethod: method);

    // ── Decisión ─────────────────────────────────────────────────────────

    [Fact]
    public void Sin_reglas_todo_va_al_default()
    {
        var stub = Fake("stub");
        var sut = new RoutingPaymentProvider(new[] { stub, Fake("wompi") }, null, "stub");

        Assert.Same(stub, sut.Resolve(Req(method: "pse")));
    }

    [Fact]
    public void Una_regla_por_METODO_desvia_solo_ese_riel()
    {
        // El caso real de arranque: mandar PSE por Wompi y dejar el resto en el
        // stub mientras se prueba, sin recompilar.
        var stub = Fake("stub");
        var wompi = Fake("wompi");
        var sut = new RoutingPaymentProvider(
            new[] { stub, wompi },
            new[] { new PaymentRoutingRule("wompi", Method: "pse") },
            "stub");

        Assert.Same(wompi, sut.Resolve(Req(method: "pse")));
        Assert.Same(stub, sut.Resolve(Req(method: "card")));
        Assert.Same(stub, sut.Resolve(Req()));
    }

    [Fact]
    public void Gana_la_PRIMERA_regla_que_encaja()
    {
        // Orden explícito en vez de puntajes: mover una regla en la lista es un
        // cambio visible, no un efecto lateral.
        var a = Fake("a");
        var b = Fake("b");
        var sut = new RoutingPaymentProvider(
            new[] { a, b },
            new[]
            {
                new PaymentRoutingRule("a", Vertical: "shop"),
                new PaymentRoutingRule("b", Vertical: "shop", Method: "pse"),
            },
            "a");

        Assert.Same(a, sut.Resolve(Req(vertical: "shop", method: "pse")));
    }

    [Fact]
    public void Los_criterios_combinan_en_AND()
    {
        var stub = Fake("stub");
        var wompi = Fake("wompi");
        var sut = new RoutingPaymentProvider(
            new[] { stub, wompi },
            new[] { new PaymentRoutingRule("wompi", CountryCode: "CO", Currency: "COP") },
            "stub");

        Assert.Same(wompi, sut.Resolve(Req(country: "CO", currency: "COP")));
        Assert.Same(stub, sut.Resolve(Req(country: "MX", currency: "COP")));
        Assert.Same(stub, sut.Resolve(Req(country: "CO", currency: "USD")));
    }

    [Fact]
    public void Una_regla_hacia_un_proveedor_INEXISTENTE_se_descarta_al_construir()
    {
        // Un typo en appsettings no puede volverse un pago perdido a mitad de un
        // checkout: se descarta al arrancar, no al cobrar.
        var stub = Fake("stub");
        var sut = new RoutingPaymentProvider(
            new[] { stub },
            new[] { new PaymentRoutingRule("wompiii", Method: "pse") },
            "stub");

        Assert.Same(stub, sut.Resolve(Req(method: "pse")));
    }

    [Fact]
    public void Sin_proveedores_no_se_puede_construir()
    {
        Assert.Throws<ArgumentException>(() =>
            new RoutingPaymentProvider(Array.Empty<IPaymentProvider>()));
    }

    // ── El id calificado ─────────────────────────────────────────────────

    [Fact]
    public async Task La_sesion_vuelve_con_el_id_PREFIJADO_y_el_proveedor_real()
    {
        var sut = new RoutingPaymentProvider(
            new[] { Fake("stub"), Fake("wompi") },
            new[] { new PaymentRoutingRule("wompi", Method: "pse") },
            "stub");

        var session = await sut.CreateSessionAsync(Req(method: "pse"));

        Assert.Equal("wompi|wompi_native", session.SessionId);
        // El ProviderKey es el de quien cobró, NO el del router: sin esto un
        // operador no sabría a qué PSP reclamarle.
        Assert.Equal("wompi", session.ProviderKey);
    }

    [Fact]
    public async Task Capture_llega_al_proveedor_dueño_con_su_id_NATIVO()
    {
        var wompi = Fake("wompi");
        var sut = new RoutingPaymentProvider(new[] { Fake("stub"), wompi }, null, "stub");

        await sut.CaptureAsync("wompi|ch_123", 500m);

        await wompi.Received(1).CaptureAsync("ch_123", 500m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_llamante_recibe_de_vuelta_el_MISMO_id_que_pasó()
    {
        var sut = new RoutingPaymentProvider(new[] { Fake("stub"), Fake("wompi") }, null, "stub");

        var outcome = await sut.CaptureAsync("wompi|ch_123");

        Assert.Equal("wompi|ch_123", outcome.SessionId);
    }

    [Fact]
    public async Task Void_y_Refund_tambien_enrutan()
    {
        var wompi = Fake("wompi");
        var sut = new RoutingPaymentProvider(new[] { Fake("stub"), wompi }, null, "stub");

        await sut.VoidAsync("wompi|ch_9");
        await sut.RefundAsync("wompi|ch_9", 100m);

        await wompi.Received(1).VoidAsync("ch_9", Arg.Any<CancellationToken>());
        await wompi.Received(1).RefundAsync("ch_9", 100m, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_id_SIN_prefijo_cae_al_default_y_no_revienta()
    {
        // Sesiones creadas antes de este cambio. Una sesión viva no se puede
        // romper porque cambiemos de plomería.
        var stub = Fake("stub");
        var sut = new RoutingPaymentProvider(new[] { stub, Fake("wompi") }, null, "stub");

        var outcome = await sut.CaptureAsync("stub_abc123");

        await stub.Received(1).CaptureAsync("stub_abc123", null, Arg.Any<CancellationToken>());
        Assert.Equal("stub_abc123", outcome.SessionId);
    }

    [Fact]
    public async Task Un_prefijo_DESCONOCIDO_cae_al_default_entero()
    {
        // No se parte el id: se pasa completo, y el default responderá "no
        // existe". Preferimos un fallo claro del PSP a un enrutamiento fantasma.
        var stub = Fake("stub");
        var sut = new RoutingPaymentProvider(new[] { stub }, null, "stub");

        await sut.GetStatusAsync("otropsp|ch_1");

        await stub.Received(1).GetStatusAsync("otropsp|ch_1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void El_separador_es_barra_vertical_y_no_dos_puntos()
    // Los ids de PSP llevan dos puntos con más frecuencia de la que uno espera,
    // y un separador ambiguo convierte un enrutamiento en un bug silencioso.
        => Assert.Equal('|', RoutingPaymentProvider.Separator);

    [Fact]
    public async Task Con_UN_solo_proveedor_el_router_sigue_siendo_transparente()
    {
        var sut = new RoutingPaymentProvider(new[] { Fake("stub") });

        var session = await sut.CreateSessionAsync(Req());
        var outcome = await sut.CaptureAsync(session.SessionId);

        Assert.Equal("stub|stub_native", session.SessionId);
        Assert.Equal(PaymentStatus.Captured, outcome.Status);
    }
}
