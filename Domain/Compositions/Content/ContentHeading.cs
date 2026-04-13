using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Heading composition — mirrors UI ContentHeading contract.
/// </summary>
public sealed record ContentHeading(
    string? HeadingText = null,
    string? HeadingLevel = null,
    string? HeadingTagOverride = null);
