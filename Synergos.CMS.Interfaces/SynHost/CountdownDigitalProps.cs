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
/// <see cref="CountdownClockProps"/>). <b><c>showLabels</c></b> viaja SIEMPRE: el interruptor de
/// Umbraco vale <c>false</c> sin tocar y el elemento, sin la clave, pinta los rótulos; el
/// ElementType dice «si activo, muestra», así que <c>false</c> es una decisión y tiene que llegar.
/// <b><c>style</c></b>: el nombre lo pone el elemento (ADR 0083) —<c>digits</c> es su
/// <c>plain</c>—; <c>circular</c> no existe en este elemento (es el aspecto de
/// <c>countdown-clock</c>) y viaja como lo eligió el editor: el elemento cae a su default.</para>
///
/// <para><c>startedLabel</c>, <c>invalidLabel</c> y <c>labels</c> los acepta el elemento y no los
/// autora el ElementType: quedan como atributos. Comparado con <c>countdown-clock</c>, ver
/// <see cref="CountdownClockProps"/>: comparten <c>targetDate</c> y nada más.</para>
/// </remarks>
[ElementoSynHost("countdown-digital", TipoDeColocable.Pieza)]
public sealed record CountdownDigitalProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TargetDate,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? ShowLabels,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Style);
