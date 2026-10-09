namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio del alquiler de equipos ya fusionados para un sitio (ADR 0137, #147).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
public sealed record NegocioDeAlquiler(string ApiBase);
