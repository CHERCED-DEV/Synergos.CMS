using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La configuración de una cabina que el stub sabe emular.
/// </summary>
/// <param name="Ref">Referencia con la que se pide. Ej: <c>a320-bog-mdE</c>.</param>
/// <param name="Name">Cómo se le muestra al pasajero.</param>
/// <param name="Formula">
/// La distribución, en notación aeronáutica: <c>3-3</c>, <c>2-4-2</c>, <c>1-2-1</c>. Cada
/// número es un bloque de asientos y cada guion un pasillo.
/// </param>
/// <param name="Sections">Las secciones de la cabina, de proa a popa.</param>
/// <param name="ExitRows">Qué filas son de salida de emergencia.</param>
/// <param name="BlockedSeats">Butacas que existen pero no se venden (tripulación, avería).</param>
public sealed record CabinSpec(
    string Ref,
    string Name,
    string Formula,
    IReadOnlyList<CabinSectionSpec> Sections,
    IReadOnlyList<int>? ExitRows = null,
    IReadOnlyList<string>? BlockedSeats = null);

/// <summary>Un tramo contiguo de filas con la misma clase de servicio y el mismo precio.</summary>
/// <param name="ServiceClass"><c>first</c> | <c>business</c> | <c>premium</c> | <c>economy</c>.</param>
/// <param name="FirstRow">Primera fila del tramo, inclusive.</param>
/// <param name="LastRow">Última fila del tramo, inclusive.</param>
/// <param name="Price">Recargo por elegir butaca en este tramo. 0 = sin recargo.</param>
public sealed record CabinSectionSpec(string ServiceClass, int FirstRow, int LastRow, decimal Price);

/// <summary>
/// Proveedor de mapas de asientos que EMULA una cabina de avión. Es el default stub-first de
/// <see cref="ISeatMapProvider"/>.
/// </summary>
/// <remarks>
/// <b>Emula, no inventa.</b> Reproduce lo que hace de verdad un sistema de aerolínea: filas
/// numeradas que se saltan la 13, columnas que se saltan la I, un pasillo en el sitio que dicta
/// la distribución, secciones por clase de servicio, filas de salida con más espacio, y butacas
/// bloqueadas que no son "vendidas". Todo eso importa porque es lo que hace que el mapa se vea
/// creíble y que el adapter real, cuando llegue, no cambie la forma del dato.
///
/// <para><b>Determinista.</b> La misma referencia da siempre el mismo mapa, incluidas las
/// butacas ocupadas: se derivan de un hash estable de la referencia y el asiento, no de un
/// aleatorio. Una demo donde el mapa cambia en cada refresco no se puede grabar ni enseñar, y
/// un test sobre ella sería intermitente.</para>
///
/// <para>Lógica pura en Application (ADR 0002). El adapter real —un GDS, el sistema de la
/// aerolínea, el del recinto— implementa la misma seam y se registra en su lugar sin tocar el
/// CMS ni el componente.</para>
/// </remarks>
public sealed class StubCabinSeatMapProvider : ISeatMapProvider
{
    private const string Currency = "COP";

    /// <summary>
    /// Las letras de columna, <b>sin la I</b>. En un asiento impreso la I se confunde con un 1,
    /// y por eso ninguna aerolínea la usa. El componente que pinta el mapa aplica la misma
    /// regla, así que apartarse de ella desalinearía el rótulo del servidor con el del cliente.
    /// </summary>
    internal const string ColumnLetters = "ABCDEFGHJK";

    /// <summary>
    /// La fila 13 se salta. No es superstición del código: media flota mundial la omite, y un
    /// mapa que la incluye se lee como falso al primer vistazo de alguien que vuela seguido.
    /// </summary>
    private const int SkippedRow = 13;

    private readonly IReadOnlyDictionary<string, CabinSpec> _cabins;

    /// <summary>Ctor por defecto: las tres cabinas sembradas.</summary>
    public StubCabinSeatMapProvider()
        : this(DefaultCabins())
    {
    }

    public StubCabinSeatMapProvider(IEnumerable<CabinSpec> cabins)
    {
        ArgumentNullException.ThrowIfNull(cabins);
        _cabins = cabins.ToDictionary(c => c.Ref, StringComparer.OrdinalIgnoreCase);
    }

