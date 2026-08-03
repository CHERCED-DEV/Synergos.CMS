using Modelo = Synergos.Api.Cart.Domain;

namespace Synergos.Api.Cart.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Abrir una canasta.</summary>
public sealed record OpenCartRequest(string? OwnerKind, string? OwnerId, int? TtlHours);

/// <summary>Poner o quitar una línea.</summary>
public sealed record CartLineRequest(string? SubjectKind, string? SubjectId, int? Quantity);

/// <summary>Cómo sale una línea.</summary>
public sealed record CartLineResponse(string SubjectKind, string SubjectId, int Quantity);

/// <summary>Cómo sale una canasta.</summary>
public sealed record CartResponse(
    string Id, string OwnerKind, string OwnerId, IReadOnlyList<CartLineResponse> Lines,
    int TotalUnits, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, bool CheckedOut, bool Open)
{
    public static CartResponse From(Modelo.Cart c, DateTimeOffset now) => new(
        c.Id, c.Owner.Kind, c.Owner.Id,
        c.Lines.Select(l => new CartLineResponse(l.Subject.Kind, l.Subject.Id, l.Quantity)).ToList(),
        c.TotalUnits, c.CreatedAtUtc, c.ExpiresAtUtc, c.CheckedOut, c.IsOpen(now));
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
