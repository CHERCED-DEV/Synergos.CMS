# Synergos.CMS — Guía para agentes

> Punto de entrada para agentes LLM que vayan a escribir o modificar código
> en este proyecto. Lee en orden y ataja los errores comunes.

## 0. Los 10 principios que NO se violan

1. **Grafo de dependencias unidireccional**
   `Interfaces ← Application ← Web ← Tests`. Ninguna capa importa de
   arriba. Application **no** referencia `Umbraco.Cms.*` ni
   `Microsoft.AspNetCore.*`. Ver ADR 0002.
2. **Schema via uSync, no code-first**. Los DocType / DataType /
   MediaType / Dictionary se autoran como XML en
   `Synergos.CMS.Web/uSync/v9/`. Ver ADR 0008.
3. **Composers centralizados** en `Synergos.CMS.Web/Composers/`.
   Ningún `IComposer` vive en Application. Ver ADR 0005.
4. **Seeders prohibidos**. Cero seeding automático en boot. Tooling
   dev tras flag `Synergos:DevSeed:Enabled`. Ver ADR 0013.
5. **Branding via provider**, no `if (brand.Key == "X")` en core.
   Usar `IBrandingProvider` / `IBrandThemeProvider`. Ver ADR 0010 + 0020.
6. **Framework-agnóstico para CDN** — todos los blocks CDN-hosted se
   crean como `elementSyn*` + custom element tag `<synergos-*>`. Ver
   ADR 0015.
7. **CDN contract es CONSUMIDO**, no owned. `IBundleRegistryClient`
   es la seam; cero paths cablados. Ver ADR 0012.
8. **No multi-tenant SaaS**. Un deploy = un "origen". Multi-siteRoot
   via hostname nativo de Umbraco. Prohibido `ITenantContext` o
   tenant-resolver middleware.
9. **Tests por seam** — gate liftado post-Ola 190 (ADR 0075). Cada
   nuevo seam ship con tests (empty / happy / filter / idempotent).
   Tests project: **976 passing**. Memoria `feedback_tests_after_full_migration`
   (status: superseded).
10. **GUIDs verificados cuádruple** antes de cualquier XML uSync
    nuevo. Memoria `feedback_no_preassigned_guids_usync`.

## 1. Umbraco 13 LTS pinned

Umbraco 13.13.1 — **no upgrade** a 14+ sin ADR nuevo. La razón:
Umbraco 14+ descontinuó Macros, cambió el editor de Block Grid a
Lit/TS, y requiere .NET 9+. Ver ADR 0001.

NU1902 (vulnerabilidad moderate) es un conocido-sin-patch dentro
del branch 13.x. Aceptado.

## 2. Mapa del proyecto

```
Synergos.CMS/
├── Synergos.CMS.Interfaces/     seams puros (I*Provider, I*Emitter, ISchemaHealthProbe)
├── Synergos.CMS.Application/    lógica de aplicación + DTOs + Configuration POCOs
├── Synergos.CMS.Web/            host Umbraco + ASP.NET + views + composers
│   ├── App_Plugins/             plugins backoffice (LayoutComposer AngularJS)
│   ├── Composers/               wiring de arranque
│   ├── Controllers/             RenderControllers + ApiControllers
│   ├── Notifications/           notification handlers
│   ├── Services/                Umbraco-dependent services (LayoutCssBuilder, FlowResolver, etc.)
│   ├── Views/                   Razor templates + partials + blockgrid components
│   ├── docs/
│   │   ├── adr/                 129 ADRs (0001-0130, sin 0016) — SOURCE OF TRUTH
│   │   ├── contracts/           los 5 contratos CMS↔UI + harness Vitest
│   │   └── umbraco/             cdn-contract.md (externalmente bloqueado)
│   └── uSync/v9/                SCHEMA AUTORITATIVO
│       ├── ContentTypes/        DocTypes + ElementTypes + Compositions (243 archivos)
│       ├── DataTypes/           109 archivos (57 DTSelect*) + UrlPicker/MediaPicker/Tags/ContentPicker
│       ├── Dictionary/          i18n es-CO + en-US (481 keys)
│       ├── Languages/           es-CO (default) + en-US
│       ├── MediaTypes/          synImage + synDocument + synIcon + los stock de Umbraco
│       ├── MemberTypes/         member
│       ├── Templates/           Razor template registry (14)
│       ├── Content/             contenido editorial autorado (ADR 0129) — lo exporta
│       │                        uSync al guardar; el agente NO lo autora
│       └── Media/               nodos de la biblioteca (binarios en wwwroot/media/)
├── Synergos.CMS.Tests/          xUnit — 976 tests passing (gate liftado ADR 0075)
└── Synergos.CMS.Benchmarks/     BenchmarkDotNet (WebhookSigner + BridgeContextSerializer)
```

