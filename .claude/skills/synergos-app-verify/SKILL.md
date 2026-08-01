---
name: synergos-app-verify
description: Verificación END-TO-END en NAVEGADOR de las apps/fichas Angular custom-element (synergos-*) de SynergosLabs — lo que un smoke HTTP no ve. Activar DESPUÉS de synergos-cdn-build / synergos-smoke-test, tras republicar bundles o tocar libs/shared, o cuando el arquitecto reporta "la app no aparece / se ve pobre / rota". Complementa (no duplica) synergos-smoke-test: aquí se fuerza la hidratación real de custom elements montados lazy, se verifica customElements.get, se hace leak-scan del DOM (undefined/NaN/[object), se mide overflow horizontal responsive a 375px, y se recorren los 7 temas por-siteRoot (dark/eventsNight/silverGold/scholar/terraLux/meridian/light) para cazar roturas de contraste que solo aparecen en un tema. Incluye los gotchas reales de las dos herramientas de navegador (embebido vs Chrome-ext) y el recordatorio del ciclo build:runtime+publish:runtime cuando un cambio de libs/shared no se ve.
model: claude-opus-4-8
---

# SYNERGOS App Verify — verificación viva de apps Angular en el navegador

`synergos-smoke-test` prueba el **transporte** (HTTP 200, Content-Type, Cache-Control, placeholders en el HTML raíz, SEO). Esta skill prueba lo que ese smoke NO puede ver: que el custom element **hidrata de verdad**, que **renderiza data real** (no `undefined`/`NaN`/mock), que **no desborda** en móvil, y que **luce bien en los 7 temas por-siteRoot**. Un `dotnet build` verde y un smoke verde NO garantizan integración viva — el arquitecto ha reportado apps que salían como "grid SSR sin la app" con todo verde.

> Regla de oro: **build verde + smoke verde NO es "hecho".** "Hecho" = verificado en navegador con `customElements.get(tag) === true` y data real a la vista. Ver `feedback_parallel_agents_contract_and_input_race` y `feedback_synhost_mount_hydration_gotchas`.

---

## 0. Prerequisitos

- CMS corriendo en `http://synergos.local:5000` (si no, `synergos-run-dev`). Confirmar con el ping del smoke test antes de abrir el navegador.
- Bundles publicados en `C:\LOCAL_CDN\synergos\` (si no, `synergos-cdn-build`).
- Corre el smoke test HTTP primero (`synergos-smoke-test`). Si el smoke ya marca placeholders `<!-- synergos-* placeholder -->` en el HTML raíz, el problema es de emisión (registry/ISynHostEmitter), NO de hidratación — resuélvelo allí antes de seguir aquí.

Rutas y hechos verificados (2026-07-13):
- Workspace UI: `C:\Users\HITMA\Desktop\synergos\Synergos.UI` (los `npm run` del runtime corren desde aquí).
- CDN local: `C:\LOCAL_CDN\synergos\` — apps en `<name>/angular/{latest,v0,<ver>}/main.js`; runtime compartido en `runtime/angular/21.1.6/sg-shared.js`.
- URL servida por el CMS: `http://synergos.local:5000/cdn-bundles/synergos/<name>/angular/latest/main.js`.
- El emitter (`DefaultSynHostEmitter.BuildScriptTags`) emite `<script src="…/cdn-bundles/…/main.js" type="module" defer></script>` (+ SRI cuando el descriptor lo trae). El runtime `sg-shared.js` llega por import map (`_SynHostRuntime.cshtml`). Por eso, para forzar hidratación, se re-importan los `<script src*="cdn-bundles">` **excluyendo** los de `/runtime/`.

Los 7 temas por-siteRoot (`data-theme`) y su vertical típico:

| data-theme | Vertical (siteRoot) |
|------------|---------------------|
| `light` | Healthcare / Gobierno (default) |
| `dark` | Tienda |
| `eventsNight` | Eventos |
| `silverGold` | Blogs |
| `scholar` | Educación |
| `terraLux` | Propiedades |
| `meridian` | Booking |

---

## 1. Elegir herramienta de navegador (y sus trampas)

Hay dos MCP de navegador. **Ninguna es completa; se usan según la tarea.**

| Herramienta | JS / DOM / resize | Screenshot | Cuándo usarla |
|-------------|-------------------|------------|---------------|
| **Embebido** `mcp__Claude_Browser__*` | OK | **ROTO** (no confiar) | Default para TODO lo de esta skill (checks JS, `customElements.get`, leak-scan, overflow, resize/tema). No pidas screenshot aquí. |
| **Chrome ext** `mcp__claude-in-chrome__*` (deferido) | OK | OK | Solo cuando NECESITAS ver píxeles (contraste real, layout roto). Cárgalo con ToolSearch primero. |

Trampas de la Chrome-ext:
- **Zoom/DPR raro tras clicks**: después de un `computer` click el device-pixel-ratio puede quedar alterado y los screenshots salen escalados. Si el screenshot sale zoomeado, hacer `resize_window` de nuevo (re-asienta el viewport) antes de re-capturar.
- **Timeouts CDP intermitentes**: un `screenshot` puede fallar por timeout. **Reintenta UNA vez** antes de concluir que algo está roto — casi siempre la 2ª pasa.
- `grep -P` para emoji/multibyte al escanear texto capturado (los `grep` sin `-P` parten mal los code points).

Para cargar la Chrome-ext en una sola llamada:
```
ToolSearch query: "select:mcp__claude-in-chrome__tabs_context_mcp,mcp__claude-in-chrome__navigate,mcp__claude-in-chrome__computer,mcp__claude-in-chrome__javascript_tool,mcp__claude-in-chrome__resize_window,mcp__claude-in-chrome__read_console_messages"
```

En adelante los snippets JS se ejecutan con `mcp__Claude_Browser__javascript_tool` (o el `javascript_tool` de la Chrome-ext). Navegar con `navigate`.

---

## 2. Forzar hidratación (carga diferida del módulo)

Las apps cargan vía `<script src="…/main.js" type="module" defer>` (§0), y el runtime `sg-shared.js` llega por import map. En un navegador **automatizado** el módulo puede no ejecutarse o el elemento no upgradearse de forma fiable (timing del import-map, el módulo diferido no corre, o el `customElements.define` no se dispara) → el tag queda en el DOM sin hidratar y parece roto sin estarlo. Fuerza la carga re-importando los bundles de app con cache-bust (garantiza que corra `customElements.define`):

```js
// Re-importa TODOS los bundles de app (no el runtime) con cache-bust.
// Devuelve por script si importó OK o el mensaje de error.
const scripts = [...document.querySelectorAll('script[src*="cdn-bundles"]')]
  .filter(s => !/\/runtime\//.test(s.src));   // excluir sg-shared runtime
const results = await Promise.all(scripts.map(async s => {
  const url = s.src.split('?')[0] + '?m=' + Date.now();
  try { await import(url); return { url, ok: true }; }
  catch (e) { return { url, ok: false, err: String(e.message || e) }; }
}));
return results;
```

Interpretación:
- `Failed to fetch dynamically imported module` → el bundle no existe en la URL (404) o el registry/ISynHostEmitter no lo emitió. Vuelve a `synergos-cdn-build`.
- `does not provide an export named 'X'` → **runtime STALE**. El bundle de app se construyó contra un `libs/shared` más nuevo que el `sg-shared.js` publicado. Ir a §6.
- Todos `ok:true` pero el elemento sigue sin definirse → sigue en §3 (puede ser alias/import-map/props; ver `feedback_synhost_mount_hydration_gotchas`).

---

## 3. Verificar que los custom elements se definieron

```js
// Lista los tags synergos-* presentes en el DOM y si el navegador los definió.
const tags = [...new Set(
  [...document.querySelectorAll('*')]
    .map(e => e.tagName.toLowerCase())
    .filter(t => t.startsWith('synergos-'))
)];
return tags.map(t => ({
  tag: t,
  defined: !!customElements.get(t),          // <- la verdad de la hidratación
  count: document.querySelectorAll(t).length,
  hasContent: [...document.querySelectorAll(t)]
    .some(el => el.shadowRoot ? el.shadowRoot.childElementCount > 0
                              : el.childElementCount > 0)
}));
```

- `defined:false` en cualquier tag = **NO hidrató**. No lo des por bueno. Causas ordenadas por frecuencia (ver `feedback_synhost_mount_hydration_gotchas`):
  1. **Runtime stale** (§6) — el síntoma más común tras un lote de republish; a menudo SIN error claro de consola.
  2. **BlockAlias camelCase** en el partial: el tag sale bien pero el lookup del bundle por alias crudo no matchea el `name` kebab del registry → sin `<script>`. Usa el `name` kebab (como `feature-grid`).
  3. **Página `Layout=null` sin `_SynHostRuntime`**: el import map no está → `Failed to resolve module specifier "@angular/*"`. Añadir `@await Html.PartialAsync("_SynHostRuntime")` en el `<head>`.
- `defined:true` pero `hasContent:false` → hidrató pero renderiza vacío. Suele ser **input-race** o **contrato de datos** (§4).

Revisa también la consola por errores:
```js
// (con la Chrome-ext) mcp__claude-in-chrome__read_console_messages onlyErrors:true
```

---

## 4. Leak-scan: data real, no basura ni mock

Hidratar no basta: hay que ver **data real**. Divergencias de contrato JSON backend↔UI y el input-race de Angular Elements hacen que la app renderice `undefined`, `NaN`, `$NaN`, `[object Object]` o caiga a un banner de "datos de ejemplo" (ver `feedback_parallel_agents_contract_and_input_race`).

```js
// Escanea el textContent de cada app en busca de fugas.
const BAD = ['undefined', 'NaN', '$NaN', '[object', 'null', 'Invalid Date'];
const apps = [...document.querySelectorAll('[class*="synergos-"], synergos-*')]
  .filter(e => e.tagName.toLowerCase().startsWith('synergos-'));
const hits = [];
for (const el of apps) {
  const text = (el.shadowRoot?.textContent || el.textContent || '');
  for (const bad of BAD) {
    if (text.includes(bad)) hits.push({ tag: el.tagName.toLowerCase(), leak: bad });
  }
}
// ¿Banner de degradación a mock?
const mockBanner = /datos de ejemplo|sample data|modo demo/i.test(document.body.innerText);
return { hits, mockBanner };
```

- Cualquier `hit` = bug de render. Rastrear qué clave llegó vacía.
- `mockBanner:true` = la app degradó a mock → el normalizer devolvió null porque el backend emitió claves distintas a las que la UI exige (la UI/spec es la fuente de verdad; el backend hace reshape). Confirmar a qué URL pegó:

```js
// ¿A qué endpoint pegó la app y con qué status? (input-race / contrato)
return performance.getEntriesByType('resource')
  .filter(r => /\/api\/|\/umbraco\/|synergos/i.test(r.name))
  .map(r => ({ url: r.name, dur: Math.round(r.duration) }));
```
Si la app pega al endpoint (200) pero muestra mock, es **contrato JSON** o **input-race** (fetch en `constructor()` que lee el input default en vez del compuesto por CMS; el fix es disparar la carga desde un `effect()` reactivo). No es infraestructura.

---

## 5. Responsive: overflow horizontal a 375px

Un desborde horizontal en móvil es el defecto de acabado más común. Redimensiona a 375px y mide:

```js
// (embebido) resize_window preset:"mobile"  → 375x812
// luego:
const overflow = document.documentElement.scrollWidth - window.innerWidth;
if (overflow <= 2) return { overflow, culprits: [] };
const culprits = [...document.querySelectorAll('*')]
  .filter(e => e.getBoundingClientRect().right > window.innerWidth + 2)
  .slice(0, 25)
  .map(e => ({
    tag: e.tagName.toLowerCase(),
    cls: (typeof e.className === 'string' ? e.className : '').slice(0, 60),
    right: Math.round(e.getBoundingClientRect().right)
  }));
return { overflow, culprits };
```

- `overflow > 2` = hay desborde (el +2 tolera redondeo subpíxel). Los `culprits` son los elementos que se salen — típicamente una imagen sin `max-width:100%`, una tabla/grid ancha, o un `min-width` fijo. El fix se **compone/tokeniza**, no se parchea con CSS ad-hoc (ver `feedback_compose_spacing_via_layout_composer`).
- Volver a `desktop` (1280x800) al terminar para no ensuciar los siguientes checks.

---

## 6. Recordatorio del runtime compartido (si un cambio no se ve)

Si tocaste `Synergos.UI/platforms/angular/libs/shared/` (un componente compartido, un export nuevo, un mixin) y NO se ve en el navegador, casi seguro falta regenerar el runtime. `@synergos/shared` es **externalizado** (un solo `sg-shared.js` por import map), no bundleado en cada app. Publicar solo los bundles de app deja el runtime STALE y las apps no hidratan (a veces sin error claro; a veces `does not provide an export named 'X'`).

Ciclo correcto, desde `C:\Users\HITMA\Desktop\synergos\Synergos.UI` (ver `synergos-cdn-build` y `feedback_shared_runtime_rebuild_required`):

```powershell
Set-Location "C:\Users\HITMA\Desktop\synergos\Synergos.UI"
npm run build:runtime      # re-bundlea sg-shared.js desde el dist fresco + reescribe import-map (SRI nuevo)
npm run publish:runtime    # copia el runtime al CDN
# (o, para todo consistente de una: npm run release:angular)
```
Luego **hard reload Ctrl+Shift+R** en el navegador (la URL del runtime es immutable/`max-age=31536000`; F5 normal sirve el cache viejo).

Verificación:
```powershell
# El símbolo nuevo debe estar en el runtime publicado:
Select-String -Path "C:\LOCAL_CDN\synergos\runtime\angular\21.1.6\sg-shared.js" -Pattern "NuevoSymbol" -SimpleMatch
```
```js
// ¿El navegador sirve cache viejo o fresco? Si difieren en longitud, es cache stale, no bug:
const u = '/cdn-bundles/synergos/runtime/angular/21.1.6/sg-shared.js';
const [cached, fresh] = await Promise.all([
  fetch(u).then(r => r.text()),
  fetch(u, { cache: 'reload' }).then(r => r.text())
]);
return { cachedLen: cached.length, freshLen: fresh.length, stale: cached.length !== fresh.length };
```

> Lección operativa (re-caída 2026-07-13): SIEMPRE cierra un lote de republish de apps con `build:runtime` + `publish:runtime` (o `release:angular`). `publish:element` de las apps NO basta.

---

## 7. Verificar en los 7 temas por-siteRoot

Un token de tema puede romper contraste en UN solo tema. Caso real: `--syn-color-text-on-accent` quedaba oscuro en `dark`/`eventsNight` → texto ilegible sobre las bandas brand/accent en Tienda y Eventos, mientras en los demás temas estaba bien (ver `feedback_verify_all_siteroot_themes`). Recorre cada vertical (cada uno trae su `data-theme`) y comprueba contraste:

```js
// Ejecutar en cada vertical tras navegar a su URL. Compara luminancia texto vs fondo.
function lum(rgb) {
  const [r, g, b] = rgb.match(/\d+/g).map(Number).map(v => {
    v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
const theme = document.querySelector('[data-theme]')?.getAttribute('data-theme') || '(none)';
// superficies típicas afectadas: bandas de sección, botones brand, badges, chips
const targets = [...document.querySelectorAll(
  '.syn-preset--theme-brand, .syn-preset--theme-accent, .syn-preset--theme-dark, [class*="btn"], [class*="badge"]'
)].slice(0, 40);
const lowContrast = targets.map(el => {
  const cs = getComputedStyle(el);
  const L1 = lum(cs.color), L2 = lum(cs.backgroundColor);
  const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
  return { cls: (typeof el.className === 'string' ? el.className : '').slice(0, 40), ratio: +ratio.toFixed(2) };
}).filter(x => x.ratio < 3);   // <3:1 = sospechoso (bg transparente da falsos positivos, verificar con screenshot)
return { theme, lowContrast };
```

- `ratio < 3` en texto sobre color saturado (brand/accent) = probable ilegibilidad. El texto SOBRE brand/accent debe ser CLARO en TODOS los temas.
- `backgroundColor` transparente (`rgba(0,0,0,0)`) da falsos positivos — para esos casos confirma con la Chrome-ext (screenshot) sobre ese tema.
- No basta con verificar `light`. Recorre los 7. Usa `resize_window colorScheme:"dark"` solo para el toggle de OS; el `data-theme` de SynergosLabs manda sobre el look, y viene por siteRoot.

---

## 8. Qué NO hacer

| ❌ No hagas | ✅ En su lugar |
|-------------|----------------|
| Confiar en `dotnet build` verde o smoke verde como "integración OK" | Abrir el navegador y confirmar `customElements.get(tag)===true` + data real |
| Asumir que el elemento hidrató porque el tag está en el DOM | `customElements.get(tag)` — el tag presente con `defined:false` = NO hidrató |
| Dar por muerta una app que está fuera del viewport | Forzar `import(url + '?m='+Date.now())` (§2) — monta lazy |
| Verificar solo vía Management API / HTTP | Eso es `synergos-smoke-test`; esta skill es el DOM vivo del navegador |
| Pedir screenshot al navegador embebido | Embebido = JS/DOM/resize; screenshots con la Chrome-ext (y reintenta 1 vez si CDP timeout) |
| Publicar solo `publish:element` tras tocar `libs/shared` | Cerrar con `build:runtime`+`publish:runtime` (o `release:angular`) + Ctrl+Shift+R (§6) |
| F5 normal esperando ver el runtime nuevo | Hard reload Ctrl+Shift+R (runtime es immutable/versionado) |
| Verificar el look solo en `light` | Recorrer los 7 temas por-siteRoot (§7) |
| Parchear un overflow con CSS ad-hoc | Componer/tokenizar el fix (Layout Composer + tokens `--syn-*`) |
| Tratar `mockBanner` como cosmético | Es contrato JSON o input-race: la app degradó a mock pese a 200 (§4) |

---

## 9. Reporte final sugerido

Por cada app/vertical verificada, reportar:
- `tag` + `defined` (hidratación) + `count`.
- Leaks encontrados (`undefined`/`NaN`/`[object`) y si hubo `mockBanner`.
- Endpoint real al que pegó (confirma data viva, no mock).
- Overflow a 375px (px + culprits si >2).
- Temas recorridos y hallazgos de contraste (`ratio<3`).
- Si aplicó el ciclo runtime: confirmación `stale:false`.

Veredicto PASS solo si: todos los tags `defined:true`, cero leaks, sin `mockBanner`, `overflow<=2` en móvil, y sin baja de contraste en ninguno de los 7 temas.

---

## Relacionadas

- `synergos-smoke-test` — capa HTTP/placeholder (correr ANTES).
- `synergos-cdn-build` — compilar y publicar bundles + runtime a LOCAL_CDN.
- `synergos-run-dev` / `synergos-health-check` — levantar y semaforear el stack.
- Memorias: `feedback_shared_runtime_rebuild_required`, `feedback_synhost_mount_hydration_gotchas`, `feedback_verify_all_siteroot_themes`, `feedback_parallel_agents_contract_and_input_race`, `feedback_prefer_cdn_angular_components`. ADRs: 0012 (CDN consumido), 0015 (framework-agnóstico), 0099 (registry+SRI+import map).