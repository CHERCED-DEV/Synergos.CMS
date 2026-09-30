namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-notification-toast&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>message</c> y <c>type</c> dentro del
/// <c>config</c>, y el elemento sólo los lee como ATRIBUTOS sueltos: del <c>config</c> lee la lista
/// <c>toasts</c> (<c>message</c>/<c>variant</c> por aviso). El aviso colocado hidrataba sin nada
/// que mostrar; sólo <c>durationMs</c> llegaba.</para>
///
/// <para><b>El editor autora UN aviso; el elemento lee una lista.</b> El resolver arma la lista de
/// uno con el mensaje y su tipo (<c>type</c> del ElementType → <c>variant</c> del elemento). El
/// DataType ofrece <c>neutral</c>, que el elemento no conoce y pinta como <c>info</c>.</para>
///
/// <para><c>durationMs</c> viaja como NÚMERO: el ElementType lo autora en un TextBox y dice que
/// <c>0</c> es persistente, así que el cero viaja (no es «vacío»). Lo que no es un entero no viaja
/// y se anota; el elemento aplica sus 5000 ms. <c>position</c> y el título de cada aviso los sabe
/// pintar el elemento pero el ElementType no los autora: quedan como atributo.</para>
/// </remarks>
[ElementoSynHost("notification-toast", TipoDeColocable.Pieza)]
public sealed record NotificationToastProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<NotificationToastSeed>? Toasts,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? DurationMs);

/// <summary>Un aviso: el texto y su tipo semántico (<c>info</c>, <c>success</c>, <c>warning</c>, <c>error</c>).</summary>
public sealed record NotificationToastSeed(string Message, string? Variant = null);
