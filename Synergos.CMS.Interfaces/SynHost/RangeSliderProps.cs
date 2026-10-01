namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-range-slider&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>minValue</c>, <c>maxValue</c> e
/// <c>initialValue</c> como TEXTO, y el elemento lee <c>min</c>, <c>max</c> y <c>high</c> como
/// números: el editor definía un rango de precios y el deslizador salía de 0 a 100.</para>
///
/// <para><b>El valor inicial es <c>high</c>.</b> El ElementType pide UN valor inicial, y el
/// elemento, con un solo número, mueve el pulgar alto (el único cuando <c>range</c> está apagado);
/// el bajo queda en el mínimo. Es la misma lectura que el elemento hace de su atributo
/// <c>initialValue</c>, así que el editor ve lo mismo por los dos caminos.</para>
///
/// <para><b>Enteros, sólo dígitos.</b> Los cuatro números salen de TextBox y se leen como ENTEROS:
/// «500.000» (el separador de miles del editor es-CO) no viaja y se anota. Leído como decimal
/// valdría 500 —el deslizador acabaría en 500 sin que nadie se entere—, que es peor que el default
/// del elemento con el aviso en el log. Un paso decimal («0,5») tampoco viaja.</para>
///
/// <para><b>Qué NO decide este record</b>: si el deslizador sigue siendo colocable. Su evento
/// <c>rangechange</c> no lo escucha nadie en el CMS (ADR 0134 §3, pendiente de producto); mientras
/// lo sea, lo que el editor autora tiene que llegar.</para>
///
/// <para><b>Sección <c>RangeSlider</c></b> (ADR 0136): el nombre de cada pulgar, «{label} —
/// mínimo/máximo», con el rótulo del editor como marcador (o «Rango» si no escribió ninguno).</para>
/// </remarks>
[ElementoSynHost("range-slider", TipoDeColocable.Pieza, Diccionario = ["RangeSlider"])]
public sealed record RangeSliderProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Min,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Max,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Step,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? High);
