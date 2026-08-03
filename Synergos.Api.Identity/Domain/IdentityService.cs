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
