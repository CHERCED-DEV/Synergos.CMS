using Synergos.Api.Moderation.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Moderation.Domain;

/// <summary>Compone las reglas de <see cref="ModerationRules"/> con el almacén.</summary>
public sealed class ModerationService
{
    private readonly IModerationStore _store;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public ModerationService(IModerationStore store, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _store = store;
        _idempotency = idempotency;
        _clock = clock;
    }

    public Result<ModerationCase> Report(Ref target, Ref? reporter, string? reason, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("case", key) is { } yaEra)
            {
                return _store.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{ModerationRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el caso no está.");
            }

            var motivo = ModerationRules.CheckReport(reason);
            if (motivo is not null) return Result.Rejected<ModerationCase>(motivo);

            // Varios reportes sobre lo mismo NO se colapsan en uno: cada persona que reporta
            // aporta un motivo distinto, y fundirlos perdería la señal de cuánta gente se quejó
            // — que es justo lo que hace que un caso suba de prioridad.
            var id = Guid.NewGuid().ToString("n");
            var caso = new ModerationCase(id, target, reporter, reason!.Trim(),
                CaseStatus.Open, null, null, null, _clock.GetUtcNow());

            _store.Put(caso);
            _idempotency.Remember("case", key, id);
            return Result.Ok(caso);
        }
    }

    public Result<ModerationCase> Get(string id)
        => _store.Find(id) is { } c
            ? Result.Ok(c)
            : Rejection.NotFound($"{ModerationRules.CodePrefix}.case_not_found", $"No existe el caso {id}.");

    public Result<ModerationCase> Decide(string id, Decision decision, Ref? decidedBy, string? note)
    {
        lock (_gate)
        {
            var caso = _store.Find(id);
            if (caso is null)
            {
                return Rejection.NotFound($"{ModerationRules.CodePrefix}.case_not_found", $"No existe el caso {id}.");
            }

            var motivo = ModerationRules.CheckDecidable(caso, decidedBy, note);
            if (motivo is not null) return Result.Rejected<ModerationCase>(motivo);

            var decidido = caso with
            {
                Status = CaseStatus.Decided,
                Decision = decision,
                DecidedBy = decidedBy,
                DecisionNote = note!.Trim(),
                DecidedAtUtc = _clock.GetUtcNow(),
            };

            _store.Put(decidido);
            return Result.Ok(decidido);
        }
    }

    /// <summary>La cola de lo pendiente.</summary>
    public Page<ModerationCase> Queue(int offset, int limit)
    {
        var todos = _store.Queue();
        return new Page<ModerationCase>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset);
    }

    public Result<Page<ModerationCase>> ListForTarget(Ref? target, int offset, int limit)
    {
        if (target is null)
        {
            return Rejection.Invalid($"{ModerationRules.CodePrefix}.target_required", "Hace falta filtrar por objetivo.");
        }

        var todos = _store.ForTarget(target)
            .OrderByDescending(c => c.ReportedAtUtc)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<ModerationCase>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }
}
