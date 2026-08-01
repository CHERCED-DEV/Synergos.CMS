# Capa UI y distribución a CDN

Repo auditado: `/workspace/synergos.ui` (solo lectura). Cruce contra `/home/user/Synergos.CMS` para verificar el acople real, no la documentación.

## Resumen ejecutivo

El catálogo publicable son **153 filas de registro** (`vitals/contracts/src/element-registry.json`) que resuelven a **141 tags DOM únicos** (62 module / 51 composition / 40 primitive por tier declarado). De esas 153 filas, **125 viven como apps Angular reales**, **8 más como dominio "shop"** (Angular, fuera del árbol `elements/`), y **20 se reparten entre React (4), Svelte (4) y Vanilla (3)** más 9 "experiences" repartidas entre los cuatro frameworks — de las cuales **5 no tienen ninguna app en ningún framework** (solo existen como fila de registro + modelo TS). Angular es, sin ambigüedad, el motor primario; React/Svelte/Vanilla no son andamios vacíos pero tampoco son catálogos paralelos — son un **programa deliberado de "canarios cross-framework"** (2 experiences por framework, más 1-2 elementos sueltos) para demostrar que el pipeline CDN es agnóstico de framework, confirmado por el propio `SynergosDocs/BUILD_PIPELINE.md` que documenta "6 cross-framework experiences".

El sistema de diseño está sano: 950-955 custom properties `--syn-*`, 6 temas nombrados + dark + auto-dark (8 rutas de render), generado desde una única fuente (`Synergos.CMS.Web/wwwroot/css/syn-tokens.css`) y sincronizado a UI vía `platforms/angular/tools/sync-tokens.mjs`. Solo se encontró **1 archivo** con colores hardcodeados fuera del sistema de tokens en todo `apps/elements`.

El hallazgo más importante NO está en el código UI en sí, sino en el **acople real CMS↔UI**: ejecutando el propio validador oficial (`node tools/validate-cms-contracts.mjs --cms-path=/home/user/Synergos.CMS`) contra los dos repos reales, aparecen **79 de 153 elementos (52%) sin su mirror de config tipado** (`ELEMENT_CONFIG_FIELDS`), 6 desajustes de baseline ya documentados (2 elementos publicados al CDN sin DocType — viajes/eventos —, 4 DocTypes vivos sin bundle CDN que muestran skeleton para siempre), y un `cms-contract-baseline.json` **creado hoy mismo, 2026-08-01**, que el propio comentario del archivo dice es "la primera vez que el validador corrió de verdad contra los dos repos" — es decir, esta deuda nunca se había medido hasta ahora.

El pipeline de publicación está **acoplado a una máquina concreta**: el CDN local por defecto es literalmente `C:\LOCAL_CDN\synergos` (Windows), hardcodeado en al menos 5 ficheros de `tools/`, solo evitable con variables de entorno explícitas.

## Catálogo de elementos por dominio de negocio

> **Nota metodológica**: `element-registry.json` NO tiene un campo `domain`. La clasificación abajo es por semántica de nombre (verificado abriendo el código de cada app), no un dato del repo. La mayoría del catálogo es deliberadamente genérico/reusable — solo 8 elementos son "domain shells" dedicados a una vertical.

