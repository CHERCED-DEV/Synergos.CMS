using Synergos.Api.Messaging.Domain;

namespace Synergos.Api.Messaging.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Una referencia tal como llega o sale.</summary>
public sealed record RefDto(string? Kind, string? Id);

/// <summary>Abrir un hilo.</summary>
public sealed record OpenThreadRequest(string? TopicKind, string? TopicId, IReadOnlyList<RefDto>? Participants);

/// <summary>Escribir en un hilo.</summary>
/// <param name="FromKind">Tipo de quien escribe.</param>
/// <param name="FromId">Identificador de quien escribe.</param>
/// <param name="Body">El texto.</param>
/// <param name="Attachments">Referencias a documentos.</param>
/// <param name="AcknowledgeBeforeUtc">Hasta cuándo se admite registrar el acceso. Opcional: el
/// plazo lo define el dominio —Gobierno sí lo tiene, un DM social no— y lo pone el orquestador.
/// La capacidad solo sabe que hay una fecha y que después de ella no certifica.</param>
public sealed record PostRequest(
    string? FromKind, string? FromId, string? Body, IReadOnlyList<RefDto>? Attachments,
    DateTimeOffset? AcknowledgeBeforeUtc = null);

/// <summary>Registrar que alguien accedió a un mensaje.</summary>
/// <param name="WhoKind">Tipo de quien accede.</param>
/// <param name="WhoId">Identificador de quien accede.</param>
/// <param name="Assertion">Cómo se afirmó su identidad. <b>Obligatorio:</b> sin esto el registro
/// certificaría lo que el propio llamador dijo.</param>
public sealed record AcknowledgeRequest(string? WhoKind, string? WhoId, string? Assertion);

/// <summary>Cómo sale un hilo.</summary>
public sealed record ThreadResponse(
    string Id, string TopicKind, string TopicId, IReadOnlyList<RefDto> Participants, bool Closed, DateTimeOffset OpenedAtUtc)
{
    public static ThreadResponse From(MessageThread t) => new(
        t.Id, t.Topic.Kind, t.Topic.Id,
        t.Participants.Select(p => new RefDto(p.Kind, p.Id)).ToList(), t.Closed, t.OpenedAtUtc);
}

/// <summary>
/// Un acuse tal como sale: quién accedió, cuándo, y con qué se certificó su identidad.
/// </summary>
/// <param name="WhoKind">Tipo de quien accedió.</param>
/// <param name="WhoId">Identificador de quien accedió.</param>
/// <param name="AtUtc">Cuándo accedió. <b>No es cuándo se envió el mensaje.</b></param>
/// <param name="Assertion">Con qué se afirmó la identidad — <c>CmsSession</c> es un
/// <b>registro de acceso autenticado</b>, no un acuse con valor probatorio.</param>
public sealed record AcknowledgmentResponse(string WhoKind, string WhoId, DateTimeOffset AtUtc, string Assertion)
{
    public static AcknowledgmentResponse From(Acknowledgment a)
        => new(a.Who.Kind, a.Who.Id, a.AtUtc, a.Assertion.ToString());
}

/// <summary>Cómo sale un mensaje.</summary>
/// <remarks>
/// <b><c>Acknowledgments</c> reemplaza a <c>ReadBy</c>.</b> Aquella era una lista de <i>quién</i>
/// sin <i>cuándo</i>: no podía certificar fecha y hora de acceso, que es de lo que depende que un
/// término legal haya empezado a correr. Es un cambio de contrato deliberado, hecho antes de que
/// existieran datos.
/// </remarks>
public sealed record MessageResponse(
    string Id, string ThreadId, string FromKind, string FromId, string Body,
    IReadOnlyList<RefDto> Attachments, IReadOnlyList<AcknowledgmentResponse> Acknowledgments,
    DateTimeOffset AtUtc, DateTimeOffset? AcknowledgeBeforeUtc)
{
    public static MessageResponse From(Message m) => new(
        m.Id, m.ThreadId, m.From.Kind, m.From.Id, m.Body,
        m.Attachments.Select(a => new RefDto(a.Kind, a.Id)).ToList(),
        m.Acknowledgments.Select(AcknowledgmentResponse.From).ToList(), m.AtUtc, m.AcknowledgeBeforeUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
