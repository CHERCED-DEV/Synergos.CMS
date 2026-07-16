namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento opaco del estado de sesiones de pago (T3, doc 25).
/// JSON por <c>sessionId</c> — no conoce el dominio; guarda/lee/lista/borra strings.
/// Calca <see cref="IShopOrderStore"/> (T1): el <see cref="IPaymentProvider"/>
/// serializa/deserializa encima. Con un adapter FileSystem la sesión de pago
/// SOBREVIVE un reinicio — cierra la brecha por la que un checkout hecho antes del
/// reinicio no se podía capturar (la sesión vivía solo en memoria del proceso).
/// Cifrado NO aplica (PII de compra, no PHI).
/// </summary>
public interface IPaymentSessionStore
{
    Task WriteAsync(string sessionId, string json, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}
