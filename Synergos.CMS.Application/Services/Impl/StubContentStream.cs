using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IContentStream"/> — feed/contenido (dominio Blogs — red
/// social) STUB con un feed sembrado en memoria del proceso. Implementa el
/// stream paginado por cursor (Para ti / Siguiendo / perfil), el detalle de un
/// item y la creación de items nuevos (optimistic insert al top). Compone
/// <see cref="ISocialGraphService"/> (para derivar el feed "Siguiendo") e
/// <see cref="IReactionService"/> (para las métricas de reacciones), reusando
/// los otros seams sin duplicar estado (DIP, ADR 0002).
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
/// ADR 0075 (seam con tests). Singleton — los items creados viven en el proceso.
/// </para>
/// </remarks>
public sealed class StubContentStream : IContentStream
{
    private const string CursorPrefix = "off:";

    private readonly ISocialGraphService _graph;
    private readonly StubReactionService _reactions;
    private readonly Func<DateTime> _utcNow;

    // Items creados en runtime (más reciente primero se logra por CreatedUtc).
    private readonly ConcurrentDictionary<string, ContentStreamItem> _created =
        new(StringComparer.Ordinal);

    public StubContentStream(ISocialGraphService graph, IReactionService reactions)
        : this(graph, reactions, null)
    {
    }

    /// <summary>
    /// Ctor con time source inyectable (<paramref name="utcNow"/>) para
    /// determinismo en tests. El stub depende del <see cref="StubReactionService"/>
    /// concreto para leer los conteos de reacción O(1) (las métricas del feed son
    /// una proyección de las reacciones — no se duplican).
    /// </summary>
    public StubContentStream(ISocialGraphService graph, IReactionService reactions, Func<DateTime>? utcNow)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _reactions = reactions as StubReactionService
            ?? throw new ArgumentException(
                "StubContentStream espera el StubReactionService concreto para proyectar métricas.",
                nameof(reactions));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new FeedQuery();
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        // 1) Conjunto base de items (semilla + creados), más reciente primero.
        IEnumerable<ContentStreamItem> items = AllItems();

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

    public Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<ContentStreamItem?>(null);
        }

        var item = AllItems().FirstOrDefault(i =>
            string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(item);
    }

    public Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
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

        _created[id] = created;
        return Task.FromResult(created);
    }

    // Semilla + creados, más reciente primero, con métricas frescas de reacciones.
    private IEnumerable<ContentStreamItem> AllItems()
    {
        var seeded = SocialDemoSeed.Posts.Select(p => new ContentStreamItem(
            Id: p.Id,
            Kind: p.Kind,
            Author: SocialDemoSeed.AuthorById(p.AuthorId),
            Body: p.Body,
            MediaUrl: p.MediaUrl,
            CreatedUtc: SocialDemoSeed.Epoch.AddMinutes(p.OffsetMinutes),
            Metrics: new ContentMetrics()));

        return seeded
            .Concat(_created.Values)
            .OrderByDescending(i => i.CreatedUtc)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(WithLiveMetrics);
    }

    // Las métricas de reacción se proyectan en vivo desde el IReactionService —
    // no se duplica el contador en el item (single source of truth).
    private ContentStreamItem WithLiveMetrics(ContentStreamItem item)
        => item with { Metrics = item.Metrics with { Reactions = _reactions.CountFor(item.Id) } };

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
