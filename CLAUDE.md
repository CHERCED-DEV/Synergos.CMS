# Synergos.CMS — Guía para agentes

> Punto de entrada para agentes LLM que vayan a escribir o modificar código
> en este proyecto. Lee en orden y ataja los errores comunes.

## 0. Los principios que NO se violan

> **El repo tiene DOS árboles.** El del CMS (Umbraco, §0.A) y el de servicios
> (capacidades + orquestadores, §0.B). Se hablan **solo por HTTP** y hay gates
> que lo verifican. Antes de tocar nada, mirá en cuál de los dos estás.

### 0.A — El árbol del CMS

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
   Tests project: **2255 passing**. Memoria `feedback_tests_after_full_migration`
   (status: superseded). En el árbol de servicios el gate es más duro:
   además de tests, **mutación de cada gate** y **verificación con
   procesos reales** cuando el cambio cruza servicios.
10. **GUIDs verificados cuádruple** antes de cualquier XML uSync
    nuevo. Memoria `feedback_no_preassigned_guids_usync`.

### 0.B — El árbol de servicios

11. **Tres capas, una sola dirección.**
    `Capacidad (Synergos.Api.*) ← BFF (Synergos.Bff.*) ← consumidores`.
    **Todo el acople es HTTP**: ninguna referencia de ensamblado cruza
    capas, y ninguna API referencia el CMS ni al revés. Si pudiera
    llamarse en proceso, la capacidad sería una carpeta con ínfulas.
    Ver doc 06 + `BackendSegregationTests`.
12. **La capacidad es dueña del CUÁNDO; el orquestador, del QUÉ.**
    `Api.Booking` sabe «recurso + ventana + cupo»; **no** sabe que el
    recurso es un médico. Un sustantivo de negocio dentro de una
    `Api.*` rompe el build.
13. **`Ref(Kind, Id)` se guarda y se devuelve, NUNCA se ramifica.**
    Un `if (kind == "salud.profesional")` dentro de una capacidad la
    inutiliza para el siguiente dominio. Hay gate.
14. **El piso de la atomicidad**: algo es una capacidad si (a) puede
    decir NO sola y (b) es dueña de su almacén. **Lo que no tiene
    almacén es un tipo, no un servicio.** Ver doc 07.
15. **El molde es idéntico en las veinte.** Cuatro carpetas
    (`Contracts/ Domain/ Storage/ Endpoints/`), llave compartida,
    `/health`, todo bajo `/v1/`, sin `MapPut`/`MapPatch`, ruteo solo en
    `Endpoints/`. Ver doc 08 §4 + `ApiMoldTests`.
16. **Idempotencia primero.** La llave se resuelve **antes** de
    cualquier regla que dependa del estado. Al revés, un reintento
    choca con lo que él mismo creó (defecto real, ver §11).
17. **Se promueve al SEGUNDO consumidor, no antes.** `Synergos.Shared`
    esperó a seis; `Synergos.Bff.Core` esperó a que existiera
    `Bff.Tienda`. Es CLAUDE.md §6 aplicado con fecha, no con
    corazonada. Ver doc 10.
