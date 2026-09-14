using Modelo = Synergos.Api.Cart.Domain;

namespace Synergos.Api.Cart.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Abrir una canasta.</summary>
/// <param name="OwnerKind">El vocabulario del dueño — <c>tienda.comprador</c>.</param>
/// <param name="OwnerId">Quién, dentro de ese vocabulario. Opaco.</param>
/// <param name="TtlHours">Cuánto vive, si no se quiere la vigencia por defecto.</param>
/// <param name="Assertion">
/// Con qué dice el llamador que se afirmó la identidad del dueño. <b>Es una declaración, no un
/// hecho</b>: lo que se guarda lo decide esta capacidad tras mirar el token (HU #14), y lo único
/// que se acepta sin prueba es lo más débil.
/// </param>
public sealed record OpenCartRequest(string? OwnerKind, string? OwnerId, int? TtlHours, string? Assertion);

/// <summary>Poner o quitar una línea.</summary>
public sealed record CartLineRequest(string? SubjectKind, string? SubjectId, int? Quantity);

/// <summary>Cómo sale una línea.</summary>
public sealed record CartLineResponse(string SubjectKind, string SubjectId, int Quantity);

/// <summary>Cómo sale una canasta.</summary>
/// <param name="OpenedWith">
/// Con qué se afirmó la identidad del dueño al abrirla. <b>Nulo es «no consta»</b>, y sale para
/// que quien lea pueda distinguir una canasta respaldada por un token verificado de una abierta
/// con la sola llave compartida. Sin exponerlo, el gate sería invisible desde fuera.
/// </param>
public sealed record CartResponse(
    string Id, string OwnerKind, string OwnerId, IReadOnlyList<CartLineResponse> Lines,
    int TotalUnits, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc, bool CheckedOut, bool Open,
    string? OpenedWith)
{
    public static CartResponse From(Modelo.Cart c, DateTimeOffset now) => new(
        c.Id, c.Owner.Kind, c.Owner.Id,
        c.Lines.Select(l => new CartLineResponse(l.Subject.Kind, l.Subject.Id, l.Quantity)).ToList(),
        c.TotalUnits, c.CreatedAtUtc, c.ExpiresAtUtc, c.CheckedOut, c.IsOpen(now),
        c.OpenedWith?.ToString());
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
