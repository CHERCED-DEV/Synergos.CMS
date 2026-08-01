# ADR 0116 — El motor de pagos es una seam TRANSVERSAL y polimórfica: un router que es el mismo contrato, tres formas de "requiere acción", y un despacho de eventos que no es de Tienda

- **Status:** **Accepted — parcialmente construido.** El arquitecto respondió las tres preguntas abiertas: Wompi como primer adaptador, la forma se rompe ahora, y se trabaja todo. Fases 1-4 completas y fase 5 casi (5 de 6 defectos reales); fase 6 pendiente. Ver §Estado de construcción.
- **Date:** 2026-08-01
- **Deciders:** Arquitecto (encargo textual: *"como todas van a tener pagos, hacer bien payment, que se pueda reinstanciar o reutilizar o polimorfizar, es muy ganador"*) + agente.
- **Investigación:** cuatro barridos paralelos, dos sobre código y dos con búsqueda web autorizada. Anexos en `docs/product/investigacion-pagos/` (871 líneas, con URLs y fechas).
- **Relacionados:** ADR 0104 (pagos y reservas durables + webhook entrante — **este ADR lo supersede en un punto concreto**, ver §6), ADR 0105 (`IJsonEntityStore`), ADR 0106 (notificaciones at-most-once), ADR 0107 (una capacidad transversal no se implementa dos veces), ADR 0009 (seams de extensión), ADR 0002 (Application sin Umbraco/AspNetCore).

---

## Context

### Lo que ya estaba bien

`IPaymentProvider` **no** es un stub improvisado. Ya trae los siete estados
canónicos rotulados como mapeo a Stripe/Wompi/PayU, la separación
autorización/captura, `Metadata` genérica, un `PaymentWebhookController` con
verificación HMAC, y `PaymentsSettings` con las cuatro llaves de Wompi
reservadas y rotuladas "Ola B". Quien lo escribió sabía a dónde iba.

`IIdempotencyLedger` (marca-después-de-confirmar, `FileMode.CreateNew` atómico)
y `PaymentWebhookVerifier` (HMAC + ventana ±5 min + re-consulta anti-tampering
+ fail-closed) **ya implementan lo que la industria recomienda**. No se tocan.

### Lo que la investigación encontró

**1. Ocho verticales usan el motor, y siete lo usan mal — cada uno distinto.**

| Vertical | Defecto verificado |
|---|---|
| Salud | `StubClinicalSchedulingService.cs:128` confirma la reserva **incondicionalmente**, sin mirar si `CaptureAsync` capturó. Único sin `IJsonEntityStore`. |
| Gobierno | `StubApplicationService.cs:171-248` abre sesión, **nunca captura ni consulta**, y `CaseState` no tiene campo donde persistirla. El expediente se radica con o sin pago. |
| Booking | `BookingController.cs:261-289` calcula penalidad y monto reembolsable, y **nunca llama `RefundAsync`**. La cifra es decorativa. |
| Eventos, Educación | Sin reembolso, en absoluto. |
| Viajes | Compensación parcial: queda en `Partial` sin revertir el cobro. |

Ninguno usa autorización diferida real. **Nada de esto se escapa hoy** porque el
único PSP auto-aprueba — es el mismo caso que el guard del RMA: son los patrones
que se heredan el día que se conecte Wompi.

**2. `RequiresAction` + `RedirectUrl` es demasiado angosto.** No hay una forma de
"requiere acción del cliente". Hay tres, incompatibles:

| Método | Qué exige |
|---|---|
| PSE | Redirect completo del navegador |
| 3DS2 con challenge | HTML **inline** en iframe + polling cada 2-3 s |
| Nequi | Ni URL ni HTML: esperar aprobación push en el celular |

**3. El despacho de webhooks solo conoce Tienda.**
`PaymentWebhookController.cs:36,40` enruta `"stub"` → `IShopOrderService`. Con
tarjeta se disimula. Con **PSE** —que confirma en diferido— siete de ocho
verticales no se enteran de que les pagaron.

