namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-countdown-digital&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la fecha como <c>endDateTime</c> y el
/// elemento lee <c>targetDate</c> en el <c>config</c>: el reloj colocado decía «Fecha del evento no
/// disponible». Y el estilo que elegía el editor casi nunca llegaba: el DataType dice
/// <c>digits</c>/<c>flip</c>/<c>circular</c> y el elemento sabe <c>flip</c>/<c>plain</c>, así que
/// «digits» caía al default (<c>flip</c>, con animación).</para>
///
/// <para><b><c>targetDate</c></b>: la fecha ISO 8601 tal como la escribió el editor (ver
/// <see cref="CountdownClockProps"/>). <b><c>showLabels</c></b> se autora en NEGATIVO
/// (<c>hideLabels</c>, #192 caso 24), como <c>seat-map</c>: el interruptor de Umbraco vale
/// <c>false</c> sin tocar, y con «mostrar» un bloque nuevo ocultaba los rótulos que el elemento
/// pinta por defecto. Sólo viaja el apagado.
/// <b><c>style</c></b>: el nombre lo pone el elemento (ADR 0083) —<c>digits</c> es su
/// <c>plain</c>—. El DataType ya no ofrece <c>circular</c> (#192, caso 19): el elemento no lo
/// pinta y caía a su default.</para>
///
/// <para><c>startedLabel</c>, <c>invalidLabel</c> y <c>labels</c> los acepta el elemento y no los
/// autora el ElementType: quedan como atributos. Comparado con <c>countdown-clock</c>, ver
/// <see cref="CountdownClockProps"/>: comparten <c>targetDate</c> y nada más.</para>
///
/// <para><b>Sección <c>Countdown</c></b> (ADR 0136), la MISMA que declara
/// <c>countdown-clock</c>: los dos relojes son el mismo concepto (UI#86) y sus rótulos, sus hitos
/// en voz alta, «empezó» y «no disponible» son una clave cada uno.</para>
/// </remarks>
[ElementoSynHost("countdown-digital", TipoDeColocable.Pieza, Diccionario = ["Countdown"])]
public sealed record CountdownDigitalProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TargetDate,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? ShowLabels,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Style);
