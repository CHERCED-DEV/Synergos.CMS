using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El motor de catálogo: barrido lineal en memoria, ranking ponderado y facetas drill-down.
/// UNA impl para los 5 verticales; el comportamiento de cada uno entra por su
/// <see cref="CatalogDescriptor{T}"/>.
/// </summary>
/// <remarks>
/// <b>Por qué en memoria y no Lucene</b> (ADR 0107, decidido con datos): a ~24 ítems por
/// vertical un índice invertido no compra rendimiento —el propio <c>IShopQuery.cs:14</c>
/// pone el umbral en "10k+"— y sí cuesta cuatro modos de fallo (índice ausente = PLP vacía
/// en demo, read-your-writes roto en los 3 <c>Publish*</c>, orden de precio lexicográfico
/// porque <c>productPriceBase</c> es TextBox, y mezcla cross-vertical porque tres verticales
/// comparten <c>productPage</c>). Examine 3.7.1 además no tiene facetado first-class, así
/// que el conteo habría que escribirlo aquí igual. Reabrir si &gt;5.000 ítems/vertical o
/// p95 &gt;50ms.
///
/// <para>Sin estado: el motor es una función pura de (ítems, query). Es Singleton-safe por
/// construcción, no por disciplina.</para>
/// </remarks>
public sealed class InMemoryCatalogIndex<T> : ICatalogIndex<T>
{
    private readonly CatalogDescriptor<T> _descriptor;
    private readonly int _maxTake;
    private readonly int _facetUniverseHardCap;

    public InMemoryCatalogIndex(CatalogDescriptor<T> descriptor, CatalogSettings settings)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        ArgumentNullException.ThrowIfNull(settings);

