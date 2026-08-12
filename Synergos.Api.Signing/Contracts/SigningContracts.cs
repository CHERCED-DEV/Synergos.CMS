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

/// <summary>Sellar un contenido: identificador opaco, permanente y determinista.</summary>
public sealed record SealRequest(string? Purpose, string? Payload);

/// <summary>
/// Comprobar que un sello es el que le corresponde a ese contenido.
/// </summary>
/// <remarks>
/// <b>Lleva el contenido, no solo el sello</b>, y esa es la diferencia con
/// <see cref="VerifyRequest"/>. Un sello no se lee: sin el contenido al lado no hay nada contra
/// qué compararlo, que es justo la propiedad por la que no publica a su titular.
/// </remarks>
public sealed record VerifySealRequest(string? Purpose, string? Payload, string? Seal);

/// <summary>Cómo sale una llave. <b>SIN el material.</b></summary>
public sealed record KeyResponse(string Id, string Purpose, DateTimeOffset CreatedAtUtc, DateTimeOffset? RetiredAtUtc, bool CanSign)
{
    public static KeyResponse From(SigningKey k) => new(k.Id, k.Purpose, k.CreatedAtUtc, k.RetiredAtUtc, k.CanSign);
}

/// <summary>Un token firmado.</summary>
public sealed record SignatureResponse(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>Lo que había dentro de un token válido.</summary>
public sealed record VerifiedResponse(string Payload);

/// <summary>Un sello, con la llave que lo produjo.</summary>
public sealed record SealResponse(string Seal, string KeyId);

/// <summary>
/// Un sello que cuadró, con la llave que cuadró.
/// </summary>
/// <remarks>
/// <b>No devuelve el contenido</b>, al revés que <see cref="VerifiedResponse"/>: quien pregunta
/// ya lo trajo. Devolverlo daría a entender que el sello lo contiene.
/// </remarks>
public sealed record SealVerifiedResponse(string KeyId);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
