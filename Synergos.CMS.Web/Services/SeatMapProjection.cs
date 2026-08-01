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
/// <c>seatmap</c> parser only looks at <c>rows[].rowNumber</c>, <c>rows[].seats[]</c>
/// (<c>id</c>, <c>type</c>, <c>available</c>, <c>price</c>) and <c>aisleAfterColumns</c>.
/// Nothing else is emitted. Keys are pinned with <see cref="JsonPropertyNameAttribute"/> so
/// they survive whatever naming policy the emitter is configured with.
/// </para>
/// <para>
/// <b>What the projection deliberately drops</b>, because the published contract has nowhere
/// to put it: <see cref="SeatMapRow.ServiceClass"/> (there is no per-row class key),
/// <see cref="SeatMapRow.IsExitRow"/> (an exit row is not a seat <c>type</c>, and folding it
/// into <c>extra-legroom</c> would erase the regulatory meaning), and every
/// <see cref="SeatMapSeat.Features"/> value other than <c>extra-legroom</c> — the one feature
/// the bundle's <c>type</c> enum can name. Extending the contract is a UI-repo decision;
/// emitting keys nobody reads would be exactly the drift this repo fights.
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

    /// <summary>The one <see cref="SeatMapSeat.Features"/> value the bundle's <c>type</c> can express.</summary>
    public const string ExtraLegroomFeature = "extra-legroom";

    /// <summary>The only <see cref="SeatMapSeat.Status"/> the passenger can actually pick.</summary>
    public const string FreeStatus = "free";

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

        // The aisle position is passed through verbatim. It is the only geometry the bundle
        // consumes, and without it the bundle splits the widest row in half — which on a
        // 2-4-2 puts the aisle between the two middle seats instead of after column B.
        var aisle = layout.AisleAfterColumns > 0 ? layout.AisleAfterColumns : 0;
        return new SeatMapPayload(rows, aisle);
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
    public static IReadOnlyDictionary<string, object?> BuildProps(
        SeatMapLayout? layout,
        string? currencyOverride,
        int? maxSelectable)
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

        return props;
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
            .Select(ProjectSeat)
            .ToList();

        if (seats.Count == 0)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(row.Label) ? string.Empty : row.Label.Trim();
        return new SeatMapPayloadRow(label, seats);
    }

    /// <summary>Unknown columns sort last instead of first, so they never displace a lettered seat.</summary>
    private static int SortKey(SeatMapSeat seat)
    {
        var rank = ColumnRank(seat.Column);
        return rank == 0 ? int.MaxValue : rank;
    }

    private static SeatMapPayloadSeat ProjectSeat(SeatMapSeat seat) => new(
        seat.Id.Trim(),
        SeatType(seat),
        // Anything that is not `free` is unavailable to this visitor: `sold` and `blocked`
        // never come back, and a `held` seat belongs to someone else's checkout right now.
        string.Equals(seat.Status?.Trim(), FreeStatus, StringComparison.OrdinalIgnoreCase),
        seat.Price > 0m ? seat.Price : 0m);

    private static string SeatType(SeatMapSeat seat)
    {
        if (seat.Features is not null)
        {
            foreach (var feature in seat.Features)
            {
                if (string.Equals(feature?.Trim(), ExtraLegroomFeature, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtraLegroomFeature;
                }
            }
        }

        return seat.Position?.Trim().ToLowerInvariant() switch
        {
            "window" => "window",
            "aisle" => "aisle",
            "middle" => "middle",
            _ => DefaultSeatType,
        };
    }
}

/// <summary>
/// The <c>seatmap</c> payload, shaped exactly as the published bundle parses it.
/// </summary>
public sealed record SeatMapPayload(
    [property: JsonPropertyName("rows")] IReadOnlyList<SeatMapPayloadRow> Rows,
    [property: JsonPropertyName("aisleAfterColumns")] int AisleAfterColumns);

/// <summary>One row of the payload. <c>rowNumber</c> stays a string — real rows skip 13.</summary>
public sealed record SeatMapPayloadRow(
    [property: JsonPropertyName("rowNumber")] string RowNumber,
    [property: JsonPropertyName("seats")] IReadOnlyList<SeatMapPayloadSeat> Seats);

/// <summary>
/// One seat of the payload. <c>type</c> is the bundle's four-value enum
/// (<c>window|aisle|middle|extra-legroom</c>), not the provider's richer position + features.
/// </summary>
public sealed record SeatMapPayloadSeat(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("price")] decimal Price);
