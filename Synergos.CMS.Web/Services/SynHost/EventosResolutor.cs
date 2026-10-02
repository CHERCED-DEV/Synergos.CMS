using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynEventos</c> → <see cref="EventosProps"/>: lo que escribe el editor, más la
/// configuración de negocio del sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// La comisión sale de <see cref="INegocioDelSitio{TNegocio}"/>, la MISMA fuente que leen los motores de
/// compra para cobrarla: lo que el carrito muestra es lo que se cobra. Del sitio se copian sólo las
/// claves que el elemento declara leer; la sección entera no llega al navegador.
/// </remarks>
public sealed class EventosResolutor : IResolutorSynHost<EventosProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeEventos> _negocio;
    private readonly ILogger<EventosResolutor> _log;

    public EventosResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeEventos> negocio, ILogger<EventosResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<EventosProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<EventosProps>(new EventosProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            Role: editor.Texto("role"),
            ApiBase: negocio.ApiBase,
            FeePercent: negocio.FeePercent,
            PlatformFeePercent: negocio.PlatformFeePercent));
    }
}
