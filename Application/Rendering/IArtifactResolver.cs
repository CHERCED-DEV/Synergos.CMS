namespace Synergos.CMS.Application.Rendering;

/// <summary>
/// Resolves element aliases to CDN artifact metadata.
/// Implementations may hot-reload from a filesystem registry, a remote HTTP manifest,
/// or anything else — callers depend only on this abstraction.
/// </summary>
public interface IArtifactResolver
{
    /// <summary>True when the given alias is configured as a client-hosted element.</summary>
    bool IsClientHosted(string alias);

    /// <summary>Metadata for an element, or null when unknown.</summary>
    ArtifactInfo? ResolveByAlias(string alias);

    /// <summary>Distinct stylesheet URLs required by the currently loaded client-hosted artifacts.</summary>
    IEnumerable<string> GetClientStyleUrls();
}

/// <summary>Resolved artifact metadata for a single element.</summary>
public sealed record ArtifactInfo(
    string  Name,
    string  Tag,
    string  ScriptUrl,
    string? StyleUrl,
    string? Integrity,
    string  Version,
    string? Framework);
