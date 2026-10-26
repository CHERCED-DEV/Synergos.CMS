using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynGov</c> → <see cref="GovProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>El camino de la radicación lo decide acá el CMS</b> (009-gobierno-tramite T030 y T033): lo que declara el
/// sitio en <c>Synergos:Features:Gov:FilingPath</c> gana; si no declara, la entrada de la puerta
/// (<c>Camino: directa</c> en el <c>appsettings.json</c> base); todo por la regla común de los flujos
/// (<see cref="ICaminoDeLosFlujos"/>) con la clave que coloca la vista del bloque.</para>
///
/// <para><b>La puerta declarada y cerrada es <see cref="NoDisponible"/></b>, como la reserva de servicio: el sitio
/// pidió radicar por la puerta y el despliegue no tiene el orquestador. El elemento lo dice y no cae a la directa a
/// escondidas —el sitio decidió que la tasa se cobra por la puerta—.</para>
/// </remarks>
public sealed class GovResolutor : IResolutorSynHost<GovProps>
{
    /// <summary>La clave del flujo de la radicación: la que coloca <c>elementSynGov.cshtml</c>.</summary>
    public const string FlujoDeLaRadicacion = "gobierno.radicacion";

    /// <summary>Lo que resuelve el CMS cuando el camino es la puerta y la puerta no abre el flujo.</summary>
    public const string NoDisponible = "no-disponible";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeGov> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ILogger<GovResolutor> _log;

    public GovResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeGov> negocio, ICaminoDeLosFlujos caminos,
        IPuertaDeLosFlujos puerta, ILogger<GovResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _puerta = puerta;
        _log = log;
    }

    public ElementoResuelto<GovProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();
        var camino = _caminos.Decidir(FlujoDeLaRadicacion, negocio.FilingPath);

        return new ElementoResuelto<GovProps>(new GovProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            Camino: camino == CaminoDelFlujo.Puerta && !_puerta.Abre(FlujoDeLaRadicacion) ? NoDisponible : camino));
    }
}
