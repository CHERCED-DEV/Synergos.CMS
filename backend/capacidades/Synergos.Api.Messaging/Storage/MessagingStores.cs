using Microsoft.Extensions.Options;
using Synergos.Api.Messaging.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Messaging.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class MessagingStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "messaging");
}

public interface IThreadStore
{
    MessageThread? Find(string id);
    IReadOnlyList<MessageThread> ForParticipant(Ref who);
    void Put(MessageThread thread);
}

public interface IMessageStore
{
    Message? Find(string id);
    IReadOnlyList<Message> InThread(string threadId);
    void Put(Message message);
}

public sealed class FileSystemThreadStore : IThreadStore
{
    private readonly JsonCollectionStore<MessageThread> _store;

    public FileSystemThreadStore(IOptions<MessagingStorageOptions> options)
        => _store = new JsonCollectionStore<MessageThread>(options.Value.Root, "threads", t => t.Id);

    public MessageThread? Find(string id) => _store.Find(id);
    public IReadOnlyList<MessageThread> ForParticipant(Ref who) => _store.Where(t => t.Includes(who));
    public void Put(MessageThread thread) => _store.Put(thread);
}

public sealed class FileSystemMessageStore : IMessageStore
{
    private readonly JsonCollectionStore<Message> _store;

    public FileSystemMessageStore(IOptions<MessagingStorageOptions> options)
        => _store = new JsonCollectionStore<Message>(options.Value.Root, "messages", m => m.Id);

    public Message? Find(string id) => _store.Find(id);

    public IReadOnlyList<Message> InThread(string threadId)
        => _store.Where(m => string.Equals(m.ThreadId, threadId, StringComparison.Ordinal))
            .OrderBy(m => m.AtUtc)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();

    public void Put(Message message) => _store.Put(message);
}