**4. Faltan los contracargos.** Una disputa llega semanas o meses después de
`Captured`, la inicia el emisor y va por otro canal que el reembolso. No cabe en
ninguno de los siete estados.

**5. Separar pago / pedido / cumplimiento es consenso.** Shopify separa cuatro
capas, Medusa tres, VTEX encadena pago y handling en paralelo. Sólo WooCommerce
los funde, y por eso es el peor referente. Synergos **ya está alineado
conceptualmente** —`OrderStatus` es pago, `IOrderTrackingService` es
cumplimiento, `StubReturnService` es devoluciones— pero **no está operado**:
nada avanza el timeline más allá de `paid`, y tracking y RMA viven en memoria.

## Decision

### 1. El router ES el mismo contrato, no una interfaz nueva

`RoutingPaymentProvider : IPaymentProvider` compone N proveedores y enruta por
reglas (vertical / país / moneda / método). Con un solo proveedor activo
devuelve el proveedor directo: **cero cambio de comportamiento hoy y cero cambio
en los 8 consumidores**.

Es el patrón que la casa ya validó con `IOrderTrackingService`, que hoy se
instancia dos veces con pipelines distintos (envío en Tienda, otro en Eventos).
Ésa es la respuesta a "reinstanciar o polimorfizar": **misma seam, N instancias
configuradas**, no una jerarquía nueva.

El `sessionId` lleva el proveedor como prefijo para que `Capture`/`Status`/
`Refund` enruten sin almacenamiento extra. **Con separador `|`, no `:`** — los
ids de PSP contienen dos puntos con más frecuencia de la que uno espera, y el
parseo va con test propio.

### 2. La forma se rompe UNA vez, y es ahora

Medusa rediseñó su interfaz de proveedores en v2 y tuvo que pedir disculpas por
adelantado a los mantenedores de adaptadores externos. Con **cero adaptadores
reales**, hoy es el momento más barato. Por eso los cuatro cambios entran juntos
y no en olas separadas:

```csharp
public enum PaymentActionKind { None, Redirect, InlineChallenge, AwaitApproval }

/// Cómo se le pide al cliente que haga algo. Reemplaza el par
/// RedirectUrl/ClientSecret, que sólo sabía expresar el primer caso.
public sealed record PaymentAction(
    PaymentActionKind Kind,
    Uri? RedirectUrl = null,      // Redirect       — PSE
    string? InlineHtml = null,    // InlineChallenge— 3DS2 (iframe srcDoc)
    string? ClientSecret = null,  // InlineChallenge— Stripe Elements
    TimeSpan? PollInterval = null,// InlineChallenge/AwaitApproval
    string? UserHint = null);     // AwaitApproval  — "aprobá en tu Nequi"

public sealed record PaymentSession(
    string SessionId,
    PaymentStatus Status,
    PaymentAction? Action = null,
    string? ProviderKey = null);

public interface IPaymentProvider
{
    string ProviderKey { get; }
    Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest r, CancellationToken ct = default);
    Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken ct = default);
    Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken ct = default);
    Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken ct = default);
    Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken ct = default);
}
```

- **`CaptureAsync` gana monto.** Hoy `RefundAsync` acepta parcial y `CaptureAsync`
  no: esa asimetría bloquea la captura por noche en hoteles.
- **`VoidAsync` es nuevo** y no es cosmético: es lo que le falta a Salud para
  soltar el cupo cuando el pago no capturó, y a Booking para cancelar sin cobrar.

### 3. `PaymentStatus` gana `Disputed` y `ChargedBack`

Un contracargo no es un reembolso: no lo inicia el comercio, llega tarde, y
puede revertirse. Modelarlo como `Refunded` haría que la contabilidad mintiera.

### 4. El despacho de eventos deja de ser de Tienda

`IPaymentEventSink` — cada vertical registra el suyo. El controller deja de
conocer `IShopOrderService`.

