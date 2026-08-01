using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IReactionService"/> — reacciones/likes por item del feed
/// (dominio Blogs). Modela "contador + estado-por-usuario" idempotente por
/// <c>(actor, objeto)</c> (un actor tiene a lo sumo una reacción por objeto).
/// Los contadores de la demo salen de <see cref="SocialDemoSeed"/> para que
/// arranque con engagement.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore
/// (ADR 0002). <see cref="ReactAsync"/> es un toggle idempotente; el adapter real
/// (store dedicado / event-sourced) reemplaza la seam sin tocar el módulo
/// Angular. ADR 0075 (seam con tests).</para>
///
/// <para><b>Durabilidad.</b> Las reacciones viven detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del
/// proceso: un reinicio borraba TODAS las reacciones de TODOS los posts, y con
/// ellas las métricas del feed y las notificaciones que se derivan de ellas.</para>
///
/// <para><b>Un documento por objeto</b> (<c>{objectKey}</c> es la clave) con las
/// reacciones de cada actor. La semilla NO se escribe al arrancar (ADR 0013,
/// cero I/O en boot): un documento ausente se resuelve contra
/// <see cref="SocialDemoSeed.Reactions"/>, y la primera reacción sobre ese objeto
/// escribe el documento, que a partir de ahí MANDA sobre la semilla — así retirar
/// una reacción sembrada sobrevive el reinicio en vez de re-sembrarse.</para>
///
/// <para><b>Costo de lectura.</b> Reaccionar, retirar y consultar un objeto son
/// UNA lectura puntual (más una escritura si muta).
/// <see cref="CountsForAllAsync"/> existe justamente para que el feed NO haga N
/// lecturas puntuales: recorre el espacio UNA vez y devuelve todos los totales.
/// Se recorre en cada llamada a propósito — en este repo una caché se entrega
/// junto con su invalidador y su consumidor, nunca antes.</para>
/// </remarks>
public sealed class StubReactionService : IReactionService
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-social-reactions/).</summary>
    public const string DefaultResourceType = "social-reactions";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write del toggle. lock{} no sirve: el cuerpo hace await.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubReactionService()
        : this(null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa las reacciones de las otras familias sociales.
    /// </summary>
    /// <param name="store">Backing store durable. Null deja las reacciones en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades. Null usa
    ///   <see cref="DefaultResourceType"/>.</param>
    public StubReactionService(IJsonEntityStore? store, string? storeNamespace)
    {
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<ReactionState> ReactAsync(string actorId, string objectKey, string type, CancellationToken cancellationToken = default)
    {
        RequireActor(actorId);
        RequireObject(objectKey);
        var normalizedType = string.IsNullOrWhiteSpace(type) ? "like" : type.Trim().ToLowerInvariant();

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bucket = await LoadAsync(objectKey, cancellationToken).ConfigureAwait(false);
            var index = IndexOfActor(bucket, actorId);
            if (index >= 0)
            {
                // Toggle: mismo tipo → retira; otro tipo → reemplaza.
                if (string.Equals(bucket[index].Type, normalizedType, StringComparison.Ordinal))
                {
                    bucket.RemoveAt(index);
                }
                else
                {
                    bucket[index] = new ReactionEntry { ActorId = actorId, Type = normalizedType };
                }
            }
            else
            {
                bucket.Add(new ReactionEntry { ActorId = actorId, Type = normalizedType });
            }

            await SaveAsync(objectKey, bucket, cancellationToken).ConfigureAwait(false);
            return BuildState(objectKey, bucket, actorId);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<ReactionState> UnreactAsync(string actorId, string objectKey, CancellationToken cancellationToken = default)
    {
        RequireActor(actorId);
        RequireObject(objectKey);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bucket = await LoadAsync(objectKey, cancellationToken).ConfigureAwait(false);
            var index = IndexOfActor(bucket, actorId);
            if (index >= 0)
            {
                // idempotente: remover lo ausente no lanza (ni escribe).
                bucket.RemoveAt(index);
                await SaveAsync(objectKey, bucket, cancellationToken).ConfigureAwait(false);
            }
            return BuildState(objectKey, bucket, actorId);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<ReactionState> GetStateAsync(string objectKey, string? actorId = null, CancellationToken cancellationToken = default)
    {
        RequireObject(objectKey);

        var bucket = await LoadAsync(objectKey, cancellationToken).ConfigureAwait(false);
        return BuildState(objectKey, bucket, actorId);
    }

    /// <summary>Total de reacciones de un objeto — una lectura puntual.</summary>
    public async Task<int> CountForAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return 0;
        }
        var bucket = await LoadAsync(objectKey, cancellationToken).ConfigureAwait(false);
        return bucket.Count;
    }

    /// <summary>
    /// Totales de TODOS los objetos con reacciones, en UN recorrido del espacio.
    /// Lo consume <see cref="StubContentStream"/> para proyectar las métricas del
    /// feed: sin esto, una página de feed haría una lectura puntual por item.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> CountsForAllAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (objectKey, byActor) in SocialDemoSeed.Reactions)
        {
            counts[objectKey] = byActor.Count;
        }

        // OJO: ListAsync devuelve los DOCUMENTOS, no las claves — por eso cada
        // documento lleva dentro su ObjectKey.
        var raws = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        foreach (var json in raws)
        {
            var doc = TryParse(json);
            if (doc is null)
            {
                continue;
            }
            counts[doc.ObjectKey] = doc.Reactions.Count;
        }
        return counts;
    }

    /// <summary>
    /// Snapshot de las reacciones de un objeto: <c>(actorId, tipo)</c> por cada
    /// actor que reaccionó. Lo consume el <see cref="StubNotificationFeed"/> para
    /// derivar notificaciones "X reaccionó a tu post" sin duplicar el estado de
    /// reacciones (misma técnica de composición que <see cref="StubContentStream"/>).
    /// Lista vacía si el objeto no tiene reacciones.
    /// </summary>
    public async Task<IReadOnlyList<KeyValuePair<string, string>>> ReactionsForAsync(
        string objectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }
        var bucket = await LoadAsync(objectKey, cancellationToken).ConfigureAwait(false);
        return bucket.Select(r => new KeyValuePair<string, string>(r.ActorId, r.Type)).ToList();
    }

    /// <summary>
    /// Reacciones de un objeto. Documento ausente o ilegible cae a la semilla —
    /// un documento corrupto no puede tumbar el feed de nadie.
    /// </summary>
    private async Task<List<ReactionEntry>> LoadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, objectKey, cancellationToken).ConfigureAwait(false);
        var doc = TryParse(json);
        if (doc is not null)
        {
            return doc.Reactions;
        }
        if (SocialDemoSeed.Reactions.TryGetValue(objectKey, out var seeded))
        {
            return seeded
                .Select(kv => new ReactionEntry { ActorId = kv.Key, Type = kv.Value })
                .ToList();
        }
        return new List<ReactionEntry>();
    }

    private static ReactionDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var doc = JsonSerializer.Deserialize<ReactionDocument>(json, _json);
            if (doc is null || string.IsNullOrWhiteSpace(doc.ObjectKey))
            {
                return null;
            }
            doc.Reactions ??= new List<ReactionEntry>();
            doc.Reactions.RemoveAll(r => r is null || string.IsNullOrWhiteSpace(r.ActorId));
            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task SaveAsync(string objectKey, List<ReactionEntry> bucket, CancellationToken cancellationToken)
        => _store.WriteAsync(
            _resourceType,
            objectKey,
            JsonSerializer.Serialize(
                new ReactionDocument { ObjectKey = objectKey, Reactions = bucket }, _json),
            cancellationToken);

    private static int IndexOfActor(List<ReactionEntry> bucket, string actorId)
        => bucket.FindIndex(r => string.Equals(r.ActorId, actorId, StringComparison.Ordinal));

    private static void RequireActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("El actorId es requerido.", nameof(actorId));
        }
    }

    private static void RequireObject(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("El objectKey es requerido.", nameof(objectKey));
        }
    }

    private static ReactionState BuildState(string objectKey, List<ReactionEntry> bucket, string? actorId)
    {
        var countsByType = bucket
            .GroupBy(r => r.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string? mine = null;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            var index = IndexOfActor(bucket, actorId);
            if (index >= 0)
            {
                mine = bucket[index].Type;
            }
        }

        return new ReactionState(objectKey, bucket.Count, countsByType, mine);
    }

    /// <summary>Documento persistido: las reacciones de UN objeto.</summary>
    private sealed class ReactionDocument
    {
        public string ObjectKey { get; set; } = string.Empty;
        public List<ReactionEntry> Reactions { get; set; } = new();
    }

    /// <summary>La reacción de un actor: quién y de qué tipo.</summary>
    private sealed class ReactionEntry
    {
        public string ActorId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
