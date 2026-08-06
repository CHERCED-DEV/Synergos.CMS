using Synergos.Api.Messaging.Contracts;
using Synergos.Api.Messaging.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Messaging.Endpoints;

/// <summary>El ruteo de la mensajería.</summary>
public static class MessagingEndpoints
{
    public static IEndpointRouteBuilder MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/threads", (OpenThreadRequest req, HttpRequest http, MessagingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, MessagingRules.CodePrefix, out var key, out var falta)) return falta!;

            var topic = Ref.TryCreate(req.TopicKind, req.TopicId);
            if (topic is null) return Invalid("bad_topic", "Hacen falta topicKind y topicId.");

            var participantes = new List<Ref>();
            foreach (var p in req.Participants ?? Array.Empty<RefDto>())
            {
                var r = Ref.TryCreate(p.Kind, p.Id);
                if (r is null) return Invalid("bad_participant", "Cada participante necesita kind e id.");
                participantes.Add(r);
            }

            return svc.OpenThread(topic, participantes, key).Match(
                t => Results.Created($"/v1/threads/{t.Id}", ThreadResponse.From(t)),
                bad => bad.ToProblem());
        });

        // Quién pregunta viaja en la query: un hilo solo lo ven sus participantes, y sin ese
        // dato la capacidad no puede decidir. Es un Ref, no un secreto.
        app.MapGet("/v1/threads/{id}", (string id, string? whoKind, string? whoId, MessagingService svc) =>
            svc.GetThread(id, Ref.TryCreate(whoKind, whoId)).Map(ThreadResponse.From).ToHttp());

        app.MapGet("/v1/threads", (string? whoKind, string? whoId, int? offset, int? limit, MessagingService svc) =>
            svc.ListThreads(Ref.TryCreate(whoKind, whoId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<ThreadResponse>(
                    p.Items.Select(ThreadResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/threads/{id}/close", (string id, MessagingService svc) =>
            svc.CloseThread(id).Map(ThreadResponse.From).ToHttp());

        app.MapPost("/v1/threads/{id}/messages", (string id, PostRequest req, HttpRequest http, MessagingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, MessagingRules.CodePrefix, out var key, out var falta)) return falta!;

            var from = Ref.TryCreate(req.FromKind, req.FromId);
            if (from is null) return Invalid("bad_from", "Hacen falta fromKind y fromId.");

            var adjuntos = new List<Ref>();
            foreach (var a in req.Attachments ?? Array.Empty<RefDto>())
            {
                var r = Ref.TryCreate(a.Kind, a.Id);
                if (r is null) return Invalid("bad_attachment", "Cada adjunto necesita kind e id.");
                adjuntos.Add(r);
            }

            return svc.Post(id, from, req.Body, adjuntos, key, req.AcknowledgeBeforeUtc).Match(
                m => Results.Created($"/v1/messages/{m.Id}", MessageResponse.From(m)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/threads/{id}/messages", (string id, string? whoKind, string? whoId, int? offset, int? limit, MessagingService svc) =>
            svc.ListMessages(id, Ref.TryCreate(whoKind, whoId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<MessageResponse>(
                    p.Items.Select(MessageResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        // Registra que una persona ACCEDIÓ al mensaje. No es «marcar como leído»: es el dato del
        // que depende que un término legal haya empezado a correr, y por eso exige decir cómo se
        // afirmó la identidad de quien accede.
        app.MapPost("/v1/messages/{id}/acknowledge", (string id, AcknowledgeRequest req, MessagingService svc) =>
        {
            var who = Ref.TryCreate(req.WhoKind, req.WhoId);
            if (who is null) return Invalid("bad_who", "Hacen falta whoKind y whoId.");

            // Se parsea acá y no en el servicio para poder distinguir «no vino» de «vino un
            // valor que no existe» — las dos van al mismo rechazo, pero con detalle distinto.
            IdentityAssertion? assertion = Enum.TryParse<IdentityAssertion>(req.Assertion, ignoreCase: true, out var a)
                ? a
                : null;
            if (assertion is null)
            {
                return Invalid("access_requires_identity",
                    $"Hace falta 'assertion' y tiene que ser una de: {string.Join(", ", Enum.GetNames<IdentityAssertion>())}.");
            }

            return svc.Acknowledge(id, who, assertion).Map(MessageResponse.From).ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{MessagingRules.CodePrefix}.{code}", message).ToProblem();
}
