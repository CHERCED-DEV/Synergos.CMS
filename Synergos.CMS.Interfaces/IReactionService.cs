namespace Synergos.CMS.Interfaces;

/// <summary>
/// Reacciones/likes por item del feed (dominio Blogs — red social, OLA 3).
/// Modela "contador + estado-por-usuario" (¿yo reaccioné?), idempotente por
/// <c>(actor, objeto, tipo)</c> — el patrón canónico de toda red social. Es la
/// pieza del MOTOR que habilita el optimistic UI del botón de reacción.
/// </summary>
/// <remarks>
/// Seam stub-first: el default <c>StubReactionService</c> (Application, estado
/// en memoria del proceso) sirve la demo end-to-end; el adapter real (store
/// dedicado / event-sourced) implementa la misma seam y se enchufa sin tocar el
/// módulo Angular. <see cref="ReactAsync"/> es un <b>toggle idempotente</b>:
/// reaccionar dos veces con el mismo tipo retira la reacción; reaccionar con
/// otro tipo reemplaza la anterior (un actor tiene a lo sumo una reacción por
/// objeto). ADR 0002 (Application sin Umbraco) + ADR 0075 (seam con tests).
///
/// Generalidad: <c>objectKey</c> es opaco (un item del feed, pero también podría
/// ser otro objeto reaccionable), así que la seam sirve a cualquier dominio que
/// reuse <see cref="IContentStream"/>.
/// </remarks>
public interface IReactionService
{
    /// <summary>
    /// Aplica (toggle) la reacción de <paramref name="actorId"/> sobre
    /// <paramref name="objectKey"/> con el tipo <paramref name="type"/>.
    /// Idempotente: si el actor ya tenía ESE mismo tipo, lo retira (toggle off);
    /// si tenía otro tipo, lo reemplaza; si no tenía, lo agrega. Devuelve el
    /// estado resultante (conteos por tipo + la reacción actual del actor).
    /// </summary>
    Task<ReactionState> ReactAsync(string actorId, string objectKey, string type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retira explícitamente cualquier reacción de <paramref name="actorId"/>
    /// sobre <paramref name="objectKey"/>. Idempotente: si no había reacción, no
    /// lanza. Devuelve el estado resultante.
    /// </summary>
    Task<ReactionState> UnreactAsync(string actorId, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el estado de reacciones de un objeto desde la perspectiva de un
    /// actor: conteos por tipo + el tipo que <paramref name="actorId"/> tiene
    /// puesto (<c>null</c> si no reaccionó). Objeto sin reacciones → conteos
    /// vacíos + total 0.
    /// </summary>
    Task<ReactionState> GetStateAsync(string objectKey, string? actorId = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Estado de reacciones de un objeto: el total, el desglose por tipo
/// (<c>like</c> → 12, <c>love</c> → 3, …) y la reacción actual del actor
/// consultado (<c>null</c> si no reaccionó). Habilita el optimistic UI.
/// </summary>
public sealed record ReactionState(
    string ObjectKey,
    int Total,
    IReadOnlyDictionary<string, int> CountsByType,
    string? MyReaction);
