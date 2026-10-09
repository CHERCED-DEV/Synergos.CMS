using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAlquiler</c> → <see cref="AlquilerProps"/>: lo que escribe el editor, más dónde
/// vive la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class AlquilerResolutor : IResolutorSynHost<AlquilerProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeAlquiler> _negocio;
    private readonly ILogger<AlquilerResolutor> _log;

    /// <summary>Construye el resolutor.</summary>
    /// <param name="fallback">El fallback de cultura de Umbraco para leer las propiedades.</param>
    /// <param name="negocio">La configuración de negocio del sitio de la petición.</param>
    /// <param name="log">Para decir qué campo del editor no se pudo leer.</param>
    public AlquilerResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeAlquiler> negocio, ILogger<AlquilerResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    /// <inheritdoc />
    public ElementoResuelto<AlquilerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<AlquilerProps>(new AlquilerProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            Category: editor.Texto("category"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
