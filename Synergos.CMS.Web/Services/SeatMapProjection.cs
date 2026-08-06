using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Projects a <see cref="SeatMapLayout"/> — the rich shape an
/// <see cref="ISeatMapProvider"/> publishes — into the flat <c>seatmap</c> payload the
/// published <c>seat-map</c> bundle actually reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a class and not four lines in the view.</b> Views compile at runtime
/// (<c>RazorCompileOnBuild=false</c>), so a mistake here would not surface until the page is
/// requested — and a Razor exception takes the whole page down. Everything with a decision in
/// it lives here, where <c>dotnet build</c> and the test suite can see it, and the partial is
/// left with nothing but two calls.
/// </para>
/// <para>
/// <b>The UI is the source of truth (ADR 0083).</b> The bundle reads exactly four inputs —
/// <c>config</c>, <c>seatmap</c>, <c>currency</c>, <c>maxSelectable</c> — and its
/// <c>seatmap</c> parser looks at <c>rows[].rowNumber</c>, <c>rows[].serviceClass</c>,
/// <c>rows[].seats[]</c> (<c>id</c>, <c>type</c>, <c>available</c>, <c>price</c>,
/// <c>features[]</c>) and <c>aisleAfterColumns</c>. Nothing else is emitted. Keys are pinned
/// with <see cref="JsonPropertyNameAttribute"/> so they survive whatever naming policy the
/// emitter is configured with.
/// </para>
/// <para>
/// <b>Servicio y categorías ya viajan.</b> El contrato del componente se extendió primero en el
/// repo UI y esta proyección se adaptó después, que es la dirección que fija el ADR 0083.
/// <see cref="SeatMapRow.ServiceClass"/> sale como <c>rows[].serviceClass</c> y el componente
/// dibuja un encabezado cuando la clase cambia; <see cref="SeatMapSeat.Features"/> sale como
/// <c>seats[].features</c> con vocabulario ABIERTO, y <see cref="SeatMapRow.IsExitRow"/> se
/// reparte ahí como <c>exit-row</c> — separado de <c>extra-legroom</c> aunque casi siempre
/// coincidan, porque una fila de salida conlleva requisitos regulatorios que "más espacio" no
/// tiene. <c>type</c> vuelve a ser solo la posición.
/// </para>
/// <para>
/// <b>La geometría también.</b> <c>aisleAfterColumns</c> es una LISTA de índices de columna:
/// un <c>3-3-3</c> emite <c>[3, 6]</c>. Era un solo entero, y con eso una cabina de doble
/// pasillo se dibujaba con uno solo — el bloque derecho soldado al central. Y va en dos
/// niveles: el del mapa, y el de la fila cuya sección tiene otra distribución — la ejecutiva
/// de un 787 es <c>1-2-1</c> sobre una turista <c>3-3-3</c>.
/// </para>
/// <para>
/// <b>Degrades, never throws.</b> A null layout, a layout with no rows, or rows whose seats
/// were all dropped produce <i>no</i> <c>seatmap</c> key at all — the bundle then renders its
/// own empty state.
/// </para>
/// </remarks>
public static class SeatMapProjection
{
    /// <summary>
    /// The column alphabet the bundle uses to letter a seat from its index
    /// (<c>COLUMN_LETTERS</c>). <b>The I is missing on purpose</b>: printed on a seat it reads
    /// as a 1, so no airline uses it. A projection that ignored this would hand the bundle a
    /// seat at index 8 and the passenger would see an <c>I</c> where the boarding pass says
    /// <c>J</c>.
    /// </summary>
    public const string ColumnAlphabet = "ABCDEFGHJK";

    /// <summary>The seat <c>type</c> the bundle falls back to for anything it cannot name.</summary>
    public const string DefaultSeatType = "middle";

    /// <summary>
    /// El rasgo de confort. Vive en <c>features</c>, no en <c>type</c>: una butaca de ventana
    /// con espacio extra sigue siendo de ventana.
    /// </summary>
    public const string ExtraLegroomFeature = "extra-legroom";

