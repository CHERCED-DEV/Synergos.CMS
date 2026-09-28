using Synergos.Api.Payments.Contracts;
using Synergos.Api.Payments.Domain;
using Synergos.Api.Payments.Transport;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Payments.Endpoints;

/// <summary>El ruteo de cobros.</summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/payments", async (
            AuthorizeRequest req, HttpRequest http, PaymentService svc,
            IdentityTokenGate identidad, TimeProvider clock, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;

            var forWhat = Ref.TryCreate(req.ForKind, req.ForId);
            if (forWhat is null) return Invalid("bad_for", "Hacen falta forKind y forId.");
            var payer = Ref.TryCreate(req.PayerKind, req.PayerId);
            if (payer is null) return Invalid("bad_payer", "Hacen falta payerKind y payerId.");
            if (!TryMoney(req.Amount, out var amount, out var badMoney)) return badMoney!;

            // La afirmacion se resuelve ANTES de tocar nada, y la decide la capacidad.
            // El discriminador es el RECHAZO y no la afirmacion (#168): ausente es «no consta»,
            // que es un resultado valido y no un fallo, asi que `assertion is null` ya no sirve
            // para decidir — habria tratado el caso bueno como el malo.
            var (assertion, motivo) = Afirmacion(identidad, http, payer, req.Assertion, clock);
            if (motivo is not null) return motivo.ToProblem();

            return (await svc.AuthorizeAsync(forWhat, payer, amount, assertion, key, ct)).Match(
                p => Results.Created($"/v1/payments/{p.Id}", PaymentResponse.From(p)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/payments/{id}", (string id, PaymentService svc) =>
            svc.Get(id).Map(PaymentResponse.From).ToHttp());

        app.MapGet("/v1/payments", (string? forKind, string? forId, int? offset, int? limit, PaymentService svc) =>
            svc.ListFor(Ref.TryCreate(forKind, forId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<PaymentResponse>(
                    p.Items.Select(PaymentResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/payments/{id}/capture", async (
            string id, HttpRequest http, PaymentService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;
            return (await svc.CaptureAsync(id, key, ct)).Map(PaymentResponse.From).ToHttp();
        });

        // Liberar NO lleva llave: la operación ya es idempotente por diseño —liberar lo
        // liberado devuelve lo mismo— y exigir una cabecera que no protege de nada solo
        // enseñaría a los clientes a inventar llaves.
        app.MapPost("/v1/payments/{id}/void", async (string id, PaymentService svc, CancellationToken ct) =>
            (await svc.VoidAsync(id, ct)).Map(PaymentResponse.From).ToHttp());

        app.MapPost("/v1/payments/{id}/refund", async (
            string id, RefundRequest req, HttpRequest http, PaymentService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;
            if (!TryMoney(req.Amount, out var amount, out var badMoney)) return badMoney!;

            return (await svc.RefundPaymentAsync(id, amount, req.Reason, key, ct)).Map(PaymentResponse.From).ToHttp();
        });

        // El camino de vuelta: lo que la pasarela cuenta de un cobro suyo.
        //
        // Es el ÚNICO endpoint que no va detrás de la llave compartida, porque quien lo llama es
        // un tercero que no la tiene. Lo que lo protege es la firma — y sin ella cualquiera que
        // supiera la URL marcaría un cobro como pagado y el pedido saldría.
        app.MapPost("/v1/webhooks/wompi", async (
            HttpRequest http, WebhookVerifier verificador, PaymentService svc, CancellationToken ct) =>
        {
            // El cuerpo se lee crudo: la firma cubre los bytes exactos, y deserializar y volver a
            // serializar cambia espacios y orden. Un verificador que firma sobre el objeto
            // reconstruido falla de una forma que parece intermitente.
            using var lector = new StreamReader(http.Body);
            var cuerpo = await lector.ReadToEndAsync(ct);

            return await WebhookHandler.HandleAsync(
                new WebhookHeaders(http.Headers["X-Event-Checksum"].FirstOrDefault()),
                cuerpo, verificador, svc, ct);
        });

        return app;
    }

    private static bool TryMoney(MoneyDto? dto, out Money money, out IResult? bad)
    {
        money = default;
        bad = null;
        if (dto is null || string.IsNullOrWhiteSpace(dto.Currency))
        {
            bad = Invalid("bad_money", "Hace falta un monto con su moneda: {amount, currency}.");
            return false;
        }
        try
        {
            money = Money.Of(dto.Amount, dto.Currency);
            return true;
        }
        catch (ArgumentException)
        {
            bad = Invalid("bad_currency", $"'{dto.Currency}' no es un código ISO 4217.");
            return false;
        }
    }

    /// <summary>
    /// Con qué se afirmó la identidad de quien paga — <b>lo decide esta capacidad</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Sólo este endpoint la pide, y no es un olvido en los otros.</b>
    /// <c>/v1/payments</c> es el ÚNICO donde el llamador NOMBRA a una persona; capture, void,
    /// refund y el GET llegan con el identificador de un cobro que ya sabe de quién es. Pedirles
    /// identidad no añadiría prueba ninguna: movería el campo de sitio. Es el mismo corte que
    /// <c>Api.Cart</c>, donde sólo <c>POST /v1/carts</c> nombra al dueño.</para>
    ///
    /// <para><b>Que alguien con la llave compartida pueda capturar el cobro de otro si adivina su
    /// identificador es la OTRA pregunta</b> —autorizar, no atribuir— y esto no la contesta, ni
    /// en ésta ni en ninguna capacidad.</para>
    ///
    /// <para><b>Verificación LOCAL</b>, sin llamar a <c>Api.Identity</c>: llamarla en cada cobro
    /// la convertiría en el punto único de fallo, y sería además una flecha capacidad→capacidad,
    /// prohibida de plano y con gate (#49). Y la regla no se reimplementa acá — vive en
    /// <c>Synergos.Shared</c>, porque dos copias se desvían en silencio y las dos compilan.</para>
    /// </remarks>
    /// <summary>
    /// Qué significa —en ESTA capacidad— presentar o no presentar una afirmación de identidad.
    /// </summary>
    /// <remarks>
    /// <para><b>Ausente es «no consta», y no un rechazo</b> (#168). Esto exigía una afirmación sin
    /// condición, y **ningún orquestador puede mandarla**: propagar el token del CMS a través de
    /// una saga está descartado por diseño —un token es una credencial CON RELOJ y una saga es
    /// trabajo CON DURACIÓN; para que sobreviviera habría que guardar un bearer en el almacén de
    /// sagas, que se respalda—. Medido levantando esta capacidad en proceso: el mismo cuerpo que
    /// manda `Bff.Tienda` contestaba <c>400 payments.access_requires_identity</c>, así que con
    /// <c>Payments:Mode=Api</c> **no se podía cobrar nada** por ninguno de los cuatro
    /// orquestadores, y con <c>transient: false</c> la saga no reintentaba: compensaba.
    /// `CLAUDE.md` §11 ya decía que esta puerta NO debía existir por esa vía; lo que estaba mal
    /// era el código.</para>
    ///
    /// <para><b>La decisión vive ACÁ y no en <see cref="IdentityAssertions.Resolve"/>.</b> Aflojar
    /// el helper compartido habría aflojado la bitácora, y ahí el criterio es el contrario y está
    /// escrito: «un asiento que no registra ninguna se volvería un hueco». Cada capacidad decide
    /// qué significa la ausencia; lo que NO se duplica es la verificación del token, que sigue
    /// viviendo una sola vez en `Shared`. Meterlo como una bandera del helper habría escondido la
    /// política donde nadie la lee.</para>
    ///
    /// <para><b>Y el corte que importa: AUSENTE no es lo mismo que PRESENTE E ILEGIBLE.</b> El
    /// parseo devolvía <c>null</c> para las dos cosas, así que tratar «null» como «no consta»
    /// habría hecho que un <c>assertion: "SuperFuerte"</c> pasara como no-consta en vez de
    /// rechazarse — el campo existe justamente para impedir eso (defecto #42). Se mira la cadena
    /// cruda: sólo su ausencia total es «no consta».</para>
    /// </remarks>
    private static (IdentityAssertion? Assertion, Rejection? Rejection) Afirmacion(
        IdentityTokenGate identidad, HttpRequest http, Ref who, string? declarada, TimeProvider clock)
    {
        var token = http.Headers[IdentityTokens.HeaderName].FirstOrDefault();

        // Nadie presentó nada y nadie declaró nada: «no consta», que es la verdad y es lo que el
        // `PaidWith` nulo de `Payment` y de `PaymentResponse` ya existían para decir.
        if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(declarada))
        {
            return (null, null);
        }

        // Se parsea acá y no en el servicio para distinguir «no vino» de «vino algo que no
        // existe»: la primera es «no consta» y la segunda se RECHAZA.
        IdentityAssertion? afirmada = Enum.TryParse<IdentityAssertion>(declarada, ignoreCase: true, out var a)
            ? a
            : null;

        return IdentityAssertions.Resolve(
            identidad,
            token,
            who,
            afirmada,
            clock.GetUtcNow(),
            PaymentRules.CodePrefix);
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{PaymentRules.CodePrefix}.{code}", message).ToProblem();
}
