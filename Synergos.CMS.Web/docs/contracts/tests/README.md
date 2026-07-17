# Contract tests harness — Cap-240 Batch D (Ola 238)

Validación de los 5 contratos CMS↔UI documentados en
`Synergos.CMS/Synergos.CMS.Web/docs/contracts/`. Standalone — el resto
de Synergos.CMS no consume `node_modules`. Este harness es opt-in
para el contract owner / UI team validar shape antes de bumpear
contract version.

## Setup

```bash
cd Synergos.CMS/Synergos.CMS.Web/docs/contracts/tests
npm install
npm test
```

## Stack

- **Vitest** 2.x — runner.
- **happy-dom** — DOM environment para CustomEvents sin browser real.
- **TypeScript** — los specs son `.test.ts` para validar shape al compile.

## Scope inicial

Cubierto en Ola 238:

- ✅ `dom-events.contract.test.ts` — `syn:component:ready` +
  `syn:component:error` shape, naming convention, outcome tri-state,
  bubbling default behavior.

Cubierto en Olas 243-245 (Cap-250 Batch B):

- ✅ `host-bridge.contract.test.ts` — shape de `window.synergos`
  (version + i18n + theme + brand + member + page), degradación
  graceful, security boundary (no secrets en member), versioning
  major check.
- ✅ `i18n-bridge.contract.test.ts` — resolution order de `t(key,
  fallback)` (3 niveles), naming convention `{Section}.{SubSection}.
  {Key}`, subset publishing, format placeholders {0}, standalone
  fallback.
- ✅ `css-tokens.contract.test.ts` — naming convention `--syn-*`,
  color primitive scale (neutral/brand/accent + shades 0-950),
  semantic colors (text/surface/border/state/action), spacing
  (xs..4xl), typography roles, radius, theme override pattern,
  fallback defensivo en UI consumption.

Pending (futuro):

- ⬜ Bundle registry contract — más complejo (HTTP mocked); deferred.
- ⬜ Validation contra archivos source (`syn-tokens.css`,
  `Dictionary/*.config`) en lugar de solo shape patterns. Requiere
  fs read + parse — agregar en CI step.

## Notas

- **CI gating activo** desde Cap-250 Batch C (Ola 246): el workflow
  `.github/workflows/contract-tests.yml` corre este harness en cada
  PR que toque `Synergos.CMS.Web/docs/contracts/**`. Bloquea el
  merge si los tests fallan.
- El harness se ejecuta separado del CI .NET — no requiere build de
  Umbraco ni DB. Solo Node 20 + npm install.
- Si llegan los specs spread to mirror project Synergos.UI, este
  harness queda obsoleto y se elimina (DRY: una sola fuente).
- El target NO es testear la implementación — es testear que el
  CONTRATO documentado sea consumible y self-consistent.

## Convención de nombres

- Los specs terminan en `.contract.test.ts` (vitest config exige
  ese sufijo) para distinguir de tests unitarios de implementación
  reales que vivirían en Synergos.UI.

## Referencias

- `../README.md` — overview de los 5 contratos.
- `../dom-events.md` — spec validado por este harness.
- ADR 0083 — CMS↔UI alignment via contracts.
