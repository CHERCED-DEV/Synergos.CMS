using Synergos.Core;

namespace Synergos.Api.Geo.Domain;

/// <summary>Un punto en el mapa asociado a algo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Subject">De qué es la ubicación — opaco.</param>
/// <param name="Latitude">Grados decimales, −90 a 90.</param>
/// <param name="Longitude">Grados decimales, −180 a 180.</param>
/// <param name="Label">Un nombre legible: "Laureles, Medellín".</param>
/// <param name="Facets">Zona, ciudad, país… pares de texto, como en el catálogo.</param>
/// <param name="UpdatedAtUtc">Cuándo se fijó.</param>
/// <remarks>
/// <b>Grados decimales y no una cadena de dirección.</b> Una dirección postal no se puede
/// comparar ni medir: "Cra 70 #44-30" y "Carrera 70 No 44-30" son el mismo sitio y ninguna
/// consulta lo sabría. Geocodificar es un servicio externo, y su resultado —el punto— es lo que
/// esta capacidad guarda.
/// </remarks>
public sealed record Place(
    string Id, Ref Subject, double Latitude, double Longitude,
    string? Label, IReadOnlyDictionary<string, string> Facets, DateTimeOffset UpdatedAtUtc);
