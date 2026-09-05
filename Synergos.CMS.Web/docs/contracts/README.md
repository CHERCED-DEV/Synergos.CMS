# Synergos contracts — CMS ↔ UI alignment

- **Status:** Living document — cap-220 (Olas 211-220).
- **Audience:** Both Synergos.CMS y Synergos.UI maintainers.
- **Scope:** Contratos de integración entre el CMS host (Razor SSR)
  y los Web Components del UI (Angular custom elements).

## Premisa

CMS y UI viven en repos potencialmente separados. La única superficie
de acoplamiento son los **contratos** documentados aquí. Cada lado
implementa contra estos contratos sin importar código del otro.

```
┌──────────────────────────────────┐         ┌──────────────────────────────────┐
│  Synergos.CMS (Umbraco + Razor)  │         │  Synergos.UI (Angular elements)  │
│                                  │         │                                  │
│  Emits HTML con <synergos-X>     │ ──HTTP─►│  Bundles publicados al CDN       │
│  Inyecta window.synergos bridge  │         │  Custom elements hidratan        │
│  Lee bundle registry             │ ◄──────│  Emiten CustomEvents al host     │
│  Renderiza tokens CSS            │         │  Consumen tokens via :root       │
└──────────────────────────────────┘         └──────────────────────────────────┘
                ▲                                         ▲
                │                                         │
                └──────── Contratos (esta carpeta) ───────┘
```

## Los 5 contratos

| # | Doc | Propósito | Owner del schema |
|---|---|---|---|
| 1 | [`cdn-bundle-structure.md`](cdn-bundle-structure.md) | La forma de las rutas del CDN —`{tag}/{framework}/{versión}/`— que `IBundleRegistryClient` resuelve. | Joint (el CDN produce, el CMS consume) |
| 2 | [`dom-events.md`](dom-events.md) | CustomEvents que los `<synergos-*>` emiten + payload schemas. CMS escucha si necesita. | UI team |
| 3 | [`css-tokens.md`](css-tokens.md) | Las `--syn-*` custom properties que el CMS publica vía `<head>` y que el UI puede asumir. UI declara fallbacks. | CMS host (source of truth) |
| 4 | [`i18n-bridge.md`](i18n-bridge.md) | `window.synergos.i18n.t(key, fallback)` global que el UI consume. CMS popula via Razor partial. | CMS (server-side resolution) |
| 5 | [`host-bridge.md`](host-bridge.md) | Big picture: cómo los 4 anteriores se conectan en runtime. Init order + lifecycle. | Joint |

> **Dónde se verifica cada uno, porque no es donde parece.** El harness Vitest de
> `tests/` cubre **cuatro** —tokens CSS, eventos DOM, puente de host, i18n—, que son
> los de comportamiento en el navegador. El **primero no está ahí y no le falta**:
> es una forma de rutas, y quien la verifica es el lado C# que las resuelve
> (`HttpBundleRegistryClientTests`, `FileSystemBundleRegistryClientTests`,
> `BundleRegistryProbeTests`).
>
> Se dice porque «los 5 contratos + un harness» se lee como «los cinco están en el
> harness», y entonces alguien abre `tests/`, cuenta cuatro, y va a escribir un
> spec que duplicaría lo que ya cubre la suite.

## Naming conventions canónicas

| Asset | Convention | Example |
|---|---|---|
| Custom element tag | `synergos-{kebab-case}` | `<synergos-accordion>` |
| Schema alias (CMS) | `elementSyn{PascalCase}` | `elementSynAccordion` |
| Bundle path on CDN | `/elements/{alias}/{version}/main.js` | `/elements/elementSynAccordion/1.4.2/main.js` |
| CSS token | `--syn-{category}-{descriptor}` | `--syn-color-brand-500` |
| Dictionary key (i18n) | `{Section}.{SubSection}.{Key}` PascalCase | `Admin.Action.Approve` |
| CustomEvent name | `syn:{component}:{event}` | `syn:accordion:opened` |
| Window namespace | `window.synergos.*` | `window.synergos.i18n.t(...)` |

## Reglas de no-acoplamiento

❌ **El UI NO importa código del CMS** (sin shared TS package, sin
gRPC stubs, etc.).
❌ **El CMS NO importa código del UI** (sin npm install del UI).
❌ **Cero shared NuGet/npm package compartido** — solo contratos en
markdown + JSON Schema cuando aplique.
✅ **Single source of truth** del schema vive en el CMS uSync XMLs.
El UI espeja via `element-registry.json` exportado (process futuro:
generation script que lee uSync y emite el JSON).
✅ **Cambios de contracts** se proponen en este folder + ADR antes
de implementar. Compatibilidad backward-first.

## Versioning de los contratos

Cada doc lleva un `Contract version: vN` en su header. Bumps:

- **Patch** (typo, clarification): no bump.
- **Minor** (additive, e.g. nuevo CustomEvent): bump.
- **Major** (breaking): nuevo doc + ADR superseding.

CMS y UI commit de adoption del contract version en sus respectivos
CHANGELOGs.

## Bootstrapping

Para nuevos developers:

1. Lee este README.
2. Lee `host-bridge.md` para entender el flow runtime end-to-end.
3. Para tu cambio específico, lee el contract doc relevante.
4. Si tu cambio rompe un contract version: nuevo ADR.

## References

- ADR 0012 — CDN contract is consumed, not owned.
- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0083 — CMS↔UI alignment via contracts (este cap).
- `feedback_synhost_naming_convention` (memory).
- `feedback_framework_agnostic_integration` (memory).
