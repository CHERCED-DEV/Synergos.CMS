using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Media composition — mirrors UI ContentMedia contract.
/// </summary>
public sealed record ContentMedia(
    Image? Media = null,
    string? AltText = null,
    string? MediaTitle = null,
    string? MediaDescription = null);
