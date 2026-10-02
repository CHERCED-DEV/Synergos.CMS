using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynFormStepper</c> → <see cref="FormStepperProps"/>: los pasos y los campos del
/// formulario, su clave, y dónde y cómo se envía para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// Los campos son <c>elementFormField</c>, los mismos de <c>elementFormContainer</c>, y se leen con
/// sus mismas reglas que <see cref="UmbracoFormDefinitionReader"/>: un campo sin nombre o sin
/// etiqueta no se pinta, y el servidor tampoco lo exige. Lo que se muestra y lo que se comprueba
/// salen de la misma definición.
/// </remarks>
public sealed class FormStepperResolutor : IResolutorSynHost<FormStepperProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeFormStepper> _negocio;
    private readonly IOptions<FormsSettings> _formularios;
    private readonly ILogger<FormStepperResolutor> _log;

    public FormStepperResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeFormStepper> negocio,
        IOptions<FormsSettings> formularios,
        ILogger<FormStepperResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _formularios = formularios;
        _log = log;
    }

    public ElementoResuelto<FormStepperProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        var pasos = editor.Bloques("steps")
            .Select(Paso)
            .OfType<PasoDelFormulario>()
            .ToList();

        return new ElementoResuelto<FormStepperProps>(new FormStepperProps(
            FormKey: editor.Texto("formInternalKey"),
            Steps: pasos.Count > 0 ? pasos : null,
            AllowSkip: editor.Interruptor("allowSkip") ? true : null,
            ApiBase: _negocio.Actual().ApiBase,
            HoneypotField: _formularios.Value.HoneypotFieldName));
    }

    /// <summary>Un paso con título y al menos un campo; si no, no se pinta.</summary>
    private static PasoDelFormulario? Paso(LectorDelEditor paso)
    {
        var titulo = paso.Texto("stepTitle");
        var campos = paso.Bloques("fields").Select(Campo).OfType<CampoDelFormulario>().ToList();
        return titulo is null || campos.Count == 0
            ? null
            : new PasoDelFormulario(titulo, campos, paso.TextoPlano("stepDescription"));
    }

    private static CampoDelFormulario? Campo(LectorDelEditor campo)
    {
        var nombre = campo.Texto("fieldName");
        var etiqueta = campo.Texto("fieldLabel");
        if (nombre is null || etiqueta is null)
        {
            return null;
        }

        return new CampoDelFormulario(
            Name: nombre,
            Label: etiqueta,
            Type: campo.Texto("fieldType")?.ToLowerInvariant() ?? "text",
            Required: campo.Interruptor("fieldRequired"),
            Placeholder: campo.Texto("fieldPlaceholder"),
            HelpText: campo.Texto("fieldHelpText"),
            Options: campo.Opciones("fieldOptions"));
    }
}