## 3. Dónde está la verdad

| Pregunta                        | Dónde mirar                                                |
|---------------------------------|------------------------------------------------------------|
| "¿Por qué se tomó esta decisión?" | `Synergos.CMS.Web/docs/adr/NNNN-*.md` — índice en `docs/adr/README.md` |
| "¿Qué DocTypes existen?"         | `uSync/v9/ContentTypes/`                                   |
| "¿Qué Dictionary keys hay?"      | `uSync/v9/Dictionary/` (481 archivos .config — alias PascalCase, filename lowercase por convención uSync) |
| "¿Qué compositions y para qué?"  | `uSync/v9/ContentTypes/compdom*.config` + `compcontent*.config` |
| "¿Hay compositions reservadas sin consumers?"  | Sí. Marker `[Bloqueado externamente - ...]` o `[Disponible — sin consumers actuales]` al inicio de `<Description>`. NO son orphans; son scaffolding tracked. Cap-260 audit (Cap-270 Batch C) las reconoce. |
| "¿Cómo se acopla con el UI?"     | `Synergos.CMS.Web/docs/contracts/` — los 5 contratos. Es la ÚNICA superficie de acople. |
| "¿Qué elementos publica el CDN?" | Repo hermano `Synergos.UI`, `vitals/contracts/src/element-registry.json` |

> **Fuentes que NO viven en este repo.** `refactor-docs/` (status de la
> migración, inventario del legado) y el `MEMORY.md` del agente son locales de
> la máquina del arquitecto y **no están versionados**. Un agente que corra en
> un clon limpio —CI, contenedor, Claude Code on the web— no los tiene: no los
> cites como si estuvieran, y si necesitás ese contexto, pedilo.

## 4. Flujo de trabajo para un cambio

### 4.1 Cambio de schema (DocType / DataType / Dictionary)

1. Si introduce GUIDs nuevos, generarlos con `[guid]::NewGuid()` y
   verificar 0 colisiones con `grep -rl $guid Synergos.CMS/Synergos.CMS.Web/uSync`.
2. Escribir el XML uSync directo en `Synergos.CMS.Web/uSync/v9/{tipo}/`.
3. Si es icono: verificar que existe en `tools/umbraco13-icons-stock.txt`
   (627 iconos, versionado en el repo) — no inventar.
4. Correr `node tools/usync-audit.mjs` — 8 checks: colisión de GUID,
   compositions huérfanas, refs rotas, iconos, alias de Dictionary,
   cross-check `<Definition>`↔`<DataType Key>`, DataTypes huérfanos y
   mojibake. Es el mismo gate que corre en CI.
5. El arquitecto corre uSync Import desde backoffice manualmente para
   aplicar al DB.

### 4.2 Cambio de runtime C# / Razor

1. Seguir el grafo de dependencias estricto.
2. `dotnet build Synergos.CMS/Synergos.CMS.Web/Synergos.CMS.Web.csproj
    -v quiet --no-dependencies` — esperar 0 CS errors. Los
   warnings MSB3021 de file-locks son esperados mientras el Web
   corre (PID locking DLLs).
3. Si el Web project no está corriendo, build directo (sin
   `--no-dependencies`) para validar cross-project.

### 4.3 Commit

