# ADR 0091 — Cap-300: Visible CDN-offline fallback + uSync audit extensions (Olas 298-299)

- **Status:** Accepted
- **Date:** 2026-04-29
- **Deciders:** Arquitecto + agente.

## Context

Cap-300 ataca dos items low-effort que cap-280/cap-290 dejaron en
backlog, alineados con KISS / 0-deps / 1-dev:

1. **Visible CDN-offline fallback** (§11.25 item 6 + §11.26 item 3):
   hoy cuando el bundle registry no resuelve, el `DefaultSynHostEmitter`
   emite el custom element con `data-synergos-cdn-offline="true"` pero
   sin contenido. El editor en backoffice ve un elemento vacío sin
   indicación visual de qué está pasando.
2. **Audit harness extensions** (§11.26 items 10 + 11): faltaban
   detección de DataType orphan (custom DataTypes sin consumers) y
   mojibake hygiene (UTF-8 mal decodificado por PowerShell 5.1 ANSI
   encoding trap, memoria `feedback_powershell_utf8_bulk_edits`).

## Decision

### Batch A — Ola 298 — Visible fallback content

**`DefaultSynHostEmitter`** ahora emite contenido placeholder DENTRO
del custom element cuando descriptor es null:

```html
<synergos-foo data-synergos-cdn-offline="true" config='...'>
  <div class="syn-cdn-offline-fallback"
       data-block-alias="foo"
       data-custom-tag="synergos-foo"
       aria-hidden="true"></div>
</synergos-foo>
```

**Por qué adentro del tag**: el Custom Elements spec define que el
contenido dentro del tag se renderiza as-is hasta que el bundle
registre el component (upgrade). Cuando el bundle hidrate, el
component decide si lo reemplaza o lo conserva como slot fallback.

**Por qué empty styleable div** (no texto literal):
- El server queda **framework/i18n-agnóstico** — coincide con la
  decisión de cap-280 Batch D de no tomar decisiones visuales server-
  side.
- La layer CSS del host (`wwwroot/css/syn-base.css` o un sheet del
  CDN) define el visual via selector
  `[data-synergos-cdn-offline] .syn-cdn-offline-fallback`. Loading
  states, error boundaries, icon + label, dimensiones por block.
- `data-attrs` exponen identidad para diferenciación per-block (e.g.
  Hero necesita height grande; Badge necesita inline-block).

**`aria-hidden="true"`**: el placeholder es presentational mientras el
component genuino carga. Cuando el bundle hidrate, se vuelve
informativo y el component lo controla (puede remover el atributo o
reemplazar el subtree completo).

**`BuildOfflineFallback` helper**: privado, encoded via
`EncodeAttributeDoubleQuoted` para defensa XSS (el alias viene del
schema, controlado, pero igual encoded por consistencia).

**2 tests** en `DefaultSynHostEmitterTests`:
- `EmitAsync_NullRegistryResolution_EmitsVisibleFallbackContent`
  (nuevo): verifica el div va DENTRO del custom element con data-attrs
  esperados + `aria-hidden` + sandwich check del orden.
- `EmitAsync_ResolvedRegistry_DoesNotEmitOfflineAttribute` extendido
  con assert "no `syn-cdn-offline-fallback`".

### Batch B — Ola 299 — Audit harness checks #7 + #8

**Check #7 — DataType orphan** (warning level):

DataType custom (`EditorAlias` no empieza con `Umbraco.`) definido
pero nunca referenciado por ningún `<Definition>` en ContentTypes.
Built-ins Umbraco se skipean siempre — son parte del runtime aún
cuando un site no los use directamente (e.g. `Umbraco.ListView` lo
usa el backoffice).

Warning level porque un DataType custom sin consumer puede ser:
- **Dead weight** (refactor candidate): rename en ContentTypes
  cambió el Definition pero el DataType viejo quedó.
- **Reserved scaffolding**: agregado preventivamente para una feature
  futura.

El operador decide caso por caso. Sin marker convention (los
DataTypes XML no tienen `<Description>` CDATA donde poner el marker
como en compositions).

**Check #8 — Mojibake hygiene** (error level):

Detecta byte sequences típicas de UTF-8 mal decodificado como Latin-1
y re-encodeado. Patrones cubiertos:

| Pattern | Carácter original | Frecuente en |
|---|---|---|
| `Ã¡` | á | español |
| `Ã©` | é | español, francés |
| `Ã­` | í | español |
| `Ã³` | ó | español |
| `Ãº` | ú | español |
| `Ã±` | ñ | español |
| `Ã¼` | ü | alemán, español (cigüeña) |
| `Â¿` | ¿ | español |
| `Â¡` | ¡ | español |
| `Ã'` | Ñ | español |

