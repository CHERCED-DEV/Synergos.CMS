using Synergos.Api.Catalog.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Catalog.Domain;

/// <summary>Compone las reglas de <see cref="CatalogRules"/> con el índice.</summary>
public sealed class CatalogService
{
    private readonly ICatalogStore _store;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public CatalogService(ICatalogStore store, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _store = store;
        _idempotency = idempotency;
        _clock = clock;
    }

    /// <summary>
    /// Indexa, o REINDEXA si ese sujeto ya estaba.
    /// </summary>
    /// <remarks>
    /// Reindexar en vez de rechazar por duplicado es lo correcto para un índice: quien publica un
    /// inmueble y luego le corrige el título no está creando otro inmueble. Rechazarlo obligaría
    /// a cada dominio a llevar la cuenta de qué ya indexó, que es justo el estado que esta
    /// capacidad existe para llevar.
    /// </remarks>
    public Result<CatalogItem> Index(
        Ref subject, string? title, string? summary,
        IReadOnlyList<string>? tags, IReadOnlyDictionary<string, string>? facets, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("item", key) is { } yaEra)
            {
                return _store.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{CatalogRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero el ítem no está.");
            }

            var motivo = CatalogRules.CheckIndexable(title, tags, facets);
            if (motivo is not null) return Result.Rejected<CatalogItem>(motivo);

            var existente = _store.FindBySubject(subject);
            var id = existente?.Id ?? Guid.NewGuid().ToString("n");

            var item = new CatalogItem(
                id, subject, title!.Trim(), summary?.Trim(),
                (tags ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                facets ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                _clock.GetUtcNow());

            _store.Put(item);
            _idempotency.Remember("item", key, id);
            return Result.Ok(item);
        }
    }

    public Result<CatalogItem> Get(string id)
    {
        var item = _store.Find(id);
        return item is null || item.Unindexed
            ? Rejection.NotFound($"{CatalogRules.CodePrefix}.item_not_found", $"No existe el ítem {id}.")
            : Result.Ok(item);
    }

    public Result<CatalogItem> Unindex(string id)
    {
        lock (_gate)
        {
            var item = _store.Find(id);
            if (item is null)
            {
                return Rejection.NotFound($"{CatalogRules.CodePrefix}.item_not_found", $"No existe el ítem {id}.");
            }
            if (item.Unindexed) return Result.Ok(item);   // idempotente

            var retirado = item with { Unindexed = true };
            _store.Put(retirado);
            return Result.Ok(retirado);
        }
    }

    /// <summary>Busca por texto, etiquetas y facetas.</summary>
    public Result<Page<CatalogItem>> Search(
        string? text, IReadOnlyList<string>? tags, IReadOnlyDictionary<string, string>? facets,
        int offset, int limit)
    {
        var motivo = CatalogRules.CheckQuery(text, tags, facets);
        if (motivo is not null) return Result.Rejected<Page<CatalogItem>>(motivo);

        var normalizado = CatalogRules.Normalize(text);

        var candidatos = _store.Indexed()
            // Las etiquetas y facetas son conjunción: pedir dos filtros y recibir lo que cumple
            // uno convierte cada refinamiento en un ensanche, que es lo contrario de filtrar.
            .Where(i => tags is null || tags.All(t =>
                i.Tags.Any(x => CatalogRules.Normalize(x) == CatalogRules.Normalize(t))))
            .Where(i => facets is null || facets.All(f =>
                i.Facets.TryGetValue(f.Key, out var v) && CatalogRules.Normalize(v) == CatalogRules.Normalize(f.Value)))
            .Select(i => (Item: i, Score: CatalogRules.Score(i, normalizado)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Item.IndexedAtUtc)
            // Desempate estable: sin él, dos búsquedas iguales devuelven órdenes distintos y la
            // lista "parpadea" sin que nada haya cambiado.
            .ThenBy(x => x.Item.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<CatalogItem>(
            candidatos.Skip(offset).Take(limit).Select(x => x.Item).ToList(), candidatos.Count, offset));
    }
}
