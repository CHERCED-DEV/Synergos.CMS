namespace Synergos.CMS.Interfaces;

/// <summary>
/// Candado de idempotencia GENÉRICO y domain-neutral: reclama de forma ATÓMICA y
/// exclusiva una <paramref name="key"/> dentro de un <paramref name="scope"/>. Es la
/// generalización del ledger que T3 introdujo para el webhook de pago (su primer
/// consumidor); T4 (notificaciones) es el segundo — el mismo razonamiento de fan-out que
/// llevó a <see cref="IJsonEntityStore"/> (ADR 0105).
/// </summary>
/// <remarks>
/// <b>Por qué NO se colapsa en <see cref="IJsonEntityStore"/>:</b> su primitiva es
/// distinta. El store genérico hace upsert (write-overwrite) y NO tiene compare-and-swap;
/// esta operación exige un candado atómico anti-TOCTOU — el adapter FileSystem lo logra
/// con <c>FileMode.CreateNew</c> (el SO garantiza que solo un llamado crea el archivo).
/// La familia de storage es "uno por PRIMITIVA", no "uno para todo".
///
/// <para><b>scope</b> aísla familias de claves (calca el <c>resourceType</c> del store
/// genérico): <c>"payment-events"</c> (webhook del PSP, keyed por <c>{provider}-{eventId}</c>)
/// y <c>"notifications"</c> (T4, keyed por el HECHO de negocio). Cambiarlo re-ubica los
/// marcadores.</para>
/// </remarks>
public interface IIdempotencyLedger
{
    /// <summary>
    /// Reclama <paramref name="key"/> en <paramref name="scope"/>. Devuelve <c>true</c> si
    /// ESTE llamado la reclamó por primera vez (el caller debe ejecutar el side-effect);
    /// <c>false</c> si ya estaba reclamada (duplicado — no re-ejecutar). Atómico: bajo
    /// concurrencia exactamente UN llamado obtiene <c>true</c>. Un fallo de I/O real
    /// PROPAGA (jamás se silencia como duplicado).
    /// </summary>
    Task<bool> TryClaimAsync(string scope, string key, CancellationToken cancellationToken = default);
}
