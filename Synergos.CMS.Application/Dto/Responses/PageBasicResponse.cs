namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO for the <c>pageBasic</c> Document Type. Projected
/// from an Umbraco <c>IPublishedContent</c> by the Web layer and
/// consumed by <c>PageBasic.cshtml</c>.
/// </summary>
/// <param name="SeoTitle">Value of the <c>seoTitle</c> property
/// composed from <c>compSeo</c>. <c>null</c> when the editor has
/// not filled the field.</param>
/// <param name="BodyHtml">HTML content of the <c>body</c> richtext
/// property. <c>null</c> when empty. Already sanitised by Umbraco's
/// TinyMCE pipeline (appsettings enables <c>SanitizeTinyMce=true</c>).
/// Kept as <see cref="string"/> — not <c>IHtmlContent</c> — so that
/// this DTO stays in <c>Synergos.CMS.Application</c> without
/// dragging <c>Microsoft.AspNetCore.Html.*</c> into the Application
/// layer (ADR 0002).</param>
/// <remarks>
/// Scope per ADR 0014. The static <see cref="Project"/> helper
/// exists so the projection logic can be unit-tested without
/// constructing a fake <c>IPublishedContent</c>: callers pass a
/// <see cref="Func{T, TResult}"/> that looks up property values by
/// alias. The Web-layer controller wires this accessor to
/// <c>CurrentPage.Value&lt;string&gt;(alias)</c>.
/// </remarks>
public sealed record PageBasicResponse(
    string? SeoTitle,
    string? BodyHtml)
{
    /// <summary>
    /// Projects a <see cref="PageBasicResponse"/> by calling
    /// <paramref name="valueAccessor"/> with the expected aliases
    /// (<c>seoTitle</c>, <c>body</c>).
    /// </summary>
    public static PageBasicResponse Project(Func<string, string?> valueAccessor) =>
        new(
            SeoTitle: valueAccessor("seoTitle"),
            BodyHtml: valueAccessor("body"));
}
