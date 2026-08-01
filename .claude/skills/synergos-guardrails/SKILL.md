---
name: synergos-guardrails
description: LÉEME PRIMERO. Onboarding y guardrails del proyecto Synergos — cómo trabajamos, cómo lo hacemos y qué NO hacer NUNCA. Es un proyecto delicado (Umbraco 13 CMS que compone vitrinas SSR + apps Angular custom-element vía CDN local + design system tokenizado, 7 temas por-siteRoot). Actívala al ENTRAR al proyecto o antes de proponer cualquier cambio, para no violar los principios inviolables. Consolida en un solo lugar los 10 principios (grafo de dependencias, schema solo uSync no code-first, cero seeders, branding vía provider, no multi-tenant, CDN consumido no owned, GUIDs cuádruple), la premisa capital COMPONER-NUNCA-HARDCODEAR (spacing vía Layout Composer, colores vía tokens --syn-*), la verificación real (build verde ≠ hecho; navegador + 7 temas), el rebuild del runtime compartido, la higiene de commits/DB, y el pin de Umbraco 13. Es el índice que remite a las 21 skills específicas y a los ADRs.
model: claude-opus-4-8
---

# SYNERGOS — Guardrails y forma de trabajo (LÉEME PRIMERO)

Este es un **proyecto delicado**. Muchos cambios que "compilan" igual violan un
principio de arquitectura o rompen la composabilidad. Esta skill es el **índice de
entrada**: lee esto, y salta a la skill específica para la tarea. Cuando dudes, la
verdad canónica vive en `Synergos.CMS/CLAUDE.md`, los ADRs (`Synergos.CMS.Web/docs/adr/`)
y las memorias del agente (`~/.claude/projects/.../memory/MEMORY.md`).

## 0. Qué es Synergos (el modelo mental)

**Un motor, muchos productos.** Umbraco 13 (CMS) **compone** páginas (vitrinas SSR en
Razor) y **monta** apps Angular como **custom elements** (`<synergos-*>`) servidas desde
una **CDN local** (`C:\LOCAL_CDN`). La identidad (color/tipografía/logo) se aplica por
**siteRoot** vía **tokens** `--syn-*` y `data-theme` (7 temas: `dark`, `eventsNight`,
`silverGold`, `scholar`, `terraLux`, `meridian`, `light`). Cada dominio (Tienda, Booking,
Eventos, Propiedades, Educación, Blogs, Healthcare, Gobierno) es una **app real**, no una
fachada. **Es un producto, no un SaaS multi-tenant**: un deploy = un origen; multi-siteRoot
por hostname nativo de Umbraco.

## 1. Los 10 principios que NO se violan

| # | Principio | Por qué / dónde |
|---|-----------|-----------------|
| 1 | **Grafo de dependencias unidireccional** `Interfaces ← Application ← Web ← Tests`. `Application` NO referencia `Umbraco.Cms.*` ni `Microsoft.AspNetCore.*`. | ADR 0002. La lógica de presentación vive en Web. |
| 2 | **Schema vía uSync XML, NO code-first**. DocTypes/DataTypes/MediaTypes/Dictionary se autoran como XML en `Synergos.CMS.Web/uSync/v9/`. | ADR 0008 · `synergos-usync-author` · `synergos-usync-import` |
| 3 | **Composers centralizados** en `Synergos.CMS.Web/Composers/`. Ningún `IComposer` en Application. | ADR 0005 |
| 4 | **Seeders prohibidos**. Cero seeding automático en boot. El tooling dev va tras flag `Synergos:DevSeed:Enabled`. | ADR 0013 · `feedback_no_automatic_seeders` |
| 5 | **Branding vía provider**, nunca `if (brand.Key == "X")` en core. Usar `IBrandingProvider`/`IBrandThemeProvider`. | ADR 0010/0020 · `feedback_branding_via_provider` |
| 6 | **Framework-agnóstico para CDN**: los blocks CDN son `elementSyn*` (alias) + `<synergos-*>` (tag). El framework se resuelve en runtime, no en schema/C#. | ADR 0015 · `feedback_synhost_naming_convention` |
| 7 | **CDN contract es CONSUMIDO, no owned**. `IBundleRegistryClient` es la seam; cero paths cableados. | ADR 0012 · `feedback_cdn_contract_consumed` |
| 8 | **No multi-tenant SaaS**. Un deploy = un origen. Multi-siteRoot por hostname. Prohibido `ITenantContext` o tenant middleware. | `feedback_product_not_saas_multitenant` |
| 9 | **Tests por seam** (empty/happy/filter/idempotent). Cada seam nuevo ship con tests. | ADR 0075 · `synergos-test-author` |
| 10 | **GUIDs verificados cuádruple** antes de cualquier XML uSync nuevo. El agente escribe XML con GUID fresco; el arquitecto corre Import. | `feedback_no_preassigned_guids_usync` · `feedback_guid_block_element_collision` |

