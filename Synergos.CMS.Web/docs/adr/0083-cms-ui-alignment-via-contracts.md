# ADR 0083 — Synergos.CMS ↔ Synergos.UI alignment via contracts (Olas 211-220)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente.

## Context

Synergos.CMS y Synergos.UI han crecido como repos separados con
intersección en runtime — el CMS (Razor SSR) emite HTML con
`<synergos-X>` custom elements que el UI (Angular Web Components)
hidrata. Hasta cap-210, la "alineación" era implícita: ambos lados
asumían convenciones (prefix `synergos-`, naming `elementSyn*`) sin
documentación canónica.

Audit Explore agent (cap-220 prep) reveló mismatches concretos:

- ✅ Naming `synergos-*` MATCH end-to-end (71 elements alineados 1:1).
- ⚠️ CSS tokens: CMS publica 344 `--syn-*` tokens, UI usa SCSS local
  hardcoded sin contrato.
- 🚨 i18n bridge: UI no tiene; CMS tiene 369+ Dictionary keys.
- ⚠️ Outcome enum: UI `success: boolean` vs CMS `success|failure|partial`.
- 🚨 DOM events contract: UI no define eventos estructurados que el
  host pueda escuchar.

## Decision

Definir **5 contratos canónicos** documentados en
`Synergos.CMS.Web/docs/contracts/` que ambos lados implementan
independientemente. Cero shared code package — solo specs markdown.

### Las 5 superficies de alineación

| Contract | Owner | Consumer | Status cap-220 |
|---|---|---|---|
| 1. CDN bundle registry | CDN team | CMS reads | ⏸️ Bloqueado externo (proposal en `cdn-contract.md`) |
| 2. DOM events `syn:*` | UI emits | CMS subscribes opt-in | ✅ Spec shipped (`dom-events.md`) |
| 3. CSS tokens `--syn-*` | CMS publishes | UI consumes con fallback | ✅ Spec + UI fallback shipped |
| 4. i18n bridge `window.synergos.i18n` | CMS server-side resolves | UI client-side lookups | ✅ Spec + impl ambos lados |
| 5. Host bridge `window.synergos` (full) | CMS injects | UI consumes via helpers | ✅ Spec + impl ambos lados |

### Principios

1. **Cero code-share entre repos** — no shared NuGet/npm package.
2. **Source of truth** del schema vive en CMS uSync XMLs.
3. **Naming conventions canónicas** documentadas en
   `docs/contracts/README.md`.
4. **Versioning** explícito en cada contract (`Contract version: vN`).
5. **Backward compatibility** mandatoria — additive changes solo.
6. **Standalone-safe** UI — todos los helpers degradan graceful si
   `window.synergos` undefined (Storybook, dev preview).

## Implementation

### Olas 211-215 — Specs en `Synergos.CMS.Web/docs/contracts/`

5 docs nuevos (~1300 líneas de spec):

- **`README.md`** — Premisa + tabla de 5 contratos + naming conventions
  canónicas (custom elements, schemas, tokens, Dictionary keys,
  CustomEvents, window namespace) + reglas no-acoplamiento.
- **`dom-events.md`** — Convention `syn:{component}:{event}` con
  bubbles+composed. Lifecycle events (`ready` / `error`). Catálogo
  per categoría (action / form / disclosure / media). Outcome
  tri-state. Standard listener pattern CMS-side.
- **`css-tokens.md`** — 6 categorías (color/space/typography/border/
  radius/shadow/z-index). Theme override via `[data-theme]` attribute.
  Fallback strategy `var(--syn-X, defaultValue)` mandatoria. Lista MVP
  de tokens minimum-viable.
- **`i18n-bridge.md`** — `window.synergos.i18n` shape + `t(key, fallback)`
  resolution order. Subset publishing config. `{0}` placeholder
  convention.
- **`host-bridge.md`** — Big picture end-to-end. `window.synergos` shape
  completa (i18n + theme + brand + member + page + version).
  Init lifecycle T+0ms→T+180ms. Failure modes + degradación graceful.
  Performance budget (bridge < 4KB). Security notes.

### Ola 216 — CMS-side implementation

- **`IHostBridgeContextBuilder`** seam (Synergos.CMS.Interfaces) +
  records POCO (HostBridgeContext + I18n + Theme + Brand + Member + Page).
- **`HostBridgeSettings`** POCO (Application/Configuration) con
  I18nKeyPrefixes subset (Form/Search/Common/Comments/Cart/Shop/etc.,
  no Admin keys porque admin dashboard es Razor SSR puro), bool flags
  IncludeMember/IncludePage, ContractVersion.
- **`DefaultHostBridgeContextBuilder`** impl consumiendo
  IBrandingProvider + IPageRenderContextResolver + IMemberAccessGate +
  IUmbracoContextAccessor + ILocalizationService. Best-effort try/catch
  en BuildI18n.
- **`_SynergosBridge.cshtml`** partial serializa context a JSON camelCase
  + emite `<script>window.synergos = {...}</script>` con `t(k, f)` helper.
