using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Un filtro/faceta de un catálogo: sabe si un ítem pasa la selección del usuario y sabe
/// derivar sus propios chips con conteo. Cada <see cref="CatalogFacetKind"/> tiene su
/// subclase; el motor no hace <c>switch</c> sobre el Kind.
/// </summary>
/// <remarks>
/// <b>Por qué polimorfismo y no un <c>switch (Kind)</c> en el motor:</b> las tres semánticas
/// (término, umbral, rango) difieren tanto en el match como en cómo derivan sus chips —un
/// término los descubre del dato, un umbral los tiene declarados—. Con un switch, el motor
/// crecería con cada Kind nuevo y el descriptor dejaría de ser declaración. Así, añadir un
/// Kind es una subclase y el motor no se toca. Es la línea que el plan marca en rojo: <i>si
/// el motor se toca para acomodar un vertical, el descriptor está mal modelado</i>.
/// </remarks>
/// <typeparam name="T">El tipo de ítem del catálogo.</typeparam>
public abstract class CatalogFilter<T>
{
    protected CatalogFilter(string field, string label, CatalogFacetKind kind)
    {
        Field = field;
        Label = label;
        Kind = kind;
    }

    /// <summary>El nombre canónico que viaja en <see cref="CatalogQuery.Filters"/>.</summary>
    public string Field { get; }

    /// <summary>El título de la columna de chips. Editor-facing.</summary>
    public string Label { get; }

    /// <summary>Cómo se comporta al filtrar.</summary>
    public CatalogFacetKind Kind { get; }

    /// <summary>
    /// ¿<paramref name="item"/> pasa esta selección? <paramref name="selected"/> nunca llega
    /// vacío (el motor no llama a un filtro sin selección).
    /// </summary>
    public abstract bool Matches(T item, IReadOnlyList<string> selected);

    /// <summary>
    /// Los chips de esta faceta con su conteo, derivados de <paramref name="universe"/>.
    /// </summary>
    public abstract IReadOnlyList<CatalogFacetValue> Facets(IReadOnlyList<T> universe);
}

/// <summary>
/// Filtro por término: el ítem pasa si alguno de sus valores coincide con alguno de los
/// seleccionados (OR). Los chips se descubren del propio dato.
/// </summary>
/// <remarks>
/// El selector devuelve una LISTA y no un string porque hay campos legítimamente
/// multi-valor (tags, amenities). Un campo simple devuelve una lista de uno.
/// </remarks>
public sealed class CatalogTermFilter<T> : CatalogFilter<T>
{
    private readonly Func<T, IReadOnlyList<string>> _valuesOf;

    public CatalogTermFilter(
        string field,
        string label,
        Func<T, IReadOnlyList<string>> valuesOf,
        CatalogFacetKind kind = CatalogFacetKind.MultiSelect)
        : base(field, label, kind)
    {
        _valuesOf = valuesOf;
    }

    public override bool Matches(T item, IReadOnlyList<string> selected)
    {
        var values = _valuesOf(item);
        for (var i = 0; i < values.Count; i++)
        {
            for (var j = 0; j < selected.Count; j++)
            {
                // Plegado en ambos lados: el chip puede venir de la URL sin tildes y el dato
                // tenerlas ("tecnologia" debe casar con "Tecnología").
                if (CatalogText.AreEqual(values[i], selected[j]))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public override IReadOnlyList<CatalogFacetValue> Facets(IReadOnlyList<T> universe)
    {
        // Agrupa por la forma PLEGADA para no partir "Tecnología" y "tecnologia" en dos
        // chips, pero muestra la primera grafía vista: el usuario lee el dato como se autoró.
        var groups = new Dictionary<string, (string Display, int Count)>(StringComparer.Ordinal);

        foreach (var item in universe)
        {
            // Un ítem cuenta UNA vez por chip, aunque tenga dos valores que plieguen igual
            // (tags ["Bogotá","bogota"]). Contar ocurrencias haría que el chip prometiera
            // más ítems de los que el filtro devuelve al pulsarlo.
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var raw in _valuesOf(item))
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;   // un valor vacío no es una faceta, es un dato que falta
                }
                var key = CatalogText.Fold(raw);
                if (!seen.Add(key))
                {
                    continue;
                }
                if (groups.TryGetValue(key, out var existing))
                {
                    groups[key] = (existing.Display, existing.Count + 1);
                }
                else
                {
                    groups[key] = (raw.Trim(), 1);
                }
            }
        }

        return groups.Values
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Display, StringComparer.OrdinalIgnoreCase)   // estable entre requests
            .Select(g => new CatalogFacetValue(g.Display, g.Display, g.Count))
            .ToList();
    }
}

