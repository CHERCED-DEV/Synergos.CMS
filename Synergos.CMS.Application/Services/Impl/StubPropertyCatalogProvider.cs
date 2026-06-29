using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPropertyCatalogProvider"/> — catálogo STUB del portal
/// inmobiliario (doc propiedades-app-spec), calcando <c>StubEventCatalogProvider</c>
/// / <c>StubProductCatalogProvider</c>. Sirve un catálogo sembrado en memoria con
/// geo realista (varias ciudades CO × tipos apto/casa/oficina/local) para que el
/// search facetado + el mapa corran end-to-end en demo.
/// </summary>
/// <remarks>
/// Lógica pura/determinista en <c>Synergos.CMS.Application</c> — cero dependencia
/// de Umbraco/AspNetCore (ADR 0002). El search aplica todos los filtros de
/// <see cref="PropertyQuery"/> en AND y deriva las facetas (conteos por tipo/
/// operación/ciudad) del universo filtrado. El adapter real (Examine sobre
/// <c>propertyListing</c> o una API MLS) implementa la misma seam y se registra en
/// su lugar vía el composer sin tocar el motor. ADR 0075.
/// </remarks>
public sealed class StubPropertyCatalogProvider : IPropertyCatalogProvider
{
    private const string Cop = "COP";

    private readonly IReadOnlyList<PropertyDetail> _catalog = Seed();

    public Task<PropertySearchResult> SearchAsync(PropertyQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new PropertyQuery();

        var matches = _catalog
            .Select(d => d.Summary)
            .Where(l => MatchesText(l, query.Text))
            .Where(l => query.Type is null || string.Equals(l.Type, query.Type, StringComparison.OrdinalIgnoreCase))
            .Where(l => query.MinPrice is null || l.Price >= query.MinPrice.Value)
            .Where(l => query.MaxPrice is null || l.Price <= query.MaxPrice.Value)
            .Where(l => query.Beds is null || l.Beds >= query.Beds.Value)
            .Where(l => MatchesLocation(l, query.Location))
            // Destacados primero, luego precio ascendente — orden estable de la grilla.
            .OrderByDescending(l => l.Featured)
            .ThenBy(l => l.Price)
            .ToList();

        var facets = new List<PropertyFacet>
        {
            BuildFacet("type", matches.Select(l => l.Type)),
            BuildFacet("operation", matches.Select(l => l.Operation)),
            BuildFacet("city", matches.Select(l => l.City)),
            BuildFacet("beds", matches.Select(l => l.Beds.ToString())),
        };

        return Task.FromResult(new PropertySearchResult(matches, facets));
    }

    public Task<PropertyDetail?> GetListingAsync(string listingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return Task.FromResult<PropertyDetail?>(null);
        }