**Corrección respecto al borrador de este ADR:** se propuso indexar por el
prefijo de `OrderReference` y al construirlo se descartó. Las referencias del
repo no comparten convención —`ord_`, `enr_`, un radicado, el id de una
reserva— y una convención que hay que recordar es una convención que alguien va
a romper. Cada sink responde `OwnsAsync` mirando si tiene el registro: el dueño
del dato es la única autoridad fiable.

La deduplicación es por **`(ProviderKey, EventId)`**. Para los eventos fuera de
orden **no hizo falta** el descarte por antigüedad que este ADR anticipaba: el
anti-tampering ya re-consulta el estado real al proveedor, así que un evento
rancio actúa sobre la verdad actual y no sobre la que traía.

### 5. Tres máquinas de estado, y `DeliveredAt` persistido

Pago, pedido y cumplimiento se mantienen separados —ya lo están— y se **operan**:
alguien tiene que llamar `AdvanceAsync` más allá de `paid`. `IOrderTrackingService`
e `IReturnService` pasan a `IJsonEntityStore`.

`DeliveredAt` se persiste por orden. No es un adorno: **es la base legal**. El
retracto (5 días hábiles desde la entrega) y la garantía (1 año) se cuentan
desde ahí, y hoy esa fecha no existe en ninguna parte.

### 6. Lo que este ADR supersede del 0104

El ADR 0104 §135-138 descartó un reconciliador de pagos agnóstico del dominio
por **prematuro**. Con ocho consumidores, siete de ellos defectuosos y un PSP
asíncrono a la vista, ya no lo es. `IPaymentEventSink` es esa pieza.

### 7. Fuera de alcance, a propósito

Envíos (`IShippingProvider`, adaptador a Mipaquete), impuestos y facturación
DIAN (`ITaxProvider` / `IInvoiceEmitter`) son **seams nuevas de otro dominio**.
Entran después, con el molde de `IBundleRegistryClient`. Meterlas acá sería
convertir un ADR de pagos en un ADR de comercio entero.

## Consequences

**Bueno.** Un solo contrato sirve a los nueve verticales y admite N proveedores
sin tocar consumidores. `VoidAsync` y la captura parcial cierran tres bugs
verificados. El despacho generalizado es lo que hace viable PSE, que es el
método que el mercado colombiano exige.

**El coste, dicho en claro.** Es un cambio de forma que toca los 8 consumidores
del lado de la construcción del request, aunque no de la lógica. Y no arregla
por sí solo los siete usos defectuosos: los habilita. Cada vertical necesita su
propia corrección, y son siete.

**Riesgo asumido.** Si el arquitecto elige un PSP distinto de Wompi, la forma
sigue sirviendo —es agnóstica— pero las tres modalidades de acción se
dimensionaron con Wompi/PSE/Nequi a la vista.

## Alternatives considered

- **Una interfaz nueva de orquestación por encima.** Habría dejado
  `IPaymentProvider` intacto a cambio de dos conceptos donde hay uno. Se
  descarta por ADR 0107.
- **Romper la forma en olas.** Más seguro en apariencia, peor en la práctica: dos
  migraciones de adaptadores en vez de una, y la segunda ya con adaptadores
  reales escritos.
- **Modelar el contracargo como reembolso.** Más barato hoy, y hace que la
  contabilidad mienta.

## Preguntas abiertas — RESUELTAS por el arquitecto

1. **¿Wompi como primer adaptador?** → **Sí.**
2. **¿Se rompe la forma ahora?** → **Sí**, en una sola ronda.
3. **¿Los siete usos defectuosos se corrigen en la misma ola?** → **Sí**, se
   trabaja todo. Pendiente en fase 5.

## Decisión adicional tomada al construir: Web Checkout, no API directa

`WompiPaymentProvider` usa el **checkout hospedado**, no la API de
tokenización. No es una limitación: cubre tarjeta, PSE, Nequi, Bancolombia y
efectivo con **un solo flujo**, y deja los datos de tarjeta fuera de nuestros
servidores — la carga de PCI se queda en Wompi. La API directa da más control
sobre la experiencia a cambio de tokenizar cada método y asumir ese alcance; es
la evolución natural, no el punto de partida.

