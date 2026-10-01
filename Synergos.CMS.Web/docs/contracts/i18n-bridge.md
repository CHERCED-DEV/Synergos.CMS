# i18n bridge contract — `window.synergos.i18n`

- **Contract version:** v1.1 — *la forma no cambia; cambia QUÉ se publica* (ADR 0136, piloto
  #186): cada página publica las secciones que declaran sus elementos, con fallback por clave a la
  cultura por defecto, y el bridge se escribe al final del `<body>`. Ningún consumidor leía las
  claves que dejaron de viajar (0 llamadores de `t()` antes del piloto, medido).
- **Owner:** CMS host (server-side resolution + injection)
- **Consumer:** UI components (lookup at runtime)

## Premisa

El CMS resuelve las claves server-side desde el Dictionary de Umbraco
(511 ítems en `uSync/v9/Dictionary/`: 473 claves con texto y 38 contenedores, uno por sección) y
publica en `window.synergos.i18n` las de las secciones que piden los elementos de la página. Los
componentes las leen con `t(clave, respaldo)` de `@synergos/vitals-core`, que pasa por
`window.synergos.i18n.t`.

```
┌────────────────────────────────┐         ┌──────────────────────────────────┐
│ CMS (Razor)                    │         │ UI (Angular custom element)      │
│                                │         │                                  │
│ _SynergosBridge.cshtml emite   │         │ <synergos-X> hidrata             │
│ <script>                       │         │ Llama window.synergos.i18n.t(    │
│   window.synergos.i18n =       │ ──DOM──►│   'Form.Submit', 'Submit')       │
│     { "Form.Submit": "Enviar", │  bridge │ Render usa la string traducida   │
│       "Form.Cancel": ...} ;    │         │                                  │
│ </script>                      │         │                                  │
└────────────────────────────────┘         └──────────────────────────────────┘
```

## Naming convention

```
{Section}.{SubSection}.{Key}        // PascalCase
```

Una **sección** es un prefijo ENTERO de alias, sin mayúsculas (como resuelve Umbraco: la columna
`cmsDictionary.key` es `COLLATE NOCASE`): `Slider` casa `Slider.Next`; `Common.States` casa
`Common.States.NoResults` y no `Common.Buttons.Save`; `Tag` no casa `Tagline.X`. Hay 38 de primer
nivel (`Common`, `Shop`, `Slider`, `Rating`, `AppLauncher`…) y un elemento puede declarar un
sub-prefijo para no publicar la sección entera. `Account.*` y `Admin.*` son de Razor (SSR puro, ADR
0061/0073) y no viajan por el bridge.

## Window namespace shape

```typescript
declare global {
  interface Window {
    synergos: {
      i18n: SynergosI18n;
      theme: SynergosTheme;
      brand: SynergosBrand;
      version: string;       // host-bridge contract version
    };
  }
}

interface SynergosI18n {
  /** Active culture (e.g. "es-CO"). */
  readonly culture: string;
  /** Default culture for fallback when current key missing (la que Umbraco marca como default;
   *  el fallback ya viene resuelto en `keys`, ver «Fallback por clave»). */
  readonly defaultCulture: string;
  /** Map of resolved keys → strings (server-side resolved). */
  readonly keys: Record<string, string>;
  /** Lookup helper. Returns key itself if missing AND no fallback. */
  t(key: string, fallback?: string): string;
}
```

## Helper: `t(key, fallback)`

Resolution order:
1. Si `keys[key]` existe → retorna esa string (ya resuelta con el fallback por clave del servidor).
2. Si `fallback` provided → retorna `fallback`.
3. Sino → retorna `key` literal (defensive — visible al developer
   que falta una key). Los componentes SIEMPRE pasan respaldo: la clave cruda no sale en pantalla.

```typescript
t('Slider.Next', 'Siguiente diapositiva');                          // "Siguiente diapositiva" (es-CO)
t('Rating.Stars.Aria', '{n} de {max} estrellas', { n: 4, max: 5 }); // "4 de 5 estrellas"
t('NonExistentKey', 'Default');                                     // "Default"
```

## Qué se publica en cada página (v1.1, ADR 0136)

**La unión de las secciones que declaran los records de los elementos que la página emite**
(`[ElementoSynHost(..., Diccionario = ["Slider"])]`, ADR 0135), una sola vez por página. No hay
lista fija: la de once prefijos que había publicaba 176 claves (≈7 KB) en todas las páginas, tres
de sus prefijos no casaban ninguna clave, y ningún componente las leía. Medido en 98 páginas reales
antes y después del piloto: mediana del bridge de 7.755 B a 401 B; la más cargada, 1.602 B (el
presupuesto de `host-bridge.md` es < 4 KB).

Una sección que el record declara tiene que casar al menos una clave con texto (gate
`ContratoSynHostTests`), y cada `t('Clave')` de un elemento tiene que caer en las suyas (gate
`gate:diccionario` del UI, contra las `claves` que trae `elementos-synhost.json`). Un elemento sin
record no declara secciones: su `t()` pinta el respaldo.

## Fallback por clave

Si una clave de una sección declarada no tiene texto en la cultura activa, `keys` la trae con el de
la **cultura por defecto** (`defaultCulture`). Se resuelve al construir el bridge, en el servidor.
Antes la clave desaparecía de `keys` y `t()` sin respaldo devolvía la clave cruda (medido en vivo
borrando la traducción en-US de una clave en una copia de la base).

## Initialization order

El bridge va **al final del `<body>`** (v1.1): sólo ahí se sabe qué elementos emitió la página.
Sigue estando antes de que hidrate ninguno porque los `<script type="module">` de los elementos son
diferidos —corren al terminar el parseo— y este `<script>` clásico corre al parsearse.

```html
<body>
    <!-- … los <synergos-*> de la página y sus <script type="module"> … -->
    <script>
        window.synergos = window.synergos || {};
        window.synergos.i18n = {
            culture: "es-CO",
            defaultCulture: "es-CO",
            keys: {
                "Form.Submit": "Enviar",
                "Form.Cancel": "Cancelar",
                "Common.Loading": "Cargando..."
            },
            t: function(k, f) {
                return this.keys[k] !== undefined ? this.keys[k] : (f !== undefined ? f : k);
            }
        };
    </script>
</body>
```

Cuando los Web Components hidratan, `window.synergos.i18n.t` ya existe.

## Standalone (sin host)

Si el UI corre standalone (Storybook, demo page), `window.synergos`
puede estar undefined. Components UI deben handle gracefully:

```typescript
import { t } from '@synergos/vitals-core';
t('Slider.Next', 'Siguiente diapositiva'); // sin bridge: el respaldo
```

El helper vive en `vitals/core/src/bridge/synergos-bridge.ts` y es seguro sin host.

## Reactivity

El bridge es **estático** — populated una vez al render del HTML.
Cambios de cultura mid-session requieren full page reload.

Para hot-reload de cultures (CMS multi-language switcher) el flow:
1. CMS recibe request con `?culture=en-US` (o cookie).
2. CMS re-renderiza con keys de en-US.
3. Bundle UI se re-hidrata con nuevo `window.synergos.i18n`.

## Reglas

✅ UI **consume** keys, nunca las muta.
✅ UI **siempre declara fallback** en `t(key, fallback)`, y la clave va LITERAL.
✅ Traduce la funcionalidad o la pieza colocable; las hojas del design system reciben strings.
✅ Nuevas keys se proponen en CMS uSync XMLs primero, luego UI las
   consume.
❌ UI no inventa keys propias (todo viene del CMS Dictionary).
❌ UI no parse formato HTML/Markdown en las strings (los keys son
   plain text + simple `{0}` placeholders para `string.Format`).

## Format placeholders

Para strings parametrizadas:

```typescript
// CMS Dictionary key:
//   "Admin.Welcome" → "Bienvenido, {0}"

const greeting = synergos.i18n.t('Admin.Welcome', 'Welcome, {0}');
const personalised = greeting.replace('{0}', userName);
```

Dos convenciones conviven en uSync, y `t()` rellena las dos: **con nombre** —`{n}`, `{max}`,
`{count}`: 20 de las 25 claves con marcador— pasando un objeto, y **posicionales** —`{0}`, `{1}`,
como `String.Format`: las otras 5— pasando argumentos sueltos. Un marcador que no se pasa queda
escrito tal cual.

```typescript
t('Rating.Stars.Aria', '{n} de {max} estrellas', { n: 4, max: 5 });
t('Admin.Welcome', 'Bienvenido, {0}', userName);
```

## Versioning

- v1: canon inicial cap-220. Subset publishing por once prefijos fijos.
- v1.1 (ADR 0136, piloto #186): secciones por página declaradas por los records, fallback por
  clave en el servidor, bridge al final del `<body>`, `t()` con marcadores con nombre.
- Cambios de keys en uSync no rompen el contract — solo el UI puede
  ver "key no encontrada" + fallback se muestra.

## References

- `host-bridge.md` — full picture init order.
- `Synergos.CMS.Web/uSync/v9/Dictionary/` — source of truth de keys.
- ADR 0061 — i18n admin baseline (32 keys initial).
- ADR 0073 — i18n admin extension (+22 keys).
