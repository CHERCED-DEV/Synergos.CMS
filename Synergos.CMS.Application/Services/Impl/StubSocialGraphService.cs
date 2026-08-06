using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ISocialGraphService"/> — grafo social dirigido asimétrico
/// (dominio Blogs). follow/unfollow idempotentes por <c>(follower, followee)</c>,
/// listas y conteos. El grafo de la demo sale de <see cref="SocialDemoSeed"/>
/// para que el feed "Siguiendo" y los conteos del perfil arranquen poblados.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore
/// (ADR 0002). El adapter real (store de grafo + caché de conteos) reemplaza la
/// seam sin tocar el módulo Angular. ADR 0075.</para>
///
/// <para><b>Durabilidad.</b> El grafo vive detrás de <see cref="IJsonEntityStore"/>
/// (ADR 0105) y ya no en un diccionario del proceso: un reinicio borraba quién
/// sigue a quién, y con eso el feed "Siguiendo" y los conteos del perfil de
/// todos los usuarios.</para>
///
/// <para><b>Un documento por seguidor</b> (<c>{followerId}</c> es la clave) con
/// la lista de a quiénes sigue. La semilla NO se escribe al arrancar (ADR 0013,
/// cero I/O en boot): un documento ausente se resuelve contra
/// <see cref="SocialDemoSeed.Follows"/>, y el primer follow/unfollow de ese
/// actor escribe el documento, que a partir de ahí MANDA sobre la semilla. Así
/// dejar de seguir a alguien sembrado sobrevive el reinicio en vez de
/// re-sembrarse.</para>
///
/// <para><b>Costo de lectura.</b> "A quién sigo" y "¿sigo a X?" son UNA lectura
/// puntual. Los seguidores y los conteos de seguidores son inherentemente el
/// índice inverso: exigen recorrer el espacio completo (un <c>ListAsync</c>).
/// follow/unfollow también, porque devuelven los conteos del followee. No se
/// cachea a propósito: en este repo una caché se entrega junto con su
/// invalidador y su consumidor, nunca antes.</para>
///
/// <para>Los ids de actor se comparan ordinal (case-SENSITIVE), igual que antes.
/// Ojo con el adapter FileSystem sobre un sistema de archivos
/// case-insensitive: dos ids que difieran solo en mayúsculas caerían en el mismo
/// documento. Los actores del proyecto son minúsculas (<c>act-elena</c>).</para>
/// </remarks>
public sealed class StubSocialGraphService : ISocialGraphService
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-social-graph/).</summary>
    public const string DefaultResourceType = "social-graph";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write de follow/unfollow. lock{} no sirve: el
    // cuerpo hace await.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubSocialGraphService()
        : this(null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa el grafo de las otras familias sociales.
    /// </summary>
    /// <param name="store">Backing store durable. Null deja el grafo en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades. Null usa
    ///   <see cref="DefaultResourceType"/>.</param>
    public StubSocialGraphService(IJsonEntityStore? store, string? storeNamespace)
    {
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<FollowState> FollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        ValidatePair(followerId, followeeId);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var graph = await LoadAllAsync(cancellationToken).ConfigureAwait(false);

            // No auto-seguirse: devolver el estado sin cambio.
            if (!string.Equals(followerId, followeeId, StringComparison.Ordinal))
            {
                if (!graph.TryGetValue(followerId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    graph[followerId] = set;
                }
                if (set.Add(followeeId)) // idempotente: re-seguir no duplica ni reescribe.
                {
                    await SaveAsync(followerId, set, cancellationToken).ConfigureAwait(false);
                }
            }

            return BuildState(graph, followerId, followeeId);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<FollowState> UnfollowAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        ValidatePair(followerId, followeeId);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var graph = await LoadAllAsync(cancellationToken).ConfigureAwait(false);

            if (graph.TryGetValue(followerId, out var set) && set.Remove(followeeId))
            {
                // idempotente: quitar lo ausente no lanza ni reescribe.
                await SaveAsync(followerId, set, cancellationToken).ConfigureAwait(false);
            }

            return BuildState(graph, followerId, followeeId);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<bool> IsFollowingAsync(string followerId, string followeeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(followerId) || string.IsNullOrWhiteSpace(followeeId))
        {
            return false;
        }

        var set = await LoadOneAsync(followerId, cancellationToken).ConfigureAwait(false);
        return set.Contains(followeeId);
    }

    public async Task<IReadOnlyList<string>> GetFollowersAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Array.Empty<string>();
        }

        // Índice inverso: no hay forma de resolverlo sin recorrer el espacio.
        var graph = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return graph
            .Where(kv => kv.Value.Contains(actorId))
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetFollowingAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Array.Empty<string>();
        }

        var set = await LoadOneAsync(actorId, cancellationToken).ConfigureAwait(false);
        return set.OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    public async Task<SocialCounts> GetCountsAsync(string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return new SocialCounts(0, 0);
        }

        var graph = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return Counts(graph, actorId);
    }

    /// <summary>
    /// Reconstruye el grafo completo: la semilla como base y los documentos
    /// guardados encima (un documento MANDA sobre la semilla del mismo actor).
    /// Un documento ilegible se SALTA — el grafo de un usuario corrupto no puede
    /// tumbar el feed de todos los demás.
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (follower, followees) in SocialDemoSeed.Follows)
        {
            graph[follower] = new HashSet<string>(followees, StringComparer.Ordinal);
        }

        // OJO: ListAsync devuelve los DOCUMENTOS, no las claves — por eso cada
        // documento lleva dentro su FollowerId.
        var raws = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        foreach (var json in raws)
        {
            var doc = TryParse(json);
            if (doc is null)
            {
                continue;
            }
            graph[doc.FollowerId] = new HashSet<string>(doc.Followees, StringComparer.Ordinal);
        }
        return graph;
    }

    /// <summary>
    /// A quién sigue UN actor — una lectura puntual. Documento ausente o
    /// ilegible cae a la semilla (mismo criterio que <see cref="LoadAllAsync"/>).
    /// </summary>
    private async Task<HashSet<string>> LoadOneAsync(string actorId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, actorId, cancellationToken).ConfigureAwait(false);
        var doc = TryParse(json);
        if (doc is not null)
        {
            return new HashSet<string>(doc.Followees, StringComparer.Ordinal);
        }
        return SocialDemoSeed.Follows.TryGetValue(actorId, out var seeded)
            ? new HashSet<string>(seeded, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
    }

    private static FollowDocument? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var doc = JsonSerializer.Deserialize<FollowDocument>(json, _json);
            if (doc is null || string.IsNullOrWhiteSpace(doc.FollowerId))
            {
                return null;
            }
            doc.Followees ??= new List<string>();
            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task SaveAsync(string followerId, IEnumerable<string> followees, CancellationToken cancellationToken)
        => _store.WriteAsync(
            _resourceType,
            followerId,
            JsonSerializer.Serialize(
                new FollowDocument { FollowerId = followerId, Followees = followees.ToList() }, _json),
            cancellationToken);

    private static void ValidatePair(string followerId, string followeeId)
    {
        if (string.IsNullOrWhiteSpace(followerId))
        {
            throw new ArgumentException("El followerId es requerido.", nameof(followerId));
        }
        if (string.IsNullOrWhiteSpace(followeeId))
        {
            throw new ArgumentException("El followeeId es requerido.", nameof(followeeId));
        }
    }

    private static SocialCounts Counts(Dictionary<string, HashSet<string>> graph, string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return new SocialCounts(0, 0);
        }

        var followers = graph.Count(kv => kv.Value.Contains(actorId));
        var following = graph.TryGetValue(actorId, out var set) ? set.Count : 0;
        return new SocialCounts(followers, following);
    }

    private static FollowState BuildState(
        Dictionary<string, HashSet<string>> graph, string followerId, string followeeId)
    {
        var following = graph.TryGetValue(followerId, out var set) && set.Contains(followeeId);
        var followeeCounts = Counts(graph, followeeId);
        return new FollowState(
            followerId,
            followeeId,
            following,
            followeeCounts.Followers,
            followeeCounts.Following);
    }

    /// <summary>Documento persistido: a quiénes sigue UN actor.</summary>
    private sealed class FollowDocument
    {
        public string FollowerId { get; set; } = string.Empty;
        public List<string> Followees { get; set; } = new();
    }
}