    /// <summary>
    /// La marca de fila de salida. Es de la FILA en el dominio y se reparte a cada butaca,
    /// porque el contrato del componente lleva los rasgos por butaca.
    /// </summary>
    public const string ExitRowFeature = "exit-row";

    /// <summary>The only <see cref="SeatMapSeat.Status"/> the passenger can actually pick.</summary>
    public const string FreeStatus = "free";

    /// <summary>Densidad cómoda — la del componente cuando no se le dice nada.</summary>
    public const string ComfortableDensity = "comfortable";

    /// <summary>
    /// Densidad compacta. Achica butaca, huecos y pasillo; <b>no quita ninguna butaca</b>. Una
    /// cabina de 44 filas mide más de mil píxeles de alto y en un móvil deja el resumen y el
    /// botón de compra fuera de la pantalla.
    /// </summary>
    public const string CompactDensity = "compact";

    /// <summary>
    /// 1-based rank of <paramref name="column"/> in <see cref="ColumnAlphabet"/>, or <c>0</c>
    /// when the provider used a column the bundle cannot letter (including a literal
    /// <c>I</c>). Seats are ordered by this rank so the index the bundle letters from lines up
    /// with the column the provider named.
    /// </summary>
    public static int ColumnRank(string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return 0;
        }

        var trimmed = column.Trim();
        if (trimmed.Length != 1)
        {
            return 0;
        }