| Dominio | Nº elementos | Ejemplos | Tier predominante | Madurez |
|---|---|---|---|---|
| Comercio (shop) | 10 | `product-card`, `product-grid`, `cart-summary`, `variant-picker`, `price-display`, `storefront`, `seller` | module/composition/primitive | VIVO (8 en `apps/domains/shop/`, con `project.json` real) pero **0/8 tiene `ELEMENT_CONFIG_FIELDS`** — el mirror CMS→UI no existe todavía |
| Contenido / marketing genérico | ~100 | `hero`, `banner`, `card`, `accordion`, `tabs`, `faq-section`, `testimonial-*`, `heading`/`paragraph` (familia `text-block`), `image-block`, `video-block` | los tres tiers | Mayoritariamente VIVO; es el núcleo maduro del Layout Composer (ver ADR 0017 en CMS) |
| Dashboard / analítica | 8 | `kpi-card`, `chart-bar`, `data-grid`, `data-table`, `stat-counter`, `stat-ticker`, `tree-view`, `key-value` | composition/module | PARCIAL — están en el registry y tienen app Angular, pero `stat-counter`/`chart-bar`/`data-grid` etc. están en la lista de 79 sin `ELEMENT_CONFIG_FIELDS` |
| Social / comunidad | ~10 | `comments-widget`, `social-proof`, `social-share`, `share-bar`, `poll`, `rating-stars`, `notification-center/toast/stack` | composition/module | PARCIAL, mismo motivo (sin config mirror) |
| Educación | 1 (domain shell) | `academy` (`platforms/angular/apps/elements/modules/academy/`) | module | VIVO — `academy.ts`+`.model.ts`+`.spec.ts`, importa `@synergos/shells`, pero sin `ELEMENT_CONFIG_FIELDS` |
| Salud | 1 (domain shell) | `ehr` | module | VIVO como app, mismo hueco de config mirror |
| Gobierno | 1 (domain shell) | `gov` | module | VIVO — usa `@synergos/shells` (`gov.ts:35`), mismo hueco |
| Inmobiliaria | 1 (domain shell) | `realty` | module | VIVO — usa `@synergos/shells` (`realty.ts:47`), tiene `mortgage.calc.ts` y `realty-fulfillment.strategy.ts` propios (lógica de negocio real, no maqueta) |
| Viajes | 2 | `travel-shell`, `pax-selector` | module/composition | **PARCIAL/ANDAMIO** — `pax-selector` (alias `elementSynPaxSelector`) está publicado al CDN pero **sin DocType en el CMS**: un editor no puede colocarlo (baseline `e1`, `tools/cms-contract-baseline.json:18`) |
| Eventos | 2 | `eventos`, `seat-map` | module/composition | **PARCIAL/ANDAMIO** — `seat-map` (`elementSynSeatMap`) mismo problema; ADR 0110 del CMS deja pendiente modelar el aforo como contenido |

Los 8 "domain shells" (academy, ehr, gov, realty, travel-shell, eventos, seller, storefront) comparten una arquitectura común: consumen `@synergos/shells` (checkout-wizard, discovery-shell, dynamic-form-shell, etc.) + tienen su propio `*.model.ts`, `*-api.client.ts` y `*.mock.ts`. Verificado en `platforms/angular/apps/elements/modules/{gov,realty}/src/*/gov.ts:35` y `realty.ts:47` (`import ... from '@synergos/shells'`).

## Los tiers explicados con ejemplos reales del repo

- **Primitive** — sin estado de negocio, un solo concepto visual. Ej: `avatar` (`platforms/angular/apps/elements/primitives/avatar/`), `badge`, `divider`. La familia `text-block` (heading/paragraph/rich-text/eyebrow/quote/label) son **6 filas de registro que comparten un solo bundle** (`synergos-text-block`) y cambian de forma según `variant`/`headingLevel` — documentado explícitamente como intencional en `SynergosDocs/ELEMENT_CONTRACT.md:278`.
- **Composition** — combina 2+ primitives con layout propio pero sin fetch de datos externos. Ej: `card`, `accordion`, `product-card` (compone `price-display` + `variant-picker`, ambos primitives/compositions del dominio shop).
- **Module** — unidad de contenido "servible" en Block Grid, con su propio layout y a veces su propio fetch. Ej: `hero`, `banner-slider`, `faq-section` (ensamblado server-side por el CMS: "Collection items... fully server-assembled by CMS resolvers", `vitals/contracts/src/element-config.contract.ts:13-14`). Los "domain shells" (`gov`, `realty`, `academy`...) también son tier `module` pero son mini-SPAs completas con API client + modelo de dominio, un salto de complejidad real frente a un `hero`.
- **Experience** — SPA autónoma que trae sus propios datos vía API interna; el CMS solo le pasa config estructural, no contenido. Cita textual: "Experience elements (feature-journey, insight-explorer, etc.) receive only structural config; they fetch their own data via internal APIs" (`vitals/contracts/src/element-config.contract.ts:11-12`). Las 9 experiences (`feature-journey`, `insight-explorer`, `media-explorer` en Angular; `content-carousel`, `quiz-flow` en React; `rating-widget`, `filter-board` en Svelte; `notification-stack`, `countdown-clock` en Vanilla) son el único tier repartido deliberadamente entre los 4 frameworks.

