using Synergos.Api.Identity.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Identity.Domain;

/// <summary>Compone las reglas de <see cref="IdentityRules"/> con el almacén.</summary>
public sealed class IdentityService
{
    private readonly IPrincipalStore _principals;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public IdentityService(IPrincipalStore principals, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _principals = principals;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Principal> Register(Ref subject, string? secret, IReadOnlyList<string> roles, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("principal", key) is { } yaEra)
            {
                return _principals.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{IdentityRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero el principal no está.");
            }

            if (_principals.FindBySubject(subject) is not null)
            {
                // Conflict y no Invalid: la petición está bien, el estado ya tiene ese sujeto.
                // Devolver el existente sería peor — dos registros distintos con la misma llave
                // de idempotencia habrían compartido credencial sin que nadie lo pidiera.
                return Rejection.Conflict($"{IdentityRules.CodePrefix}.subject_taken",
                    $"Ya hay un principal para {subject}.");
            }

            var motivo = IdentityRules.CheckSecret(secret);
            if (motivo is not null) return Result.Rejected<Principal>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var principal = new Principal(id, subject, IdentityRules.Derive(secret!), Normalize(roles));

            _principals.Put(principal);
            _idempotency.Remember("principal", key, id);
            return Result.Ok(principal);
        }
    }

    public Result<Principal> Get(string id)
        => _principals.Find(id) is { } p
            ? Result.Ok(p)
            : Rejection.NotFound($"{IdentityRules.CodePrefix}.principal_not_found", $"No existe el principal {id}.");

