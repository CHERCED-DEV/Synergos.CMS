# ADR 0094 — Design tokens como única fuente de verdad + identidad por-siteRoot vía mapeo a tokens canónicos

- **Status:** Accepted
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, durante la fase SynergosLabs (rebrand + híbrido CMS↔Angular CDN vivo + refinamiento premium con auditoría multi-agente).

## Context

La fase SynergosLabs dejó vivo el híbrido CMS↔Angular CDN (5 tipos de
componente servidos por la CDN local y configurados 100% desde el CMS)
y forzó una auditoría visual end-to-end. Esa auditoría descubrió que la
**línea de diseño no era una fuente de verdad real** — se aplicaba por
tres caminos paralelos que no convergían:

1. **Identidad por-siteRoot inyectada en tokens MUERTOS.**
   `Views/Shared/_BrandThemeStyle.cshtml` emitía el brand del siteRoot
   (PrimaryColor / AccentColor / fonts) a variables CSS
   `--syn-color-primary`, `--syn-color-background`, `--syn-color-text`,
   `--syn-font-heading`, `--syn-font-body` que **0 componentes leían**.
   Los renderers SSR y los custom elements consumen los tokens
   canónicos de `syn-tokens.css` (`--syn-color-action-primary`,
   `--syn-color-brand-500`, etc.), no esos alias. Resultado: el color
   de marca por siteRoot **no pintaba** — Entidad/Blogs/Ecommerce
   compartían el mismo look default.

2. **~46 canales RGB de marca crudos en gradientes/glows.** Los
   gradientes y glows hardcodeaban componentes RGB de indigo/violet
   inline en vez de leer canales theme-aware, así que no respondían al
   tema (dark / silverGold) ni a la identidad del siteRoot.

3. **67 scaffolds + stat-ticker fuera de la línea.** Los ~67 scaffolds
   placeholder y el primer componente construido de verdad
   (stat-ticker) traían hex sueltos y la familia `Inter` hardcodeada —
   fuera de la grilla de 8pt, del color brand canónico (`#4f6ef7`) y de
   la tipografía Manrope.

El sistema CSS ya existía (ADR 0048 lo construyó como espejo manual de
`Synergos.UI`), pero **no estaba consagrado como única fuente de verdad
de la línea**: nadie había declarado que todo color/espacio/tipografía/
radio/elevación/motion DEBE salir de un token, y que la identidad por
brand entra al sistema **por mapeo a esos tokens**, no por un canal
paralelo. La auditoría también ratificó la línea canónica (ver memoria
`reference_design_line_canonical` + `wwwroot/css/syn-tokens.css`): grilla
8pt, Manrope (nunca Inter, pesos ≤700), `brand-500 = #4f6ef7` (nunca
`#4f46e5` de Tailwind), radios y elevación por rol, motion (hover lift).

## Decision

**`syn-tokens.css` es la única fuente de verdad de la línea de diseño.**
Toda decisión visual (color, espacio, tipografía, radio, elevación,
motion, z-index) se expresa como design token y se consume vía
`var(--syn-*)`. Cero hex sueltos, cero familias tipográficas
hardcodeadas, cero magic numbers de espaciado fuera de la grilla de 8pt
en cualquier componente SSR o Angular.

### 1. Identidad por-siteRoot → mapeo a tokens canónicos

`Views/Shared/_BrandThemeStyle.cshtml` deja de escribir a los tokens
muertos y **mapea el brand del siteRoot a los tokens canónicos que los
componentes SÍ consumen**:

- `PrimaryColor` →
  - `--syn-color-action-primary` (+ `-hover` + `-text`)
  - `--syn-color-brand-500` (+ `-400` / `-600` derivados vía
    `color-mix` para mantener la rampa coherente)
- `AccentColor` → `--syn-color-accent-500`
- superficie / texto → `--syn-color-surface-primary`,
  `--syn-color-text-primary`
- tipografía → `--syn-font-family-heading` / `--syn-font-family-body`

Se eliminan además los bloques `[data-theme]` hardcodeados redundantes
del partial: el theming dark / silverGold ya vive tokenizado en
`syn-tokens.css`, así que el partial solo aporta el override de
identidad, no re-declara el tema.

### 2. Gradientes y glows → canales theme-aware

Los gradientes/glows consumen **canales** theme-aware
(`--syn-channel-brand`, `--syn-channel-accent`, `--syn-channel-ink`) en
vez de componentes RGB crudos. Los ~46 canales inline se reemplazan por
estos tokens, de modo que gradientes y glows responden al tema y a la
identidad del siteRoot automáticamente.

### 3. Todo componente consume tokens — cero hardcode

- Cada componente Angular se reescribe con el config-pattern y
  resuelve color/tipo/espacio desde tokens (sin hex, sin `Inter`).
- Los 67 scaffolds placeholder quedan tokenizados vía el mixin
  `syn-scaffold-placeholder` (un solo punto que lee tokens).
- stat-ticker (primer scaffold construido de verdad) usa `tabular-nums`
  + tokens; radios Angular unificados a `--syn-card-radius`.