## Estado de los 4 frameworks

| Framework | Apps reales verificadas | Libs | Veredicto |
|---|---|---|---|
| Angular | 125 en `apps/elements/{primitives(27)+compositions(45)+modules(53)}` + 8 en `apps/domains/shop/` + 3 en `apps/experiences/` = **136** | `core`, `shared`, `shells` (10 shells con lógica real: checkout-wizard, discovery-shell, credential-wallet...), `shop`, `rendering`, `integrations`, `transaction-engine` | **VIVO** — motor primario, cubre el 89% del catálogo (136/153 filas de registro les corresponde una app Angular) |
| React | 4 (`stat-counter`, `pricing-card` bajo `elements/compositions/`; `quiz-flow`, `content-carousel` bajo `experiences/`) | `libs/shared` (2 archivos), `libs/core` (3 archivos) | **VIVO pero acotado a propósito** — `stat-counter.tsx` tiene 157 líneas + spec real, no un stub. React 19 + Vite real. No es un catálogo paralelo, es el canario de 2 experiences + 2 compositions |
| Svelte | 4 (`avatar`, `accordion` bajo `elements/{primitives,compositions}/`; `filter-board`, `rating-widget` bajo `experiences/`) | `libs/shared` (1 archivo), `libs/core` (2 archivos) | **VIVO pero acotado** — usa `<svelte:options customElement="synergos-avatar" />`, mismo tag DOM que la versión Angular (parity test deliberado, no elemento nuevo) |
| Vanilla | 3 (`hello-world` bajo `elements/primitives/`; `notification-stack`, `countdown-clock` bajo `experiences/`) | `libs/shared` (1 archivo), `libs/core` (2 archivos) | **VIVO pero mínimo** — `hello-world` es explícitamente "development placeholder — not a real CMS element type" (whitelisteado como `UI_ONLY_ALIASES` en `tools/validate-cms-contracts.mjs:322-324`) |

Confirmado por `SynergosDocs/BUILD_PIPELINE.md:18`: "Build all **6** cross-framework experiences" → `npm run build:experiences:cross` — coincide exactamente con las 6 experiences no-Angular encontradas (2×React + 2×Svelte + 2×Vanilla). **Pero ese script no existe**: `node -e "require('./package.json').scripts"` no tiene `build:experiences:cross`, ni `release:experiences`, ni `build:angular:dev/stable/changed/elements/experiences` — los 7 comandos documentados en `SynergosDocs/BUILD_PIPELINE.md:10-25,45,55-70` **no existen en `package.json` raíz** (verificado, `false` para los 7). La doc está adelantada al código o describe scripts internos de `platforms/angular/package.json` con otro nombre.

## El sistema de diseño

- **Fuente de verdad**: `Synergos.CMS.Web/wwwroot/css/syn-tokens.css` (1746 líneas, 955 `--syn-*`).
- **Espejo en UI**: `platforms/angular/libs/shared/src/styles/_tokens-bridge.scss` (1610 líneas, 950 `--syn-*`), generado por `platforms/angular/tools/sync-tokens.mjs` — el propio header dice "DO NOT EDIT MANUALLY" y trae timestamp de generación (`2026-07-21T16:32:56.022Z`).
- **Propagación CMS→UI**: el CMS emite el CSS de tokens en runtime; los componentes leen `var(--syn-X, $fallback)` para funcionar standalone en Storybook/dev-preview. Contrato documentado en `Synergos.CMS.Web/docs/contracts/css-tokens.md` (no auditado en detalle, pero referenciado consistentemente).
- **Temas**: 6 nombrados (`dark`, `silverGold`/`silver-gold`, `eventsNight`, `terraLux`, `scholar`, `meridian`) + light por defecto + una "octava ruta" de auto-dark sin `data-theme` explícito (`_tokens-bridge.scss:1503,1530` — comentario propio: "la que se olvida siempre porque no tiene `data-theme` y no sale en los barridos"). Hay un gate dedicado, `tools/audit-themes.mjs` (`npm run gate:themes`), justamente para esa ruta.
- **Desviaciones encontradas**: solo **1 archivo** con hex hardcodeado en todo `platforms/angular/apps/elements`: `platforms/angular/apps/elements/modules/academy/src/academy/academy.scss:191` (`color-mix(in srgb, var(--ac-brand) 65%, #1e1b4b)`), `:930` (`background: #0f172a` — reproduce a mano el valor exacto de `--syn-color-neutral-900` en lugar de referenciarlo), `:948` (`color-mix(in srgb, #fff 78%, transparent)`). Baja severidad, un solo archivo, pero es la única fuga real del sistema de tokens.

