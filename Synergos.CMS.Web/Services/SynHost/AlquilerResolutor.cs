using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAlquiler</c> → <see cref="AlquilerProps"/>: lo que escribe el editor, más dónde
/// vive la API para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <b>El camino del alquiler lo decide acá el CMS</b> (ADR 0140, <see cref="CaminoDeCompra.Decidir"/>; línea 005,
/// research R9): el override del sitio si lo hay; si no, la puerta cuando abre <see cref="FlujoDeLaRenta"/>; si no,
/// la ruta directa. La pregunta a la puerta es la misma condición con que contesta 503, así que el elemento nunca va
/// a una puerta cerrada ni deja de ir a una abierta. No se decide con <c>Synergos:Alquiler:Mode</c>: ese interruptor
/// dice qué motor atiende la ruta directa, no si la puerta abre.
/// </remarks>
public sealed class AlquilerResolutor : IResolutorSynHost<AlquilerProps>
{
    /// <summary>El flujo de la renta: el que coloca <c>elementSynAlquiler.cshtml</c> alrededor del elemento.</summary>
    public const string FlujoDeLaRenta = "alquiler.renta";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeAlquiler> _negocio;
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly ILogger<AlquilerResolutor> _log;

    /// <summary>Construye el resolutor.</summary>
    /// <param name="fallback">El fallback de cultura de Umbraco para leer las propiedades.</param>
    /// <param name="negocio">La configuración de negocio del sitio de la petición.</param>
    /// <param name="puerta">Lo que la puerta abre en este despliegue.</param>
    /// <param name="log">Para decir qué campo del editor no se pudo leer.</param>
    public AlquilerResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeAlquiler> negocio, IPuertaDeLosFlujos puerta,
        ILogger<AlquilerResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _puerta = puerta;
        _log = log;
    }

    /// <inheritdoc />
    public ElementoResuelto<AlquilerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<AlquilerProps>(new AlquilerProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            Category: editor.Texto("category"),
            ApiBase: negocio.ApiBase,
            RentalPath: CaminoDeCompra.Decidir(negocio.RentalPath, _puerta.Abre(FlujoDeLaRenta))));
    }
}
