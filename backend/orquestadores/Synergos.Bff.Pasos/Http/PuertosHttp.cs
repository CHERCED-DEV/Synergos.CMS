using System.Globalization;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Pasos.Http;

/// <summary>
/// Los puertos de los pasos comunes cumplidos por HTTP contra cada capacidad: uno para todos los
/// orquestadores, no un <c>*Ports</c> por cada uno (núcleo N1, P06 del informe 81).
/// </summary>
/// <remarks>
/// <para><b>Dos capas en una clase, y a propósito.</b> Los métodos públicos hablan HTTP y devuelven la
/// forma mínima que leen de la capacidad (<c>…Dto</c>): son los que el gate de compatibilidad
/// (<c>ContratoConsumidorPasosTests</c>) recorre uno a uno contra el contrato comiteado. Los puertos
/// van como implementación EXPLÍCITA y son una línea cada uno sobre el método público: el gate no los
/// ve, y no hace falta, porque no hablan con nadie.</para>
///
/// <para><b>Los nombres de cliente son los de siempre</b> (<c>pricing</c>, <c>inventory</c>,
/// <c>payments</c>, <c>booking</c>, <c>notifications</c>, y desde N2 <c>consent</c>, <c>cart</c>, <c>orders</c> y <c>fulfillment</c>): los registra <c>AddSagaMachinery</c> con su
/// URL, su llave y su timeout por capacidad. Lo compartido con Eventos manda los mismos cuerpos que
/// <c>EventosCapabilities</c>, que sigue siendo el de Eventos (supuesto P5).</para>
/// </remarks>
public sealed class PuertosHttp : CapabilityClients,
    IPricingPort, IOfertaPort, IInventoryPort, IPaymentsPort, INotificationsPort, IBookingPort,
    IConsentPort, ICartPort, IOrdersPort, IFulfillmentPort, IWorkflowPort,
    IDeshacerExistencias, IDeshacerCobros, IDeshacerAgenda, IDeshacerPedidos
{
    public const string Pricing = "pricing";
    public const string Inventory = "inventory";
    public const string Payments = "payments";
    public const string Booking = "booking";
    public const string Notifications = CompensationAlert.Capability;
    public const string Consent = "consent";
    public const string Cart = "cart";
    public const string Orders = "orders";
    public const string Fulfillment = "fulfillment";
    public const string Workflow = "workflow";

    /// <summary>Las capacidades con las que habla: las que registra el arranque.</summary>
    public static IReadOnlyList<string> Capacidades { get; } =
        [Pricing, Inventory, Payments, Booking, Notifications, Consent, Cart, Orders, Fulfillment, Workflow];

    public PuertosHttp(IHttpClientFactory clients) : base(clients) { }

    // ── Pricing ─────────────────────────────────────────────────────────────

    /// <summary>Cotiza las líneas; sin código, el cuerpo no lleva <c>promotionCode</c> (como Eventos).</summary>
    public Task<Result<CotizacionDto>> CotizarHttpAsync(
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var lines = lineas.Select(l => new { subjectKind = l.Sujeto.Kind, subjectId = l.Sujeto.Id, quantity = l.Cantidad });
        object cuerpo = string.IsNullOrWhiteSpace(promocion) ? new { lines } : new { lines, promotionCode = promocion };
        return Post<CotizacionDto>(Pricing, "v1/quotes", cuerpo, null, ct);
    }

    async Task<Result<QuoteView>> IPricingPort.CotizarAsync(
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct)
        => (await CotizarHttpAsync(lineas, promocion, ct)).Map(q => new QuoteView(
            Money.Of(q.Subtotal.Amount, q.Subtotal.Currency),
            q.Discount is { } descuento ? Money.Of(descuento.Amount, descuento.Currency) : Money.Zero(q.Total.Currency),
            Money.Of(q.Tax.Amount, q.Tax.Currency),
            Money.Of(q.Total.Amount, q.Total.Currency),
            q.Lines?.Select(l => (Ref.Create(l.SubjectKind, l.SubjectId), Money.Of(l.UnitPrice.Amount, l.UnitPrice.Currency))).ToList()));

    /// <summary>Fija un precio sin impuesto, vigente hasta <paramref name="hasta"/> (la oferta declarada, P12).</summary>
    public Task<Result<PrecioDto>> FijarPrecioDeLaOfertaAsync(
        Ref sujeto, Money precio, DateTimeOffset? hasta, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        return Post<PrecioDto>(Pricing, "v1/prices", new
        {
            subjectKind = sujeto.Kind,
            subjectId = sujeto.Id,
            amount = new { amount = precio.Amount, currency = precio.Currency },
            taxRateBasisPoints = 0,
            validTo = hasta,
        }, llave, ct);
    }

    async Task<Result<string>> IOfertaPort.FijarPrecioAsync(
        Ref sujeto, Money precio, DateTimeOffset? hasta, IdempotencyKey llave, CancellationToken ct)
        => (await FijarPrecioDeLaOfertaAsync(sujeto, precio, hasta, llave, ct)).Map(p => p.Id);

    // ── Inventory ───────────────────────────────────────────────────────────

    public Task<Result<ExistenciaDto>> HallarExistenciaAsync(Ref sujeto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        return Get<ExistenciaDto>(Inventory,
            $"v1/items?subjectKind={Uri.EscapeDataString(sujeto.Kind)}&subjectId={Uri.EscapeDataString(sujeto.Id)}", ct);
    }

    public Task<Result<ApartadoDto>> ApartarExistenciaAsync(
        string itemId, int cantidad, Ref paraQue, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paraQue);
        return Post<ApartadoDto>(Inventory, $"v1/items/{itemId}/holds",
            new { quantity = cantidad, forKind = paraQue.Kind, forId = paraQue.Id }, llave, ct);
    }

    public Task<Result<ApartadoDto>> ConsumirExistenciaAsync(string holdId, CancellationToken ct)
        => Post<ApartadoDto>(Inventory, $"v1/holds/{holdId}/consume", null, null, ct);

    public Task<Result<ApartadoDto>> SoltarExistenciaAsync(string holdId, CancellationToken ct)
        => Post<ApartadoDto>(Inventory, $"v1/holds/{holdId}/release", null, null, ct);

    public Task<Result<ExistenciaDto>> ReponerExistenciaAsync(string itemId, int cantidad, IdempotencyKey llave, CancellationToken ct)
        => Post<ExistenciaDto>(Inventory, $"v1/items/{itemId}/adjust", new { delta = cantidad }, llave, ct);

    async Task<Result<string>> IInventoryPort.HallarAsync(Ref sujeto, CancellationToken ct)
        => (await HallarExistenciaAsync(sujeto, ct)).Map(i => i.Id);

    async Task<Result<string>> IInventoryPort.ApartarAsync(
        string itemId, int cantidad, Ref paraQue, IdempotencyKey llave, CancellationToken ct)
        => (await ApartarExistenciaAsync(itemId, cantidad, paraQue, llave, ct)).Map(h => h.Id);

    async Task<Result<string>> IInventoryPort.ConsumirAsync(string holdId, CancellationToken ct)
        => (await ConsumirExistenciaAsync(holdId, ct)).Map(h => h.Id);

    async Task<Result<string>> IDeshacerExistencias.SoltarAsync(string apartado, CancellationToken ct)
        => (await SoltarExistenciaAsync(apartado, ct)).Map(h => h.Id);

    async Task<Result<string>> IDeshacerExistencias.ReponerAsync(string pozo, int cantidad, IdempotencyKey llave, CancellationToken ct)
        => (await ReponerExistenciaAsync(pozo, cantidad, llave, ct)).Map(i => i.Id);

    // ── Payments ────────────────────────────────────────────────────────────

    public Task<Result<CobroDto>> AutorizarCobroAsync(Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paraQue);
        ArgumentNullException.ThrowIfNull(pagador);
        return Post<CobroDto>(Payments, "v1/payments", new
        {
            forKind = paraQue.Kind,
            forId = paraQue.Id,
            payerKind = pagador.Kind,
            payerId = pagador.Id,
            amount = new { amount = monto.Amount, currency = monto.Currency },
        }, llave, ct);
    }

    public Task<Result<CobroDto>> CapturarCobroAsync(string cobroId, IdempotencyKey llave, CancellationToken ct)
        => Post<CobroDto>(Payments, $"v1/payments/{cobroId}/capture", null, llave, ct);

    public Task<Result<CobroDto>> AnularCobroAsync(string cobroId, CancellationToken ct)
        => Post<CobroDto>(Payments, $"v1/payments/{cobroId}/void", null, null, ct);

    public Task<Result<CobroDto>> LeerCobroAsync(string cobroId, CancellationToken ct)
        => Get<CobroDto>(Payments, $"v1/payments/{cobroId}", ct);

    public Task<Result<CobroDto>> DevolverCobroAsync(string cobroId, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct)
        => Post<CobroDto>(Payments, $"v1/payments/{cobroId}/refund", new
        {
            amount = new { amount = monto.Amount, currency = monto.Currency },
            reason = motivo,
        }, llave, ct);

    async Task<Result<string>> IPaymentsPort.AutorizarAsync(Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct)
        => (await AutorizarCobroAsync(paraQue, pagador, monto, llave, ct)).Map(p => p.Id);

    async Task<Result<string>> IPaymentsPort.CapturarAsync(string cobroId, IdempotencyKey llave, CancellationToken ct)
        => (await CapturarCobroAsync(cobroId, llave, ct)).Map(p => p.Id);

    async Task<Result<string>> IDeshacerCobros.AnularAsync(string cobro, CancellationToken ct)
        => (await AnularCobroAsync(cobro, ct)).Map(p => p.Id);

    async Task<Result<Money>> IDeshacerCobros.DevolvibleAsync(string cobro, CancellationToken ct)
        => (await LeerCobroAsync(cobro, ct)).Map(p => Money.Of(p.Refundable.Amount, p.Refundable.Currency));

    async Task<Result<string>> IDeshacerCobros.DevolverAsync(string cobro, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct)
        => (await DevolverCobroAsync(cobro, monto, motivo, llave, ct)).Map(p => p.Id);

    // ── Booking ─────────────────────────────────────────────────────────────

    public Task<Result<RecursoDto>> HallarRecursoAsync(Ref sujeto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        return Get<RecursoDto>(Booking,
            $"v1/resources/por-sujeto?subjectKind={Uri.EscapeDataString(sujeto.Kind)}&subjectId={Uri.EscapeDataString(sujeto.Id)}", ct);
    }

    public Task<Result<ApartadoDeAgendaDto>> ApartarVentanaAsync(
        string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paraQuien);
        return Post<ApartadoDeAgendaDto>(Booking, "v1/holds", new
        {
            resourceId = recurso,
            start = ventana.Start,
            end = ventana.End,
            heldForKind = paraQuien.Kind,
            heldForId = paraQuien.Id,
        }, llave, ct);
    }

    /// <summary>Las franjas del recurso en el rango (NB-2): una petición, no una por ventana.</summary>
    public Task<Result<FranjasDto>> FranjasDelRecursoAsync(string recurso, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
        => Get<FranjasDto>(Booking,
            $"v1/resources/{Uri.EscapeDataString(recurso)}/slots?from={Uri.EscapeDataString(desde.ToString("O", CultureInfo.InvariantCulture))}"
            + $"&to={Uri.EscapeDataString(hasta.ToString("O", CultureInfo.InvariantCulture))}", ct);

    public Task<Result<ReservaDto>> ConfirmarApartadoAsync(string apartado, IdempotencyKey llave, CancellationToken ct)
        => Post<ReservaDto>(Booking, $"v1/holds/{apartado}/confirm", null, llave, ct);

    public Task<Result<ApartadoDeAgendaDto>> LiberarApartadoAsync(string apartado, CancellationToken ct)
        => Post<ApartadoDeAgendaDto>(Booking, $"v1/holds/{apartado}/release", null, null, ct);

    public Task<Result<ReservaDto>> CancelarReservaAsync(string reserva, CancellationToken ct)
        => Post<ReservaDto>(Booking, $"v1/reservations/{reserva}/cancel", null, null, ct);

    /// <summary>Cancela con llave: repetir la misma cancelación devuelve lo que hizo la primera (núcleo T052).</summary>
    public Task<Result<ReservaDto>> CancelarReservaConLlaveAsync(string reserva, IdempotencyKey llave, CancellationToken ct)
        => Post<ReservaDto>(Booking, $"v1/reservations/{reserva}/cancel", null, llave, ct);

    async Task<Result<string>> IBookingPort.HallarAsync(Ref sujeto, CancellationToken ct)
        => (await HallarRecursoAsync(sujeto, ct)).Map(r => r.Id);

    async Task<Result<Apartado>> IBookingPort.ApartarAsync(
        string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
        => (await ApartarVentanaAsync(recurso, ventana, paraQuien, llave, ct)).Map(h => new Apartado(h.Id, h.ExpiresAt));

    async Task<Result<IReadOnlyList<FranjaLibre>>> IBookingPort.FranjasAsync(string recurso, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken ct)
        => (await FranjasDelRecursoAsync(recurso, desde, hasta, ct))
            .Map(f => (IReadOnlyList<FranjaLibre>)f.Items.Select(s => new FranjaLibre(s.Start, s.End, s.Free)).ToList());

    async Task<Result<string>> IBookingPort.ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct)
        => (await ConfirmarApartadoAsync(apartado, llave, ct)).Map(r => r.Id);

    async Task<Result<string>> IDeshacerAgenda.LiberarAsync(string apartado, CancellationToken ct)
        => (await LiberarApartadoAsync(apartado, ct)).Map(h => h.Id);

    async Task<Result<string>> IDeshacerAgenda.CancelarAsync(string reserva, CancellationToken ct)
        => (await CancelarReservaAsync(reserva, ct)).Map(r => r.Id);

    async Task<Result<string>> IDeshacerAgenda.CancelarAsync(string reserva, IdempotencyKey llave, CancellationToken ct)
        => (await CancelarReservaConLlaveAsync(reserva, llave, ct)).Map(r => r.Id);

    // ── Notifications ───────────────────────────────────────────────────────

    /// <summary>Pide un aviso con una plantilla por clave; el texto lo rellena la capacidad.</summary>
    public Task<Result<EntregaDto>> PedirAvisoAsync(
        Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
        IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destinatario);
        return Post<EntregaDto>(Notifications, "v1/deliveries", new
        {
            toKind = destinatario.Kind,
            toId = destinatario.Id,
            address = direccion,
            templateKey = plantilla,
            values = valores,
        }, llave, ct);
    }

    async Task<Result<string>> INotificationsPort.AvisarAsync(
        Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
        IdempotencyKey llave, CancellationToken ct)
        => (await PedirAvisoAsync(destinatario, direccion, plantilla, valores, llave, ct)).Map(d => d.Id);

    // ── Consent ─────────────────────────────────────────────────────────────

    /// <summary>
    /// El permiso vigente para un propósito. Por POST y con el sujeto en el cuerpo: sujeto y propósito juntos
    /// son un dato personal y no van en la URL.
    /// </summary>
    public Task<Result<PermisoDto>> VerificarPermisoAsync(Ref sujeto, string proposito, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        return Post<PermisoDto>(Consent, "v1/grants/check",
            new { subjectKind = sujeto.Kind, subjectId = sujeto.Id, purpose = proposito }, null, ct);
    }

    async Task<Result<string>> IConsentPort.VerificarAsync(Ref sujeto, string proposito, CancellationToken ct)
        => (await VerificarPermisoAsync(sujeto, proposito, ct)).Map(p => p.Id);

    // ── Cart ────────────────────────────────────────────────────────────────

    public Task<Result<CanastaDto>> LeerCanastaAsync(string canasta, CancellationToken ct)
        => Get<CanastaDto>(Cart, $"v1/carts/{canasta}", ct);

    public Task<Result<CanastaDto>> CerrarCanastaAsync(string canasta, CancellationToken ct)
        => Post<CanastaDto>(Cart, $"v1/carts/{canasta}/checkout", null, null, ct);

    async Task<Result<CanastaView>> ICartPort.LeerAsync(string canasta, CancellationToken ct)
        => (await LeerCanastaAsync(canasta, ct)).Map(c => new CanastaView(
            Ref.Create(c.OwnerKind, c.OwnerId),
            c.Lines.Select(l => (Ref.Create(l.SubjectKind, l.SubjectId), l.Quantity)).ToList(),
            c.CheckedOut, c.Open));

    async Task<Result<string>> ICartPort.CerrarAsync(string canasta, CancellationToken ct)
        => (await CerrarCanastaAsync(canasta, ct)).Map(c => c.Id);

    // ── Orders ──────────────────────────────────────────────────────────────

    public Task<Result<PedidoDto>> CrearPedidoAsync(
        Ref comprador, IReadOnlyList<LineaDePedido> lineas, Money total, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comprador);
        ArgumentNullException.ThrowIfNull(lineas);
        return Post<PedidoDto>(Orders, "v1/orders", new
        {
            buyerKind = comprador.Kind,
            buyerId = comprador.Id,
            lines = lineas.Select(l => new
            {
                subjectKind = l.Sujeto.Kind,
                subjectId = l.Sujeto.Id,
                quantity = l.Cantidad,
                unitPrice = new { amount = l.PrecioUnitario.Amount, currency = l.PrecioUnitario.Currency },
            }),
            total = new { amount = total.Amount, currency = total.Currency },
        }, llave, ct);
    }

    public Task<Result<PedidoDto>> CumplirPedidoAsync(string pedido, CancellationToken ct)
        => Post<PedidoDto>(Orders, $"v1/orders/{pedido}/fulfill", null, null, ct);

    public Task<Result<PedidoDto>> CancelarPedidoAsync(string pedido, CancellationToken ct)
        => Post<PedidoDto>(Orders, $"v1/orders/{pedido}/cancel", null, null, ct);

    async Task<Result<string>> IOrdersPort.CrearAsync(
        Ref comprador, IReadOnlyList<LineaDePedido> lineas, Money total, IdempotencyKey llave, CancellationToken ct)
        => (await CrearPedidoAsync(comprador, lineas, total, llave, ct)).Map(o => o.Id);

    async Task<Result<string>> IOrdersPort.CumplirAsync(string pedido, CancellationToken ct)
        => (await CumplirPedidoAsync(pedido, ct)).Map(o => o.Id);

    async Task<Result<string>> IDeshacerPedidos.CancelarAsync(string pedido, CancellationToken ct)
        => (await CancelarPedidoAsync(pedido, ct)).Map(o => o.Id);

    // ── Fulfillment ─────────────────────────────────────────────────────────

    public Task<Result<EnvioDto>> DespacharEnvioAsync(
        Ref paraQue, DireccionDeEnvio direccion, string transportadora, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paraQue);
        ArgumentNullException.ThrowIfNull(direccion);
        return Post<EnvioDto>(Fulfillment, "v1/shipments", new
        {
            forKind = paraQue.Kind,
            forId = paraQue.Id,
            to = new
            {
                line1 = direccion.Linea1,
                line2 = direccion.Linea2,
                city = direccion.Ciudad,
                region = direccion.Region,
                postalCode = direccion.CodigoPostal,
                country = direccion.Pais,
                contact = direccion.Contacto,
            },
            carrier = transportadora,
        }, llave, ct);
    }

    async Task<Result<string>> IFulfillmentPort.DespacharAsync(
        Ref paraQue, DireccionDeEnvio direccion, string transportadora, IdempotencyKey llave, CancellationToken ct)
        => (await DespacharEnvioAsync(paraQue, direccion, transportadora, llave, ct)).Map(s => s.Id);

    /// <summary>Lleva el envío a otro estado (NS-1 de 013): <c>TrackShipment</c>, sin llave.</summary>
    public Task<Result<EnvioSeguidoDto>> SeguirEnvioAsync(string envio, string estado, string? nota, CancellationToken ct)
        => Post<EnvioSeguidoDto>(Fulfillment, $"v1/shipments/{Uri.EscapeDataString(envio)}/track", new { status = estado, note = nota }, null, ct);

    async Task<Result<string>> IFulfillmentPort.SeguirAsync(string envio, string estado, string? nota, CancellationToken ct)
        => (await SeguirEnvioAsync(envio, estado, nota, ct)).Map(e => e.Status);

    /// <summary>El envío entero (NS-2 de 013): <c>GetShipment</c>.</summary>
    public Task<Result<EnvioCompletoDto>> LeerEnvioAsync(string envio, CancellationToken ct)
        => Get<EnvioCompletoDto>(Fulfillment, $"v1/shipments/{Uri.EscapeDataString(envio)}", ct);

    async Task<Result<EnvioLeido>> IFulfillmentPort.LeerAsync(string envio, CancellationToken ct)
        => (await LeerEnvioAsync(envio, ct)).Map(e => new EnvioLeido(
            e.Id,
            new DireccionDeEnvio(e.To.Line1 ?? string.Empty, e.To.Line2, e.To.City ?? string.Empty, e.To.Region, e.To.PostalCode,
                e.To.Country ?? string.Empty, e.To.Contact),
            e.Carrier, e.TrackingCode, e.Status,
            e.Events.Select(v => new EventoDeEnvio(v.Status, v.Note, v.AtUtc)).ToList()));

    // ── Workflow ────────────────────────────────────────────────────────────

    public Task<Result<InstanciaDto>> ArrancarInstanciaAsync(string proceso, Ref sujeto, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sujeto);
        return Post<InstanciaDto>(Workflow, "v1/instances",
            new { definitionKey = proceso, subjectKind = sujeto.Kind, subjectId = sujeto.Id }, llave, ct);
    }

    /// <summary>Dispara con los roles DECLARADOS en el cuerpo y sin token (009-P6 = A: la guarda es la puerta).</summary>
    public Task<Result<InstanciaDto>> DispararTransicionAsync(
        string instancia, string transicion, Ref actor, IReadOnlyList<string> roles, string? nota, IdempotencyKey llave, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Post<InstanciaDto>(Workflow, $"v1/instances/{Uri.EscapeDataString(instancia)}/fire",
            new { transition = transicion, actorKind = actor.Kind, actorId = actor.Id, actorRoles = roles, note = nota }, llave, ct);
    }

    public Task<Result<InstanciaDto>> LeerInstanciaAsync(string instancia, CancellationToken ct)
        => Get<InstanciaDto>(Workflow, $"v1/instances/{Uri.EscapeDataString(instancia)}", ct);

    async Task<Result<InstanciaDeProceso>> IWorkflowPort.ArrancarAsync(string proceso, Ref sujeto, IdempotencyKey llave, CancellationToken ct)
        => (await ArrancarInstanciaAsync(proceso, sujeto, llave, ct)).Map(Instancia);

    async Task<Result<InstanciaDeProceso>> IWorkflowPort.DispararAsync(
        string instancia, string transicion, Ref actor, IReadOnlyList<string> roles, string? nota, IdempotencyKey llave, CancellationToken ct)
        => (await DispararTransicionAsync(instancia, transicion, actor, roles, nota, llave, ct)).Map(Instancia);

    async Task<Result<InstanciaDeProceso>> IWorkflowPort.LeerAsync(string instancia, CancellationToken ct)
        => (await LeerInstanciaAsync(instancia, ct)).Map(Instancia);

    private static InstanciaDeProceso Instancia(InstanciaDto i)
        => new(i.Id, i.DefinitionKey, i.State,
            i.History.Select(h => new PasoDeLaHistoria(h.Transition, h.From, h.To, Ref.Create(h.ByKind, h.ById), h.Note, h.AtUtc)).ToList());
}

