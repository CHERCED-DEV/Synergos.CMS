using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynStorefront</c> → <see cref="StorefrontProps"/>: lo que escribe el editor, más dónde vive
/// la API, por dónde compra, a qué país despacha el sitio de la petición (ADR 0137) y en qué zona dice sus fechas (la de
/// los listados, como Realty).
/// </summary>
/// <remarks>
/// <b>El camino de compra lo decide acá el CMS</b> (ADR 0140) con la regla de cualquier flujo
/// (<see cref="ICaminoDeLosFlujos"/>, núcleo N3), como el de Eventos: el override del sitio si lo hay; si no,
/// lo que declare <c>Synergos:Puerta:Flujos:tienda.compra:Camino</c>; si no, la puerta cuando abre
/// <see cref="FlujoDeLaCompra"/>; si no, la ruta directa. La pregunta a la puerta es la misma condición con que
/// contesta 503.
/// </remarks>
public sealed class StorefrontResolutor : IResolutorSynHost<StorefrontProps>
{
    /// <summary>El flujo de la compra: el que coloca <c>elementSynStorefront.cshtml</c> alrededor del elemento.</summary>
    public const string FlujoDeLaCompra = "tienda.compra";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeStorefront> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly string _zona;
    private readonly ILogger<StorefrontResolutor> _log;

    public StorefrontResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeStorefront> negocio,
        ICaminoDeLosFlujos caminos,
        IOptions<ListadosSettings> listados,
        ILogger<StorefrontResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _zona = listados.Value.ZonaHoraria;
        _log = log;
    }

    public ElementoResuelto<StorefrontProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<StorefrontProps>(new StorefrontProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            PurchasePath: _caminos.Decidir(FlujoDeLaCompra, negocio.PurchasePath),
            ShippingCountry: negocio.ShippingCountry,
            ZonaHoraria: _zona));
    }
}
