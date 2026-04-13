using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// CTA composition — mirrors UI ContentCta contract.
/// </summary>
public sealed record ContentCta(
    string? CtaLabel = null,
    Link? CtaLink = null,
    string? CtaTarget = null,
    string? CtaAriaLabel = null);
