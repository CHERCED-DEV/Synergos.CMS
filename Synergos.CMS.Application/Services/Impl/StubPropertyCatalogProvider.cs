using System.Collections.Concurrent;
using Synergos.CMS.Application.Configuration;
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

    // Catálogo sembrado + inmuebles publicados por agentes en runtime
    // (PublishListingAsync). ConcurrentDictionary keyed por id → el estado (listados
    // creados en la demo) vive en el proceso, igual que el resto de stubs del motor.
    private static readonly ConcurrentDictionary<string, PropertyDetail> Catalog = BuildCatalog();

    // Contador monotónico para el id de inmuebles publicados por agentes.
    private static int _publishedCounter;

    private readonly ICatalogIndex<PropertyListing> _index;

    /// <summary>Ctor por defecto: el motor, sin tope de página (esta seam devuelve todo).</summary>
    public StubPropertyCatalogProvider()
        : this(new InMemoryCatalogIndex<PropertyListing>(Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal StubPropertyCatalogProvider(ICatalogIndex<PropertyListing> index)
        => _index = index ?? throw new ArgumentNullException(nameof(index));

    /// <summary>
    /// Lo que Propiedades DECLARA sobre su catálogo. Cero lógica.
    /// </summary>
    /// <remarks>
    /// <b>El barrio pesa tanto como la ciudad, a propósito:</b> en Colombia se busca por
    /// barrio ("Chicó", "Laureles", "El Poblado") tanto como por ciudad, y el código anterior
    /// los trataba igual en un OR plano — no hay evidencia para desempatarlos, así que
    /// inventar una jerarquía aquí sería añadir una opinión que nadie tomó.
    /// </remarks>
    internal static CatalogDescriptor<PropertyListing> Descriptor { get; } = new(
        idOf: l => l.Id,
        searchFields: new[]
        {
            new CatalogSearchField<PropertyListing>(5, l => l.Title),
            new CatalogSearchField<PropertyListing>(3, l => l.Neighborhood),
            new CatalogSearchField<PropertyListing>(3, l => l.City),
            // El tipo casa con media grilla ("apartamento" = 4 de 8): un match aquí vale poco.
            new CatalogSearchField<PropertyListing>(2, l => l.Type),
        },
        // El orden histórico: destacados primero, luego precio ascendente.
        defaultOrder: listings => listings.OrderByDescending(l => l.Featured).ThenBy(l => l.Price),
        filters: new CatalogFilter<PropertyListing>[]
        {
            new CatalogTermFilter<PropertyListing>("type", "Tipo", l => new[] { l.Type }),
            new CatalogTermFilter<PropertyListing>("operation", "Operación", l => new[] { l.Operation }),
            new CatalogTermFilter<PropertyListing>("city", "Ciudad", l => new[] { l.City }),
            // Threshold y NO valores exactos: el filtro siempre fue `l.Beds >= query.Beds`
            // ("3 habitaciones o más" es lo que pide quien busca casa), pero la faceta contaba
            // EXACTOS — o sea que el chip MENTÍA: decía "3 (2)" y al pulsarlo devolvía 4
            // (los de 3 y los de 4). Declarar los tramos alinea el chip con lo que el filtro
            // hace. Se cae el chip "0", que como umbral no significa nada (Beds >= 0 = todo).
            // Ascendente: los chips se emiten en el orden declarado, y "1+, 2+, 3+, 4+" es
            // como los lee cualquiera (y como los pinta la UI cuando deriva los suyos).
            new CatalogThresholdFilter<PropertyListing>("beds", "Habitaciones", l => l.Beds, new[]
            {
                new CatalogThresholdBucket(1, "1+ habitación"),
                new CatalogThresholdBucket(2, "2+ habitaciones"),
                new CatalogThresholdBucket(3, "3+ habitaciones"),
                new CatalogThresholdBucket(4, "4+ habitaciones"),
            }),
        });

    public Task<PropertySearchResult> SearchAsync(PropertyQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new PropertyQuery();

        // El precio se filtra AQUÍ y no en el descriptor: minPrice/maxPrice son un rango
        // CONTINUO que el usuario teclea, no los tramos declarados que CatalogRangeFilter
        // espera — no son la misma cosa y forzarlos sería deformar el motor para acomodar un
        // vertical. Acotar QUÉ ÍTEMS EXISTEN es trabajo del llamador (lo dice el contrato de
        // ICatalogIndex), y hacerlo antes es correcto también para las facetas: el rango
        // reduce el universo, y los chips de tipo/ciudad/habitaciones cuentan dentro de él.
        //
        // Location va por aquí por la MISMA razón, y NO como la faceta `city`: busca por
        // SUBSTRING sobre ciudad O barrio ("laureles" es un barrio, "medell" media ciudad),
        // mientras una faceta casa por igualdad exacta sobre un campo. Doblarlo en el filtro
        // de `city` habría hecho dos daños: buscar un barrio devolvería CERO, y los barrios
        // saldrían como chips en la columna Ciudad.
        var universe = Catalog.Values
            .Select(d => d.Summary)
            .Where(l => query.MinPrice is null || l.Price >= query.MinPrice.Value)
            .Where(l => query.MaxPrice is null || l.Price <= query.MaxPrice.Value)
            .Where(l => MatchesLocation(l, query.Location))
            .ToList();

        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            filters["type"] = new[] { query.Type.Trim() };
        }
        if (query.Beds is > 0)
        {
            filters["beds"] = new[] { query.Beds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        }

        // Take explícito + CatalogSettings.Unpaged en el ctor: esta seam promete TODAS las
        // coincidencias, y el default del motor (24) las truncaría en silencio.
        var result = _index.Search(universe, new CatalogQuery(
            Text: query.Text,
            Filters: filters.Count > 0 ? filters : null,
            Take: int.MaxValue));

        return Task.FromResult(new PropertySearchResult(
            Listings: result.Items,
            Facets: result.Facets.Select(ToPropertyFacet).ToList()));
    }

    /// <summary>
    /// <see cref="CatalogFacet"/> → <see cref="PropertyFacet"/>. El Label VIAJA: sin él, el
    /// chip de habitaciones se pinta con el número pelado ("3") mientras significa "3 o más".
    /// </summary>
    private static PropertyFacet ToPropertyFacet(CatalogFacet f)
        => new(f.Field, f.Values.Select(v => new PropertyFacetValue(v.Value, v.Count, v.Label)).ToList());

    public Task<PropertyDetail?> GetListingAsync(string listingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return Task.FromResult<PropertyDetail?>(null);
        }

        var id = listingId.Trim();
        var detail = Catalog.Values.FirstOrDefault(d =>
            string.Equals(d.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Summary.Slug, id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(detail);
    }

    public Task<PropertyDetail> PublishListingAsync(PropertyDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ArgumentException("El título del inmueble es obligatorio.", nameof(draft));
        }
        if (draft.Price <= 0m)
        {
            throw new ArgumentException("El precio del inmueble debe ser mayor a cero.", nameof(draft));
        }
        if (draft.Geo is null)
        {
            throw new ArgumentException("La ubicación (geo) del inmueble es obligatoria.", nameof(draft));
        }

        var id = $"prop-org-{Interlocked.Increment(ref _publishedCounter)}";
        var slug = Slugify(draft.Title) + "-" + id;
        var currency = string.IsNullOrWhiteSpace(draft.Currency) ? Cop : draft.Currency!.Trim();
        var neighborhood = string.IsNullOrWhiteSpace(draft.Neighborhood) ? draft.City.Trim() : draft.Neighborhood!.Trim();
        var gallery = (draft.Gallery ?? Array.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .ToList();

        var summary = new PropertyListing(
            Id: id, Slug: slug, Title: draft.Title.Trim(),
            Operation: string.IsNullOrWhiteSpace(draft.Operation) ? "venta" : draft.Operation.Trim(),
            Type: string.IsNullOrWhiteSpace(draft.Type) ? "apartamento" : draft.Type.Trim(),
            Price: draft.Price, Currency: currency,
            City: draft.City.Trim(), Neighborhood: neighborhood,
            Beds: draft.Beds, Baths: draft.Baths, AreaM2: draft.Area, Stratum: draft.Stratum,
            Lat: draft.Geo.Lat, Lng: draft.Geo.Lng,
            ImageUrl: gallery.Count > 0 ? gallery[0] : $"/media/realty/{slug}.jpg",
            Featured: false);

        var specs = new List<PropertySpec>
        {
            new("Área", $"{draft.Area} m²"),
            new("Habitaciones", draft.Beds.ToString()),
            new("Baños", draft.Baths.ToString()),
            new("Estrato", draft.Stratum.ToString()),
        };

        var location = new PropertyLocation(draft.Geo.Lat, draft.Geo.Lng,
            Address: $"{neighborhood}, {draft.City.Trim()}", Neighborhood: neighborhood, City: draft.City.Trim());

        var detail = new PropertyDetail(
            summary,
            string.IsNullOrWhiteSpace(draft.Description) ? string.Empty : draft.Description.Trim(),
            specs,
            Array.Empty<string>(),
            gallery,
            location,
            string.IsNullOrWhiteSpace(draft.AgentName) ? "Agente" : draft.AgentName!.Trim(),
            string.IsNullOrWhiteSpace(draft.AgentPhone) ? string.Empty : draft.AgentPhone!.Trim());

        Catalog[id] = detail;
        return Task.FromResult(detail);
    }

    // Slug amable a partir del título (ascii-ish, sin acentos sofisticados — suficiente
    // para la demo; el adapter real usa el slug del CMS).
    private static string Slugify(string title)
    {
        var chars = title.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }

    private static bool MatchesLocation(PropertyListing l, string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return true;
        }
        var loc = location.Trim();
        return CatalogText.Contains(l.City, loc)
            || CatalogText.Contains(l.Neighborhood, loc);
    }


    // ── Catálogo sembrado (memoria, determinista) ───────────────────────
    // Varias ciudades CO × tipos. Geo real (lat/lng) para que el mapa pinte
    // pins coherentes. Precios y estratos plausibles del mercado CO.
    private static ConcurrentDictionary<string, PropertyDetail> BuildCatalog()
    {
        var seed = new List<PropertyDetail>
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

        return new ConcurrentDictionary<string, PropertyDetail>(
            seed.ToDictionary(d => d.Summary.Id, StringComparer.Ordinal),
            StringComparer.Ordinal);
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

        // Cover local coherente con el tema (servido desde wwwroot/media/realty/).
        // Un solo cover por listado: la PLP usa gallery[0]; la ficha muestra la galería.
        var gallery = new List<string>
        {
            $"/media/realty/{slug}.jpg",
        };

        var location = new PropertyLocation(lat, lng,
            Address: $"{neighborhood}, {city}", Neighborhood: neighborhood, City: city);

        return new PropertyDetail(summary, description, specs, amenities, gallery, location, agentName, agentPhone);
    }
}
