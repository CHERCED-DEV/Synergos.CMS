using Synergos.Core;

namespace Synergos.Api.Messaging.Domain;

/// <summary>Lo que la mensajería rechaza <b>sola</b>.</summary>
public static class MessagingRules
{
    public const string CodePrefix = "messaging";

    public const int MaxBodyLength = 20_000;
    public const int MaxAttachments = 10;
    public const int MaxParticipants = 100;

    /// <summary>Si el hilo que llega se puede abrir.</summary>
    public static Rejection? CheckOpen(IReadOnlyList<Ref> participants)
    {
        if (participants.Count < 2)
        {
            // Un hilo de una sola persona es una nota, no una conversación — y si lo que se
            // quería era una nota, esta no es la capacidad.
            return Rejection.Invalid($"{CodePrefix}.too_few_participants",
                "Una conversación necesita al menos dos participantes.");
        }
        if (participants.Count > MaxParticipants)
        {
            return Rejection.Invalid($"{CodePrefix}.too_many_participants", $"Hasta {MaxParticipants} participantes.");
        }
        return participants.Distinct().Count() != participants.Count
            ? Rejection.Invalid($"{CodePrefix}.duplicate_participants", "Hay participantes repetidos.")
            : null;
    }

    /// <summary>Si se puede escribir en el hilo.</summary>
    public static Rejection? CheckPost(MessageThread thread, Ref from, string? body, IReadOnlyList<Ref> attachments)
    {
        // El orden importa: primero si puede estar acá, y solo entonces si el hilo está cerrado.
        // Al revés, alguien ajeno al hilo aprendería que existe y que se cerró.
        if (!thread.Includes(from))
        {
            return Rejection.Forbidden($"{CodePrefix}.not_a_participant", "Quien escribe no está en el hilo.");
        }
        if (thread.Closed)
        {
            return Rejection.Conflict($"{CodePrefix}.thread_closed", "El hilo está cerrado.");
        }
        if (string.IsNullOrWhiteSpace(body) && attachments.Count == 0)
        {
            return Rejection.Invalid($"{CodePrefix}.empty_message", "Un mensaje sin texto ni adjuntos no dice nada.");
        }
        if (body is { Length: > MaxBodyLength })
        {
            return Rejection.Invalid($"{CodePrefix}.body_too_long", $"El mensaje pasa de {MaxBodyLength} caracteres.");
        }
        return attachments.Count > MaxAttachments
            ? Rejection.Invalid($"{CodePrefix}.too_many_attachments", $"Hasta {MaxAttachments} adjuntos por mensaje.")
            : null;
    }

    /// <summary>Si alguien puede leer el hilo.</summary>
    public static Rejection? CheckRead(MessageThread thread, Ref who)
        => thread.Includes(who)
            ? null
            : Rejection.Forbidden($"{CodePrefix}.not_a_participant", "Quien pregunta no está en el hilo.");

    /// <summary>
    /// Si el acceso que se quiere registrar puede certificarse.
    /// </summary>
    /// <remarks>
    /// <para><b>La afirmación de identidad es obligatoria, y ése es el punto entero.</b> Un acuse
    /// dice que <i>esta persona</i> accedió; sin decir quién lo certifica, el sistema estaría
    /// dando fe de lo que el propio llamador le contó. Puede parecer un campo más; es la
    /// diferencia entre un registro que sirve y uno que solo parece servir.</para>
    ///
    /// <para>Se rechaza <b>antes</b> de mirar si es participante: quien no dice cómo se
    /// identificó no debería enterarse de si el hilo existe.</para>
    /// </remarks>
    public static Rejection? CheckAcknowledge(MessageThread thread, Ref who, IdentityAssertion? assertion)
    {
        if (assertion is null || !Enum.IsDefined(assertion.Value))
        {
            return Rejection.Invalid($"{CodePrefix}.access_requires_identity",
                "Hace falta decir cómo se afirmó la identidad de quien accede: sin eso el registro no certifica nada.");
        }
        return CheckRead(thread, who);
    }

    /// <summary>
    /// Si el plazo para registrar el acceso sigue abierto.
    /// </summary>
    /// <remarks>
    /// <para><b>Se comprueba DESPUÉS de resolver si ya había acuse</b>, y ése es el punto delicado.
    /// Quien accedió dentro del plazo y vuelve a entrar tres meses después tiene que recibir
    /// <i>su</i> acuse, el de la primera vez — no un rechazo. Al revés, el sistema le negaría a la
    /// persona el registro que ella misma generó a tiempo (CLAUDE.md §0.B, punto 16).</para>
    ///
    /// <para><b>Sin plazo no se cierra nunca.</b> El plazo es de Gobierno; un DM social no tiene
    /// término que corra, y obligar a uno haría inservible la capacidad para los otros cinco
    /// dominios que la usan.</para>
    /// </remarks>
    public static Rejection? CheckAcknowledgeWindow(Message message, DateTimeOffset now)
        => message.AcknowledgeBeforeUtc is { } limite && now > limite
            ? Rejection.Conflict($"{CodePrefix}.acknowledgment_window_closed",
                $"El plazo para registrar el acceso venció el {limite:O}.")
            : null;
}
