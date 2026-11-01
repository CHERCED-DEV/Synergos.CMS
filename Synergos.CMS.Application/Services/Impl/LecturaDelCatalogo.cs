using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La lectura genérica del catálogo sobre el motor (014-despiece-tienda, I4): la fuente y el índice del vertical, y su
/// proyección a la forma neutra. Es lo que cada fachada (<see cref="IProductCatalogProvider"/>, la de eventos…) armaba
/// en su forma propia; un vertical la adopta registrándose con su fuente, su índice y su proyección.
/// </summary>
public sealed class LecturaDelCatalogo<T> : ILecturaDelCatalogo
{
    private readonly ICatalogSource<T> _fuente;
    private readonly ICatalogIndex<T> _indice;
    private readonly ProyeccionDelCatalogo<T> _proyeccion;
    private readonly int _maxPorPagina;

    /// <summary>La lectura del <paramref name="vertical"/> sobre su fuente y su índice.</summary>
    /// <param name="maxPorPagina">El tope del índice (<c>CatalogSettings.MaxTake</c>): el listado dice la página que
    /// de verdad devolvió, no la que se pidió.</param>
    public LecturaDelCatalogo(
        string vertical,
        ICatalogSource<T> fuente,
        ICatalogIndex<T> indice,
        ProyeccionDelCatalogo<T> proyeccion,
        int maxPorPagina)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vertical);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPorPagina, 1);
        Vertical = vertical.Trim().ToLowerInvariant();
        _fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
        _indice = indice ?? throw new ArgumentNullException(nameof(indice));
        _proyeccion = proyeccion ?? throw new ArgumentNullException(nameof(proyeccion));
        _maxPorPagina = maxPorPagina;
    }

    /// <inheritdoc />
    public string Vertical { get; }

    /// <inheritdoc />
    public async Task<ListadoDelCatalogo> ListarAsync(
        ConsultaDelCatalogo consulta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consulta);
        var pagina = Math.Max(1, consulta.Pagina);
        var porPagina = Math.Clamp(consulta.PorPagina, 1, _maxPorPagina);
        var desde = (int)Math.Min((long)(pagina - 1) * porPagina, int.MaxValue);
        var filtros = consulta.Filtros
            .Where(f => !string.IsNullOrWhiteSpace(f.Key) && f.Value is { Count: > 0 })
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);

        var elementos = await _fuente.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var resultado = _indice.Search(
            elementos,
            new CatalogQuery(
                Text: consulta.Texto,
                Filters: filtros.Count > 0 ? filtros : null,
                Sort: consulta.Orden,
                Skip: desde,
                Take: porPagina));

        return new ListadoDelCatalogo(
            Elementos: resultado.Items.Select(_proyeccion.Elemento).ToList(),
            Facetas: resultado.Facets.Select(Faceta).ToList(),
            Total: resultado.Total,
            Pagina: pagina,
            PorPagina: porPagina);
    }

    /// <inheritdoc />
    public async Task<FichaDelCatalogo?> FichaAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var elementos = await _fuente.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var elemento = elementos.FirstOrDefault(e =>
            string.Equals(_proyeccion.Id(e), id.Trim(), StringComparison.OrdinalIgnoreCase));
        return elemento is null ? null : _proyeccion.Ficha(elemento);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ElementoDelCatalogo>> ElementosAsync(
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var elementos = await _fuente.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var porId = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var elemento in elementos)
        {
            porId.TryAdd(_proyeccion.Id(elemento), elemento);
        }

        var encontrados = new List<ElementoDelCatalogo>(ids.Count);
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id) && porId.TryGetValue(id.Trim(), out var elemento))
            {
                encontrados.Add(_proyeccion.Elemento(elemento));
            }
        }

        return encontrados;
    }

    private static FacetaDelListado Faceta(CatalogFacet faceta) => new(
        Campo: faceta.Field,
        Titulo: faceta.Label,
        Tipo: faceta.Kind.ToString(),
        Valores: faceta.Values.Select(v => new ValorDeLaFaceta(v.Value, v.Label, v.Count)).ToList());
}

/// <summary>
/// Cómo se ve un elemento del vertical en la forma neutra: su id, su tarjeta y su ficha.
/// </summary>
public sealed record ProyeccionDelCatalogo<T>(
    Func<T, string> Id,
    Func<T, ElementoDelCatalogo> Elemento,
    Func<T, FichaDelCatalogo> Ficha);
