# ADR 0061 — i18n admin baseline (Olas 139-141)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 3 olas en un único ADR.

## Context

El admin dashboard `/admin` (cubierto por ADRs 0051, 0053, 0054, 0055,
0056, 0058) tenía sus strings hardcoded en español dentro de los
templates Razor. El diferido §11.12 listaba "i18n admin baseline via
Dictionary keys uSync" para cerrar la gap.

Convención del codebase ya establecida (ADRs 0024, 0033, 0040): los
templates públicos (PostPage, ProductPage, _Layout) consumen
`@Umbraco.GetDictionaryValue("flow.start", "Comenzar")` con fallback
ES, schema authoring via uSync XML.

## Decision

### Ola 139 — uSync Dictionary tree para Admin

Top-level Dictionary alias **"Admin"** (parent) + 32 children con
GUIDs verificados quad cero colisiones. Cubre:

- **Topbar nav** (8): `Admin.Brand`, `Admin.Aria.Nav`, `Admin.Nav.{Home,Moderation,Forms,Search,Webhooks,Health,Members,PendingAria}`.
- **Action buttons** (13): `Admin.Action.{Approve,Reject,Spam,Delete,Cancel,Confirm,Undo,Export,BulkApprove,BulkReject,ApplyFilters,Clear,Test}`.
- **Landing strings** (10): `Admin.Welcome`, `Admin.Subtitle`,
  `Admin.Cards.{Moderation,Forms,Search}.{Label,Empty,QueueClean}`,
  `Admin.TopQueriesPanel.Title`.
- **(+1 Members nav)** que llegó después en Ola 145.

Cada entry con traducciones es-CO + en-US.

### Ola 140 — Topbar + Index refactor

`_AdminTopbar.cshtml` y `Index.cshtml` consumen
`@Umbraco.GetDictionaryValue("admin.X", "fallback ES")` con los
strings actuales como fallback. Antes del uSync Import, sigue
funcionando con los fallback strings — zero regresión visual.

### Ola 141 — Common action buttons sweep

Refactor de **6 admin views** (`ModerationComments`,
`FormSubmissions`, `FormSubmissionDetail`, `WebhookTestHarness`,
`Members.cshtml` que llegó en Ola 145, y `_AdminTopbar.cshtml`):

- Approve / Reject / Spam button labels.
- Bulk approve / bulk reject button labels.
- Cancel / Confirm / Delete dialog buttons.
- Clear / Apply filters / Export CSV / Test trigger labels.
- Undo flash button.

Un strings hardcoded selecto (page subtitles largas, hint text,
panel titles especializados) se mantiene en Razor — el costo de
extraer todo no está justificado para strings que aparecen una vez.

## Consequences

**Positivas:**

- **i18n switchable runtime**: arquitecto activa en-US como cultura
  default y los admins ven UI en inglés sin recompilar. Aceptable
  para el target market (Colombia primary, US secondary).
- **Zero regresión visual** durante el rollout: fallback strings ES
  son idénticos a los hardcoded actuales. uSync Import puede
  realizarse en cualquier momento sin coordinación.
- **Convención canónica establecida**: future olas que agreguen
  admin views sigan el pattern `admin.section.label` con fallback
  ES.

**Negativas:**

- **32 archivos uSync nuevos**: requiere un solo `uSync Import` para
  materializar. Los entries son leaf-only (no relations), import
  rápido.
- **String duplication runtime**: los fallback strings en Razor
  duplican el ES de los `<Translation>` XMLs. Aceptable — los
  fallback son safety net para un escenario donde Umbraco
  Dictionary no responde (DB unreachable, etc.).
- **Selective coverage**: no todos los strings están i18nados.
  Próximas olas pueden extender si llegan requirements en-US.

**Neutras:**

- 1 commit feat batch (Olas 139+140+141 unificadas) + 1 docs ADR.
- 32 GUIDs nuevos verificados quad-check.
- 0 NuGet packages nuevos.
- 0 schema rompedor (Dictionary entries son additive).

## Implementation summary

| # | Foco |
|---|---|
| 139 | 32 uSync Dictionary entries: parent `Admin` + 31 children con traducciones es-CO + en-US. |
| 140 | `_AdminTopbar.cshtml` (8 strings) + `Index.cshtml` (8 strings) refactor a `@Umbraco.GetDictionaryValue`. |
| 141 | Sweep en 4 views adicionales (`ModerationComments`, `FormSubmissions`, `FormSubmissionDetail`, `WebhookTestHarness`) cubriendo action buttons reusables. |
| 0061 | (este) ADR consolidado |

## Próximas direcciones

- **Admin Members view** (Ola 144-145, ADR 0063) sumó +1 dictionary
  key adicional `admin.nav.members` con su GUID propio.
- **Strings sin i18n**: page subtitles + hint text que aparecen una
  vez — pueden extenderse si llega requirement en-US.
- **Dictionary admin completo**: si el target market expande, esta
  cobertura puede ampliarse a 100+ keys (status labels, table
  headers, accessibility aria-labels especializados).

## References

- ADR 0024 — Pages mínimas + descripciones editor-facing (mismo
  pattern @GetDictionaryValue con fallback).
- ADR 0033 — SEO infrastructure (Dictionary keys flow.* + blog.* +
  search.* establecidos).
- `feedback_no_preassigned_guids_usync` — pattern Flow B (agente
  escribe XML con GUID fresco verificado, arquitecto corre Import).
