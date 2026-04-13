using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Utilidad estática para leer atributos DOM transversales (cssId, cssClass)
/// desde cualquier IPublishedElement.
///
/// Centraliza la lógica duplicada que antes vivía en SectionMapperDispatcher
/// y en ElementResolver por separado.
/// </summary>
internal static class DomAttributeReader
{
    public static (string? ElementId, string? CssClasses) Read(IPublishedElement element)
    {
        var id      = element.HasProperty("cssId")    ? element.Value<string>("cssId")    : null;
        var classes = element.HasProperty("cssClass") ? element.Value<string>("cssClass") : null;
        return (id, classes);
    }
}
