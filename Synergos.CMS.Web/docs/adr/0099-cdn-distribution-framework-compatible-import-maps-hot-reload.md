# ADR 0099 — Distribución a CDN: bundles framework-compatible + import maps (Angular) + hot-reload + SSR

- **Status:** Proposed (consolida el estado real + define el norte y el refactor; parte vivo, parte por refinar)
- **Date:** 2026-06-25
- **Deciders:** Arquitecto + agente, fase SynergosLabs. Verificado contra código vivo (pipeline UI `Synergos.UI/tools/` + `FileSystemBundleRegistryClient` del CMS + `C:\LOCAL_CDN`).
- **Soporta a:** ADR 0096 (module-mount), 0097 (dashboard), 0098 (healthcare) y todos los `elementSyn*`.

## Context

El arquitecto fijó el norte de la capa de distribución a CDN:

> "Debemos ser framework-compatible; en Angular debe tener su **import map**;
> todo debe distribuirse a la CDN; usamos un CLI en UI para hacer esos
> bundles, pero falta mucha refinada y muchísimo refactor para simplificar.
> Lo que se quería era desligue a CDN de uno o muchos —sea componente,
> módulo, framework completo o no— que sean simples, que usen libs, que sean
> un producto, que empalmen al CDN, y que permitan trabajar con **hot-reload**,
> así sea **SSR**."

**Estado verificado contra código vivo** (no asumido):

1. **El contrato CDN ya existe** (`docs/contracts/cdn-bundle-structure.md`):
   estructura `{cdn}/synergos/{folder}/{framework}/{slot}/{main.js,manifest.json}`
   con slots `latest` / `v{N}` / semver exacto; `framework ∈ {angular,
   react, svelte, vanilla}`; `registry.json` índice global. Forma corta de
   folder (`hello-world/`, no `synergos-hello-world/`).
2. **El CMS ya consume + hot-reloadea** (`FileSystemBundleRegistryClient`):
   lee `registry.json`, resuelve `BlockAlias` por Name/Tag/Alias, elige
   framework (default o primero disponible), computa SRI sha384 lazy si el
   manifest no lo trae, y **recarga vía `FileSystemWatcher` con debounce**.
3. **El import map de Angular YA está prototipado** (`tools/build-runtime.mjs`):
   construye un **runtime compartido** `dist/runtime/angular/{ng-version}/`
   con `ng-core.js` / `ng-common.js` / `ng-elements.js` /
   `ng-platform-browser.js` / `sg-core.js` / `sg-shared.js` + un
   **`import-map.json`** listo para inyectar. Cada bundle de elemento
   externaliza Angular + `@synergos/*` → pesa ~5-15 KB en vez de ~150 KB.
4. **El CLI ya existe** (`tools/`): `publish-element.mjs` (publica a los 3
   slots), `manifest-builder.mjs`, `dev-cdn.mjs` + `lib/livereload.mjs`
   (hot-reload de la CDN en dev), `build-runtime.mjs` / `publish-runtime.mjs`
   (runtime/import-map), `catalog.mjs` (índice). Docs en
   `Synergos.UI/SynergosDocs/` (CDN_RUNTIME, BUILD_PIPELINE, DEV_CDN_MODE).
5. **Ya hay experimentos de full-app on CDN**: `angular-host` / `mf-host` /
   `macro-host` (tier=module) en el registry.

**Corrección (verificada contra código vivo):** el CMS **YA emite el import
map** — `Views/Partials/_SynHostRuntime.cshtml` (cableado en `_Layout`
`<head>`, antes de cualquier `<script type="module">`) lee el
`import-map.json` real del CDN, **reescribe el host baked
(`https://…`) por el `PublicBaseUrl` configurado (`/cdn-bundles`)** y emite
`<script type="importmap">`. Es decir, los bundles Angular que externalizan
deps **SÍ resuelven** (los `elementSyn*` Angular ya andan por esto). Mi
suposición previa de que "faltaba" era errónea.

**El gap real (corregido)**: (a) `_SynHostRuntime` es **FileSystem-only**
(lee `localPath` del disco) → en prod con CDN remota (Mode=Http) hay que
leer el `import-map.json` por HTTP — el MISMO gap que el
`HttpBundleRegistryClient` diferido; ambos van juntos. (b) el CLI acopla a
Nx y dispersa pasos (build → manifest → publish → regenerar registry); (c)
inconsistencia SRI (publish usa sha256; el CMS y el contrato usan sha384).
El arquitecto pide refinar esto.

## Decision

### 0. CMS y UI son deploys INDEPENDIENTES (gobernado por ADR 0083) — no negociable

`Synergos.CMS` y `Synergos.UI` son repos/deploys separados, en hosts
distintos. **El único acople es el CDN (bundles) + los contratos
(`docs/contracts/`).** Cero shared code, cero npm/NuGet compartido, cero
import cruzado, cero build-time coupling (regla de `docs/contracts/README.md`,
ADR 0083). Consecuencias para esta capa:

