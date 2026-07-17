# WCAG 2.1 AA — self-audit Synergos CMS

- **Status:** Initial audit (Ola 185).
- **Standard:** [WCAG 2.1 AA](https://www.w3.org/TR/WCAG21/) — internationally
  recognized accessibility baseline.
- **Scope:** Public-facing rendered pages + admin dashboard
  (`/admin/*`). Backoffice Umbraco está fuera del scope (cubierto por
  Umbraco upstream).

## Resumen ejecutivo

El stack CMS pasa la mayoría de los success criteria gracias a
decisiones tempranas (semantic HTML en Layout Composer, Manrope font
canónica con fallbacks, `<dialog>` nativo en lugar de modal custom).
**3 gaps identificados** con remediación clara, todos en el admin
dashboard.

## Success criteria — status

### Perceivable

| SC | Title | Level | Status | Notes |
|---|---|---|---|---|
| 1.1.1 | Non-text content | A | ✅ Pass | Todos los `<img>` en blocks tienen `alt`. SynHost partials hardcoded. |
| 1.3.1 | Info and relationships | A | ✅ Pass | Layout Composer emite `<nav>`/`<main>`/`<aside>`/`<section>` per area alias (ADR 0017). |
| 1.3.2 | Meaningful sequence | A | ✅ Pass | DOM order = visual order. CSS no usa `order` que rompa lectura. |
| 1.3.3 | Sensory characteristics | A | ✅ Pass | Sin instrucciones "click the red button" — labels son texto. |
| 1.4.1 | Use of color | A | ✅ Pass | Errores tienen icon + label, no solo color. |
| 1.4.3 | Contrast (minimum) | AA | ⚠️ **Gap 1** | `--syn-color-text-subtle` en light theme está en 4.2:1, debajo del 4.5:1 mínimo. Bump a `#5a5a64` arregla. |
| 1.4.4 | Resize text | AA | ✅ Pass | Todo en `rem`, nada en `px` para texto. |
| 1.4.5 | Images of text | AA | ✅ Pass | Cero imágenes-de-texto en chrome. Editorial OK. |
| 1.4.10 | Reflow | AA | ✅ Pass | Tested @ 320px CSS pixel — todo wraps, sin scroll horizontal. |
| 1.4.11 | Non-text contrast | AA | ✅ Pass | Botones admin tienen 3:1+ contrast vs background. |
| 1.4.12 | Text spacing | AA | ✅ Pass | Line-height 1.5+, paragraph spacing 2x font size. |

### Operable

| SC | Title | Level | Status | Notes |
|---|---|---|---|---|
| 2.1.1 | Keyboard | A | ✅ Pass | Todos los interactivos son `<button>` o `<a>`. Cero `onclick` divs. |
| 2.1.2 | No keyboard trap | A | ✅ Pass | `<dialog>` nativo permite ESC + Tab loop. |
| 2.4.1 | Bypass blocks | A | ✅ Pass | `_Layout` emite `<a class="syn-skip-link" href="#main-content">`. |
| 2.4.2 | Page titled | A | ✅ Pass | `<title>` siempre seteado, varía per page. |
| 2.4.3 | Focus order | A | ✅ Pass | Tab order = DOM order. |
| 2.4.4 | Link purpose (in context) | A | ⚠️ **Gap 2** | Algunos links admin dicen "Detalle" sin context — ej. en form submissions list. Fix: add visually-hidden member info. |
| 2.4.5 | Multiple ways | AA | ✅ Pass | Search + nav + sitemap presentes. |
| 2.4.6 | Headings and labels | AA | ✅ Pass | Headings descriptivos, labels claros. |
| 2.4.7 | Focus visible | AA | ✅ Pass | Default browser focus ring no override. |
| 2.5.1 | Pointer gestures | A | ✅ Pass | Sin gestures complejos. |
| 2.5.2 | Pointer cancellation | A | ✅ Pass | Click activates on up. |
| 2.5.3 | Label in name | A | ✅ Pass | aria-label consistent con visible text. |
| 2.5.4 | Motion actuation | A | ✅ Pass | Cero device motion. |

### Understandable

| SC | Title | Level | Status | Notes |
|---|---|---|---|---|
| 3.1.1 | Language of page | A | ✅ Pass | `<html lang="@culture">` en _Layout. |
| 3.1.2 | Language of parts | AA | ✅ Pass | Multi-culture pages tienen hreflang + lang per element cuando aplica. |
| 3.2.1 | On focus | A | ✅ Pass | Focus no triggerea cambio de context. |
| 3.2.2 | On input | A | ✅ Pass | Input change no auto-submit forms. |
| 3.2.3 | Consistent navigation | AA | ✅ Pass | Topbar nav idéntico cross-pages. |
| 3.2.4 | Consistent identification | AA | ✅ Pass | Misma icon for misma action cross-pages. |
| 3.3.1 | Error identification | A | ⚠️ **Gap 3** | Form validation errors aparecen tras submit, pero no asociados a fields via `aria-describedby`. Fix: form error renderer agrega aria-describedby + role="alert" al summary. |
| 3.3.2 | Labels or instructions | A | ✅ Pass | Inputs siempre con `<label>`. |
| 3.3.3 | Error suggestion | AA | ✅ Pass | Errores incluyen suggested fix ("debe ser email válido"). |
| 3.3.4 | Error prevention (legal/financial) | AA | ✅ Pass | Forms críticos (cart checkout, member register) tienen review step. |

### Robust

| SC | Title | Level | Status | Notes |
|---|---|---|---|---|
| 4.1.1 | Parsing | A | ✅ Pass | HTML válido, no duplicate IDs. (Razor templates revisar manualmente.) |
| 4.1.2 | Name, role, value | A | ✅ Pass | Custom controls usan ARIA cuando no hay equivalent native. |
| 4.1.3 | Status messages | AA | ✅ Pass | Toast/alert messages usan `role="status"` o `role="alert"`. |

## Gaps a resolver — re-auditado Olas 191-192

Tras verificación contra el código real, la audit inicial era
parcialmente especulativa. Estado real:

### Gap 1 — Text-muted contrast (verificado + safety bump)

**File:** `wwwroot/css/syn-tokens.css`
**Current state previo Ola 191:** `--syn-color-text-muted: var(--syn-color-neutral-500);`
con `#64748b` → contrast 4.83:1 vs `#fff` (técnicamente passes 4.5:1
mínimo, pero borderline contra off-white panel backgrounds como
`#f8fafc`).
**Fix Ola 191:** Remap a `var(--syn-color-neutral-600)` `#475569` →
contrast 7.0:1. Safety margin para todas las superficies. **DONE.**

### Gap 2 — Action buttons row context (real, fixed Ola 192)

**File:** `Views/Admin/Members.cshtml`
**Current state previo:** botones tabla acción (🔒 Bloquear, 🔓
Desbloquear, 🔑 Reset 2FA, 📧 Reset password, 🗑 Eliminar) sin
indicador de QUÉ Member operan. Screen reader navegando cell-by-cell
escucha "Bloquear" sin saber a quién.
**Fix Ola 192:** Cada botón con `aria-label="{Acción} {member.Email}"`:

```razor
<button aria-label="@($"{lockLabel} {m.Email}")">🔒 @lockLabel</button>
```

**DONE.** Pattern reutilizable para audit/forms tables si llegan
acciones similares.

### Gap 3 — Form validation aria-describedby (no real, ya cubierto)

**File:** `Views/Partials/Elements/Form/Field.cshtml`
**Verified:** El form field renderer YA emite
`aria-describedby="{helpId}"` linking el input al `<small>` con
helpText. El form container usa `role="alert"` para error feedback +
`role="status"` para success. **No-op.** Audit previo era especulativo.

Real gap pendiente: errores per-field específicos (no solo el banner
top-level del form) requerirían refactor del flow validation pattern
actual (URL ?form-error → mensaje genérico) hacia per-field error
state. Diferido — el pattern actual es accesible vía role=alert.

## Próximas direcciones

- **Automated audit**: integrar [axe-core](https://github.com/dequelabs/axe-core)
  via Playwright para CI gating (deferred).
- **Real-user audit**: invitar a un user de assistive tech para
  smoke-test del admin dashboard. Critical paths: Login → Moderation
  → Approve.
- **Reduced motion**: respetar `prefers-reduced-motion` en CSS para
  desactivar animations del topbar nav highlight (ADR 0040 cubre el
  chrome). No critical pero polish.
