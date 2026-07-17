# Host bridge contract — full picture CMS ↔ UI

- **Contract version:** v1
- **Owner:** Joint (CMS emite, UI consume) — both sides must implement.

## Big picture

Este doc cierra los 4 contratos anteriores con el flujo end-to-end:
desde que el visitor hits una page hasta que un Web Component está
hidratado y respondiendo a interacciones.

```
1. Visitor → GET /post/welcome
2. CMS Razor SSR → renders HTML con <synergos-X> tags + <head> tokens
3. <head> ejecuta <script>window.synergos = {...}</script>
4. <body> tail ejecuta <script src="cdn.../bundle.js">
5. Bundle hidrata custom elements
6. Custom elements consumen window.synergos.* + emit syn:* events
7. CMS host (opcional) escucha syn:* events para analytics/audit
```

## `window.synergos` shape canónica

```typescript
declare global {
  interface Window {
    synergos: {
      /** Bridge contract version — UI verifica compat antes de
       *  consumir features. */
      readonly version: string;  // e.g. "1.0.0"

      /** i18n bridge — ver i18n-bridge.md */
      readonly i18n: SynergosI18n;

      /** Theme info — read-only para UI. */
      readonly theme: SynergosTheme;

      /** Brand info — para UI que quiera customizar logos/colors. */
      readonly brand: SynergosBrand;

      /** Member context si está autenticado. Null para anónimos. */
      readonly member: SynergosMember | null;

      /** Site / page metadata. */
      readonly page: SynergosPage;
    };
  }
}

interface SynergosTheme {
  /** "light" | "dark" | "silvergold" — current. */
  readonly variant: string;
  /** All available variants. */
  readonly available: readonly string[];
}

interface SynergosBrand {
  /** Brand key del siteRoot activo (e.g. "acme", "default"). */
  readonly key: string;
  /** Display name. */
  readonly displayName: string;
}

interface SynergosMember {
  /** Member key (Guid string format "N"). */
  readonly key: string;
  /** Display name. */
  readonly displayName: string;
  /** Email (lowercase). */
  readonly email: string;
  /** Roles — case-sensitive. */
  readonly roles: readonly string[];
}

interface SynergosPage {
  /** Page Id Umbraco (numeric). */
  readonly id: number;
  /** Page DocType alias. */
  readonly docType: string;
  /** Canonical URL absoluta. */
  readonly canonicalUrl: string;
  /** Published cultures (e.g. ["es-CO", "en-US"]). */
  readonly cultures: readonly string[];
}
```

## CMS-side implementation

`Synergos.CMS.Web/Views/Shared/_SynergosBridge.cshtml` partial:

```razor
@using System.Text.Json
@inject Synergos.CMS.Interfaces.IHostBridgeContextBuilder Bridge
@{
    var ctx = Bridge.Build(ViewContext.HttpContext);
    var json = JsonSerializer.Serialize(ctx, new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}
<script>
window.synergos = @Html.Raw(json);
</script>
```

`IHostBridgeContextBuilder` seam (en Synergos.CMS.Interfaces) consume:
- `IBrandingProvider` — brand.
- `IBrandThemeProvider` — theme.
- `IMemberAccessGate` — member context.
- `IUmbracoContextAccessor` — page context.
- `IDictionaryService` o equivalente — i18n keys filtered.

`_Layout.cshtml` invoca el partial **antes** de cualquier `<link>` de
bundles UI. Pattern: tokens primero, scripts después.

## UI-side implementation

UI standalone helper en `vitals/runtime/synergos-bridge.ts`:

```typescript
export function getBridge(): Window['synergos'] | null {
  return typeof window !== 'undefined' && window.synergos
    ? window.synergos
    : null;
}

export function t(key: string, fallback: string, ...args: unknown[]): string {
  const bridge = getBridge();
  let str = bridge?.i18n?.t?.(key, fallback) ?? fallback;
  for (let i = 0; i < args.length; i++) {
    str = str.replace(`{${i}}`, String(args[i]));
  }
  return str;
}

export function getMember(): SynergosMember | null {
  return getBridge()?.member ?? null;
}

export function getTheme(): string {
  return getBridge()?.theme?.variant ?? 'light';
}
```

Cada Angular component que necesite acceso al bridge importa estos
helpers. **No inyección via Angular DI** — el bridge es global,
sincrónico, leíble en constructor sin promises.

