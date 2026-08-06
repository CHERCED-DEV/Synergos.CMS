using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IBrandingProvider"/> that projects the active
/// <see cref="BrandingSettings"/> as a <see cref="BrandIdentity"/>.
/// </summary>
/// <remarks>
/// Per ADR 0010, consumers MUST NOT branch on <c>BrandIdentity.Key</c>.
/// This implementation is the stable baseline; variants per host,
/// tenant, or cookie live in the future custom layer and register as
/// alternate implementations of <see cref="IBrandingProvider"/>.
/// </remarks>
public sealed class DefaultBrandingProvider(BrandingSettings settings) : IBrandingProvider
{
    public BrandIdentity GetCurrent() =>
        new(settings.Key, settings.DisplayName);
}
