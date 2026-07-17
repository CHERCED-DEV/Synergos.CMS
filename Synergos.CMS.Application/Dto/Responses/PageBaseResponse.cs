namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO para el Document Type <c>pageBase</c>.
/// Página editorial estándar bajo siteRoot. Su contenido editorial vive
/// 100% en la prop <c>sections</c> (Layout Composer) — leída por
/// <c>PageBase.cshtml</c> directo del IPublishedContent. Este DTO
/// transporta solo el header SEO de chrome.
/// </summary>
public sealed record PageBaseResponse(string? SeoTitle)
{
    public static PageBaseResponse Project(Func<string, string?> valueAccessor) =>
        new(SeoTitle: valueAccessor("seoTitle"));
}