        var index = ColumnAlphabet.IndexOf(char.ToUpperInvariant(trimmed[0]));
        return index < 0 ? 0 : index + 1;
    }

    /// <summary>
    /// The <c>seatmap</c> payload for <paramref name="layout"/>, or <c>null</c> when there is
    /// nothing to draw.
    /// </summary>
    public static SeatMapPayload? Project(SeatMapLayout? layout)
    {
        if (layout?.Rows is null || layout.Rows.Count == 0)
        {
            return null;
        }

        var rows = new List<SeatMapPayloadRow>(layout.Rows.Count);
        foreach (var row in layout.Rows)
        {
            var projected = ProjectRow(row);
            if (projected is not null)
            {
                rows.Add(projected);
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        return new SeatMapPayload(rows, Aisles(layout.AisleAfterColumns));
    }

    /// <summary>
    /// The full prop bag handed to <c>SynHostEmitRequest.Props</c>: the projected
    /// <c>seatmap</c> (when there is one), plus the two editor-authored knobs.
    /// </summary>
    /// <param name="layout">What the provider resolved, or <c>null</c> when it knew nothing.</param>
    /// <param name="currencyOverride">
    /// The <c>currency</c> the editor typed. Wins over the layout's own currency — the editor
    /// is deciding what the visitor is quoted in. Blank falls back to the layout, and a layout
    /// without one leaves the key out so the bundle applies its own default.
    /// </param>
    /// <param name="maxSelectable">
    /// How many seats one passenger may pick. <c>null</c> or below 1 leaves the key out.
    /// </param>
    /// <param name="density">
    /// <c>comfortable</c> | <c>compact</c>. Cualquier otra cosa —incluido el blanco— deja la
    /// clave fuera y el componente aplica <c>comfortable</c>.
    /// </param>
    /// <param name="hidePrices">
    /// Lo que el editor marcó para <b>quitar</b> los precios. Se emite como <c>showPrices:
    /// false</c>; si no lo marcó, la clave no va y el componente los muestra.
    /// </param>
    /// <param name="hideLegend">Igual, para la leyenda.</param>
    public static IReadOnlyDictionary<string, object?> BuildProps(
        SeatMapLayout? layout,
        string? currencyOverride,
        int? maxSelectable,
        string? density = null,
        bool hidePrices = false,
        bool hideLegend = false)
    {
        var props = new Dictionary<string, object?>(StringComparer.Ordinal);

        var payload = Project(layout);
        if (payload is not null)
        {
            props["seatmap"] = payload;
        }

        var currency = !string.IsNullOrWhiteSpace(currencyOverride)
            ? currencyOverride.Trim().ToUpperInvariant()
            : layout?.Currency?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(currency))
        {
            props["currency"] = currency;
        }

        if (maxSelectable is > 0)
        {
            props["maxSelectable"] = maxSelectable.Value;
        }

        var resolvedDensity = Density(density);
        if (resolvedDensity is not null)
        {
            props["density"] = resolvedDensity;
        }

        // Solo se emite el APAGADO. El componente ya enciende por defecto, y mandar
        // "showPrices": true en cada bloque sería repetir su propio default.
        if (hidePrices)
        {
            props["showPrices"] = false;
        }

        if (hideLegend)
        {
            props["showLegend"] = false;
        }

        return props;
    }

    /// <summary>
    /// La densidad, o <c>null</c> cuando el editor no eligió una que el componente entienda.
    /// </summary>
    /// <remarks>
    /// Omitir vale más que emitir <c>comfortable</c>: si algún día el componente cambia su
    /// default, un bloque que nunca eligió nada debe seguirlo, no quedar clavado al de hoy.
    /// </remarks>
    private static string? Density(string? density)
        => density?.Trim().ToLowerInvariant() switch
        {
            ComfortableDensity => ComfortableDensity,
            CompactDensity => CompactDensity,
            _ => null,
        };

    /// <summary>
    /// Las posiciones de pasillo, saneadas: enteros positivos, sin repetidos y en orden
    /// ascendente.
    /// </summary>
    /// <remarks>
    /// El orden no es cosmético: el componente las compara mientras recorre la fila de
    /// izquierda a derecha. Y la lista vacía se emite igual —no se omite— porque el componente
    /// distingue "sin geometría" (parte la fila más ancha por la mitad) de "geometría conocida
    /// sin pasillos", y un teatro de una sola grilla es lo segundo.
    /// </remarks>
    private static IReadOnlyList<int> Aisles(IReadOnlyList<int>? declared)
    {
        if (declared is null || declared.Count == 0)
        {
            return Array.Empty<int>();
        }

        return declared.Where(c => c > 0).Distinct().OrderBy(c => c).ToList();
    }

    private static SeatMapPayloadRow? ProjectRow(SeatMapRow? row)
    {
        if (row?.Seats is null || row.Seats.Count == 0)
        {
            return null;
        }

        var seats = row.Seats
            .Where(seat => seat is not null && !string.IsNullOrWhiteSpace(seat.Id))
            // OrderBy is stable, so unknown columns keep the provider's order among themselves.
            .OrderBy(SortKey)
            .Select(seat => ProjectSeat(seat, row.IsExitRow))
            .ToList();

        if (seats.Count == 0)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(row.Label) ? string.Empty : row.Label.Trim();
        var serviceClass = string.IsNullOrWhiteSpace(row.ServiceClass)
            ? null
            : row.ServiceClass.Trim().ToLowerInvariant();

        // null se OMITE del JSON y significa "usa los del mapa"; una lista vacía SÍ viaja y
        // significa "esta fila no tiene ningún pasillo". Colapsarlas dejaría la sección corta
        // de un widebody dibujada con los pasillos de la larga.
        var aisles = row.AisleAfterColumns is null ? null : Aisles(row.AisleAfterColumns);

        return new SeatMapPayloadRow(label, seats, serviceClass, aisles);
    }

    /// <summary>Unknown columns sort last instead of first, so they never displace a lettered seat.</summary>
    private static int SortKey(SeatMapSeat seat)
    {
        var rank = ColumnRank(seat.Column);
        return rank == 0 ? int.MaxValue : rank;
    }

    private static SeatMapPayloadSeat ProjectSeat(SeatMapSeat seat, bool isExitRow) => new(
        seat.Id.Trim(),
        SeatType(seat),
        // Anything that is not `free` is unavailable to this visitor: `sold` and `blocked`
        // never come back, and a `held` seat belongs to someone else's checkout right now.
        string.Equals(seat.Status?.Trim(), FreeStatus, StringComparison.OrdinalIgnoreCase),
        seat.Price > 0m ? seat.Price : 0m,
        Features(seat, isExitRow));

    /// <summary>
    /// La POSICIÓN de la butaca. Ya no se pisa con el confort.
    /// </summary>
    /// <remarks>
    /// Antes, una butaca con <c>extra-legroom</c> se emitía con ese valor en <c>type</c> y
    /// perdía su posición: una de ventana con espacio extra dejaba de ser de ventana. El
    /// contrato del componente ya separa las dos cosas (ADR 0127), así que aquí solo va la
    /// posición y el confort viaja en <c>features</c>.
    /// </remarks>
    private static string SeatType(SeatMapSeat seat)
        => seat.Position?.Trim().ToLowerInvariant() switch
        {
            "window" => "window",
            "aisle" => "aisle",
            "middle" => "middle",
            _ => DefaultSeatType,
        };

    /// <summary>
    /// Los rasgos de la butaca, normalizados y sin repetidos.
    /// </summary>
    /// <remarks>
    /// <b><c>IsExitRow</c> es de la FILA y se reparte a cada butaca</b>, porque el contrato del
    /// componente lleva los rasgos por butaca. No se pliega dentro de <c>extra-legroom</c>
    /// aunque casi siempre coincidan: una fila de salida conlleva requisitos regulatorios
    /// —edad mínima, nada en el piso— que "más espacio" no tiene, y confundirlos borraría del
    /// mapa la única marca que los comunica.
    ///
    /// <para>El vocabulario es ABIERTO: lo que el proveedor mande pasa tal cual. Filtrar contra
    /// una lista conocida haría que un rasgo nuevo exigiera desplegar el CMS.</para>
    /// </remarks>
    private static IReadOnlyList<string>? Features(SeatMapSeat seat, bool isExitRow)
    {
        var features = new List<string>(2);

        if (isExitRow)
        {
            features.Add(ExitRowFeature);
        }

        foreach (var raw in seat.Features ?? Array.Empty<string>())
        {
            var feature = raw?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(feature) && !features.Contains(feature, StringComparer.Ordinal))
            {
                features.Add(feature);
            }
        }

        // Null en vez de lista vacía: el emisor omite los nulos, y una carga con
        // "features":[] en cada butaca es ruido en cada request de la ficha.
        return features.Count > 0 ? features : null;
    }
}