- Mensaje con type prefix: `feat`, `fix`, `refactor`, `docs`, `chore`.
- Subject bajo 70 chars.
- Body explica WHY + cómo se aplicará.
- Co-Authored-By al final si el agente debe aparecer.
- Commits atómicos por fase. Nunca mezclar feature + refactor.

## 5. Memorias de agente — los guardrails no escritos en ADR

> ⚠️ **Este fichero no está en el repo.** Vive en
> `~/.claude/projects/c--Users-HITMA-Desktop-synergos/memory/MEMORY.md`, en la
> máquina del arquitecto. La lista de abajo es el índice de lo que contiene,
> para que un agente sin acceso sepa **qué reglas existen** aunque no pueda
> leer el detalle — y para que las pida en vez de inventarlas.

Las memorias relevantes antes de proponer cualquier ola:

- `feedback_composition_design_solid` — filtro de 3 preguntas antes
  de crear una `comp*` nueva.
- `feedback_usync_hybrid_ssot` — schema en XML, nunca code-first.
- `feedback_no_automatic_seeders` — cero seeders en boot.
- `feedback_branding_via_provider` — no `if (brand.Key == "X")`.
- `feedback_product_not_saas_multitenant` — no tenant middleware.
- `feedback_tests_after_full_migration` — no proponer tests aún.
- `feedback_cdn_contract_consumed` + `feedback_cdn_integration_is_core`.
- `feedback_synhost_naming_convention` — `elementSyn*` +
  `<synergos-*>` DOM tag.
- `feedback_variations_culture_default` — Culture por default, Nothing
  solo para datos compartidos.
- `feedback_picker_semantics` — URLs → MultiUrlPicker, media →
  MediaPicker3, enums → Dropdown, booleans → TrueFalse (ADR 0021).
- `feedback_editor_description_style` — descripciones schema ≤120
  chars editor-facing, sin ADR-jargon.
- `feedback_powershell_utf8_bulk_edits` — usar `[IO.File]::ReadAllText`/
  `WriteAllBytes` con BOM explícito. Set-Content causa mojibake doble.
- `feedback_umbraco_icon_library` — verificar iconos contra stock.
- `feedback_guid_block_element_collision` — procedimiento cuádruple.
- `feedback_no_preassigned_guids_usync` — agente-autor escribe XML con
  GUID fresco verificado.
- `feedback_ola_execution_flow` — flujo estándar.
- `feedback_windows_powershell_native` — comandos del arquitecto.
- `feedback_backups_external_to_repo` — backups SQLite en
  `C:\Users\HITMA\Desktop\synergos-backups\`.
- `feedback_neutral_backoffice_instructions` — describir intención, no
  path UI exacto.
- `feedback_non_destructive_smoke_first` — primer import no-destructivo.
- `feedback_dev_setup_hygiene` — commits atómicos, DB nunca commiteada.

## 6. Prohibiciones explícitas

- **No copiar-pegar del legado**. `_archive/fails/Synergos.CMS.epicfail*`
  es referencia histórica. Cualquier port requiere re-evaluación
  (inventario `05-legacy-refinement-inventory.md` tiene el veredicto
  por familia: REFINAR / REDISEÑAR / DESCARTAR / DIFERIR / DONE).
- **No introducir abstracciones prematuras**. Sin `Shared/`,
  `Common/`, `Utils/`. Interfaces solo cuando hay 2+ implementaciones
  o es genuina seam de extensión.
- **No agregar paquetes NuGet** sin ADR o sin verificar la versión
  en nuget.org (memoria `feedback_verify_nuget_versions`).
- **No skippear hooks git** (`--no-verify`, `--no-gpg-sign`) sin
  petición explícita.
- **No usar `-i` interactivo** en git (rebase / add) — no hay TTY.

## 7. Build verification

Los paths son relativos a la raíz del repo (que ES `Synergos.CMS/`; no hay
carpeta anidada con ese nombre).

```bash
# Application compila clean (sin warnings CS):
dotnet build Synergos.CMS.Application/Synergos.CMS.Application.csproj -v quiet

# Web compila clean (solo MSB3021 file-lock esperados si Web corre):
dotnet build Synergos.CMS.Web/Synergos.CMS.Web.csproj -v quiet --no-dependencies

