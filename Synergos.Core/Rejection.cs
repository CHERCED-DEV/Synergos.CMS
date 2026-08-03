namespace Synergos.Core;

/// <summary>
/// Por qué una capacidad dijo que no. Cerrado y corto <b>a propósito</b>.
/// </summary>
/// <remarks>
/// <para>Cerrado porque es lo que permite que el borde HTTP mapee a un código de estado sin que
/// cada API invente el suyo, y —más importante— que un orquestador sepa <b>si reintentar o
/// rendirse</b> sin leer el texto del mensaje. Un enum abierto o un <c>string</c> devolverían
/// esa decisión a la lectura de prosa, que es como se escriben los reintentos infinitos.</para>
///
/// <para>Corto porque cada valor que se agrega hay que mapearlo en dieciséis servicios y en cada
/// orquestador. Si algo no cabe en estos seis, casi siempre es un <see cref="Invalid"/> con
/// mejor <c>Code</c>.</para>
/// </remarks>
public enum RejectionKind
{
    /// <summary>La petición está mal formada o viola una regla de entrada. No reintentar igual.</summary>
    Invalid,

    /// <summary>Lo que se pidió no existe. No reintentar.</summary>
    NotFound,

    /// <summary>Choca con el estado actual — cupo lleno, ya capturado, doble confirmación. Reintentar NO ayuda solo.</summary>
    Conflict,

    /// <summary>Quien pide no tiene permiso. No reintentar.</summary>
    Forbidden,

    /// <summary>Existía y se venció — un hold, una promoción, un consentimiento. Rehacer desde el principio.</summary>
    Expired,

    /// <summary>Un dependiente no respondió. <b>El único donde reintentar tiene sentido.</b></summary>
    Unavailable,
}

/// <summary>
/// La forma única de decir NO.
/// </summary>
/// <param name="Kind">Qué clase de no. Lo que decide el código HTTP y si se reintenta.</param>
/// <param name="Code">
/// Identificador estable y legible por máquina: <c>booking.slot_taken</c>,
/// <c>payments.already_captured</c>. Es lo que un orquestador compara; <b>nunca</b> el mensaje.
/// </param>
/// <param name="Message">Para un humano leyendo un log. Puede cambiar sin romper a nadie.</param>
/// <remarks>
/// <para><b>Por qué dieciséis capacidades rechazando igual no es cosmético.</b> Es lo que permite
/// que un orquestador maneje fallos con una sola rama en vez de un <c>switch</c> por servicio.
/// Sin esto, cada capacidad devuelve su forma, la capa media traduce dieciséis formas a una, y
/// esa traducción a mano es exactamente donde viven los errores que ningún test de una capacidad
/// encuentra.</para>
///
/// <para><b>Sin datos del dominio.</b> Un <c>Rejection</c> no lleva el paciente ni el pedido: los
/// rechazos terminan en logs y en respuestas, y ahí es donde se filtra lo que no debía salir.</para>
/// </remarks>
public sealed record Rejection(RejectionKind Kind, string Code, string Message)
{
    public static Rejection Invalid(string code, string message) => new(RejectionKind.Invalid, code, message);
    public static Rejection NotFound(string code, string message) => new(RejectionKind.NotFound, code, message);
    public static Rejection Conflict(string code, string message) => new(RejectionKind.Conflict, code, message);
    public static Rejection Forbidden(string code, string message) => new(RejectionKind.Forbidden, code, message);
    public static Rejection Expired(string code, string message) => new(RejectionKind.Expired, code, message);
    public static Rejection Unavailable(string code, string message) => new(RejectionKind.Unavailable, code, message);

    /// <summary>
    /// Si reintentar <b>lo mismo</b> puede dar un resultado distinto sin que cambie nada más.
    /// </summary>
    /// <remarks>
    /// Solo <see cref="RejectionKind.Unavailable"/>. Un <c>Conflict</c> tienta —"el cupo se
    /// puede liberar"— pero reintentarlo en un lazo es una tormenta de peticiones contra un
    /// servicio que ya dijo que no; si el cupo se libera, alguien tiene que volver a pedirlo, no
    /// insistir. Esta propiedad existe para que esa decisión se tome UNA vez, aquí, y no en cada
    /// orquestador.
    /// </remarks>
    public bool IsTransient => Kind == RejectionKind.Unavailable;

    public override string ToString() => $"{Kind}/{Code}: {Message}";
}
