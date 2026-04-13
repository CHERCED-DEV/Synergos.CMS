using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Extensiones internas de lectura de propiedades Umbraco.
///
/// Centraliza la lógica de extracción de campos comunes (imagen, enlace,
/// visibilidad, tracking) para que cada mapper de sección la reutilice
/// sin duplicación. Solo esta capa llama a .Value&lt;T&gt;() sobre IPublishedElement.
/// </summary>
internal static class ContentExtensions
{
    internal static bool ReadIsVisible(this IPublishedElement element)
        => !element.HasProperty("isVisible")
           || element.Value<bool>(
               "isVisible",
               fallback: Umbraco.Cms.Core.Models.PublishedContent.Fallback.ToDefaultValue,
               defaultValue: true);

    internal static Image? ReadImage(this IPublishedElement element, string alias)
    {
        var media = element.Value<IPublishedContent>(alias);
        if (media is null) return null;
        var w = media.Value<int>("umbracoWidth");
        var h = media.Value<int>("umbracoHeight");
        return new Image(media.Url(), media.Name, w > 0 ? w : null, h > 0 ? h : null);
    }

    internal static Link? ReadLink(this IPublishedElement element, string alias)
    {
        var link = element.Value<Umbraco.Cms.Core.Models.Link>(alias);
        return link is null ? null : new Link(link.Url ?? "#", link.Target ?? "_self", link.Name);
    }

    internal static TrackingData? ReadTracking(this IPublishedElement element)
    {
        if (!element.HasProperty("trackingLabel")) return null;

        return new TrackingData(
            element.Value<string>("trackingLabel"),
            element.Value<string>("trackingCategory"),
            element.Value<string>("trackingAction"));
    }

    /// <summary>
    /// Convierte IHtmlContent a string en el límite de la capa de aplicación,
    /// evitando que la dependencia de Microsoft.AspNetCore.Html se filtre al dominio.
    /// </summary>
    internal static string? ReadHtml(this IPublishedElement element, string alias)
    {
        if (!element.HasProperty(alias)) return null;

        var html = element.Value<Microsoft.AspNetCore.Html.IHtmlContent>(alias)?.ToString();
        if (!string.IsNullOrWhiteSpace(html)) return html;

        var text = element.Value<string>(alias);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        return element.Value<object>(alias)?.ToString();
    }

}