# Suite completa:
dotnet test Synergos.CMS.sln -v quiet

# uSync Import: lo hace el arquitecto manualmente desde backoffice —
# agente NO ejecuta import desde CLI ni toca la DB.
```

### Los gates de Node — corren sin SDK .NET

Los cuatro son los mismos que gatean los PRs. No necesitan `dotnet`, así que
un agente en un contenedor sin SDK **sí puede** verificarlos:

```bash
node tools/usync-audit.mjs        # 8 checks de schema uSync
node tools/check-css-parity.mjs   # G-3: toda clase syn-* emitida tiene CSS
(cd Synergos.CMS.Web/docs/contracts/tests && npm ci && npm test)  # contratos
```

El cuarto es **cross-repo** y necesita `Synergos.UI` clonado como hermano
(`../Synergos.UI`), porque valida las dos mitades del acople:

```bash
(cd ../Synergos.UI && npm run cms:validate)   # registry ↔ DocTypes
(cd ../Synergos.UI/platforms/angular && node tools/sync-tokens.mjs --check)
```

## 8. Layout Composer — el feature más maduro

Después de las Olas 42 → 44 el Layout Composer es end-to-end:

- **14 Layout Preset ElementTypes** en `uSync/v9/ContentTypes/
  elementlayout*.config`: Section, Container, Stack, Grid, Column,
  1Col, 2ColEven, MainSidebar, 3Col, 4Col, HolyGrail, SidebarMain,
  Hero, SnippetRef.
- **Block Grid con areas** (`DTBlockGridSections.config`) permite al
  editor dropear presets al root de `sections` y cualquier elemento
  de contenido (148 blocks) dentro de las areas.
- **Plugin backoffice** `App_Plugins/LayoutComposer/` con custom
  views + SVG thumbnails + JS defaults pre-drop.
- **Runtime SSR** `Views/Partials/blockgrid/Components/
  elementLayout*.cshtml` con semantic HTML landmarks
  (nav/main/aside por area alias).
- **Server-side defaults handler** `LayoutPresetDefaults.cs` refuerza
  per-prop fill si el JS no corrió.
- **Starter scaffold opt-in** via
  `Synergos:LayoutComposer:EnableStarterScaffold`.
- **Reusable snippets** (Ola 42.10) via `elementLayoutSnippetRef` que
  referencia un `reusableBlock` de Ola 34.
- **compDom* universal** (Ola 43.15/43.16): los 156 element types
  tienen compDomClass + compDomVariant + compDomVisibility +
  compDomAttributes. El wrapper `SynHost/_Wrapper.cshtml` (Ola 44.1)
  aplica estos props al HTML emitido por los SynHost partials.
- **SEO <head>** (Ola 44.2): `Views/Shared/_SeoHead.cshtml` consume
  compSeo (seoTitle/Description/canonicalLink/ogImage/ogType/
  metaRobots) con fallback cascade a siteConfigSettings del brand
  activo (default* + socialOgImage).

Ver ADR 0017 (con 2 addenda: Ola 42.6 + Ola 42.7) para el modelo.
Ver ADR 0021 para el mapping canonical DataType ↔ editorial intent.

## 9. Tareas bloqueadas externamente

- **HttpBundleRegistryClient** — `docs/umbraco/cdn-contract.md` lista
  los 5 puntos que el CDN team debe publicar. Hasta entonces,
  `StubBundleRegistryClient` sigue activo (siempre retorna null, los
  71 `elementSyn*` emiten placeholder HTML comment).
- **Experience CDN** (9 DocTypes) + `compBehaviorTracking` +
  `compBehaviorInteraction` — mismo bloqueo.

## 10. Cuando termines una tarea

1. `dotnet build` (Web) → 0 CS errors.
2. `git log --oneline -5` → commits atómicos legibles.
3. Si tocaste schema: el arquitecto correrá uSync Import manualmente.
4. Actualiza memorias del agente si aprendiste una regla nueva.
5. Actualiza `refactor-docs/architecture/00-current-state-synergos-cms.md`
   §11 si cambiaste algo estructural relevante.
