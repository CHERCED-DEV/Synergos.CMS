using Microsoft.Extensions.Options;
using Synergos.Api.Workflow.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Workflow.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class WorkflowStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "workflow");
}

public interface IDefinitionStore
{
    WorkflowDefinition? Find(string id);
    WorkflowDefinition? FindByKey(string key);
    void Put(WorkflowDefinition definition);
}

public interface IInstanceStore
{
    WorkflowInstance? Find(string id);
    IReadOnlyList<WorkflowInstance> ForSubject(Ref subject);
    void Put(WorkflowInstance instance);
}

public sealed class FileSystemDefinitionStore : IDefinitionStore
{
    private readonly JsonCollectionStore<WorkflowDefinition> _store;

    public FileSystemDefinitionStore(IOptions<WorkflowStorageOptions> options)
        => _store = new JsonCollectionStore<WorkflowDefinition>(options.Value.Root, "definitions", d => d.Id);

    public WorkflowDefinition? Find(string id) => _store.Find(id);

    public WorkflowDefinition? FindByKey(string key)
    {
        var hit = _store.Where(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        return hit.Count > 0 ? hit[0] : null;
    }

    public void Put(WorkflowDefinition definition) => _store.Put(definition);
}

public sealed class FileSystemInstanceStore : IInstanceStore
{
    private readonly JsonCollectionStore<WorkflowInstance> _store;

    public FileSystemInstanceStore(IOptions<WorkflowStorageOptions> options)
        => _store = new JsonCollectionStore<WorkflowInstance>(options.Value.Root, "instances", i => i.Id);

    public WorkflowInstance? Find(string id) => _store.Find(id);
    public IReadOnlyList<WorkflowInstance> ForSubject(Ref subject) => _store.Where(i => i.Subject == subject);
    public void Put(WorkflowInstance instance) => _store.Put(instance);
}
