using Synergos.Api.Documents.Contracts;
using Synergos.Api.Documents.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Documents.Endpoints;

/// <summary>El ruteo de los documentos.</summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/documents", (UploadDocumentRequest req, HttpRequest http, DocumentService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, DocumentRules.CodePrefix, out var key, out var falta)) return falta!;

            var owner = Ref.TryCreate(req.OwnerKind, req.OwnerId);
            if (owner is null) return Invalid("bad_owner", "Hacen falta ownerKind y ownerId.");

            byte[] content;
            try
            {
                content = Convert.FromBase64String(req.ContentBase64 ?? string.Empty);
            }
            catch (FormatException)
            {
                // Se atrapa acá y se traduce: dejarla subir sería un 500, y un 500 le dice al
                // cliente "reintentá" cuando lo que tiene que hacer es arreglar lo que mandó.
                return Invalid("bad_base64", "contentBase64 no es base64 válido.");
            }

            return svc.Upload(owner, req.Name, req.ContentType, content, key).Match(
                d => Results.Created($"/v1/documents/{d.Id}", DocumentResponse.From(d)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/documents/{id}", (string id, DocumentService svc) =>
            svc.Get(id).Map(DocumentResponse.From).ToHttp());

        app.MapGet("/v1/documents", (string? ownerKind, string? ownerId, int? offset, int? limit, DocumentService svc) =>
            svc.ListForOwner(Ref.TryCreate(ownerKind, ownerId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<DocumentResponse>(
                    p.Items.Select(DocumentResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/documents/{id}/links", (string id, MintLinkRequest? req, DocumentService svc) =>
        {
            var vigencia = req?.LifetimeMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;

            return svc.MintLink(id, vigencia).Match(
                l => Results.Ok(new LinkResponse(id, l.Token, l.ExpiresAt.ToUnixTimeSeconds(),
                    $"/v1/documents/{id}/content?expires={l.ExpiresAt.ToUnixTimeSeconds()}&token={l.Token}")),
                bad => bad.ToProblem());
        });

        // Sin Idempotency-Key porque no muta nada, y SIN llave de servicio no: la exención de
        // SharedKeyAuth es solo /health. El enlace firmado no reemplaza la autenticación del
        // servicio; la complementa, acotando QUÉ puede leer quien ya entró.
        app.MapGet("/v1/documents/{id}/content", (string id, long? expires, string? token, DocumentService svc) =>
        {
            if (expires is null) return Invalid("link_unsigned", "Falta el vencimiento del enlace.");

            return svc.Download(id, expires.Value, token).Match(
                d => Results.File(d.Content, d.Document.ContentType, d.Document.Name),
                bad => bad.ToProblem());
        });

        app.MapPost("/v1/documents/{id}/delete", (string id, DocumentService svc) =>
            svc.Delete(id).Map(DocumentResponse.From).ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{DocumentRules.CodePrefix}.{code}", message).ToProblem();
}
