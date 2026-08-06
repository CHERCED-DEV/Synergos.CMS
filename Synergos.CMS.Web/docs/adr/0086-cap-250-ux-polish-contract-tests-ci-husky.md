# ADR 0086 — Cap-250: UX polish + contract tests expansion + CI gating + Husky (Olas 241-250)

- **Status:** Accepted
- **Date:** 2026-04-28
- **Deciders:** Arquitecto + agente.

## Context

Cap-240 cerró 4 deferred items concretos pero dejó una lista de 14
items en §11.21. Los más baratos y de mayor user-value:

1. GDPR RTBF UI button — el endpoint del Cap-240 Batch B existía
   pero requería POST manual (curl/Postman). El operador
   compliance no debería tocar curl para procesar un RTBF.
2. Contract tests cobertura — el skeleton del Cap-240 Batch D solo
   cubría `dom-events.md`. Los otros 3 contratos (host-bridge,
   i18n-bridge, css-tokens) quedaban sin spec verification.
3. CI gating ausente — el harness era opt-in manual. Risk de
   contract drift sin gate automático.
4. Sync tokens manual — el script `sync:tokens` regenera
   `_tokens-bridge.scss` desde el CMS source pero requiere que el
   dev recuerde correrlo. Risk de bridge stale en producción.

Cap-250 los cierra todos.

## Decision

### Batch A — Olas 241-242 — GDPR RTBF UI button

**Members.cshtml** ahora tiene un botón "🛡 GDPR erase" per-row al
lado del existente "🗑 Eliminar". El botón abre un nuevo
`<dialog id="member-gdpr-confirm">` con copy explícito enumerando
los 4 efectos cascading:

1. Hard-delete del Member record + cascada 2FA secret/recovery codes.
2. Anonimización de comments (`AuthorName → "[deleted]"`).
3. Anonimización de form submissions (`email → "[deleted]@gdpr.local"`).
4. Audit `gdpr.rtbf-processed` preservado por GDPR Art. 17(3).

**JS refactor**: el wiring inline duplicable del dialog hard-delete
se extrajo a un helper `wireDialog(dialogId, formId, emailId,
triggerAttr, urlSuffix)` reutilizado por delete + gdpr-erase. Ambos
flujos comparten el mismo pattern (data-attr trigger → dialog
showModal → form action dynamic).

**Decisión**: dos botones separados (no toggle). Los flujos son
distintos:
- **Delete**: scope mínimo (Member record + 2FA cascade), comments
  orphan.
- **GDPR erase**: scope full RTBF (todo lo del delete + anonimización
  persistida + audit terminal).

### Batch B — Olas 243-245 — Contract tests expansion

**3 specs nuevos** en `docs/contracts/tests/` cubriendo los 3
contratos pendientes:

**`host-bridge.contract.test.ts`** (Ola 243):
- Shape de `window.synergos` (version + i18n + theme + brand +
  member + page).
- `getBridge()` returns null cuando undefined; degradación graceful.
- Member null para anónimos; shape full para autenticados.
- Cultures formato BCP-47 simple `xx-XX`.
- Versioning compat check (major bump detection).
- **Security boundary**: member NO contiene `passwordHash`,
  `secret`, `sessionToken`, `twoFactorSecret`.

**`i18n-bridge.contract.test.ts`** (Ola 244):
- Resolution order del `t(key, fallback)` en 3 niveles
  (keys[key] → fallback → key literal).
- Naming convention `{Section}.{SubSection}.{Key}` PascalCase.
- Subset publishing: Form/Search/Common/Comments/Cart/Shop
  publicados; Admin/Account NO (Razor SSR puro).
- Format placeholders `{0}`/`{1}` preservados.
- Standalone fallback (sin `window.synergos`).
- Bridge estático (mutación client-side no notifica reload).
- Culture format `xx-XX`.
- Case-insensitive lookup tolerable como UI optimization.

**`css-tokens.contract.test.ts`** (Ola 245):
- Token naming convention `--syn-*` lowercase con dashes.
- Color primitive scale: families `neutral|brand|accent` + shades
  `0/50/100..900/950`.
- Semantic colors: `text/surface/border/state/action` con role
  lists canónicos.
- Spacing: `xs/sm/md/lg/xl/2xl/3xl/4xl`.
- Typography roles + radius.
- Theme override pattern (`:root[data-theme="dark"]`).
- **Fallback defensivo obligatorio** en UI: `var(--syn-X, default)`.
- CMS source nunca declara fallback (es source-of-truth).

Coverage: 4/5 contratos. Bundle registry queda deferred (requiere
HTTP mocking más complejo).

### Batch C — Ola 246 — CI integration via GitHub Action

**`.github/workflows/contract-tests.yml`**: workflow que corre el
Vitest harness en cada PR que toque `Synergos.CMS.Web/docs/contracts/**`
o el workflow mismo.

- Triggers: `pull_request` + `push` a main/master.
- Job: ubuntu-latest, Node 20, working-directory `docs/contracts/tests/`.
- Install: `npm ci` si lockfile, `npm install --no-audit --no-fund`
  sino. Cache de npm activo via `setup-node@v4` cuando lockfile
  presente.
- Run: `npm test` (vitest run).

**Decisión**: el harness pasa de "opt-in manual" a "CI gating
activo". Contratos rotos bloquean merge — son la única coupling
surface CMS↔UI, equivalente a un breaking change en una API
publicada.

### Batch D — Ola 247 — Husky pre-commit hook

