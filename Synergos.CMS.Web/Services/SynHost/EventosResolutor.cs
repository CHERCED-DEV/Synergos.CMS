using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynEventos</c> → <see cref="EventosProps"/>: lo que escribe el editor, más la
/// configuración de negocio del sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <para>La comisión sale de <see cref="INegocioDelSitio{TNegocio}"/>, la MISMA fuente que leen los motores de
/// compra para cobrarla: lo que el carrito muestra es lo que se cobra. Del sitio se copian sólo las
/// claves que el elemento declara leer; la sección entera no llega al navegador.</para>
///
/// <para><b>El camino de compra lo decide acá el CMS</b> (ADR 0140, <see cref="CaminoDeCompra.Decidir"/>): el
/// override del sitio si lo hay; si no, la puerta cuando abre <see cref="FlujoDeLaCompra"/>; si no, la ruta
/// directa. La pregunta a la puerta es la misma condición con que contesta 503, así que el elemento nunca
/// va a una puerta cerrada ni deja de ir a una abierta.</para>
/// </remarks>
public sealed class EventosResolutor : IResolutorSynHost<EventosProps>
{
    /// <summary>El flujo de la compra: el que coloca <c>elementSynEventos.cshtml</c> alrededor del elemento.</summary>
    public const string FlujoDeLaCompra = "eventos.compra";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeEventos> _negocio;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ILogger<EventosResolutor> _log;

    public EventosResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeEventos> negocio,
        IPuertaDeLosFlujos puerta,
        ILogger<EventosResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _puerta = puerta;
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
            PlatformFeePercent: negocio.PlatformFeePercent,
            PurchasePath: CaminoDeCompra.Decidir(negocio.PurchasePath, _puerta.Abre(FlujoDeLaCompra))));
    }
}