## El pipeline de build y publicación

Comandos reales (root `package.json`, `tools/*.mjs`):

| Fase | Comando | Qué hace |
|---|---|---|
| Build | `npm run build` | orquesta vitals → angular → runtime → react → svelte → vanilla |
| Test | `npm run test` | orquesta test por framework |
| Publish | `npm run publish:cdn` → `tools/publish.mjs` | escanea `dist/` de todas las plataformas y publica a `LOCAL_CDN` |
| Publish 1 elemento | `npm run publish:element` → `tools/publish-element.mjs` | `--project=react-hero --cdn C:\MY_CDN` |
| Release completo | `npm run release` | build + `contracts:validate` + `publish:runtime` + `publish.mjs --verify --clean` |
| Gate compuesto | `npm run contracts:validate` | `sync:tokens:check` + `element:audit` + `manifest:validate` + `cms:validate` + `cms:sync:check` |
| Auditoría de catálogo | `node tools/catalog.mjs`, `node tools/element-contract-audit.mjs` | inventario + consistencia registry↔mapper↔models↔inputs↔apps Nx |
| Validación cross-repo | `node tools/validate-cms-contracts.mjs [--cms-path=...]` | el único gate que compara contra los `.config` uSync reales del CMS |
| Temas | `npm run gate:themes` → `tools/audit-themes.mjs` | audita la "octava ruta" de dark mode |

**Acoplado a una máquina concreta** (evidencia con línea):
- `tools/catalog.mjs:31` — `const CDN_ROOT = process.env.CDN_ROOT ?? 'C:\\LOCAL_CDN\\synergos';`
- `tools/dev-cdn.mjs:38` — `const CDN_ROOT = resolve(process.env.SYNERGOS_CDN || String.raw\`C:\LOCAL_CDN\`);`
- `tools/publish-runtime.mjs:28` — mismo patrón
- `tools/lib/synergos-config.mjs:68` — `export const DEFAULT_CDN_ROOT = String.raw\`C:\LOCAL_CDN\`;`
- `tools/refresh-skill-catalog.mjs:44,142,235` — mismo `C:/LOCAL_CDN/synergos` (con un comentario propio, línea 38-42, admitiendo que ya hubo un incidente por un literal sin escape que rompía en no-Windows)
- Corriendo `node tools/catalog.mjs` en este contenedor Linux, imprime literalmente: `CDN registry not found at C:\LOCAL_CDN\synergos/registry.json — framework availability shown as "not published"` — el fallback funciona (no crashea) pero la disponibilidad por framework queda invisible sin `CDN_ROOT`/`SYNERGOS_CDN` seteado.

**Qué está roto o incompleto**:
- 7 comandos de `SynergosDocs/BUILD_PIPELINE.md` no existen en `package.json` (ver sección Frameworks).
- `contracts:validate` (el gate que corre en release) SÍ incluye `cms:validate` → `node tools/validate-cms-contracts.mjs`, pero ese script solo encuentra el CMS real si está clonado como hermano `../Synergos.CMS` o si `SYNERGOS_CMS_PATH` está seteado (`tools/validate-cms-contracts.mjs:52-56`). En un checkout donde el CMS no es hermano (como este barrido, que lo tiene en `/home/user/Synergos.CMS`), el gate necesita el flag explícito o **corre en silencio sin validar nada real** — riesgo de falso-verde en CI si el path no está configurado.
- Toda la maquinaria de validación cross-repo es **literalmente de hoy**: `tools/validate-cms-contracts.mjs` (mtime 2026-08-01 00:37), `tools/cms-contract-baseline.json` (mtime 2026-08-01 00:27), `tools/cms-sync.mjs` (mtime 2026-07-31 23:13). El comentario del propio baseline lo confirma: antes de esto el validador "salía 0 sin validar nada cuando no encontraba el CMS, así que la deriva nunca se vio".

