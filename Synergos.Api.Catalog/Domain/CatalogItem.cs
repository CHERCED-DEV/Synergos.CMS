using Synergos.Core;

namespace Synergos.Api.Catalog.Domain;

/// <summary>
/// Algo indexado y buscable.
/// </summary>
/// <param name="Id">Identificador dentro de esta capacidad.</param>
/// <param name="Subject">Qué es, en el vocabulario de quien lo indexó. Opaco.</param>
/// <param name="Title">Lo que se busca y se muestra.</param>
/// <param name="Summary">Texto secundario, también buscable.</param>
/// <param name="Tags">Etiquetas exactas.</param>
/// <param name="Facets">Pares para filtrar — <c>ciudad=medellin</c>, <c>modalidad=virtual</c>.</param>
/// <param name="IndexedAtUtc">Cuándo entró o se actualizó.</param>
/// <param name="Unindexed">Si se retiró del índice.</param>
/// <remarks>
/// <para><b>Las facetas son pares de texto y no un esquema por dominio.</b> Es lo que permite que
/// el mismo índice sirva a inmuebles (<c>barrio</c>, <c>habitaciones</c>), a cursos
/// (<c>nivel</c>, <c>modalidad</c>) y a trámites (<c>entidad</c>). Un esquema tipado por dominio
/// habría obligado a esta capacidad a conocerlos — y ahí deja de servir al siguiente.</para>
///
/// <para><b>Y lo que NO lleva: precio, existencias, disponibilidad.</b> Son de <c>Pricing</c>,
/// <c>Inventory</c> y <c>Booking</c>, y copiarlos acá crearía dos verdades que se desincronizan
/// — con el catálogo mostrando un precio que la caja no cobra.</para>
/// </remarks>
public sealed record CatalogItem(
    string Id,
    Ref Subject,
    string Title,
    string? Summary,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Facets,
    DateTimeOffset IndexedAtUtc,
    bool Unindexed = false);
