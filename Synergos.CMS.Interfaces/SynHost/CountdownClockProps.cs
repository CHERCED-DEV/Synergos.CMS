namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-countdown-clock&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la fecha como <c>endDateTime</c> y el
/// elemento lee <c>targetDate</c> en el <c>config</c> (<c>endDateTime</c> sólo lo acepta como
/// atributo): el reloj colocado decía «Fecha del evento no disponible» con la fecha del editor
/// escrita.</para>
///
/// <para><b><c>targetDate</c> es la fecha ISO 8601 tal como la escribió el editor</b> (el
/// ElementType la guarda en un TextBox): se valida y no se reescribe —una fecha sin zona la lee el
/// navegador en la hora local del visitante—. Una que no es ISO no viaja y se anota.</para>
///
/// <para><b><c>labelFormat</c> no viaja.</b> El ElementType lo describe como una plantilla con
/// <c>{days}</c>, <c>{hours}</c>… que el elemento no implementa: el atributo homónimo lo usa como
/// rótulo de «empezó», así que mandarlo pintaría «Quedan {days} días» literal al llegar a cero.
/// <c>startedLabel</c>, <c>invalidLabel</c> y <c>labels</c> los acepta el elemento y no los autora
/// el ElementType: quedan como atributos.</para>
///
/// <para>Comparado con <c>countdown-digital</c> (el mismo concepto al 83 %, UI#86): los dos viajan
/// <c>targetDate</c> con la misma lectura; éste no tiene decisiones del editor y aquél sí
/// (<c>showLabels</c>, <c>style</c>). Los records NO salen idénticos.</para>
/// </remarks>
[ElementoSynHost("countdown-clock", TipoDeColocable.Pieza)]
public sealed record CountdownClockProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? TargetDate);
