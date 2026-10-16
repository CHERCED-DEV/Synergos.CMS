namespace Synergos.CMS.Interfaces;

/// <summary>
/// Por dónde compra una funcionalidad que tiene DOS caminos para la misma intención (ADR 0140): la
/// <see cref="Puerta"/> —el flujo declarado en su orquestador, por <c>/api/flujos/…</c>— o la
/// <see cref="Directa"/> —la ruta de la funcionalidad en el CMS, con el motor que diga su <c>Mode</c>—.
/// </summary>
/// <remarks>
/// <para><b>Ninguno de los dos se retira</b> (Synergos es un superset): el CMS DECLARA cuál gana y el
/// elemento lo sigue. Sin esta declaración el elemento iba siempre a la puerta, y en un despliegue sin
/// orquestador —el clon limpio— cada compra era un 503 <c>puerta.flujo_no_disponible</c> con la ruta directa
/// viva al lado.</para>
///
/// <para><b>La regla de cuál gana</b> (<see cref="Decidir"/>): lo que diga la configuración de negocio de la
/// funcionalidad, si lo dice (con override por sitio, ADR 0137); si no, la puerta cuando ABRE el flujo —está
/// en <c>Synergos:Puerta:Flujos</c> y su orquestador tiene destino—; si no, la directa.</para>
///
/// <para><b>Desde el núcleo N3 la regla es de cualquier flujo</b> (<see cref="CaminoDelFlujo"/>, y el servicio
/// <see cref="ICaminoDeLosFlujos"/>): esto queda, con su API, como la pieza de la compra que la nombró, y
/// delega allá.</para>
/// </remarks>
public static class CaminoDeCompra
{
    /// <summary>La compra va por la puerta de los flujos, al orquestador.</summary>
    public const string Puerta = CaminoDelFlujo.Puerta;

    /// <summary>La compra va por la ruta de la funcionalidad en el CMS.</summary>
    public const string Directa = CaminoDelFlujo.Directa;

    /// <summary>
    /// El camino que gana: <paramref name="declarado"/> si la configuración lo dice; si no, la puerta cuando
    /// <paramref name="laPuertaAbre"/> el flujo, y la directa en otro caso.
    /// </summary>
    /// <param name="declarado">El override de la configuración de negocio, ya normalizado; nulo si no hay.</param>
    /// <param name="laPuertaAbre">Si la puerta abre el flujo de la compra en este despliegue.</param>
    public static string Decidir(string? declarado, bool laPuertaAbre)
        => CaminoDelFlujo.Decidir(declarado, laPuertaAbre);

    /// <summary>
    /// Lo que se escribió en la configuración, comparable: sin espacios y en minúsculas; vacío es nulo (no
    /// declara nada y decide la regla).
    /// </summary>
    public static string? Normalizar(string? valor) => CaminoDelFlujo.Normalizar(valor);

    /// <summary>Si <paramref name="camino"/> (normalizado) es uno de los dos, o nulo.</summary>
    public static bool EsValido(string? camino) => CaminoDelFlujo.EsValido(camino);
}
