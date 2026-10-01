namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-rating-stars&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista emitía <c>valueNow</c>, <c>maxStars</c> y
/// <c>ariaLabel</c>, y el elemento lee <c>value</c>, <c>max</c> y <c>label</c>: el editor ponía un 4
/// y la página decía «0 de 5 estrellas».</para>
///
/// <para><b><c>value</c> y <c>max</c> viajan como NÚMEROS.</b> El editor los escribe en un TextBox
/// («4,5»); el resolver los convierte y lo que no es un número no viaja y se anota. El rango (3-10,
/// 5 por defecto según el ElementType) lo aplica el elemento, que es quien pinta las estrellas.</para>
///
/// <para><c>label</c> sale de <c>compDomAttributes.ariaLabel</c>, que es lo que la vista ya mandaba:
/// el nombre accesible del indicador («Valoración: 4 de 5 estrellas»).</para>
/// </remarks>
[ElementoSynHost("rating-stars", TipoDeColocable.Pieza, Diccionario = ["Rating"])]
public sealed record RatingStarsProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] decimal? Value,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Max,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);
