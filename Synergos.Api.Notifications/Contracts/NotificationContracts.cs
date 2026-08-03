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
public sealed record DeliveryResponse(
    string Id, string ToKind, string ToId, string Address, string Channel,
    string TemplateKey, string Subject, string Status, DateTimeOffset AtUtc)
{
    public static DeliveryResponse From(Delivery d) => new(
        d.Id, d.To.Kind, d.To.Id, d.Address, d.Channel.ToString(),
        d.TemplateKey, d.Subject, d.Status.ToString(), d.AtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
