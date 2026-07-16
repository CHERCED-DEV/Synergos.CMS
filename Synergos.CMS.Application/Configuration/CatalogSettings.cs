namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado que se bindea de la sección <c>Synergos:Catalog</c> de
/// <c>appsettings.*.json</c>. Configura el motor de catálogo transversal (T5, ADR 0107):
/// topes de paginación y de qué fuente sale el catálogo de cada vertical.
/// </summary>
public sealed class CatalogSettings
{
    /// <summary>
    /// Tope duro del tamaño de página. Un <c>?take=</c> mayor se capa a esto en vez de
    /// honrarse: sin el tope, <c>?take=1000000</c> materializa y serializa el catálogo
    /// entero por petición. Default 96 = 4 páginas de 24.
    /// </summary>
    public int MaxTake { get; init; } = 96;

    /// <summary>
    /// Tope de ítems sobre los que se cuentan las facetas. Las facetas se cuentan sobre
    /// TODO el universo filtrado (no sobre la página), que es lo correcto pero también lo
    /// caro; esto pone el techo. Default 5.000 = el mismo umbral en el que el ADR 0107
    /// manda reabrir la discusión de Lucene.
    /// </summary>
    public int FacetUniverseHardCap { get; init; } = 5_000;

    /// <summary>
    /// Ajustes para una seam que <b>NO pagina</b> y promete TODAS las coincidencias.
    /// </summary>
    /// <remarks>
    /// <b>Por qué existe:</b> <see cref="MaxTake"/> protege del WIRE — que un
    /// <c>?take=1000000</c> no materialice el catálogo entero por petición. Pero cuatro de
    /// los cinco catálogos (Trámites, Propiedades, Eventos, Educación) tienen seams que
    /// devuelven la lista completa por contrato y no están expuestas a un <c>?take=</c>: para
    /// ellas, materializar todo ES el contrato. Con el tope por defecto, un catálogo de más
    /// de 96 ítems perdería el resto EN SILENCIO — y tres de esos verticales tienen
    /// <c>Publish*</c>, así que sus catálogos crecen en runtime.
    ///
    /// <para>El tope se quita por CONFIGURACIÓN de esa instancia del motor, no tocando el
    /// motor: la regla sigue siendo que si hay que tocarlo para acomodar un vertical, el
    /// descriptor está mal modelado.</para>
    /// </remarks>
    public static CatalogSettings Unpaged { get; } = new() { MaxTake = int.MaxValue };

    /// <summary>
    /// De qué fuente sale el catálogo de cada vertical: <c>demo</c> (el seed en memoria de
    /// siempre) o <c>cms</c> (el contenido que autoró el editor). Clave = el vertical
    /// (<c>Shop</c>, <c>Events</c>…).
    /// </summary>
    /// <remarks>
    /// Calco del gating de proveedor de T3/ADR 0104. Default <c>demo</c> en todos:
    /// mientras la Ola A no esté verificada, el comportamiento no cambia, y el rollback del
    /// flip es una línea de config sin redeploy. Un vertical ausente del diccionario es
    /// <c>demo</c>: la omisión nunca activa una fuente nueva.
    /// </remarks>
    public Dictionary<string, string> Sources { get; init; } = new();
}
