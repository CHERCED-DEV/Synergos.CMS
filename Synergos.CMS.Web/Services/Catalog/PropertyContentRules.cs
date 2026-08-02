using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Lo que el editor escribió en un <c>propertyListing</c>, sin interpretar.</summary>
public sealed record PropertyListingContent(
    string? Slug,
    string? Title,
    string? Operation,
    string? Type,
    decimal Price,
    bool Featured = false,
    string? Description = null,
    IReadOnlyList<string>? Gallery = null,
    IReadOnlyList<string>? Amenities = null,
    int Beds = 0,
    int Baths = 0,
    int AreaM2 = 0,
    int Parking = 0,
    int Stratum = 0,
    string? Age = null,
    string? Floor = null,
    string? City = null,
    string? Neighborhood = null,
    string? Address = null,
    double? Lat = null,
    double? Lng = null,
    string? AgentName = null,
    string? AgentPhone = null);

/// <summary>
/// Las etiquetas de las características, resueltas del Dictionary uSync por el llamador.
/// </summary>
/// <remarks>
/// <b>Viajan como parámetro y no se leen aquí</b> porque estas reglas son puras y el
/// Dictionary es de Umbraco. Y son etiquetas del SERVIDOR, no del bundle: <c>PropertySpec</c>
/// lleva su <c>Label</c> ya escrito en el JSON que consume la ficha, así que su sitio natural
/// es el Dictionary del CMS y no la i18n del bundle — al revés que el resto del copy del
/// vertical, que sí vive en el paquete Angular.
/// </remarks>
public sealed record PropertySpecLabels(
    string Area,
    string Beds,
    string Baths,
    string Parking,
    string Stratum,
    string Age,
    string Floor);

/// <summary>
/// Las reglas de negocio que convierten un <c>propertyListing</c> autorado en una ficha
/// servible del portal inmobiliario.
/// </summary>
/// <remarks>
/// <b>Clase pura</b>, por la misma razón que <see cref="EventContentRules"/>: es lo único que
/// hace verificables unas reglas que de otro modo quedarían soldadas a
/// <c>IPublishedContent</c>, que en la práctica no se puede simular. Devuelve los problemas en
/// vez de loguearlos; el llamador —que sí tiene <c>ILogger</c>— los emite.
///
/// <para><b>Omitir un inmueble es caro, así que se omite poco.</b> A diferencia de una
/// localidad de un evento, perder una ficha es perder el activo que el portal existe para
/// mostrar. Solo se descarta lo que no se puede servir sin mentir: sin identidad (slug),
/// sin nombre, sin ciudad, o con un precio que no es un precio.</para>
/// </remarks>
public static class PropertyContentRules
{
    /// <summary>Operación por defecto cuando el valor autorado no se reconoce.</summary>
    public const string DefaultOperation = "venta";

    private static readonly HashSet<string> KnownOperations =
        new(StringComparer.OrdinalIgnoreCase) { "venta", "arriendo" };

    /// <summary>
    /// El inmueble servible, o null si no se puede servir sin mentir.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Sin slug:</b> no hay identidad. Es lo que resuelve la ficha y lo que lleva la
    /// URL; un inmueble sin él es un enlace roto.</item>
    /// <item><b>Sin título:</b> a diferencia de un producto, aquí NO se cae al nombre del nodo
    /// del árbol — saldría "Inmueble (1)" en la tarjeta. Que falte la ficha se ve; que salga
    /// con un título de andamiaje, no.</item>
    /// <item><b>Sin ciudad:</b> es faceta y es orientación. Un inmueble sin ciudad no aparece
    /// en ningún filtro y nadie lo encuentra igual.</item>
    /// <item><b>Precio ≤ 0:</b> se pinta como <i>Gratis</i> o <i>$0</i>. Un inmueble anunciado
    /// en cero es la clase de basura que esta capa no debe emitir, y además es exactamente lo
    /// que <c>PublishListingAsync</c> ya rechaza para el agente: la misma regla a los dos
    /// lados, no una para cada origen.</item>
    /// </list>
    ///
    /// <para><b>La geo incompleta o fuera de rango se sirve como (0, 0), no descarta.</b> El
    /// módulo Angular ya declara ese contrato — solo dibuja el pin si
    /// <c>lat !== 0 || lng !== 0</c>—, así que el inmueble aparece en los resultados y se
    /// queda fuera del mapa, que es justo lo que corresponde. Se exigen las DOS coordenadas y
    /// dentro de rango: media coordenada no es medio pin, es un pin en el meridiano de
    /// Greenwich, y una longitud tecleada en el campo de latitud manda el mapa al océano.</para>
    /// </remarks>
    public static EventContentResult<PropertyDetail?> BuildListing(
        PropertyListingContent content,
        string currency,
        PropertySpecLabels labels)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(labels);

        var issues = new List<EventContentIssue>();

        var slug = Normalize(content.Slug);
        if (slug.Length == 0)
        {
            issues.Add(Warn("Un inmueble no tiene slug; se omite. Sin él la ficha no se puede enlazar."));
            return new EventContentResult<PropertyDetail?>(null, issues);
        }