    public Page<Principal> List(int offset, int limit)
    {
        var todos = _principals.All().OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        return new Page<Principal>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset);
    }

    /// <summary>
    /// Verifica una credencial. Devuelve el <see cref="Actor"/> que el resto del sistema usa.
    /// </summary>
    /// <remarks>
    /// Devuelve un <c>Actor</c> y no el <c>Principal</c>: el llamador no tiene por qué ver la
    /// credencial derivada ni el contador de fallos, y lo que necesita para autorizar y auditar
    /// es exactamente lo que un <c>Actor</c> lleva.
    /// </remarks>
    /// <summary>El principal de un sujeto, o <c>null</c> si no hay ninguno registrado.</summary>
    /// <remarks>
    /// Hace falta para emitir tokens (HU #14): el sujeto tiene que EXISTIR. Sin esta
    /// comprobación, cualquiera con la llave compartida emitiría tokens para identidades
    /// inventadas y el token dejaría de significar «esta capacidad conoce a esta persona».
    /// </remarks>
    public Principal? FindBySubject(Ref subject)
    {
        lock (_gate) { return _principals.FindBySubject(subject); }
    }

    /// <summary>
    /// Emite un token para un sujeto que ya se autenticó en otro sitio (HU #14, camino (b)).
    /// </summary>
    /// <remarks>
    /// <para><b>El sujeto tiene que EXISTIR como principal.</b> Sin eso, cualquiera con la llave
    /// compartida emitiría tokens para identidades inventadas, y el token dejaría de significar
    /// «esta capacidad conoce a esta persona» — que es lo único que aporta sobre una afirmación
    /// del llamador.</para>
    ///
    /// <para><b>Y un principal bloqueado no recibe token</b>, aunque el CMS jure que se
    /// autenticó. El bloqueo es de esta capacidad y tiene que valer también acá; si no, bastaría
    /// con pedir un token para saltárselo.</para>
    ///
    /// <para>Vive en el servicio y no en el endpoint a propósito (<c>CLAUDE.md</c> §15): las
    /// reglas metidas en un lambda de ruteo no se pueden probar sin levantar el host, y lo
    /// primero que se descubre al mutarlas es que las sostenía el compilador.</para>
    /// </remarks>
    public Result<IdentityClaims> IssueToken(Ref subject, int lifetimeMinutes)
    {
        lock (_gate)
        {
            var principal = _principals.FindBySubject(subject);
            if (principal is null)
            {
                return Rejection.NotFound($"{IdentityRules.CodePrefix}.principal_not_found",
                    $"No hay principal registrado para {subject}.");
            }

            var ahora = Now;
            if (principal.IsLocked(ahora))
            {
                return Rejection.Conflict($"{IdentityRules.CodePrefix}.principal_locked",
                    "El principal está bloqueado; no se emiten tokens para él.");
            }

            return Result.Ok(new IdentityClaims(
                principal.Subject, principal.Roles, ahora, ahora.AddMinutes(lifetimeMinutes), ahora));
        }
    }

    /// <summary>
    /// Renueva un token vigente, refrescando los roles y respetando el techo de la sesión.
    /// </summary>
    /// <remarks>
    /// <para><b>Solo se renueva lo VIGENTE</b>: aceptar un token vencido volvería la vigencia
    /// corta un adorno, porque quien se hiciera con uno lo tendría para siempre.</para>
    ///
    /// <para><b>El techo se cuenta desde que empezó la SESIÓN</b>, no desde el último token. Si
    /// se contara desde el último no sería un techo: sería la misma vigencia con otro nombre, y
    /// un token robado se renovaría indefinidamente de a quince minutos.</para>
    ///
    /// <para><b>Los roles se refrescan acá</b>, y es lo que acota el costo de llevarlos dentro
    /// del token: revocar uno tarda, como mucho, lo que quede de vigencia.</para>
    /// </remarks>
    public Result<IdentityClaims> RenewToken(
        IdentityTokens tokens, string? rawToken, int lifetimeMinutes, int maxSessionMinutes)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var ahora = Now;
        var (claims, motivo) = tokens.Verify(rawToken, ahora);
        if (claims is null) return IdentityTokens.ToRejection(motivo!.Value);

        if (ahora >= claims.SessionStartedAtUtc.AddMinutes(maxSessionMinutes))
        {
            return Rejection.Invalid($"{IdentityRules.CodePrefix}.session_expired",
                "La sesión llegó a su límite; hay que volver a autenticarse.");
        }

        lock (_gate)
        {
            var principal = _principals.FindBySubject(claims.Subject);
            if (principal is null || principal.IsLocked(ahora))
            {
                return Rejection.Conflict($"{IdentityRules.CodePrefix}.principal_locked",
                    "El principal ya no puede operar; no se renueva.");
            }

            return Result.Ok(new IdentityClaims(
                claims.Subject, principal.Roles, ahora, ahora.AddMinutes(lifetimeMinutes),
                claims.SessionStartedAtUtc));
        }
    }

    public Result<Actor> Authenticate(Ref subject, string? secret)
    {
        lock (_gate)
        {
            var principal = _principals.FindBySubject(subject);
            var resultado = IdentityRules.Authenticate(principal, secret, Now);

            if (principal is not null)
            {
                // El contador se persiste SIEMPRE, acierte o falle: si solo se guardara al
                // fallar, el reseteo del acierto no llegaría al disco y el bloqueo acabaría
                // cayendo sobre alguien que entra todos los días.
                _principals.Put(principal with
                {
                    FailedAttempts = resultado.FailedAttempts,
                    LockedUntilUtc = resultado.LockedUntilUtc,
                });
            }

            return resultado.Rejection is { } bad
                ? Result.Rejected<Actor>(bad)
                : Result.Ok(principal!.AsActor());
        }
    }

    public Result<Principal> GrantRoles(string id, IReadOnlyList<string> roles)
    {
        lock (_gate)
        {
            if (_principals.Find(id) is not { } principal)
            {
                return Rejection.NotFound($"{IdentityRules.CodePrefix}.principal_not_found", $"No existe el principal {id}.");
            }

            var union = Normalize(principal.Roles.Concat(roles).ToList());
            var actualizado = principal with { Roles = union };
            _principals.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    public Result<Principal> RevokeRoles(string id, IReadOnlyList<string> roles)
    {
        lock (_gate)
        {
            if (_principals.Find(id) is not { } principal)
            {
                return Rejection.NotFound($"{IdentityRules.CodePrefix}.principal_not_found", $"No existe el principal {id}.");
            }

            var quedan = principal.Roles
                .Where(r => !roles.Any(x => string.Equals(x.Trim(), r, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            var actualizado = principal with { Roles = quedan };
            _principals.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    /// <summary>Levanta el bloqueo por fuerza bruta.</summary>
    public Result<Principal> Unlock(string id)
    {
        lock (_gate)
        {
            if (_principals.Find(id) is not { } principal)
            {
                return Rejection.NotFound($"{IdentityRules.CodePrefix}.principal_not_found", $"No existe el principal {id}.");
            }

            var actualizado = principal with { FailedAttempts = 0, LockedUntilUtc = null };
            _principals.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    /// <summary>Sin vacíos, sin repetidos, sin sensibilidad a mayúsculas — como en <see cref="Actor"/>.</summary>
    private static IReadOnlyList<string> Normalize(IEnumerable<string> roles)
        => roles.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
