using Synergos.Api.Workflow.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Workflow.Domain;

/// <summary>Compone las reglas de <see cref="WorkflowRules"/> con los almacenes.</summary>
public sealed class WorkflowService
{
    private readonly IDefinitionStore _definitions;
    private readonly IInstanceStore _instances;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public WorkflowService(IDefinitionStore definitions, IInstanceStore instances, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _definitions = definitions;
        _instances = instances;
        _idempotency = idempotency;
        _clock = clock;
    }

    public Result<WorkflowDefinition> Define(
        string? key, string? initial, IReadOnlyList<string> finals, IReadOnlyList<TransitionRule> transitions, IdempotencyKey idem)
    {
        lock (_gate)
        {
            if (_idempotency.Find("definition", idem) is { } yaEra)
            {
                return _definitions.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{WorkflowRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la definición no está.");
            }

            var motivo = WorkflowRules.CheckDefinition(key, initial, finals, transitions);
            if (motivo is not null) return Result.Rejected<WorkflowDefinition>(motivo);

            if (_definitions.FindByKey(key!) is not null)
            {
                // No se reescribe: hay instancias corriendo sobre la definición vigente, y
                // cambiarle las transiciones bajo los pies las dejaría en estados imposibles.
                // Versionar una definición es publicar otra clave.
                return Rejection.Conflict($"{WorkflowRules.CodePrefix}.key_taken",
                    $"Ya hay una definición '{key}'. Cambiarla bajo instancias en marcha las dejaría en estados imposibles: publica otra clave.");
            }

            var id = Guid.NewGuid().ToString("n");
            var def = new WorkflowDefinition(id, key!.Trim(), initial!.Trim(), finals, transitions);
            _definitions.Put(def);
            _idempotency.Remember("definition", idem, id);
            return Result.Ok(def);
        }
    }

    public Result<WorkflowDefinition> GetDefinition(string key)
        => _definitions.FindByKey(key) is { } d
            ? Result.Ok(d)
            : Rejection.NotFound($"{WorkflowRules.CodePrefix}.definition_not_found", $"No existe la definición '{key}'.");

    public Result<WorkflowInstance> Start(string? definitionKey, Ref subject, IdempotencyKey idem)
    {
        lock (_gate)
        {
            if (_idempotency.Find("instance", idem) is { } yaEra)
            {
                return _instances.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{WorkflowRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la instancia no está.");
            }

            if (string.IsNullOrWhiteSpace(definitionKey) || _definitions.FindByKey(definitionKey!) is not { } def)
            {
                return Rejection.NotFound($"{WorkflowRules.CodePrefix}.definition_not_found", $"No existe la definición '{definitionKey}'.");
            }

            var id = Guid.NewGuid().ToString("n");
            var instancia = new WorkflowInstance(id, def.Key, subject, def.InitialState,
                Array.Empty<HistoryEntry>(), _clock.GetUtcNow());

            _instances.Put(instancia);
            _idempotency.Remember("instance", idem, id);
            return Result.Ok(instancia);
        }
    }

    public Result<WorkflowInstance> GetInstance(string id)
        => _instances.Find(id) is { } i
            ? Result.Ok(i)
            : Rejection.NotFound($"{WorkflowRules.CodePrefix}.instance_not_found", $"No existe la instancia {id}.");

    public Result<WorkflowInstance> Fire(string instanceId, string? transition, Actor actor, string? note, IdempotencyKey idem)
    {
        lock (_gate)
        {
            if (_idempotency.Find("fire", idem) is { } yaEra)
            {
                return _instances.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{WorkflowRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la instancia no está.");
            }

            var instancia = _instances.Find(instanceId);
            if (instancia is null)
            {
                return Rejection.NotFound($"{WorkflowRules.CodePrefix}.instance_not_found", $"No existe la instancia {instanceId}.");
            }
            if (_definitions.FindByKey(instancia.DefinitionKey) is not { } def)
            {
                return Rejection.Conflict($"{WorkflowRules.CodePrefix}.definition_gone",
                    $"La instancia sigue la definición '{instancia.DefinitionKey}', que ya no está.");
            }

            var regla = WorkflowRules.Resolve(def, instancia, transition, actor);
            if (!regla.IsOk) return Result.Rejected<WorkflowInstance>(regla.Rejection!);

            var paso = new HistoryEntry(regla.Value.Name, instancia.State, regla.Value.To, actor.Principal, note?.Trim(), _clock.GetUtcNow());
            var avanzada = instancia with { State = regla.Value.To, History = instancia.History.Append(paso).ToList() };

            _instances.Put(avanzada);
            _idempotency.Remember("fire", idem, instanceId);
            return Result.Ok(avanzada);
        }
    }

    public Result<Page<WorkflowInstance>> ListForSubject(Ref? subject, int offset, int limit)
    {
        if (subject is null)
        {
            return Rejection.Invalid($"{WorkflowRules.CodePrefix}.subject_required", "Hace falta filtrar por sujeto.");
        }

        var todas = _instances.ForSubject(subject)
            .OrderByDescending(i => i.StartedAtUtc)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<WorkflowInstance>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset));
    }
}
