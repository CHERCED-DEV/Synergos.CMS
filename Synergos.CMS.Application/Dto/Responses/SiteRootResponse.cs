namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Response DTO for the <c>siteRoot</c> Document Type (Ola 8).
/// Projected by the Web layer and consumed by <c>SiteRoot.cshtml</c>.
/// </summary>
/// <param name="SeoTitle">Value of composed <c>seoTitle</c> (from
/// <c>compSeo</c>). Culture-variant. <c>null</c> when empty.</param>
/// <param name="SiteDisplayName">Human name of the site for the
/// current culture (e.g. "Acme Colombia"). Required in the schema but
/// may be <c>null</c> here for defensive reading.</param>
/// <param name="CanonicalHostname">Invariant canonical hostname
/// (e.g. "www.acme.co") used to build canonical URLs. <c>null</c>
/// when the editor has not filled it.</param>
/// <remarks>
/// Scope per ADR 0014. Static <see cref="Project"/> enables unit
/// testing without constructing a fake <c>IPublishedContent</c>.
/// Internal/lifecycle notes (from compCoreBase) intentionally omitted
/// — those are admin-only and never render publicly.
/// </remarks>
public sealed record SiteRootResponse(
    string? SeoTitle,
    string? SiteDisplayName,
    string? CanonicalHostname)
{
    public static SiteRootResponse Project(Func<string, string?> valueAccessor) =>
        new(
            SeoTitle: valueAccessor("seoTitle"),
            SiteDisplayName: valueAccessor("siteDisplayName"),
            CanonicalHostname: valueAccessor("canonicalHostname"));
}
