namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO for the <c>pageBare</c> Document Type (Ola 8).
/// Chrome-less page variant. Projected by the Web layer and consumed
/// by <c>PageBare.cshtml</c>.
/// </summary>
/// <param name="SeoTitle">Value of composed <c>seoTitle</c> (from
/// <c>compSeo</c>). Culture-variant. <c>null</c> when empty.</param>
/// <param name="BodyHtml">HTML content of the <c>body</c> richtext
/// property. Culture-variant. Sanitised by Umbraco TinyMCE pipeline.
/// </param>
public sealed record PageBareResponse(
    string? SeoTitle,
    string? BodyHtml)
{
    public static PageBareResponse Project(Func<string, string?> valueAccessor) =>
        new(
            SeoTitle: valueAccessor("seoTitle"),
            BodyHtml: valueAccessor("body"));
}
