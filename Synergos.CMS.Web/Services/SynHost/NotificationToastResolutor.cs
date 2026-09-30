using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynNotificationToast</c> → <see cref="NotificationToastProps"/>: el aviso que autora el
/// editor como la lista de un aviso que lee el elemento, y la duración como número.
/// </summary>
/// <remarks>
/// Sin mensaje no hay aviso: el tipo solo no viaja (el elemento descartaría un aviso sin texto).
/// </remarks>
public sealed class NotificationToastResolutor : IResolutorSynHost<NotificationToastProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<NotificationToastResolutor> _log;

    public NotificationToastResolutor(IPublishedValueFallback fallback, ILogger<NotificationToastResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<NotificationToastProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var mensaje = editor.Texto("message");

        return new ElementoResuelto<NotificationToastProps>(new NotificationToastProps(
            Toasts: mensaje is null
                ? null
                : new[] { new NotificationToastSeed(mensaje, editor.Texto("type")?.ToLowerInvariant()) },
            DurationMs: editor.Entero("durationMs")));
    }
}
