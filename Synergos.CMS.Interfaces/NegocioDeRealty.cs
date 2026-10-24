namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de Propiedades ya fusionados para un sitio (ADR 0137).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="DefaultRatePercent">La tasa efectiva anual, en porcentaje, con la que el simulador de
/// hipoteca arranca. El servidor no la usa: calcula con la que le mande quien simula.</param>
/// <param name="VisitPath">Por dónde agenda la visita el sitio (008-P1): <see cref="CaminoDelFlujo.Puerta"/> o
/// <see cref="CaminoDelFlujo.Directa"/>, ya normalizado. Nunca nulo: sin declaración, la directa.</param>
/// <remarks>
/// <b>La moneda no está acá</b>: viaja con el precio de cada inmueble, que la decide el catálogo.
/// </remarks>
public sealed record NegocioDeRealty(string ApiBase, decimal DefaultRatePercent, string VisitPath = CaminoDelFlujo.Directa);
