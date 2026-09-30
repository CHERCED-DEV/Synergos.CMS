using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynCookieConsent</c> → <see cref="CookieConsentProps"/>.</summary>
/// <remarks>El enlace a la política viaja como su destino y su texto.</remarks>
public sealed class CookieConsentResolutor : IResolutorSynHost<CookieConsentProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<CookieConsentResolutor> _log;

    public CookieConsentResolutor(IPublishedValueFallback fallback, ILogger<CookieConsentResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<CookieConsentProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var politica = editor.Enlace("policyLink");

        return new ElementoResuelto<CookieConsentProps>(new CookieConsentProps(
            BannerText: editor.Texto("bannerText"),
            AcceptLabel: editor.Texto("acceptLabel"),
            RejectLabel: editor.Texto("rejectLabel"),
            SettingsLabel: editor.Texto("settingsLabel"),
            PolicyLink: politica?.Url,
            PolicyLabel: politica?.Nombre));
    }
}
