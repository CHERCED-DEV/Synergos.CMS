using Synergos.Api.Documents.Domain;

namespace Synergos.Api.Documents.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Subir un documento. El contenido viaja en base64.</summary>
/// <remarks>
/// Base64 y no multipart: es un 33% más de bytes, y a cambio el contrato es un JSON como el de
/// las otras diecinueve capacidades. Con el tope de 10 MB la diferencia no paga la excepción al
/// molde; el día que haya que subir vídeo, esa cuenta cambia y con ella el contrato.
/// </remarks>
public sealed record UploadDocumentRequest(
    string? OwnerKind, string? OwnerId, string? Name, string? ContentType, string? ContentBase64);

/// <summary>Pedir un enlace de descarga.</summary>
public sealed record MintLinkRequest(int? LifetimeMinutes);

/// <summary>Cómo sale un documento.</summary>
public sealed record DocumentResponse(
    string Id, string OwnerKind, string OwnerId, string Name, string ContentType,
    long Size, string Sha256, DateTimeOffset UploadedAtUtc)
{
    public static DocumentResponse From(Document d) => new(
        d.Id, d.Owner.Kind, d.Owner.Id, d.Name, d.ContentType, d.Size, d.Sha256, d.UploadedAtUtc);
}

/// <summary>Un enlace firmado, listo para armar la URL de descarga.</summary>
public sealed record LinkResponse(string DocumentId, string Token, long ExpiresAtUnix, string Url);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