**Repo Synergos.UI** (separado): agrega `husky` 9.1.7 + hook
`.husky/pre-commit` que detecta cambios al bridge SCSS o al script
generador y re-ejecuta `sync:tokens` para garantizar fresh output.

**`package.json`** del UI workspace:
- `"husky": "^9.1.7"` en devDependencies.
- `"prepare": "husky"` script (auto-install al `npm install`).
- `"sync:tokens": "npm run --prefix platforms/angular sync:tokens"`
  alias top-level (el script real vive en
  `platforms/angular/tools/sync-tokens.mjs`).

**`.husky/pre-commit`**:
```sh
CHANGED=$(git diff --cached --name-only --diff-filter=ACMR | \
    grep -E "^platforms/angular/libs/shared/src/styles/_tokens-bridge\.scss$|^platforms/angular/tools/sync-tokens\.mjs$")

if [ -z "$CHANGED" ]; then exit 0; fi

npm run sync:tokens --silent
git add platforms/angular/libs/shared/src/styles/_tokens-bridge.scss
```

Trigger condicional: ~0ms overhead en commits que no tocan tokens.
Solo se activa cuando hay risk real de drift.

### Olas 248-250 — Cierre

Este ADR + actualización current-state §11.22 + memory.

## Consequences

**Positivas:**

- **GDPR self-service real**: operador compliance procesa RTBF en 2
  clicks con threat model claro. Reduce risk de error humano + tiempo
  de processing.
- **Contract drift detectable en PR**: 4/5 contratos auditados
  automáticamente vía Vitest + GitHub Action. Cualquier cambio que
  rompa la shape se detecta antes del merge.
- **Tokens always fresh**: dev no puede commitear `_tokens-bridge.scss`
  stale ni un script generador cambiado sin regenerar output. Risk
  de drift en producción → 0.
- **§11.21 deferred items cerrados** (4/14): cap-250 deja la lista
  con 10 items, todos de mayor scope.

**Negativas:**

- **CI ahora depende de Node**: el workflow requiere setup-node
  cuando antes el repo era pure-.NET. Trade-off mínimo — Node es
  ubicuo en runners de GitHub Actions y el job no toca el build CMS.
- **Husky setup en UI**: dev nuevos del UI repo deben correr
  `npm install` (que dispara `husky install` via prepare). Si no lo
  hacen, los hooks no se activan. Mitigación: README del UI repo
  debería mencionarlo (deferred).
- **Bundle registry contract no cubierto**: los otros 4 contratos
  son shape-validation simple, pero bundle registry requiere mocking
  HTTP. Deferred a cap futuro cuando llegue el HttpBundleRegistryClient
  real (sigue StubBundleRegistryClient mientras el CDN team publica
  los 5 puntos del `cdn-contract.md`).
- **Vitest harness no installa node_modules en el repo**: el workflow
  los descarga cada run. Si la suite crece, considerar matrix +
  cache key strategy. Por ahora 4 specs, runtime <5s.

**Neutras:**

- 4 commits feat/test/ci/chore + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 1 npm package nuevo en UI: `husky` 9.1.7 (test-time only).
- 0 GUIDs nuevos, 0 schema rompedor.
- 3 specs nuevos en Vitest harness (host-bridge + i18n-bridge +
  css-tokens) — total ~120 test cases shape-validation.

## Implementation summary

| # | Foco | Repo | Commit |
|---|---|---|---|
| 241-242 | GDPR RTBF UI button + dialog | CMS | `2ea490e` |
| 243-245 | host-bridge + i18n-bridge + css-tokens contract tests | CMS | `795ef3c` |
| 246 | GitHub Action contract-tests.yml | CMS | `0508cc0` |
| 247 | Husky pre-commit + package.json updates | UI | `61ad854` |
| 248-250 | (este) ADR + current-state §11.22 |

## Próximas direcciones (Cap-260 candidatos)

Items §11.21 que quedan (10 → cap-260+):

- **CSP-strict mode** — `/synergos-bridge.js` endpoint con nonce
  para eliminar `'unsafe-inline'`.
- **Composite notifier para alerts** (Rule of Three si llegan
  Slack + Discord + Teams para alerts).
- **2FA multi-instance encryption-at-rest** —
  `AddDataProtection().PersistKeysToFileSystem` con shared keyring.
- **Performance benchmarks** (BenchmarkDotNet harness + targets).
- **DB-backed comment repository / audit trail / 2FA challenge
  cache** (cap-270 — requiere decisión EF Core target).
- **Soft-delete undo cross-restart** (depende de DB-backed comments).
- **Time-series store adapter** webhook telemetry (decisión
  Postgres TimescaleDB vs improved in-memory).
- **Snapshot tests** payloads (Verify.NET decisión).
- **Bundle registry contract tests** (cuando llegue
  HttpBundleRegistryClient real).
- **CI integration del Vitest harness** validation contra archivos
  source (CMS `syn-tokens.css` + Dictionary XMLs) en lugar de solo
  shape patterns.

## References

- ADR 0083 — CMS↔UI alignment via contracts (base).
- ADR 0084 — Cap-230 (deja `IEmailTemplateRenderer` como deferred).
- ADR 0085 — Cap-240 (deferred items origen de cap-250).
- `docs/contracts/host-bridge.md` — spec validado por host-bridge tests.
- `docs/contracts/i18n-bridge.md` — spec validado por i18n-bridge tests.
- `docs/contracts/css-tokens.md` — spec validado por css-tokens tests.
- `docs/hardening/gdpr-rtbf.md` — flow doc consumed by Batch A.
- [Husky 9 docs](https://typicode.github.io/husky/).