- Wired en `_Layout.cshtml` ANTES de bundle scripts (init order
  garantizado).

### Olas 217-218 — UI-side implementation

- **`platforms/angular/libs/shared/src/styles/_tokens-bridge.scss`**
  declara `--syn-*` con fallbacks defensivos para standalone runs.
  6 categorías + 2 theme overrides + reduced-motion.
- **`vitals/contracts/src/host-bridge.contract.ts`** mirror del shape
  de `window.synergos` con interfaces TypeScript readonly. Type alias
  `SynergosOutcome = 'success' | 'failure' | 'partial'`.
- **`vitals/contracts/src/form.contract.ts`** extended:
  `FormSubmissionResult.outcome` tri-state + `partialErrors[]`.
  Backward-compat con `success: boolean`. Helper `resolveFormOutcome()`.
- **`vitals/core/src/bridge/synergos-bridge.ts`** helpers:
  `getBridge()`, `t(key, fallback, ...args)`, `getMember()`,
  `getBrand()`, `getTheme()`, `getPage()`, `hasAnyRole()`,
  `getBridgeVersion()`. Standalone-safe — todos retornan defaults
  graceful.

## Consequences

**Positivas:**

- **Mirror real**: ambos lados pueden vivir en repos separados
  independientes — la única superficie de coupling son los 5 contratos
  markdown + el shape `window.synergos` runtime.
- **KISS**: cero magic, cero shared code package, cero gRPC/protobuf.
  Solo `<script>window.synergos = {...}</script>` inline + JS helpers.
- **SOLID**: ISP — cada contrato es una abstracción específica
  (i18n / tokens / events / etc.). DIP — UI consume via helpers que
  abstraen el lookup.
- **Clean**: cada contrato versioned. Cambios additive sin breaking.
  Major bumps requieren ADR superseding.
- **Standalone preview funcional**: UI components corren en Storybook /
  dev preview con tokens fallback + helpers safe-default. No requieren
  CMS host activo para iterar.
- **Audit trail alineado**: Outcome tri-state matches CMS audit events
  (`IAuditTrailWriter.AuditEvent.Outcome`).

**Negativas:**

- **Doc maintenance**: 5 contracts requieren update manual cuando algo
  cambia. Mitigation: cada cambio de contract = nuevo ADR.
- **No type-checking cross-repo**: TypeScript types en UI no validan
  contra C# types del CMS al compile time. Solo runtime validation.
  Mitigation: shape canónico documentado + tests integration deferred.
- **Bundle size de los keys**: si el CMS publica todas las 369 keys
  cada page-load, suma ~50KB. Mitigation: I18nKeyPrefixes subset
  default solo publica las relevantes a UI hidratable.
- **CSP inline scripts**: `<script>window.synergos = {...}</script>`
  inline requiere CSP `'unsafe-inline'` o nonce. Mitigation
  documentada en host-bridge.md, future endpoint-eq deferred.

**Neutras:**

- 1 commit feat batch CMS (216) + 1 commit feat batch UI (217-218)
  + 1 commit docs CMS (211-215) + 1 commit ADR (este).
- 0 GUIDs nuevos, 0 NuGet packages nuevos.
- 0 schema rompedor.

## Implementation summary

| # | Foco | Repo | Commit |
|---|---|---|---|
| 211-215 | 5 contracts specs en `docs/contracts/` | CMS | `db002b2` |
| 216 | IHostBridgeContextBuilder + DefaultBuilder + Settings + partial + wire | CMS | `6270dda` |
| 217 | `_tokens-bridge.scss` con fallbacks 6 categorías + themes | UI | `7439361` |
| 218 | `host-bridge.contract.ts` + `form.contract.ts` outcome tri-state + `synergos-bridge.ts` helpers | UI | `7439361` |
| 219-220 | (este) ADR + current-state §11.19 + memory |

## Próximas direcciones

- **Sync script tokens**: generar `_tokens-bridge.scss` automáticamente
  desde `wwwroot/css/syn-tokens.css` para evitar drift manual.
- **Component contract tests**: package `@synergos/contract-tests`
  validating cada custom element fires `syn:component:ready` post-hydrate.
- **CSP-strict mode**: serve `/synergos-bridge.js` endpoint en lugar
  de inline script (cuando llegue requirement de CSP estricto).
- **Bridge hot-reload**: si culture/theme cambia mid-session, push update
  via `BroadcastChannel` o similar (deferred — page reload aceptable hoy).
- **Contract version negotiation strict**: UI valida bridge.version
  contra expected major y warns/blocks si mismatch.

## References

- ADR 0012 — CDN contract is consumed, not owned (relacionado).
- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0061 — i18n admin baseline (Dictionary keys).
- ADR 0067 — IAuditTrailWriter (outcome enum source).
- `Synergos.CMS.Web/docs/contracts/README.md` — index canónico.
- `feedback_synhost_naming_convention` (memory).
- `feedback_framework_agnostic_integration` (memory).
- `feedback_cdn_integration_is_core` (memory).
