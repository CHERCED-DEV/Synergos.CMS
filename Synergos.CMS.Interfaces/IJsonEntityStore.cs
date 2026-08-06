namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento opaco y GENÉRICO de entidades JSON, keyed por
/// <paramref name="resourceType"/> + clave. Es la generalización de los stores
/// dedicados que T1/T3 y el fan-out de Booking probaron por separado (órdenes de
/// Tienda, sesiones de pago, reservas, órdenes de viaje): los cuatro tenían la MISMA
/// forma y la misma implementación FileSystem duplicada. Regla de oro del doc 25:
/// ninguna capacidad transversal se implementa dos veces.
/// </summary>
/// <remarks>
/// No conoce el dominio: guarda/lee/lista/borra strings. El motor de cada dominio
/// serializa/deserializa encima y filtra sobre <see cref="ListAsync"/> — igual que
/// antes. El adapter FileSystem persiste en <c>{StorageRoot}/syn-{resourceType}/{key}.json</c>
/// con escritura atómica; el swap a SQLite (índices secundarios) es invisible para los
/// motores. Cifrado NO aplica (PII de compra, no PHI — el PHI tiene su propio
/// <c>IPhiStore</c> cifrado).
///
/// <para><b>resourceType</b> es el discriminador de la familia de entidades
/// (<c>"orders"</c>, <c>"payments"</c>, <c>"reservations"</c>, <c>"travel-orders"</c>).
/// Define el subdirectorio, así que cambiarlo re-ubica los datos.</para>
/// </remarks>
public interface IJsonEntityStore
{
    Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default);
    Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default);
}
