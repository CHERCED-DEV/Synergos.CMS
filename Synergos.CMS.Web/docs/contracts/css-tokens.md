# CSS tokens contract — `--syn-*` custom properties

- **Contract version:** v1.1 — *minor, aditivo.* Documenta las 8 variantes de
  tema vigentes y fija el casing canónico. v1 listaba tres y escribía
  `silvergold` todo-minúscula, que no la emite nadie.
- **Owner:** CMS host (source of truth via `wwwroot/css/syn-tokens.css`)
- **Consumer:** UI components (Angular Web Components)

## Premisa

El CMS host publica un set de **CSS custom properties con prefix
`--syn-*`** en `:root` (y override en `[data-theme]` para light/dark/
silverGold). Los components UI **consumen** estos tokens via `var(--syn-*)`
y declaran **fallbacks defensivos** para el caso de standalone (sin
host).

```
CMS:  :root { --syn-color-brand-500: #6366f1; }
UI:   .button { background: var(--syn-color-brand-500, #6366f1); }
              /*                                    └─ fallback ─*/
```

## Categorías canónicas

### Color

```
--syn-color-{family}-{shade}        # primitive scale
--syn-color-text-{role}             # semantic text
--syn-color-surface-{role}          # semantic surface
--syn-color-border-{role}           # semantic border
--syn-color-state-{state}           # success/warn/danger/info
--syn-color-action-{kind}-{state}   # button bg states
```

Families: `neutral`, `brand`, `accent`. Shades: `0`, `50`, `100`, ...,
`900`, `950`.

### Spacing

```
--syn-space-{size}    # xs sm md lg xl 2xl 3xl 4xl
```

Maps to rem-based scale (e.g. `--syn-space-md: 1rem`).

### Typography

```
--syn-font-family-{role}    # heading body mono
--syn-font-size-{size}      # xs sm base md lg xl 2xl 3xl
--syn-font-weight-{weight}  # regular medium semibold bold
--syn-line-height-{role}    # tight normal relaxed
```

Manrope canonical para heading + body (font-family con stack fallback
sans-serif).

### Border + radius

```
--syn-radius-{size}     # none sm md lg full
--syn-border-width-{size}
```

### Shadow

```
--syn-shadow-{level}    # none sm md lg xl
```

### Z-index layers

```
--syn-z-{layer}    # base sticky overlay modal toast
```

## Theme override pattern

CMS emite themes vía `data-theme` attribute en `<html>`:

```css
:root                          { /* light tokens — el base */ }
[data-theme="dark"]            { /* dark overrides */ }
[data-theme="silverGold"]      { /* silverGold overrides */ }
```

Los components UI no necesitan saber el theme — solo consumen los
tokens y se adaptan automáticamente.

### El casing es literal — `silverGold`, no `silvergold`

**El value que elige el editor ES el `data-theme` ES el nombre del bloque
CSS**, el mismo string sin ninguna transformación (ADR 0101 §1).
`_Layout.cshtml` escribe el atributo verbatim. Por tanto el canónico es
**`silverGold` en camelCase**.

`[data-theme="silver-gold"]` (kebab) sobrevive en el CSS como **alias
deprecado**; no lo emite nadie. `silvergold` (todo-minúscula) **no existe** en
ninguna capa — era un error de este documento en v1, propagado al
`host-bridge.contract.ts` del UI. Si tu código ramifica por string, usá
camelCase.

### Las 8 variantes vigentes

| `data-theme` | Origen | Bloque en `syn-tokens.css` |
|---|---|---|
| `light` | default (`:root`) | — es el base |
| `dark` | genérico | ✅ |
| `silverGold` | genérico / vertical Blogs | ✅ |
| `brand` | refuerza el primary de la marca | emitido en runtime por `_BrandThemeStyle.cshtml` |
| `eventsNight` | vertical Eventos (ADR 0101) | ✅ |
| `terraLux` | vertical Propiedades (ADR 0101) | ✅ |
| `scholar` | vertical Educación (ADR 0102) | ✅ |
| `meridian` | vertical Booking (ADR 0102) | ✅ |

La lista canónica en código es `DropdownOptions.PageThemeVariant.All`, y es la
que el host bridge publica en `window.synergos.theme.available`. `inherit`
**no** aparece: es el centinela del resolver, no un tema.

Agregar un tema = una entrada en esa constante + su bloque en
`syn-tokens.css`. `HostBridgeThemeContractTests` verifica en ambas direcciones
que ninguna de las dos listas se adelante a la otra.

## Fallback strategy (UI standalone)

Cuando el UI corre fuera del CMS host (Storybook, dev preview),
los tokens no existen. Cada component SCSS debe declarar
fallbacks via la sintaxis `var(--syn-X, defaultValue)`:

```scss
.synergos-card {
  background: var(--syn-color-surface-default, #ffffff);
  color:      var(--syn-color-text-primary, #1a1a1f);
  padding:    var(--syn-space-md, 1rem);
  border-radius: var(--syn-radius-md, 8px);
  box-shadow: var(--syn-shadow-sm, 0 1px 3px rgba(0,0,0,0.08));
}
```

## Single source of truth

El catálogo completo vive en
[`Synergos.CMS.Web/wwwroot/css/syn-tokens.css`](../../wwwroot/css/syn-tokens.css)
(344 declarations en cap-200).

Para que el UI no quede out-of-sync, un script de sincronización
(deferred) puede generar un `tokens.scss` con todos los fallbacks
inferidos del CSS source. Hasta entonces, manual sync.

## Tokens canónicos minimum-viable

Los components MVP deben asumir disponibles **al menos** estos
tokens (cualquier UI build pre-cap-220 debería degradarse limpio):

```css
/* Color */
--syn-color-text-primary
--syn-color-text-secondary
--syn-color-text-muted
--syn-color-surface-default
--syn-color-surface-elevated
--syn-color-border-default
--syn-color-brand-500
--syn-color-brand-600
--syn-color-state-success
--syn-color-state-warning
--syn-color-state-danger

/* Spacing */
--syn-space-xs   /* 0.25rem */
--syn-space-sm   /* 0.5rem  */
--syn-space-md   /* 1rem    */
--syn-space-lg   /* 1.5rem  */
--syn-space-xl   /* 2rem    */

/* Typography */
--syn-font-family-body
--syn-font-family-heading
--syn-font-size-base
--syn-font-size-lg

/* Radius + Shadow */
--syn-radius-sm
--syn-radius-md
--syn-shadow-sm
--syn-shadow-md
```

UI components que dependan de tokens fuera de esta lista mínima
deben declarar fallback OBLIGATORIO en su SCSS para no romper en
standalone.

## Reglas

✅ El UI **consume** tokens, no los define para el sistema host.
✅ El UI **siempre declara fallback** en cada `var()`.
✅ Si un nuevo token es necesario, propuesta en este doc + bump
   contract version.
❌ El UI no muta `:root` ni custom properties del host.
❌ El CMS no asume que el UI está hidratado para servir tokens —
   los tokens existen aunque el UI no esté cargado.

## Versioning

- v1 (este doc): MVP cap-220.
- Adición de category nueva: minor bump.
- Rename/remove existing token: major bump + ADR.

## References

- `host-bridge.md` — cómo el host emite `<head>` con los tokens.
- `Synergos.CMS.Web/wwwroot/css/syn-tokens.css` — source of truth.
