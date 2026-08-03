using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Notifications.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class NotificationStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "notifications");
}

public interface ITemplateStore
{
    Template? Find(string id);
    Template? FindByKey(string key);
    IReadOnlyList<Template> All();
    void Put(Template item);
}

public interface IDeliveryStore
{
    Delivery? Find(string id);
    IReadOnlyList<Delivery> ForRecipient(Ref recipient);
    void Put(Delivery delivery);
}

public sealed class FileSystemTemplateStore : ITemplateStore
{
    private readonly JsonCollectionStore<Template> _store;

    public FileSystemTemplateStore(IOptions<NotificationStorageOptions> options)
        => _store = new JsonCollectionStore<Template>(options.Value.Root, "templates", t => t.Id);

    public Template? Find(string id) => _store.Find(id);

    public Template? FindByKey(string key)
    {
        var hit = _store.Where(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
        return hit.Count > 0 ? hit[0] : null;
    }

    public IReadOnlyList<Template> All() => _store.All();
    public void Put(Template item) => _store.Put(item);
}

public sealed class FileSystemDeliveryStore : IDeliveryStore
{
    private readonly JsonCollectionStore<Delivery> _store;

    public FileSystemDeliveryStore(IOptions<NotificationStorageOptions> options)
        => _store = new JsonCollectionStore<Delivery>(options.Value.Root, "deliveries", d => d.Id);

    public Delivery? Find(string id) => _store.Find(id);
    public IReadOnlyList<Delivery> ForRecipient(Ref recipient) => _store.Where(d => d.To == recipient);
    public void Put(Delivery delivery) => _store.Put(delivery);
}

/// <summary>
/// El transporte por defecto: registra y da por entregado.
/// </summary>
/// <remarks>
/// <para>Existe para que esta API arranque y se pruebe <b>sin ningún proveedor</b>. Es el mismo
/// patrón del <c>StubBundleRegistryClient</c> del CMS (ADR 0012): la costura existe para ser
/// reemplazada, y hasta que haya un proveedor de verdad, degradar visiblemente es mejor que no
/// arrancar.</para>
///
/// <para><b>Avisa a gritos en cada envío</b>, y es a propósito: un transporte silencioso que dice
/// "entregado" sin entregar es la forma más cara de descubrir en producción que nadie configuró
/// el correo.</para>
/// </remarks>
public sealed class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _log;

    public LoggingNotificationSender(ILogger<LoggingNotificationSender> log) => _log = log;

    public bool Send(Channel channel, string address, string subject, string body)
    {
        _log.LogWarning(
            "SIN TRANSPORTE REAL: el aviso {Channel} para {Address} ('{Subject}') se registró pero NO se envió.",
            channel, address, subject);
        return true;
    }
}
