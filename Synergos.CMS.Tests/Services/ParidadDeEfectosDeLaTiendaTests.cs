using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La paridad de efectos de la compra de la tienda (004-tienda-compra, FR-021, T044): cada efecto que produce
/// el motor del CMS al confirmar una compra tiene su lugar en el camino de la puerta —un paso de
/// <c>tienda.compra.json</c> o el registro del comprador— o queda declarado como diferido o como que no aplica,
/// con su razón.
/// </summary>
/// <remarks>
/// <para><b>La tabla no se cree: se mide contra los dos caminos.</b> El motor en proceso se corre de verdad
/// (checkout y confirmación) con testigos en el seguimiento, los avisos y el panel de ventas, y lo que hizo es
/// la lista de efectos que la puerta tiene que cubrir. El registro de la puerta se corre igual (anotar y
/// confirmar), y los pasos se leen de la fase «cerrar» del flujo declarado.</para>
///
/// <para><b>Rojo en los dos sentidos.</b> Un efecto que el motor produce y la tabla no nombra se perdió sin
/// declararse; una fila que dice «registro» o «paso» y el camino no lo hace es una tabla que miente; y una fila
/// diferida cuyo motivo ya no es cierto —el aviso ya lleva las líneas, el seguimiento ya avanza con el envío— es
/// una declaración vieja que hay que mover a su lugar.</para>
/// </remarks>
public sealed class ParidadDeEfectosDeLaTiendaTests
{
    private const string CobroCapturado = "cobro capturado";
    private const string ExistenciasConsumidas = "existencias consumidas";
    private const string OrdenPagada = "orden pagada";
    private const string SeguimientoPagado = "seguimiento «pagado»";
    private const string AvisoAlComprador = "aviso al comprador";
    private const string LineasEnElCorreo = "líneas en el correo";
    private const string PanelDeVentas = "panel de ventas";
    private const string HistorialDelComprador = "historial del comprador";
    private const string SeguimientoDelEnvio = "seguimiento del envío";
    private const string CanastaEnApiCart = "canasta en Api.Cart";

    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private enum Lugar { Paso, Registro, Diferido, NoAplica }

    /// <param name="Donde">El tipo del paso, la pieza del registro, o la razón de lo diferido y lo que no aplica.</param>
    private sealed record Fila(string Efecto, Lugar Lugar, string Donde);

    /// <summary>El efecto → dónde vive en el camino de la puerta, o por qué no está.</summary>
    private static readonly Fila[] Tabla =
    [
        new(CobroCapturado, Lugar.Paso, "payments.capturar"),
        new(ExistenciasConsumidas, Lugar.Paso, "inventory.consumir"),
        new(OrdenPagada, Lugar.Registro, "LibroDeComprasDeLaPuerta.ConfirmarAsync: la orden de la puerta pasa a pagada una vez"),
        new(SeguimientoPagado, Lugar.Registro, "LibroDeComprasDeLaPuerta.ConfirmarAsync: el seguimiento avanza a «pagado»"),
        new(AvisoAlComprador, Lugar.Paso, "notifications.avisar"),
        new(LineasEnElCorreo, Lugar.Diferido,
            "N-10: el paso genérico del aviso manda valores fijos (nombre, número, total, enlace, sitio); las líneas "
            + "se ven en el enlace de la compra"),
        new(PanelDeVentas, Lugar.Registro, "LibroDeComprasDeLaPuerta.ConfirmarAsync: la venta se registra una vez"),
        new(HistorialDelComprador, Lugar.Registro, "OrdenesConLasDeLaPuerta.GetOrdersByMemberAsync: «Mis compras» lista la del miembro"),
        new(SeguimientoDelEnvio, Lugar.Diferido,
            "el envío existe en Api.Fulfillment (paso fulfillment.despachar), pero nadie lee hoy su estado hacia el "
            + "seguimiento: se queda en «pagado», como con el motor en proceso, que no despacha"),
        new(CanastaEnApiCart, Lugar.NoAplica,
            "R-1: la compra por la puerta entra con sus líneas y no abre canasta; las líneas quedan en el pedido de "
            + "Api.Orders y en el registro del comprador"),
    ];

