# ADR 0097 — Módulo Dashboard analítico (ventas + progreso de usuarios + comportamiento)

- **Status:** Proposed (proyección — segundo consumidor del patrón module-mount)
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, fase SynergosLabs. Diseño verificado por workflow multi-agente contra código vivo.
- **Depende de:** ADR 0096 (module-mount). Sinergia opcional con ADR 0095 (capa IA).

## Context

El arquitecto quiere "un dashboard real con métricas y comportamiento de cómo van
las ventas y el progreso de usuarios". Es el **segundo consumidor** del patrón
module-mount (ADR 0096): mismo `elementSynModuleMount` (`moduleAlias="dashboard"`),
app Angular completa (`<synergos-dashboard>`, Tier=module/experience), datos por
`/api/dashboard/*` detrás de seams.

**Hechos verificados contra código vivo:**

1. El `AdminController` (`/admin`, gated `admin,moderator,editor`) **YA es un
   dashboard SSR vivo**: consume `ISearchAnalyticsStore` + `IFormSubmissionReader`
   + `IMemberRosterReader` + `IAuditTrailWriter` + `IAnalyticsTracker`. El módulo
   Angular leería las **mismas fuentes** → hay coexistencia que resolver.
2. Las fuentes de comportamiento son hoy **efímeras**: `LoggerAnalyticsTracker`
   solo loguea; `InMemorySearchAnalyticsStore` / `InMemoryWebhookTelemetryStore` se
   pierden en restart. **No existe persistencia de conversiones/revenue.**
3. `DefaultCartService.Clear()` solo llama `MarkCompleted` — **no registra venta**
   (el comentario inline dice *"Clear puede significar checkout completado"*).
   Acoplar revenue a un método de borrado = falsos positivos.
4. `IAnalyticsTracker.Track()` es `void` fire-and-forget; `LoggerAnalyticsTracker`
   es `sealed`, registrado `AddSingleton` en `SeamComposer`. Umbraco DI **no** trae
   Scrutor → no asumir `.Decorate()`.

## Decision

El reto no es CRUD sino **agregación barata**: una capa de **proyección
pre-agregada** (el costo se paga O(1) en la escritura del evento; la lectura es
O(buckets-en-rango), nunca re-escanea eventos crudos).

### 1. Seams nuevos (Interfaces, puros; 4 tests canónicos cada uno)

