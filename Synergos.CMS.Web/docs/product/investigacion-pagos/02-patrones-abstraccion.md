# Patrones de abstracción de pagos — qué hacen los maduros

> Investigación para el motor de pagos multi-vertical de Synergos (9 verticales,
> hoy consumidas por un único `IPaymentProvider` singleton). Objetivo del
> arquitecto: "que se pueda reinstanciar o reutilizar o polimorfizar".

## Comparativa de modelos

| Plataforma | Abstracción central | Multi-proveedor | Transacción de 1ª clase | Veredicto |
|---|---|---|---|---|
| **Medusa.js (v2)** | `Payment Module` con 3 conceptos: Payment Processor (provider), Payment Session, Payment. Cada gateway es un "Payment Provider" plugin registrado en el módulo. | Sí — N providers registrados, uno por región/proveedor vía `payment_providers` en config de región. | Parcial — `Payment` es la entidad post-captura, `PaymentSession` es la pre-captura. No hay ledger de eventos explícito. | Buen ejemplo de *seam + registro N-a-1*, pero tuvo un rediseño doloroso v1→v2 (rompió toda la interfaz de provider). |
| **Saleor** | Migró de "Payment plugin" (mutations directas) a **Transactions API**: `transactionInitialize` / `transactionProcess` / eventos de webhook (`PAYMENT_GATEWAY_INITIALIZE_SESSION`, `TRANSACTION_*`). | Sí — "Payment Apps", cada una un adaptador instalable, seleccionable por checkout. | **Sí, explícita** — `TransactionItem` es una entidad con `events[]` (histórico de auth/charge/refund/cancel), no un simple estado mutable. | El caso de estudio más fuerte: pasaron de sesión-mutable a transacción-con-historial *porque* la sesión-mutable no soportaba pagos parciales, multi-captura ni reconciliación. |
| **Sylius** | `PaymentMethod` (config) + `Payment` (registro) + **Payum** (librería PHP externa) que abstrae captura/refund/recurring por "gateway factory". | Sí — cualquier gateway soportado por Payum, uno por `PaymentMethod`, elegible en checkout. | No — Payum modela "requests" (Capture, Refund, Status) contra un gateway, sin un ledger de eventos de dominio propio. | Separación de responsabilidades limpia (Sylius = checkout/dominio, Payum = protocolo PSP), pero acopla todo el ecosistema a una librería PHP externa — no portable a .NET tal cual, sí el *principio*. |
| **Spree Commerce** | `PaymentMethod` (wrapper de `active_merchant`) + `Payment` (registro contra `Order`) + **Payment Sessions API** (unifica Stripe/Adyen/PayPal/custom detrás de un único endpoint). | Sí — el backend decide qué PSP corresponde a esa tienda/moneda/método; el frontend es agnóstico. | Parcial — `Payment` trackea `source` + `payment_method`, sin ledger de eventos rico. | El más parecido en espíritu a lo que Synergos ya tiene (`PaymentSession` agnóstica + `ProviderKey`). Confirma que el diseño actual de Synergos no está desalineado. |
| **Shopware** | Sistema de "Payment Handlers" plugin-based, uno por método de pago, con `PaymentException` tipada y `struct` de contexto por transacción. | Sí, vía plugins instalables por canal de venta. | Sí desde 6.4+ — introdujeron una `Payment Handler` orientada a **estados de transacción** explícitos tras detectar que el modelo síncrono anterior no soportaba PSPs asíncronos. | No se profundizó por límite de tiempo; el patrón (handler + estados explícitos) coincide con Saleor/Medusa. |

## Medusa / Saleor / Sylius / Spree en detalle