    public Task<SeatMapLayout?> GetLayoutAsync(string mapRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapRef) || !_cabins.TryGetValue(mapRef.Trim(), out var spec))
        {
            // Una referencia que el proveedor no conoce es un dato del editor, no una falla del
            // sistema: degrada la pantalla, no la tumba.
            return Task.FromResult<SeatMapLayout?>(null);
        }

        return Task.FromResult<SeatMapLayout?>(Build(spec));
    }

    /// <summary>
    /// Arma el mapa de una cabina. Es determinista: misma referencia, mismo resultado.
    /// </summary>
    internal static SeatMapLayout Build(CabinSpec spec)
    {
        var blocks = ParseFormula(spec.Formula);
        var columns = BuildColumns(blocks);
        var aisles = ResolveAisles(blocks);
        var exitRows = new HashSet<int>(spec.ExitRows ?? Array.Empty<int>());
        var blocked = new HashSet<string>(spec.BlockedSeats ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<SeatMapRow>();
        foreach (var section in spec.Sections)
        {
            for (var rowNumber = section.FirstRow; rowNumber <= section.LastRow; rowNumber++)
            {
                if (rowNumber == SkippedRow)
                {
                    continue;
                }

                var isExit = exitRows.Contains(rowNumber);
                var seats = new List<SeatMapSeat>(columns.Count);

                for (var i = 0; i < columns.Count; i++)
                {
                    var column = columns[i];
                    var id = $"{rowNumber}{column}";

                    var features = new List<string>(2);
                    if (isExit)
                    {
                        // La fila de salida SIEMPRE tiene más espacio: es el requisito
                        // regulatorio que la define, no una cortesía comercial.
                        features.Add("exit-row");
                        features.Add("extra-legroom");
                    }

                    seats.Add(new SeatMapSeat(
                        Id: id,
                        Column: column,
                        Status: blocked.Contains(id) ? "blocked"
                            : IsSold(spec.Ref, id) ? "sold"
                            : "free",
                        Position: ResolvePosition(i, blocks),
                        Price: section.Price,
                        Features: features.Count > 0 ? features : null));
                }

                rows.Add(new SeatMapRow(
                    Label: rowNumber.ToString(),
                    ServiceClass: section.ServiceClass,
                    Seats: seats,
                    IsExitRow: isExit));
            }
        }

        return new SeatMapLayout(spec.Ref, spec.Name, Currency, aisles, rows);
    }

    /// <summary>
    /// Dónde caen los pasillos de una distribución: en los bordes ENTRE bloques.
    /// <c>[3,3,3]</c> → <c>[3, 6]</c>; <c>[1,2,1]</c> → <c>[1, 3]</c>; <c>[3,3]</c> → <c>[3]</c>.
    /// </summary>
    /// <remarks>
    /// Son las sumas acumuladas <b>menos la última</b>: un pasillo después de la última columna
    /// no separa nada, quedaría colgando del borde de la cabina.
    /// </remarks>
    internal static IReadOnlyList<int> ResolveAisles(IReadOnlyList<int> blocks)
    {
        var aisles = new List<int>(Math.Max(blocks.Count - 1, 0));
        var running = 0;
        for (var i = 0; i < blocks.Count - 1; i++)
        {
            running += blocks[i];
            aisles.Add(running);
        }

        return aisles;
    }

    /// <summary>
    /// <c>"2-4-2"</c> → <c>[2, 4, 2]</c>. Una fórmula ilegible cae a <c>3-3</c>.
    /// </summary>
    /// <remarks>
    /// Cae en vez de lanzar porque la fórmula la escribe una persona: un mapa con la
    /// distribución más común del mundo es un desperfecto visible que se corrige; una excepción
    /// en la ficha es una pantalla en blanco.
    /// </remarks>
    internal static IReadOnlyList<int> ParseFormula(string? formula)
    {
        var blocks = (formula ?? string.Empty)
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var n) && n > 0 ? n : 0)
            .Where(n => n > 0)
            .ToList();

        if (blocks.Count == 0 || blocks.Sum() > ColumnLetters.Length)
        {
            return new[] { 3, 3 };
        }

        return blocks;
    }

    private static IReadOnlyList<string> BuildColumns(IReadOnlyList<int> blocks)
        => Enumerable.Range(0, blocks.Sum())
            .Select(i => ColumnLetters[i].ToString())
            .ToList();

    /// <summary>
    /// Ventana, pasillo o centro, según dónde cae la columna dentro de su bloque.
    /// </summary>
    /// <remarks>
    /// <b>Se deriva de la geometría, no del índice global.</b> En un <c>2-4-2</c> la columna C
    /// es pasillo y la D es centro; contar desde el borde de la fila diría otra cosa. Solo los
    /// bloques de los extremos tienen ventana — el bloque central de un widebody no tiene
    /// ninguna, por evidente que parezca al mirar un <c>3-3</c>.
    /// </remarks>
    internal static string ResolvePosition(int columnIndex, IReadOnlyList<int> blocks)
    {
        var offset = 0;
        for (var b = 0; b < blocks.Count; b++)
        {
            var size = blocks[b];
            if (columnIndex >= offset + size)
            {
                offset += size;
                continue;
            }

            var withinBlock = columnIndex - offset;
            var isFirstBlock = b == 0;
            var isLastBlock = b == blocks.Count - 1;
            var atBlockStart = withinBlock == 0;
            var atBlockEnd = withinBlock == size - 1;

            // Ventana solo en el borde EXTERIOR de los bloques de los extremos.
            if ((isFirstBlock && atBlockStart) || (isLastBlock && atBlockEnd))
            {
                // Un bloque de una sola butaca en un extremo (1-2-1) es ventana y pasillo a la
                // vez; gana ventana, que es lo que el pasajero elige.
                return "window";
            }

            // Pasillo en el borde INTERIOR: el que da al corredor.
            if ((!isLastBlock && atBlockEnd) || (!isFirstBlock && atBlockStart))
            {
                return "aisle";
            }

            return "middle";
        }

        return "middle";
    }

    /// <summary>
    /// Si la butaca está vendida. Determinista a partir de (referencia, butaca).
    /// </summary>
    /// <remarks>
    /// Un aleatorio daría un mapa distinto en cada refresco: imposible de grabar para una demo
    /// e imposible de testear sin intermitencias. El reparto ronda un tercio ocupado, que es lo
    /// que hace que el mapa se vea usado sin dejar al comprador sin opciones.
    /// </remarks>
    internal static bool IsSold(string cabinRef, string seatId)
    {
        // FNV-1a: barato, estable entre procesos y sin pretensión criptográfica — aquí no
        // protege nada, solo reparte.
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in cabinRef + "|" + seatId)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash % 3 == 0;
    }

    /// <summary>
    /// Tres cabinas sembradas que cubren las tres distribuciones que existen de verdad.
    /// </summary>
    private static IReadOnlyList<CabinSpec> DefaultCabins() => new[]
    {
        // Narrowbody: el caballito de batalla doméstico.
        new CabinSpec(
            Ref: "a320-domestico",
            Name: "Airbus A320 · Cabina doméstica",
            Formula: "3-3",
            Sections: new[]
            {
                new CabinSectionSpec("business", 1, 3, 180_000m),
                new CabinSectionSpec("premium", 4, 8, 90_000m),
                new CabinSectionSpec("economy", 9, 30, 0m),
            },
            ExitRows: new[] { 12, 14 },
            BlockedSeats: new[] { "30A", "30F" }),

        // Widebody: el caso que rompe cualquier suposición de "el pasillo va a la mitad".
        new CabinSpec(
            Ref: "b787-internacional",
            Name: "Boeing 787 · Cabina internacional",
            Formula: "3-3-3",
            Sections: new[]
            {
                new CabinSectionSpec("business", 1, 6, 1_200_000m),
                new CabinSectionSpec("premium", 7, 12, 420_000m),
                new CabinSectionSpec("economy", 14, 44, 0m),
            },
            ExitRows: new[] { 20, 21 },
            BlockedSeats: Array.Empty<string>()),

        // Suites: el bloque de una butaca en los extremos, que es ventana y pasillo a la vez.
        new CabinSpec(
            Ref: "a350-suites",
            Name: "Airbus A350 · Suites",
            Formula: "1-2-1",
            Sections: new[]
            {
                new CabinSectionSpec("first", 1, 4, 2_400_000m),
                new CabinSectionSpec("business", 5, 12, 900_000m),
            },
            ExitRows: new[] { 5 },
            BlockedSeats: Array.Empty<string>()),
    };
}
