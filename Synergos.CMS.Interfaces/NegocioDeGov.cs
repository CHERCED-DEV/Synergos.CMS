namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de el portal de trámites ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="FilingPath">Por dónde radica el sitio, si lo declara (009-gobierno-tramite T033): <see cref="CaminoDelFlujo.Puerta"/>
/// o <see cref="CaminoDelFlujo.Directa"/>, ya normalizado. Nulo es que no lo declara, y decide la entrada de la puerta.</param>
public sealed record NegocioDeGov(string ApiBase, string? FilingPath = null);
