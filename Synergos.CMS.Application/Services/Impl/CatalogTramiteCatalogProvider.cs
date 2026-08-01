using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de trámites servido desde el CONTENIDO que el editor de la entidad autoró. Es el
/// <see cref="ITramiteCatalogProvider"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Gov = cms</c>.
/// </summary>
/// <remarks>
/// <b>Es la versión SIMPLE de la forma de dos capas</b>, igual que
/// <see cref="CatalogStayContentProvider"/> y a diferencia de
/// <see cref="CatalogPropertyCatalogProvider"/> / <see cref="CatalogEventCatalogProvider"/>: una
/// sola capa, la del contenido. <see cref="ITramiteCatalogProvider"/> es de SOLO LECTURA
/// —<c>SearchAsync</c> y <c>GetAsync</c>, y nada más—, así que no existe un segundo autor que
/// publique trámites por fuera del backoffice y no hay nada que fusionar.
///
/// <para>Prescindir del <c>IJsonEntityStore</c> aquí no es una omisión: un overlay durable sin
/// escritor sería exactamente el campo que promete algo que nadie cumple, y el siguiente que lo
/// viera confiaría en él. Lo que sí es durable en Gobierno son los EXPEDIENTES
/// (<c>gov-cases</c>), que son de otro agregado y ya tienen su capa.</para>
///
/// <para><b>La búsqueda se calca del stub, no se reinventa</b>: mismo descriptor —nombre 5,
/// entidad 3, categoría 2, resumen 1—, mismo motor, misma faceta única por categoría y el mismo
/// <c>Take</c> explícito, porque esta seam promete TODAS las coincidencias y el tope por defecto
/// del motor (24) las truncaría en silencio. Si el buscador se comportara distinto según de
/// dónde salen los trámites, sería una regresión invisible que solo aparecería al mover el
/// flag.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002).</para>
/// </remarks>
public sealed class CatalogTramiteCatalogProvider : ITramiteCatalogProvider
{
    private readonly ICatalogSource<TramiteDetail> _source;
    private readonly ICatalogIndex<TramiteSummary> _index;

    public CatalogTramiteCatalogProvider(ICatalogSource<TramiteDetail> source)
        : this(source, new InMemoryCatalogIndex<TramiteSummary>(
            StubTramiteCatalogProvider.Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal CatalogTramiteCatalogProvider(
        ICatalogSource<TramiteDetail> source,
        ICatalogIndex<TramiteSummary> index)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public async Task<IReadOnlyList<TramiteSummary>> SearchAsync(
        string? query,
        string? category,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        var filters = string.IsNullOrWhiteSpace(category)
            ? null
            : new Dictionary<string, IReadOnlyList<string>> { ["category"] = new[] { category.Trim() } };

        var result = _index.Search(
            catalog.Select(d => d.Summary).ToList(),
            new CatalogQuery(Text: query, Filters: filters, Take: int.MaxValue));

        return result.Items;
    }

    public async Task<TramiteDetail?> GetAsync(string tramiteId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tramiteId))
        {
            return null;
        }

        var id = tramiteId.Trim();
        var catalog = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        return catalog.FirstOrDefault(d =>
            string.Equals(d.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));
    }
}
