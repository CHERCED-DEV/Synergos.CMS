namespace Synergos.CMS.Interfaces;

/// <summary>
/// Grafo social dirigido asimétrico (dominio Blogs — red social, OLA 3):
/// follow/unfollow + listas y conteos de seguidores/seguidos. Es la pieza del
/// MOTOR que deriva el feed "Siguiendo" (<see cref="FeedScope.Following"/>) y
/// el botón Follow del perfil. "A sigue a B" no implica reciprocidad
/// (asimétrico, modelo Twitter/Instagram).
/// </summary>
/// <remarks>
/// Seam stub-first: el default <c>StubSocialGraphService</c> (Application,
/// estado en memoria del proceso, idempotente) sirve la demo end-to-end; el
/// adapter real (store de grafo + caché de conteos) implementa la misma seam y
/// se enchufa sin tocar el módulo Angular. <see cref="FollowAsync"/> /
/// <see cref="UnfollowAsync"/> son idempotentes por <c>(follower, followee)</c>.
/// ADR 0002 (Application sin Umbraco) + ADR 0075 (seam con tests).
/// </remarks>
public interface ISocialGraphService
{
    /// <summary>
    /// <paramref name="followerId"/> empieza a seguir a
    /// <paramref name="followeeId"/>. Idempotente: re-seguir no duplica ni
    /// lanza. No se permite auto-seguirse (devuelve el estado sin cambio).
    /// Devuelve el estado de relación resultante (incluye conteos del followee).
    /// </summary>
    Task<FollowState> FollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="followerId"/> deja de seguir a
    /// <paramref name="followeeId"/>. Idempotente: dejar de seguir a quien no
    /// sigue no lanza. Devuelve el estado resultante.
    /// </summary>
    Task<FollowState> UnfollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si <paramref name="followerId"/> sigue actualmente a
    /// <paramref name="followeeId"/>.
    /// </summary>
    Task<bool> IsFollowingAsync(string followerId, string followeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los ids de los actores que siguen a <paramref name="actorId"/>
    /// (sus seguidores). Vacío si nadie lo sigue.
    /// </summary>
    Task<IReadOnlyList<string>> GetFollowersAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los ids de los actores que <paramref name="actorId"/> sigue.
    /// Vacío si no sigue a nadie. Esta lista alimenta el feed "Siguiendo".
    /// </summary>
    Task<IReadOnlyList<string>> GetFollowingAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conteos agregados de un actor (seguidores + seguidos) en una sola
    /// llamada — lo consume el header del perfil sin traer las listas enteras.
    /// </summary>
    Task<SocialCounts> GetCountsAsync(string actorId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Estado de una relación de follow tras un follow/unfollow: si el follower
/// sigue al followee + los conteos actualizados del followee (para el optimistic
/// UI del botón Follow).
/// </summary>
public sealed record FollowState(
    string FollowerId,
    string FolloweeId,
    bool Following,
    int FolloweeFollowers,
    int FolloweeFollowing);

/// <summary>Conteos agregados de un actor: cuántos lo siguen y a cuántos sigue.</summary>
public sealed record SocialCounts(int Followers, int Following);
