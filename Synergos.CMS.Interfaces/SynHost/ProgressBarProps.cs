namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-progress-bar&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista emitía <c>valueNow</c>, <c>valueMax</c> y
/// <c>ariaLabel</c>, y el elemento lee <c>value</c>, <c>max</c> y <c>label</c>: el editor ponía 3
/// de 5 y la barra hidrataba vacía, «Progreso» al 0 %.</para>
///
/// <para><b><c>value</c> y <c>max</c> viajan como NÚMEROS.</b> El editor los escribe en un TextBox
/// («42,5» vale); lo que no es un número no viaja y se anota. El rango (de 0 a <c>max</c>, 100 por
/// defecto) lo aplica el elemento, que es quien pinta la barra.</para>
///
/// <para><c>label</c> sale de <c>compDomAttributes.ariaLabel</c>, que es lo que la vista ya
/// mandaba: el nombre accesible de la barra, que el elemento además rotula encima. Es texto que el
/// editor escribe por instancia, así que es contenido (como el <c>label</c> de
/// <c>rating-stars</c>). Indeterminado, mostrar el porcentaje, tamaño y tono los sabe pintar el
/// elemento pero el ElementType no los autora: quedan como atributo.</para>
///
/// <para><b>Sección <c>ProgressBar</c></b> (ADR 0136): el nombre accesible cuando el editor no
/// escribió rótulo.</para>
/// </remarks>
[ElementoSynHost("progress-bar", TipoDeColocable.Pieza, Diccionario = ["ProgressBar"])]
public sealed record ProgressBarProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] decimal? Value,
    [property: CampoSynHost(OrigenDelCampo.Decision)] decimal? Max,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);
