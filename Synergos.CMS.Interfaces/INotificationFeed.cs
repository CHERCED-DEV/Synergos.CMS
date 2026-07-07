namespace Synergos.CMS.Interfaces;

/// <summary>
/// Feed de notificaciones dirigidas a un actor (dominio Blogs — red social, OLA
/// 6 / doc 21 §2.3). Es la pieza del MOTOR que materializa el
/// <c>notification-center</c>: eventos que le pasaron AL actor (alguien lo
/// siguió, reaccionó a su post, comentó, lo mencionó) derivados del grafo social
/// (<see cref="ISocialGraphService"/>), de las reacciones
/// (<see cref="IReactionService"/>) y del stream (<see cref="IContentStream"/>) —
/// no un store paralelo de eventos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Genérico por diseño.</b> El actor destinatario es un identificador estable
/// (actorId / email del Member). Los tipos de evento (<c>follow</c>,
/// <c>reaction</c>, <c>comment</c>, <c>mention</c>) son strings abiertos para que
/// cualquier dominio que reuse los seams sociales pueda notificar sin instanciar
/// Blogs (DIP, ADR 0002).
/// </para>
/// <para>
/// Seam stub-first (igual que el resto del motor social): el default
/// <c>StubNotificationFeed</c> (Application, lógica pura/determinista) DERIVA las
/// notificaciones de los otros seams sociales — no duplica estado. El adapter
/// real (store de eventos / fan-out on write) implementa la misma seam y se
/// enchufa sin tocar el módulo Angular ni el controller. ADR 0002 (Application
/// sin Umbraco) + ADR 0075 (seam con tests).
/// </para>
/// </remarks>
public interface INotificationFeed
{
    /// <summary>
    /// Devuelve las notificaciones dirigidas a <paramref name="recipientId"/>,
    /// de la más reciente a la más antigua, capadas por <paramref name="limit"/>.
    /// Lista vacía si el actor no tiene notificaciones (o viene vacío) — nunca
    /// lanza por destinatario desconocido.
    /// </summary>
    Task<IReadOnlyList<SocialNotification>> GetForAsync(
        string recipientId,
        int limit = 30,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Una notificación dirigida a un actor: qué tipo de evento, quién lo originó
/// (el actor), sobre qué objeto (post/comentario, o <c>null</c> para follow),
/// un texto legible y cuándo ocurrió.
/// </summary>
/// <param name="Id">Id estable/determinista de la notificación.</param>
/// <param name="Type">Tipo de evento: <c>follow</c> | <c>reaction</c> |
///   <c>comment</c> | <c>mention</c> — string abierto.</param>
/// <param name="Actor">Autor del evento (quien siguió/reaccionó/comentó).</param>
/// <param name="ObjectId">Id del objeto afectado (postId), o <c>null</c> para
///   eventos sin objeto (follow).</param>
/// <param name="Text">Texto legible ya compuesto para el centro de notificaciones.</param>
/// <param name="CreatedUtc">Timestamp UTC del evento.</param>
public sealed record SocialNotification(
    string Id,
    string Type,
    NotificationActor Actor,
    string? ObjectId,
    string Text,
    DateTime CreatedUtc);

/// <summary>
/// Proyección liviana del actor que originó una notificación — lo mínimo para
/// renderar el avatar + nombre + link al perfil en el centro de notificaciones.
/// </summary>
public sealed record NotificationActor(
    string Id,
    string Handle,
    string DisplayName,
    string? AvatarUrl,
    bool Verified = false);
