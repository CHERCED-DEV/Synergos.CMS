# ADR 0104 — Pagos y reservas durables + webhook de pago entrante (Tienda, T3 Ola A, doc 25)

- **Status:** Accepted
- **Date:** 2026-07-15
- **Deciders:** Arquitecto + agente, fase de lógica de negocio (doc rector `25`, transversal T3 pagos). Diseño producido por un workflow multi-agente (panel de jueces: ganó el enfoque "adapter gated + stub durable", partido en dos olas) y endurecido por un workflow de revisión adversarial (6 hallazgos, 0 refutados → 4 fixes). Verificado end-to-end contra código vivo, incluido reinicio del CMS entre checkout y confirmación.
- **Relacionados:** ADR 0103 (T1+T2 — persistencia FileSystem + ownership; este ADR reusa su patrón de store 1:1 y cierra la brecha de restart que dejó latente), ADR 0002 (Application sin Umbraco/AspNetCore — el adapter PSP HTTP y el webhook viven en Web), ADR 0075 (tests por seam), ADR 0013 (sin I/O en boot), ADR 0028 (Shop runtime + `IPriceFormatter`), ADR 0080/0065 (webhooks SALIENTES + verificación HMAC de referencia — este es el primer webhook ENTRANTE), ADR 0089 (gating por config `BundleRegistry.Mode`, patrón calcado para el proveedor). Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

El seam `IPaymentProvider` (PSP-agnóstico, estados canónicos) existía desde el motor,
pero su único adapter, `StubPaymentProvider`, auto-autorizaba **en memoria del proceso**.
El doc 25 (§6) define T3 como "Pagos reales (PSP CO: Wompi/PayU/Mercado Pago)", P0/P1,
validado en Tienda y heredado por Booking/Eventos.

Dos problemas concretos:

1. **El dinero era cero durabilidad.** El estado de la sesión de pago vivía en un
   diccionario del proceso. Peor: T1 durabilizó la *orden* (persiste `PaymentSessionId`)
   pero no la sesión — así que un checkout hecho **antes** de un reinicio no se podía
   confirmar: `CaptureAsync(sessionId)` no encontraba la sesión perdida. La revisión de
   diseño halló que la brecha era **doble**: `ConfirmAsync` también confirma las
   **reservas** de stock, cuyo hold vivía igualmente en memoria — tras un reinicio la
   confirmación lanzaba "Reserva no encontrada" **antes** de marcar la orden pagada.

2. **Restricción del entorno:** el adapter PSP real necesita llaves de sandbox +
   webhooks + red que no están disponibles ahora (el operador no puede teclear
   credenciales). Un T3 que dependiera de ellas no sería construible ni verificable.

## Decision

T3 se parte en **dos olas** sobre **dos ejes ortogonales**. Esta ola (A) entrega todo
lo verificable **sin llaves de PSP**; la Ola B (adapter Wompi HTTP real) queda gated por
config para encenderse cuando haya llaves.

### Eje 1 — Durabilidad (registro INCONDICIONAL, cierra el restart-gap)

Se reusa el patrón de T1 (ADR 0103) 1:1. Seams opacos nuevos:
- **`IPaymentSessionStore`** + `InMemoryPaymentSessionStore` (Application) +
  `FileSystemPaymentSessionStore` (Web, `App_Data/syn-payments/`). El
  `StubPaymentProvider` pierde su diccionario y serializa su estado por `sessionId`.
- **`IReservationStore`** + impls análogas (`App_Data/syn-reservations/`). El
  `StubReservationService` pierde su diccionario. **Necesario** para cerrar el restart-gap
  end-to-end (la corrección del panel). Reusable por Booking/Eventos.
- **`IPaymentEventStore`** — ledger de idempotencia del webhook, **create-exclusivo
  atómico** (`FileMode.CreateNew`, no write-overwrite — anti-TOCTOU).

Escritura atómica (temp + `File.Move`), lazy (nada en boot), fail-safe, sin cifrar (PII
de compra, no PHI). Ctors aditivos (default InMemory) → cero call-sites rotos, NO EF.

### Eje 2 — Selección de proveedor (config-gated)

`PaymentsSettings.Provider` (default `"Stub"`), calcando `BundleRegistry.Mode`. Un solo
punto de swap en `SeamComposer` (switch a compose-time); los 6+ consumidores del seam
(`StubShopOrderService`, `StubEventTicketingService`, `StubEnrollmentService`,
`StubClinicalSchedulingService`, `StubApplicationService`, `StubReturnService`,
Booking) resuelven la abstracción `IPaymentProvider` → 0 ediciones aguas abajo. La rama
`"Wompi"` está preparada (comentada) para la Ola B; sin adapter cae al stub durable —
la demo nunca se bloquea.

### Webhook de pago entrante (el primero del repo)

`PaymentWebhookController` (`POST /api/payments/webhook/{provider}`, `[AllowAnonymous]`):
el PSP postea server-to-server sin cookie de member → **la firma ES la autorización**
(`IMemberAccessGate` se bypassea). Flujo endurecido: lee el **body RAW a bytes** antes de
deserializar (el HMAC es sobre bytes exactos) → verifica firma (`PaymentWebhookVerifier`,
espejo inverso de `WebhookSigner`: `HMACSHA256(secret,"{ts}.{body}")` + `FixedTimeEquals`
+ ventana ±5 min) → resuelve la orden → **liga la sesión a la orden** → **anti-tampering**
(re-consulta `GetStatusAsync`, nunca confía el estado del payload) → confirma
(`IShopOrderService.ConfirmAsync`, idempotente) → **marca DESPUÉS del éxito**. Códigos
400/401/404/500/200. Despacho Tienda-específico por ahora (único vertical durable).

