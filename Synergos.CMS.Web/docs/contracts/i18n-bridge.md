# i18n bridge contract — `window.synergos.i18n`

- **Contract version:** v1
- **Owner:** CMS host (server-side resolution + injection)
- **Consumer:** UI components (lookup at runtime)

## Premisa

El CMS resuelve i18n keys server-side desde Umbraco Dictionary
(369+ keys en `uSync/v9/Dictionary/`) y las publica al UI via
`window.synergos.i18n` global. Los components UI hacen lookup
client-side via `window.synergos.i18n.t(key, fallback)`.

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

Sections actuales (cap-200):
- `Account.*` — Login/Register/Profile/2FA self-service.
- `Admin.*` — Dashboard operacional (56 keys cap-200).
- `Form.*` — Form submission UX.
- `Search.*` — Search box + results.
- `Common.*` — Cross-domain (Cancel, Close, Loading).
- `Comments.*` — Engagement comments.
- `Cart.*` / `Shop.*` — E-commerce.

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
  /** Default culture for fallback when current key missing. */
  readonly defaultCulture: string;
  /** Map of resolved keys → strings (server-side resolved). */
  readonly keys: Record<string, string>;
  /** Lookup helper. Returns key itself if missing AND no fallback. */
  t(key: string, fallback?: string): string;
}
```

## Helper: `t(key, fallback)`

Resolution order:
1. Si `keys[key]` existe → retorna esa string.
2. Si `fallback` provided → retorna `fallback`.
3. Sino → retorna `key` literal (defensive — visible al developer
   que falta una key).

Case-insensitive lookup recomendado para hacer el UI tolerante a
typos (`Form.Submit` vs `form.submit`).

```typescript
synergos.i18n.t('Form.Submit', 'Submit');     // "Enviar" (es-CO)
synergos.i18n.t('NonExistentKey', 'Default'); // "Default"
synergos.i18n.t('NonExistentKey');            // "NonExistentKey" (warning visible)
```

## Subset publishing

El CMS NO publica todas las 369 keys en window — solo las relevantes
al UI hidratable. Categorías:

- ✅ Publicadas: `Form.*`, `Search.*`, `Common.*`, `Comments.*`,
  `Cart.*`, `Shop.*` (consumidas por components UI).
- ❌ NO publicadas: `Admin.*` (admin dashboard es Razor SSR puro,
  no necesita en UI), `Account.*` (mismo motivo).

El operador puede override via `IHostBridgeContextBuilder` config
qué prefixes publicar.

## Initialization order

```html
<head>
    <!-- ... css tokens, etc ... -->
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
    <!-- ... bundle scripts despues ... -->
</head>
```

CMS emite el script ANTES de cargar bundles UI — garantiza que cuando
los Web Components hidraten, `window.synergos.i18n.t` ya existe.

## Standalone (sin host)

Si el UI corre standalone (Storybook, demo page), `window.synergos`
puede estar undefined. Components UI deben handle gracefully:

```typescript
function t(key: string, fallback: string): string {
    return window.synergos?.i18n?.t?.(key, fallback) ?? fallback;
}
```

Pattern: helper module en UI (`vitals/i18n/translate.ts` deferred)
que exporta `t()` con safe fallback.

## Reactivity

El bridge es **estático** — populated una vez al render del HTML.
Cambios de cultura mid-session requieren full page reload.

Para hot-reload de cultures (CMS multi-language switcher) el flow:
1. CMS recibe request con `?culture=en-US` (o cookie).
2. CMS re-renderiza con keys de en-US.
3. Bundle UI se re-hidrata con nuevo `window.synergos.i18n`.

## Reglas

✅ UI **consume** keys, nunca las muta.
✅ UI **siempre declara fallback** en `t(key, fallback)`.
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

Convención `{0}`, `{1}`, ... (compatible con `String.Format` C#).
Helper UI deferred para auto-replace con args:

```typescript
synergos.i18n.t('Admin.Welcome', 'Welcome, {0}', userName);
//                                                   └─ args ─┘
```

## Versioning

- v1 (este doc): canon inicial cap-220.
- Subset publishing inicial.
- Cambios de keys en uSync no rompen el contract — solo el UI puede
  ver "key no encontrada" + fallback se muestra.

## References

- `host-bridge.md` — full picture init order.
- `Synergos.CMS.Web/uSync/v9/Dictionary/` — source of truth de keys.
- ADR 0061 — i18n admin baseline (32 keys initial).
- ADR 0073 — i18n admin extension (+22 keys).
