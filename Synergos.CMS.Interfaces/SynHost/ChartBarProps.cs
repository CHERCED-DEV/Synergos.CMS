namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-chart-bar&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>chartTitle</c> y el TEXTO
/// <c>dataJson</c>; el elemento lee <c>title</c> y la LISTA <c>data</c> en el <c>config</c>: el
/// gráfico colocado decía «No hay datos para graficar» y sin título.</para>
///
/// <para><b>Cada barra lleva <c>label</c> y <c>value</c>, y <c>value</c> es un NÚMERO</b>: un
/// número JSON se lee tal cual; una cadena, con la lectura es-CO del lector («1.234.567»,
/// «12,5»), y la que admite dos lecturas («500.000») no viaja y se anota — el elemento, con su
/// propio parser de cadenas, la habría leído como 500. Una barra sin etiqueta o sin valor no viaja.
/// <c>color</c> por barra lo acepta el elemento y el ElementType no lo documenta: no viaja.</para>
///
/// <para><c>orientation</c> viaja como la elige el editor (los valores del DataType son los del
/// elemento). Los ejes, prefijo/sufijo, <c>maxValue</c>, <c>locale</c>, <c>showValues</c> y
/// <c>emptyLabel</c> los acepta el elemento y no los autora el ElementType: quedan como
/// atributos.</para>
/// </remarks>
[ElementoSynHost("chart-bar", TipoDeColocable.Pieza)]
public sealed record ChartBarProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Title,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Orientation,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<ChartBarEntry>? Data);

/// <summary>Una barra: su etiqueta y su valor.</summary>
public sealed record ChartBarEntry(string Label, decimal Value);
