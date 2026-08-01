using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Compone N proveedores de pago y decide cuál cobra cada petición.
/// <b>Implementa <see cref="IPaymentProvider"/>, que es el punto</b>: los ocho
/// consumidores no se enteran de que existe.
/// </summary>
/// <remarks>
/// <para>Es la respuesta al encargo de que el motor se pueda "reinstanciar,
/// reutilizar o polimorfizar" (ADR 0116), y calca el patrón que la casa ya
/// validó con <c>IOrderTrackingService</c>, que se instancia dos veces con
/// pipelines distintos: <b>misma seam, N instancias configuradas</b>. No hay
/// jerarquía nueva ni una segunda interfaz de orquestación — eso habría dejado
/// dos conceptos donde hay uno (ADR 0107).</para>
///
/// <para><b>Cómo vuelve una sesión a su proveedor.</b> El id que devuelve
/// <see cref="CreateSessionAsync"/> viene prefijado
/// (<c>"wompi|ch_123"</c>) para que Capture/Status/Refund/Void enruten sin
/// almacenamiento aparte. El separador es <c>|</c> y no <c>:</c> a propósito:
/// los ids de PSP llevan dos puntos con más frecuencia de la que uno espera, y
/// un separador ambiguo convierte un enrutamiento en un bug silencioso.</para>
///
/// <para><b>Con un solo proveedor no cambia nada.</b> Si hay uno registrado, el
/// router sigue prefijando —para que el formato del id no dependa de cuántos
/// haya— pero la decisión es trivial. Y si un id llega SIN prefijo (creado
/// antes de este cambio, o por un camino que no pasó por acá), cae al
/// proveedor por defecto en vez de fallar: una sesión viva no se rompe porque
/// cambiemos de plomería.</para>
/// </remarks>
public sealed class RoutingPaymentProvider : IPaymentProvider
{
    /// <summary>Separa el proveedor del id nativo dentro del sessionId.</summary>
    public const char Separator = '|';

    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly IReadOnlyList<PaymentRoutingRule> _rules;
    private readonly IPaymentProvider _default;

    /// <param name="providers">Proveedores disponibles. Al menos uno.</param>
    /// <param name="rules">Reglas en orden de prioridad; la primera que encaja
    ///   gana. Null o vacío = todo al default.</param>
    /// <param name="defaultProviderKey">A quién cobrar cuando ninguna regla
    ///   encaja. Si no se da, o no existe, se usa el primero registrado.</param>
    public RoutingPaymentProvider(
        IEnumerable<IPaymentProvider> providers,
        IEnumerable<PaymentRoutingRule>? rules = null,
        string? defaultProviderKey = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var map = new Dictionary<string, IPaymentProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            if (p is null || string.IsNullOrWhiteSpace(p.ProviderKey)) continue;
            // El primero gana: un duplicado de configuración no debe cambiar en
            // silencio quién cobra.
            map.TryAdd(p.ProviderKey, p);
        }
        if (map.Count == 0)
        {
            throw new ArgumentException(
                "El router necesita al menos un proveedor de pago.", nameof(providers));
        }

        _providers = map;
        _default = defaultProviderKey is not null && map.TryGetValue(defaultProviderKey, out var d)
            ? d
            : map.Values.First();

        // Las reglas que apuntan a un proveedor inexistente se descartan al
        // construir, no al cobrar: un typo en appsettings no puede convertirse
        // en un pago perdido a mitad de un checkout.
        _rules = (rules ?? Array.Empty<PaymentRoutingRule>())
            .Where(r => r is not null && map.ContainsKey(r.ProviderKey))
            .ToList();
    }

    /// <summary>
    /// Identidad del router, no la de sus miembros. Quien necesita saber quién
    /// cobró de verdad lo lee en <see cref="PaymentSession.ProviderKey"/>.
    /// </summary>
    public string ProviderKey => "router";

    /// <summary>Los proveedores registrados, por clave. Para diagnóstico.</summary>
    public IReadOnlyCollection<string> RegisteredProviders => (IReadOnlyCollection<string>)_providers.Keys;

    /// <summary>A quién le tocaría esta petición. Público para poder testear la decisión sin cobrar.</summary>
    public IPaymentProvider Resolve(PaymentSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var rule in _rules)
        {
            if (rule.Matches(request) && _providers.TryGetValue(rule.ProviderKey, out var p))
            {
                return p;
            }
        }
        return _default;
    }

    public async Task<PaymentSession> CreateSessionAsync(
        PaymentSessionRequest request, CancellationToken cancellationToken = default)
    {
        var provider = Resolve(request);
        var session = await provider.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);

        return session with
        {
            SessionId = Qualify(provider.ProviderKey, session.SessionId),
            // Quién cobró de verdad, no el router. Sin esto, un operador mirando
            // una sesión no podría saber a qué PSP reclamarle.
            ProviderKey = provider.ProviderKey,
        };
    }

    public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
        => Dispatch(sessionId, (p, id) => p.GetStatusAsync(id, cancellationToken), sessionId);

    public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
        => Dispatch(sessionId, (p, id) => p.CaptureAsync(id, amount, cancellationToken), sessionId);

    public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default)
        => Dispatch(sessionId, (p, id) => p.VoidAsync(id, cancellationToken), sessionId);

    public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
        => Dispatch(sessionId, (p, id) => p.RefundAsync(id, amount, cancellationToken), sessionId);

    /// <summary>
    /// Parte el id, llama al proveedor dueño con SU id nativo, y vuelve a
    /// calificar el id del resultado para que el llamante siempre vea el mismo
    /// formato que recibió.
    /// </summary>
    private async Task<PaymentOutcome> Dispatch(
        string sessionId,
        Func<IPaymentProvider, string, Task<PaymentOutcome>> call,
        string originalId)
    {
        var (provider, nativeId) = Split(sessionId);
        var outcome = await call(provider, nativeId).ConfigureAwait(false);
        return outcome with { SessionId = originalId };
    }

    /// <summary>Prefija el id nativo con su proveedor.</summary>
    internal static string Qualify(string providerKey, string nativeSessionId)
        => $"{providerKey}{Separator}{nativeSessionId}";

    /// <summary>
    /// Resuelve a qué proveedor pertenece un id. Un id sin prefijo, o con un
    /// prefijo desconocido, cae al default: preferimos intentar cobrar en el
    /// proveedor equivocado —que responderá "no existe"— antes que reventar
    /// sobre una sesión legítima creada antes de este cambio.
    /// </summary>
    private (IPaymentProvider provider, string nativeId) Split(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return (_default, string.Empty);
        }
        var idx = sessionId.IndexOf(Separator);
        if (idx <= 0 || idx == sessionId.Length - 1)
        {
            return (_default, sessionId);
        }
        var key = sessionId[..idx];
        var native = sessionId[(idx + 1)..];
        return _providers.TryGetValue(key, out var p)
            ? (p, native)
            : (_default, sessionId);
    }
}