## Shells, experiences y módulos completos

- **Shells** (`platforms/angular/libs/shells/src/`): 10 componentes reutilizables por los domain-modules — `detail-shell`, `discovery-shell`, `account-shell` (+ `tracking-timeline`), `authoring-wizard`, `checkout-wizard`, `message-center`, `results-map`, `dynamic-form-shell`, `credential-wallet`. Cada uno con `.ts` + `.scss` + `.spec.ts` — no son maquetas.
- **Experiences**: ver tabla de tiers arriba — 3 en Angular (`feature-journey`, `insight-explorer`, `media-explorer`), 6 repartidas en los otros 3 frameworks (2 cada uno). Las 9 SÍ tienen app real en algún framework — no hay gap de "app faltante" en este tier. El gap real de las experiencias es del lado CMS: son "código-first" (`SCHEMA_MANAGED_ALIASES` en `tools/validate-cms-contracts.mjs:371-380`) — Umbraco las crea al arrancar pero uSync todavía no las exportó como `.config`, así que hoy no aparecen en `uSync/v9/ContentTypes/`.
- **Módulos completos (domain shells)**: `academy`, `ehr`, `eventos`, `gov`, `realty`, `seller`, `storefront`, `travel-shell` — 8 mini-SPAs con modelo de dominio propio, cliente API propio y mocks propios, todas importando de `@synergos/shells`. `realty` es el más profundo: tiene lógica de negocio real (`mortgage.calc.ts`, `realty-fulfillment.strategy.ts`), no solo maqueta visual.

## Huecos y deuda (con fichero:línea)

1. **52% del catálogo sin mirror de config CMS↔UI**. `SYNERGOS_CMS_PATH=/home/user/Synergos.CMS node tools/validate-cms-contracts.mjs` → `[W4] Registry entries missing from ELEMENT_CONFIG_FIELDS (79)` — incluye toda la ola `elementSyn*` reciente (avatar-group, badge-group, calendar, data-grid, tabs, tooltip...) y los 8 domain shells completos. Sin este mirror en `vitals/contracts/src/element-config.contract.ts`, el "three-way mirror CdnConfig(C#)↔TS↔prop" que el propio archivo declara como obligatorio (líneas 1-16) no existe para más de la mitad del catálogo.
2. **2 elementos publicados al CDN sin DocType en el CMS** (`tools/cms-contract-baseline.json:17-29`, `e1_registryAliasMissingFromCms`): `elementSynPaxSelector` (Viajes) y `elementSynSeatMap` (Eventos) — un editor no puede colocarlos porque no existen en el backoffice.
3. **4 DocTypes vivos en el CMS sin bundle CDN** (`tools/cms-contract-baseline.json:32-56`, `e2_cmsAliasMissingFromRegistry`): `elementSynFaqSection`, `elementSynFeatureGrid`, `elementSynMediaText`, `elementSynTestimonialSection` — el editor SÍ puede colocarlos, el visitante ve el skeleton "CDN-offline" para siempre porque el partial Razor no tiene `FallbackHtml`. El propio baseline marca `media-text` como "el más barato de resolver" (SSR sin interactividad).
4. **Documentación de pipeline adelantada al código**: `SynergosDocs/BUILD_PIPELINE.md:10-25,45,55-70` documenta `build:angular:dev/stable/changed/elements/experiences`, `build:experiences:cross` y `release:experiences` — ninguno existe en `package.json` raíz (verificado programáticamente).
5. **Registry con 7 tags DOM duplicados por 2 filas** (`vitals/contracts/src/element-registry.json`): `accordion`, `divider`, `spacer`, `avatar`, `badge`, `text-block`(×7, intencional) y `countdown-clock`. De estos, divider/spacer/avatar/badge/countdown-clock tienen **ambas** aliases como DocTypes reales en el CMS (`elementstructdivider.config` + `elementsyndivider.config`, etc.) — dos DocTypes distintos compitiendo por el mismo Web Component, deuda explícitamente reconocida en `tools/validate-cms-contracts.mjs:326-357` como "legacy retenido por compat con contenido publicado viejo, pendiente de cleanup pass".
6. **Pipeline acoplado a `C:\LOCAL_CDN\synergos`** en 5+ ficheros de `tools/` (ver sección Pipeline) — funciona con fallback silencioso en Linux/CI pero oculta disponibilidad real por framework en `catalog.mjs`.
7. **`Synergos.CMS.Web/docs/umbraco/cdn-contract.md` bloqueado externamente** (mencionado en CLAUDE.md del CMS) — `StubBundleRegistryClient` sigue activo del lado CMS; combinado con el hallazgo #3 (4 DocTypes sin bundle), la superficie de "placeholder eterno" es mayor de lo que un solo repo deja ver.
8. **`academy.scss:191,930,948`** — 3 valores hex hardcodeados fuera del sistema de tokens (baja severidad, único archivo encontrado).

