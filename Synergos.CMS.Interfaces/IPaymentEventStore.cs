namespace Synergos.CMS.Interfaces;

/// <summary>
/// Ledger de idempotencia de webhooks de pago entrantes (T3, doc 25). Un PSP puede
/// entregar el MISMO evento 2+ veces (reintentos, entregas concurrentes); el receptor
/// debe ejecutar los side-effects UNA sola vez. Este seam ofrece una primitiva
/// <b>create-exclusiva atómica</b> keyed por <c>(provider, eventId)</c>.
/// </summary>
/// <remarks>
/// A diferencia de <see cref="IPaymentSessionStore"/>/<see cref="IShopOrderStore"/>
/// (que usan write-overwrite), la idempotencia exige un candado atómico anti-TOCTOU:
/// el adapter FileSystem lo implementa con <c>FileMode.CreateNew</c> (el SO garantiza
/// que solo un llamado crea el archivo). NO es read-then-write (reintroduciría la
/// carrera que se busca evitar).
/// </remarks>
public interface IPaymentEventStore
{
    /// <summary>
    /// Marca <paramref name="eventId"/> del <paramref name="provider"/> como procesado
    /// de forma atómica. Devuelve <c>true</c> si ESTE llamado lo marcó por primera vez
    /// (el receptor debe ejecutar los side-effects); <c>false</c> si ya estaba marcado
    /// (duplicado — el receptor responde 200 sin re-ejecutar nada).
    /// </summary>
    Task<bool> TryMarkProcessedAsync(string provider, string eventId, CancellationToken cancellationToken = default);
}
