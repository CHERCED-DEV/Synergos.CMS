# ADR 0092 — Cap-310: Default fallback CSS + ASCII-only runtime logs (Olas 302-303)

- **Status:** Accepted
- **Date:** 2026-04-29
- **Deciders:** Arquitecto + agente.

## Context

Cap-310 cierra dos refinamientos triviales emergentes del smoke
test post-cap-300:

1. **Cosmético log encoding**: el operador observó en boot
   `[INF] Local CDN mounted: path=C:\LOCAL_CDN ␦ route=/cdn-bundles`
   donde `␦` es el "Symbol For Delete" (U+2421) — Windows console
   sustituyendo el caracter Unicode `→` que el `app.Logger.LogInformation`
   en `Program.cs:80` emitía. Cosmético, pero el operador debe poder
   leer logs sin distracción.
2. **CSS default ausente para el visible fallback** (§11.27 item 9):
   Cap-300 Batch A introdujo `<div class="syn-cdn-offline-fallback">`
   dentro del custom element offline, pero el div era invisible sin
   CSS del host. UX out-of-the-box era "vacío" — necesita styling
   default mínimo.

## Decision

### Ola 302 — Hotfix ASCII-only en runtime log statements

**`Program.cs`** los 2 logger calls del Local CDN mount usan ASCII
en lugar de Unicode:
- LogInformation: `→` → `->`.
- LogWarning: ` — ` → ` - `.

Otros usos de `→` y `—` viven en comments y XmlDoc que NO se
renderean en console — no se modifican (preservan readability del
código).

### Batch A — Ola 303 — Default fallback CSS

**`syn-base.css`** extendido con block `[data-synergos-cdn-offline]
.syn-cdn-offline-fallback`:

```css
[data-synergos-cdn-offline] .syn-cdn-offline-fallback {
  display: block;
  min-height: 4rem;
  padding: var(--syn-space-md);
  background: linear-gradient(90deg,
    var(--syn-color-surface-secondary) 0%,
    var(--syn-color-surface-tertiary) 50%,
    var(--syn-color-surface-secondary) 100%);
  background-size: 200% 100%;
  border: 1px dashed var(--syn-color-border-subtle, var(--syn-color-surface-tertiary));
  border-radius: var(--syn-radius-md);
  animation: syn-cdn-offline-shimmer 2s ease-in-out infinite;
}
```

Decisiones de diseño:

- **Skeleton shimmer** (idiom UX universal): linear-gradient con 3
  stops de surface-secondary/tertiary + background-size 200% 100% +
  animation 2s ease-in-out infinite. Editores reconocen el patrón
  inmediatamente.
- **`min-height: 4rem`**: garantiza visibilidad incluso cuando el
  block es pequeño (ej. badge inline). Host override per-block via
  `[data-block-alias="hero"] .syn-cdn-offline-fallback { min-height:
  320px; }` para casos especiales.
- **`::after { content: attr(data-block-alias) }`**: muestra el nombre
  del block centered con font-mono opacity 0.6 — info diagnóstica
  sin distraer.
- **Border `1px dashed`**: diferencia visualmente del component
  cargado (que típicamente tiene border solid o ninguno).
- **`prefers-reduced-motion: reduce`**: desactiva el shimmer y deja
  background plano — WCAG 2.3.1 compliance.
- **Tokens del design system**: 0 hardcoded values, swap de tema
  (light/dark/silverGold) preservado.

El operador puede override completo via specificity mayor si quiere
otra UX (ej. spinner icon, empty state custom). El default es
"skeleton + alias label" que es zero-config útil.

### Olas 304-305 — Cierre

Este ADR + actualización current-state §11.28.

## Consequences

**Positivas:**

- **Console legible**: el operador ve `path=C:\LOCAL_CDN -> route=/cdn-bundles`
  en lugar del `␦` críptico. Pequeño pero acumulativo en sesiones
  largas de debugging.
- **Visible fallback funciona out-of-the-box**: editores ven skeleton
  shimmer + nombre del block sin requerir CSS del host. UX de cap-300
  ahora completa.
- **Tokens-aware**: el visual respeta tema activo (light/dark/silverGold).
- **A11y compliant**: `prefers-reduced-motion` honrado.
- **0 dependencies nuevas**: solo extensiones a archivos existentes.

**Negativas:**

- **Mensaje skeleton es texto del alias raw** (`countdownClock` no
  "Countdown Clock"). Editores no técnicos podrían confundirse.
  Mitigation: el host puede override `::after content` con i18n key
  via Razor antes de la response. Diferido — fix cuando llegue
  feedback real.
- **Animation 2s puede ser molesta** si hay muchos blocks offline
  simultáneamente (ej. CDN total down). Mitigation: el operador
  override la animation a none + cambia a `prefers-reduced-motion`
  global si necesita.

**Neutras:**

- 2 commits fix/feat + 1 ADR + 1 current-state.
- 0 NuGet packages nuevos.
- 0 npm packages nuevos.
- 0 tests nuevos (CSS no es testeable via xUnit; visual verification
  manual).

## Implementation summary

| # | Foco | Commit |
|---|---|---|
| 302 | ASCII-only en `Program.cs` log statements (CDN mount) | `3bf58bd` |
| 303 | Default fallback CSS en `syn-base.css` con shimmer + tokens | `7733660` |
| 304-305 | (este) ADR + current-state §11.28 |

## Próximas direcciones

Items que podrían atacarse en caps futuros:

- **Localización del alias en `::after content`** vía partial Razor +
  Dictionary lookup cuando llegue feedback de editores.
- **Mojibake detection extendido** a otros idiomas (mandarín, árabe).
- **DataType orphan marker convention** via XML comment cuando
  aparezca primer caso reservado.
- **uSync filename casing inconsistente** — cosmetic only.
- **`HttpBundleRegistryClient`** sigue bloqueado externamente.

## References

- ADR 0089 — Cap-280 (introduce `data-synergos-cdn-offline` marker).
- ADR 0091 — Cap-300 (introduce `<div class="syn-cdn-offline-fallback">`).
- `feedback_powershell_utf8_bulk_edits` (memoria — encoding traps).
- [WCAG 2.3.1 Three Flashes or Below Threshold](https://www.w3.org/WAI/WCAG21/Understanding/three-flashes-or-below-threshold).