/// <summary>
/// Filtro por umbral: el valor seleccionado es un mínimo, no una coincidencia
/// (<c>minRating=4.0</c> = "4 o más"). Los chips están DECLARADOS, no se descubren.
/// </summary>
/// <remarks>
/// <b>Con varios valores gana el MENOS restrictivo</b> (encender "4+" y "3+" significa
/// "3+"), y esa decisión es deliberada: es lo que el usuario ve al tener los dos chips
/// encendidos. El código anterior hacía <c>double.TryParse("4.0,4.5")</c>, que devuelve
/// false, y entonces el filtro no se aplicaba EN SILENCIO — los dos chips encendidos y el
/// listado sin filtrar.
///
/// <para>Los chips se declaran porque derivarlos del dato daría un chip por cada rating
/// distinto (4.3, 4.7, 4.8…) en vez de los cuatro tramos que el usuario espera.</para>
/// </remarks>
public sealed class CatalogThresholdFilter<T> : CatalogFilter<T>
{
    private readonly Func<T, double> _numberOf;
    private readonly IReadOnlyList<CatalogThresholdBucket> _buckets;

    public CatalogThresholdFilter(
        string field,
        string label,
        Func<T, double> numberOf,
        IReadOnlyList<CatalogThresholdBucket> buckets)
        : base(field, label, CatalogFacetKind.Threshold)
    {
        _numberOf = numberOf;
        _buckets = buckets;
    }

    public override bool Matches(T item, IReadOnlyList<string> selected)
    {
        var floor = LowestOf(selected);
        return floor is null || _numberOf(item) >= floor.Value;
    }

    /// <summary>
    /// El umbral MENOS restrictivo de los seleccionados, o null si ninguno parsea (y
    /// entonces el filtro no aplica: un valor basura no debe vaciar el listado).
    /// </summary>
    /// <remarks>
    /// <c>NaN</c> e <c>Infinity</c> se descartan explícitamente: <c>NumberStyles.Float</c>
    /// los PARSEA, así que sin esta guarda <c>?minRating=NaN</c> pasaría por "umbral válido"
    /// y vaciaría el listado —cualquier comparación con NaN es false—, que es justo lo que
    /// esta función promete que la basura no haga.
    /// </remarks>
    private static double? LowestOf(IReadOnlyList<string> selected)
    {
        double? lowest = null;
        foreach (var raw in selected)
        {
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                && !double.IsNaN(parsed)
                && !double.IsInfinity(parsed)
                && (lowest is null || parsed < lowest))
            {
                lowest = parsed;
            }
        }
        return lowest;
    }

    public override IReadOnlyList<CatalogFacetValue> Facets(IReadOnlyList<T> universe)
        => _buckets
            .Select(b => new CatalogFacetValue(
                b.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                b.Label,
                universe.Count(i => _numberOf(i) >= b.Value)))
            .ToList();
}

/// <summary>
/// Un tramo declarado de un <see cref="CatalogThresholdFilter{T}"/>: el mínimo + cómo se lee.
/// </summary>
/// <param name="Value">El umbral (4.0). Es lo que viaja en la query.</param>
/// <param name="Label">Cómo se lee el chip ("4 estrellas o más"). Sin esto se pinta "4.0".</param>
public sealed record CatalogThresholdBucket(double Value, string Label);

