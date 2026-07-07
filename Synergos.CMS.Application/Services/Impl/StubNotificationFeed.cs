using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="INotificationFeed"/> — centro de notificaciones (dominio
/// Blogs — red social, OLA 6) STUB que DERIVA los eventos de los otros seams
/// sociales en vez de mantener un store paralelo: quién sigue al destinatario
/// (<see cref="ISocialGraphService"/>), quién reaccionó a sus posts
/// (<see cref="IReactionService"/> concreto, conteo O(1)) y las menciones/
/// comentarios sembrados. Coherente con el resto de la demo (mismos actores,
/// mismos posts).
/// </summary>
/// <remarks>
/// <para>
/// <b>Sin estado propio</b> — cada llamada recompone las notificaciones desde el
/// grafo/reacciones vivos, por lo que un follow o una reacción NUEVOS (hechos vía
/// el controller) aparecen en la próxima consulta sin fan-out on write. El Id de
/// cada notificación es determinista (<c>tipo + actor + objeto</c>) para que la
/// UI pueda deduplicar/mostrar leído sin store.
/// </para>
/// <para>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore (ADR
/// 0002). El adapter real (store de eventos con fan-out) reemplaza la seam sin
/// tocar el módulo Angular. ADR 0075 (seam con tests). Singleton — stateless.
/// </para>
/// </remarks>
public sealed class StubNotificationFeed : INotificationFeed
{
    private readonly ISocialGraphService _graph;
    private readonly StubReactionService _reactions;
    private readonly Func<DateTime> _utcNow;

    public StubNotificationFeed(ISocialGraphService graph, IReactionService reactions)
        : this(graph, reactions, null)
    {
    }

    /// <summary>
    /// Ctor con time source inyectable (<paramref name="utcNow"/>) para
    /// determinismo en tests. Depende del <see cref="StubReactionService"/>
    /// concreto para leer las reacciones por objeto (misma técnica que
    /// <see cref="StubContentStream"/> — no se duplica el estado de reacciones).
    /// </summary>
    public StubNotificationFeed(ISocialGraphService graph, IReactionService reactions, Func<DateTime>? utcNow)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _reactions = reactions as StubReactionService
            ?? throw new ArgumentException(
                "StubNotificationFeed espera el StubReactionService concreto para leer reacciones.",
                nameof(reactions));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<SocialNotification>> GetForAsync(
        string recipientId,
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientId))
        {
            return Array.Empty<SocialNotification>();
        }

        var recipient = recipientId.Trim();
        var cap = limit <= 0 ? 30 : limit;
        var now = _utcNow();
        var events = new List<SocialNotification>();

        // ── follow: quién sigue al destinatario (del grafo vivo) ─────────
        var followers = await _graph.GetFollowersAsync(recipient, cancellationToken);
        var minute = 0;
        foreach (var followerId in followers)
        {
            var actor = ToActor(followerId);
            events.Add(new SocialNotification(
                Id: $"ntf-follow-{followerId}-{recipient}",
                Type: "follow",
                Actor: actor,
                ObjectId: null,
                Text: $"{actor.DisplayName} empezó a seguirte.",
                CreatedUtc: now.AddMinutes(-minute++)));
        }

        // ── reaction: quién reaccionó a los posts del destinatario ───────
        foreach (var post in SocialDemoSeed.Posts.Where(p =>
                     string.Equals(p.AuthorId, recipient, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var (reactorId, type) in _reactions.ReactionsFor(post.Id))
            {
                if (string.Equals(reactorId, recipient, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // no notificar reacciones propias.
                }
                var actor = ToActor(reactorId);
                events.Add(new SocialNotification(
                    Id: $"ntf-reaction-{reactorId}-{post.Id}",
                    Type: "reaction",
                    Actor: actor,
                    ObjectId: post.Id,
                    Text: $"{actor.DisplayName} reaccionó ({type}) a tu publicación.",
                    CreatedUtc: now.AddMinutes(-minute++)));
            }
        }

        // ── mention: menciones @handle del destinatario en posts ajenos ──
        var myHandle = SocialDemoSeed.AuthorById(recipient).Handle;
        if (!string.IsNullOrWhiteSpace(myHandle))
        {
            var needle = "@" + myHandle;
            foreach (var post in SocialDemoSeed.Posts.Where(p =>
                         !string.Equals(p.AuthorId, recipient, StringComparison.OrdinalIgnoreCase) &&
                         p.Body.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                var actor = ToActor(post.AuthorId);
                events.Add(new SocialNotification(
                    Id: $"ntf-mention-{post.AuthorId}-{post.Id}",
                    Type: "mention",
                    Actor: actor,
                    ObjectId: post.Id,
                    Text: $"{actor.DisplayName} te mencionó en una publicación.",
                    CreatedUtc: now.AddMinutes(-minute++)));
            }
        }

        return events
            .OrderByDescending(e => e.CreatedUtc)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(cap)
            .ToList();
    }

    private static NotificationActor ToActor(string actorId)
    {
        var author = SocialDemoSeed.AuthorById(actorId);
        return new NotificationActor(
            Id: author.Id,
            Handle: author.Handle,
            DisplayName: author.DisplayName,
            AvatarUrl: author.AvatarUrl,
            Verified: author.Verified);
    }
}