Nota de proceso: `tools/cms-contract-baseline.json` fue creado **hoy, 2026-08-01** — su propio comentario dice que es la primera vez que el validador corrió de verdad contra los dos repos clonados juntos ("hasta entonces salía 0 sin validar nada cuando no encontraba el CMS"). Los hallazgos #1-#3 de esta lista son, literalmente, nuevos para el propio equipo.

## Tabla maestra de artefactos publicables

| Categoría | Cantidad | Ubicación | Fuente de verdad |
|---|---|---|---|
| Filas en element-registry.json | 153 | `vitals/contracts/src/element-registry.json` | catálogo canónico |
| Tags DOM únicos | 141 | — | 12 filas son alias duplicados/variantes |
| Apps Angular (`elements/`) | 125 | `platforms/angular/apps/elements/{primitives,compositions,modules}` | 27+45+53 |
| Apps Angular (`domains/shop`) | 8 | `platforms/angular/apps/domains/shop/` | product-*, cart-*, price-display, quantity-selector, variant-picker |
| Apps Angular (`experiences`) | 3 | `platforms/angular/apps/experiences/` | feature-journey, insight-explorer, media-explorer |
| Apps React | 4 | `platforms/react/apps/elements/compositions/` (2) + `experiences/` (2) | stat-counter, pricing-card, quiz-flow, content-carousel |
| Apps Svelte | 4 | `platforms/svelte/apps/elements/{primitives,compositions}/` (2) + `experiences/` (2) | avatar, accordion, filter-board, rating-widget |
| Apps Vanilla | 3 | `platforms/vanilla/apps/elements/primitives/` (1) + `experiences/` (2) | hello-world, notification-stack, countdown-clock |
| **Total apps Nx (proyectos)** | **147** | — | 125+8+3 (Angular) + 4+4+3 (otros) = 147 (`element-contract-audit.mjs` cuenta 144 "Nx element projects" porque excluye algunas experiences duplicadas de conteo) |
| Manifests generados | 612 | `dist/manifests` (gitignored, generado bajo demanda por `manifest-gen.mjs`) | 1 manifest por combinación elemento×framework×build target |
| Mapper aliases (compat incluida) | 172 | `vitals/core/src/mappers/block.mapper.ts` | 153 canónicos + ~19 alias de compatibilidad histórica |
| Model files | 144 | `vitals/core/src/models/` | 9 menos que registry (comparten modelo, ej. text-block family) |
| CMS DocType aliases (`element*`/`experience*`) | 243 total en uSync, de los cuales ~149 con prefijo element/experience | `Synergos.CMS.Web/uSync/v9/ContentTypes/*.config` | cruce real vía `validate-cms-contracts.mjs` |
| Tokens de diseño (`--syn-*`) | 950-955 | CMS: `wwwroot/css/syn-tokens.css` (955) · UI: `_tokens-bridge.scss` (950) | fuente CMS, espejo UI |
| Temas | 6 nombrados + light + auto-dark | `_tokens-bridge.scss` | dark, silverGold, eventsNight, terraLux, scholar, meridian |
