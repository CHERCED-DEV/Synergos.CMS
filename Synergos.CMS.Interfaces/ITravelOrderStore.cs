namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento opaco del estado de órdenes de viaje del dominio Booking
/// (fan-out de T1, doc 25). JSON por <c>orderRef</c> — calca <see cref="IShopOrderStore"/>:
/// el <see cref="ITravelCartService"/> serializa/deserializa encima. Con un adapter
/// FileSystem, un carrito de viaje confirmado SOBREVIVE un reinicio (las reservas y la
/// sesión de pago ya son durables por T3; faltaba durabilizar el estado del carrito).
/// Cifrado NO aplica (PII de compra, no PHI).
/// </summary>
public interface ITravelOrderStore
{
    Task WriteAsync(string orderRef, string json, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string orderRef, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string orderRef, CancellationToken cancellationToken = default);
}
