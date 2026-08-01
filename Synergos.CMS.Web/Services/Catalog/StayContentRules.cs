using System.Globalization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Lo que el hotelero escribió en un <c>stayListing</c>, sin interpretar.</summary>
public sealed record StayListingContent(
    string? Slug,
    string? Name,
    string? City,
    string? Region = null,
    string? Description = null,
    IReadOnlyList<string>? Gallery = null,
    IReadOnlyList<string>? Amenities = null,
    IReadOnlyList<string>? RoomTypeCodes = null,
    int Rooms = 0,
    int MaxGuests = 0,
    int RoomSizeM2 = 0,
    TimeOnly? CheckIn = null,
    TimeOnly? CheckOut = null,
    string? Address = null,
    double? Lat = null,
    double? Lng = null,
    decimal ReviewAverage = 0m,
    int ReviewCount = 0,
    string? ReviewHighlight = null,
    decimal ScoreCleanliness = 0m,
    decimal ScoreLocation = 0m,
    decimal ScoreService = 0m,
    decimal ScoreValue = 0m);

/// <summary>
/// Las etiquetas de las características de la estadía, resueltas del Dictionary uSync por el
/// llamador.
/// </summary>
/// <remarks>
/// <b>Viajan como parámetro y no se leen aquí</b> por lo mismo que en
/// <see cref="PropertySpecLabels"/>: estas reglas son puras y el Dictionary es de Umbraco. Y son
/// etiquetas del SERVIDOR — <see cref="StaySpec"/> lleva su <c>Label</c> ya escrito en el JSON
/// que consume la ficha—, así que su sitio es el Dictionary del CMS y no la i18n del bundle.
/// </remarks>
public sealed record StaySpecLabels(
    string Rooms,
    string MaxGuests,
    string RoomSize,
    string CheckIn,
    string CheckOut);

/// <summary>
/// Los nombres de los cuatro ejes de reseñas, resueltos del Dictionary uSync por el llamador.
/// </summary>
/// <remarks>
/// Son etiquetas de servidor por la misma razón que las de las características:
/// <see cref="StayReviewCategoryScore.Category"/> viaja ya escrito en el JSON de la ficha.
/// </remarks>
public sealed record StayReviewLabels(
    string Cleanliness,
    string Location,
    string Service,
    string Value);

/// <summary>
/// Las reglas de negocio que convierten un <c>stayListing</c> autorado en la ficha de estadía
/// que sirve la cara viajera de Booking.
/// </summary>
/// <remarks>
/// <b>Clase pura</b>, por la misma razón que <see cref="PropertyContentRules"/> y
/// <see cref="EventContentRules"/>: <c>IPublishedContent.Value&lt;T&gt;</c> no se puede simular
/// con coste razonable, y las reglas que deciden si una propiedad hotelera aparece —y con qué
/// prueba social— no podían ser las únicas sin cobertura. Devuelve los problemas en vez de
/// loguearlos; el llamador —que sí tiene <c>ILogger</c>— los emite.
///
/// <para><b>Perder una propiedad es perder inventario, así que se omite poco.</b> Igual que en
/// Inmobiliaria: solo se descarta lo que no se puede servir sin mentir —sin identidad, sin
/// nombre o sin ciudad—. Lo demás se degrada.</para>
///
/// <para><b>No hay regla de precio</b>, y no es un olvido: el precio de una estadía no vive en
/// su contenido sino en <see cref="IRoomAvailabilityProvider"/>, que cotiza por fecha y
/// ocupación. Esta capa describe la propiedad; nunca la tarifa.</para>
/// </remarks>
public static class StayContentRules
{
    /// <summary>Un puntaje de reseñas va de 0 a 10, como en el resto del vertical.</summary>
    public const decimal MaxScore = 10m;

