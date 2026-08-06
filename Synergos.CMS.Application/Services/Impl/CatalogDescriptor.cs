namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Lo que un vertical DECLARA sobre su catálogo: en qué campos se busca y con qué peso, qué
/// facetas tiene, cómo se puede ordenar y cuál es su orden por defecto. Cero lógica: es
/// declaración, y por eso los 5 descriptores caben en ~25 líneas cada uno.
/// </summary>
/// <remarks>
/// <b>La línea que separa esto del motor:</b> el motor sabe BUSCAR; el descriptor sabe qué
/// significa buscar EN ESTE vertical. Si algún día hay que tocar el motor para acomodar un
/// vertical, el descriptor está mal modelado — pararse a pensar, no parchear.
/// </remarks>
/// <typeparam name="T">El tipo de ítem del catálogo.</typeparam>
public sealed class CatalogDescriptor<T>
{
    public CatalogDescriptor(
        Func<T, string> idOf,
        IReadOnlyList<CatalogSearchField<T>> searchFields,
        Func<IEnumerable<T>, IOrderedEnumerable<T>> defaultOrder,
        IReadOnlyList<CatalogFilter<T>>? filters = null,
        IReadOnlyDictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>>? sorts = null)
    {
        IdOf = idOf;
        SearchFields = searchFields;
        DefaultOrder = defaultOrder;
        Filters = filters ?? Array.Empty<CatalogFilter<T>>();
        Sorts = sorts ?? new Dictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>>();
    }

    /// <summary>
    /// El id del ítem. Lo usa el motor como ÚLTIMO desempate del orden.
    /// </summary>
    /// <remarks>
    /// No es un detalle: sin un desempate total, dos ítems con la misma relevancia (o el
    /// mismo rating) pueden salir en orden distinto entre dos requests idénticos, y entonces
    /// la paginación baila — un ítem aparece en la página 1 y en la 2, y otro en ninguna.
    /// </remarks>
    public Func<T, string> IdOf { get; }

    /// <summary>Los campos sobre los que corre el texto libre, con su peso.</summary>
    public IReadOnlyList<CatalogSearchField<T>> SearchFields { get; }

    /// <summary>
    /// El orden cuando no hay texto ni <c>?sort=</c>. <b>Es el orden HISTÓRICO de cada
    /// vertical</b>, declarado aquí para que la demo no cambie de aspecto al montar el motor:
    /// Eventos por fecha, Propiedades por destacado+precio, Trámites por nombre, Educación
    /// por rating.
    /// </summary>
    public Func<IEnumerable<T>, IOrderedEnumerable<T>> DefaultOrder { get; }

    /// <summary>Las facetas del vertical.</summary>
    public IReadOnlyList<CatalogFilter<T>> Filters { get; }

    /// <summary>Los órdenes que el vertical ofrece, por clave de wire ("price-asc"…).</summary>
    public IReadOnlyDictionary<string, Func<IEnumerable<T>, IOrderedEnumerable<T>>> Sorts { get; }
}

/// <summary>
/// Un campo buscable + cuánto pesa en la relevancia. Peso alto = casar aquí importa más
/// (el nombre del producto pesa más que su descripción).
/// </summary>
/// <param name="Weight">Multiplicador de la puntuación. Relativo a los demás campos, no absoluto.</param>
/// <param name="TextOf">De dónde sale el texto del ítem para este campo.</param>
public sealed record CatalogSearchField<T>(int Weight, Func<T, string?> TextOf);