**Medusa** (documentación oficial: [Payment Module](https://docs.medusajs.com/resources/commerce-modules/payment), [Payment Architecture v1](https://docs.medusajs.com/v1/modules/carts-and-checkout/payment)) declara el módulo como "a standalone package that provides features for a single domain" — aislado y resuelto vía DI del framework (`container.resolve(Modules.PAYMENT)`). El módulo expone `createPaymentCollections`, autoriza/captura/reembolsa, y soporta *saved payment methods* vía "Account Holders" (una entidad cliente creada en el PSP, ej. un customer de Stripe, para no tener que re-tokenizar tarjeta cada vez). El salto v1→v2 fue disruptivo: "the payment provider interface, originally designed for Medusa V1, has been redesigned for V2 — a long overdue update" y el propio equipo "apologized in advance for the inconvenience" ([release notes / discusión GitHub](https://github.com/medusajs/medusa/discussions/7955)). Lección: diseñar la interfaz de provider pensando en 2-3 años, porque romperla después cuesta caro y en cascada (N adapters a reescribir).

**Saleor** es el caso más instructivo porque documentó *por qué* migró. El flujo viejo (`checkoutPaymentCreate` contra un plugin) fue reemplazado por `paymentGatewayInitialize` + `transactionInitialize` + `transactionProcess`, todo dirigido a "Payment Apps" en vez de plugins in-process ([docs.saleor.io/developer/payments/payment-apps](https://docs.saleor.io/developer/payments/payment-apps), [issue #11258](https://github.com/saleor/saleor/issues/11258), [mutation reference](https://docs.saleor.io/api-reference/payments/mutations/payment-gateway-initialize)). El punto de diseño clave: "Instead of multiple calling `checkoutComplete`... the `transactionProcess` should be called" — es decir, dejaron de modelar el pago como una llamada RPC única y lo modelaron como una **máquina de estados con eventos**, porque un pago real casi nunca es una sola llamada (redirect, 3DS, webhook asíncrono, reintento).

**Sylius** ([blog oficial](https://sylius.com/blog/new-docs-adding-a-payment-gateway-in-sylius/), [docs](https://docs.sylius.com/the-book/carts-and-orders/payments)) delega el protocolo PSP a Payum y se queda solo con el dominio (`PaymentMethod`, `Payment`). Es la separación más limpia de las cuatro, aunque atada a una librería externa.

**Spree** ([spreecommerce.org/docs](https://spreecommerce.org/docs/developer/core-concepts/payments)) es el más cercano a Synergos hoy: "One unified interface handles Stripe, Adyen, PayPal, and any custom gateway through a single set of endpoints, with the backend coordinating with whichever payment service provider is configured for that store, currency, or payment method. The frontend code does not know or care which payment service provider is behind the session."

## Sesión vs Transacción — el cambio de modelo y por qué

Una **sesión** (lo que Synergos tiene hoy: `PaymentSession { SessionId, Status, RedirectUrl?, ClientSecret? }`) modela "el pago" como un objeto con un estado mutable actual. Funciona perfecto para el caso feliz: crear → (redirect) → capturar. Se queda corta en tres escenarios reales:

1. **Captura parcial / múltiple contra una autorización** — ej. un hotel que autoriza el total pero captura noche a noche, o un e-commerce que envía en 2 paquetes y cobra cada uno. Un estado único (`Captured`) no puede representar "$40 de $100 capturados, resto sigue autorizado".
2. **Reconciliación** — cuando el PSP y Synergos se desincronizan (timeout de red, webhook perdido), sin un log de eventos no hay forma barata de saber *qué pasó* vs. *qué creemos que pasó*. Saleor resolvió esto con `TransactionItem.events[]`: cada intento de autorizar/capturar/reembolsar queda como un evento inmutable, y el estado "actual" se deriva, no se sobreescribe.
3. **Multi-PSP** — si dos PSPs distintos pueden procesar la misma orden en momentos distintos (fallback), la sesión-mutable no tiene dónde guardar "quién hizo qué".

**Stripe PaymentIntents** resuelve lo mismo del lado del PSP: un `PaymentIntent` es explícitamente un objeto con historial de estados (`requires_payment_method → requires_confirmation → requires_action → processing → succeeded`), no una llamada única ([docs.stripe.com/payments/payment-intents](https://docs.stripe.com/payments/payment-intents)).

**Para Synergos:** el ADR 0104 ya resolvió la mitad del problema (durabilidad de la sesión vía `IJsonEntityStore` + `IPaymentEventStore` como ledger de idempotencia del webhook — ver `Synergos.CMS.Web/docs/adr/0104-durable-payments-reservations-and-inbound-webhook-tienda-t3-ola-a.md`). Lo que falta es que **la sesión persistida sea append-only para las mutaciones críticas** (cada `CaptureAsync`/`RefundAsync` agrega un evento con timestamp + monto + resultado) en vez de sobreescribir el `PersistedSession` completo como hace `StubPaymentProvider` hoy. Esto no exige tocar la interfaz pública — es un cambio interno del provider — pero si se introduce una interfaz nueva de "transacción" debe ser *aditiva* y no reemplazar `PaymentSession`.

## Enrutamiento multi-proveedor (por moneda/país/método/vertical)

Los research de industria orquestación de pagos (no plataformas open-source de commerce, sino la capa que se pone *encima* — Primer, Lago, orquestadores comerciales) describen el patrón como una capa de enrutamiento *delante* de N PSPs:

> "A routing layer (often called a payment orchestration layer) sits in front of multiple PSPs and decides — per transaction — where to send it based on BIN country, card brand, amount, currency, historical approval rates, and current provider health." — [Lago blog](https://getlago.com/blog/payment-orchestration-multi-psp-routing-and-failover)

Tres estrategias documentadas ([Lago](https://getlago.com/blog/payment-orchestration-multi-psp-routing-and-failover), [Primer](https://primer.io/blog/what-payment-platforms-support-multi-acquirer-smart-routing)):
- **Cascading** — reintenta automáticamente en un PSP backup si el primero declina.
- **Geo/BIN routing** — el acquirer con mejor tasa de aprobación para el país emisor de la tarjeta.
- **Rule-based routing** — reglas explícitas por geografía/moneda/tipo de transacción (esto es lo directamente aplicable a Synergos: Wompi/PayU para Colombia, Stripe para internacional, sin necesidad de scoring dinámico de aprobación).

Para Synergos, con verticales fijas y mercado inicial CO, la forma correcta es **rule-based routing simple** (vertical/país/moneda → provider key, con un fallback), NO un motor de scoring — eso sería sobre-ingeniería para el estado actual (un solo PSP real: Wompi, aún no encendido).

## Autorización diferida vs captura — con los 3 casos de Synergos

Regla general de la industria: "Delayed capture protects both parties: merchants avoid charging for unfulfilled orders, and cardholders can have holds released without a formal refund, reducing chargeback risk" ([Inai](https://inai.io/blog/authorization-vs.-capture-in-payments), [Engine](https://engine.com/business-travel-guide/hotel-credit-card-authorization)). El hold típico dura 7-30 días según marca de tarjeta antes de expirar solo.

| Caso Synergos | Patrón correcto | Por qué |
|---|---|---|
| **Evento con aforo** (Ola de Eventos, `StubEventTicketingService`) | Autorizar al reservar el asiento (`IReservationService.HoldItemAsync`), **capturar solo si el hold se confirma** dentro de la ventana. Si el hold expira sin confirmar → `VoidAsync`, no `RefundAsync` (nunca hubo cobro real). | Evita cobrar por un asiento que el comprador no terminó de reservar (timeout de sesión, carrito abandonado). |
| **Reserva de hotel con hold** (Booking) | Igual patrón: autorizar al crear la reserva, capturar al confirmar (o parcialmente noche a noche si el vertical lo requiere a futuro). El hold de tarjeta (7-30 días) debe ser ≥ la ventana de negocio del hold de inventario — si el hold de habitación dura más que el hold de tarjeta, hay que re-autorizar. | Hoy `IPaymentProvider.CaptureAsync` no soporta monto parcial — bloquea captura-por-noche a futuro. |
| **Matrícula** (`StubEnrollmentService`) | Si el curso es de pago, captura inmediata es aceptable (no hay "aforo" que se libere solo, el cupo del curso es más laxo). Diferir solo si se introduce un período de prueba/cooling-off. | Menor urgencia que los otros dos — no hay inventario físico perecedero de corto plazo. |

**Gap concreto en la interfaz actual:** `CaptureAsync(string sessionId, CancellationToken)` no acepta `amount`, y no existe `VoidAsync` distinto de `RefundAsync`. Ambos se necesitan para modelar bien Eventos y Booking (ver propuesta de firma más abajo).

## Split payments y suscripciones — qué exigen de la abstracción

**Split payments (marketplace).** Stripe Connect es la referencia de facto ([docs.stripe.com/connect](https://docs.stripe.com/connect), [separate charges and transfers](https://docs.stripe.com/connect/marketplace/tasks/accept-payment/separate-charges-and-transfers)). Dos modelos:
- **Destination charges** — la plataforma es el *merchant of record*, Stripe reparte una porción al vendedor conectado. Simple, bueno para 1 vendedor por orden.
- **Separate charges and transfers** — la plataforma cobra el total y hace transferencias explícitas después, necesario cuando una orden reparte entre *múltiples* vendedores o la lógica de reparto es compleja.

Lo que esto exige de la abstracción: (a) una noción de "cuenta conectada" o `PayeeId` por línea/split, (b) que `Capture`/`Refund` puedan operar por línea o por split, no solo por sesión completa. **Synergos no tiene hoy ningún vertical multi-vendedor real** — es fase especulativa. La forma correcta de prepararse sin sobre-construir: el `Metadata: IReadOnlyDictionary<string,string>` que ya existe en `PaymentSessionRequest` es el punto de extensión (se le puede meter `payeeId`/`splitPlan` cuando exista un vendedor real), sin tocar la interfaz. Construir `ISplitPaymentProvider` ahora violaría la prohibición explícita del repo de abstracciones sin 2+ implementaciones reales (CLAUDE.md §6).

**Suscripciones/recurrencia.** Stripe separa esto explícitamente de PaymentIntents: "Stripe offers several ways to accept recurring payments: Subscriptions with Stripe Billing, PaymentIntents, SetupIntents, or Invoicing" ([docs.stripe.com/recurring-payments](https://docs.stripe.com/recurring-payments)). La pieza clave que un `PaymentIntent`/sesión de pago único NO cubre es el **SetupIntent** (guardar un método de pago sin cobrar todavía, para cobros futuros no supervisados). Ningún vertical de Synergos hoy es recurrente (matrícula es pago único por curso, tasas de gobierno son por trámite). Si en el futuro aparece (ej. suscripción a un plan de gimnasio en Salud, cuota mensual educativa), la extensión natural es una interfaz **nueva y separada** (`IRecurringPaymentProvider` o similar) que reutiliza `IPaymentProvider` para el cobro puntual pero agrega `SaveMethodAsync`/`ChargeStoredMethodAsync` — no forzar el caso recurrente dentro de `CreateSessionAsync`.

## Idempotencia y orden de eventos

Consenso de la industria (Stripe, y los post-mortems de idempotencia revisados):
- **Idempotency keys en toda escritura** — "Stripe recommends adding an idempotency key to all POST requests... typically based on the ID associated with the cart or customer session" ([docs.stripe.com/plan-integration](https://docs.stripe.com/plan-integration/get-started/server-side-integration?locale=en-GB)).
- **Webhooks duplicados son la norma, no la excepción** — "Stripe may send the same webhook more than once due to network issues or retries... verify the webhook signature, store the raw webhook event, and check whether the event was already received" ([APIScout guide](https://apiscout.dev/guides/stripe-webhooks-complete-guide-2026)).
- **El webhook es la fuente de verdad, no el redirect del navegador** — "The source of truth for payment completion is the webhook, not the redirect."
- **"Exactly-once" es una trampa** — de los post-mortems: "Exactly-once processing is one of the most misleading ideas in distributed systems... the reality is that exactly-once is often simulated through at-least-once delivery combined with idempotent handling" ([engineeringenablement.substack.com](https://engineeringenablement.substack.com/p/what-i-wish-i-knew-before-i-designed)).

**Synergos ya implementa la parte difícil correctamente** (ADR 0104): `PaymentWebhookController` lee el body RAW antes de deserializar (el HMAC es sobre bytes exactos), verifica firma con ventana ±5 min y `FixedTimeEquals`, re-consulta `GetStatusAsync` en vez de confiar el payload del webhook ("anti-tampering"), y usa `IPaymentEventStore` con `FileMode.CreateNew` (create-exclusivo atómico, "anti-TOCTOU") como ledger de idempotencia — exactamente el patrón que la industria recomienda ("store the raw webhook event, and check whether the event was already received"). Esto **no hay que rediseñarlo**, solo generalizarlo a más verticales cuando dejen de ser solo Tienda.

## Errores conocidos que otros ya cometieron

1. **Romper la interfaz de provider sin necesidad, tarde.** Medusa rediseñó su Payment Provider interface completa en v2, con el propio equipo "apologizing in advance" a todos los mantenedores de adapters de terceros ([GitHub discussion #7955](https://github.com/medusajs/medusa/discussions/7955)). Lección directa para Synergos: como hoy solo hay un adapter real pendiente (Wompi, Ola B, aún no escrito), **este es el momento más barato para fijar bien la forma de `IPaymentProvider`** — después de que exista un adapter real en producción, cambiar la firma cuesta N veces más.
2. **Modelar el pago como una llamada única en vez de una máquina de estados.** Es literalmente por qué Saleor migró de plugins a Transactions API — "instead of multiple calling `checkoutComplete`... the `transactionProcess` should be called" ([Saleor docs](https://docs.saleor.io/developer/payments/payment-apps)). Synergos ya evita parte de esto (`PaymentStatus` enum con 7 estados canónicos), pero **capturar sin soporte de monto parcial** es la misma trampa en miniatura.
3. **Tratar "duplicar el webhook" como caso raro en vez de caso normal.** El post-mortem citado de un fintech real: "a network glitch that double-charged more than 3,000 users in 30 minutes" por no diseñar el handler para ser llamado dos veces desde el día uno ([Medium — idempotency lessons](https://medium.com/@vaidya.seshagiri/why-payment-systems-fail-without-idempotency-a-developers-guide-2026-daddb7260263)). Synergos ya lo resolvió bien (ver arriba) — el riesgo es que un adapter futuro (Wompi real) rompa esta disciplina si no se documenta como *contrato obligatorio* del seam, no solo como implementación del stub.
4. **Falta de documentación de gateways legacy SOAP.** Medusa señala que un gateway con REST limpio toma 20-40h de integración, uno legacy SOAP toma 40-80h ([StudyRaid/Medusa community estimate](https://tonie.hashnode.dev/implementing-a-custom-payment-gateway-integration-in-medusajs)) — no es un error de diseño per se, pero es una señal de presupuesto a tener en cuenta cuando llegue Wompi/PayU real (ambos tienen APIs REST razonables en CO, así que el riesgo es bajo, pero no cero).
5. **Cripto/providers "colgados" sin ruta de migración clara.** El propio Medusa v2 dejó proveedores de pago cripto (BitPay, Coinbase) sin guía de compatibilidad tras el rediseño ([issue #14971](https://github.com/medusajs/medusa/issues/14971)) — lección: cualquier cambio de interfaz debe decidir explícitamente qué pasa con adapters no migrados, no dejarlo implícito.

## Propuesta de forma para `IPaymentProvider` en Synergos

Principio rector: **evolución aditiva, cero breaking changes a los 8 consumidores actuales**, respetando ADR 0002 (Application sin Umbraco/AspNetCore/paquetes de DI) y la prohibición de abstracciones sin 2+ implementaciones reales (CLAUDE.md §6). El patrón que ya usa `IOrderTrackingService` ("misma seam, N instancias configuradas por call-site") es el molde: en vez de una única instancia global, un **Composite que implementa la misma interfaz** y decide internamente a cuál delegar — así los 8 consumidores no cambian ni una línea.

```csharp
// Synergos.CMS.Interfaces — sin dependencias externas, cero Umbraco/AspNetCore.

// 1) Extensiones ADITIVAS al contrato existente — mismos records, nuevos campos opcionales.
public sealed record PaymentSessionRequest(
    string OrderReference,
    decimal Amount,
    string Currency,
    IReadOnlyList<PaymentLineItem> Items,
    string? CustomerEmail = null,
    string? ReturnUrl = null,
    string? Vertical = null,               // NUEVO — "shop"|"events"|"booking"|... para enrutamiento y auditoría
    string? CountryCode = null,             // NUEVO — enrutamiento por país (ISO 3166-1 alpha-2)
    IReadOnlyDictionary<string, string>? Metadata = null); // YA EXISTE — punto de extensión para split/payeeId a futuro

public interface IPaymentProvider
{
    string ProviderKey { get; }

    Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default);

    Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default);

    // CAMBIO: amount opcional → soporta captura total (comportamiento actual sin cambios,
    // pasar null) Y captura parcial/múltiple (hotel noche a noche, envío en 2 paquetes).
    Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default);

    // NUEVO: libera un hold SIN haber cobrado (aforo de evento no confirmado, cotización
    // expirada). Semánticamente distinto de RefundAsync (que revierte un cobro YA hecho).
    Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default);
}

// 2) Seam de enrutamiento — NO reemplaza IPaymentProvider, decide cuál instancia usar.
//    Vive en Interfaces (puro). Análogo a cómo hoy cada vertical construye su propia
//    instancia de IOrderTrackingService con un pipeline distinto.
public sealed record PaymentRoutingRule(string ProviderKey, string? Vertical = null, string? CountryCode = null, string? Currency = null)
{
    public bool Matches(PaymentSessionRequest r) =>
        (Vertical is null || string.Equals(Vertical, r.Vertical, StringComparison.OrdinalIgnoreCase)) &&
        (CountryCode is null || string.Equals(CountryCode, r.CountryCode, StringComparison.OrdinalIgnoreCase)) &&
        (Currency is null || string.Equals(Currency, r.Currency, StringComparison.OrdinalIgnoreCase));
}

// 3) Implementación — Application, lógica pura, cero DI. Se registra como EL ÚNICO
//    IPaymentProvider en el composer; internamente delega a N providers reales.
//    El sessionId codifica el provider real como prefijo ("wompi:ch_123") para que
//    Capture/GetStatus/Refund/Void puedan enrutar sin storage adicional.
public sealed class RoutingPaymentProvider : IPaymentProvider
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers; // por ProviderKey
    private readonly IReadOnlyList<PaymentRoutingRule> _rules;                 // orden = prioridad
    private readonly string _fallbackKey;

    public RoutingPaymentProvider(
        IEnumerable<IPaymentProvider> providers,
        IReadOnlyList<PaymentRoutingRule> rules,
        string fallbackProviderKey)
    {
        _providers = providers.ToDictionary(p => p.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _rules = rules;
        _fallbackKey = fallbackProviderKey;
    }

    public string ProviderKey => "routing"; // identidad del composite; el real queda en el prefijo del sessionId

    public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken ct = default)
    {
        var provider = Resolve(request);
        // delega y re-prefija el sessionId devuelto con provider.ProviderKey (ver detalle abajo)
        return CreateAndPrefixAsync(provider, request, ct);
    }

    public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken ct = default)
        => ResolveByPrefix(sessionId).CaptureAsync(StripPrefix(sessionId), amount, ct);

    // GetStatusAsync / VoidAsync / RefundAsync: mismo patrón — ResolveByPrefix + StripPrefix.

    private IPaymentProvider Resolve(PaymentSessionRequest r)
    {
        foreach (var rule in _rules)
            if (rule.Matches(r) && _providers.TryGetValue(rule.ProviderKey, out var p)) return p;
        return _providers.TryGetValue(_fallbackKey, out var fallback) ? fallback : _providers.Values.First();
    }

    // ResolveByPrefix/StripPrefix/CreateAndPrefixAsync: detalle de implementación,
    // omitido aquí por brevedad — parsean "providerKey:innerId".
}
```

**Cómo se registra (Web, `SeamComposer` — el único punto que cambia hoy):**

```csharp
// Hoy: un switch que registra UN IPaymentProvider según PaymentsSettings.Provider.
// Propuesta: registrar TODOS los providers habilitados + un Router que los envuelve,
// bajo la MISMA interfaz — los 8 consumidores (sp.GetRequiredService<IPaymentProvider>())
// no cambian.
services.AddSingleton<IPaymentProvider>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<PaymentsSettings>>().Value;
    var stub = new StubPaymentProvider(sp.GetRequiredService<IJsonEntityStore>(), settings);
    var all = new List<IPaymentProvider> { stub };
    // if (WompiEnabled) all.Add(new WompiPaymentProvider(...));  // Ola B, gated
    if (all.Count == 1) return all[0]; // hoy: cero overhead, comportamiento IDÉNTICO al actual
    return new RoutingPaymentProvider(all, settings.Routing, settings.Provider);
});
```

Con un solo provider activo (estado actual), el composer devuelve el provider directo — **cero cambio de comportamiento hoy**. El día que Wompi (o PayU) se enciende junto al stub, o que aparece un segundo PSP real, el `RoutingPaymentProvider` se activa solo, sin tocar ninguno de los 8 consumidores ni el resto del motor.

## Qué del diseño actual se conserva y qué cambia

**Se conserva (está bien hecho, alineado con lo que hacen los maduros):**
- La forma general del seam: `ProviderKey` + `CreateSession/GetStatus/Capture/Refund`, PSP-agnóstico — coincide con el "Payment Sessions API" de Spree y el "Payment Provider" de Medusa.
- `PaymentStatus` como enum canónico de 7 estados, no un string libre del PSP — evita el acoplamiento que Saleor tuvo que deshacer.
- La durabilidad vía `IJsonEntityStore` (ADR 0104) — ya resuelve el problema que forzó a Saleor a migrar a Transactions API (estado sobrevive reinicio/reconciliación).
- El ledger de idempotencia del webhook (`IPaymentEventStore`, create-exclusivo atómico) — ya implementa exactamente lo que Stripe/la industria recomienda para webhooks duplicados.
- El registro config-gated (`PaymentsSettings.Provider`, switch en el composer) — buen punto de partida, se generaliza sin descartarse.
- `Metadata: IReadOnlyDictionary<string,string>` en `PaymentSessionRequest` — ya es el punto de extensión correcto para split/payeeId futuro, no hace falta tocarlo hoy.

**Cambia (gaps reales encontrados en la investigación):**
- `CaptureAsync` gana `decimal? amount = null` — soporta captura parcial/múltiple (hotel, envíos fraccionados) sin romper las llamadas existentes (pasan `null` = comportamiento actual).
- Se agrega `VoidAsync` — semántica distinta de `RefundAsync` para liberar holds no capturados (aforo de evento, cotización expirada), evitando que el flujo de Eventos/Booking tenga que fingir un refund de $0.
- Se agrega `Vertical`/`CountryCode` opcionales a `PaymentSessionRequest` — insumo para enrutamiento, no obligatorios (los 8 call-sites pueden migrarse gradualmente, con default null = comportamiento actual).
- Se agrega `RoutingPaymentProvider` (Composite en Application) + `PaymentRoutingRule` (Interfaces) — el mecanismo de "reinstanciar/polimorfizar" que pidió el arquitecto, calcando el patrón ya validado de `IOrderTrackingService` pero sin tocar los 8 consumidores.
- **NO se agrega** todavía: `ISplitPaymentProvider`, `IRecurringPaymentProvider` ni ledger de eventos append-only nuevo — son extensiones futuras documentadas (arriba) pero construirlas ahora violaría la regla del repo de "no abstracciones sin 2+ implementaciones reales" (no hay hoy ningún vertical multi-vendedor ni recurrente).

## Fuentes (URLs)

Documentación oficial:
- Medusa — [Payment Module](https://docs.medusajs.com/resources/commerce-modules/payment)
- Medusa — [Payment Architecture Overview (v1)](https://docs.medusajs.com/v1/modules/carts-and-checkout/payment)
- Medusa — [v2.5.0 release notes](https://github.com/medusajs/medusa/releases/tag/v2.5.0)
- Medusa — [Discussion #7955 — 2.0 Public Preview](https://github.com/medusajs/medusa/discussions/7955)
- Medusa — [Issue #14971 — crypto providers sin guía de compat v2](https://github.com/medusajs/medusa/issues/14971)
- Saleor — [Using Payment Apps](https://docs.saleor.io/developer/payments/payment-apps)
- Saleor — [paymentGatewayInitialize mutation](https://docs.saleor.io/api-reference/payments/mutations/payment-gateway-initialize)
- Saleor — [Issue #11258 — Support for payment apps](https://github.com/saleor/saleor/issues/11258)
- Saleor — [Stripe App migration from plugin](https://docs.saleor.io/developer/app-store/apps/stripe/migration-from-plugin)
- Sylius — [Adding a payment gateway (blog oficial)](https://sylius.com/blog/new-docs-adding-a-payment-gateway-in-sylius/)
- Sylius — [Payments (docs)](https://docs.sylius.com/the-book/carts-and-orders/payments)
- Spree — [Payments — core concepts](https://spreecommerce.org/docs/developer/core-concepts/payments)
- Stripe — [The Payment Intents API](https://docs.stripe.com/payments/payment-intents)
- Stripe — [Server-side integration / idempotency](https://docs.stripe.com/plan-integration/get-started/server-side-integration?locale=en-GB)
- Stripe — [Connect — platforms and marketplaces](https://docs.stripe.com/connect)
- Stripe — [Separate charges and transfers](https://docs.stripe.com/connect/marketplace/tasks/accept-payment/separate-charges-and-transfers)
- Stripe — [Accept a payment (Connect)](https://docs.stripe.com/connect/marketplace/tasks/accept-payment)
- Stripe — [Recurring payments](https://docs.stripe.com/recurring-payments)

Opinión / terceros (marcado explícitamente como tal):
- [Lago blog — Payment Orchestration: Multi-PSP Routing and Failover](https://getlago.com/blog/payment-orchestration-multi-psp-routing-and-failover) (vendor de orquestación, opinión de industria)
- [Primer — What payment platforms support multi-acquirer smart routing](https://primer.io/blog/what-payment-platforms-support-multi-acquirer-smart-routing) (vendor, opinión)
- [Inai — Authorization vs Capture](https://inai.io/blog/authorization-vs.-capture-in-payments) (blog educativo de vendor)
- [Engine — Hotel Credit Card Holds](https://engine.com/business-travel-guide/hotel-credit-card-authorization) (blog de industria travel)
- [APIScout — Stripe Webhooks 2026 guide](https://apiscout.dev/guides/stripe-webhooks-complete-guide-2026) (guía de terceros)
- [engineeringenablement.substack.com — What I wish I knew before I designed my first payment system](https://engineeringenablement.substack.com/p/what-i-wish-i-knew-before-i-designed) (relato personal, opinión)
- [Medium — Why Payment Systems Fail Without Idempotency](https://medium.com/@vaidya.seshagiri/why-payment-systems-fail-without-idempotency-a-developers-guide-2026-daddb7260263) (opinión + caso citado sin fuente primaria verificable — tratar el dato del incidente de 3000 usuarios como anecdótico, no verificado)
- [tonie.hashnode.dev — Implementing a Custom Payment Gateway in MedusaJS](https://tonie.hashnode.dev/implementing-a-custom-payment-gateway-integration-in-medusajs) (estimaciones de horas de un desarrollador individual, no dato oficial)

Código propio citado (referencia interna, no URL externa):
- `Synergos.CMS.Interfaces/IPaymentProvider.cs`
- `Synergos.CMS.Application/Services/Impl/StubPaymentProvider.cs`
- `Synergos.CMS.Application/Configuration/PaymentsSettings.cs`
- `Synergos.CMS.Web/Composers/SeamComposer.cs`
- `Synergos.CMS.Web/docs/adr/0104-durable-payments-reservations-and-inbound-webhook-tienda-t3-ola-a.md`
