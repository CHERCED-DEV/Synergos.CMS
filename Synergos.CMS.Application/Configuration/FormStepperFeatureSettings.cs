using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio del formulario por pasos (ADR 0137) — sección
/// <c>Synergos:Features:FormStepper</c>.
/// </summary>
/// <remarks>
/// Antes cada bloque escribía la URL completa de envío (<c>/api/forms/{clave}/submit</c>): la clave
/// es del editor y queda en el bloque; dónde vive la API es del despliegue, por sitio (#196).
/// </remarks>
public sealed class FormStepperFeatureSettings : SeccionDeNegocio<FormStepperFeatureSitio, NegocioDeFormStepper>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:FormStepper";

    /// <summary>Dónde vive la API de formularios: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/forms";

    /// <inheritdoc />
    protected override NegocioDeFormStepper Fusionar(FormStepperFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeFormStepper negocio)
        => new[] { ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase) }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="FormStepperFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class FormStepperFeatureSitio
{
    /// <inheritdoc cref="FormStepperFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }
}
