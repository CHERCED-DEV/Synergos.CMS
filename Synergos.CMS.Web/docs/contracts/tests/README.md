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

Pending (futuro):

- ⬜ `host-bridge.contract.test.ts` — shape de `window.synergos`
  (i18n + theme + brand + member + page) per
  `host-bridge.md`.
- ⬜ `i18n-bridge.contract.test.ts` — `t(key, fallback)` API +
  Dictionary key naming.
- ⬜ `css-tokens.contract.test.ts` — naming convention de CSS custom
  props (`--syn-*`).
- ⬜ Bundle registry contract — más complejo (HTTP mocked); deferred.

## Notas

- El harness se llama desde el CMS pero **no integra con CI .NET**
  por ahora. El operador o UI dev lo corre manualmente cuando hace
  cambios al contract spec o cuando agrega nuevo evento.
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
