namespace Synergos.CMS.Application.Dinero;

/// <summary>
/// Un importe en las unidades MENORES de su moneda, que es lo que viaja en todo campo
/// <c>*Minor</c> de una API: COP, USD y EUR con dos decimales (centavos); CLP y JPY sin
/// decimales; KWD con tres.
/// </summary>
/// <remarks>
/// <para><b>Qué cierra (#196).</b> Cada borde decidía la unidad por su cuenta. El copago de
/// salud mandaba centavos, y la tasa de un trámite y la facturación de salud mandaban PESOS con
/// el nombre <c>feeMinor</c>/<c>balanceMinor</c> («COP no tiene centavos»). Medido en el sitio
/// real: salud dividía por 100 y pintaba un saldo de 123.500 como <b>$ 1.235</b> y una consulta de
/// 80.000 como $ 800. Gobierno se salvaba por casualidad: sacaba su factor de <c>Intl</c>, que en
/// el Chromium del navegador da 0 decimales a COP (y 2 en Node, donde corren los tests).</para>
///
/// <para><b>La tabla es la de ISO-4217, ESCRITA</b> aquí y en el UI (<c>desdeMenores</c> de
/// <c>vitals/core/src/formato</c>), y no leída de <c>Intl</c>: los motores no coinciden —el Chromium
/// del navegador dice que COP tiene 0 decimales y el Node de los tests, 2—, y una unidad que
/// depende de dónde corre el código es justo el defecto. Lo comprueban los vectores de oro
/// <c>docs/contracts/minor-units-vectors.json</c>, que corren aquí y en el UI (G-13).</para>
///
/// <para><b>Redondeo al par</b>, el de la casa: un importe con más decimales que su moneda se
/// redondea como en <c>Synergos.Core.Money</c>.</para>
/// </remarks>
public static class UnidadesMenores
{
    /// <summary>Los decimales de la mayoría de las monedas, y el de una que no se sabe.</summary>
    public const int DecimalesPorDefecto = 2;

    /// <summary>Las monedas que en ISO-4217 no tienen dos decimales.</summary>
    private static readonly IReadOnlyDictionary<string, int> Excepciones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0,
        ["KRW"] = 0, ["PYG"] = 0, ["RWF"] = 0, ["UGX"] = 0, ["UYI"] = 0, ["VND"] = 0, ["VUV"] = 0,
        ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        ["CLF"] = 4, ["UYW"] = 4,
    };

    /// <summary>Cuántos decimales tiene la unidad menor de <paramref name="moneda"/>.</summary>
    public static int Decimales(string? moneda)
        => !string.IsNullOrWhiteSpace(moneda) && Excepciones.TryGetValue(moneda.Trim(), out var decimales)
            ? decimales
            : DecimalesPorDefecto;

    /// <summary>
    /// <paramref name="importe"/> (en unidades mayores: pesos) en las unidades menores de su
    /// moneda, redondeado al par.
    /// </summary>
    public static long Desde(decimal importe, string? moneda)
        => (long)Math.Round(importe * Factor(moneda), MidpointRounding.ToEven);

    /// <summary>De unidades menores a mayores.</summary>
    public static decimal Hacia(long menores, string? moneda) => menores / Factor(moneda);

    private static decimal Factor(string? moneda) => Decimales(moneda) switch
    {
        0 => 1m,
        3 => 1_000m,
        4 => 10_000m,
        _ => 100m,
    };
}