/// <summary>
/// The <c>seatmap</c> payload, shaped exactly as the published bundle parses it.
/// </summary>
public sealed record SeatMapPayload(
    [property: JsonPropertyName("rows")] IReadOnlyList<SeatMapPayloadRow> Rows,
    // Los pasillos, por índice de columna 1-based. Es una LISTA porque un widebody tiene dos:
    // con un solo valor, un 3-3-3 no dibujaba nada entre F y G.
    [property: JsonPropertyName("aisleAfterColumns")] IReadOnlyList<int> AisleAfterColumns);

/// <summary>One row of the payload. <c>rowNumber</c> stays a string — real rows skip 13.</summary>
public sealed record SeatMapPayloadRow(
    [property: JsonPropertyName("rowNumber")] string RowNumber,
    [property: JsonPropertyName("seats")] IReadOnlyList<SeatMapPayloadSeat> Seats,
    // Sección de cabina. Null se omite del JSON, y un mapa sin clases se ve
    // exactamente como antes de que el contrato las admitiera.
    [property: JsonPropertyName("serviceClass")] string? ServiceClass = null,
    // Los pasillos propios de la fila. Null se omite y la fila usa los del mapa;
    // una lista VACÍA sí viaja y declara una fila sin ningún pasillo.
    [property: JsonPropertyName("aisleAfterColumns")] IReadOnlyList<int>? AisleAfterColumns = null);

/// <summary>
/// One seat of the payload. <c>type</c> es SOLO la posición (<c>window|aisle|middle</c>); el
/// confort y la marca de salida viajan en <c>features</c>, que es vocabulario abierto.
/// </summary>
public sealed record SeatMapPayloadSeat(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("price")] decimal Price,
    // Rasgos de la butaca. Null se omite del JSON.
    [property: JsonPropertyName("features")] IReadOnlyList<string>? Features = null);
