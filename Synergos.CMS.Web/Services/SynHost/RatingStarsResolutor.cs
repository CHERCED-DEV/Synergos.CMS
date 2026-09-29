using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynRatingStars</c> → <see cref="RatingStarsProps"/>.</summary>
public sealed class RatingStarsResolutor : IResolutorSynHost<RatingStarsProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<RatingStarsResolutor> _log;

    public RatingStarsResolutor(IPublishedValueFallback fallback, ILogger<RatingStarsResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<RatingStarsProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<RatingStarsProps>(new RatingStarsProps(
            Value: editor.Numero("valueNow"),
            Max: editor.Entero("maxStars"),
            Label: editor.Texto("ariaLabel")));
    }
}
