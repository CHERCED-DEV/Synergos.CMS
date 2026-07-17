# ADR 0073 — i18n admin extension: +22 Dictionary keys (Olas 167-168)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

ADR 0061 introdujo 32 Dictionary keys para topbar nav + action buttons
+ landing strings. Las olas 144-145 + 153-156 agregaron nuevas vistas
(Members, Audit) con strings hardcoded — gap creciente vs i18n
coverage.

Las próximas direcciones de ADR 0061 listaron:
> Strings sin i18n: page subtitles + hint text que aparecen una vez
> — pueden extenderse si llega requirement en-US.

El "requirement en-US" implícito ya estaba en uso operativo (el
site_root de demos en inglés usaba en-US fallback). Bonificación:
mientras se hace el sweep, se cubren table headers + status labels
que se repiten en 3+ vistas (Members + Audit + future Members CRUD
destructivo).

## Decision

### Ola 167 — 22 nuevos Dictionary keys

4 categorías:

**Action labels** (2):
- `Admin.Action.Lock` / `Admin.Action.Unlock`

**Status labels** (6):
- `Admin.Status.Ok` / `Admin.Status.Locked` / `Admin.Status.Unconfirmed`
  (Members table).
- `Admin.Status.Success` / `Admin.Status.Failure` / `Admin.Status.Partial`
  (Audit outcomes).

**Column headers** (12):
- Members table: `Admin.Col.Email` / `Admin.Col.Name` / `Admin.Col.Roles`
  / `Admin.Col.LastLogin` / `Admin.Col.Created` / `Admin.Col.State` /
  `Admin.Col.Actions`.
- Audit table: `Admin.Col.When` / `Admin.Col.Actor` / `Admin.Col.Action`
  / `Admin.Col.Resource` / `Admin.Col.Outcome`.

**Pagination** (2):
- `Admin.Page.Previous` / `Admin.Page.Next` (reusable cross-views).

22 GUIDs verificados quad cero colisiones.

### Ola 168 — View refactors

**`Members.cshtml`**:
- 7 table headers (Email, Nombre, Roles, Último login, Creado, Estado, Acciones).
- 3 status labels (OK/Locked/Sin confirmar).
- 2 action button labels (🔒 Bloquear, 🔓 Desbloquear).
- 2 pagination labels (← Anterior, Siguiente →).

**`Audit.cshtml`**:
- 5 table headers (Cuándo, Actor, Acción, Recurso, Resultado).
- 3 outcome labels via switch dispatch (Success/Failure/Partial)
  preservando el class CSS.

Strings hardcoded sin extraer (low-frequency, página-específica):
- Page subtitles (e.g. "Listing read-only de Members registrados.")
- Hint text en filters
- Section panel titles especializados (e.g. "Webhook telemetry")

Extender estos cuando llegue requirement concreto.

## Consequences

**Positivas:**

- **Coverage canónica**: las strings que aparecen en 3+ admin views
  ahora vienen de Dictionary. Si se agrega una vista nueva, los
  table headers + pagination ya tienen i18n key.
- **Switch-on-culture funcional**: setear cultura en-US ya rinde admin
  legible (no perfecto — strings hardcoded restantes en español pero
  los high-frequency están).
- **Convención reusable**: pattern `admin.col.X` / `admin.status.X` /
  `admin.page.X` es discoverable. Future olas siguen mismo pattern.

**Negativas:**

- **Coverage selectivo**: page subtitles + hint text + panel titles
  siguen hardcoded. Para 100% coverage se necesitarían +30-50 keys
  más. Justificación: ROI vs effort no aún.
- **Switch dispatch en Razor**: el helper `outcomeLabel` switch en
  Audit.cshtml duplica el switch de outcomeClass. Refactor a una
  function helper sería overengineering — los 2 son distintos
  retornos.

**Neutras:**

- 1 commit feat batch (Olas 167+168) + 1 docs ADR.
- 22 GUIDs nuevos verificados quad-check.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 167 | 22 uSync Dictionary entries: 2 actions + 6 status + 12 columns + 2 pagination con traducciones es-CO + en-US. GUIDs verificados quad-check. |
| 168 | Refactors: Members.cshtml (14 strings) + Audit.cshtml (8 strings). |
| 0073 | (este) ADR consolidado |

## Próximas direcciones

- **Page subtitles + hint text**: cuando llegue audit/legal review en
  en-US, extender +20-30 keys más.
- **Webhook telemetry table headers**: la sección de Health.cshtml
  agregada en Ola 166 todavía tiene "Total/OK/Fail/P50/P95/P99/Última
  call" hardcoded. Extraer cuando se i18n el Health view.

## References

- ADR 0061 — i18n admin baseline (32 keys originales). Este ADR
  extiende.
- `feedback_no_preassigned_guids_usync` — pattern Flow B con quad
  verification de GUIDs.
