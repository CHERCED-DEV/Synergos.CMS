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

    /// <summary>El envío que corresponde a un id de mensaje <b>del proveedor</b>.</summary>
    /// <remarks>
    /// Es la consulta de la que depende saber si un correo llegó: el webhook cita el id del
    /// proveedor, no el nuestro. Sin esta búsqueda, el evento no tiene contra qué apuntarse.
    /// </remarks>
    Delivery? FindByProviderMessageId(string providerMessageId);

    IReadOnlyList<Delivery> ForRecipient(Ref recipient);

    /// <summary>Los que están en un estado concreto. <b>La consulta del barrido</b> (HU #29).</summary>
    /// <remarks>
    /// Sin destinatario a propósito: quien pregunta no es una persona mirando lo suyo, es el
    /// barrido, y lo que necesita es todo lo que está colgado sin importar de quién.
    /// </remarks>
    IReadOnlyList<Delivery> WithStatus(DeliveryStatus status);

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

    public Delivery? FindByProviderMessageId(string providerMessageId)
    {
        var hit = _store.Where(d => string.Equals(d.ProviderMessageId, providerMessageId, StringComparison.Ordinal));
        return hit.Count > 0 ? hit[0] : null;
    }

    public IReadOnlyList<Delivery> ForRecipient(Ref recipient) => _store.Where(d => d.To == recipient);

    public IReadOnlyList<Delivery> WithStatus(DeliveryStatus status)
        => _store.Where(d => d.Status == status);
    public void Put(Delivery delivery) => _store.Put(delivery);
}

/// <summary>
/// El transporte por defecto: registra a gritos y <b>no da nada por entregado</b>.
/// </summary>
/// <remarks>
/// <para>Existe para que esta API arranque y se pruebe <b>sin ningún proveedor</b>. Es el mismo
/// patrón del <c>StubBundleRegistryClient</c> del CMS (ADR 0012): la costura existe para ser
/// reemplazada, y hasta que haya un proveedor de verdad, degradar visiblemente es mejor que no
/// arrancar.</para>
///
/// <para><b>Antes devolvía <c>true</c>, y eso era el defecto entero.</b> Un despliegue sin
/// credenciales dejaba un rastro lleno de envíos «entregados» que nadie recibió — la forma más
/// cara de descubrir en producción que nadie configuró el correo, porque el sistema afirmaba lo
/// contrario. Ahora rechaza con <c>transport_not_configured</c>: el aviso no sale, y <b>se
/// nota</b>.</para>
/// </remarks>
public sealed class LoggingNotificationSender : INotificationSender
{
    private readonly ILogger<LoggingNotificationSender> _log;

    public LoggingNotificationSender(ILogger<LoggingNotificationSender> log) => _log = log;

    /// <summary>Dice que sí a todo para que el rechazo sea <c>not_configured</c> y no «canal desconocido».</summary>
    /// <remarks>
    /// Son dos problemas distintos y conviene no confundirlos: «este despliegue no habla SMS» es
    /// una decisión; «falta la credencial de correo» es un olvido. Quien lea el rechazo tiene que
    /// poder distinguirlos sin abrir el código.
    /// </remarks>
    public bool Supports(Channel channel) => true;

    public Task<Result<string>> SendAsync(
        Channel channel, string address, string subject, string body,
        string idempotencyKey, CancellationToken ct = default)
    {
        _log.LogError(
            "SIN TRANSPORTE REAL: el aviso {Channel} para {Address} ('{Subject}') NO se envió. Falta configurar un proveedor.",
            channel, address, subject);

        return Task.FromResult(Result.Rejected<string>(NotificationRules.TransportNotConfigured(channel)));
    }
}
