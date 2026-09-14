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

    /// <summary>
    /// El cobro que lleva esa referencia del proveedor, o <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Es lo único con lo que se puede atender un webhook: el proveedor no conoce nuestro
    /// identificador —se lo inventamos nosotros después de firmar— y lo que trae de vuelta es la
    /// referencia que le mandamos.
    /// </remarks>
    Payment? FindByProviderReference(string providerReference);

    IReadOnlyList<Payment> ForSubject(Ref subject);
    void Put(Payment payment);
}

public sealed class FileSystemPaymentStore : IPaymentStore
{
    private readonly JsonCollectionStore<Payment> _store;

    public FileSystemPaymentStore(IOptions<PaymentStorageOptions> options)
        => _store = new JsonCollectionStore<Payment>(options.Value.Root, "payments", p => p.Id);

    public Payment? Find(string id) => _store.Find(id);

    public Payment? FindByProviderReference(string providerReference)
        => _store.Where(p => string.Equals(p.ProviderReference, providerReference, StringComparison.Ordinal))
            .OrderBy(p => p.AuthorizedAtUtc)
            .FirstOrDefault();

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

    /// <inheritdoc />
    /// <remarks>Dice que NO, y por eso el gate lo puede echar de producción.</remarks>
    public bool MuevePlata => false;

    public Task<PaymentAttempt> AuthorizeAsync(Money amount, Ref payer, CancellationToken ct = default)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'autorizó' {Amount} de {Payer} sin mover nada.", amount, payer);
        return Task.FromResult(PaymentAttempt.Ok($"stub-{Guid.NewGuid():n}"));
    }

    public Task<PaymentAttempt> CaptureAsync(string providerReference, Money amount, CancellationToken ct = default)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'capturó' {Amount} ({Ref}) sin mover plata.", amount, providerReference);
        return Task.FromResult(PaymentAttempt.Ok(providerReference));
    }

    public Task<PaymentAttempt> RefundAsync(string providerReference, Money amount, CancellationToken ct = default)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'devolvió' {Amount} ({Ref}) sin mover plata.", amount, providerReference);
        return Task.FromResult(PaymentAttempt.Ok(providerReference));
    }

    public Task<PaymentAttempt> VoidAsync(string providerReference, CancellationToken ct = default)
    {
        _log.LogWarning("SIN PASARELA REAL: se 'liberó' la autorización {Ref}.", providerReference);
        return Task.FromResult(PaymentAttempt.Ok(providerReference));
    }
}

/// <summary>
/// El proveedor que se elige cuando hay UNO configurado por nombre y le falta la credencial.
/// </summary>
/// <remarks>
/// <para><b>Rechaza cada cobro y lo grita</b>, exactamente como ADR 0131 hizo con el correo. Es la
/// diferencia entre un despliegue a medias que se ve a medias y uno que aparenta funcionar — y
/// aparentar es lo caro: significa pedidos «pagados» que nadie cobró, descubiertos cuando alguien
/// cuadre la caja.</para>
///
/// <para>No es lo mismo que <see cref="LoggingPaymentProvider"/>: aquél es el default de
/// desarrollo y dice que sí a propósito; éste aparece cuando alguien PIDIÓ cobrar de verdad y no
/// dejó con qué.</para>
/// </remarks>
public sealed class NotConfiguredPaymentProvider : IPaymentProvider
{
    private readonly string _pedido;
    private readonly string _queFalta;
    private readonly ILogger<NotConfiguredPaymentProvider> _log;

    public NotConfiguredPaymentProvider(string pedido, string queFalta, ILogger<NotConfiguredPaymentProvider> log)
    {
        _pedido = pedido;
        _queFalta = queFalta;
        _log = log;
    }

    public string Name => $"{_pedido}-sin-configurar";

    public bool MuevePlata => false;

    private PaymentAttempt Gritar(string que)
    {
        _log.LogError(
            "NO SE PUEDE COBRAR: se pidió el proveedor '{Pedido}' y falta {Falta}. Se rechaza {Que}.",
            _pedido, _queFalta, que);
        return PaymentAttempt.NotConfigured($"El medio de pago no está configurado: falta {_queFalta}.");
    }

    public Task<PaymentAttempt> AuthorizeAsync(Money amount, Ref payer, CancellationToken ct = default)
        => Task.FromResult(Gritar("autorizar"));

    public Task<PaymentAttempt> CaptureAsync(string providerReference, Money amount, CancellationToken ct = default)
        => Task.FromResult(Gritar("capturar"));

    public Task<PaymentAttempt> RefundAsync(string providerReference, Money amount, CancellationToken ct = default)
        => Task.FromResult(Gritar("devolver"));

    public Task<PaymentAttempt> VoidAsync(string providerReference, CancellationToken ct = default)
        => Task.FromResult(Gritar("liberar"));
}
