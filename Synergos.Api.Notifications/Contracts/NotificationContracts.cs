using Synergos.Api.Notifications.Domain;

namespace Synergos.Api.Notifications.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Guardar una plantilla.</summary>
public sealed record SaveTemplateRequest(string? Key, string? Channel, string? Subject, string? Body);

/// <summary>Mandar un aviso.</summary>
public sealed record SendRequest(
    string? ToKind, string? ToId, string? Address, string? TemplateKey,
    IReadOnlyDictionary<string, string>? Values);

/// <summary>Cómo sale una plantilla.</summary>
public sealed record TemplateResponse(string Id, string Key, string Channel, string Subject, string Body)
{
    public static TemplateResponse From(Template t) => new(t.Id, t.Key, t.Channel.ToString(), t.Subject, t.Body);
}

/// <summary>Cómo sale un envío.</summary>
/// <param name="Status">
/// En qué va. <b><c>Accepted</c> no es «llegó»</b>: es «el proveedor se hace cargo de
/// intentarlo». Sale de aquella forma vieja que solo distinguía <c>Sent</c> de <c>Failed</c>.
/// </param>
/// <param name="ProviderMessageId">
/// El id del proveedor, o <c>null</c> si todavía no aceptó. Es con lo que se correlaciona el
/// webhook de vuelta.
/// </param>
/// <param name="StatusAtUtc">Cuándo cambió el estado por última vez — distinto de <c>AtUtc</c>.</param>
/// <param name="Attempts">Cuántas veces se intentó. Sin esto, quien barre no sabe cuándo rendirse.</param>
/// <param name="LastError">Por qué no salió la última vez. Sin esto, «se rindió» no es accionable.</param>
public sealed record DeliveryResponse(
    string Id, string ToKind, string ToId, string Address, string Channel,
    string TemplateKey, string Subject, string Status, DateTimeOffset AtUtc,
    string? ProviderMessageId, DateTimeOffset? StatusAtUtc,
    // Los dos existen para el barrido y para quien mira: sin Attempts no hay forma de saber
    // cuándo rendirse, y sin LastError «se rindió» no es accionable.
    int Attempts, string? LastError)
{
    public static DeliveryResponse From(Delivery d) => new(
        d.Id, d.To.Kind, d.To.Id, d.Address, d.Channel.ToString(),
        d.TemplateKey, d.Subject, d.Status.ToString(), d.AtUtc, d.ProviderMessageId, d.StatusAtUtc,
        d.Attempts, d.LastError);
}

/// <summary>
/// Lo que se le contesta a un proveedor cuando su evento es legítimo pero no cambia nada acá.
/// </summary>
/// <param name="Matched">Si el evento se pudo apuntar contra un envío de este despliegue.</param>
/// <param name="Reason">Por qué no, en texto — para que se pueda ver sin abrir el log.</param>
/// <remarks>
/// Existe para poder decir «recibido, y no hice nada» <b>sin mentir y sin provocar reintentos</b>.
/// Un 4xx haría que el proveedor insista durante días; un 200 a secas haría creer que se guardó.
/// </remarks>
public sealed record WebhookAck(bool Matched, string Reason);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);

/// <summary>Rendirse con su causa — sin causa, «se rindió» no es accionable.</summary>
public sealed record GiveUpRequest(string? Reason);
