using Synergos.Api.Documents.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Documents.Domain;

/// <summary>Compone las reglas de <see cref="DocumentRules"/> con los almacenes.</summary>
public sealed class DocumentService
{
    private readonly IDocumentStore _documents;
    private readonly IBlobStore _blobs;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly string _signingKey;
    private readonly object _gate = new();

    public DocumentService(
        IDocumentStore documents, IBlobStore blobs, IIdempotencyLedger idempotency,
        TimeProvider clock, string signingKey)
    {
        _documents = documents;
        _blobs = blobs;
        _idempotency = idempotency;
        _clock = clock;
        _signingKey = signingKey;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Document> Upload(Ref owner, string? name, string? contentType, byte[]? content, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("document", key) is { } yaEra)
            {
                return _documents.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{DocumentRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero el documento no está.");
            }

            var motivo = DocumentRules.CheckUpload(name, contentType, content);
            if (motivo is not null) return Result.Rejected<Document>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var doc = new Document(id, owner, name!.Trim(), contentType!, content!.LongLength,
                DocumentRules.Fingerprint(content), Now);

            // El binario PRIMERO y el metadato después: al revés, una caída entre las dos
            // escrituras dejaría un documento listado que no se puede descargar — y eso se
            // investiga como corrupción, no como una escritura a medias.
            _blobs.Write(id, content);
            _documents.Put(doc);
            _idempotency.Remember("document", key, id);
            return Result.Ok(doc);
        }
    }

    public Result<Document> Get(string id)
    {
        var doc = _documents.Find(id);
        if (doc is null || doc.Deleted)
        {
            // Un documento retirado responde NotFound y no Gone: quien pregunta por un
            // identificador que ya no sirve no tiene por qué enterarse de que existió.
            return Rejection.NotFound($"{DocumentRules.CodePrefix}.document_not_found", $"No existe el documento {id}.");
        }
        return Result.Ok(doc);
    }

    public Result<Page<Document>> ListForOwner(Ref? owner, int offset, int limit)
    {
        if (owner is null)
        {
            return Rejection.Invalid($"{DocumentRules.CodePrefix}.owner_required",
                "Hace falta filtrar por dueño: sin filtro esto es un volcado del almacén.");
        }

        var todos = _documents.ForOwner(owner)
            .Where(d => !d.Deleted)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Document>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }

    /// <summary>Emite un enlace firmado y con vencimiento.</summary>
    public Result<(string Token, DateTimeOffset ExpiresAt)> MintLink(string id, TimeSpan? lifetime)
    {
        var doc = Get(id);
        if (!doc.IsOk) return Result.Rejected<(string, DateTimeOffset)>(doc.Rejection!);

        var vigencia = lifetime ?? DocumentRules.DefaultLinkLifetime;
        if (vigencia <= TimeSpan.Zero || vigencia > DocumentRules.MaxLinkLifetime)
        {
            return Rejection.Invalid($"{DocumentRules.CodePrefix}.bad_lifetime",
                $"La vigencia del enlace tiene que estar entre 0 y {DocumentRules.MaxLinkLifetime}.");
        }

        var expira = Now + vigencia;
        return Result.Ok((DocumentRules.SignLink(id, expira, _signingKey), expira));
    }

    /// <summary>Descarga con un enlace firmado.</summary>
    public Result<(Document Document, byte[] Content)> Download(string id, long expiresAtUnix, string? token)
    {
        var expira = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);

        // La firma se comprueba ANTES de mirar si el documento existe: al revés, un 404 con
        // firma inventada ya diría que ese identificador no está — y probando identificadores
        // se dibuja el inventario.
        var motivo = DocumentRules.CheckLink(id, expira, token, _signingKey, Now);
        if (motivo is not null) return Result.Rejected<(Document, byte[])>(motivo);

        var doc = Get(id);
        if (!doc.IsOk) return Result.Rejected<(Document, byte[])>(doc.Rejection!);

        var content = _blobs.Read(id);
        return content is null
            ? Rejection.Conflict($"{DocumentRules.CodePrefix}.content_missing",
                "El metadato está pero el contenido no. El almacén quedó inconsistente.")
            : Result.Ok((doc.Value, content));
    }

    /// <summary>Retira un documento: borra el contenido y marca el metadato.</summary>
    public Result<Document> Delete(string id)
    {
        lock (_gate)
        {
            var doc = _documents.Find(id);
            if (doc is null)
            {
                return Rejection.NotFound($"{DocumentRules.CodePrefix}.document_not_found", $"No existe el documento {id}.");
            }
            if (doc.Deleted) return Result.Ok(doc);   // idempotente: retirar lo retirado no es un error

            _blobs.Delete(id);
            var retirado = doc with { Deleted = true };
            _documents.Put(retirado);
            return Result.Ok(retirado);
        }
    }
}
