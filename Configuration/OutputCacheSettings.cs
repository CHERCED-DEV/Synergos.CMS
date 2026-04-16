namespace Synergos.CMS.Configuration;

/// <summary>
/// Output-cache profiles bound from <c>Synergos:OutputCache</c>. Each profile
/// becomes a named ASP.NET Core output cache policy registered at startup,
/// reusable from controllers via <c>[OutputCache(PolicyName = "name")]</c>.
///
/// Inspired by NS.Booking.CMS's centralised <c>OutputCacheProfiles</c> config —
/// keeps TTLs, vary-by, and tag rules in appsettings instead of scattered
/// across attributes, so ops can tune cache behaviour without recompiling.
/// </summary>
public sealed class OutputCacheSettings
{
    public const string SectionPath = "Synergos:OutputCache";

    /// <summary>Default profiles seeded when none are configured.</summary>
    public OutputCacheProfile[] Profiles { get; init; } =
    [
        new() { Name = "PageContent",  DurationSeconds = 30,  VaryByQueryKeys = [ "path", "culture" ] },
        new() { Name = "BlogListing",  DurationSeconds = 60,  VaryByQueryKeys = [ "page", "culture" ] },
        new() { Name = "BlogPost",     DurationSeconds = 300, VaryByQueryKeys = [ "culture" ] },
        new() { Name = "SiteSettings", DurationSeconds = 600, VaryByQueryKeys = [ "culture" ] }
    ];
}

public sealed class OutputCacheProfile
{
    /// <summary>Policy name — referenced in <c>[OutputCache(PolicyName = "...")]</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Cache TTL in seconds. 0 = effectively no caching.</summary>
    public int DurationSeconds { get; init; } = 30;

    /// <summary>Query-string keys that produce distinct cache entries.</summary>
    public string[] VaryByQueryKeys { get; init; } = [];

    /// <summary>HTTP header names that produce distinct cache entries.</summary>
    public string[] VaryByHeaderNames { get; init; } = [];

    /// <summary>Tags applied to cached entries — usable for bulk eviction via <c>IOutputCacheStore.EvictByTagAsync</c>.</summary>
    public string[] Tags { get; init; } = [];
}