- **`ICheckoutRecorder`** — `Record(CheckoutCompleted)` append-only (orderId,
  lineItems, subtotal, currency, occurredUtc). Cierra el gap #3. **Se cablea en un
  punto de checkout EXPLÍCITO, NO en `Clear()`** (corrige #3). Hasta que ese punto
  exista (idealmente lo aporta Ecommerce), **degradación honesta**: el panel ventas
  muestra solo abandonment + carts activos, no revenue falso.
- **`IMetricsProjectionStore`** — `RecordEvent(MetricEvent)` (incremento O(1) de
  bucket día/hora por slug) + `GetSeries(slug, from, to, granularity)` +
  `GetTotals(...)`. Impl en Web: `ConcurrentDictionary` en-memoria + flush a JSONL +
  **rehidratación en boot** → sobrevive restart (cierra #2).
- **`IDashboardReadModel`** — fachada de lectura que compone fuentes y entrega
  ViewModels listos para charts (`GetSalesOverview`, `GetMemberProgress`,
  `GetBehavior`, `GetSearchInsights`, `GetWebhookHealth`, `GetAgentInsights`).
- **`IMetricsExporter`** — `ExportCsvAsync(...)` streaming (espejo de
  `AdminController.ExportFormSubmissions`).

### 2. `ProjectingAnalyticsTracker` (decorator) — guardrail DURO

Segundo listener de `IAnalyticsTracker` que delega al logger existente **y** proyecta
al store. Cero cambio en los callers (ya emiten 11 slugs). **Reglas no-negociables
(corrige #4):** `Track` debe ser O(1) en-memoria, **try-catch-swallow**, CERO IO
inline, CERO await — jamás contender el hot-path de `ShopController`/`SearchController`.
El flush a disco vive SOLO en `DashboardSnapshotFlushHostedService`. Como Umbraco DI
no trae Scrutor, el decorator se compone **manualmente** en `SeamComposer`
(construye el logger interno y lo envuelve). Tests obligatorios: "Track no lanza
aunque el store falle" + "Track no hace IO síncrono".

### 3. Métricas (qué responde cada panel, reutilizando seams vivos)

| Panel | Fuente |
|---|---|
| **Ventas** | revenue series / AOV / top products (`ICheckoutRecorder` agregado) + abandonment rate (`ICartAbandonmentTracker` + checkouts) |
| **Progreso de usuarios** | signups/día + active (`IMemberRosterReader.CreatedUtc`/`LastLoginUtc` bucketizado) + conversión signup→compra + % con 2FA + por rol |
| **Comportamiento** | eventos por slug (`IMetricsProjectionStore`) + funnel cart (added→abandoned→completed) + submissions/día (`IFormSubmissionReader`) |
| **Búsqueda** | top queries + top no-result (`ISearchAnalyticsStore` directo) |
| **Webhooks** | channel health (`IWebhookTelemetryStore` directo, ya agregado) |
| **Insights** | sugerencias del agente (ADR 0095) — **feature-flagged off por defecto** |

### 4. Coexistencia con `/admin` SSR (corrige #1)

`/admin` (AdminController) **es la superficie de degradación canónica** (no-JS /
CDN-caído); `<synergos-dashboard>` Angular es la superficie rica. **Ambos leen el
mismo `IDashboardReadModel`** — cero lógica de lectura duplicada, cero verdades
divergentes. El `<noscript>` del module-mount apunta a `/admin`. (A futuro `/admin`
puede reescribirse sobre el read-model; decisión abierta D4.)

### 5. Render Angular

App standalone en Shadow DOM (Angular Elements): shell con sidebar (6 paneles) +
barra de filtro de **rango** (7/30/90/custom) y **granularidad** (día/hora) como
estado global (signal); KPI cards con sparkline + delta vs período anterior; cada
panel = ruta lazy (`loadComponent`) → code-splitting (solo `main.js` al inicio).
**Charts**: recomendado **ngx-charts** (SVG, declarativa, theming por
`var(--syn-*)`, tree-shakeable) — **condicionado al POC tokens→Shadow DOM de ADR
0096**. Cero color hardcoded.

### 6. Seguridad / privacidad

- Auth-gate en dos capas: `compMemberGating allowedRolesCsv="admin"` (SSR) +
  `IMemberAccessGate.HasAnyRole("admin")` por endpoint (primer paso). Sin ownership
  por-recurso (no hay PHI). UI gate = cosmético.
- **Sin PII**: el read-model sirve agregados; ADR 0037 ya omite identidad de usuario.
  Los charts usan counts/buckets. El único punto con PII es el roster (ya en `/admin`).
- Audit: `dashboard.viewed` / `dashboard.exported` (slugs nuevos, cero cambio al seam).
- `siteRootKey`: **single-origin** (Decisión D1 de ADR 0096) — sin partición; el
  dashboard sirve el deploy completo.
- Persistencia: filesystem JSONL `App_Data/syn-dashboard/{orders,projections}/`,
  **escritura atómica** (temp+`File.Move`, regla de ADR 0096). Retención normal vía
  `DashboardSnapshotRetentionPolicy` (default 730 días) sobre `IRetentionPolicy`
  existente.

### 7. Sinergia con ADR 0095 (sin acoplamiento duro)

El dashboard ya tendrá top-no-result-queries + funnel → es **exactamente el insumo**
del batch-learning de 0095. `GetAgentInsights` lee structured outputs si existen
(`App_Data/syn-ai/insights/`); si 0095 no está construido, el panel se oculta vía
`showAgentInsights=false`. **Recomendación de orden: el dashboard se construye antes
de ejecutar 0095** — el read-model agregado es precondición natural de la superficie
de revisión humana del agente.

## Phases

| Fase | Entregable | Verificable |
|---|---|---|
| **D0** | Este ADR + índice §11.2. | ADR mergeado. |
| **D1** | `ICheckoutRecorder` + `IMetricsProjectionStore` + `ProjectingAnalyticsTracker` (decorator manual) + flush HostedService + rehidratación. Cablear captura en punto de checkout explícito (no `Clear()`). | Build 0 CS; un checkout simulado aparece en `orders/`; un evento incrementa un bucket que sobrevive restart; "Track no lanza/ no bloquea". |
| **D2** | `IDashboardReadModel` + `IMetricsExporter` + `DashboardApiController` (8 endpoints) auth-gate admin + audit. | Endpoints gated; series agregadas correctas; tests de endpoint (anónimo→401, no-admin→403). |
| **D3** | `cfgDashboardSettings` (uSync quad-check) + nodo Settings + `compMemberGating admin` + `moduleAlias="dashboard"`. | No-admin bloqueado SSR; admin ve el shell; config llega al módulo. |
| **D4** | App Angular real: shell + filtros + KPI cards + paneles sales/members/behavior/search/webhooks con charts, lazy, tema por tokens, CustomEvents, `<noscript>`→`/admin`. | `synergos-smoke-test`: hidrata, no queda como comentario HTML, charts renderizan contra API, filtros re-disparan fetch. |
| **D5** | Export CSV + `DashboardSnapshotRetentionPolicy` + panel Insights (oculto si 0095 no existe). | Export baja CSV; retención idempotente. **El panel insights NO es criterio de ola** hasta que 0095 produzca outputs. |
| **D6 (opcional)** | Real-time (SignalR push de KPI deltas). | Diferido — el polling por rango cubre el caso base. |

## Consequences

**Positivas:** dashboard real reutilizando seams vivos; proyección pre-agregada
evita matar el disco; `/admin` SSR y Angular comparten read-model (una sola verdad);
prepara los datos para la capa IA.

**Costos/riesgos:** `ICheckoutRecorder` bloquea el bloque "ventas" hasta tener punto
de checkout (degradación honesta hasta entonces); el decorator de tracking toca un
hot-path (guardrail duro + tests); coexistencia con `/admin` debe mantenerse
(read-model compartido, no copiar lógica).

## Decisiones abiertas

- **D3 — charting lib**: ngx-charts (recomendado) vs Chart.js — tras el POC de tokens.
- **D4 — `/admin` futuro**: ¿reescribir sobre el read-model o dejarlo como está?
- **D5 — punto de captura de conversión**: introducir `ICartService.CompleteCheckout`
  ahora vs esperar a Ecommerce. Recomendado: método explícito en el seam de cart.

## Relación con otros ADRs

Depende de 0096. Extiende 0037 (analytics), 0067 (audit), 0070/0088 (retención),
0072 (webhook telemetry), 0031/0032 (search analytics). Coexiste con
0051/0053 (dashboard admin SSR). Sinergia con 0095. Comparte con Ecommerce el
gap de captura de conversión.
