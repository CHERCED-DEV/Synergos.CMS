using Synergos.Api.Signing.Domain;

namespace Synergos.Api.Signing.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación evita el
// peor accidente posible de esta capacidad: SigningKey lleva el material de la llave, y
// serializarlo por error entregaría la capacidad de firmar a quien pregunte.

/// <summary>Crear una llave para un propósito.</summary>
public sealed record CreateKeyRequest(string? Purpose);

/// <summary>Firmar un contenido.</summary>
public sealed record SignRequest(string? Purpose, string? Payload, int? LifetimeMinutes);

/// <summary>Verificar un token.</summary>
public sealed record VerifyRequest(string? Token);

/// <summary>Cómo sale una llave. <b>SIN el material.</b></summary>
public sealed record KeyResponse(string Id, string Purpose, DateTimeOffset CreatedAtUtc, DateTimeOffset? RetiredAtUtc, bool CanSign)
{
    public static KeyResponse From(SigningKey k) => new(k.Id, k.Purpose, k.CreatedAtUtc, k.RetiredAtUtc, k.CanSign);
}

/// <summary>Un token firmado.</summary>
public sealed record SignatureResponse(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>Lo que había dentro de un token válido.</summary>
public sealed record VerifiedResponse(string Payload);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