## 2. La premisa capital: COMPONER, nunca hardcodear

Todo el look es **código**; el contenido **no**. Nada de contenido baked ni estilos
ad-hoc. Se **compone** en el CMS (Umbraco → front) y se **estila** vía tokens.

- **Contenido y estructura** → se componen en el CMS (Block Grid, element types,
  data types). El editor arma la página; el agente provee los "bloques de Lego".
- **Espaciado** → se **compone** con los knobs del Layout Composer
  (`compDomSpacing`: spacingTop/Bottom/Inline), **NUNCA** con padding/margin en CSS.
  El arquitecto insiste mucho en esto. → `feedback_compose_spacing_via_layout_composer`
- **Color/tipografía/radios/sombras** → tokens `--syn-*` con fallback obligatorio
  (`var(--syn-X, default)`), por `data-theme`. Cero HEX hardcodeado en SCSS de UI.
  → ADR 0094 · `reference_design_line_canonical` · `feedback_design_system_8pt_grid`
- **Lógica pesada** → puede vivir dentro del Angular (si el bloque viene por CDN). Lo
  que NO puede es que el contenido/estilo esté cableado: debe ser ajustable, editable.
- Espaciados = múltiplos de 8 (grilla de 8pt). Auditar `--syn-space-*` antes de aprobar UI.

> Filtro de 3 preguntas antes de crear una `comp*` nueva: ver
> `feedback_composition_design_solid` (doc 06). Preferir los `elementSyn*` de la CDN que
> hidratan, no stubs (`feedback_prefer_cdn_angular_components`).

## 3. Schema, GUIDs y uSync (lo más frágil)

- Schema = **XML uSync** (SSOT). El agente escribe el XML; **el arquitecto corre uSync
  Import manualmente** desde el backoffice. El agente NO ejecuta import ni toca la DB.
- **Pickers por semántica**: URLs→MultiUrlPicker, media→MediaPicker3, enums→Dropdown,
  booleans→TrueFalse. → `feedback_picker_semantics` (ADR 0021)
- **Key nueva** si cambia el `<Type>` (storage). Reusar corrompe data legacy.
- `IsElement` es **inmutable** post-creación (false→true no propaga vía uSync).
- Descripciones editor-facing ≤120 chars, 1 frase, sin jerga ADR.
- Iconos: **no inventar** — verificar contra `reference_umbraco13_icons` (627 stock).
- Detalle operativo: `synergos-usync-author` · `synergos-usync-import` · `synergos-schema-audit`.

## 4. Verificar de verdad — "build verde ≠ hecho"

Un `dotnet build` verde y hasta un smoke HTTP verde **NO** garantizan integración viva.
"Hecho" = verificado en navegador.

- ⚠️ **Una verificación mal montada NO es una verificación, y engaña más que no hacerla.**
  Antes de creerte cualquier cifra, corre lo que la refutaría:
  - **curl → query de CONTROL primero.** Un `[FromQuery]` mal nombrado **no falla: devuelve
    TODO**. `?text=` sobre un endpoint cuyo param es `q` no da 400 — da el catálogo entero,
    y tu "filtro funciona" es humo. Corre `?q=xxnoexiste` (debe dar **0**) y compara contra
    el total sin filtro: si tu resultado "filtrado" == el total, no filtró.
    **Los 5 catálogos usan `q`, no `text`.** → `feedback_live_check_needs_control_query`
  - **test → mutar y confirmar que FALLA.** Un test que pasa con el código roto es peor que
    ninguno. Si no puedes romperlo a propósito, no prueba lo que dice su nombre.
  - **workflow de revisión → mirar `agents_error` antes que el resultado.** Si los
    refutadores mueren, devuelve `confirmed: []`, que parece un aprobado y es un artefacto.
  - **Desconfía del resultado que confirma demasiado bien.**
- **Hidratación**: `customElements.get('synergos-X') === true` + data real a la vista
  (no `undefined`/`NaN`/mock). → `synergos-app-verify`
- **Contrato CMS↔UI**: la UI es la fuente de verdad; el backend emite las claves que la
  UI lee (ADR 0083). Build verde no atrapa drift de claves JSON. → `synergos-contract-drift`
- **7 temas por-siteRoot**: un token puede romper contraste en UN solo tema. Verificar en
  todos, no solo `light`. → `feedback_verify_all_siteroot_themes`
