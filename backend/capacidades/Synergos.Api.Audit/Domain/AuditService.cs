using Synergos.Api.Audit.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Audit.Domain;

/// <summary>Compone las reglas de <see cref="AuditRules"/> con el almacén append-only.</summary>
public sealed class AuditService
{
    /// <summary>Tope de filas por consulta. Una bitácora crece sin parar; una consulta no puede.</summary>
    public const int MaxRows = 500;

    private readonly IAuditStore _store;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public AuditService(IAuditStore store, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _store = store;
        _idempotency = idempotency;
        _clock = clock;
    }

    /// <param name="actedWith">
    /// Con qué se afirmó la identidad del actor. La resuelve el borde —necesita la cabecera del
    /// token, que el servicio no tiene ni debe tener—; acá sólo se guarda.
    /// </param>
    public Result<AuditEntry> Append(
        Actor? actor, string? action, Ref? target,
        IReadOnlyDictionary<string, string>? details, IdempotencyKey key,
        IdentityAssertion? actedWith = null)
    {
        lock (_gate)
        {
            // La llave primero, antes de validar: un reintento tiene que devolver lo ya escrito.
            if (_idempotency.Find("entry", key) is { } yaEra)
            {
                return _store.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{AuditRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero la entrada no está.");
            }

            var motivo = AuditRules.CheckWritable(actor, action, target, details);
            if (motivo is not null) return Result.Rejected<AuditEntry>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var entry = new AuditEntry(id, actor!, action!.Trim(), target!, _clock.GetUtcNow(),
                details ?? new Dictionary<string, string>(StringComparer.Ordinal), actedWith);

            _store.Append(entry);
            _idempotency.Remember("entry", key, id);
            return Result.Ok(entry);
        }
    }

    public Result<AuditEntry> Get(string id)
        => _store.Find(id) is { } e
            ? Result.Ok(e)
            : Rejection.NotFound($"{AuditRules.CodePrefix}.entry_not_found", $"No existe la entrada {id}.");

    public Result<Page<AuditEntry>> Query(
        Ref? target, string? actorId, TimeWindow window, int offset, int limit)
    {
        var motivo = AuditRules.CheckQuery(target, actorId);
        if (motivo is not null) return Result.Rejected<Page<AuditEntry>>(motivo);

        var todas = _store.Query(target, actorId, window);
        var pagina = todas.Skip(offset).Take(Math.Min(limit, MaxRows)).ToList();
        return Result.Ok(new Page<AuditEntry>(pagina, todas.Count, offset));
    }
}
