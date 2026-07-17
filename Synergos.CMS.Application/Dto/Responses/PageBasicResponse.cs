namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO para el Document Type <c>pageBasic</c>.
/// Página simple — contenido editorial 100% via <c>sections</c>
/// (Layout Composer). Este DTO solo transporta el SEO header.
/// </summary>
public sealed record PageBasicResponse(string? SeoTitle)
{
    public static PageBasicResponse Project(Func<string, string?> valueAccessor) =>
        new(SeoTitle: valueAccessor("seoTitle"));
}
