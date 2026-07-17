using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IReactionService"/> — reacciones/likes por item del feed
/// (dominio Blogs) STUB con estado en memoria del proceso. Modela "contador +
/// estado-por-usuario" idempotente por <c>(actor, objeto)</c> (un actor tiene a
/// lo sumo una reacción por objeto). Siembra contadores realistas desde
/// <see cref="SocialDemoSeed"/> para que la demo arranque con engagement.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore (ADR
/// 0002). <see cref="ReactAsync"/> es un toggle idempotente; el adapter real
/// (store dedicado / event-sourced) reemplaza la seam sin tocar el módulo Angular.
/// ADR 0075 (seam con tests). Singleton — el estado vive en el proceso.
/// </remarks>
public sealed class StubReactionService : IReactionService
{
    // objectKey → (actorId → type). ConcurrentDictionary externo; el inner se
    // protege con lock por objeto para mantener el toggle atómico.
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _reactions =
        new(StringComparer.Ordinal);

    public StubReactionService()
    {
        // Sembrar desde la semilla compartida (copia defensiva — no mutar la semilla).
        foreach (var (objectKey, byActor) in SocialDemoSeed.Reactions)
        {
            _reactions[objectKey] = new Dictionary<string, string>(byActor, StringComparer.Ordinal);
        }
    }

    public Task<ReactionState> ReactAsync(string actorId, string objectKey, string type, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("El actorId es requerido.", nameof(actorId));
        }
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("El objectKey es requerido.", nameof(objectKey));
        }
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "like" : type.Trim().ToLowerInvariant();

        var bucket = _reactions.GetOrAdd(objectKey, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        lock (bucket)
        {
            if (bucket.TryGetValue(actorId, out var existing))
            {
                // Toggle: mismo tipo → retira; otro tipo → reemplaza.
                if (string.Equals(existing, normalizedType, StringComparison.Ordinal))
                {
                    bucket.Remove(actorId);
                }
                else
                {
                    bucket[actorId] = normalizedType;
                }
            }
            else
            {
                bucket[actorId] = normalizedType;
            }

            return Task.FromResult(BuildState(objectKey, bucket, actorId));
        }
    }

    public Task<ReactionState> UnreactAsync(string actorId, string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("El actorId es requerido.", nameof(actorId));
        }
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("El objectKey es requerido.", nameof(objectKey));
        }

        var bucket = _reactions.GetOrAdd(objectKey, _ => new Dictionary<string, string>(StringComparer.Ordinal));
        lock (bucket)
        {
            bucket.Remove(actorId); // idempotente: remover lo ausente no lanza.
            return Task.FromResult(BuildState(objectKey, bucket, actorId));
        }
    }

    public Task<ReactionState> GetStateAsync(string objectKey, string? actorId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("El objectKey es requerido.", nameof(objectKey));
        }

        if (!_reactions.TryGetValue(objectKey, out var bucket))
        {
            return Task.FromResult(new ReactionState(
                objectKey, 0, new Dictionary<string, int>(StringComparer.Ordinal), null));
        }

        lock (bucket)
        {
            return Task.FromResult(BuildState(objectKey, bucket, actorId));
        }
    }

    /// <summary>Total de reacciones de un objeto (lo consume el feed para las métricas).</summary>
    public int CountFor(string objectKey)
        => _reactions.TryGetValue(objectKey, out var bucket)
            ? CountSnapshot(bucket)
            : 0;

    /// <summary>
    /// Snapshot de las reacciones de un objeto: <c>(actorId, tipo)</c> por cada
    /// actor que reaccionó. Lo consume el <see cref="StubNotificationFeed"/> para
    /// derivar notificaciones "X reaccionó a tu post" sin duplicar el estado de
    /// reacciones (misma técnica de composición que <see cref="StubContentStream"/>).
    /// Lista vacía si el objeto no tiene reacciones.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> ReactionsFor(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || !_reactions.TryGetValue(objectKey, out var bucket))
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }

        lock (bucket)
        {
            return bucket.ToList();
        }
    }

    private static int CountSnapshot(Dictionary<string, string> bucket)
    {
        lock (bucket)
        {
            return bucket.Count;
        }
    }

    // Asume que el caller ya tomó el lock del bucket.
    private static ReactionState BuildState(string objectKey, Dictionary<string, string> bucket, string? actorId)
    {
        var countsByType = bucket.Values
            .GroupBy(t => t, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string? mine = null;
        if (!string.IsNullOrWhiteSpace(actorId) && bucket.TryGetValue(actorId, out var t))
        {
            mine = t;
        }

        return new ReactionState(objectKey, bucket.Count, countsByType, mine);
    }
}
