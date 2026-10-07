using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Clients;

/// <summary>
/// Los puertos de los pasos comunes, cumplidos con los clientes que este orquestador ya tenía.
/// </summary>
/// <remarks>
/// <para><b>Una línea por método, y <see cref="EventosCapabilities"/> no se toca.</b> El avance de
/// la compra pasa por acá; el DESHACER sigue yendo directo a <see cref="EventosCapabilities"/> desde
/// <c>EventosCompensationExecutor</c>, porque los tests de la compra lo construyen así y porque
/// mover las dos mitades a la vez es cambiar lo que se está midiendo. Son dos costuras hacia la
/// misma capacidad hasta que los demás orquestadores se porten (ADR 0140, F2+).</para>
///
/// <para><b>Mismas llamadas, mismo orden, mismos cuerpos</b>: cada método es exactamente lo que
/// <c>TicketingFlow</c> llamaba, y el dinero se arma con <see cref="Money.Of"/> igual que allá.</para>
/// </remarks>
public sealed class EventosPorts : IPricingPort, IInventoryPort, IPaymentsPort
{
    private readonly EventosCapabilities _caps;

    public EventosPorts(EventosCapabilities caps) => _caps = caps;

    public async Task<Result<QuoteView>> CotizarAsync(IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, CancellationToken ct)
        => (await _caps.QuoteAsync(lineas, ct)).Map(q => new QuoteView(
            Money.Of(q.Subtotal.Amount, q.Subtotal.Currency), Money.Of(q.Total.Amount, q.Total.Currency)));

    public async Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct)
        => (await _caps.FindAforoAsync(sujeto, ct)).Map(i => i.Id);

    public async Task<Result<string>> ApartarAsync(
        string itemId, int cantidad, Ref paraQue, IdempotencyKey llave, CancellationToken ct)
        => (await _caps.HoldAforoAsync(itemId, cantidad, paraQue, llave, ct)).Map(h => h.Id);

    public async Task<Result<string>> ConsumirAsync(string holdId, CancellationToken ct)
        => (await _caps.ConsumeAforoAsync(holdId, ct)).Map(h => h.Id);

    public async Task<Result<string>> AutorizarAsync(
        Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct)
        => (await _caps.AuthorizeAsync(paraQue, pagador, monto, llave, ct)).Map(p => p.Id);

    public async Task<Result<string>> CapturarAsync(string cobroId, IdempotencyKey llave, CancellationToken ct)
        => (await _caps.CaptureAsync(cobroId, llave, ct)).Map(p => p.Id);
}