Knobs de simulación del stub (`DeclineTriggerSku`, `SimulateRequiresAction`) demuestran
rechazo y 3DS/redirect offline.

### Endurecimiento por revisión adversarial (4 fixes)

Una revisión adversarial del changeset halló 4 defectos de correctness de pagos que ni
los tests ni el e2e feliz atrapaban, todos corregidos:
1. **Mark-before-confirm** → confirmar-luego-marcar (un fallo transitorio dejaba la orden
   cobrada-sin-confirmar permanente).
2. **`CreateNew` catch-filter** invertía el fail-safe (un write parcial se tomaba por
   duplicado) → separar CreateNew del write.
3. **Firma fail-open** ("Provider != Stub → firma obligatoria" sin enforcement) →
   `requireSignature` + `MisconfiguredSecret` (500).
4. **Anti-tampering desacoplado** (validaba la sesión del payload, confirmaba la de la
   orden) → ligar `payload.SessionId == order.PaymentSessionId`.

## Consequences

**Positivas:**

- **Meta cumplida y verificada e2e sin llaves:** checkout → **reiniciar el CMS** →
  confirm = 200 Paid (antes fallaba en dos puntos). Webhook firmable confirma async;
  duplicado → 200 sin doble efecto; sesión ajena → rechazada. "Pagos con rieles reales"
  salvo el HTTP al banco (Ola B).
- **Fan-out barato:** los stores y el webhook son genéricos; Booking/Eventos heredan la
  durabilidad + el patrón de confirmación async con casi cero código nuevo.
- **Adapter real desacoplado:** encender Wompi (Ola B) es config + un adapter tras el
  mismo seam, sin tocar el motor ni los 6 consumidores.
- **Sin EF, sin cifrado innecesario, sin seeders. ADR 0002 intacto.**

**Negativas o trade-offs:**

- **Deuda de verticales:** el estado de orden de Booking/Eventos/Educación/Gobierno sigue
  volátil → el "heredan de Tienda" del doc 25 no es e2e para ellos hasta durabilizarlos
  (mismo refactor T1). Documentado.
- **Tres stores idénticos** (order/payment/reservation) → duplicación del cuerpo
  FileSystem. Justifican extraer un **`IJsonEntityStore(resourceType,key)` genérico** como
  próximo refactor (3 consumidores probados); se difirió para no re-tocar T1 en esta ola.
- **`ListAsync` O(n) por request** (aceptable a volumen demo; SQLite con índice es el
  camino de escala, detrás del mismo seam).
- **Ola B sin verificar:** la firma de integridad y el checksum de webhook de Wompi se
  escribirán desde supuestos no falseables sin llaves de sandbox — categoría de bug
  runtime-only; verificar contra el sandbox antes de declarar T3 completo.

**Notas de implementación:**

- `App_Data/syn-payments|reservations|payment-events/` en `.gitignore` (runtime, no
  commiteado). Single-instance (locks locales / `SemaphoreSlim`).
- Commits: `8ff9519` (T3 Ola A), `ff3378c` (4 fixes del review). 27 tests T3 verdes;
  suite 692/701 (los 9 rojos son pre-existentes: formato es-CO/ICU del entorno de test,
  confirmado por baseline — ajenos a T3).

## Alternatives considered

- **Wompi-first** (adapter real como entregable primario). Descartado: inverificable sin
  llaves; riesgo runtime-only alto; bloquearía la demo.
- **Solo simulación durable, sin webhook.** Descartado: el webhook es el riel de
  confirmación async que un PSP real exige; simularlo ahora lo deja probado y es infra
  reusable.
- **`FileSystemPaymentProvider` monolítico** (rehostear el motor en Web). Descartado:
  violaría ADR 0002; el seam de store apilado mantiene el motor en Application.
- **`IPaymentReconciler` domain-neutral** para el despacho del webhook. Descartado por
  ahora: abstracción prematura de una sola impl (Tienda) cuyos beneficiarios están
  bloqueados por su estado volátil; el controller llama `IShopOrderService` directo, se
  extrae al 2º vertical durable.
- **Ledger read-then-write** para idempotencia. Descartado: reintroduce el TOCTOU;
  `FileMode.CreateNew` es el candado atómico correcto.

## References

- doc rector `25` §6 (roadmap de transversales) + `scratchpad/t3-design.md` (diseño del
  workflow).
- Código: `Synergos.CMS.Interfaces/{IPaymentSessionStore,IReservationStore,IPaymentEventStore}.cs`;
  `Synergos.CMS.Application/Services/Impl/{StubPaymentProvider,StubReservationService,
  InMemoryPaymentSessionStore,InMemoryReservationStore,InMemoryPaymentEventStore}.cs`,
  `Configuration/{PaymentsSettings,PaymentSessionsSettings,ReservationsSettings}.cs`;
  `Synergos.CMS.Web/Services/{FileSystemPaymentSessionStore,FileSystemReservationStore,
  FileSystemPaymentEventStore,PaymentWebhookVerifier}.cs`,
  `Controllers/PaymentWebhookController.cs`, `Composers/{SeamComposer,OptionsComposer}.cs`.
- Memorias: `project_business_logic_t3_payments`, `project_business_logic_t1_t2`.
