namespace Synergos.CMS.Interfaces;

/// <summary>
/// Por dónde va un flujo que tiene DOS caminos para la misma intención (ADR 0140, núcleo N3 FR-034): la
/// <see cref="Puerta"/> —el flujo declarado en su orquestador, por <c>/api/flujos/…</c>— o la
/// <see cref="Directa"/> —la ruta de la funcionalidad en el CMS—. Es la regla de <see cref="CaminoDeCompra"/>,
/// generalizada a cualquier flujo.
/// </summary>
/// <remarks>
/// <para><b>Cuál gana</b> (<see cref="Decidir(string?, string?, bool)"/>): lo que declara el sitio, si lo
/// declara (la configuración de negocio de la funcionalidad, con override por sitio, ADR 0137); si no, lo que
/// declara el flujo en <c>Synergos:Puerta:Flujos:{flujo}:Camino</c>; si no, la puerta cuando ABRE el flujo, y
/// la directa en otro caso.</para>
///
/// <para><b>Dos declaraciones que no coinciden son un defecto</b>, no una preferencia: quien escribió una
/// creyó elegir y la otra decide (<see cref="SeContradicen"/>). Lo vigila <c>CaminoDeLosFlujosTests</c>
/// sobre la configuración del repo, y en ejecución queda en el log.</para>
/// </remarks>
public static class CaminoDelFlujo
{
    /// <summary>El flujo va por la puerta, a su orquestador.</summary>
    public const string Puerta = "puerta";

    /// <summary>El flujo va por la ruta de la funcionalidad en el CMS.</summary>
    public const string Directa = "directa";

    /// <summary>
    /// El camino que gana con UNA declaración: <paramref name="declarado"/> si la hay; si no, la puerta cuando
    /// <paramref name="laPuertaAbre"/> el flujo, y la directa en otro caso.
    /// </summary>
    public static string Decidir(string? declarado, bool laPuertaAbre)
        => declarado ?? (laPuertaAbre ? Puerta : Directa);

    /// <summary>
    /// El camino que gana con las dos declaraciones, ya normalizadas: la del sitio, la del flujo, la regla.
    /// </summary>
    public static string Decidir(string? delSitio, string? delFlujo, bool laPuertaAbre)
        => Decidir(delSitio ?? delFlujo, laPuertaAbre);

    /// <summary>Lo escrito, comparable: sin espacios y en minúsculas; vacío es nulo (no declara nada).</summary>
    public static string? Normalizar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim().ToLowerInvariant();

    /// <summary>Si <paramref name="camino"/> (normalizado) es uno de los dos, o nulo.</summary>
    public static bool EsValido(string? camino) => camino is null or Puerta or Directa;

    /// <summary>Si dos declaraciones (normalizadas) dicen cosas distintas: las dos están y no coinciden.</summary>
    public static bool SeContradicen(string? una, string? otra)
        => una is not null && otra is not null && !string.Equals(una, otra, StringComparison.Ordinal);
}