    /// <summary>
    /// La ficha de la estadía servible, o null si no se puede servir sin mentir.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Sin slug:</b> no hay identidad. Es lo que resuelve <c>GetStayAsync</c> y lo que
    /// viaja en <c>/api/travel/stay/{id}</c>; una propiedad sin él es una ficha inalcanzable.</item>
    /// <item><b>Sin nombre:</b> NO se cae al nombre del nodo del árbol — saldría "Estadía (1)"
    /// como título de la tarjeta de resultados, que además es el <c>Title</c> que hereda cada
    /// oferta del buscador de hotel.</item>
    /// <item><b>Sin ciudad:</b> es lo que agrupa los resultados y lo que arma la dirección
    /// cuando el hotelero no la escribe. Una propiedad sin ciudad no se encuentra.</item>
    /// </list>
    ///
    /// <para><b>La geo incompleta o fuera de rango se sirve como (0, 0), no descarta</b> — mismo
    /// contrato que Inmobiliaria (ADR 0118 §4). Se exigen las DOS coordenadas y dentro de rango:
    /// media coordenada no es medio pin, es un pin en el meridiano de Greenwich.</para>
    /// </remarks>
    public static EventContentResult<StayDetail?> BuildStay(
        StayListingContent content,
        StaySpecLabels specLabels,
        StayReviewLabels reviewLabels)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(specLabels);
        ArgumentNullException.ThrowIfNull(reviewLabels);

        var issues = new List<EventContentIssue>();

        var slug = Normalize(content.Slug);
        if (slug.Length == 0)
        {
            issues.Add(Warn("Una estadía no tiene slug; se omite. Sin él la ficha no se puede resolver."));
            return new EventContentResult<StayDetail?>(null, issues);
        }