18. **Una compensación es un DATO, no una función**, y se anota en el
    instante en que existe lo que hay que deshacer. **Armada no es
    pendiente**: solo es trabajo cuando algo YA falló. Ver doc 09.

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
│   │   ├── adr/                 132 ADRs (0001-0133, sin 0016) — SOURCE OF TRUTH
│   │   ├── contracts/           los 5 contratos CMS↔UI + harness Vitest
│   │   └── umbraco/             cdn-contract.md (externalmente bloqueado)
│   └── uSync/v9/                SCHEMA AUTORITATIVO
│       ├── ContentTypes/        DocTypes + ElementTypes + Compositions (254 archivos)
│       ├── DataTypes/           129 archivos (67 DTSelect*) + UrlPicker/MediaPicker/Tags/ContentPicker
│       ├── Dictionary/          i18n es-CO + en-US (481 keys)
│       ├── Languages/           es-CO (default) + en-US
│       ├── MediaTypes/          synImage + synDocument + synIcon + los stock de Umbraco
│       ├── MemberTypes/         member
│       ├── Templates/           Razor template registry (14)
│       ├── Content/             contenido editorial autorado (ADR 0129) — lo exporta
│       │                        uSync al guardar; el agente NO lo autora
│       └── Media/               nodos de la biblioteca (binarios en wwwroot/media/)
├── Synergos.CMS.Tests/          xUnit — 2255 tests passing (gate liftado ADR 0075)
│   ├── Architecture/            LOS GATES: segregación (13) + molde (8) + capas (10)
│   │                            + imagen de contenedor (6) + compose (8)
│   │                            + despliegue (14, ADR 0133)
│   ├── Api/                     tests de reglas y servicio por capacidad
│   └── Bff/                     la compensación cruzada (48)
├── Synergos.CMS.Benchmarks/     BenchmarkDotNet (WebhookSigner + BridgeContextSerializer)
│
├── Synergos.Core/               EL VOCABULARIO. Ref, Money, TimeWindow, Rejection,
│                                Result, IdempotencyKey, Actor, Page,
│                                IdentityAssertion.
│                                CERO referencias. No sabe qué es ASP.NET.
├── Synergos.Shared/             fontanería de host. Llave compartida, Rejection→HTTP,
│                                libro de idempotencia, JsonCollectionStore, correlación.
│                                Solo puede referenciar Core — UNA flecha.
├── Synergos.Api.*/              LAS 20 CAPACIDADES, agnósticas. 132 endpoints.
│     Sessions · Booking · Identity · Audit · Notifications · Documents ·
│     Catalog · Pricing · Cart · Orders · Payments · Inventory · Workflow ·
│     Messaging · Signing · Consent · Engagement · Geo · Fulfillment · Moderation
├── Synergos.Bff.Core/           la máquina de sagas: deshacer, reintentar,
│                                rendirse, avisar. Promovida al segundo consumidor;
│                                el TERCERO (Eventos) y el CUARTO (Viajes)
│                                entraron sin tocarla.
└── Synergos.Bff.*/              LOS ORQUESTADORES. Salud, Tienda, Eventos y
                                 Viajes construidos; faltan Realty, Gob,
                                 Academy, Social.
