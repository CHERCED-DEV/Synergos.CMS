using Synergos.Api.Consent.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Consent.Domain;

/// <summary>Compone las reglas de <see cref="ConsentRules"/> con el almacén.</summary>
public sealed class ConsentService
{
    private readonly IConsentStore _store;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public ConsentService(IConsentStore store, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _store = store;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <param name="assertion">
    /// Con qué fuerza se afirmó la identidad de quien lo da — <b>ya RESUELTA por el borde</b>, no
    /// lo que el llamador declaró. El servicio no puede saber si se presentó un token: eso vive
    /// en la petición HTTP, y por eso la decisión se toma en <c>Endpoints/</c> y llega hecha.
    /// </param>
    public Result<ConsentGrant> Grant(Ref subject, string? purpose, string? policyVersion, DateTimeOffset? expiresAt, IdempotencyKey key, IdentityAssertion assertion)
    {
        lock (_gate)
        {
            if (_idempotency.Find("grant", key) is { } yaEra)
            {
                return _store.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{ConsentRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el consentimiento no está.");
            }

            var motivo = ConsentRules.CheckGrant(purpose, policyVersion, expiresAt, Now);
            if (motivo is not null) return Result.Rejected<ConsentGrant>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var grant = new ConsentGrant(id, subject, purpose!.Trim(), policyVersion!.Trim(), Now, expiresAt, GrantedWith: assertion);
            _store.Put(grant);
            _idempotency.Remember("grant", key, id);
            return Result.Ok(grant);
        }
    }

    /// <summary>Retira el consentimiento vigente para ese propósito.</summary>
    public Result<ConsentGrant> Revoke(Ref subject, string purpose, IdentityAssertion assertion)
    {
        lock (_gate)
        {
            var grant = _store.FindLatest(subject, purpose);
            if (grant is null)
            {
                return Rejection.NotFound($"{ConsentRules.CodePrefix}.not_granted", $"No hay consentimiento para '{purpose}'.");
            }
            // Revocar lo revocado NO es un error: es el estado que el llamador quería.
            if (grant.RevokedAtUtc is not null) return Result.Ok(grant);

            var revocado = grant with { RevokedAtUtc = Now, RevokedWith = assertion };
            _store.Put(revocado);
            return Result.Ok(revocado);
        }
    }

    public Result<ConsentGrant> Get(string id)
        => _store.Find(id) is { } g
            ? Result.Ok(g)
            : Rejection.NotFound($"{ConsentRules.CodePrefix}.grant_not_found", $"No existe el consentimiento {id}.");

    /// <summary>La pregunta que hacen las demás capacidades: ¿puedo hacer esto?</summary>
    public Result<ConsentGrant> Check(Ref subject, string purpose)
    {
        var grant = _store.FindLatest(subject, purpose);
        var motivo = ConsentRules.CheckActive(grant, purpose, Now);
        return motivo is not null ? Result.Rejected<ConsentGrant>(motivo) : Result.Ok(grant!);
    }

    public Result<Page<ConsentGrant>> ListForSubject(Ref? subject, int offset, int limit)
    {
        if (subject is null)
        {
            return Rejection.Invalid($"{ConsentRules.CodePrefix}.subject_required", "Hace falta filtrar por sujeto.");
        }

        var todos = _store.ForSubject(subject)
            .OrderByDescending(g => g.GrantedAtUtc)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<ConsentGrant>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }

    /// <summary>
    /// Derecho al olvido: revoca TODO lo de ese sujeto y devuelve cuántos permisos tocó.
    /// </summary>
    /// <remarks>
    /// <b>Revoca, no borra.</b> Borrar la fila haría imposible demostrar que la revocación se
    /// atendió — y esa demostración es exactamente lo que exige quien la pidió. Lo que se borra
    /// son los datos personales, y esos no viven acá.
    /// </remarks>
    public int Forget(Ref subject)
    {
        lock (_gate)
        {
            var tocados = 0;
            foreach (var grant in _store.ForSubject(subject).Where(g => g.RevokedAtUtc is null))
            {
                _store.Put(grant with { RevokedAtUtc = Now });
                tocados++;
            }
            return tocados;
        }
    }
}