        // Los topes se sanean UNA vez, aquí, en vez de confiar en el appsettings: un
        // MaxTake de 0 haría que Math.Clamp(take, 1, 0) lanzara en CADA búsqueda de los 5
        // verticales, y un FacetUniverseHardCap de 0 vaciaría todas las facetas en silencio.
        // Un tope absurdo es un error de config, y degradar al default es mejor que tumbar
        // la búsqueda del sitio entero.
        _maxTake = settings.MaxTake > 0 ? settings.MaxTake : 96;
        _facetUniverseHardCap = settings.FacetUniverseHardCap > 0 ? settings.FacetUniverseHardCap : 5_000;
    }

    public CatalogResult<T> Search(IReadOnlyList<T> items, CatalogQuery query)
    {
        ArgumentNullException.ThrowIfNull(items);
        query ??= new CatalogQuery();

        var selections = Normalize(query.Filters);

        // 1) Filtrar por facetas.
        var filtered = items.Where(i => PassesAll(i, selections, skipField: null)).ToList();

        // 2) Texto libre: filtra Y puntúa en la misma pasada.
        var matched = query.Text is { Length: > 0 } && !string.IsNullOrWhiteSpace(query.Text)
            ? Score(filtered, query.Text)
            : filtered.Select(i => (Item: i, Score: 0)).ToList();

        var total = matched.Count;

        // 3) Orden. Con texto manda la relevancia; sin texto, el orden histórico del
        //    vertical (o el ?sort= pedido). Siempre con desempate total por id.
        var ordered = Order(matched, query);

        // 4) Paginar. Los topes se capan, no se rechazan: un ?take=1000000 es un cliente
        //    torpe, no un ataque, y responder la página máxima es mejor que un 400.
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, _maxTake);
        var page = ordered.Skip(skip).Take(take).ToList();

        // 5) Facetas: sobre el universo filtrado COMPLETO, nunca sobre la página.
        var facets = BuildFacets(items, selections, query.Text);

        return new CatalogResult<T>(page, facets, total);
    }

    /// <summary>
    /// Las facetas con su conteo. <b>Dos decisiones que no son detalle:</b>
    /// </summary>
    /// <remarks>
    /// <b>(a) Drill-down:</b> cada faceta se cuenta sobre el universo filtrado por TODAS las
    /// demás menos por ella misma. Si se contara incluyéndose, al encender "Aurora" la
    /// columna Marca colapsaría a un solo chip ("Aurora (1)") y el usuario no podría añadir
    /// "Barista" — que es justo lo que un multi-select promete. Excluirse a sí misma es lo
    /// que mantiene los hermanos visibles y con su conteo real.
    ///
    /// <para><b>(b) Sobre el universo filtrado, no sobre la página:</b> los conteos
    /// describen cuántos hay en total, no cuántos caben en 24. Con 30 ítems y take=5, el
    /// chip debe decir 30. Esto es exactamente lo que Lucene no da sin materializar todo.</para>
    ///
    /// <para><b>Salvo por encima de <c>FacetUniverseHardCap</c>:</b> ahí se trunca y los
    /// conteos pasan a ser un "al menos N". Es un régimen que hoy no se alcanza —el tope son
    /// 5.000 ítems y los verticales tienen ~24—, y de alcanzarse, el ADR 0107 ya manda
    /// reabrir la discusión del motor en ese mismo umbral.</para>
    /// </remarks>
    private IReadOnlyList<CatalogFacet> BuildFacets(
        IReadOnlyList<T> all,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections,
        string? text)
    {
        if (_descriptor.Filters.Count == 0)
        {
            return Array.Empty<CatalogFacet>();
        }

        var facets = new List<CatalogFacet>(_descriptor.Filters.Count);

        foreach (var filter in _descriptor.Filters)
        {
            // El texto SÍ acota el universo de las facetas: buscar "laptop" y ver
            // "Marca: Barista (3)" sería mentira — no hay 3 laptops Barista.
            var universe = all.Where(i => PassesAll(i, selections, skipField: filter.Field));
            if (!string.IsNullOrWhiteSpace(text))
            {
                universe = universe.Where(i => Matches(i, text));
            }

            var capped = universe.Take(_facetUniverseHardCap).ToList();
            var values = filter.Facets(capped);

            // Una faceta SIN valores no se emite: una columna con título y ningún chip no es
            // información, es ruido. Pasa de verdad — al mudar la Tienda a contenido, el
            // rating es UGC derivado y vale 0 en todo, así que "Calificación" salía como una
            // columna muerta.
            //
            // Y NO deja al usuario sin salida cuando filtra a cero: lo que le da la salida es
            // el DRILL-DOWN (cada faceta se cuenta excluyéndose a sí misma), así que la
            // columna por la que filtró SIGUE trayendo sus hermanas con conteo. Si una faceta
            // llega vacía es porque de verdad no hay nada que ofrecer en ella.
            if (values.Count > 0)
            {
                facets.Add(new CatalogFacet(filter.Field, filter.Label, filter.Kind, values));
            }
        }

        return facets;
    }

    /// <summary>
    /// ¿El ítem pasa todas las facetas seleccionadas, saltándose <paramref name="skipField"/>?
    /// AND entre facetas distintas; el OR dentro de una lo hace cada filtro.
    /// </summary>
    private bool PassesAll(
        T item,
        IReadOnlyDictionary<string, IReadOnlyList<string>> selections,
        string? skipField)
    {
        foreach (var filter in _descriptor.Filters)
        {
            if (skipField is not null && string.Equals(filter.Field, skipField, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (selections.TryGetValue(filter.Field, out var selected)
                && selected.Count > 0
                && !filter.Matches(item, selected))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Normaliza las selecciones que llegan del wire: quita claves desconocidas, valores
    /// vacíos y facetas que quedaron sin valores.
    /// </summary>
    /// <remarks>
    /// <b>Una clave desconocida se IGNORA, no vacía el resultado</b> (un <c>?color=rojo</c>
    /// sobre un vertical sin faceta de color devuelve el catálogo, no cero). Es la decisión
    /// menos sorprendente para un enlace viejo o un typo — y la que evita que un parámetro
    /// de tracking pegado a la URL rompa la página.
    /// </remarks>
    private IReadOnlyDictionary<string, IReadOnlyList<string>> Normalize(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? raw)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (raw is null || raw.Count == 0)
        {
            return result;
        }

        foreach (var filter in _descriptor.Filters)
        {
            if (!raw.TryGetValue(filter.Field, out var values) || values is null)
            {
                continue;
            }
            var clean = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
            if (clean.Count > 0)
            {
                result[filter.Field] = clean;
            }
        }
        return result;
    }

    /// <summary>¿Algún campo buscable del ítem casa con el texto?</summary>
    private bool Matches(T item, string text) => ScoreOf(item, text) > 0;

    private List<(T Item, int Score)> Score(IReadOnlyList<T> items, string text)
    {
        var scored = new List<(T Item, int Score)>(items.Count);
        foreach (var item in items)
        {
            var score = ScoreOf(item, text);
            if (score > 0)
            {
                scored.Add((item, score));
            }
        }
        return scored;
    }

    /// <summary>
    /// Cuánto casa un ítem con el texto. 0 = no casa (y queda fuera).
    /// </summary>
    /// <remarks>
    /// <b>AND entre tokens:</b> escribir "laptop pro" pide los que tienen AMBOS, no la unión
    /// —el OR de Lucene devolvería medio catálogo por el token común—. Cada token puntúa
    /// exacto = peso×3 y prefijo = peso×1, de modo que "zapa" encuentra "zapatos" (ni
    /// <c>.Contains</c> ni el <c>GroupedOr</c> de Examine dan esto) pero un nombre que casa
    /// entero gana. Bonus por la frase completa en el campo de más peso, para que "laptop
    /// pro" ponga arriba al que se llama exactamente así.
    /// </remarks>
    private int ScoreOf(T item, string text)
    {
        var tokens = CatalogText.Tokenize(text);
        if (tokens.Count == 0)
        {
            return 0;
        }

        var total = 0;
        foreach (var token in tokens)
        {
            var best = 0;
            foreach (var field in _descriptor.SearchFields)
            {
                var hit = CatalogText.ScoreToken(field.TextOf(item), token);
                if (hit > 0)
                {
                    best = Math.Max(best, field.Weight * hit);
                }
            }
            if (best == 0)
            {
                return 0;   // AND: un token sin casar descarta el ítem
            }
            total += best;
        }

        // Bonus de frase: el campo declarado con más peso conteniendo el texto entero.
        var top = _descriptor.SearchFields.OrderByDescending(f => f.Weight).FirstOrDefault();
        if (top is not null && CatalogText.Contains(top.TextOf(item), text))
        {
            total += 10;
        }

        return total;
    }

    /// <summary>
    /// Ordena. Con texto manda la relevancia; sin texto, el <c>?sort=</c> pedido o el orden
    /// histórico del vertical. Siempre cierra con el id, para que el orden sea TOTAL.
    /// </summary>
    /// <remarks>
    /// Un <c>?sort=</c> desconocido cae al orden por defecto en vez de fallar: es un enlace
    /// viejo, no un error que merezca un 400.
    /// </remarks>
    private IEnumerable<T> Order(List<(T Item, int Score)> matched, CatalogQuery query)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Text);

        if (hasText && string.IsNullOrWhiteSpace(query.Sort))
        {
            return matched
                .OrderByDescending(m => m.Score)
                .ThenBy(m => _descriptor.IdOf(m.Item), StringComparer.Ordinal)
                .Select(m => m.Item);
        }

        var items = matched.Select(m => m.Item);

        if (!string.IsNullOrWhiteSpace(query.Sort)
            && _descriptor.Sorts.TryGetValue(query.Sort, out var sort))
        {
            return sort(items).ThenBy(_descriptor.IdOf, StringComparer.Ordinal);
        }

        return _descriptor.DefaultOrder(items).ThenBy(_descriptor.IdOf, StringComparer.Ordinal);
    }
}
