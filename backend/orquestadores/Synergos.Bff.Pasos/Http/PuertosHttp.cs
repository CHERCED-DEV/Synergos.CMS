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
/// <c>payments</c>, <c>booking</c>, <c>notifications</c>): los registra <c>AddSagaMachinery</c> con su
/// URL, su llave y su timeout por capacidad. Lo compartido con Eventos manda los mismos cuerpos que
/// <c>EventosCapabilities</c>, que sigue siendo el de Eventos (supuesto P5).</para>
/// </remarks>
public sealed class PuertosHttp : CapabilityClients,
    IPricingPort, IInventoryPort, IPaymentsPort, INotificationsPort, IBookingPort,
    IDeshacerExistencias, IDeshacerCobros, IDeshacerAgenda
{
    public const string Pricing = "pricing";
    public const string Inventory = "inventory";
    public const string Payments = "payments";
    public const string Booking = "booking";
    public const string Notifications = CompensationAlert.Capability;

    /// <summary>Las capacidades con las que habla: las que registra el arranque.</summary>
    public static IReadOnlyList<string> Capacidades { get; } = [Pricing, Inventory, Payments, Booking, Notifications];

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
            Money.Of(q.Total.Amount, q.Total.Currency)));

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

    public Task<Result<ReservaDto>> ConfirmarApartadoAsync(string apartado, IdempotencyKey llave, CancellationToken ct)
        => Post<ReservaDto>(Booking, $"v1/holds/{apartado}/confirm", null, llave, ct);

    public Task<Result<ApartadoDeAgendaDto>> LiberarApartadoAsync(string apartado, CancellationToken ct)
        => Post<ApartadoDeAgendaDto>(Booking, $"v1/holds/{apartado}/release", null, null, ct);

    public Task<Result<ReservaDto>> CancelarReservaAsync(string reserva, CancellationToken ct)
        => Post<ReservaDto>(Booking, $"v1/reservations/{reserva}/cancel", null, null, ct);

    async Task<Result<string>> IBookingPort.HallarAsync(Ref sujeto, CancellationToken ct)
        => (await HallarRecursoAsync(sujeto, ct)).Map(r => r.Id);

    async Task<Result<string>> IBookingPort.ApartarAsync(
        string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
        => (await ApartarVentanaAsync(recurso, ventana, paraQuien, llave, ct)).Map(h => h.Id);

    async Task<Result<string>> IBookingPort.ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct)
        => (await ConfirmarApartadoAsync(apartado, llave, ct)).Map(r => r.Id);

    async Task<Result<string>> IDeshacerAgenda.LiberarAsync(string apartado, CancellationToken ct)
        => (await LiberarApartadoAsync(apartado, ct)).Map(h => h.Id);

    async Task<Result<string>> IDeshacerAgenda.CancelarAsync(string reserva, CancellationToken ct)
        => (await CancelarReservaAsync(reserva, ct)).Map(r => r.Id);

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
}

// ── Las formas mínimas que se leen de cada capacidad ────────────────────────
// Sólo los campos que se usan: un DTO que copie la respuesta entera obligaría a tocar esto cada vez
// que una capacidad agrega un campo que no le importa. El gate de compatibilidad cruza cada uno con
// el esquema de la respuesta comiteada.

public sealed record DineroDto(decimal Amount, string Currency);
public sealed record CotizacionDto(DineroDto Subtotal, DineroDto Tax, DineroDto Total, DineroDto? Discount = null);
public sealed record ExistenciaDto(string Id);
public sealed record ApartadoDto(string Id);
public sealed record CobroDto(string Id, DineroDto Refundable);
public sealed record EntregaDto(string Id);
public sealed record RecursoDto(string Id);
public sealed record ApartadoDeAgendaDto(string Id);
public sealed record ReservaDto(string Id);
