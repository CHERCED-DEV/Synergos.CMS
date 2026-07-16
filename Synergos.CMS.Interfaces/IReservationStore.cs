namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento opaco del estado de reservas (T3, doc 25). JSON por
/// <c>reservationId</c> — calca <see cref="IShopOrderStore"/> (T1): el
/// <see cref="IReservationService"/> serializa/deserializa encima. Necesario para
/// cerrar la brecha de restart END-TO-END: <c>ConfirmAsync</c> de una orden no solo
/// captura el pago, también confirma sus reservas de stock; si el hold vive solo en
/// memoria, tras un reinicio la confirmación lanza "Reserva no encontrada" ANTES de
/// marcar la orden pagada. Con un adapter FileSystem el hold sobrevive el reinicio.
/// Reusable por Booking/Eventos (el motor de reservas es transversal).
/// </summary>
public interface IReservationStore
{
    Task WriteAsync(string reservationId, string json, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string reservationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string reservationId, CancellationToken cancellationToken = default);
}
