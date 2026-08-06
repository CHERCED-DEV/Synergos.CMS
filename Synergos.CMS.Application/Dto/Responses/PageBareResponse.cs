namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO para el Document Type <c>pageBare</c>.
/// Variante chrome-less — contenido 100% via <c>sections</c>
/// (Layout Composer). Este DTO solo transporta el SEO title.
/// </summary>
public sealed record PageBareResponse(string? SeoTitle)
{
    public static PageBareResponse Project(Func<string, string?> valueAccessor) =>
        new(SeoTitle: valueAccessor("seoTitle"));
}
