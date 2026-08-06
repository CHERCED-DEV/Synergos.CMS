namespace Synergos.CMS.Interfaces;

/// <summary>Coordenadas de una estadía — alimentan el mapa de resultados (SH-8).</summary>
public sealed record StayGeo(double Lat, double Lng);

/// <summary>Una spec de la ficha de estadía (label es-CO + valor), e.g. "Check-in" → "3:00 p. m.".</summary>
public sealed record StaySpec(string Label, string Value);

/// <summary>Puntaje 0-10 de una categoría de reviews (Limpieza/Ubicación/Servicio…).</summary>
public sealed record StayReviewCategoryScore(string Category, double Score);

/// <summary>
/// Resumen de reviews de la estadía: promedio 0-10 + volumen + highlight
/// human-facing + desglose por categoría. Es el bloque social-proof de la
/// ficha (estilo Booking/Airbnb), NO el listado de reviews individuales.
/// </summary>
public sealed record StayReviewSummary(
    double Average,
    int Count,
    string Highlight,
    IReadOnlyList<StayReviewCategoryScore> Categories);

/// <summary>
/// Ficha rica de una estadía (propiedad hotelera) para la cara viajera:
/// galería + amenities + descripción + specs + geo + resumen de reviews.
/// <see cref="RoomTypeCodes"/> liga la estadía con las ofertas del search de
/// hotel (<see cref="RoomOffer.RoomTypeCode"/>) para que cada oferta herede
/// las coordenadas de su propiedad en el mapa.
/// </summary>
public sealed record StayDetail(
    string StayId,
    string Name,
    string City,
    string Region,
    string Address,
    string Description,
    IReadOnlyList<string> Gallery,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<StaySpec> Specs,
    StayGeo Geo,
    StayReviewSummary Reviews,
    IReadOnlyList<string> RoomTypeCodes);

/// <summary>
/// Contenido rico de estadías del dominio Booking (OLA 2 — doc 21 §2.2):
/// resuelve la ficha completa de una propiedad (galería/amenities/specs/geo/
/// reviews) por stayId, por room type code o por offerId del search. Separa
/// el CONTENIDO de la estadía (esta seam) de la DISPONIBILIDAD/precio
/// (<see cref="IRoomAvailabilityProvider"/>), que sigue intacta.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que el resto del motor): el default
/// <c>StubStayContentProvider</c> (Application, lógica pura/determinista)
/// siembra estadías realistas CO (Cartagena/Medellín/Bogotá/Eje Cafetero) con
/// geo real; el adapter real (CMS content / channel-manager) se enchufa sin
/// tocar el motor. ADR 0002 (Application sin Umbraco) + ADR 0075 (tests).
/// </remarks>
public interface IStayContentProvider
{
    /// <summary>
    /// Devuelve la ficha de la estadía por referencia, o null si no existe.
    /// Acepta el stayId ("ctg-getsemani"), un room type code ("DLX") o el
    /// offerId del search ("DLX/DLX-FLEX" — se resuelve por el room type).
    /// </summary>
    Task<StayDetail?> GetStayAsync(string stayRef, CancellationToken cancellationToken = default);
}