- El CMS **consume** el CDN vía `IBundleRegistryClient` + lee el
  `import-map.json` publicado; **nunca** lee el repo UI ni asume que está
  co-localizado.
- El **FileSystem CDN local (`C:\LOCAL_CDN`) es conveniencia de DEV.** En
  prod el CMS apunta a un CDN remoto (Mode=Http) — y ese modo está diferido
  tanto para el registry client como para el import-map emitter (§4). No
  asumir filesystem en prod.
- Las **URLs del CDN se resuelven por config** (PublicBaseUrl / registry),
  no se cablean. El import-map emitter reescribe el host baked → PublicBaseUrl
  precisamente para no acoplar el CMS a un host del UI.
- Schema sigue siendo SoT del CMS (uSync); el UI lo espeja vía
  `element-registry.json` (ADR 0083). El CMS no genera bundles; el UI no
  importa schema C#.
- **Atajos locales OK, con disciplina:** publicar un bundle a mano en
  `C:\LOCAL_CDN` (como el `hello-module` de prueba) sirve para smoke local,
  pero el **source canónico de cualquier elemento vive en el repo UI** y se
  publica con su CLI. Un bundle hand-written sin source en UI es throwaway,
  no un elemento del producto.

### 1. La CDN distribuye CUALQUIER tier con el MISMO contrato

Componente (primitive/composition), módulo, experiencia o app de framework
completo se publican con la misma estructura (`registry.json` + slots +
`manifest.json`) y se consumen con el mismo `IBundleRegistryClient`. El
`Tier` del descriptor (`primitive|composition|module|experience`) es solo
metadato de budget — **el mecanismo de montaje no cambia con el tamaño**.
El module-mount (ADR 0096) consume `module`/`experience`; los `elementSyn*`
consumen `primitive`/`composition`. Un "framework completo" es un bundle
`experience`.

### 2. Framework-compatible: el framework se resuelve en runtime

4 frameworks oficiales (`angular`/`react`/`svelte`/`vanilla`). El CMS
resuelve el framework del descriptor en runtime (default por settings +
fallback al primero disponible) — **el schema CMS nunca hornea el framework**
(principio 6). Un mismo elemento puede tener implementaciones en varios
frameworks; el CMS sirve la que esté.

### 3. Import map para Angular (y por-framework) — el desligue

**El bundle de cada elemento externaliza su framework + las libs compartidas;
un runtime compartido se carga UNA vez vía import map.**

- **Runtime compartido** (ya: `build-runtime.mjs`): `runtime/angular/{ver}/`
  con Angular partido en ESM + `@synergos/core`+`shared` (`sg-*`) +
  `import-map.json`. Análogo para react/svelte cuando haga falta.
- **Bundle de elemento**: build con Angular + `@synergos/*` marcados como
  **external** → importa specifiers bare (`@angular/core`, `@synergos/shared`)
  que el import map resuelve al runtime del CDN. Resultado: bundles chicos,
  un solo download del framework por página, varios módulos comparten runtime.
- **Vanilla** no necesita import map (self-contained) — sirve de piso
  framework-compatible y de prueba del mecanismo.

### 4. El CMS emite el import map una vez por página — YA IMPLEMENTADO

`Views/Partials/_SynHostRuntime.cshtml` (en el `<head>` de `_Layout`, antes
de cualquier `<script type="module">`):

- Lee el `import-map.json` del runtime publicado en el CDN
  (`{LocalPath}/{ns}/runtime/angular/latest/import-map.json`).
- **Reescribe el host absoluto baked (`^https?://[^/]+`) por el
  `PublicBaseUrl` configurado** (`/cdn-bundles`) → las URLs apuntan a donde
  el browser SÍ alcanza el CDN según la config del CMS, sin que el CMS
  conozca el host del UI. Esto es el punto de **desacople de URLs**.
- Emite un único `<script type="importmap">{ "imports": {…} }</script>`.
  Degradación graceful: si no hay runtime publicado, no emite nada y el
  fallback offline del SynHost emitter aplica.

**Pendiente (prod):** el partial es FileSystem-only. Para CDN remota
(Mode=Http) hay que leer el `import-map.json` por HTTP — va junto con el
`HttpBundleRegistryClient` diferido. Refinamiento opcional: promover la
lógica a un seam `ISynRuntimeImportMapEmitter` (Interfaces) con impls
FileSystem/Http, para testearlo y soportar prod — pero **no inventar uno
nuevo: el partial ya cumple en dev**.

### 5. Hot-reload — funciona, incluso con SSR

- **Lado CMS** (ya): `FileSystemBundleRegistryClient` + `FileSystemWatcher`
  con debounce recargan `registry.json` + invalidan manifest/SRI cache al
  cambiar archivos en `C:\LOCAL_CDN`. Sin reiniciar el CMS.
