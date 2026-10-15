using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Api.Consent.Contracts;
using Synergos.Api.Consent.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Consent.Endpoints;

/// <summary>El ruteo del consentimiento.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO, su nombre con <c>WithName</c> y lo que lee
/// de la petición —la llave, el token de identidad— con su metadato (núcleo N2, el molde F2 de la ADR
/// 0140): de ahí sale el contrato publicado (<c>docs/contracts/openapi/Synergos.Api.Consent.json</c>),
/// contra el que se cruza el paso <c>consent.verificar</c>. Las rutas, los estados y los cuerpos son los
/// de antes.
/// </remarks>
public static class ConsentEndpoints
{
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/grants", Results<Created<ConsentResponse>, ProblemHttpResult> (
            GrantRequest req, HttpRequest http, ConsentService svc,
            IdentityTokenGate identidad, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, ConsentRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            var (assertion, motivo) = Afirmacion(identidad, http, subject, req.Assertion, clock);
            if (assertion is null) return motivo!.ToProblem();

            return svc.Grant(subject, req.Purpose, req.PolicyVersion, req.ExpiresAtUtc, key, assertion.Value)
                .Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToCreated(g => $"/v1/grants/{g.Id}");
        }).WithName("GrantConsent").ConLlaveDeIdempotencia().ConTokenDeIdentidad();

        app.MapGet("/v1/grants/{id}", (string id, ConsentService svc, TimeProvider clock) =>
            svc.Get(id).Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp()).WithName("GetConsent");

        app.MapGet("/v1/grants", (string? subjectKind, string? subjectId, int? offset, int? limit, ConsentService svc, TimeProvider clock) =>
        {
            var ahora = clock.GetUtcNow();
            return svc.ListForSubject(Ref.TryCreate(subjectKind, subjectId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<ConsentResponse>(
                    p.Items.Select(g => ConsentResponse.From(g, ahora)).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        }).WithName("ListConsents");

        // Consultar es POST porque el sujeto y el propósito son datos personales por su sola
        // combinación: "¿este paciente autorizó tratamiento oncológico?" no puede quedar en la
        // URL, donde vive en logs de proxy y en el historial.
        app.MapPost("/v1/grants/check", Results<Ok<ConsentResponse>, ProblemHttpResult> (
            CheckConsentRequest req, ConsentService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");
            if (string.IsNullOrWhiteSpace(req.Purpose)) return Invalid("bad_purpose", "Hace falta el propósito.");

            return svc.Check(subject, req.Purpose!).Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp();
        }).WithName("CheckConsent");

        // Revocar TAMBIÉN afirma identidad, y no es simetría por gusto: retirar el permiso de
        // otro es tan grave como darlo en su nombre. Un registro que dijera quién lo dio y con
        // qué, pero no quién lo quitó, dejaría el hueco justo donde más duele.
        app.MapPost("/v1/grants/revoke", Results<Ok<ConsentResponse>, ProblemHttpResult> (
            RevokeRequest req, HttpRequest http, ConsentService svc,
            IdentityTokenGate identidad, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");
            if (string.IsNullOrWhiteSpace(req.Purpose)) return Invalid("bad_purpose", "Hace falta el propósito.");

            var (assertion, motivo) = Afirmacion(identidad, http, subject, req.Assertion, clock);
            if (assertion is null) return motivo!.ToProblem();

            return svc.Revoke(subject, req.Purpose!, assertion.Value)
                .Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp();
        }).WithName("RevokeConsent").ConTokenDeIdentidad();

        // EL DERECHO AL OLVIDO TAMBIÉN SE COMPRUEBA (defecto #83).
        //
        // Éste era el tercer endpoint que escribe, y el gate de esta capacidad —que decía, con
        // todas las letras, que un gate que sólo mirara `grants` dejaría `grants/revoke` de puerta
        // de atrás— tenía el razonamiento bien y la LISTA corta. Con la llave compartida sola,
        // cualquiera retiraba TODOS los consentimientos de cualquier persona, y no quedaba nada
        // escrito sobre quién lo pidió.
        //
        // Y hay una ironía en el propio fichero: `Forget` revoca en vez de borrar «para poder
        // demostrar que la revocación se atendió», que es justo lo que exige quien la pidió. Esa
        // prueba no decía quién la pidió — le faltaba la mitad que la sostiene.
        //
        // Se exige LO MISMO que `revoke` y ni un escalón más: quien ejerce el derecho es el propio
        // sujeto. Si algún día hace falta que un operador lo ejerza en su nombre —una petición por
        // ventanilla— eso es otra cosa: hay que distinguir «revocado por el titular» de «revocado
        // por un tercero a petición suya», y hoy no lo pide nadie.
        app.MapPost("/v1/grants/forget", Results<Ok<ForgetResponse>, ProblemHttpResult> (
            ForgetRequest req, HttpRequest http, ConsentService svc,
            IdentityTokenGate identidad, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            var (assertion, motivo) = Afirmacion(identidad, http, subject, req.Assertion, clock);
            if (assertion is null) return motivo!.ToProblem();

            return TypedResults.Ok(new ForgetResponse(svc.Forget(subject, assertion.Value)));
        }).WithName("ForgetSubject").ConTokenDeIdentidad();

        return app;
    }

    /// <summary>
    /// Con qué fuerza se afirmó la identidad de quien actúa — <b>lo decide ESTA capacidad</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>No se cree lo declarado</b> (HU #14, defecto #42). Si presenta token, se verifica
    /// en local y se comprueba que su sujeto sea el mismo <paramref name="who"/>: sin eso el token
    /// sería decoración y la capacidad seguiría creyendo el sujeto que le mandan. Si no presenta,
    /// lo más fuerte que se le acepta es <c>CmsSession</c>, que es honesto porque significa «nos
    /// fiamos de quien llama».</para>
    ///
    /// <para><b>Verificación LOCAL</b>, sin llamar a <c>Api.Identity</c>: llamarla en cada
    /// petición la convertiría en el punto único de fallo de las veinte. Con esto,
    /// <c>Api.Identity</c> caída significa «no entran sesiones nuevas», no «se para todo».</para>
    ///
    /// <para>Vive acá y no en <c>Domain/</c> porque la decisión necesita la <b>petición HTTP</b> —
    /// la cabecera del token—, que el servicio no tiene ni debe tener. Lo que baja a
    /// <c>ConsentService</c> es la afirmación ya resuelta.</para>
    /// </remarks>
    private static (IdentityAssertion? Assertion, Rejection? Rejection) Afirmacion(
        IdentityTokenGate identidad, HttpRequest http, Ref who, string? declarada, TimeProvider clock)
    {
        // Se parsea acá y no en el servicio para poder distinguir «no vino» de «vino un valor que
        // no existe» — las dos van al mismo rechazo, pero con detalle distinto.
        IdentityAssertion? afirmada = Enum.TryParse<IdentityAssertion>(declarada, ignoreCase: true, out var a)
            ? a
            : null;

        return IdentityAssertions.Resolve(
            identidad,
            http.Headers[IdentityTokens.HeaderName].FirstOrDefault(),
            who,
            afirmada,
            clock.GetUtcNow(),
            ConsentRules.CodePrefix);
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{ConsentRules.CodePrefix}.{code}", message).ToProblem();
}
