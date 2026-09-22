namespace Synergos.CMS.Interfaces;

/// <summary>
/// Puerto de almacenamiento opaco y GENÉRICO de entidades JSON, keyed por
/// <c>resourceType</c> + clave. Es la generalización de los stores
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
    /// <summary>Guarda <paramref name="json"/> bajo <paramref name="key"/>. Sobrescribe.</summary>
    Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default);

    /// <summary>El documento, o <c>null</c> si no existe.</summary>
    Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Los <b>DOCUMENTOS</b> de la familia — no sus claves, aunque el nombre invite a leerlo así.
    /// </summary>
    /// <remarks>
    /// Está escrito porque la equivocación no falla: quien lo lea como una lista de ids y pida
    /// cada uno con <see cref="ReadAsync"/> recibe <c>null</c> en todos y se queda con una
    /// colección <b>vacía</b>, que se lee como «no hay nada». Las dos implementaciones coinciden
    /// —devuelven el JSON— y hasta el #158 ninguna lo decía.
    /// <para><b>Y no hay orden</b>: un directorio no lo tiene. Quien necesite uno lo aplica.</para>
    /// </remarks>
    Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default);

    /// <summary><c>true</c> si había algo que borrar.</summary>
    Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default);
}
