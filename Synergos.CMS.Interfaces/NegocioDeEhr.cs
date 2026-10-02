namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de el portal clínico ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
public sealed record NegocioDeEhr(string ApiBase);
