using Synergos.Core;

namespace Synergos.Api.Messaging.Domain;

/// <summary>Una conversación entre participantes.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Topic">Sobre qué es — opaco. Una cita, un expediente, una publicación.</param>
/// <param name="Participants">Quiénes pueden leer y escribir.</param>
/// <param name="Closed">Si ya no admite mensajes.</param>
/// <param name="OpenedAtUtc">Cuándo se abrió.</param>
/// <remarks>
/// <para><b>Es humano↔humano y bidireccional</b>, y por eso no es <c>Api.Notifications</c>:
/// aquella es sistema→humano de una vía. Comparten la palabra "mensaje" y nada más — distinto
/// almacén, distinta retención, distinto modo de fallo (doc 07 §2).</para>
///
/// <para><b>El régimen lo pone el orquestador, no esta capacidad.</b> Hoy un solo
/// <c>IMessagingService</c> sirve la in-basket clínica, la correspondencia gubernamental y los
/// DMs sociales — <i>tres regímenes regulatorios sobre un stub</i>. Acá el transporte es uno y
/// quien decide qué se conserva, quién puede leer y por cuánto tiempo es el dominio.</para>
/// </remarks>
public sealed record MessageThread(
    string Id, Ref Topic, IReadOnlyList<Ref> Participants, bool Closed, DateTimeOffset OpenedAtUtc)
{
    public bool Includes(Ref who) => Participants.Contains(who);
}

/// <summary>
/// Cómo se afirmó la identidad de quien accedió.
/// </summary>
/// <remarks>
/// <para><b>Este campo es lo que hace reversible la decisión de quién certifica la identidad.</b>
/// Sin él, el día que se pase a una afirmación más fuerte los registros viejos quedarían
/// indistinguibles de los nuevos — y todo el archivo pasaría a afirmar más de lo que puede
/// sostener. Con él, un auditor ve que el acceso de agosto se certificó con la sesión del CMS y
/// el de octubre con un token verificable.</para>
///
/// <para><b>El orden es de fuerza creciente</b>, y no es decorativo: es lo que permite responder
/// «¿este acuse aguanta?» sin volver a leer el código que lo escribió.</para>
/// </remarks>
public enum IdentityAssertion
{
    /// <summary>
    /// Lo afirma la sesión del CMS. <b>Registro de acceso autenticado, no acuse con valor
    /// probatorio</b>: quien certifica es nuestro propio sistema. Sirve para operar, auditar y
    /// discutir de buena fe; no para sostener solo que un término empezó a correr.
    /// </summary>
    CmsSession = 1,

    /// <summary><c>Api.Identity</c> emitió un token verificable.</summary>
    IdentityToken = 2,

    /// <summary>Federación con Autenticación Digital del Estado (Dec. 620/2020).</summary>
    GovFederation = 3,
}

/// <summary>
/// Que alguien accedió a un mensaje: quién, cuándo, y con qué fuerza se afirmó que era él.
/// </summary>
/// <param name="Who">Quién accedió.</param>
/// <param name="AtUtc">Cuándo accedió. <b>Distinto de <see cref="Message.AtUtc"/></b>, que es
/// cuándo se envió.</param>
/// <param name="Assertion">Con qué se certificó la identidad.</param>
/// <remarks>
/// <b>Los tres campos juntos, o no sirve.</b> Una lista de <i>quiénes</i> —que es lo que había
/// antes— no puede certificar fecha y hora de acceso, que es literalmente lo que exige la
/// notificación electrónica del CPACA. Y sin el tercero, el registro no sabe cuánto vale.
/// </remarks>
public sealed record Acknowledgment(Ref Who, DateTimeOffset AtUtc, IdentityAssertion Assertion);

/// <summary>Un mensaje dentro de un hilo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="ThreadId">De qué hilo.</param>
/// <param name="From">Quién lo escribió.</param>
/// <param name="Body">Qué dijo.</param>
/// <param name="Attachments">Referencias a documentos. El binario vive en <c>Api.Documents</c>.</param>
/// <param name="Acknowledgments">Quiénes accedieron, cuándo, y con qué afirmación de identidad.</param>
/// <param name="AtUtc">Cuándo se <b>envió</b> — no cuándo se leyó.</param>
/// <param name="AcknowledgeBeforeUtc">Hasta cuándo se admite registrar el acceso, o
/// <c>null</c> si no hay plazo.</param>
/// <remarks>
/// <para><b>Los adjuntos son referencias, no bytes.</b> Guardar el binario acá duplicaría lo que
/// <c>Api.Documents</c> ya hace —con su lista blanca de tipos, su huella y sus enlaces
/// firmados— y crearía dos sitios donde borrar cuando alguien ejerce el derecho al olvido.</para>
///
/// <para><b><c>Acknowledgments</c> reemplaza a <c>ReadBy</c>, y el cambio no es cosmético.</b>
/// Era <c>IReadOnlyList&lt;Ref&gt;</c>: una lista de <i>quién</i>, sin <i>cuándo</i>. Con esa
/// forma era imposible certificar fecha y hora de acceso — y ése es el dato del que depende que
/// un término legal haya empezado a correr.</para>
///
/// <para><b><c>AcknowledgeBeforeUtc</c> es una fecha, no una política.</b> La capacidad es dueña
/// del <i>cuándo</i>: sabe que después de esa fecha ya no certifica. Quién la calcula —diez días
/// hábiles desde el envío, en Gobierno— es cosa del orquestador. Poner acá la regla que la deriva
/// metería un régimen regulatorio dentro de una capacidad que sirve a seis dominios.</para>
/// </remarks>
public sealed record Message(
    string Id, string ThreadId, Ref From, string Body,
    IReadOnlyList<Ref> Attachments, IReadOnlyList<Acknowledgment> Acknowledgments, DateTimeOffset AtUtc,
    DateTimeOffset? AcknowledgeBeforeUtc = null)
{
    /// <summary>El acuse de esta persona, si accedió.</summary>
    public Acknowledgment? AcknowledgmentOf(Ref who) => Acknowledgments.FirstOrDefault(a => a.Who == who);
}
