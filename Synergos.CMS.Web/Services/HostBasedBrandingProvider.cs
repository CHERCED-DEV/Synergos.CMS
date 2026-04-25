using Microsoft.AspNetCore.Http;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="IBrandingProvider"/> que resuelve el brand activo según el
/// hostname del request actual, matchéandolo contra los
/// <c>siteConfigSettings</c> publicados en el Settings tree (cada uno
/// con su <c>compBranding.brandKey</c> + <c>brandDisplayName</c>).
/// </summary>
/// <remarks>
/// Pipeline de resolución (primer match gana):
/// <list type="number">
///   <item>Si no hay request HTTP → fallback al brand de
///     <see cref="BrandingSettings"/>.</item>
///   <item>Para cada <c>siteConfigSettings</c> publicado: si su
///     <c>siteRoot</c> ancestro tiene <c>canonicalHostname</c> que
///     matchea (case-insensitive) el host del request, devuelve su
///     <c>brandKey</c> + <c>brandDisplayName</c>.</item>
///   <item>Si no hay match → fallback al brand de
///     <see cref="BrandingSettings"/>.</item>
/// </list>
///
/// ADR 0010 invariante: este provider no expone, ni los consumidores
/// pueden, branchar en <c>brandKey</c>. El brand es identidad
/// transportada — comportamientos brand-specific viven en futuras
/// custom layers que registran como alternative <see cref="IBrandingProvider"/>.
///
/// Singleton porque solo lee per-request via accessors. Sin caché:
/// el lookup contra <see cref="IUmbracoContextAccessor"/> ya está
/// optimizado por Umbraco para el published cache.
/// </remarks>
public sealed class HostBasedBrandingProvider : IBrandingProvider
{
    private const string SiteConfigSettingsAlias = "siteConfigSettings";
    private const string SiteRootAlias = "siteRoot";
    private const string BrandKeyAlias = "brandKey";
    private const string BrandDisplayNameAlias = "brandDisplayName";
    private const string CanonicalHostnameAlias = "canonicalHostname";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly DefaultBrandingProvider _fallback;

    public HostBasedBrandingProvider(
        IHttpContextAccessor httpContextAccessor,
        IUmbracoContextAccessor umbracoContextAccessor,
        BrandingSettings settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _umbracoContextAccessor = umbracoContextAccessor;
        _fallback = new DefaultBrandingProvider(settings);
    }

    public BrandIdentity GetCurrent()
    {
        var hostHeader = _httpContextAccessor.HttpContext?.Request?.Host.Host;
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return _fallback.GetCurrent();
        }

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return _fallback.GetCurrent();
        }

        var siteConfigs = umbracoContext.Content?
            .GetAtRoot()
            .SelectMany(root => root.DescendantsOrSelfOfType(SiteConfigSettingsAlias))
            .ToArray() ?? Array.Empty<IPublishedContent>();

        foreach (var siteConfig in siteConfigs)
        {
            var siteRoot = siteConfig.AncestorOrSelf(SiteRootAlias);
            if (siteRoot is null)
            {
                continue;
            }

            var canonicalHost = siteRoot.Value<string>(CanonicalHostnameAlias);
            if (string.IsNullOrWhiteSpace(canonicalHost))
            {
                continue;
            }

            // Strip protocol/port from canonical for comparison
            canonicalHost = canonicalHost
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Split('/')[0]
                .Split(':')[0];

            if (!string.Equals(canonicalHost, hostHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var brandKey = siteConfig.Value<string>(BrandKeyAlias);
            if (string.IsNullOrWhiteSpace(brandKey))
            {
                continue;
            }

            var displayName = siteConfig.Value<string>(BrandDisplayNameAlias);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = brandKey;
            }

            return new BrandIdentity(brandKey, displayName);
        }

        return _fallback.GetCurrent();
    }
}
