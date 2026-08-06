using Synergos.Api.Inventory.Contracts;
using Synergos.Api.Inventory.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Inventory.Endpoints;

/// <summary>El ruteo del inventario.</summary>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/items", (DeclareStockRequest req, HttpRequest http, InventoryService svc, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, InventoryRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            var units = new List<StockUnit>();
            foreach (var u in req.Units ?? Array.Empty<StockUnitDto>())
            {
                if (string.IsNullOrWhiteSpace(u.Code)) return Invalid("bad_unit_code", "Cada unidad necesita un código.");
                units.Add(new StockUnit(u.Code!.Trim(), u.Row, u.Column));
            }

            return svc.Declare(subject, req.OnHand ?? 0, units, key).Match(
                i => Results.Created($"/v1/items/{i.Id}", StockItemResponse.From(i, clock.GetUtcNow())),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/items/{id}", (string id, InventoryService svc, TimeProvider clock) =>
            svc.Get(id).Map(i => StockItemResponse.From(i, clock.GetUtcNow())).ToHttp());

        // Consulta por la referencia que la capacidad GUARDA, no por su identificador interno.
        // Es lo que permite que quien tiene un Ref de producto —un carrito, un pedido, un
        // orquestador— llegue a sus existencias sin llevar un mapa aparte que se desincronice.
        app.MapGet("/v1/items", (string? subjectKind, string? subjectId, InventoryService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(subjectKind, subjectId);
            if (subject is null)
            {
                // Sin filtro esto sería un volcado del inventario entero por HTTP.
                return Invalid("subject_required", "Hacen falta subjectKind y subjectId.");
            }

            return svc.GetBySubject(subject).Map(i => StockItemResponse.From(i, clock.GetUtcNow())).ToHttp();
        });

        // Dos formas de ajustar, y el cuerpo dice cuál. Ver AdjustStockRequest: `delta` es
        // «cuánto cambió» y `onHand` es «cuánto hay», que no son la misma pregunta.
        app.MapPost("/v1/items/{id}/adjust", (string id, AdjustStockRequest req, HttpRequest http, InventoryService svc, TimeProvider clock) =>
        {
            if (req.Delta is not null && req.OnHand is not null)
            {
                return Invalid("ambiguous_adjust",
                    "Van delta (cuánto cambió) u onHand (cuánto hay), no los dos: son dos órdenes distintas.");
            }

            if (req.Delta is { } delta)
            {
                // La llave es OBLIGATORIA acá y no en el absoluto. Un relativo reintentado suma
                // dos veces, y pedirla al borde es lo que impide que arreglar el ajuste perdido
                // haya creado el ajuste doble.
                if (!IdempotencyHeader.TryRead(http, InventoryRules.CodePrefix, out var key, out var falta)) return falta!;
                return svc.AdjustBy(id, delta, key).Map(i => StockItemResponse.From(i, clock.GetUtcNow())).ToHttp();
            }

            if (req.OnHand is { } total)
            {
                return svc.AdjustTo(id, total).Map(i => StockItemResponse.From(i, clock.GetUtcNow())).ToHttp();
            }

            return Invalid("adjust_required", "Hace falta delta (cuánto cambió) u onHand (cuánto hay).");
        });

        app.MapPost("/v1/items/{id}/holds", (string id, HoldStockRequest req, HttpRequest http, InventoryService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, InventoryRules.CodePrefix, out var key, out var falta)) return falta!;

            var forWhat = Ref.TryCreate(req.ForKind, req.ForId);
            if (forWhat is null) return Invalid("bad_for", "Hacen falta forKind y forId.");

            var ttl = req.TtlMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;
            var codes = (req.UnitCodes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();

            return svc.Hold(id, req.Quantity ?? 0, codes, forWhat, ttl, key).Match(
                h => Results.Created($"/v1/holds/{h.Id}", StockHoldResponse.From(h)),
                bad => bad.ToProblem());
        });

        app.MapPost("/v1/holds/{id}/release", (string id, InventoryService svc) =>
            svc.ReleaseHold(id).Map(StockHoldResponse.From).ToHttp());

        app.MapPost("/v1/holds/{id}/consume", (string id, InventoryService svc) =>
            svc.ConsumeHold(id).Map(StockHoldResponse.From).ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{InventoryRules.CodePrefix}.{code}", message).ToProblem();
}
