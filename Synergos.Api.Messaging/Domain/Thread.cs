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

/// <summary>Un mensaje dentro de un hilo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="ThreadId">De qué hilo.</param>
/// <param name="From">Quién lo escribió.</param>
/// <param name="Body">Qué dijo.</param>
/// <param name="Attachments">Referencias a documentos. El binario vive en <c>Api.Documents</c>.</param>
/// <param name="ReadBy">Quiénes ya lo leyeron.</param>
/// <param name="AtUtc">Cuándo.</param>
/// <remarks>
/// <b>Los adjuntos son referencias, no bytes.</b> Guardar el binario acá duplicaría lo que
/// <c>Api.Documents</c> ya hace —con su lista blanca de tipos, su huella y sus enlaces
/// firmados— y crearía dos sitios donde borrar cuando alguien ejerce el derecho al olvido.
/// </remarks>
public sealed record Message(
    string Id, string ThreadId, Ref From, string Body,
    IReadOnlyList<Ref> Attachments, IReadOnlyList<Ref> ReadBy, DateTimeOffset AtUtc);