        var name = content.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            issues.Add(Warn($"La estadía '{slug}' no tiene nombre; se omite."));
            return new EventContentResult<StayDetail?>(null, issues);
        }

        var city = content.City?.Trim();
        if (string.IsNullOrEmpty(city))
        {
            issues.Add(Warn($"La estadía '{slug}' no tiene ciudad; se omite. Sin ella no agrupa ni se encuentra."));
            return new EventContentResult<StayDetail?>(null, issues);
        }

        var region = content.Region?.Trim() ?? string.Empty;
        var (lat, lng) = ResolveGeo(slug, content.Lat, content.Lng, issues);

        var address = content.Address?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            // Se arma con lo que hay, sin comas colgantes cuando falta el departamento.
            address = region.Length > 0 ? $"{city}, {region}" : city;
        }

        var detail = new StayDetail(
            // El slug ES el StayId, igual que en Tienda, Eventos e Inmobiliaria: emitir un
            // segundo identificador que nadie autora solo abre la puerta a que discrepen.
            StayId: slug,
            Name: name,
            City: city,
            Region: region,
            Address: address,
            Description: content.Description?.Trim() ?? string.Empty,
            Gallery: EventContentRules.CleanTextList(content.Gallery),
            Amenities: EventContentRules.CleanTextList(content.Amenities),
            Specs: BuildSpecs(content, specLabels),
            Geo: new StayGeo(lat, lng),
            Reviews: BuildReviews(slug, content, reviewLabels, issues),
            RoomTypeCodes: BuildRoomTypeCodes(content.RoomTypeCodes));

        return new EventContentResult<StayDetail?>(detail, issues);
    }

    /// <summary>
    /// Las características de la ficha, DERIVADAS de los campos tipados.
    /// </summary>
    /// <remarks>
    /// <b>No se autoran como pares etiqueta-valor</b>, por lo mismo que en Inmobiliaria
    /// (ADR 0118 §2): un BlockList de "Etiqueta / Valor" daría "Check-in", "check in",
    /// "Check-In" y "Hora de entrada" en el mismo portal, y ninguna comparación posible entre
    /// propiedades. Los campos son tipados, las etiquetas salen del Dictionary y el orden lo
    /// pone esta capa.
    ///
    /// <para>Un valor en 0 o sin autorar <b>no sale</b>: "Tamaño promedio: 0 m²" no informa,
    /// desinforma.</para>
    /// </remarks>
    public static IReadOnlyList<StaySpec> BuildSpecs(StayListingContent content, StaySpecLabels labels)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(labels);

        var specs = new List<StaySpec>(5);

        if (content.Rooms > 0)
        {
            specs.Add(new StaySpec(labels.Rooms, content.Rooms.ToString(CultureInfo.InvariantCulture)));
        }

        if (content.MaxGuests > 0)
        {
            specs.Add(new StaySpec(labels.MaxGuests, content.MaxGuests.ToString(CultureInfo.InvariantCulture)));
        }

        if (content.RoomSizeM2 > 0)
        {
            specs.Add(new StaySpec(labels.RoomSize, $"{content.RoomSizeM2} m²"));
        }

        if (content.CheckIn is { } checkIn)
        {
            specs.Add(new StaySpec(labels.CheckIn, FormatTime(checkIn)));
        }

        if (content.CheckOut is { } checkOut)
        {
            specs.Add(new StaySpec(labels.CheckOut, FormatTime(checkOut)));
        }

        return specs;
    }

    /// <summary>
    /// La hora escrita como la lee un viajero colombiano: <c>3:00 p. m.</c>, <c>11:00 a. m.</c>,
    /// <c>12:00 m.</c>
    /// </summary>
    /// <remarks>
    /// <b>Se deriva y no se teclea</b> — es la misma tesis del ADR 0118 aplicada a un VALOR y no
    /// a una etiqueta. Un campo de texto libre habría dado "15:00", "3pm", "3:00 PM" y
    /// "3:00 p.m." conviviendo en el mismo sitio; el hotelero elige una hora en un reloj y el
    /// servidor decide cómo se lee.
    ///
    /// <para>El formato se compone a mano en vez de con <c>ToString("h:mm tt", es-CO)</c> por
    /// dos razones: el mediodía en Colombia se escribe <c>12:00 m.</c> y no <c>12:00 p. m.</c>,
    /// que ninguna cultura de .NET produce; y depender de ICU haría que la cadena cambie con la
    /// versión del runtime o con <c>InvariantGlobalization</c>, sin que nada falle.</para>
    /// </remarks>
    public static string FormatTime(TimeOnly time)
    {
        var hour12 = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
        var minutes = time.Minute.ToString("00", CultureInfo.InvariantCulture);

        // 12:00 en punto es "m." (meridiano). 12:30 ya es tarde, y 00:xx es madrugada.
        var suffix = time.Hour switch
        {
            12 when time.Minute == 0 => "m.",
            < 12 => "a. m.",
            _ => "p. m.",
        };

        return $"{hour12}:{minutes} {suffix}";
    }

    /// <summary>
    /// El resumen de reseñas, DERIVADO de los cuatro puntajes tipados y su respaldo.
    /// </summary>
    /// <remarks>
    /// <b>Los ejes son cuatro y fijos, no un BlockList de "Categoría / Puntaje".</b> Era el
    /// único sitio de esta rebanada donde un ElementType anidado tenía sentido a primera vista,
    /// y se descartó: el desglose por categoría existe PARA COMPARAR propiedades entre sí, y
    /// eso solo funciona si todas puntúan los mismos ejes. Con texto libre, un hotel calificaría
    /// "Limpieza" y el de al lado "Aseo", y la comparación moriría en silencio —sin que nada
    /// falle— además de reabrir el problema de mayúsculas del ADR 0118 §2.
    ///
    /// <para><b>Un puntaje sin reseñas detrás no es prueba social, es una afirmación.</b> Con
    /// <c>ReviewCount ≤ 0</c> el bloque entero se sirve vacío: ningún portal del rubro muestra
    /// "9,2" respaldado por cero opiniones, y emitirlo sería inventar reputación. La propiedad
    /// NO se pierde por eso — sale sin bloque de reseñas, que es lo correcto para una que acaba
    /// de abrir.</para>
    ///
    /// <para><b>El promedio no se calcula a partir de los ejes.</b> La media de limpieza,
    /// ubicación, servicio y precio-calidad no es la calificación general en ningún portal del
    /// rubro: se pondera por volumen y por otras señales. Derivarla aquí sería inventar un
    /// número con pinta de dato.</para>
    /// </remarks>
    public static StayReviewSummary BuildReviews(
        string slug,
        StayListingContent content,
        StayReviewLabels labels,
        IList<EventContentIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(issues);

        var categories = new List<StayReviewCategoryScore>(4);
        AddScore(categories, slug, labels.Cleanliness, content.ScoreCleanliness, issues);
        AddScore(categories, slug, labels.Location, content.ScoreLocation, issues);
        AddScore(categories, slug, labels.Service, content.ScoreService, issues);
        AddScore(categories, slug, labels.Value, content.ScoreValue, issues);

        var average = Clamp(slug, "la calificación general", content.ReviewAverage, issues);
        var highlight = content.ReviewHighlight?.Trim() ?? string.Empty;

        if (content.ReviewCount <= 0)
        {
            // Solo se avisa si el hotelero SÍ escribió reputación: una propiedad recién abierta
            // sin nada de esto es normal y no merece una línea de log.
            if (average > 0m || categories.Count > 0 || highlight.Length > 0)
            {
                issues.Add(Warn(
                    $"La estadía '{slug}' declara puntajes pero 0 reseñas; el bloque de reseñas se sirve VACÍO. " +
                    "Un puntaje sin opiniones detrás no es prueba social."));
            }

            return new StayReviewSummary(0d, 0, string.Empty, Array.Empty<StayReviewCategoryScore>());
        }

        if (average == 0m && categories.Count > 0)
        {
            issues.Add(Warn(
                $"La estadía '{slug}' tiene desglose por categoría pero la calificación general va en 0; " +
                "la ficha mostrará las barras sin encabezado."));
        }

        return new StayReviewSummary((double)average, content.ReviewCount, highlight, categories);
    }

    /// <summary>
    /// Los códigos de tipo de habitación, normalizados a MAYÚSCULAS y sin repetidos.
    /// </summary>
    /// <remarks>
    /// La normalización no es cosmética: es lo que ata cada oferta del buscador a su propiedad
    /// (<c>RoomOffer.RoomTypeCode</c>), y ese cruce se resuelve por comparación. Un "dlx" con
    /// espacio al final es una oferta sin nombre, sin foto y sin pin en el mapa.
    ///
    /// <para>Una lista vacía es legítima y no avisa: una propiedad direct-only —sin cupo en el
    /// motor de disponibilidad— tiene ficha y no tiene ofertas.</para>
    /// </remarks>
    public static IReadOnlyList<string> BuildRoomTypeCodes(IReadOnlyList<string>? raw)
    {
        var cleaned = EventContentRules.CleanTextList(raw);
        if (cleaned.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = new List<string>(cleaned.Count);
        foreach (var code in cleaned)
        {
            var upper = code.ToUpperInvariant();
            if (seen.Add(upper))
            {
                codes.Add(upper);
            }
        }

        return codes;
    }

    /// <summary>
    /// Los códigos de tipo de habitación que más de una estadía reclama como propios.
    /// </summary>
    /// <remarks>
    /// <b>Es la regla que solo se ve mirando el catálogo entero</b>, y por eso vive aparte de
    /// <see cref="BuildStay"/>. <c>GetStayAsync</c> resuelve una oferta por su código y devuelve
    /// la PRIMERA propiedad que lo declara: si dos hoteles publican "DLX", las ofertas del
    /// segundo salen en el buscador con el nombre, la foto y el pin del primero. No falla nada
    /// —el JSON es válido y la pantalla se pinta— y es exactamente por eso que hay que
    /// gritarlo.
    ///
    /// <para>No corrige: quitarle el código a uno de los dos escondería inventario, y elegir a
    /// cuál sería arbitrario. Avisa, y el hotelero desambigua con un prefijo.</para>
    /// </remarks>
    public static IReadOnlyList<EventContentIssue> FindRoomTypeCollisions(IReadOnlyList<StayDetail> stays)
    {
        ArgumentNullException.ThrowIfNull(stays);

        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var stay in stays)
        {
            foreach (var code in stay.RoomTypeCodes)
            {
                if (!owners.TryGetValue(code, out var list))
                {
                    list = new List<string>(2);
                    owners[code] = list;
                }

                if (!list.Contains(stay.StayId, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(stay.StayId);
                }
            }
        }

        var issues = new List<EventContentIssue>();
        foreach (var (code, claimants) in owners)
        {
            if (claimants.Count > 1)
            {
                issues.Add(Err(
                    $"El tipo de habitación '{code}' lo declaran {claimants.Count} estadías " +
                    $"({string.Join(", ", claimants)}); las ofertas de ese código se atribuirán TODAS a " +
                    $"'{claimants[0]}'. Usa códigos únicos por propiedad."));
            }
        }

        return issues;
    }

    /// <summary>
    /// La coordenada de la propiedad, o <c>(0, 0)</c> si no está completa o no es plausible.
    /// </summary>
    private static (double Lat, double Lng) ResolveGeo(
        string slug,
        double? rawLat,
        double? rawLng,
        List<EventContentIssue> issues)
    {
        if (rawLat is null && rawLng is null)
        {
            // Sin geo la propiedad sigue siendo válida: simplemente no sale en el mapa.
            return (0d, 0d);
        }

        var latOk = rawLat is >= -90d and <= 90d;
        var lngOk = rawLng is >= -180d and <= 180d;

        if (latOk && lngOk && (rawLat!.Value != 0d || rawLng!.Value != 0d))
        {
            return (rawLat.Value, rawLng!.Value);
        }

        issues.Add(Warn(
            $"La estadía '{slug}' tiene geo incompleta o fuera de rango (lat={rawLat}, lng={rawLng}); " +
            "no saldrá en el mapa. Formato: grados decimales, ej. 10.4224 / -75.5468."));
        return (0d, 0d);
    }

    /// <summary>
    /// Añade un eje al desglose si tiene puntaje; un 0 es un campo sin diligenciar, no un cero.
    /// </summary>
    private static void AddScore(
        List<StayReviewCategoryScore> categories,
        string slug,
        string label,
        decimal raw,
        IList<EventContentIssue> issues)
    {
        var score = Clamp(slug, $"el puntaje de '{label}'", raw, issues);
        if (score > 0m)
        {
            categories.Add(new StayReviewCategoryScore(label, (double)score));
        }
    }

    /// <summary>
    /// Un puntaje dentro de 0-10. Fuera de rango se recorta y se avisa.
    /// </summary>
    /// <remarks>
    /// Recortar y no descartar la propiedad porque un 92 es casi siempre una escala equivocada
    /// —el hotelero pensó sobre 100— y un 10 es la lectura correcta de esa intención. Perder la
    /// propiedad entera por un dedo torcido en un campo opcional sería desproporcionado.
    /// </remarks>
    private static decimal Clamp(string slug, string what, decimal raw, IList<EventContentIssue> issues)
    {
        if (raw >= 0m && raw <= MaxScore)
        {
            return raw;
        }

        var clamped = raw < 0m ? 0m : MaxScore;
        issues.Add(Warn(
            $"La estadía '{slug}' tiene {what} en {raw}, fuera de la escala 0-{MaxScore}; se sirve {clamped}."));
        return clamped;
    }

    private static string Normalize(string? raw)
        => raw?.Trim().ToLowerInvariant() ?? string.Empty;

    private static EventContentIssue Warn(string message)
        => new(EventContentIssueLevel.Warning, message);

    private static EventContentIssue Err(string message)
        => new(EventContentIssueLevel.Error, message);
}