        var title = content.Title?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            issues.Add(Warn($"El inmueble '{slug}' no tiene título; se omite."));
            return new EventContentResult<PropertyDetail?>(null, issues);
        }

        var city = content.City?.Trim();
        if (string.IsNullOrEmpty(city))
        {
            issues.Add(Warn($"El inmueble '{slug}' no tiene ciudad; se omite. Sin ella no cae en ninguna faceta."));
            return new EventContentResult<PropertyDetail?>(null, issues);
        }

        if (content.Price <= 0m)
        {
            issues.Add(Err(
                $"El inmueble '{slug}' tiene precio {content.Price}; se OMITE. Un inmueble en cero se pinta " +
                "como gratis, y es la misma regla que ya rechaza un borrador del agente."));
            return new EventContentResult<PropertyDetail?>(null, issues);
        }

        var operation = Normalize(content.Operation);
        if (operation.Length == 0 || !KnownOperations.Contains(operation))
        {
            // Cae a venta y no se rechaza porque la operación elige PANTALLA y copy, no el
            // precio: un inmueble servido en la pestaña equivocada se ve, uno ausente no.
            if (operation.Length > 0)
            {
                issues.Add(Warn(
                    $"El inmueble '{slug}' declara operación '{operation}', que no es venta ni arriendo; " +
                    $"se trata como {DefaultOperation}."));
            }

            operation = DefaultOperation;
        }

        var type = Normalize(content.Type);
        if (type.Length == 0)
        {
            // NO se inventa un tipo: sería una mentira en la faceta, y quien filtra por
            // "apartamento" acabaría viendo un lote. Vacío no casa ninguna faceta y ya está.
            issues.Add(Warn($"El inmueble '{slug}' no tiene tipo; no saldrá en la faceta de tipo."));
        }

        var (lat, lng) = ResolveGeo(slug, content.Lat, content.Lng, issues);

        var gallery = EventContentRules.CleanTextList(content.Gallery);
        var neighborhood = content.Neighborhood?.Trim() ?? string.Empty;

        var summary = new PropertyListing(
            // El slug ES el id, igual que en Tienda y en Eventos: GetListingAsync casa por
            // cualquiera de los dos, así que emitirlos iguales evita un segundo identificador
            // que nadie autora.
            Id: slug,
            Slug: slug,
            Title: title,
            Operation: operation,
            Type: type,
            Price: content.Price,
            Currency: currency,
            City: city,
            Neighborhood: neighborhood,
            Beds: NonNegative(content.Beds),
            Baths: NonNegative(content.Baths),
            AreaM2: NonNegative(content.AreaM2),
            Stratum: NonNegative(content.Stratum),
            Lat: lat,
            Lng: lng,
            // La portada es la PRIMERA foto de la galería, que es lo que dice la ayuda del
            // campo. Sin fotos se emite vacío y la tarjeta pinta su propio placeholder: una
            // ruta inventada daría un 404 por cada tarjeta de la grilla.
            ImageUrl: gallery.Count > 0 ? gallery[0] : string.Empty,
            Featured: content.Featured);

        var address = content.Address?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            // Se arma con lo que hay, sin comas colgantes cuando falta el barrio.
            address = neighborhood.Length > 0 ? $"{neighborhood}, {city}" : city;
        }

        var detail = new PropertyDetail(
            Summary: summary,
            Description: content.Description?.Trim() ?? string.Empty,
            Specs: BuildSpecs(content, labels),
            Amenities: EventContentRules.CleanTextList(content.Amenities),
            Gallery: gallery,
            Location: new PropertyLocation(lat, lng, address, neighborhood, city),
            AgentName: content.AgentName?.Trim() ?? string.Empty,
            AgentPhone: content.AgentPhone?.Trim() ?? string.Empty);

        return new EventContentResult<PropertyDetail?>(detail, issues);
    }

    /// <summary>
    /// Las características de la ficha, DERIVADAS de los campos tipados.
    /// </summary>
    /// <remarks>
    /// <b>No se autoran como pares etiqueta-valor</b>, que era la otra opción obvia. Un
    /// BlockList de "Etiqueta / Valor" habría dado libertad total y, con ella, "Habitaciones",
    /// "habitaciones", "Hab." y "N° de habitaciones" en el mismo portal — y ninguna faceta
    /// posible, porque nada de eso es un número comparable. Los campos son tipados, las
    /// etiquetas salen del Dictionary y el orden lo pone esta capa.
    ///
    /// <para>Un valor en 0 o vacío <b>no sale</b>: "Parqueaderos: 0" es ruido en la ficha de un
    /// apartaestudio, y "Estrato: 0" es directamente falso — el estrato va de 1 a 6.</para>
    /// </remarks>
    public static IReadOnlyList<PropertySpec> BuildSpecs(
        PropertyListingContent content,
        PropertySpecLabels labels)
    {
        var specs = new List<PropertySpec>(7);

        if (content.AreaM2 > 0)
        {
            specs.Add(new PropertySpec(labels.Area, $"{content.AreaM2} m²"));
        }

        if (content.Beds > 0)
        {
            specs.Add(new PropertySpec(labels.Beds, content.Beds.ToString()));
        }

        if (content.Baths > 0)
        {
            specs.Add(new PropertySpec(labels.Baths, content.Baths.ToString()));
        }

        if (content.Parking > 0)
        {
            specs.Add(new PropertySpec(labels.Parking, content.Parking.ToString()));
        }

        if (content.Stratum > 0)
        {
            specs.Add(new PropertySpec(labels.Stratum, content.Stratum.ToString()));
        }

        var age = content.Age?.Trim();
        if (!string.IsNullOrEmpty(age))
        {
            specs.Add(new PropertySpec(labels.Age, age));
        }

        var floor = content.Floor?.Trim();
        if (!string.IsNullOrEmpty(floor))
        {
            specs.Add(new PropertySpec(labels.Floor, floor));
        }

        return specs;
    }

    /// <summary>
    /// La línea de specs de la tarjeta (<c>listing.subtitle</c>), desde los campos tipados.
    /// </summary>
    /// <remarks>
    /// <para><b>Se deriva, no se autora.</b> Es el mismo argumento de
    /// <see cref="BuildSpecs"/>: pedirle al editor que escriba "3 hab · 2 baños · 78 m²" a mano
    /// garantiza que la mitad del catálogo diga "3 habitaciones" y la otra "3 Hab." — y ninguna
    /// de las dos se puede filtrar. Los números ya están tipados; esto solo los redacta.</para>
    ///
    /// <para>Un campo en 0 <b>no aparece</b>: "0 baños" en un lote es ruido, y el separador
    /// colgante (" · ") que dejaría era exactamente el síntoma que se corrigió al cablear la
    /// tarjeta.</para>
    /// </remarks>
    public static string BuildSubtitle(PropertyListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var parts = new List<string>(4);
        if (listing.Beds > 0) parts.Add($"{listing.Beds} hab");
        if (listing.Baths > 0) parts.Add($"{listing.Baths} {(listing.Baths == 1 ? "baño" : "baños")}");
        if (listing.AreaM2 > 0) parts.Add($"{listing.AreaM2} m²");
        if (listing.Stratum > 0) parts.Add($"Estrato {listing.Stratum}");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Los chips de la tarjeta (<c>listing.badges</c>): destacado, estrato y operación.
    /// </summary>
    /// <remarks>
    /// El orden es deliberado y no alfabético: primero lo que hace que la tarjeta se mire
    /// (Destacado), después lo que segmenta (Estrato) y al final lo que ya se deduce del precio
    /// (En venta / En arriendo). Una operación que no es ninguna de las dos <b>no emite chip</b>
    /// en vez de emitir el valor crudo: la tarjeta con "En xyz" se ve peor que sin chip.
    /// </remarks>
    public static IReadOnlyList<string> BuildBadges(PropertyListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);

        var badges = new List<string>(3);
        if (listing.Featured) badges.Add("Destacado");
        if (listing.Stratum > 0) badges.Add($"Estrato {listing.Stratum}");

        var status = Normalize(listing.Operation) switch
        {
            "venta" => "En venta",
            "arriendo" => "En arriendo",
            _ => null
        };
        if (status is not null) badges.Add(status);

        return badges;
    }

    /// <summary>
    /// La coordenada del inmueble, o <c>(0, 0)</c> si no está completa o no es plausible.
    /// </summary>
    private static (double Lat, double Lng) ResolveGeo(
        string slug,
        double? rawLat,
        double? rawLng,
        List<EventContentIssue> issues)
    {
        if (rawLat is null && rawLng is null)
        {
            // Sin geo el inmueble sigue siendo válido: simplemente no sale en el mapa.
            return (0d, 0d);
        }

        var latOk = rawLat is >= -90d and <= 90d;
        var lngOk = rawLng is >= -180d and <= 180d;

        if (latOk && lngOk && (rawLat!.Value != 0d || rawLng!.Value != 0d))
        {
            return (rawLat.Value, rawLng!.Value);
        }

        issues.Add(Warn(
            $"El inmueble '{slug}' tiene geo incompleta o fuera de rango (lat={rawLat}, lng={rawLng}); " +
            "no saldrá en el mapa. Formato: grados decimales, ej. 4.6584 / -74.0936."));
        return (0d, 0d);
    }

    /// <summary>
    /// Un entero negativo es una errata, y se sirve como 0.
    /// </summary>
    /// <remarks>
    /// No descarta el inmueble: "-2 baños" es un dedo torcido en el backoffice, y perder la
    /// ficha entera por eso sería desproporcionado. Con 0 la característica simplemente no
    /// aparece en la lista.
    /// </remarks>
    private static int NonNegative(int value) => value > 0 ? value : 0;

    private static string Normalize(string? raw)
        => raw?.Trim().ToLowerInvariant() ?? string.Empty;

    private static EventContentIssue Warn(string message)
        => new(EventContentIssueLevel.Warning, message);

    private static EventContentIssue Err(string message)
        => new(EventContentIssueLevel.Error, message);
}