    [Fact]
    public async Task Cada_efecto_del_motor_tiene_su_lugar_en_la_puerta_o_esta_declarado()
    {
        var delMotor = await LoQueHaceElMotor();

        // El control: el motor de referencia hace las ocho cosas que se miden. Si un testigo dejara de ver una, la
        // tabla se estaría midiendo contra menos de lo que el motor hace.
        Assert.Superset(new HashSet<string>(StringComparer.Ordinal)
        {
            CobroCapturado, ExistenciasConsumidas, OrdenPagada, SeguimientoPagado, AvisoAlComprador, LineasEnElCorreo, PanelDeVentas,
            HistorialDelComprador,
        }, delMotor);

        var sinLugar = delMotor.Where(e => Tabla.All(f => f.Efecto != e)).ToList();
        Assert.True(sinLugar.Count == 0,
            $"El motor del CMS produce {string.Join(", ", sinLugar)} y la paridad no dice dónde vive en la puerta ni por qué no está.");
        Assert.Equal(Tabla.Length, Tabla.Select(f => f.Efecto).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Lo_que_la_tabla_pone_en_el_registro_lo_hace_el_registro()
    {
        var (delRegistro, _) = await LoQueHaceElRegistro();

        var noLoHace = Tabla.Where(f => f.Lugar == Lugar.Registro && !delRegistro.Contains(f.Efecto))
            .Select(f => $"«{f.Efecto}» ({f.Donde})").ToList();
        Assert.True(noLoHace.Count == 0, $"La paridad dice que el registro de la puerta hace {string.Join("; ", noLoHace)} y no lo hace.");
    }

    [Fact]
    public void Lo_que_la_tabla_pone_en_un_paso_lo_declara_la_fase_que_cierra()
    {
        var cerrar = PasosDe("cerrar").Select(p => p.Tipo).ToHashSet(StringComparer.Ordinal);

        var sinPaso = Tabla.Where(f => f.Lugar == Lugar.Paso && !cerrar.Contains(f.Donde))
            .Select(f => $"«{f.Efecto}» ({f.Donde})").ToList();
        Assert.True(sinPaso.Count == 0, $"La paridad pone en un paso {string.Join("; ", sinPaso)} y «cerrar» de tienda.compra no lo declara.");
    }

    [Fact]
    public async Task Lo_diferido_y_lo_que_no_aplica_dicen_por_que_y_siguen_sin_hacerse()
    {
        foreach (var fila in Tabla.Where(f => f.Lugar is Lugar.Diferido or Lugar.NoAplica))
        {
            Assert.False(string.IsNullOrWhiteSpace(fila.Donde), $"«{fila.Efecto}» se declara sin su razón.");
        }

        // Las líneas: el aviso del flujo no las lee. Si un día las lee, la fila pasa a «paso».
        var avisar = Assert.Single(PasosDe("cerrar"), p => p.Tipo == "notifications.avisar");
        Assert.DoesNotContain("lineasDelPedido", avisar.Lee);

        // El envío: se despacha, pero el seguimiento del registro sólo avanza a «pagado».
        Assert.Contains(PasosDe("cerrar"), p => p.Tipo == "fulfillment.despachar");
        var (_, etapas) = await LoQueHaceElRegistro();
        Assert.Equal(new[] { StubOrderTrackingService.StagePaid }, etapas);

        // La canasta: ninguna fase del flujo abre una.
        Assert.DoesNotContain(PasosDe("abrir").Concat(PasosDe("cerrar")), p => p.Tipo.StartsWith("cart.", StringComparison.Ordinal));
    }

    // ── Los dos caminos, medidos ─────────────────────────────────────────────────────────────

    private sealed record Testigos(IOrderTrackingService Seguimiento, ITransactionalNotifier Avisos, ICheckoutRecorder Ventas)
    {
        public static Testigos Nuevos() => new(
            Substitute.For<IOrderTrackingService>(), Substitute.For<ITransactionalNotifier>(), Substitute.For<ICheckoutRecorder>());

        public IReadOnlyList<string> Etapas(string orden) => Seguimiento.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IOrderTrackingService.AdvanceAsync) && Equals(c.GetArguments()[0], orden))
            .Select(c => (string)c.GetArguments()[1]!)
            .ToList();

        public IReadOnlyList<NotificationEvent> Avisados() => Avisos.ReceivedCalls()
            .Select(c => c.GetArguments()[0]).OfType<NotificationEvent>().ToList();

        public bool Vendio(string orden) => Ventas.ReceivedCalls()
            .Select(c => c.GetArguments()[0]).OfType<CheckoutCompleted>().Any(v => v.OrderId == orden);
    }

    /// <summary>Lo que hace el motor en proceso al comprar y confirmar: la referencia de la paridad.</summary>
    private static async Task<HashSet<string>> LoQueHaceElMotor()
    {
        var almacen = new InMemoryJsonEntityStore();
        using var reservas = new StubReservationService();
        using var pagos = new StubPaymentProvider();
        var testigos = Testigos.Nuevos();
        var motor = new StubShopOrderService(new StubProductCatalogProvider(), reservas, pagos, testigos.Seguimiento, almacen, null,
            testigos.Avisos, testigos.Ventas);

        var compra = await motor.CheckoutAsync(
            [new ShopCartItem("tec-laptop-pro-14", "tec-laptop-pro-14-16-512", 1)], new ShopCustomer("Ana", "ana@ejemplo.co", Ana));
        await motor.ConfirmAsync(compra.OrderRef);

        var efectos = Comunes(testigos, compra.OrderRef, await motor.GetOrderAsync(compra.OrderRef), await motor.GetOrdersByMemberAsync(Ana));
        var orden = (await motor.GetOrderAsync(compra.OrderRef))!;
        if ((await pagos.GetStatusAsync(orden.PaymentSessionId!)).Status == PaymentStatus.Captured) efectos.Add(CobroCapturado);
        var reservadas = Reservas(await almacen.ReadAsync(LibroDeComprasDeLaPuerta.ResourceType, compra.OrderRef));
        if (reservadas.Count > 0 && (await Task.WhenAll(reservadas.Select(r => reservas.GetAsync(r)))).All(r => r?.Status == ReservationStatus.Confirmed))
        {
            efectos.Add(ExistenciasConsumidas);
        }
        return efectos;
    }

