using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynVideoPlayer</c> → <see cref="VideoPlayerProps"/>.</summary>
/// <remarks>
/// El video y su póster viajan como URLs de medios. <c>chaptersJson</c> y <c>enableAnalytics</c>
/// no se leen: el elemento no los implementa (ver el record).
/// </remarks>
public sealed class VideoPlayerResolutor : IResolutorSynHost<VideoPlayerProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly IPublishedUrlProvider _urls;
    private readonly ILogger<VideoPlayerResolutor> _log;

    public VideoPlayerResolutor(IPublishedValueFallback fallback, IPublishedUrlProvider urls, ILogger<VideoPlayerResolutor> log)
    {
        _fallback = fallback;
        _urls = urls;
        _log = log;
    }

    public ElementoResuelto<VideoPlayerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log, _urls);
        return new ElementoResuelto<VideoPlayerProps>(new VideoPlayerProps(
            VideoFile: editor.Medio("videoFile")?.Url,
            PosterImage: editor.Medio("posterImage")?.Url));
    }
}
