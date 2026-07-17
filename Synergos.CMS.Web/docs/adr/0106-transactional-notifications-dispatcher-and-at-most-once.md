# ADR 0106 — Notificaciones transaccionales: un dispatcher transversal + idempotencia at-most-once (T4, doc 25)

- **Status:** Accepted
- **Date:** 2026-07-16
- **Deciders:** Arquitecto + agente, fase de lógica de negocio (doc rector `25`, transversal T4). Diseño producido por un workflow multi-agente (panel de jueces sobre 3 formas: dispatcher genérico / eventos tipados / outbox+worker). Verificado end-to-end con **entrega real** (archivos `.eml` en disco), no con logs.
- **Relacionados:** ADR 0105 (`IJsonEntityStore` — el precedente de generalizar al 2º consumidor, que este ADR aplica al ledger), ADR 0104 (T3 — introdujo el ledger create-exclusivo y el webhook que provoca la doble confirmación), ADR 0002 (Application sin Umbraco/AspNetCore), ADR 0044 (render de plantillas de email), ADR 0075 (tests por seam), ADR 0013 (sin I/O en boot). Regla de oro doc 25: ninguna capacidad transversal se implementa dos veces.

---

## Context

Los 5 verticales transaccionales ya eran durables (T1/T3 + fan-out), pero **no le avisaban
a nadie**: el comprador no recibía recibo, el ciudadano no recibía su radicado, el alumno
no sabía que su matrícula quedó activa. El doc 25 (§6) define T4 como "Notificaciones
(email/SMS/push) a eventos de dominio", P1.

Tres premisas del encuadre inicial resultaron **falsas** al leer el código, y cambian el
plan por completo:

1. **No hay muro de credenciales.** Se asumió, por analogía con Wompi (T3), que el envío
   real exigiría SMTP y quedaría gated. Falso: Umbraco 13 acepta
   `DeliveryMethod: SpecifiedPickupDirectory` y escribe el `.eml` a disco **sin host ni
   secretos**. T4 tiene meta dura verificable hoy con 3 líneas de config.
2. **Los logs mentían.** `DefaultEmailService` sí llamaba al `IEmailSender` de Umbraco,
   pero éste, **sin transporte configurado, hace `LogDebug` + `return` silencioso — no
   lanza**. El servicio igual logueaba `"Email sent"`. Un `grep` habría declarado T4 verde
   con **cero entregas**.
3. **El render de emails estaba roto para TODOS sus consumidores, en silencio.**
   `RazorEmailTemplateRenderer` es Singleton y pasaba el **root provider** como
   `RequestServices`; Razor necesita servicios *scoped* (`IViewBufferScope`) → *"Cannot
   resolve scoped service from root provider"* en cuanto la validación de scopes está
   activa (Development la activa por defecto). Password reset, formularios,
   cart-abandonment y moderación **nunca renderizaron en dev**; sus callers envuelven en
   `catch` + `LogWarning`, así que el fallo era invisible.

Además, el riesgo central es de **correctness**, no de infraestructura: `ConfirmAsync` es
idempotente y se ejecuta **más de una vez por diseño** — el comprador vuelve del redirect y
llama `/confirm` mientras el PSP postea el webhook. Sin dedupe, dos recibos.

## Decision

### Un dispatcher transversal, no un notifier por dominio

- **`ITransactionalNotifier`** (Interfaces) + marker **`ITransactionalNotifierChannel`** +
  **`NotificationEvent`** genérico polimórfico (`Type` string **abierto**: el dispatcher no
  conoce ningún dominio) + `NotificationTypes` (los 6 hechos). Se descartan 6 records
  tipados por dominio: pagan ~19 artefactos donde el genérico paga 3 y encarecen el
  plegado futuro de las 4 familias de notifier de ops.
- **`CompositeTransactionalNotifier`** (Web) es la única impl y el **único dueño del gating
  y de la idempotencia** — replicarlos en los 6 motores sería la duplicación por-dominio
  que la regla de oro prohíbe. Abanica a los canales con aislamiento **por canal** y es
  **TOTAL: nunca lanza**.
- **`EmailTransactionalNotifier`** es el único canal (calca `EmailCartAbandonmentNotifier`).
  SMS/push = un `AddSingleton` más, sin tocar motores ni dispatcher. **No** se clonan los 5
  transportes de ops (Slack/Discord/Teams/Webhook): nadie pidió un recibo por Discord.
- **Un mapa de copy es-CO `Type → copy` + UNA plantilla data-driven** — no una plantilla por
  vertical. Un `Type` sin copy cae a un fallback genérico con `LogWarning`: nunca un hueco
  silencioso ni una excepción.
- **Emisión inline** desde los motores (el idioma ya vivo de `_tracking`/`_audit`), con el
  notifier como **último parámetro opcional** del ctor → cero call-sites rotos.

### Idempotencia: se marca ANTES de enviar (at-most-once)

**Es una elección irreducible y se declara explícita.** Con un archivo y un email no existe
exactly-once. Marcar *después* sería at-least-once y **no** protege de la carrera —que aquí
es la condición **diseñada**—, produciendo el segundo recibo. Se elige **at-most-once**
porque:

- un recibo **duplicado no tiene mitigación** (no se des-envía un email);
- un recibo **perdido sí la tiene**: el historial durable (T1/T2) y la **re-emisión en los
  short-circuits** de los motores, que el ledger deduplica.

La clave es el **HECHO DE NEGOCIO**, no la entrega del transporte, y vive en scope propio
(`"notifications"`): `(provider,eventId)` sería la clave equivocada porque `/confirm` **no
tiene eventId**, dos eventos del PSP sobre la misma orden pasarían ambos, y el webhook ya
gasta esa clave. Tres hechos exigen clave compuesta:

