using Synergos.Api.Notifications.Contracts;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Notifications.Endpoints;

/// <summary>El ruteo de los avisos.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/templates", (SaveTemplateRequest req, HttpRequest http, NotificationService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, NotificationRules.CodePrefix, out var key, out var falta)) return falta!;

            if (!Enum.TryParse<Channel>(req.Channel, ignoreCase: true, out var canal))
            {
                return Invalid("bad_channel", $"'{req.Channel}' no es un canal. Son: {string.Join(", ", Enum.GetNames<Channel>())}.");
            }

            return svc.SaveTemplate(req.Key, canal, req.Subject, req.Body, key).Match(
                t => Results.Created($"/v1/templates/{t.Id}", TemplateResponse.From(t)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/templates/{id}", (string id, NotificationService svc) =>
            svc.GetTemplate(id).Map(TemplateResponse.From).ToHttp());

        app.MapGet("/v1/templates", (int? offset, int? limit, NotificationService svc) =>
        {
            var page = svc.ListTemplates(Math.Max(0, offset ?? 0), QueryWindow.Limit(limit));
            return Results.Ok(new PageResponse<TemplateResponse>(
                page.Items.Select(TemplateResponse.From).ToList(), page.Total, page.Offset, page.HasMore));
        });

        app.MapPost("/v1/deliveries", (SendRequest req, HttpRequest http, NotificationService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, NotificationRules.CodePrefix, out var key, out var falta)) return falta!;

            var to = Ref.TryCreate(req.ToKind, req.ToId);
            if (to is null) return Invalid("bad_recipient", "Hacen falta toKind y toId.");

            return svc.Send(to, req.Address, req.TemplateKey, req.Values, key).Match(
                d => Results.Created($"/v1/deliveries/{d.Id}", DeliveryResponse.From(d)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/deliveries/{id}", (string id, NotificationService svc) =>
            svc.GetDelivery(id).Map(DeliveryResponse.From).ToHttp());

        app.MapGet("/v1/deliveries", (string? toKind, string? toId, int? offset, int? limit, NotificationService svc) =>
            svc.ListDeliveries(Ref.TryCreate(toKind, toId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<DeliveryResponse>(
                    p.Items.Select(DeliveryResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{NotificationRules.CodePrefix}.{code}", message).ToProblem();
}