- **Responsive**: sin overflow horizontal a 375px. El fix se compone/tokeniza.
- **Runtime híbrido**: 3 gotchas de montaje (alias camelCase, `Layout=null` sin
  `_SynHostRuntime`, prop JSON-array como string). → `feedback_synhost_mount_hydration_gotchas`

## 5. El runtime compartido (te va a morder si no lo sabes)

`@synergos/shared` está **externalizado** como un único `sg-shared.js` (import map), NO
bundleado en cada app. Si tocas `Synergos.UI/platforms/angular/libs/shared/`, **OBLIGA**
regenerar el runtime, o las apps no hidratan (a veces sin error claro):

```powershell
Set-Location "C:\Users\HITMA\Desktop\synergos\Synergos.UI"
npm run build:runtime ; npm run publish:runtime   # (o: npm run release:angular)
```
Luego **Ctrl+Shift+R** (el runtime es immutable/versionado; F5 sirve cache viejo). Síntoma:
`customElements.get()=false` / `Failed to fetch dynamically imported module` /
`does not provide an export named 'X'`. → `synergos-cdn-build` · `synergos-app-verify` ·
`feedback_shared_runtime_rebuild_required`.

## 6. Higiene: commits, DB, ops

- **SON TRES REPOS GIT, no uno.** Te va a morder el primer día:

  | Repo | Qué contiene | Ojo |
  |---|---|---|
  | `synergos/` (raíz) | `refactor-docs/` (docs rectores + índice §11.2 de ADRs), `.claude/skills/` | **Ignora `Synergos.CMS/` y `Synergos.UI/`** |
  | `synergos/Synergos.CMS/` | Todo el C# + Razor + uSync + **los ADRs** | Repo propio |
  | `synergos/Synergos.UI/` | Todo el Angular/Nx | Repo propio |

  Consecuencia práctica: **un ADR y su entrada en el índice §11.2 NO caben en un commit**
  (ADR → repo CMS; índice → repo raíz). Igual con un cambio backend+UI: son dos commits.
  Un `git add` que cruce la frontera falla o no ve nada. → `synergos-adr-author`
- **Commits atómicos** por fase, con prefijo (`feat`/`fix`/`refactor`/`docs`/`chore`),
  subject <70 chars, body con el POR QUÉ. **Nunca mezclar feature + refactor.**
