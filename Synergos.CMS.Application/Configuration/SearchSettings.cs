namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Search</c>. Configura la búsqueda full-text del CMS
/// (campos a buscar, max items, denylist de DocTypes para excluir
/// schema interno).
/// </summary>
public sealed class SearchSettings
{
    /// <summary>
    /// Campos del nodo a buscar (con OR semántico). Default cubre los
    /// campos editoriales más comunes: Name, seoTitle, seoDescription,
    /// excerpt, plus el campo nodeName que Examine indexa por default.
    /// </summary>
    public string[] SearchableFields { get; init; } =
    {
        "nodeName",
        "seoTitle",
        "seoDescription",
        "excerpt",
        "summaryText",
    };

    /// <summary>
    /// Aliases de DocType a EXCLUIR de los resultados. Default cubre
    /// los DocTypes de Settings y de configuración que el visitante
    /// no debería ver en una búsqueda pública.
    /// </summary>
    public string[] ExcludedDocTypeAliases { get; init; } =
    {
        "siteConfigSettings",
        "themeSettings",
        "platformSettings",
        "flowDefinition",
        "flowStep",
        "reusableBlock",
    };

    /// <summary>
    /// Máximo absoluto de hits que el provider devuelve por query —
    /// hard cap defensivo, override desde <see cref="SearchRequest.MaxItems"/>
    /// no puede superarlo. Default 100.
    /// </summary>
    public int MaxHitsHardCap { get; init; } = 100;

    /// <summary>
    /// Largo del excerpt generado a partir del primer match en el
    /// contenido del nodo. Default 200 chars. Excerpts más largos
    /// pesan más en la respuesta sin agregar utilidad.
    /// </summary>
    public int ExcerptMaxLength { get; init; } = 200;
}