PowerShell 5.1 default ANSI encoding causa este artifact al editar
XMLs uSync. La memoria `feedback_powershell_utf8_bulk_edits` ya
documenta el bug y la mitigación (`[IO.File]::WriteAllBytes` con BOM
UTF-8 explícito), pero un editor humano puede saltarse la mitigación.

**Error level** porque XMLs con mojibake muestran texto roto en
backoffice (i18n strings, nombres de DocTypes con tilde, etc.) —
fix obligatorio.

**Verificación**: 0 errors / 0 warnings contra el schema actual. La
GitHub Action existente (`.github/workflows/usync-audit.yml`,
cap-270 Batch C) gateéa cualquier regresión.

### Olas 300-301 — Cierre

Este ADR + actualización current-state §11.27.

## Consequences

**Positivas:**

- **Editor no ve elementos vacíos**: el placeholder del fallback
  permite que el host CSS muestre "Loading..." / icon / spinner según
  preferencia, en lugar de un hueco blanco confuso. Coincide con la
  expectativa del operador de "preview rico en backoffice".
- **Web Components spec idiomático**: aprovechamos el slot fallback
  nativo en lugar de inventar mecanismo paralelo. Cuando el bundle
  hidrate, el component decide si reemplaza o conserva el subtree.
- **`aria-hidden=true` correcto**: durante el loading, screen readers
  no anuncian el placeholder; cuando el component hidrate y muestre
  contenido informativo, controla la accesibilidad.
- **Schema fail-fast extendido**: orphan DataTypes y mojibake se
  atrapan en CI. Antes solo se descubrían al notar texto roto en
  backoffice o al ver `DTSelect*` huérfanos en uSync export.
- **0 dependencies nuevas**: solo extensiones a archivos existentes.

**Negativas:**

- **Fallback sin texto literal**: editores que no estilen el
  `.syn-cdn-offline-fallback` siguen viendo un div vacío. Es decisión
  consciente — el server no sabe i18n para hardcodear "Loading...".
  Mitigation: un partial Razor `_SynHostFallbackStyles.cshtml` global
  con CSS default está fuera de scope de este ADR; el host puede
  agregarlo cuando le importe.
- **Mojibake check basado en blacklist**: cubre las 10 secuencias
  más comunes para español + algunas latinoamericanas. Otros idiomas
  (mandarín, árabe, ruso) tienen patrones distintos no cubiertos.
  Mitigation: extender la lista cuando llegue el caso.
- **DataType orphan check sin marker convention**: el operador no
  tiene forma de marcar un DataType como "intencionalmente unused".
  Si el warning empieza a doler, agregar comment XML
  `<!-- [Disponible — sin consumers actuales] -->` al inicio del file
  y skipear en el audit. Diferido hasta que aparezca un caso real.

**Neutras:**

- 2 commits feat/feat + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 0 npm packages nuevos.
- Tests: 232 → 233 (**+1**: visible fallback content; el resolved
  test extendido sin nuevo método).
- 2 audit checks nuevos (#7 orphan-datatype, #8 mojibake).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 298 | Visible fallback content + 2 tests | `e7cc1e9` |
| 299 | usync-audit checks #7 (orphan DataType) + #8 (mojibake) | `f822d66` |
| 300-301 | (este) ADR + current-state §11.27 |

## Próximas direcciones

Items que podrían atacarse en caps futuros:

- **Default CSS para `.syn-cdn-offline-fallback`** — partial global
  que agregue minimal "Loading..." UX out-of-the-box.
- **Mojibake detection extendido** a otros idiomas si llega
  requirement (mandarín, árabe).
- **DataType orphan marker convention** via comment XML cuando
  aparezca primer caso reservado.
- **`HttpBundleRegistryClient`** sigue bloqueado externamente.
- **uSync filename casing inconsistente** (cosmetic only).

## References

- ADR 0015 — SynHost framework-agnostic integration.
- ADR 0088 — Cap-270 (origen del audit harness `tools/usync-audit.mjs`).
- ADR 0089 — Cap-280 (origen del `data-synergos-cdn-offline` marker).
- ADR 0090 — Cap-290 (audit check #6 Definition GUID).
- `feedback_powershell_utf8_bulk_edits` (memoria — mojibake trap).
- [Custom Elements spec — fallback content](https://html.spec.whatwg.org/multipage/custom-elements.html#upgrades).
