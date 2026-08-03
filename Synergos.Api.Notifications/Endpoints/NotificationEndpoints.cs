using Synergos.Api.Notifications.Contracts;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Transport;
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

        app.MapPost("/v1/deliveries", async (SendRequest req, HttpRequest http, NotificationService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, NotificationRules.CodePrefix, out var key, out var falta)) return falta!;

            var to = Ref.TryCreate(req.ToKind, req.ToId);
            if (to is null) return Invalid("bad_recipient", "Hacen falta toKind y toId.");

            var r = await svc.SendAsync(to, req.Address, req.TemplateKey, req.Values, key, ct);
            return r.Match(
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

        // El camino de vuelta: lo que el proveedor cuenta de un envío suyo.
        //
        // Es el ÚNICO endpoint que no va detrás de la llave compartida, porque quien lo llama es
        // un tercero que no la tiene. Lo que lo protege es la firma — y por eso la verificación
        // es lo primero que pasa acá, antes de leer el cuerpo como si significara algo.
        app.MapPost("/v1/webhooks/resend", async (
            HttpRequest http, WebhookVerifier verificador, NotificationService svc) =>
        {
            // El cuerpo se lee crudo: la firma cubre los bytes exactos, y deserializar y volver a
            // serializar cambia espacios y orden. Un verificador que firma sobre el objeto
            // reconstruido falla de una forma que parece intermitente.
            using var lector = new StreamReader(http.Body);
            var cuerpo = await lector.ReadToEndAsync();

            return WebhookHandler.Handle(
                new WebhookHeaders(
                    http.Headers["svix-id"].FirstOrDefault(),
                    http.Headers["svix-timestamp"].FirstOrDefault(),
                    http.Headers["svix-signature"].FirstOrDefault()),
                cuerpo, verificador, svc);
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{NotificationRules.CodePrefix}.{code}", message).ToProblem();
}
