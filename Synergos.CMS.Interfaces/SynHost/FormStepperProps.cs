namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-form-stepper&gt;</c>: un formulario del sitio presentado por pasos.
/// </summary>
/// <remarks>
/// <para><b>Es un formulario del modelo de Forms</b> (ADR 0018/0030), no uno aparte. Se identifica con
/// su <see cref="FormKey"/>, envía a la API de formularios del sitio y el servidor exige sus
/// obligatorios con la MISMA definición que pinta: la lee de este bloque, como la de cualquier
/// <c>elementFormContainer</c>.</para>
///
/// <para><b>El defecto que cierra (#196, tanda D).</b> La vista mandaba los pasos dentro de
/// <c>config</c> y el elemento solo leía atributos sueltos: no pintaba nada en las cuatro páginas
/// que lo colocan. Y si hubiera pintado, al terminar decía «¡Gracias! Tu información fue enviada»
/// sin enviar nada: despachaba un evento que nadie escuchaba.</para>
///
/// <para><b>Lo del editor</b> son los pasos y sus campos, la clave del formulario y si se puede
/// avanzar sin completar un paso. <b>Lo del despliegue</b> (ADR 0137) es dónde vive la API, de
/// <c>Synergos:Features:FormStepper</c>, y el nombre del campo trampa, que el servidor ya fija en
/// <c>Synergos:Forms</c>: una sola fuente, el que envía y el que comprueba la leen igual.</para>
///
/// <para><b>Secciones del diccionario</b> (ADR 0136): los textos de la navegación y de los avisos
/// son los de los formularios del sitio (<c>Form.*</c>).</para>
/// </remarks>
[ElementoSynHost("form-stepper", TipoDeColocable.Funcionalidad,
    Diccionario = ["Form.Messages", "Form.Actions", "Form.Validation.Required", "Form.Placeholders.SelectOption", "Form.Submit"])]
public sealed record FormStepperProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? FormKey,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<PasoDelFormulario>? Steps,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? AllowSkip,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string HoneypotField);

/// <summary>Un paso: su título, una línea que lo explica y sus campos, en orden.</summary>
public sealed record PasoDelFormulario(string Title, IReadOnlyList<CampoDelFormulario> Fields, string? Description = null);

/// <summary>
/// Un campo del formulario, con el mismo vocabulario que <c>elementFormField</c>: el
/// <see cref="Name"/> es la clave con que se guarda el envío.
/// </summary>
public sealed record CampoDelFormulario(
    string Name,
    string Label,
    string Type,
    bool Required = false,
    string? Placeholder = null,
    string? HelpText = null,
    IReadOnlyList<string>? Options = null);
