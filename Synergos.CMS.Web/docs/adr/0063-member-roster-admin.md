# ADR 0063 — Member roster admin (Olas 144-145)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Diferido §11.12 listaba **"Member roster admin view `/admin/members`
con last login + roles"** como item dentro del cap acotado a Ola 150.

Members runtime ya cubierto por ADR 0034 (self-service: register/login/
profile via `IMemberAuthService`) + ADR 0025 (`IMemberAccessGate` +
member gating en blocks). Faltaba la vista admin para que moderators/
editors vieran quién está registrado.

Constraint: **read-only** por diseño. El editor Members ya existe en
backoffice de Umbraco — duplicar Create/Update/Delete sería re-implementar
ese mismo UI peor. La vista admin solo lista.

## Decision

### Ola 144 — IMemberRosterReader seam

Nuevo seam en `Synergos.CMS.Interfaces`:

```csharp
public interface IMemberRosterReader
{
    MemberRosterPage GetRosterPage(int page, int pageSize, string? roleFilter = null);
    IReadOnlyList<string> ListAllRoles();
}
```

Records POCO sin tipos Umbraco:
- `MemberRosterItem(Guid Key, string Email, string DisplayName,
  IReadOnlyCollection<string> Roles, DateTime? LastLoginUtc,
  DateTime CreatedUtc, bool IsApproved, bool IsLockedOut)`
- `MemberRosterPage(IReadOnlyList<MemberRosterItem> Items, int Page,
  int PageSize, long TotalCount, string? RoleFilter)` con
  `TotalPages`/`HasNext`/`HasPrev` derivados.

### Ola 145 — Implementación Umbraco + view

`UmbracoMemberRosterReader` en `Synergos.CMS.Web/Services/` consume
`IMemberService.GetAll(pageIndex, pageSize, out totalRecords, orderBy,
direction, memberTypeAlias, filter)` con `orderBy="CreateDate"` desc.

Roles via `IMemberService.GetAllRoles(memberId)`. Filter por role se
aplica post-fetch en memoria (los roles no son indexados por
IMemberService, pero el dataset es típicamente < 1000 members).

Roles disponibles via `IMemberGroupService.GetAll()` para popular el
dropdown del filter.

Wired transient en `SeamComposer` (depende de scoped Umbraco services).

`AdminController.Members` nueva action `GET /admin/members`
member-gated `admin/moderator/editor` con paginación (`DefaultPageSize`
de `AdminSettings`) + roleFilter querystring.

`Members.cshtml` view con table (Email / Nombre / Roles / Último
login / Creado / Estado: OK | Locked | Sin confirmar).

Topbar entry "Miembros" agregado al `_AdminTopbar.cshtml` con
Dictionary key `admin.nav.members` (1 GUID nuevo verificado).

## Consequences

**Positivas:**

- **Visibilidad operacional**: moderator ve quién se registró
  recientemente, quién está locked-out, quién falta confirmar email.
- **Read-only por diseño**: zero risk de destructive bugs en members
  data desde el dashboard. CRUD sigue en backoffice donde el
  workflow editorial está validado.
- **Seam clean**: `IMemberRosterReader` aísla de `IMemberService`,
  manteniendo la regla del grafo (Application no referencia
  `Umbraco.Cms.*`). Si llegara una impl alternativa (caché, índice
  externo), swap sin tocar el dashboard.

**Negativas:**

- **Filter por role en memoria**: si el roster crece > 10k members,
  filtrar in-memory por role es lento. Mitigación: `IMemberService`
  no indexa roles natively; un futuro adapter sobre Examine
  index-of-members resolvería esto.
- **No expone last IP / device**: solo last login date. Para auditoría
  forense más rica habría que persistir IP + UA en un evento store
  (Ola futura).
- **Sin ordering switch**: hardcoded a CreateDate desc. Si los
  moderators piden filtrar por LastLoginDate o alfabético, agregar
  orderBy querystring.

**Neutras:**

- 1 commit feat batch (Olas 144+145 unificadas) + 1 docs ADR.
- 1 GUID nuevo (admin.nav.members) verificado.
- 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco |
|---|---|
| 144 | `IMemberRosterReader` seam + records POCO. |
| 145 | `UmbracoMemberRosterReader` impl + `AdminController.Members` action + `Members.cshtml` view + topbar entry + Dictionary key `admin.nav.members`. |
| 0063 | (este) ADR consolidado |

## Próximas direcciones

- **CRUD admin** (lock/unlock/delete/role-toggle): merece su propio
  ADR con threat model (qué moderator puede modificar a quién?).
- **Members search** (por email/name fragment): agregar parámetro
  `q` en `GetRosterPage`.
- **Members export CSV**: paralelo del CSV de form submissions
  (streaming via `Response.Body.WriteAsync`).
- **Audit trail**: registrar acciones admin (who-changed-what) con
  retention policy.

## References

- ADR 0034 — Member self-service runtime (`IMemberAuthService` +
  Account flow).
- ADR 0025 — `IMemberAccessGate` (donde se verifica role membership
  para el gating del dashboard).
- ADR 0051 — Admin moderation dashboard SSR (mismo pattern member-gated).