| Hecho | Clave | Por qué |
|---|---|---|
| `travel.order.confirmed` | `…:{orderRef}:{status}` | Partial y Confirmed son hechos distintos; el 2º email es legítimo |
| `academy.enrollment.active` | `…:{courseId}:{email}` | el enrollmentId es un Guid fresco por llamada — no identifica "este alumno en este curso" |
| `gov.case.decided` | `…:{radicado}:{status}` | un email por transición, no por caso |

### Aislamiento de fallos: estructural, no confiado

`NotificationEmission.SafeDispatchAsync` (Application) es el **único** punto de emisión: la
garantía *"un email caído JAMÁS tumba una orden pagada"* no depende de la disciplina del
dispatcher (eso es disciplina, no compilador) ni se copia en los 7 call-sites. No traga la
cancelación. **Nota:** los seams `_tracking`/`_audit` existentes **no** tienen esta guarda —
hoy un audit caído sí tumba la transacción. T4 se desvía a propósito.

### El ledger, generalizado (aplicando ADR 0105)

`IPaymentEventStore` → **`IIdempotencyLedger.TryClaimAsync(scope, key)`**, domain-neutral.
T4 es su 2º consumidor, así que se generaliza **antes** de duplicar. Sigue **fuera** de
`IJsonEntityStore` (upsert, sin compare-and-swap): la familia de storage es *"uno por
PRIMITIVA"*. Rutas y extensión `.txt` preservadas.

## Consequences

**Positivas:**

- **El negocio por fin le habla al usuario**, y con entrega verificable hoy: `.eml` real,
  cero secretos. Un vertical nuevo notifica inyectando un seam y declarando un `Type`.
- **Tres bugs latentes reales corregidos** (el email que mentía, el renderer roto para
  todos, las 6 plantillas filtrando el layout del sitio) — los tres invisibles a build y
  tests, los tres tapándose entre sí.
- **Saldo de duplicación negativo**: borra el ledger dedicado, 1 plantilla para 6 hechos,
  1 dispatcher para 5 verticales.
- **Opt-in**: `Enabled=false` por default → sin config el sistema se comporta exacto como
  antes de T4.

**Negativas o trade-offs:**

- **at-most-once**: un fallo del transporte tras reclamar la clave pierde ese aviso, sin
  reintento. Es la cara que se eligió pagar (ver arriba). Un outbox+worker con reintentos
  se descartó: su claim protege el *encolado*, no el *despacho* — reintroduce el duplicado
  en el eje inaceptable, y cuesta ~2× para un demo.
- **Stringly-typing**: los extras viajan en `Data<string,string>` sin seguridad de
  compilación, y el `ViewName` por copy es un punto de extensión que, mal usado, podría
  reintroducir N plantillas.
- **El brand en el camino webhook** (el comprador cerró el navegador) cae al genérico: el
  canal resuelve `IBrandingProvider` en vivo. Irrelevante en single-siteRoot; en
  multi-siteRoot exigiría sellar el brandKey en los checkouts.
- **Código human-facing pobre en Educación**: hoy usa el `enrl_{guid}` como "Matrícula".
  Merece un código corto derivado, como el resto de verticales.
- **`_tracking`/`_audit` siguen sin guarda** — deuda conocida, fuera de alcance.

**Notas de implementación:**

- **Verificar SOLO con el `.eml`, jamás con logs** (regla que este ADR deja escrita).
- Maildrop **fuera del repo** (`~/Desktop/synergos-maildrop`): los `.eml` llevan PII, mismo
  criterio que los backups SQLite. Solo config de Development.
- Commits: `eafb875` (ledger), `ac7d76d` (riel + Tienda), `e265348` (los 5 hechos).
  Suite 698/707 (los 9 rojos son pre-existentes: formato es-CO/ICU del entorno).

## Alternatives considered

- **Outbox durable + worker** (puntuó más alto en el panel, 85). Rechazado con argumento:
  su claim protege el encolado, no el despacho → un crash entre `SendAsync` y el write de
  `Sent` produce el SEGUNDO recibo, convirtiendo at-most-once en at-least-once justo donde
  no se puede. Además ~2× de código (hosted service, máquina de estados, backoff,
  dead-letter, retención) y latencia de hasta un ScanInterval, para un demo sin SMTP real.
- **Records de evento tipados por dominio** (84). Rechazado: misma capacidad, 19 artefactos
  vs 3.
- **Un notifier por dominio** (calcando las 4 familias de ops). Rechazado: es exactamente la
  duplicación que ADR 0105 acaba de colapsar en storage.
- **Marcar después de enviar** (at-least-once). Rechazado: el duplicado no tiene mitigación.
- **Emitir por-asistente en Eventos**. Rechazado: solo `attendees[0]` tiene email validado.

## References

- doc rector `25` §6 · `scratchpad/t4-design.md` (diseño completo del workflow).
- Código: `Synergos.CMS.Interfaces/{ITransactionalNotifier,IIdempotencyLedger}.cs`;
  `Synergos.CMS.Application/{Configuration/NotificationsSettings.cs,Services/Impl/NotificationEmission.cs}`;
  `Synergos.CMS.Web/Services/{CompositeTransactionalNotifier,EmailTransactionalNotifier,TransactionalEmailCopy,FileSystemIdempotencyLedger,RazorEmailTemplateRenderer,DefaultEmailService}.cs`;
  `Views/Emails/Transactional.cshtml`.
- Memorias: `project_business_logic_t4_notifications`, `feedback_email_verification_eml_not_logs`.
