using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynAudioPlayer</c> → <see cref="AudioPlayerProps"/>.</summary>
/// <remarks>El medio <c>audioFile</c> viaja como su URL absoluta, con el nombre que el elemento lee.</remarks>
public sealed class AudioPlayerResolutor : IResolutorSynHost<AudioPlayerProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly IPublishedUrlProvider _urls;
    private readonly ILogger<AudioPlayerResolutor> _log;

    public AudioPlayerResolutor(IPublishedValueFallback fallback, IPublishedUrlProvider urls, ILogger<AudioPlayerResolutor> log)
    {
        _fallback = fallback;
        _urls = urls;
        _log = log;
    }

    public ElementoResuelto<AudioPlayerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log, _urls);
        return new ElementoResuelto<AudioPlayerProps>(new AudioPlayerProps(
            AudioFile: editor.Medio("audioFile")?.Url,
            TrackTitle: editor.Texto("trackTitle"),
            ArtistName: editor.Texto("artistName")));
    }
}