/// <summary>
/// Filtro por rango numérico: el ítem pasa si su valor cae dentro de alguno de los rangos
/// seleccionados. El valor viaja como <c>"min-max"</c>, con cualquiera de los dos extremos
/// opcional (<c>"1000-"</c> = de 1000 en adelante).
/// </summary>
public sealed class CatalogRangeFilter<T> : CatalogFilter<T>
{
    private readonly Func<T, double> _numberOf;
    private readonly IReadOnlyList<CatalogRangeBucket> _buckets;

    public CatalogRangeFilter(
        string field,
        string label,
        Func<T, double> numberOf,
        IReadOnlyList<CatalogRangeBucket> buckets)
        : base(field, label, CatalogFacetKind.Range)
    {
        _numberOf = numberOf;
        _buckets = buckets;
    }

    public override bool Matches(T item, IReadOnlyList<string> selected)
    {
        var value = _numberOf(item);
        var anyParsed = false;

        // OR entre rangos: encender "0-100" y "500-1000" es pedir la unión, no la
        // intersección (que sería siempre vacía).
        foreach (var raw in selected)
        {
            if (!TryParseRange(raw, out var min, out var max))
            {
                continue;
            }
            anyParsed = true;
            if ((min is null || value >= min) && (max is null || value <= max))
            {
                return true;
            }
        }

        // Si NADA parseó, el filtro no aplica (no se vacía el listado por un valor basura);
        // si algo parseó y no casó, el ítem queda fuera de verdad.
        return !anyParsed;
    }

    /// <summary>"1000-5000" → (1000, 5000). "1000-" → (1000, null). "-5000" → (null, 5000).</summary>
    internal static bool TryParseRange(string? raw, out double? min, out double? max)
    {
        min = null;
        max = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split('-');
        if (parts.Length != 2)
        {
            return false;   // ni "1000" suelto ni "1-2-3" son un rango
        }

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var style = System.Globalization.NumberStyles.Float;

        // NaN/Infinity los parsea NumberStyles.Float: se rechazan aquí o un "NaN-5000"
        // pasaría por rango válido y descartaría todo en silencio.
        if (!string.IsNullOrWhiteSpace(parts[0]))
        {
            if (!double.TryParse(parts[0], style, culture, out var parsedMin)
                || double.IsNaN(parsedMin) || double.IsInfinity(parsedMin))
            {
                return false;
            }
            min = parsedMin;
        }
        if (!string.IsNullOrWhiteSpace(parts[1]))
        {
            if (!double.TryParse(parts[1], style, culture, out var parsedMax)
                || double.IsNaN(parsedMax) || double.IsInfinity(parsedMax))
            {
                return false;
            }
            max = parsedMax;
        }

        // "-" pelado no es un rango; y un rango invertido es un error del llamador, no
        // "todo": devolverlo como válido serviría el catálogo entero en silencio.
        if (min is null && max is null)
        {
            return false;
        }
        return min is null || max is null || min <= max;
    }

    public override IReadOnlyList<CatalogFacetValue> Facets(IReadOnlyList<T> universe)
        => _buckets
            .Select(b => new CatalogFacetValue(
                b.Value,
                b.Label,
                universe.Count(i =>
                {
                    if (!TryParseRange(b.Value, out var min, out var max))
                    {
                        return false;
                    }
                    var v = _numberOf(i);
                    return (min is null || v >= min) && (max is null || v <= max);
                })))
            .ToList();
}

/// <summary>Un tramo declarado de un <see cref="CatalogRangeFilter{T}"/>.</summary>
/// <param name="Value">El rango en forma de wire ("100000-500000").</param>
/// <param name="Label">Cómo se lee ("Entre $100.000 y $500.000").</param>
public sealed record CatalogRangeBucket(string Value, string Label);