**Consecuencia honesta:** con Web Checkout el adaptador devuelve siempre
`Redirect`, porque el checkout resuelve internamente el reto 3DS y la espera de
Nequi. Las otras dos formas de `PaymentAction` **siguen justificadas** —las
necesita la API directa, y otros PSPs las exponen— pero hoy sólo las ejercita
el stub. Quien lea el código no debe concluir que sobran.

Y con Web Checkout **no hay autorización nuestra que capturar**: Wompi cobra al
aprobar. Por eso `CaptureAsync` en este adaptador *constata* en vez de cobrar, e
**ignora el monto parcial diciéndolo**, en lugar de fingir que lo honró. La
captura parcial exige el flujo de autorización diferida de la API directa.

## Estado de construcción

| Fase | Qué | Estado |
|---|---|---|
| 1 | Forma del seam: `PaymentAction` ×3, captura parcial, `VoidAsync`, `Disputed`/`ChargedBack` | ✅ construida |
| 2 | `RoutingPaymentProvider` — misma seam, N proveedores | ✅ construida |
| 3 | Firmas de Wompi + adaptador Web Checkout + cableado | ✅ construida |
| 4 | `IPaymentEventSink` — despacho de eventos por vertical | ✅ construida |
| 5 | Corregir los usos defectuosos | 🟡 5 de 6 reales (ver abajo) |
| 6 | Persistir tracking y RMA | ✅ construida · `DeliveredAt` ⬜ |

**Verificado:** `dotnet build` 0 errores · `dotnet test` **1093/1093**.

### Fase 6 — durabilidad

`IOrderTrackingService` y `IReturnService` pasan a `IJsonEntityStore`. Vivían en
un diccionario del proceso mientras órdenes, pagos y reservas ya estaban en
disco: un reinicio borraba el timeline de envío de órdenes que seguían `Paid`, y
las devoluciones que un comprador ya había pedido.

**Cada instancia de tracking necesita su propio espacio.** Hay cuatro —Tienda,
Viajes, Educación, Eventos— con pipelines de distinta longitud, y el estado
guarda el ÍNDICE de etapa. Compartir espacio haría que el índice de un dominio
se leyera contra el pipeline de otro: "enviado" convertido en otra cosa sin que
nada falle. De ahí el parámetro `storeNamespace`.

El `lock` no sobrevive un `await`, así que se sustituye por `SemaphoreSlim` —
mismo criterio que `StubPaymentProvider` ya aplicaba.

**Falta `DeliveredAt`**, que es la base legal del retracto (5 días hábiles) y de
la garantía (1 año) del Estatuto del Consumidor. Va con la etapa `delivered` del
pipeline, y hoy nadie la avanza: el pedido nunca pasa de "pagado". Las dos cosas
son la misma tarea y son la siguiente.

### Fase 5 — lo corregido y lo que falta

| Vertical | Antes | Ahora |
|---|---|---|
| **Salud** | Confirmaba el cupo sin mirar si capturó | Si no captura: `VoidAsync` + libera el hold + no agenda |
| **Gobierno** | Abría sesión, nunca capturaba ni la persistía | Captura y guarda `PaymentSessionId` + `PaymentStatus` en `CaseState` |
| **Booking** | Calculaba el reembolso y no lo ejecutaba | Llama `RefundAsync` por total menos penalidad |
| **Viajes** | Quedaba en `Partial` sin revertir el cobro | Reembolso parcial de lo no entregado; si NADA confirma, devuelve todo y falla |
| **Eventos** | No liberaba los asientos si el pago fallaba | `VoidAsync` + libera cada asiento |
| Educación | — | **Ya era correcto**: lanza y no activa. Ver corrección abajo |

### Corrección al informe de investigación

La investigación reportó *"Eventos y Educación: sin reembolso, en absoluto"*. Al
ir al código, eso mezcla dos cosas distintas:

