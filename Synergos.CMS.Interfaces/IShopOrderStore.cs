namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento OPACO de órdenes de Tienda (T1, doc 25): guarda / lee /
/// lista / borra un JSON por <c>orderRef</c>. No conoce el dominio — el motor
/// (<see cref="IShopOrderService"/>) serializa/deserializa y filtra encima. Calca el
/// apilado del PHI (<c>IPatientRepository → IPhiStore → disco</c>) sin la carga
/// regulatoria: es PII de compra, NO PHI → sin cifrado. El swap FileSystem→SQLite
/// queda invisible para el motor y el contrato del servicio.
/// </summary>
public interface IShopOrderStore
{
    /// <summary>Persiste (o sobrescribe) el JSON de una orden por su <paramref name="orderRef"/>.</summary>
    Task WriteAsync(string orderRef, string json, CancellationToken cancellationToken = default);

    /// <summary>Lee el JSON de una orden; null si no existe.</summary>
    Task<string?> ReadAsync(string orderRef, CancellationToken cancellationToken = default);

    /// <summary>Todos los JSON de órdenes (el filtrado por email/member lo hace el motor).</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Borra una orden; devuelve si existía. Idempotente.</summary>
    Task<bool> DeleteAsync(string orderRef, CancellationToken cancellationToken = default);
}