    /// <summary>Lo que hace el registro de la puerta al anotar y confirmar una compra que cobró el orquestador.</summary>
    private static async Task<(HashSet<string> Efectos, IReadOnlyList<string> Etapas)> LoQueHaceElRegistro()
    {
        const string id = "pta-paridad-1";
        var testigos = Testigos.Nuevos();
        var libro = new LibroDeComprasDeLaPuerta(new InMemoryJsonEntityStore(), testigos.Seguimiento, testigos.Ventas);
        var registro = new RegistroDeComprasDeLaTienda(
            Substitute.For<IHttpClientFactory>(), libro, Substitute.For<IOptionsMonitor<PuertaSettings>>(), new StubProductCatalogProvider(),
            NullLogger<RegistroDeComprasDeLaTienda>.Instance, hayDestino: false);
        var ordenes = new OrdenesConLasDeLaPuerta(
            new StubShopOrderService(new StubProductCatalogProvider(), new StubReservationService(), new StubPaymentProvider()),
            libro, registro, NullLogger<OrdenesConLasDeLaPuerta>.Instance);

        var compra = new CompraDeLaPuerta(id, "PARIDAD1", Ana, "Ana", "ana@ejemplo.co",
            [new LineaDeLaCompra("p-1", null, "Taza", 2, 20_000m)], 40_000m, "COP");
        await libro.AnotarPendienteAsync(compra);
        await libro.ConfirmarAsync(compra);

        return (Comunes(testigos, id, await ordenes.GetOrderAsync(id), await ordenes.GetOrdersByMemberAsync(Ana)), testigos.Etapas(id));
    }

    /// <summary>Los efectos que los dos caminos pueden producir en el CMS, leídos donde quedaron.</summary>
    private static HashSet<string> Comunes(Testigos testigos, string id, ShopOrder? orden, IReadOnlyList<ShopOrder> delMiembro)
    {
        var efectos = new HashSet<string>(StringComparer.Ordinal);
        if (orden is { Status: OrderStatus.Paid }) efectos.Add(OrdenPagada);
        if (testigos.Etapas(id).Contains(StubOrderTrackingService.StagePaid)) efectos.Add(SeguimientoPagado);
        var aviso = testigos.Avisados().FirstOrDefault(a => a.Type == NotificationTypes.ShopOrderPaid && a.SubjectId == id);
        if (aviso is not null) efectos.Add(AvisoAlComprador);
        if (aviso?.Lines is { Count: > 0 }) efectos.Add(LineasEnElCorreo);
        if (testigos.Vendio(id)) efectos.Add(PanelDeVentas);
        if (delMiembro.Any(o => o.OrderRef == id)) efectos.Add(HistorialDelComprador);
        return efectos;
    }

    /// <summary>Las reservas de las líneas de una orden guardada por el motor en proceso.</summary>
    private static List<string> Reservas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("Lines").EnumerateArray()
            .Select(l => l.GetProperty("ReservationId").GetString())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r!)
            .ToList();
    }

    // ── El flujo declarado ───────────────────────────────────────────────────────────────────

    private sealed record Paso(string Id, string Tipo, IReadOnlyList<string> Lee);

    /// <summary>Los pasos de una fase de <c>tienda.compra.json</c>, con los de dentro de cada <c>para_cada</c>.</summary>
    private static List<Paso> PasosDe(string fase)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "backend", "orquestadores", "Synergos.Bff.Tienda", "flujos", "tienda.compra.json")));
        var pasos = doc.RootElement.GetProperty("pasos");
        var ids = new List<string>();
        foreach (var e in doc.RootElement.GetProperty("fases").GetProperty(fase).EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.String) ids.Add(e.GetString()!);
            else ids.AddRange(e.GetProperty("pasos").EnumerateArray().Select(p => p.GetString()!));
        }
        return ids.Select(id =>
        {
            var p = pasos.GetProperty(id);
            var lee = p.TryGetProperty("lee", out var l) ? l.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            return new Paso(id, p.GetProperty("tipo").GetString()!, lee);
        }).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (Synergos.CMS.sln).");
    }
}
