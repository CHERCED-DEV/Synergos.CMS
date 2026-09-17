using Synergos.Api.Engagement.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Engagement.Domain;

/// <summary>El resumen de lo que opinó la gente sobre algo.</summary>
/// <param name="Comments">Cuántos comentarios visibles.</param>
/// <param name="Favorites">Cuántos favoritos.</param>
/// <param name="Ratings">Cuántas valoraciones.</param>
/// <param name="AverageRating">Promedio, o <c>null</c> si nadie valoró.</param>
/// <param name="Reactions">Cuántas de cada reacción.</param>
public sealed record EngagementSummary(
    int Comments, int Favorites, int Ratings, decimal? AverageRating, IReadOnlyDictionary<string, int> Reactions);

/// <summary>Compone las reglas de <see cref="EngagementRules"/> con el almacén.</summary>
public sealed class EngagementService
{
    private readonly IEngagementStore _store;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public EngagementService(IEngagementStore store, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _store = store;
        _idempotency = idempotency;
        _clock = clock;
    }

    public Result<Engagement> Add(
        Ref actor, Ref target, EngagementKind kind,
        string? body, int? rating, string? reactionCode, string? parentId, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("engagement", key) is { } yaEra)
            {
                return _store.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{EngagementRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la opinión no está.");
            }

            var motivo = EngagementRules.CheckShape(kind, body, rating, reactionCode)
                         ?? EngagementRules.CheckDuplicate(kind, _store.FindOf(actor, target, kind));
            if (motivo is not null) return Result.Rejected<Engagement>(motivo);

            if (parentId is not null && _store.Find(parentId) is null)
            {
                return Rejection.NotFound($"{EngagementRules.CodePrefix}.parent_not_found",
                    $"No existe la opinión {parentId} a la que se quiso responder.");
            }

            var id = Guid.NewGuid().ToString("n");
            var item = new Engagement(id, actor, target, kind,
                body?.Trim(), rating, reactionCode?.Trim(), parentId, Visible: true, _clock.GetUtcNow());

            _store.Put(item);
            _idempotency.Remember("engagement", key, id);
            return Result.Ok(item);
        }
    }

    public Result<Engagement> Get(string id)
        => _store.Find(id) is { } e
            ? Result.Ok(e)
            : Rejection.NotFound($"{EngagementRules.CodePrefix}.not_found", $"No existe la opinión {id}.");

    /// <summary>
    /// Oculta una opinión. Es lo que llama la moderación.
    /// </summary>
    /// <remarks>
    /// <b>Oculta, no borra.</b> Esta capacidad no decide QUÉ se oculta —eso es de
    /// <c>Api.Moderation</c>—; solo sabe ocultar. Y conservar la fila es lo que permite que una
    /// decisión de moderación se pueda revisar o revertir: borrada, "se ocultó por error" no
    /// tiene arreglo.
    /// </remarks>
    public Result<Engagement> SetVisibility(string id, bool visible)
    {
        lock (_gate)
        {
            var item = _store.Find(id);
            if (item is null)
            {
                return Rejection.NotFound($"{EngagementRules.CodePrefix}.not_found", $"No existe la opinión {id}.");
            }
            if (item.Visible == visible) return Result.Ok(item);   // idempotente

            var actualizado = item with { Visible = visible };
            _store.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    public Result<Page<Engagement>> ListForTarget(Ref? target, EngagementKind? kind, int offset, int limit)
    {
        if (target is null)
        {
            return Rejection.Invalid($"{EngagementRules.CodePrefix}.target_required", "Hace falta filtrar por objetivo.");
        }

        var todos = _store.ForTarget(target)
            .Where(e => e.Visible && (kind is null || e.Kind == kind))
            .OrderByDescending(e => e.AtUtc)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Engagement>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }

    /// <summary>El resumen que pinta una ficha sin traerse mil comentarios.</summary>
    public Result<EngagementSummary> Summarize(Ref? target)
    {
        if (target is null)
        {
            return Rejection.Invalid($"{EngagementRules.CodePrefix}.target_required", "Hace falta el objetivo.");
        }

        // Solo lo VISIBLE. Contar lo oculto haría que moderar un comentario no cambiara el
        // contador — y el número visible dejaría de cuadrar con la lista.
        var visibles = _store.ForTarget(target).Where(e => e.Visible).ToList();
        var puntajes = visibles.Where(e => e.Kind == EngagementKind.Rating && e.Rating is not null).Select(e => e.Rating!.Value).ToList();

        var reacciones = visibles
            .Where(e => e.Kind == EngagementKind.Reaction && e.ReactionCode is not null)
            .GroupBy(e => e.ReactionCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return Result.Ok(new EngagementSummary(
            visibles.Count(e => e.Kind == EngagementKind.Comment),
            visibles.Count(e => e.Kind == EngagementKind.Favorite),
            puntajes.Count,
            puntajes.Count == 0 ? null : Math.Round(puntajes.Sum() / (decimal)puntajes.Count, 2),
            reacciones));
    }
}