- BEM huérfanas, footer, logo-cloud, hero CTA, paleta de emails:
  todos realineados al ramp tokenizado.

### 4. El SCSS de Synergos.UI es espejo de `syn-tokens.css`

El design system de `Synergos.UI` (consumido por los custom elements
Angular vía CDN) y `syn-tokens.css` (consumido por el SSR del CMS)
expresan **los mismos tokens**. El SCSS de UI es espejo del CSS del CMS
(misma rampa, mismos nombres semánticos), para que el render SSR y el
render hidratado por web component sean visualmente idénticos.

Esto extiende ADR 0048 (que estableció el espejo manual de tokens) con
una regla normativa: la línea es **tokens-first**, y la identidad de
marca es **un consumidor más** del sistema de tokens, no un canal
paralelo.

## Consequences

**Positivas**

- **La identidad por-siteRoot ahora pinta de verdad**: Entidad = azul,
  Blogs = gold, Ecommerce = dark se renderizan correctamente porque el
  brand entra por los tokens canónicos que los componentes leen.
- **Una sola palanca**: cambiar un token canónico propaga a SSR + web
  components + emails + scaffolds. No hay que cazar hex sueltos.
- **Gradientes/glows theme-aware**: responden a dark/silverGold y a la
  identidad sin código condicional por brand (alineado con ADR 0010).
- **Coherencia premium verificada en vivo**: grilla 8pt, Manrope,
  `brand-500 = #4f6ef7`, radios/elevación/motion por rol — auditoría
  multi-agente + 2 pases de ejecución confirmaron el resultado.
- **Refuerza ADR 0010**: la marca se resuelve por provider y entra al
  layer visual por mapeo a tokens, sin `if (brand.Key == "X")`.

**Negativas**

- **Doble fuente de verdad CMS↔UI sincronizada a mano**: `syn-tokens.css`
  (CMS) y el SCSS de `Synergos.UI` se mantienen espejados manualmente.
  Si uno cambia un token y el otro no, hay **riesgo de drift** entre el
  render SSR y el render hidratado. Mitigación futura: build-step / CI
  que genere ambos desde un único origen de tokens (o falle si
  divergen). Diferido hasta que el churn de tokens lo justifique.
- **Derivados vía `color-mix`**: `--syn-color-brand-400/-600` se derivan
  del `-500` del brand del siteRoot por `color-mix`. La rampa derivada
  puede no igualar exactamente una rampa diseñada a mano para un brand
  con un primario muy saturado u oscuro. Aceptable para los 3 brands
  actuales; revisitar si un brand futuro necesita rampa custom.

**Neutras**

- Los 67 scaffolds quedan tokenizados vía el mixin
  `syn-scaffold-placeholder`; cuando cada uno se construya de verdad
  (como stat-ticker), ya parte de la línea correcta.
- 0 schema nuevo, 0 GUIDs, 0 paquetes NuGet. Cambios concentrados en
  `Views/Shared/_BrandThemeStyle.cshtml`, `wwwroot/css/syn-tokens.css`,
  CSS/SCSS de componentes y los proyectos Angular de `Synergos.UI`.

## Alternatives considered

- **Dejar los tokens muertos y migrar los componentes a leerlos** —
  rechazado: invierte la dependencia (los componentes seguirían el
  brand en vez de la línea) y rompería el espejo con `Synergos.UI`,
  cuyos web components ya consumen los canónicos.
- **Condicionales por brand en los componentes** (`if siteRoot == X`) —
  rechazado: viola ADR 0010 y dispersa la identidad por todo el árbol
  de componentes.
- **Build-step que genere `syn-tokens.css` + SCSS desde un único YAML
  de tokens ahora mismo** — diferido (no rechazado): es la mitigación
  correcta del drift, pero abstracción prematura mientras el set de
  tokens y los 3 brands se estabilizan (CLAUDE.md §6). Queda como
  próxima dirección.

## References

- ADR 0010 — Branding vía provider (identidad sin conditional branching;
  el mapeo a tokens es el vector de aplicación).
- ADR 0012 — CDN contract consumed (los web components Angular consumen
  los mismos tokens vía el SCSS espejo).
- ADR 0048 — CSS design system aligned with Synergos.UI (estableció el
  espejo manual de tokens; este ADR lo eleva a tokens-first normativo).
- `reference_design_line_canonical` (memoria — la línea canónica).
- `reference_siteroot_identity_system` (memoria — composición Apariencia
  end-to-end).
- `feedback_design_system_8pt_grid` (memoria — exigencia de grilla 8pt).
- `feedback_compose_never_hardcode` (memoria — el look es código,
  tokenizado).
- `wwwroot/css/syn-tokens.css` — fuente de verdad de la línea.
- `Views/Shared/_BrandThemeStyle.cshtml` — mapeo brand → tokens
  canónicos.
- Verificado en vivo (fase SynergosLabs): identidad por-siteRoot pinta;
  gradientes/glows theme-aware; stat-ticker count-up tokenizado.