- **La DB nunca se commitea.** Backups SQLite **externos** al repo, en
  `C:\Users\HITMA\Desktop\synergos-backups\`. → `feedback_backups_external_to_repo`
- **Operar la DB** solo con el protocolo seguro (stop CMS → checkpoint WAL → backup →
  operar). → `synergos-db-ops`
- **PowerShell** para ops del arquitecto; ediciones bulk con `[IO.File]::WriteAllBytes`
  + BOM (Set-Content da mojibake). → `feedback_powershell_utf8_bulk_edits`
- **No skippear hooks** git (`--no-verify`, `--no-gpg-sign`) sin pedido explícito.
- **Instrucciones backoffice neutrales**: describir intención + metadatos, no el path UI exacto.

## 7. Umbraco 13 — pinned

- **13.13.1, NO upgrade a 14+** sin ADR nuevo (14+ descontinuó Macros, cambió Block Grid a
  Lit/TS, requiere .NET 9+). NU1902 (moderate, sin patch en 13.x) es aceptado. → ADR 0001
- **Sin Management API para contenido** (esa REST es v14+). El contenido se autora
  **server-side** vía `IContentService` / el motor de fill (`POST /dev/fill-synergos-pages`).
  → `synergos-content-fill` · `synergos-cms-author`
- Trampas runtime: `@inherits UmbracoViewPage<T>` (no existe `IUmbracoHelper`, CS0234);
  BlockGrid server-side siembra multi-value con `"[]"`. → `feedback_razor_inject_inherits_pattern` ·
  `feedback_serverside_blockgrid_authoring`

## 8. Qué NO hacer NUNCA

| ❌ Nunca | ✅ En su lugar |
|---------|----------------|
| Code-first para schema (crear DocType/DataType en C#) | XML uSync + Import manual del arquitecto (ADR 0008) |
| Hardcodear padding/margin en CSS para espaciar secciones | Knobs `compDomSpacing` del Layout Composer |
| Hardcodear HEX de color en SCSS de UI | Tokens `var(--syn-*, fallback)` por tema |
| Bakear contenido en el template o en el DTO | Componer en CMS / poblar el stub o dominio |
| `if (brand.Key == "X")` en core | `IBrandingProvider` / `IBrandThemeProvider` (ADR 0010) |
| Seeder automático en boot | Tooling tras flag `Synergos:DevSeed:Enabled` (ADR 0013) |
| Tenant middleware / `ITenantContext` | Multi-siteRoot por hostname (no SaaS) |
| Cablear paths de bundles CDN | `IBundleRegistryClient` (CDN consumido, ADR 0012) |
| `Application` con `using Umbraco.Cms.*` / `Microsoft.AspNetCore.*` | Application es lógica pura (ADR 0002) |
| Escribir matching/facetado/orden propio en un catálogo | Descriptor declarativo sobre `ICatalogIndex` (ADR 0107). **Si hay que tocar el motor para acomodar un vertical, el descriptor está mal modelado** |
| Un store JSON dedicado nuevo | `IJsonEntityStore` + const `ResourceType` (ADR 0105) |
| Comparar texto es-CO con `.Contains(x, OrdinalIgnoreCase)` | `CatalogText` — pliega tildes y **preserva la ñ** (`año` ≠ `ano`). Sin esto, `"bogota"` devuelve CERO (ADR 0107) |
| Un campo que promete algo y nadie lo cumple (`Scope` sin lector, `Version` sin caché) | Borrarlo. Es peor que no tenerlo: el siguiente confía y el fallo es silencioso (ADR 0107) |
| Pre-asignar GUIDs en C# / reusar Key al cambiar `<Type>` | GUID fresco verificado cuádruple; Key nueva |
| Dar por "hecho" con build verde | Verificar en navegador (`customElements.get` + data + 7 temas) |
| Editar el `*.model.ts`/template Angular para que "calce con el backend" | La UI es fuente de verdad; el backend hace reshape (ADR 0083) |
| Publicar solo bundles de app tras tocar `libs/shared` | Cerrar con `build:runtime`+`publish:runtime` + Ctrl+Shift+R |
| Commitear la DB / mezclar feature+refactor / skip hooks | DB externa; commits atómicos; hooks siempre |
| Correr uSync Import o tocar la DB como agente | Lo hace el arquitecto; el agente escribe XML y avisa |
| Upgrade de Umbraco a 14+ | Pinned 13.13.1 (ADR 0001) |
| `.Root()` cuando quieres el siteRoot | `AncestorOrSelf("siteRoot")` (`.Root()` = platformRoot umbrella → barre todos los siteRoots) |
| 2+ agentes en paralelo sin pedido explícito | Uno, salvo que el arquitecto lo pida |

## 9. Mapa de skills — a dónde ir

| Tarea | Skill |
|-------|-------|
| Entender reglas / qué NO hacer | **synergos-guardrails** (esta) |
| Levantar el stack (CMS+CDN+dev) | `synergos-run-dev` · semáforo: `synergos-health-check` |
| Escribir/editar schema uSync | `synergos-usync-author` → import: `synergos-usync-import` |
| Auditar schema (orphans/drift) | `synergos-schema-audit` · mapa: `synergos-element-inventory` |
| Autorar contenido editorial | `synergos-content-fill` · `synergos-cms-author` |
| Subir una imagen a media | `synergos-media-upload` |
| Compilar y publicar un elemento a la CDN | `synergos-cdn-build` |
| Verificar apps en navegador (hidratación/leaks/temas) | `synergos-app-verify` |
| Smoke post-deploy (HTTP/placeholders/SEO) | `synergos-smoke-test` |
| Arreglar drift de contrato CMS↔UI | `synergos-contract-drift` |
| Enriquecer un dominio best-in-class | `synergos-domain-enrich` |
| Tests de un seam | `synergos-test-author` |
| Escribir un ADR | `synergos-adr-author` |
| Ops de DB (seguras) | `synergos-db-ops` |
| Abrir / cerrar una Ola | `synergos-ola-open` / `synergos-ola-close` |
| Bootstrap + empalme UI del arquitecto | `synergos-architect` |

## 10. Dónde vive la verdad

| Pregunta | Fuente |
|----------|--------|
| "¿Por qué esta decisión?" | `Synergos.CMS.Web/docs/adr/NNNN-*.md` |
| "¿Qué DocTypes/DataTypes/Dictionary hay?" | `Synergos.CMS.Web/uSync/v9/` |
| "¿Cómo se integran CMS↔UI?" | `Synergos.CMS.Web/docs/contracts/` (5 contratos, ADR 0083) |
| "¿Estado de la migración?" | `refactor-docs/architecture/00-current-state-synergos-cms.md` §11 |
| "¿Guardrails no escritos en ADR?" | `~/.claude/projects/.../memory/MEMORY.md` (sección Feedback) |
| "¿Reglas del lado UI?" | `Synergos.UI/CLAUDE.md` (signals only, prefix `syn-`, tokens con fallback) |
