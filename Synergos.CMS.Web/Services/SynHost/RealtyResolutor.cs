using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynRealty</c> → <see cref="RealtyProps"/>: lo que escribe el editor, más la
/// configuración de negocio del sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>El camino de la visita lo decide acá el CMS</b> (008-P1, núcleo N3): lo que declara el sitio en
/// <c>Synergos:Features:Realty:VisitPath</c>, que sin valor es la directa, pasado por la regla común de los flujos
/// (<see cref="ICaminoDeLosFlujos"/>) con la clave que coloca la vista del bloque. El elemento lo sigue y no cae
/// nunca de un camino al otro.</para>
/// </remarks>
public sealed class RealtyResolutor : IResolutorSynHost<RealtyProps>
{
    /// <summary>La clave del flujo de la visita: la que coloca <c>elementSynRealty.cshtml</c>.</summary>
    public const string FlujoDeLaVisita = "propiedades.visita";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeRealty> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly string _zona;
    private readonly ILogger<RealtyResolutor> _log;

    public RealtyResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeRealty> negocio,
        ICaminoDeLosFlujos caminos,
        IOptions<ListadosSettings> listados,
        ILogger<RealtyResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _zona = listados.Value.ZonaHoraria;
        _log = log;
    }

    public ElementoResuelto<RealtyProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<RealtyProps>(new RealtyProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            DefaultRatePercent: negocio.DefaultRatePercent,
            VisitPath: _caminos.Decidir(FlujoDeLaVisita, negocio.VisitPath),
            ZonaHoraria: _zona));
    }
}
