namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Produces the analytics / tracking script HTML blocks for a site root.
/// Resolves GTM, head scripts, and body-end scripts with a GlobalSettings fallback.
/// Focused interface — satisfies ISP.
/// </summary>
public interface ITrackingService
{
    TrackingScriptsConfig GetTrackingScripts(int rootNodeId);
}
