namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio del formulario por pasos ya fusionados para un sitio (ADR 0137).</summary>
/// <param name="ApiBase">Dónde vive la API de formularios del sitio (relativa al sitio o absoluta).</param>
public sealed record NegocioDeFormStepper(string ApiBase);
