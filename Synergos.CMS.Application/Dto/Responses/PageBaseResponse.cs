namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO for the <c>pageBase</c> Document Type (Ola 8).
/// Standard editorial page under siteRoot. Projected by the Web
/// layer and consumed by <c>PageBase.cshtml</c>.
/// </summary>
/// <param name="SeoTitle">Value of composed <c>seoTitle</c> (from
/// <c>compSeo</c>). Culture-variant. <c>null</c> when empty.</param>
/// <param name="BodyHtml">HTML content of the <c>body</c> richtext
/// property. Culture-variant. Already sanitised by Umbraco's TinyMCE
/// pipeline. Kept as <see cref="string"/> to keep the Application
/// layer free of <c>Microsoft.AspNetCore.Html.*</c> (ADR 0002).</param>
/// <remarks>
/// Admin-only fields from compCoreBase/compCoreLifecycle (internal
/// notes, publishing notes) intentionally omitted.
/// </remarks>
public sealed record PageBaseResponse(
    string? SeoTitle,
    string? BodyHtml)
{
    public static PageBaseResponse Project(Func<string, string?> valueAccessor) =>
        new(
            SeoTitle: valueAccessor("seoTitle"),
            BodyHtml: valueAccessor("body"));
}