// ── Las formas mínimas que se leen de cada capacidad ────────────────────────
// Sólo los campos que se usan: un DTO que copie la respuesta entera obligaría a tocar esto cada vez
// que una capacidad agrega un campo que no le importa. El gate de compatibilidad cruza cada uno con
// el esquema de la respuesta comiteada.

public sealed record DineroDto(decimal Amount, string Currency);
public sealed record CotizacionDto(
    DineroDto Subtotal, DineroDto Tax, DineroDto Total, DineroDto? Discount = null, IReadOnlyList<LineaCotizadaDto>? Lines = null);
public sealed record LineaCotizadaDto(string SubjectKind, string SubjectId, DineroDto UnitPrice);
public sealed record PrecioDto(string Id, DateTimeOffset? ValidTo);
public sealed record ExistenciaDto(string Id);
public sealed record ApartadoDto(string Id);
public sealed record CobroDto(string Id, DineroDto Refundable);
public sealed record EntregaDto(string Id);
public sealed record RecursoDto(string Id);
public sealed record ApartadoDeAgendaDto(string Id, DateTimeOffset ExpiresAt);
public sealed record FranjaDto(DateTimeOffset Start, DateTimeOffset End, int Free);
public sealed record FranjasDto(IReadOnlyList<FranjaDto> Items);
public sealed record ReservaDto(string Id);
public sealed record PermisoDto(string Id);
public sealed record LineaDeCanastaDto(string SubjectKind, string SubjectId, int Quantity);
public sealed record CanastaDto(string Id, string OwnerKind, string OwnerId, IReadOnlyList<LineaDeCanastaDto> Lines, bool CheckedOut, bool Open);
public sealed record PedidoDto(string Id);
public sealed record PasoDeHistoriaDto(string Transition, string From, string To, string ByKind, string ById, string? Note, DateTimeOffset AtUtc);
public sealed record InstanciaDto(string Id, string DefinitionKey, string State, IReadOnlyList<PasoDeHistoriaDto> History);
public sealed record EnvioDto(string Id);
public sealed record EnvioSeguidoDto(string Id, string Status);
public sealed record DireccionDto(string? Line1, string? Line2, string? City, string? Region, string? PostalCode, string? Country, string? Contact);
public sealed record EventoDeEnvioDto(string Status, string? Note, DateTimeOffset AtUtc);
public sealed record EnvioCompletoDto(
    string Id, DireccionDto To, string Carrier, string? TrackingCode, string Status, IReadOnlyList<EventoDeEnvioDto> Events);
