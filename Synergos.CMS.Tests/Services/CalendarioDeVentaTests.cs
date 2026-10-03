using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El checkout de Eventos no vende lo que la ficha dice que ya no se vende (#195): un evento que
/// ya empezó, ni una localidad fuera de su ventana de venta.
/// </summary>
/// <remarks>
/// <para><b>Lo que estaba roto.</b> <c>StubEventTicketingService.CheckoutAsync</c> miraba que la
/// localidad existiera, el aforo y el máximo por orden, y nada más. Medido el 2026-10-02 con el
/// motor en proceso: el «Festival Estéreo 2026» es del 15 de agosto, la ficha decía «El evento ya
/// comenzó», y <c>POST /api/eventos/checkout</c> con 2 × General contestaba 200 con la sesión de
/// pago abierta por 403.200. En el navegador, 2 × VIP daba «¡Compra confirmada!».</para>
///
/// <para><b>Por qué los tests no lo veían.</b> Compraban el festival con el reloj REAL: en julio
/// era un evento futuro con la venta abierta, y nadie fijaba el instante. Acá el reloj va fijo
/// siempre, y los casos se escriben sobre los límites, no lejos de ellos.</para>
///
/// <para><b>La zona horaria, que es la mitad fina.</b> El catálogo guarda instantes y el sitio es
/// de Colombia. «Hasta el 14 de agosto» vende todo el 14 en Bogotá: a las 23:59 de allá —que en UTC
/// ya es el 15— se sigue vendiendo, y a las 00:00 de allá se cierra. Medianoche UTC habría cerrado
/// a las siete de la noche del 14.</para>
/// </remarks>
public sealed class CalendarioDeVentaTests
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    /// <summary>El día que se midió el defecto, a mediodía en Bogotá.</summary>
    private static readonly DateTimeOffset DiaDelDefecto = new(2026, 10, 2, 12, 0, 0, Colombia);

    private static readonly ITicketSigner Firmante =
        new HmacTicketSigner(System.Text.Encoding.UTF8.GetBytes("llave-de-tests-195"));

    private static EventAttendeeInfo Asistente(string n) => new($"Asistente {n}", $"asistente{n}@synergos.co", $"100{n}");

    /// <summary>
    /// El motor de reservas de verdad, con un espía encima para saber si se apartó algo.
    /// </summary>
    /// <remarks>
    /// Delega en el real a propósito: con un sustituto vacío, el mutante —el motor sin la regla—
    /// revienta con un <c>NullReferenceException</c> al apartar y el test sale rojo por la razón
    /// equivocada. Con el real, el mutante VENDE, que es el síntoma que hay que ver.
    /// </remarks>
    private static IReservationService Reservas()
    {
        var real = new StubReservationService();
        var espia = Substitute.For<IReservationService>();
        espia.HoldItemAsync(default!, default).ReturnsForAnyArgs(ci =>
            real.HoldItemAsync(ci.ArgAt<TravelItemReservationRequest>(0), ci.ArgAt<CancellationToken>(1)));
        return espia;
    }

    /// <summary>La pasarela de verdad, con un espía encima para saber si se abrió una sesión.</summary>
    private static IPaymentProvider Pagos()
    {
        var real = new StubPaymentProvider();
        var espia = Substitute.For<IPaymentProvider>();
        espia.CreateSessionAsync(default!, default).ReturnsForAnyArgs(ci =>
            real.CreateSessionAsync(ci.ArgAt<PaymentSessionRequest>(0), ci.ArgAt<CancellationToken>(1)));
        return espia;
    }

    private static StubEventTicketingService Motor(
        DateTimeOffset ahora,
        IReservationService? reservas = null,
        IPaymentProvider? pagos = null,
        IEventCatalogProvider? catalogo = null)
        => new(catalogo ?? new StubEventCatalogProvider(), reservas ?? new StubReservationService(),
            pagos ?? new StubPaymentProvider(), null, null, null, () => ahora, signer: Firmante);

    // ── La regla ────────────────────────────────────────────────────────────

    [Fact] // el mismo límite que la ficha: inicio <= ahora es «ya comenzó».
    public void Un_evento_deja_de_venderse_en_el_instante_en_que_empieza()
    {
        var evento = new EventSummary("e", "e", "Noche", "Música", "Bogotá", "Teatro",
            DiaDelDefecto, string.Empty, 0m, "COP", "general");

        Assert.Null(CalendarioDeVenta.PorQueNoSeVende(evento, DiaDelDefecto.AddTicks(-1)));
        Assert.Equal("El evento 'Noche' ya comenzó: ya no se venden entradas.",
            CalendarioDeVenta.PorQueNoSeVende(evento, DiaDelDefecto));
        Assert.Equal("past", Synergos.CMS.Web.Services.Catalog.EventContentRules.BuildStatus(evento.StartUtc, DiaDelDefecto));
    }

    [Fact] // «hasta el 14 de agosto» = hasta que termina el 14 en Colombia, no en UTC.
    public async Task La_venta_cierra_al_terminar_el_dia_en_Colombia()
    {
        var general = (await new StubEventCatalogProvider().GetEventAsync("evt-festival-estereo"))!
            .Tiers.Single(t => t.Code == "GEN");
        Assert.Equal("Hasta el 14 de agosto de 2026", general.SaleWindow);

        // 23:59:59 del 14 en Bogotá es 04:59:59 del 15 en UTC: el día de la tarjeta sigue abierto.
        var ultimoInstante = new DateTimeOffset(2026, 8, 14, 23, 59, 59, Colombia).AddTicks(9_999_999);
        Assert.Equal(15, ultimoInstante.UtcDateTime.Day);
        Assert.Null(CalendarioDeVenta.PorQueNoSeVende(general, ultimoInstante));

        Assert.Equal("La venta de la localidad 'General' ya cerró (Hasta el 14 de agosto de 2026).",
            CalendarioDeVenta.PorQueNoSeVende(general, new DateTimeOffset(2026, 8, 15, 0, 0, 0, Colombia)));
    }

    [Fact] // la apertura es inclusiva: abre justo en su instante, ni un tick antes.
    public void La_venta_abre_en_su_instante()
    {
        var abre = new DateTimeOffset(2026, 9, 1, 0, 0, 0, Colombia);
        var localidad = new EventTier("PRE", "Preventa", 1m, "COP", 1, 1, 1,
            SaleWindow: "Desde el 1 de septiembre", SaleOpensUtc: abre);

        Assert.Equal("La venta de la localidad 'Preventa' todavía no abre (Desde el 1 de septiembre).",
            CalendarioDeVenta.PorQueNoSeVende(localidad, abre.AddTicks(-1)));
        Assert.Null(CalendarioDeVenta.PorQueNoSeVende(localidad, abre));
    }

    // ── El motor en proceso ─────────────────────────────────────────────────

    [Fact] // el defecto tal cual se midió: el festival del 15 de agosto, comprado en octubre.
    public async Task Un_evento_pasado_no_se_vende_ni_aparta_ni_abre_sesion_de_pago()
    {
        var reservas = Reservas();
        var pagos = Pagos();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Motor(DiaDelDefecto, reservas, pagos)
            .CheckoutAsync("evt-festival-estereo",
                new[] { new EventCheckoutItem("GEN", null, 2) }, new[] { Asistente("1"), Asistente("2") }));

        Assert.Equal("El evento 'Festival Estéreo 2026' ya comenzó: ya no se venden entradas.", ex.Message);
        await reservas.DidNotReceiveWithAnyArgs().HoldItemAsync(default!, default);
        await pagos.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, default);
    }

    [Fact] // la VIP cerró el 10; el evento es el 15. La regla es de la localidad, no del evento.
    public async Task Una_localidad_con_la_venta_cerrada_no_se_vende_aunque_el_evento_no_haya_empezado()
    {
        var reservas = Reservas();
        var pagos = Pagos();
        var el12DeAgosto = new DateTimeOffset(2026, 8, 12, 12, 0, 0, Colombia);

        // General todavía se vende; la VIP no. Se rechaza la compra entera y no se aparta NADA:
        // un rechazo a la mitad dejaría la General retenida por una compra que no va a ocurrir.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Motor(el12DeAgosto, reservas, pagos)
            .CheckoutAsync("evt-festival-estereo",
                new[] { new EventCheckoutItem("GEN", null, 1), new EventCheckoutItem("VIP", null, 1) },
                new[] { Asistente("1"), Asistente("2") }));

        Assert.Equal("La venta de la localidad 'VIP' ya cerró (Hasta el 10 de agosto de 2026).", ex.Message);
        await reservas.DidNotReceiveWithAnyArgs().HoldItemAsync(default!, default);
        await pagos.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, default);
    }

    [Fact]
    public async Task Una_localidad_que_todavia_no_abre_no_se_vende()
    {
        var catalogo = new StubEventCatalogProvider();
        var abre = new DateTimeOffset(2026, 9, 1, 0, 0, 0, Colombia);
        var publicado = await catalogo.PublishEventAsync(new EventDetail(
            new EventSummary(string.Empty, "lanzamiento", "Lanzamiento", "Música", "Bogotá", "Teatro",
                new DateTimeOffset(2026, 11, 20, 20, 0, 0, Colombia), string.Empty, 90_000m, "COP", "general"),
            string.Empty, string.Empty,
            new[]
            {
                new EventTier("PRE", "Preventa", 90_000m, "COP", 50, 50, 4,
                    SaleWindow: "Desde el 1 de septiembre", SaleOpensUtc: abre),
            },
            null));
        var pagos = Pagos();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Motor(abre.AddMinutes(-1), pagos: pagos, catalogo: catalogo)
            .CheckoutAsync(publicado.Summary.Id, new[] { new EventCheckoutItem("PRE", null, 1) }, new[] { Asistente("1") }));

        Assert.Equal("La venta de la localidad 'Preventa' todavía no abre (Desde el 1 de septiembre).", ex.Message);
        await pagos.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, default);
    }

    [Fact] // happy: el último minuto del día de la tarjeta, en Bogotá, sí se vende.
    public async Task Dentro_de_la_ventana_se_vende()
    {
        var ultimaHora = new DateTimeOffset(2026, 8, 14, 23, 0, 0, Colombia);

        var compra = await Motor(ultimaHora).CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 2) }, new[] { Asistente("1"), Asistente("2") });

        Assert.Equal(360_000m, compra.Amount);
        Assert.False(string.IsNullOrWhiteSpace(compra.PaymentSessionId));
    }

    // ── El borde ────────────────────────────────────────────────────────────

    /// <summary>
    /// El rechazo sale como el del aforo o el máximo por orden: <c>400 { error }</c> con el motivo.
    /// </summary>
    /// <remarks>
    /// No se inventa un formato: la pantalla de compra ya sabe pintar ese cuerpo, y un código nuevo
    /// la dejaría mostrando un error genérico sobre un motivo que el comprador puede entender.
    /// </remarks>
    [Fact]
    public async Task El_endpoint_rechaza_con_400_y_el_motivo_como_el_aforo()
    {
        var catalogo = new StubEventCatalogProvider();
        var pagos = Pagos();
        var controlador = new EventosController(
            catalogo,
            Motor(DiaDelDefecto, pagos: pagos, catalogo: catalogo),
            Substitute.For<IEventManagementService>(),
            Substitute.For<IPriceFormatter>(),
            Substitute.For<IMemberAccessGate>(),
            Substitute.For<IRealtimeNotifier>(),
            NullLogger<EventosController>.Instance);

        var resultado = await controlador.Checkout(new EventosController.CheckoutRequest(
            "evt-festival-estereo",
            new[] { new EventosController.CheckoutItemRequest("VIP", null, 2) },
            new[]
            {
                new EventosController.AttendeeRequest("Ana", "ana@synergos.co", null),
                new EventosController.AttendeeRequest("Beto", "beto@synergos.co", null),
            }), default);

        var rechazo = Assert.IsType<BadRequestObjectResult>(resultado);
        using var cuerpo = JsonDocument.Parse(JsonSerializer.Serialize(rechazo.Value));
        Assert.Equal(
            "El evento 'Festival Estéreo 2026' ya comenzó: ya no se venden entradas.",
            cuerpo.RootElement.GetProperty("error").GetString());
        await pagos.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, default);
    }

    // ── Lo que se muestra y lo que se cobra ─────────────────────────────────

    /// <summary>
    /// En el catálogo sembrado, el texto de cada ventana dice el MISMO día en que cierra la venta.
    /// </summary>
    /// <remarks>
    /// El texto lo lee el comprador y el instante lo aplica el checkout: si se separan, la ficha
    /// promete un día y se cobra con otro, que es la forma del defecto de #194 y de éste. Se lee el
    /// día escrito y se compara con el día —en Colombia— que termina en el cierre.
    /// </remarks>
    [Fact]
    public async Task El_texto_de_cada_ventana_dice_el_dia_en_que_cierra_la_venta()
    {
        var esCo = CultureInfo.GetCultureInfo("es-CO");
        var catalogo = new StubEventCatalogProvider();
        var localidades = new List<EventTier>();
        foreach (var resumen in await catalogo.SearchAsync(null))
        {
            localidades.AddRange((await catalogo.GetEventAsync(resumen.Id))!.Tiers);
        }

        Assert.NotEmpty(localidades);
        Assert.All(localidades, l =>
        {
            Assert.NotNull(l.SaleClosesUtc);
            var escrito = l.SaleWindow.Replace("Hasta el ", string.Empty, StringComparison.Ordinal)
                .Replace("Cerró el ", string.Empty, StringComparison.Ordinal);
            var dia = DateTime.ParseExact(escrito, "d 'de' MMMM 'de' yyyy", esCo, DateTimeStyles.None);

            var ultimoDiaDeVenta = l.SaleClosesUtc!.Value.ToOffset(Colombia).AddTicks(-1);
            Assert.Equal(DateOnly.FromDateTime(dia), DateOnly.FromDateTime(ultimoDiaDeVenta.DateTime));
            Assert.Equal(TimeSpan.Zero, l.SaleClosesUtc.Value.ToOffset(Colombia).TimeOfDay);
        });
    }
}
