using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Payments.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class PaymentStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "payments");
}

public interface IPaymentStore
{
    Payment? Find(string id);
    IReadOnlyList<Payment> ForSubject(Ref subject);
    void Put(Payment payment);
}

public sealed class FileSystemPaymentStore : IPaymentStore
{
    private readonly JsonCollectionStore<Payment> _store;

    public FileSystemPaymentStore(IOptions<PaymentStorageOptions> options)
        => _store = new JsonCollectionStore<Payment>(options.Value.Root, "payments", p => p.Id);

    public Payment? Find(string id) => _store.Find(id);
    public IReadOnlyList<Payment> ForSubject(Ref subject) => _store.Where(p => p.For == subject);
    public void Put(Payment payment) => _store.Put(payment);
}

/// <summary>
/// El medio de pago por defecto: registra, autoriza todo y avisa a gritos.
/// </summary>
/// <remarks>
/// <para>Existe para que esta API arranque y se pruebe <b>sin ninguna pasarela</b>. Es el mismo
/// patrón del <c>StubBundleRegistryClient</c> del CMS (ADR 0012) y del transporte de
/// <c>Api.Notifications</c>: la costura existe para ser reemplazada.</para>
///
/// <para><b>Avisa en CADA operación, en nivel warning.</b> Un proveedor silencioso que dice
/// "capturado" sin mover plata es, de todos los stubs del sistema, el que más caro se descubre:
/// el CMS ya tuvo exactamente ese defecto —<c>Provider=Wompi</c> servía el stub en silencio— y
/// costó una investigación. Acá no se repite.</para>
/// </remarks>
public sealed class LoggingPaymentProvider : IPaymentProvider
{
    private readonly ILogger<LoggingPaymentProvider> _log;

    public LoggingPaymentProvider(ILogger<LoggingPaymentProvider> log) => _log = log;

    public string Name => "logging-stub";

    public string? Authorize(Money amount, Ref payer)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'autorizó' {Amount} de {Payer} sin mover nada.", amount, payer);
        return $"stub-{Guid.NewGuid():n}";
    }

    public bool Capture(string providerReference, Money amount)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'capturó' {Amount} ({Ref}) sin mover plata.", amount, providerReference);
        return true;
    }

    public bool Refund(string providerReference, Money amount)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'devolvió' {Amount} ({Ref}) sin mover plata.", amount, providerReference);
        return true;
    }

    public bool Void(string providerReference)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'liberó' la autorización {Ref}.", providerReference);
        return true;
    }
}