```

> **El árbol de servicios está construido y verificado, pero casi sin
> conectar al producto**: de las 20 capacidades el CMS consume UNA
> (`Api.Sessions`, vía `HttpSearchAnalyticsStore`). Ver §11.

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
| "¿Por qué el backend está partido así?" | `docs/product/06-arquitectura-backend.md` |
| "¿Dónde para la atomicidad? ¿Qué es una capacidad?" | `docs/product/07-diseno-atomico-capacidades.md` |
| "¿Qué API necesita cada dominio? ¿Cuál es el molde?" | `docs/product/08-despiece-apis.md` — la matriz 20×9 y §4 |
| "¿Cómo se deshace lo que ya se hizo?" | `docs/product/09-compensacion-cruzada.md` |
| "¿Cuándo se promueve algo a una capa compartida?" | `docs/product/10-promocion-bff-core.md` |
| "¿Qué se hace con cada uno de los 46 `Stub*`?" | `docs/product/11-mapa-del-cableado.md` — hay gate (`WiringMapTests`) |
| "¿Qué rechaza esta capacidad?" | `Synergos.Api.X/Domain/XRules.cs` — es el único sitio |

> ⚠️ **De los seis docs de `docs/product/`, sólo el 11 está versionado.** Los
> cinco de arriba (06 a 10) **no están en el repo**: viven en la máquina del
> arquitecto, igual que `refactor-docs/`. Esta tabla los citaba como si
> estuvieran, y un agente en un clon limpio los busca y no los encuentra.
> Se pueden citar como fuente de autoridad —lo son— pero hay que **pedirlos**,
> no abrirlos.

> **Fuentes que NO viven en este repo.** `refactor-docs/` (status de la
> migración, inventario del legado), `docs/product/06` a `10` y el `MEMORY.md`
> del agente son locales de la máquina del arquitecto y **no están
> versionados**. Un agente que corra en un clon limpio —CI, contenedor, Claude
> Code on the web— no los tiene: no los cites como si estuvieran, y si
> necesitás ese contexto, pedilo.

## 3.bis El ticket va ANTES del código

> **Nada se codifica sin ticket.** Se abre, se discute, y recién ahí se escribe. Hay un gate de
> CI (`.github/workflows/ticket-first.yml`) que rechaza un PR sin issue referenciado — porque un
> proceso escrito como prosa se olvida y uno que rompe el build se cumple.

**Lo que el ticket garantiza es que la conversación pasó antes que el código. Nada más.**
No es una autorización que hay que esperar por cada cosa que aparece, ni una unidad de
trabajo que hay que respetar hasta el final: si al codificar la HU resulta ser otra cosa,
eso se escribe en el ticket y se sigue. Ver «la regla que hace que esto no estorbe», abajo —
está para leerse junto con esto, no como letra chica.

**El umbral, para que el proceso sobreviva:**

| | |
|---|---|
| **Ticket obligatorio** | cambia comportamiento, contrato o schema · es un defecto |
| **Sin ticket** | typo, comentario, formato, documentación → etiqueta `sin-ticket` en el PR |

Cuatro tipos, en `.github/ISSUE_TEMPLATE/`. Cada uno obliga a contestar lo que acá importa:

- **🐛 Defecto** — y sobre todo *por qué los tests no lo vieron* y *qué mutación lo reproduce*.
- **✨ Evolutivo** — las cuatro preguntas del refinamiento: qué problema del negocio, dónde vive
  con el filtro de atomicidad aplicado, qué rechaza y con qué código, cómo sabemos que quedó bien.
- **🔧 Mejora** — y *por qué ahora y no después*, que es la pregunta que mata a la mayoría, y
  está bien que las mate.
- **🔍 Hallazgo** — encontré algo haciendo otra cosa.

### La regla que hace que esto no estorbe

> **Lo que encontrás haciendo otra cosa se ANOTA y se sigue. Por defecto en un comentario
> del ticket que ya está abierto — no en uno nuevo.**

El proceso existe para que las cosas se hablen antes de codificarlas, **no para partir el
trabajo en pedazos que hay que esperar**. Un ticket nuevo es una espera nueva: alguien lo
tiene que leer, refinar y aprobar. Eso vale la pena cuando es trabajo de verdad separado, y
es puro peaje cuando no.

**El umbral, y ante la duda es comentario:**

| | |
|---|---|
| **Comentario en el ticket abierto** | una dificultad, una decisión que tomaste sobre la marcha, algo que no cumpliste y por qué, una duda que resolviste solo |
| **Issue aparte** | otro puede tomarlo sin tocar lo tuyo · lo pide otra área del código · se decidió NO hacerlo ahora y hay que poder buscarlo dentro de seis meses |

> **Y el trabajo se termina igual.** Encontrar algo no autoriza a entregar a medias: se sube
> el PR completo, con lo hallado anotado. Si de verdad hace falta un issue, se abre — pero
> **después de subir**, no en vez de.

Los enlaces a lo que se anotó van en la última sección del PR.

### Y lo que hace que el proyecto aprenda

Dos escrituras obligatorias **en el mismo commit** que las enseñó:

1. **Regla nueva aprendida → `CLAUDE.md` §5.** El índice de memorias es lo único que sobrevive
   a que se cierre una sesión.
2. **Algo de este fichero quedó obsoleto → se corrige acá.** Ver §10.7.

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
- **Sin firmas de agente.** Nada de `Co-Authored-By: Claude`, ni menciones a
  Anthropic, ni enlaces de sesión. Los commits los firma
  `Camilo Hernandez <hitmancodeme47@hotmail.com>` y nadie más.
  (Referirse a este fichero por su nombre —`CLAUDE.md`— sí es legítimo:
  es un fichero del repo, no una firma.)
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

Las que salieron de construir el árbol de servicios (§0.B):

- `feedback_capability_ref_opaque` — la capacidad guarda y devuelve el
  `Ref`; ramificar sobre su `Kind` la inutiliza para el siguiente dominio.
- `feedback_atomicity_floor` — sin almacén propio no es un servicio, es
  un tipo. El filtro contra el que se rechazan capacidades propuestas.
- `feedback_idempotency_before_state` — la llave se resuelve ANTES de
  toda regla que dependa del estado. Al revés, el reintento choca con
  lo que él mismo creó.
- `feedback_promote_on_second_consumer` — nada se promueve a una capa
  compartida con un solo consumidor. Shared esperó seis; Bff.Core, dos.
- `feedback_compensation_is_data` — dato y no función, anotada en el
  instante en que existe lo que hay que deshacer. **Armada ≠ pendiente.**
- `feedback_compensation_changes_character` — al capturar, «liberar» pasa
  a «devolver»; al consumir stock, «soltar» pasa a «ajustar». Si no se
  reescribe, la compensación falla para siempre por una razón que no
  tiene nada que ver con el mundo real.
- `feedback_close_doors_last` — lo que cierra una puerta (`fulfill`,
  `checkout`) va lo más tarde posible en un flujo, cuando ya no queda
  nada detrás que pueda fallar.
- `feedback_mutate_every_gate` — un gate que no se vio fallar no está
  vigilando nada. Se reintroduce el defecto y se confirma el rojo.
- `feedback_verify_with_live_processes` — los defectos caros salieron
  todos de levantar los procesos y matar uno, no de los tests: los
  tests codificaban la misma suposición equivocada que el código.

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

# Suite completa (2255 tests):
dotnet test Synergos.CMS.sln -v quiet

# LOS GATES DE ARQUITECTURA — corren solos dentro de la suite, pero
# conviene correrlos aparte al tocar el árbol de servicios:
dotnet test Synergos.CMS.Tests/Synergos.CMS.Tests.csproj --filter "FullyQualifiedName~Architecture"

# Una capacidad o un orquestador, sueltos:
dotnet build Synergos.Api.Booking/Synergos.Api.Booking.csproj -v quiet
dotnet build Synergos.Bff.Tienda/Synergos.Bff.Tienda.csproj -v quiet

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

- ~~**HttpBundleRegistryClient**~~ — **desbloqueado** (HU #20, ADR 0132).
  Existe y se activa con `Synergos:BundleRegistry:Mode=Http`. El bloqueo
  decía «esperando al equipo del CDN»: **el equipo del CDN éramos
  nosotros**, y el pipeline que publica el registry ya existía en
  `Synergos.UI` — publicaba a una carpeta local. Lo que faltaba era que
  esa carpeta fuera alcanzable por HTTP. Los tres modos hoy:
  `Stub` (default, siempre null) · `FileSystem` (CDN local) · `Http`.
- **Experience CDN** (9 DocTypes) + `compBehaviorTracking` +
  `compBehaviorInteraction` — heredaban el bloqueo de arriba, que ya no
  existe. Falta revisarlos: probablemente sea trabajo, no espera.

## 10. Cuando termines una tarea

1. `dotnet build` (Web) → 0 CS errors.
2. `git log --oneline -5` → commits atómicos legibles.
3. Si tocaste schema: el arquitecto correrá uSync Import manualmente.
4. Actualiza memorias del agente si aprendiste una regla nueva.
5. Actualiza `refactor-docs/architecture/00-current-state-synergos-cms.md`
   §11 si cambiaste algo estructural relevante.
6. Si tocaste el árbol de servicios: **mutá los gates** que escribiste
   —reintroducí el defecto, confirmá el rojo, restaurá— y **verificá
   con procesos reales** si el cambio cruza servicios. Los dos defectos
   más caros de este repo los encontró un proceso vivo, no un test.
7. Si el cambio hace obsoleto algo de este fichero, **arreglalo en el
   mismo commit**. Un `CLAUDE.md` que miente es peor que uno corto.

## 11. Estado del árbol de servicios — lo que falta de verdad

> Actualizar al cerrar cada ola. Si esta sección envejece, el siguiente
> agente propone lo que ya existe o da por hecho lo que no.

**Construido y verificado:** 20 capacidades (132 endpoints, 192 códigos
de rechazo), `Bff.Core`, `Bff.Salud`, `Bff.Tienda`, `Bff.Eventos`. 2255 tests, gates de
segregación y molde en verde.

**El despliegue está construido y espera una máquina** (HU #19, ADR 0133):
imágenes por SHA a GHCR, `compose.prod.yml`, `tools/bootstrap-servidor.sh`,
`tools/deploy-remoto.sh` (parada antes de arranque), `tools/humo-publico.sh`
(contra la URL pública, no contra el runner) y vuelta atrás automática. El
workflow **se salta solo** mientras falten `DEPLOY_HOST` / `SYNERGOS_DOMAIN`.
Lo que falta es que el arquitecto cree el VPS — decisión de compra, no código.

**Lo que NO está:**

- **Poco está conectado al producto, pero la brecha es MENOR de lo que
  parecía.** El inventario del cableado (HU #23,
  `docs/product/11-mapa-del-cableado.md`) contó los 46 `Stub*` y los
  clasificó: **8** son cableado pendiente, **5** ya salen del contenido
  de Umbraco (cablearlos sería un retroceso) y **33** se quedan en stub a
  propósito. Y 20 de los 46 **ya son durables** — «stub» en este repo
  dejó hace tiempo de querer decir «en memoria». Hay gate
  (`WiringMapTests`): un stub nuevo sin mapear rompe el build.
  De los 9, **cuatro** están hechos: la tienda compra contra `Bff.Tienda`
  (`Synergos:Tienda:Mode=Bff`, HU #24), la cita clínica agenda contra
  `Bff.Salud` (`Synergos:Salud:Mode=Bff`, HU #25) y la visita al inmueble
  aparta cupo **directo contra `Api.Booking`, sin orquestador**
  (`Synergos:Realty:Mode=Api`, HU #33a) — una visita no se cobra, así que
  toca una sola capacidad y un BFF sería una saga de un paso. El default
  sigue siendo `Stub` en los tres. Faltan `StubPaymentProvider` →
  `Api.Payments` y lo que queda de `StubReservationService` (Viajes y
  Eventos, que **no van al mismo sitio** — ver #33).

  > **El mapa se equivocó una segunda vez con el mismo filtro.**
  > `StubVisitSchedulingService` estaba en la familia C porque «no hay un
  > segundo paso que pueda fallar» — cierto, y contesta **otra** pregunta.
  > Eso decide si hace falta un orquestador, no si hace falta cablearlo.
  > Son dos preguntas y hay que hacerlas por separado.

  > **Y el seam de reservas mezcla dos atomicidades.** `IReservationService`
  > fusiona «cupo de un pozo contable» (`Api.Inventory`) con «una ventana
  > sobre un recurso» (`Api.Booking`). Por eso su `Reservation` lleva
  > `RoomTypeCode` y `GuestName`, que ninguna capacidad puede guardar. Los
  > que quedan no van todos a Booking: una butaca es un pozo contable.
- **El borde ya avisa, pero todavía no cobra.** `Api.Notifications` tiene
  transporte real (Resend, ADR 0131) y le falta solo la credencial.
  `Api.Payments` ya distingue **rechazado** (no se reintenta) de **caído**
  (sí) de **sin configurar**, y con un nombre de proveedor puesto rechaza a
  gritos en vez de caer al stub en silencio — hay gate (HU #27). Lo que
  falta es **el adaptador real y la cuenta comercial**: hoy sigue sin mover
  plata, así que ningún demo de venta corre de punta a punta.
- **La saga que nunca confirmó ya se abandona** (HU #29, parcial): el
  barrido de `Bff.Core` da por muerta la que lleva más de
  `Sweep:AbandonAfterMinutes` en `Running` y deshace lo hecho. Cero lo
  apaga. **Lo que NO rescata es el stock** —los apartados de
  `Api.Inventory` vencen solos a los 15 min—, sino la autorización del
  cobro, que no vence sola.
- **Lo que quedó en `Queued` ya se barre** (HU #29, la otra mitad).
  `Bff.Core.DeliverySweeper` mira `GET /v1/deliveries/queued`, reintenta
  lo que le queda techo y **rinde lo que no**: al llegar a
  `Sweep:DeliveryRetryCeiling` el envío pasa a `GivenUp` con la última
  causa escrita. Cero lo apaga, y apagado ni pregunta. El reparto es el
  de siempre —la capacidad sabe QUÉ está colgado y CÓMO se reintenta; el
  orquestador, CUÁNDO y CUÁNTAS VECES— y hay gate
  (`BarridoSegregationTests`): meter el lazo o el techo dentro de
  `Api.Notifications` rompe el build. **Lo levantan los dos
  orquestadores, no uno elegido a dedo**; que coincidan sobre el mismo
  envío no manda dos correos, porque la capacidad rechaza el reintento
  simultáneo (`retry_in_flight`).
- **Las copias existen pero NO salen del servidor** (HU #31).
  `tools/respaldo.sh` copia en frío los volúmenes de datos —la lista sale
  del compose, no de una lista a mano— y `tools/restaurar.sh` los
  devuelve, exigiendo `--si-estoy-seguro` porque pisa lo vivo. Hay gate
  (`RespaldoTests`). **Lo que falta es llevárselas fuera de la máquina**:
  una copia que muere con el disco no protege de perder el disco. Y dónde
  viven y cuánto duran es decisión de privacidad — llevan direcciones de
  entrega y nombres de pacientes.
- **19 capacidades sobre fichero JSON** con `lock` de proceso. Una sola
  instancia por capacidad; dos réplicas se pisan. Está dicho de frente
  en `JsonCollectionStore` y es la primera razón para cambiar de almacén.
- **Ya se puede seguir una saga por los seis servicios** (HU #28), aunque
  todavía no con trazas de verdad. Un identificador opaco nace en el borde
  —o se genera si nadie lo manda—, viaja en `X-Correlation-Id` por cada
  salto y sale impreso en cada línea de cada servicio: la pregunta que se
  hace de verdad, «mostrame todo lo de esta compra», se contesta con
  `docker compose logs | grep`. Hay gate (`CorrelationTests`): un host que
  no lo cablee, o un cliente que no lo propague, rompe el build.
  **Deliberadamente NO es OpenTelemetry**: un colector es otro proceso que
  mantener, y se justificará el día que el `grep` deje de alcanzar.
  El nombre de la cabecera es **lo único que comparten los dos árboles** —
  un contrato de una cadena, porque el CMS no referencia `Synergos.Shared`.
- **La llave compartida no es identidad.** Sirve servicio↔servicio; no
  contesta «quién es este usuario». `Api.Identity` existe pero nadie la
  usa como puerta. `Api.Messaging` ya guarda **con qué se afirmó** la
  identidad de quien accede (HU #13) precisamente para que el día que
  esto se arregle los registros viejos no mientan sobre su propia fuerza:
  hoy todos dicen `CmsSession`, que es nuestro propio sistema dando fe.
  Ese `IdentityAssertion` **subió a `Synergos.Core`** al aparecer su
  segundo consumidor (el asiento de auditoría de la HU #15), así que el
  día que #14 cablee `Api.Identity` hay **un solo sitio** que pasa de
  decir `CmsSession` a decir otra cosa. Hay gate: declararlo dos veces
  rompe el build.
- **`Api.Booking` ya deja llegar del sujeto a su recurso** (HU #25):
  `GET /v1/resources?subjectKind=&subjectId=`, calcando lo que
  `Api.Inventory` hacía con `/v1/items`. Faltaba, y obligaba a que el
  identificador interno del recurso viajara hasta el CMS — que ninguna
  convención podía adivinar, porque lo genera la capacidad.
- **Ninguna capacidad llama a otra**, y no hay gate que lo vigile porque
  no había caso. Apareció el primero —mandar un acceso rechazado a
  `Api.Audit`— y se decidió NO abrir esa flecha desde la capacidad
  (HU #15). Si algún día se abre, el gate va antes que el código.

  > **Y se decidió también quién SÍ lo escribe: el orquestador.** Se
  > miraron las tres opciones y las dos que se podían entregar ya —que
  > emita la capacidad, o que emita un middleware compartido— acaban en
  > lo mismo: cliente y llave hacia `Api.Audit` dentro del proceso de
  > cada capacidad, o sea las veinte dejando de ser hojas. La flecha se
  > movería de fichero, no desaparecería. La razón de fondo es que el
  > caso —un acto administrativo notificado— es una regla de Gobierno,
  > no de la plataforma: nadie cree que un 403 de `Api.Cart` merezca
  > asiento de auditoría. **#15 queda bloqueada por `Bff.Gob`**, que es
  > su resultado y no su fracaso.
- **Cuatro orquestadores sin construir**: Realty, Gob, Academy, Social.
  `Bff.Eventos` (HU #35) y `Bff.Viajes` (HU #36) ya están, y ninguno de los
  dos necesitó una capacidad nueva ni un endpoint nuevo — que es la
  diferencia entre «agnóstica» y «agnóstica hasta el segundo caso».

  > **`Bff.Viajes` es el primero con varios pasos reversibles HETEROGÉNEOS.**
  > Un vuelo, dos noches de hotel y un auto son tres apartados sobre tres
  > recursos con tres ventanas, y —lo que de verdad cambia— **pueden estar en
  > estados distintos cuando llega el fallo**: al confirmar el tercero, los
  > dos primeros ya son reservas. Por eso acá la compensación del cupo
  > también cambia de carácter («soltar el apartado» → «cancelar la
  > reserva»), y la reescritura va DENTRO del bucle de confirmación, ítem por
  > ítem. En Salud nunca hizo falta porque confirmar es el último paso.
  >
  > **Y resolvió la pregunta que traía #36:** «no todo va a `Api.Booking`».
  > Sí va todo, y no por comodidad: `Resource` ya lleva `Capacity` («1 para
  > un consultorio; 40 para un aula»), así que el aspecto de pozo está
  > dentro, y la regla de «horario vacío = siempre abierto» se tomó nombrando
  > el caso hotel. El vuelo se consideró para `Api.Inventory` y se descartó
  > con el argumento escrito y el disparador para revisarlo: que haga falta
  > sobreventa por clase tarifaria. Hay gate
  > (`ViajesCapabilityChoiceTests`).
  >
  > **Y la vía hotel ya está cableada** (rebanada 2, `Synergos:Viajes:Mode=Bff`,
  > con el stub de default). Verificado con los cuatro procesos vivos: matando
  > `Api.Payments` a mitad del cobro la habitación vuelve al inventario sola, y
  > al cancelar se devuelve el total MENOS la penalidad.
  >
  > **Cablearlo obligó a partir el borde antes.** Apartar, cobrar y confirmar
  > vivían dentro de `BookingController` — con dos defectos ya corregidos que
  > ningún test cubría porque no había dónde ponerlos. Ahora viven en
  > `IHotelBookingService`. Hay gates (`HotelBookingSeamTests`,
  > `ViajesWiringTests`).
  >
  > **Y destapó dos cosas más.** Una: el orquestador devolvía TODO al cancelar,
  > y la política del hotel retiene una penalidad — ahora `cancel` acepta el
  > monto a retener, ya calculado por quien vendió. Dos: un viaje ya confirmado
  > **no se deshace compensando** —`Bff.Core` lo rechaza con todas las letras,
  > «deshacerlo es una cancelación con su política»— así que eso es una
  > operación propia del flujo, no una compensación.
  >
  > **Lo que NO se cableó, y no por descuido:** el carrito multi-producto.
  > `TravelCartItem` no lleva fechas —ni el seam, ni el DTO HTTP, ni el motor en
  > proceso— y un apartado de `Api.Booking` ES una ventana sobre un recurso.
  > Añadírselas cruza a `Synergos.UI`, así que va en su propio ticket. Hay gate:
  > el día que el contrato tenga fechas, se cae solo y hay que decidir de frente.

  > **Y resolvió la pregunta que traía #35:** butaca nominada y cupo
  > general son el MISMO pozo contable. La granularidad va en el
  > identificador del sujeto —`evento/localidad` o
  > `evento/localidad/butaca` con existencia 1— y `Api.Inventory` no
  > distingue. Si tuviera que distinguir, dejaría de servirle a la tienda
  > al día siguiente.

  > **Y ya está cableado** (HU #35, rebanada 2b):
  > `Synergos:Eventos:Mode=Bff` compra contra el orquestador, con el stub de
  > default. Verificado con los cuatro procesos vivos: matando `Api.Payments`
  > a mitad de la confirmación, el aforo vuelve al pozo solo y no se emite
  > ninguna entrada.
  >
  > **La compra se parte en dos mitades que viven en sitios distintos.** El
  > orquestador mueve aforo y plata; **el artefacto se queda en el CMS**
  > —la entrada, su QR, su portador, el check-in—, porque el firmante vive
  > de este lado. Con el BFF caído se sigue pudiendo ver «mis entradas»,
  > transferir y escanear en la puerta.
  >
  > **Cablearlo obligó a partirlo dos veces antes.** `EventTicketIssuer` es
  > el ÚNICO sitio que nombra una entrada y arma el token de su QR;
  > `EventTicketLedger` es el ÚNICO registro de lo emitido, y lo comparten
  > los dos caminos de compra. Lo segundo lo destapó el propio cableado: la
  > cara de organizador colgaba del motor de compra concreto, así que
  > cambiar por dónde se compra habría dejado la puerta leyendo un almacén
  > vacío, sin que nada avisara. Hay gates (`EventTicketIssuanceTests`,
  > `EventosWiringTests`).
  >
  > **Lo que el CMS recuerda de su lado es `sagaId → asistentes`**: la saga
  > no lleva la lista de asistentes a propósito, y de quien compra solo
  > lleva un **seudónimo** — mandar el correo en crudo lo dejaba escrito en
  > el disco del orquestador, y eso lo destapó la verificación en vivo, no
  > una revisión. `Bff.Tienda` (#24) todavía manda el correo entero.
  >
  > **Y queda un defecto conocido:** la llave de idempotencia no caduca, así
  > que si una compra se deshace, el mismo comprador que reintente lo mismo
  > recibe la saga muerta y se queda encerrado. Lo comparte la tienda.
- **El retroceso no es configurable.** El plazo de abandono y el techo de
  reintentos sí (HU #29), pero la *forma* de reintentar —ocho intentos con
  retroceso exponencial— está cableada en `Compensator`. Nadie ha pedido
  otra todavía.
- ~~`Api.Inventory` necesita ajuste relativo~~ — **hecho** (defecto #30).
  `POST /v1/items/{id}/adjust` acepta `delta` («devolvieron 2», relativo,
  **exige `Idempotency-Key`** porque un relativo reintentado suma dos
  veces) u `onHand` («conté y hay 47», absoluto, sin llave porque
  repetirlo no cambia nada). Va exactamente uno de los dos.
  `Bff.Tienda` devuelve con `delta` y ya no lee el total antes.
- **`StubBundleRegistryClient` sigue siendo el default, pero ya no hay
  bloqueo**: el CDN está VIVO (`https://synergos-ui.synergos-labs.workers.dev`,
  139 elementos, cabeceras verificadas 2026-08-04) y `HttpBundleRegistryClient`
  resuelve contra él — verificado con el cliente real compilado, no con fakes.
  Lo que falta es que el despliegue configure `SYNERGOS_CDN_MODE=Http` +
  `SYNERGOS_CDN_URL` (ver `.env.example`). Es una decisión de entorno del
  arquitecto, no trabajo pendiente de código. Ver §9 y ADR 0132.