- **No manejar el fallo de captura** es un defecto. Eventos lo tenía a medias
  (no activaba la compra, pero dejaba los asientos apartados hasta que venciera
  el hold — entradas que nadie más podía comprar por un pago que no ocurrió).
  Corregido.
- **No tener cancelación con reembolso** es una capacidad que falta, no un bug.
  `IEventTicketingService` e `IEnrollmentService` no exponen `CancelAsync`, y
  añadirla exige decidir la política: ¿hasta cuándo se devuelve una entrada?
  ¿un curso ya empezado se reembolsa completo? Eso es decisión de producto y no
  se inventa desde acá.

Así que los defectos reales eran **6, no 7**, y quedan **5 corregidos**.

### Sinks registrados

Tienda y **Viajes**. Viajes importa especialmente porque su carrito se paga a
menudo por PSE, y con PSE el resultado sólo llega por evento.

Eventos y Educación **no pueden tener sink todavía**: sus seams no exponen
búsqueda por referencia de orden, así que un sink no podría ni decir si el pago
es suyo. Añadir ese método es lo siguiente, y es útil de todos modos (lo pide
cualquier pantalla de "mis entradas").

**Gobierno no aborta el trámite si la tasa no captura**, y es deliberado: en un
servicio público, perder la radicación de un ciudadano porque su banco tardó es
peor que arrastrar una tasa pendiente. Se registra el estado y el expediente
queda marcado — que es justo lo que antes era imposible, porque el id de sesión
no se guardaba en ninguna parte.

### Cómo se enruta un evento a su vertical (fase 4)

No por prefijo del identificador: las referencias del repo no comparten
convención — `ord_`, `enr_`, un radicado, el id de una reserva — y una
convención que hay que recordar es una convención que alguien va a romper. Cada
sink responde `OwnsAsync` mirando si tiene el registro: **el dueño del dato es
la única autoridad fiable sobre de quién es**.

`OwnsAsync` está separado de `HandleAsync` a propósito, y no es cosmético: el
receptor pregunta de quién es ANTES de consultarle el estado al PSP. Al revés,
cualquiera podría hacernos golpear a Wompi mandando referencias inventadas — un
amplificador gratis a costa nuestra.

**Sobre eventos que llegan fuera de orden**, que la investigación marcaba como
riesgo: no hizo falta maquinaria de versiones. El anti-tampering ya re-consulta
el estado real al proveedor, así que un evento rancio actúa sobre la verdad
actual y no sobre la que traía. Es una propiedad que el diseño ya tenía,
aprovechada para otra cosa.

**Hoy sólo Tienda tiene sink.** Los otros siete entran en la fase 5, cuando se
corrijan sus usos del motor: registrarlos antes sería entregarles un evento que
no saben procesar.

**La fase 4 es la que falta para que Wompi sirva de verdad.** Hoy
`PaymentWebhookController` sólo enruta a Tienda, y con Web Checkout el estado
final de PSE llega *sólo* por evento. Sin ese despacho, siete verticales no se
enteran de que les pagaron. `GetStatusAsync` es el respaldo, no el mecanismo.

**⚠️ Nada de Wompi está verificado contra su sandbox**: no hay llaves en el
entorno de desarrollo del agente. Los 28 tests garantizan que el algoritmo de
firma y el mapeo de estados hacen lo que la documentación describe — no que
Wompi los acepte.

## Advertencias sobre la investigación

Los anexos marcan explícitamente como **no confirmado**: los plazos exactos de
contracargo por franquicia, las tarifas vigentes de PayU y Mercado Pago, la
existencia de API B2B propia de Interrapidísimo y TCC, y el umbral exacto de
obligatoriedad DIAN para un vendedor concreto. El marco legal colombiano
(Ley 1480/2011) se citó desde fuente secundaria porque el texto primario devolvió
503 dos veces. **Nada de eso debe firmarse en un contrato sin reverificar.**