        var id = listingId.Trim();
        var detail = _catalog.FirstOrDefault(d =>
            string.Equals(d.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(detail);
    }

    private static bool MatchesText(PropertyListing l, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        var t = text.Trim();
        return l.Title.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.City.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.Neighborhood.Contains(t, StringComparison.OrdinalIgnoreCase)
            || l.Type.Contains(t, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLocation(PropertyListing l, string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return true;
        }
        var loc = location.Trim();
        return l.City.Contains(loc, StringComparison.OrdinalIgnoreCase)
            || l.Neighborhood.Contains(loc, StringComparison.OrdinalIgnoreCase);
    }

    private static PropertyFacet BuildFacet(string name, IEnumerable<string> values)
    {
        var counts = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PropertyFacetValue(g.Key, g.Count()))
            .OrderByDescending(fv => fv.Count)
            .ThenBy(fv => fv.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PropertyFacet(name, counts);
    }

    // ── Catálogo sembrado (memoria, determinista) ───────────────────────
    // Varias ciudades CO × tipos. Geo real (lat/lng) para que el mapa pinte
    // pins coherentes. Precios y estratos plausibles del mercado CO.
    private static IReadOnlyList<PropertyDetail> Seed()
    {
        return new List<PropertyDetail>
        {
            Listing(
                id: "prop-001", slug: "apto-chico-norte-bogota",
                title: "Apartamento en Chicó Norte", operation: "venta", type: "apartamento",
                price: 720_000_000m, city: "Bogotá", neighborhood: "Chicó Norte",
                beds: 3, baths: 3, area: 118, stratum: 6, lat: 4.6792, lng: -74.0494,
                featured: true, parking: 2, age: "5 años", floor: "8",
                description: "Amplio apartamento con vista a los cerros, acabados de lujo y excelente iluminación natural en uno de los sectores más exclusivos del norte de Bogotá.",
                amenities: new[] { "Gimnasio", "Salón comunal", "Vigilancia 24h", "Zona BBQ", "Ascensor" },
                agentName: "Laura Gómez", agentPhone: "+57 310 555 1001"),

            Listing(
                id: "prop-002", slug: "casa-laureles-medellin",
                title: "Casa en Laureles", operation: "venta", type: "casa",
                price: 1_150_000_000m, city: "Medellín", neighborhood: "Laureles",
                beds: 4, baths: 4, area: 240, stratum: 5, lat: 6.2447, lng: -75.5916,
                featured: true, parking: 3, age: "12 años", floor: "1",
                description: "Casa familiar de dos plantas con patio interior, en una de las zonas más caminables y tradicionales de Medellín, cerca a parques y restaurantes.",
                amenities: new[] { "Jardín", "Estudio", "Cuarto de servicio", "Patio", "Garaje cubierto" },
                agentName: "Andrés Mejía", agentPhone: "+57 311 555 1002"),

            Listing(
                id: "prop-003", slug: "apto-arriendo-poblado-medellin",
                title: "Apartamento en El Poblado", operation: "arriendo", type: "apartamento",
                price: 3_800_000m, city: "Medellín", neighborhood: "El Poblado",
                beds: 2, baths: 2, area: 85, stratum: 6, lat: 6.2086, lng: -75.5660,
                featured: false, parking: 1, age: "3 años", floor: "14",
                description: "Apartamento moderno amoblado, ideal para ejecutivos, con vista panorámica y a pasos del Parque Lleras y la zona financiera.",
                amenities: new[] { "Amoblado", "Gimnasio", "Piscina", "Coworking", "Pet friendly" },
                agentName: "Andrés Mejía", agentPhone: "+57 311 555 1002"),

            Listing(
                id: "prop-004", slug: "oficina-santa-monica-cali",
                title: "Oficina en Santa Mónica", operation: "arriendo", type: "oficina",
                price: 5_200_000m, city: "Cali", neighborhood: "Santa Mónica",
                beds: 0, baths: 2, area: 130, stratum: 5, lat: 3.4699, lng: -76.5290,
                featured: false, parking: 4, age: "8 años", floor: "5",
                description: "Oficina diáfana lista para adecuar, con recepción, sala de juntas y excelente conectividad de transporte en el norte de Cali.",
                amenities: new[] { "Recepción", "Sala de juntas", "Aire acondicionado", "Planta eléctrica", "Fibra óptica" },
                agentName: "Carolina Ruiz", agentPhone: "+57 312 555 1003"),

            Listing(
                id: "prop-005", slug: "apto-bocagrande-cartagena",
                title: "Apartamento en Bocagrande", operation: "venta", type: "apartamento",
                price: 980_000_000m, city: "Cartagena", neighborhood: "Bocagrande",
                beds: 3, baths: 3, area: 145, stratum: 6, lat: 10.4006, lng: -75.5557,
                featured: true, parking: 2, age: "2 años", floor: "20",
                description: "Apartamento frente al mar con balcón amplio y acabados premium, en el corazón turístico y financiero de Cartagena.",
                amenities: new[] { "Vista al mar", "Piscina", "Gimnasio", "Turco", "Vigilancia 24h" },
                agentName: "Diego Salas", agentPhone: "+57 313 555 1004"),

            Listing(
                id: "prop-006", slug: "casa-arriendo-cedritos-bogota",
                title: "Casa en Cedritos", operation: "arriendo", type: "casa",
                price: 6_500_000m, city: "Bogotá", neighborhood: "Cedritos",
                beds: 4, baths: 3, area: 210, stratum: 4, lat: 4.7240, lng: -74.0320,
                featured: false, parking: 2, age: "15 años", floor: "1",
                description: "Casa espaciosa con antejardín y zona verde, en un sector residencial tranquilo y bien conectado del norte de Bogotá.",
                amenities: new[] { "Antejardín", "Chimenea", "Cuarto de servicio", "Bodega", "Garaje doble" },
                agentName: "Laura Gómez", agentPhone: "+57 310 555 1001"),

            Listing(
                id: "prop-007", slug: "local-comercial-centro-cali",
                title: "Local comercial en el Centro", operation: "venta", type: "local",
                price: 420_000_000m, city: "Cali", neighborhood: "Centro",
                beds: 0, baths: 1, area: 95, stratum: 3, lat: 3.4516, lng: -76.5320,
                featured: false, parking: 0, age: "25 años", floor: "1",
                description: "Local sobre vía principal de alto flujo peatonal, ideal para retail o restaurante, con vitrina amplia y baño.",
                amenities: new[] { "Vitrina amplia", "Alto tráfico", "Baño", "Bodega trasera" },
                agentName: "Carolina Ruiz", agentPhone: "+57 312 555 1003"),

            Listing(
                id: "prop-008", slug: "apto-manga-cartagena",
                title: "Apartamento en Manga", operation: "venta", type: "apartamento",
                price: 540_000_000m, city: "Cartagena", neighborhood: "Manga",
                beds: 2, baths: 2, area: 92, stratum: 4, lat: 10.4031, lng: -75.5360,
                featured: false, parking: 1, age: "6 años", floor: "4",
                description: "Apartamento acogedor en el tradicional barrio de Manga, cerca al centro histórico, con buena ventilación y zonas comunes.",
                amenities: new[] { "Salón comunal", "Vigilancia", "Ascensor", "Parqueadero visitantes" },
                agentName: "Diego Salas", agentPhone: "+57 313 555 1004"),
        };
    }

    private static PropertyDetail Listing(
        string id, string slug, string title, string operation, string type,
        decimal price, string city, string neighborhood, int beds, int baths,
        int area, int stratum, double lat, double lng, bool featured,
        int parking, string age, string floor, string description,
        string[] amenities, string agentName, string agentPhone)
    {
        var summary = new PropertyListing(
            Id: id, Slug: slug, Title: title, Operation: operation, Type: type,
            Price: price, Currency: Cop, City: city, Neighborhood: neighborhood,
            Beds: beds, Baths: baths, AreaM2: area, Stratum: stratum,
            Lat: lat, Lng: lng,
            ImageUrl: $"/media/realty/{slug}.jpg", Featured: featured);

        var specs = new List<PropertySpec>
        {
            new("Área", $"{area} m²"),
            new("Habitaciones", beds.ToString()),
            new("Baños", baths.ToString()),
            new("Parqueaderos", parking.ToString()),
            new("Estrato", stratum.ToString()),
            new("Antigüedad", age),
            new("Piso", floor),
        };

        var gallery = new List<string>
        {
            $"/media/realty/{slug}-1.jpg",
            $"/media/realty/{slug}-2.jpg",
            $"/media/realty/{slug}-3.jpg",
            $"/media/realty/{slug}-4.jpg",
        };

        var location = new PropertyLocation(lat, lng,
            Address: $"{neighborhood}, {city}", Neighborhood: neighborhood, City: city);

        return new PropertyDetail(summary, description, specs, amenities, gallery, location, agentName, agentPhone);
    }
}
