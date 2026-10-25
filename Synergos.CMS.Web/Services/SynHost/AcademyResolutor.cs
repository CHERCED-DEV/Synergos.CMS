using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAcademy</c> → <see cref="AcademyProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>El camino de la matrícula lo decide acá el CMS</b> (010-educacion-matricula, núcleo N3): lo que declara el
/// sitio en <c>Synergos:Features:Academy:EnrollmentPath</c>, que sin valor es la directa, pasado por la regla común de
/// los flujos (<see cref="ICaminoDeLosFlujos"/>) con la clave que coloca la vista del bloque.</para>
/// </remarks>
public sealed class AcademyResolutor : IResolutorSynHost<AcademyProps>
{
    /// <summary>La clave del flujo de la matrícula: la que coloca <c>elementSynAcademy.cshtml</c>.</summary>
    public const string FlujoDeLaMatricula = "educacion.matricula";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeAcademy> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly ILogger<AcademyResolutor> _log;

    public AcademyResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeAcademy> negocio, ICaminoDeLosFlujos caminos,
        ILogger<AcademyResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _log = log;
    }

    public ElementoResuelto<AcademyProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<AcademyProps>(new AcademyProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            EnrollmentPath: _caminos.Decidir(FlujoDeLaMatricula, negocio.EnrollmentPath)));
    }
}
