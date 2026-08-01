using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IContentStream"/> — feed/contenido (dominio Blogs — red
/// social). Implementa el stream paginado por cursor (Para ti / Siguiendo /
/// perfil), el detalle de un item y la creación de items nuevos (optimistic
/// insert al top). Compone <see cref="ISocialGraphService"/> (para derivar el
/// feed "Siguiendo") e <see cref="IReactionService"/> (para las métricas de
/// reacciones), reusando los otros seams sin duplicar estado (DIP, ADR 0002).
/// </summary>
/// <remarks>
/// <para>
/// <b>Genérico / reusable por Educación.</b> El <c>Kind</c> del item discrimina
/// el tipo de contenido (<c>post</c>|<c>article</c>|<c>lesson</c>…). Educación
/// consume ESTA misma seam pasando <see cref="FeedQuery.Kind"/> = <c>lesson</c> —
/// no instancia Blogs ni copia su schema. El adapter real (índice Examine / store
/// de actividad) reemplaza el stub sin tocar el módulo Angular.
/// </para>
/// <para>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero Umbraco/AspNetCore (ADR
/// 0002). Cursor opaco = índice de offset codificado; estable y determinista.
/// ADR 0075 (seam con tests).
/// </para>
///
/// <para><b>Durabilidad.</b> Los items creados viven detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del
/// proceso: un reinicio borraba todo lo que alguien hubiera publicado, dejando
/// solo la semilla de la demo. La semilla NO se persiste — es contenido de
/// código (<see cref="SocialDemoSeed.Posts"/>), no del usuario.</para>
///
/// <para><b>Costo de lectura.</b> Una página de feed recorre DOS espacios
/// completos: los items creados (para unirlos a la semilla y poder ordenar y
/// filtrar sobre el conjunto) y los totales de reacciones —este último con UNA
/// sola llamada (<see cref="StubReactionService.CountsForAllAsync"/>), no una
/// lectura por item. El detalle de un item, en cambio, son dos lecturas
/// puntuales. No se cachea a propósito: en este repo una caché se entrega junto
/// con su invalidador y su consumidor, nunca antes.</para>
/// </remarks>
public sealed class StubContentStream : IContentStream
{
    private const string CursorPrefix = "off:";

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-social-stream/).</summary>
    public const string DefaultResourceType = "social-stream";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISocialGraphService _graph;
    private readonly StubReactionService _reactions;
    private readonly Func<DateTime> _utcNow;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    public StubContentStream(ISocialGraphService graph, IReactionService reactions)
        : this(graph, reactions, null)
    {
    }

    /// <summary>
    /// Ctor con time source inyectable (<paramref name="utcNow"/>) para
    /// determinismo en tests. El stub depende del <see cref="StubReactionService"/>
    /// concreto para leer los conteos de reacción en bloque (las métricas del feed
    /// son una proyección de las reacciones — no se duplican).
    /// </summary>
    public StubContentStream(ISocialGraphService graph, IReactionService reactions, Func<DateTime>? utcNow)
        : this(graph, reactions, utcNow, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa el stream de las otras familias sociales.
    /// </summary>
    /// <param name="graph">Grafo social — deriva el feed "Siguiendo".</param>
    /// <param name="reactions">El <see cref="StubReactionService"/> concreto.</param>
    /// <param name="utcNow">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">Backing store durable. Null deja los items en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades. Null usa
    ///   <see cref="DefaultResourceType"/>.</param>
    public StubContentStream(
        ISocialGraphService graph,
        IReactionService reactions,
        Func<DateTime>? utcNow,
        IJsonEntityStore? store,
        string? storeNamespace)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _reactions = reactions as StubReactionService
            ?? throw new ArgumentException(
                "StubContentStream espera el StubReactionService concreto para proyectar métricas.",
                nameof(reactions));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new FeedQuery();
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        // 1) Conjunto base de items (semilla + creados), más reciente primero.
        IEnumerable<ContentStreamItem> items = await AllItemsAsync(cancellationToken).ConfigureAwait(false);

        // 2) Filtro por kind (clave del polimorfismo: Educación pasa "lesson").
        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            var kind = query.Kind.Trim();
            items = items.Where(i => string.Equals(i.Kind, kind, StringComparison.OrdinalIgnoreCase));
        }

        // 3) Filtro por scope.
        switch (query.Scope)
        {
            case FeedScope.Following when !string.IsNullOrWhiteSpace(query.AuthorId):
                var followees = new HashSet<string>(
                    await _graph.GetFollowingAsync(query.AuthorId!, cancellationToken),
                    StringComparer.Ordinal);
                items = items.Where(i => followees.Contains(i.Author.Id));
                break;

            case FeedScope.Following:
                // "Siguiendo" sin actor de contexto → feed vacío (no hay grafo).
                items = Enumerable.Empty<ContentStreamItem>();
                break;

            case FeedScope.Author when !string.IsNullOrWhiteSpace(query.AuthorId):
                items = items.Where(i =>
                    string.Equals(i.Author.Id, query.AuthorId, StringComparison.OrdinalIgnoreCase));
                break;

            case FeedScope.Author:
                items = Enumerable.Empty<ContentStreamItem>();
                break;

            // ForYou: ranked = cronológico en el stub (todo el conjunto).
        }

        var ordered = items.ToList();

        // 4) Paginación por cursor (offset opaco).
        var offset = DecodeCursor(query.Cursor);
        var pageItems = ordered.Skip(offset).Take(pageSize).ToList();
        var nextOffset = offset + pageItems.Count;
        var nextCursor = nextOffset < ordered.Count ? EncodeCursor(nextOffset) : null;

        return new ContentStreamPage(pageItems, nextCursor);
    }

    public async Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        // Camino rápido: lectura puntual del store + la semilla, que vive en memoria.
        var item = TryParse(await _store.ReadAsync(_resourceType, id, cancellationToken).ConfigureAwait(false))
            ?? SeededItems().FirstOrDefault(i =>
                string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        // El contrato matchea el id case-INsensitive y la clave del store no, así
        // que un id con otro casing todavía debe resolver: recorrido de respaldo.
        item ??= (await LoadCreatedAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            return null;
        }

        var count = await _reactions.CountForAsync(item.Id, cancellationToken).ConfigureAwait(false);
        return WithReactions(item, count);
    }

    public async Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.AuthorId))
        {
            throw new ArgumentException("El autor del item es requerido.", nameof(item));
        }
        if (string.IsNullOrWhiteSpace(item.Body) && string.IsNullOrWhiteSpace(item.MediaUrl))
        {
            throw new ArgumentException("El item requiere cuerpo o media.", nameof(item));
        }

        var id = $"post-{Guid.NewGuid():N}";
        var created = new ContentStreamItem(
            Id: id,
            Kind: string.IsNullOrWhiteSpace(item.Kind) ? "post" : item.Kind.Trim().ToLowerInvariant(),
            Author: SocialDemoSeed.AuthorById(item.AuthorId),
            Body: item.Body?.Trim() ?? string.Empty,
            MediaUrl: string.IsNullOrWhiteSpace(item.MediaUrl) ? null : item.MediaUrl.Trim(),
            CreatedUtc: _utcNow(),
            Metrics: new ContentMetrics(0, 0, 0));

        await _store.WriteAsync(_resourceType, id, JsonSerializer.Serialize(created, _json), cancellationToken)
            .ConfigureAwait(false);
        return created;
    }

    // Semilla + creados, más reciente primero, con métricas frescas de reacciones.
    private async Task<List<ContentStreamItem>> AllItemsAsync(CancellationToken cancellationToken)
    {
        var created = await LoadCreatedAsync(cancellationToken).ConfigureAwait(false);
        // UN recorrido del espacio de reacciones para TODO el feed — no una
        // lectura puntual por item.
        var counts = await _reactions.CountsForAllAsync(cancellationToken).ConfigureAwait(false);

        return SeededItems()
            .Concat(created)
            .OrderByDescending(i => i.CreatedUtc)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => WithReactions(i, counts.TryGetValue(i.Id, out var c) ? c : 0))
            .ToList();
    }

    private static IEnumerable<ContentStreamItem> SeededItems()
        => SocialDemoSeed.Posts.Select(p => new ContentStreamItem(
            Id: p.Id,
            Kind: p.Kind,
            Author: SocialDemoSeed.AuthorById(p.AuthorId),
            Body: p.Body,
            MediaUrl: p.MediaUrl,
            CreatedUtc: SocialDemoSeed.Epoch.AddMinutes(p.OffsetMinutes),
            Metrics: new ContentMetrics()));

    /// <summary>
    /// Los items publicados por usuarios. Un documento ilegible se SALTA — un
    /// post corrupto no puede tumbar el feed entero.
    /// </summary>
    private async Task<List<ContentStreamItem>> LoadCreatedAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        var items = new List<ContentStreamItem>(raws.Count);
        foreach (var json in raws)
        {
            var item = TryParse(json);
            if (item is not null)
            {
                items.Add(item);
            }
        }
        return items;
    }

    private static ContentStreamItem? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var item = JsonSerializer.Deserialize<ContentStreamItem>(json, _json);
            return item is null || string.IsNullOrWhiteSpace(item.Id) ? null : item;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Las métricas de reacción se proyectan en vivo desde el IReactionService —
    // no se duplica el contador en el item (single source of truth).
    private static ContentStreamItem WithReactions(ContentStreamItem item, int reactions)
        => item with { Metrics = item.Metrics with { Reactions = reactions } };

    private static string EncodeCursor(int offset) => $"{CursorPrefix}{offset}";

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || !cursor.StartsWith(CursorPrefix, StringComparison.Ordinal))
        {
            return 0;
        }
        return int.TryParse(cursor.AsSpan(CursorPrefix.Length), out var offset) && offset >= 0 ? offset : 0;
    }
}
