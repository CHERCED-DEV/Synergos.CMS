using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynEhr</c> → <see cref="EhrProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class EhrResolutor : IResolutorSynHost<EhrProps>
{
    /// <summary>
    /// El paciente de demo que el portal muestra: el que la vista vieja ponía escrito. Es identidad,
    /// no contenido (<c>OrigenDelCampo.Sesion</c>); #197 la pasa al miembro de la sesión.
    /// </summary>
    internal const string PacienteDeDemo = "pat-jorge-medina";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeEhr> _negocio;
    private readonly ILogger<EhrResolutor> _log;

    public EhrResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeEhr> negocio, ILogger<EhrResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<EhrProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<EhrProps>(new EhrProps(
            Patient: PacienteDeDemo,
            ApiBase: _negocio.Actual().ApiBase));
    }
}