- **Lado UI** (ya): `dev-cdn.mjs` + `lib/livereload.mjs` sirven los bundles
  en dev con livereload.
- **SSR-compatible**: el server renderiza el HTML del host (página + import
  map + `<synergos-*>` con config) y el browser hidrata; el módulo es
  CSR-only por diseño (ADR 0096). Un cambio de bundle dispara livereload
  (browser) + watcher (CMS invalida descriptor) → el siguiente render trae
  el bundle nuevo. SSR no estorba porque el host solo emite tags + scripts.

### 6. El CLI de `Synergos.UI/tools/` es el camino canónico — refactor target

Distribuir = `build → manifest → publish a los 3 slots → (re)generar
registry.json`. Hoy está disperso y acoplado a Nx. **Objetivo del refactor**
(el arquitecto lo pidió): un comando único, Nx-opcional, que sirva a
componente/módulo/framework-completo por igual. Items concretos a cerrar:

- **Desacoplar de Nx**: `publish-element.mjs` resuelve element+framework via
  `nx show project`. Permitir resolución por convención de path / config sin
  Nx (el arquitecto autorizó "sacar Nx de la ecuación").
- **Unificar registry**: `publish-element` no actualiza `registry.json`; es un
  paso aparte (`catalog.mjs`). Un solo `publish` debe dejar slots + manifest
  + registry consistentes.
- **Reconciliar SRI**: `publish-element` calcula **sha256**; el contrato y el
  CMS usan **sha384**. Unificar a sha384 (o dejar que el CMS lo compute lazy
  y no escribir integrity en el manifest, como hace el bundle vanilla de
  prueba).
- **Emitir/publicar el import map** como parte del flujo de runtime y exponer
  su ubicación en el registry para que el CMS lo lea.

### 7. Validación de Fase 1 (ADR 0096) con este pipeline

Se publicó `hello-module` (vanilla, self-contained, Shadow DOM) a
`C:\LOCAL_CDN\synergos\hello-module/vanilla/{0.1.0,v0,latest}` + entrada en
`registry.json`. Prueba **end-to-end** el module-mount (el CMS resuelve
`moduleAlias="hello-module"` → emite `<synergos-hello-module>` que hidrata) y
el **POC tokens→Shadow DOM** (el bundle estiliza con `var(--syn-color-brand-500)`
etc.; las custom properties heredan a través del shadow boundary por spec —
verificar visualmente al montar). El camino **Angular + import map ya está
habilitado** en el CMS (§4 existe en `_SynHostRuntime.cshtml`); un módulo
Angular real solo requiere construir+publicar su bundle (lado UI, vía el
CLI) — el CMS lo monta igual que cualquier `elementSyn*` Angular.

## Consequences

**Positivas**
- Confirma y consagra una arquitectura que ya estaba ~70% construida, en vez
  de inventar una nueva.
- Bundles chicos + runtime compartido = páginas rápidas con muchos módulos.
- Hot-reload + SSR ya funcionan; el norte está claro y es alcanzable.
- Un solo contrato sirve componente → app completa; el module-mount no es un
  caso especial.

**Costos / riesgos**
- La emisión del import map en el CMS es trabajo NUEVO y es el bloqueante de
  los módulos Angular (los vanilla ya andan).
- El refactor del CLI es real (Nx-desacople, unificar registry, SRI). Mientras
  tanto el pipeline manual (lo usado para `hello-module`) funciona.
- Import maps: soporte de browser bueno hoy, pero un solo `<script type=importmap>`
  por documento y antes de los module scripts — disciplina de orden en el `<head>`.

## Decisiones abiertas

- **D-CDN-1**: ¿el refactor del CLI saca Nx del todo o lo deja opcional?
  (Arquitecto autorizó sacarlo.) Recomendado: resolución por convención +
  config, Nx opcional.
- **D-CDN-2**: ¿el CMS emite import map para "todos los frameworks
  publicados" o solo los presentes en la página? Recomendado: solo los
  presentes (más liviano), con fallback a todos si no se puede detectar.
- **D-CDN-3**: SRI sha384 en el publish vs lazy en el CMS. Recomendado: lazy
  en el CMS (un origen de verdad; el publish no recalcula).

## Relación con otros ADRs

Soporta 0096/0097/0098 (les da el mecanismo de distribución). Extiende 0012
(CDN consumida), 0015 (framework-agnóstico), 0089 (CDN filesystem +
hot-reload), 0083 (contratos CMS↔UI). Contrato base:
`docs/contracts/cdn-bundle-structure.md` + `host-bridge.md` + `dom-events.md`.
Docs UI: `Synergos.UI/SynergosDocs/{CDN_RUNTIME,BUILD_PIPELINE,DEV_CDN_MODE}.md`.
