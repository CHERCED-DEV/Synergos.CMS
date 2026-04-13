namespace Synergos.CMS.Configuration;

/// <summary>
/// Runtime host and CORS settings for the Synergos CMS.
/// Bound from appsettings.json section "Synergos:Runtime".
///
/// IMPORTANT: <c>WebHost</c>, <c>HttpPort</c>, and <c>HttpsPort</c> are
/// documentation-only — Kestrel reads its binding URL from launchSettings.json
/// (dev) or the ASPNETCORE_URLS environment variable (CI/production).
/// These fields exist to express the intended topology in a single versioned
/// place and to serve as the source for generating hosts-file / cert scripts.
///
/// <c>AllowedOrigins</c> drives the Synergos CORS policy registered in
/// Program.cs. It must include the static-asset server origin so that
/// browser fetch() calls from Angular Custom Elements are accepted.
/// Umbraco backoffice CORS is managed separately by Umbraco internals.
/// </summary>
public sealed class RuntimeSettings
{
    public const string SectionPath = "Synergos:Runtime";

    /// <summary>Primary hostname the CMS is expected to answer on.</summary>
    public string WebHost { get; init; } = "localhost";

    /// <summary>HTTP port (informational — mirrors launchSettings/ASPNETCORE_URLS).</summary>
    public int HttpPort { get; init; } = 5000;

    /// <summary>HTTPS port (informational — mirrors launchSettings/ASPNETCORE_URLS).</summary>
    public int HttpsPort { get; init; } = 5001;

    /// <summary>
    /// Origins allowed by the Synergos CORS policy.
    /// Must include the static-asset origin and any Angular dev-server origins
    /// in development. Empty array = CORS disabled (requests blocked by browser).
    /// </summary>
    public string[] AllowedOrigins { get; init; } = [];
}