## Init lifecycle

```
T+0ms     CMS responde HTML
          <head>
            <link rel="stylesheet" href="syn-tokens.css"> (1)
            <script>window.synergos = {...}</script>       (2)
            <link rel="stylesheet" href="syn-base.css">    (3)
T+50ms    DOMContentLoaded fires
T+100ms   <script src="cdn.../runtime.js"> loads          (4)
T+150ms   <script src="cdn.../element-bundle.js"> loads   (5)
T+160ms   customElements.define('synergos-X', ...)        (6)
T+170ms   <synergos-X> elements upgrade + hydrate         (7)
T+180ms   syn:component:ready event fires per element     (8)
```

Critical ordering:
- (1) tokens BEFORE (2) bridge BEFORE (4)+(5) bundles.
- (2) bridge debe existir cuando (6) define + (7) hydrate corren.
- Si (4) falla (CDN down), (7) nunca corre — los `<synergos-X>`
  permanecen inert pero el HTML no rompe (degradación graceful).

## Failure modes + degradación

| Failure | Behavior |
|---|---|
| CDN endpoint 404 (bundle no existe) | `IBundleRegistryClient` retorna null; CMS renderiza placeholder HTML comment. UI no hidrata nada. ✅ Graceful. |
| Bundle JS sintax error | Browser logs error; custom element NO se define; `<synergos-X>` queda inert. ✅ Page funciona sin hidratar. |
| `window.synergos` undefined (script tag failed?) | Helper `getBridge()` retorna null; `t()` usa fallback. ✅ Strings en defecto del UI. |
| Theme switch mid-session | Page reload requerido. Sticky session si LB-balanced. ⚠️ Documentado, no auto-handled. |
| Member logout via tab adyacente | window.synergos.member se vuelve stale. UI muestra como si estuviera logged. ⚠️ Long-poll + reload futuro. |

## Performance budget

- **Bridge script size:** target < 4 KB minified (cap inicial cap-220).
  Solo i18n keys frequently-used + theme + brand + member + page.
- **Init time:** bridge eval < 5ms en desktop modern.
- **Bundle load:** CDN-cached con 1 año TTL via `Cache-Control: public, max-age=31536000, immutable`.
- **Time-to-hydrate** (T+0ms → T+180ms):  target < 300ms en 3G simulated.

## Security

- **Bridge no contiene secrets**: `window.synergos.member` solo
  display name + email + roles. NO password hash, NO 2FA secret,
  NO session token (eso vive en HttpOnly cookies).
- **i18n keys plain text**: nada serializa HTML inline. UI escapa
  cuando renderiza.
- **CSP compatibility**: `<script>window.synergos = {...}</script>`
  inline requiere `script-src 'self' 'unsafe-inline'` o nonce. Si
  CSP estricto, swap por `<script src="/synergos-bridge.js">`
  inline-eq endpoint deferred.

## Versioning

- v1 (este doc): canon inicial cap-220.
- `window.synergos.version` permite UI verificar compat:

```typescript
const bridge = getBridge();
if (bridge && bridge.version) {
  const [major] = bridge.version.split('.').map(Number);
  if (major !== 1) {
    console.warn(`Synergos bridge version ${bridge.version} unexpected; UI v1 may not work`);
  }
}
```

## Implementation checklist

CMS side (cap-220 Olas 216):
- [ ] `IHostBridgeContextBuilder` seam.
- [ ] `DefaultHostBridgeContextBuilder` impl.
- [ ] `_SynergosBridge.cshtml` partial.
- [ ] Wire into `_Layout.cshtml` antes de bundle scripts.
- [ ] Settings `Synergos:HostBridge:I18nPrefixes` config.

UI side (cap-220 Olas 217-218):
- [ ] `vitals/runtime/synergos-bridge.ts` helper module.
- [ ] `_tokens-bridge.scss` con fallbacks declarados.
- [ ] Update `form.contract.ts` outcome a tri-state.
- [ ] At-least-one custom element using `t()` from bridge as smoke test.

## References

- `README.md` — index de los 5 contratos.
- `dom-events.md` — CustomEvents que el UI emite.
- `css-tokens.md` — tokens que el UI consume.
- `i18n-bridge.md` — detalle del i18n shape.
- ADR 0083 — alineación CMS↔UI via contracts.
