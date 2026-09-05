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
   Tests project: **2705 passing**. Memoria `feedback_tests_after_full_migration`
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
│   │   └── umbraco/             cdn-contract.md (DESBLOQUEADO, HU #20 · ADR 0132)
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
├── Synergos.CMS.Tests/          xUnit — 2705 tests passing (gate liftado ADR 0075)
│   ├── Architecture/            LOS GATES: segregación (17) + molde (12) + capas (8)
│   │                            + imagen de contenedor (6) + compose (10)
│   │                            + despliegue (14, ADR 0133)
│   ├── Api/                     tests de reglas y servicio por capacidad
│   └── Bff/                     la compensación cruzada (132)
├── Synergos.CMS.Benchmarks/     BenchmarkDotNet (WebhookSigner + BridgeContextSerializer)
│
├── Synergos.Core/               EL VOCABULARIO. Ref, Money, TimeWindow, Rejection,
│                                Result, IdempotencyKey, Actor, Page,
│                                IdentityAssertion.
│                                CERO referencias. No sabe qué es ASP.NET.
├── Synergos.Shared/             fontanería de host. Llave compartida, Rejection→HTTP,
│                                libro de idempotencia, JsonCollectionStore, correlación.
│                                Solo puede referenciar Core — UNA flecha.
├── Synergos.Api.*/              LAS 20 CAPACIDADES, agnósticas. 136 endpoints.
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

> **El árbol de servicios está construido y el producto ya lo consume**, aunque
> con todos los interruptores apagados por defecto. El CMS habla hoy con **siete**
> capacidades —`Sessions`, `Booking`, `Workflow`, `Messaging`, `Signing`,
> `Identity` y `Audit`— y con los cuatro orquestadores. Esta línea decía «UNA»
> desde antes de las HU #24, #25, #33a, #35, #36, #40, #44, #45, #46, #62 y #15:
> un agente que la leyera concluía que no había nada cableado y proponía de cero
> lo que ya existe. Ver §11, que es donde está el detalle.

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
| "¿Qué se hace con cada uno de los 49 `Stub*`?" | `docs/product/11-mapa-del-cableado.md` — hay gate (`WiringMapTests`) |
| "¿Qué rechaza esta capacidad?" | `Synergos.Api.X/Domain/XRules.cs` — las veinte lo tienen y hay gate (#58). Los códigos se componen de su `CodePrefix`; la única excepción son los cinco de `Api.Notifications/Transport/`, que son fallos de la firma del webhook y no reglas de negocio |

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
4. Correr `node tools/usync-audit.mjs` — 11 checks: colisión de GUID,
   compositions huérfanas, refs rotas, iconos, alias de Dictionary,
   cross-check `<Definition>`↔`<DataType Key>`, DataTypes huérfanos,
   mojibake, contenido del seeder (ADR 0129), bloqueos externos declarados
   (#53) y claves de Dictionary sin respaldo (#60). Es el mismo gate que
   corre en CI.
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

# Suite completa (2705 tests):
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
node tools/usync-audit.mjs        # 11 checks de schema uSync
node tools/check-css-parity.mjs   # G-3: toda clase syn-* emitida tiene CSS
(cd Synergos.CMS.Web/docs/contracts/tests && npm ci && npm test)  # contratos
```

El cuarto es **cross-repo** y valida las dos mitades del acople, así que
necesita `Synergos.UI` clonado. **La CI ya los corre** —`design-gates.yml`
hace checkout del hermano y lanza los dos—, así que no son gates dormidos; lo
que faltaba era poder correrlos **acá**, antes de subir. Y sí se puede: **no
hace falta que sea hermano ni correr `npm install`**, porque los dos scripts
aceptan la ruta del CMS. Esta sección describía la disposición de hermanos
como requisito, que es lo que hacía que un agente en un contenedor los diera
por imposibles (#86):

```bash
# Se guarda ANTES de moverse: los dos comandos corren dentro del otro repo,
# así que un `$PWD` relativo ahí dentro apunta al sitio equivocado.
CMS=$PWD                       # desde la raíz de ESTE repo
git clone --depth 1 https://github.com/cherced-dev/synergos.ui /tmp/ui

# registry ↔ DocTypes. También acepta --cms-path=/ruta
(cd /tmp/ui && SYNERGOS_CMS_PATH=$CMS node tools/validate-cms-contracts.mjs)

# los tokens del design system, contra el syn-tokens.css de este repo
(cd /tmp/ui/platforms/angular && SYNERGOS_CMS_PATH=$CMS node tools/sync-tokens.mjs --check)
```

Los dos pasan hoy. El primero sale con **avisos** `[W4]` —entradas del registry
sin su espejo en `ELEMENT_CONFIG_FIELDS`— que son avisos y no errores: sale 0.

> **Mirá `git status` antes de `git add -A` después de correr esto.** `master` llegó a traer un
> symlink a sí mismo —`Synergos.CMS → /home/user/Synergos.CMS`, ruta absoluta de un contenedor— y
> tumbó los **seis** gates de segregación de golpe: .NET sigue los enlaces al recorrer directorios,
> así que cada `.csproj` aparecía dos veces y todo `SingleOrDefault()` sobre un nombre reventaba
> (#90). **De dónde salió no se sabe**: las tres herramientas de acá se probaron con la variable y
> sin ella y ninguna lo crea —fallan a la vista, que es lo correcto—, así que no se les echa la
> culpa. Lo que faltaba era que **algo mirara la FORMA del árbol y no sólo su contenido**: un
> `mode 120000` entraba por un `add -A` sin que nada chistara. Hoy hay gate
> (`FormaDelArbolTests`), y va sobre la clase entera porque el próximo enlace se llamará de otra
> manera.
Son **deuda conocida y declarada**: el propio `design-gates.yml` explica que
con la bandera estricta puesta ese job llevaba rojo en `master` cuatro
corridas seguidas, y que un gate siempre rojo deja de leerse.

## 8. Layout Composer — el feature más maduro

Después de las Olas 42 → 44 el Layout Composer es end-to-end:

- **14 Layout Preset ElementTypes** en `uSync/v9/ContentTypes/
  elementlayout*.config`: Section, Container, Stack, Grid, Column,
  1Col, 2ColEven, MainSidebar, 3Col, 4Col, HolyGrail, SidebarMain,
  Hero, SnippetRef.
- **Block Grid con areas** (`DTBlockGridSections.config`) permite al
  editor dropear presets al root de `sections` y cualquier elemento
  de contenido (159 blocks) dentro de las areas.
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
- **compDom* universal** (Ola 43.15/43.16): los 173 element types
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
- ~~**Experience CDN** (9 DocTypes) + `compBehaviorTracking` +
  `compBehaviorInteraction`~~ — **nunca existieron** (#53). Los tres nombres
  sólo aparecían en prosa: acá y en las consecuencias de la ADR 0132, de donde
  esta línea los copió. No hay ni hubo un `compBehaviorTracking`, un
  `compBehaviorInteraction` ni nueve DocTypes de *Experience CDN* en
  `uSync/v9/` — el historial de git tampoco los conoce. **Mandar a alguien a
  buscarlos era peor que no decir nada**: parece trabajo identificado y es una
  hora perdida antes de descubrir que no hay nada ahí.
- La que sí existía —`compBehaviorFeatureFlag`— **no estaba en esta lista**, y
  su marcador decía esperar `HttpBundleRegistryClient`, entregado en la HU #20.
  Hoy está marcada `[Disponible — sin consumers actuales]`, que es lo que es:
  existe, no está bloqueada, nadie la usa. Darle consumer es trabajo real
  —hace falta quién lea la clave y quién decida— y no lo pide nadie todavía.

**Bloqueos vigentes:** ninguno.

> **Esa línea de arriba la cruza el gate contra el schema**
> (`tools/usync-audit.mjs`, check 10): se lee **ella sola**, no la sección —el
> resto es prosa que explica bloqueos ya levantados, y leerla entera haría que
> contar la historia de uno se leyera como declararlo vigente—. Un marker
> `[Bloqueado externamente]` que no esté en esa línea rompe el build, y nombrar
> ahí algo que el schema no tiene marcado, también.
>
> La razón por la que hace falta: el chequeo de compositions huérfanas **exime**
> a lo que lleve marker, así que un bloqueo que terminó y nadie movió deja de
> vigilarse **en silencio**. Es exactamente lo que pasó acá durante tres olas.

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

**Construido y verificado:** 20 capacidades (136 endpoints, 234 códigos
de rechazo), `Bff.Core`, `Bff.Salud`, `Bff.Tienda`, `Bff.Eventos`, `Bff.Viajes`. 2705 tests, gates de
segregación y molde en verde.

> **Los 234 se cuentan, y el criterio es parte de la cifra** (#52). Decía **195**
> y nadie la había vuelto a contar. Cuenta los códigos **literales distintos**
> que las veinte construyen —el primer argumento de un `Rejection.*`, con
> `{CodePrefix}` resuelto—, y por eso **excluye dos cosas que sí existen**: los
> que se arman en tiempo de ejecución (`orders.already_{destino}`, y los
> reenvoltorios `xxx.{code}`), que no se pueden enumerar sin ejecutar; y los que
> viven en `Synergos.Shared`, que una capacidad devuelve pero no declara.
>
> **Ésos últimos NO son un número, y creer que lo eran fue el error.** Seis
> llevan prefijo fijo —`identity.token_expired`, `token_malformed`,
> `token_unknown_key`, `token_subject_mismatch`, `assertion_not_proven` y
> `token_not_verifiable`—, así que se pueden contar y esta guía decía «con ellos
> serían 240». Pero hay **dos más que llevan el prefijo de quien llama**:
> `{prefijo}.idempotency_key_required`, que emiten las **19** capacidades que
> exigen la cabecera, y `{prefijo}.access_requires_identity`, que emite quien
> deje la afirmación sin declarar —hoy `Api.Audit` y `Api.Consent`;
> `Api.Messaging` declara el suyo y `Api.Workflow` no puede llegar ahí—. Sumarlo
> todo no da una cifra estable: da **una función de quién llama**, que cambia
> cuando una capacidad empieza a exigir una llave sin que nadie escriba un
> `Rejection` nuevo. Por eso el criterio cuenta **el árbol de las capacidades**
> y no «todo lo que puede salir»: lo segundo hay que ejecutarlo para saberlo.
>
> Se eligió el criterio **simétrico con el de endpoints** —lo que hay en el
> **árbol de las capacidades**— porque el sujeto de la frase son las veinte.
> Hay gate (`ApiMoldTests`), y desde el descuadre de abajo **cuenta también el
> número de esta nota**: tener la cifra en dos sitios y vigilar uno solo es
> cómo se acaba discutiendo cuál de los dos está mal.
>
> **El sexto llegó ahí solo, y el gate lo cazó** (#72): al subir la
> comprobación del token a `Shared`, `token_not_verifiable` dejó de
> construirse dentro de una capacidad y la cifra bajó de 235 a 234 sin que
> nadie tocara una regla. Es lo que se quería — mover código no debería poder
> cambiar en silencio lo que la guía afirma. **Y la nota se quedó diciendo
> 235** cuando el resumen ya decía 234: el gate miraba la frase del resumen y
> la de la nota no la miraba nadie.

**El despliegue está construido y espera una máquina** (HU #19, ADR 0133):
imágenes por SHA a GHCR, `compose.prod.yml`, `tools/bootstrap-servidor.sh`,
`tools/deploy-remoto.sh` (parada antes de arranque), `tools/humo-publico.sh`
(contra la URL pública, no contra el runner) y vuelta atrás automática. El
workflow **se salta solo** mientras falten `DEPLOY_HOST` / `SYNERGOS_DOMAIN`.
Lo que falta es que el arquitecto cree el VPS — decisión de compra, no código.

**Lo que NO está:**

- **Poco está conectado al producto, pero la brecha es MENOR de lo que
  parecía.** El inventario del cableado (HU #23,
  `docs/product/11-mapa-del-cableado.md`) contó los 49 `Stub*` y los
  clasificó: **13** son cableado pendiente, **5** ya salen del contenido
  de Umbraco (cablearlos sería un retroceso) y **31** se quedan en stub a
  propósito. Y 19 de los 49 **ya son durables** — «stub» en este repo
  dejó hace tiempo de querer decir «en memoria». Hay gate
  (`WiringMapTests`): un stub nuevo sin mapear rompe el build, y desde
  #50 **también cuadra las cifras de la prosa** contra el inventario y
  contra el disco — se habían desviado tres olas seguidas, porque el gate
  sólo miraba la tabla y la gente lee el resumen. Y cuadrar la cifra no
  basta: **la tabla narrativa de la familia A tiene que listarlos uno por
  uno**, porque una cabecera que dice «(14)» sobre trece filas pasa el
  recuento y deja un stub que nadie va a tomar — es la tabla que se lee
  para elegir el siguiente trabajo, no la rejilla del inventario.
  De los 13, **once** están hechos: la tienda compra contra `Bff.Tienda`
  (`Synergos:Tienda:Mode=Bff`, HU #24), la cita clínica agenda contra
  `Bff.Salud` (`Synergos:Salud:Mode=Bff`, HU #25), la visita al inmueble
  aparta cupo **directo contra `Api.Booking`, sin orquestador**
  (`Synergos:Realty:Mode=Api`, HU #33a) — una visita no se cobra, así que
  toca una sola capacidad y un BFF sería una saga de un paso — y **el
  expediente decide contra `Api.Workflow`**, también directo
  (`Synergos:Gob:Mode=Api`, HU #44) — y **el acto administrativo se pone
  en conocimiento contra `Api.Messaging`**, por su propio interruptor
  (`Synergos:Gob:Notifications:Mode=Api`, HU #62). El default sigue
  siendo `Stub` —`Local` en el de notificaciones— en todos. **Faltan
  dos**, y los dos por lo mismo: `StubPaymentProvider` y
  `StubApplicationService` → `Api.Payments`, **bloqueados por #27**.

  > **`StubApplicationService` NO espera un orquestador, y el mapa se
  > equivocó con el mismo filtro por CUARTA vez.** Decía «con pago de por
  > medio», que suena a saga; el propio código dice lo contrario y con la
  > razón escrita: **no se aborta el trámite si la captura no sale** —en un
  > servicio público, perder la radicación de un ciudadano porque su banco
  > tardó es peor que arrastrar una tasa pendiente—. Si no se aborta, no hay
  > nada que deshacer, y un orquestador sería la máquina de compensar sin
  > compensación.
  >
  > **Componer no es orquestar.** Las cuatro veces el error fue mirar
  > *cuántos pasos compone* en vez de las tres preguntas, que son distintas
  > y hay que hacerlas por separado: ¿hay algo que **deshacer**? (decide si
  > hace falta orquestador) · ¿el recurso lo lleva **alguien más**? (decide
  > si hace falta cablearlo, #33a) · ¿**quién tiene la plata**? (decide a
  > quién se le pide el movimiento, #57).
  >
  > **Y eso deja a `Bff.Gob` sin razón para existir hoy** — que es la
  > consecuencia que importa, porque #15 estaba bloqueada esperándolo.

  > **El acto administrativo notificado existe para sostener CUÁNDO
  > ACCEDIÓ, no para enviar** (#62). Un correo enviado prueba que salió del
  > servidor, no que llegó a quien tenía que llegar, y el término de un
  > recurso no empieza a contar con lo que salió. Por eso el acto **no se
  > puede leer sin registrar el acceso**: la bandeja lista el título —hace
  > falta saber que existe— y el cuerpo sólo sale al abrirlo. Si el listado
  > lo trajera, el acuse sería un botón decorativo y el término no
  > empezaría nunca. Y abrir es `POST`: con un `GET` lo dispararía el
  > prefetch del navegador y el ciudadano perdería días sin haber leído
  > nada.
  >
  > **El primer acceso es el que cuenta**, y re-notificar el mismo acto
  > devuelve el que ya está: dos plazos para un acto le dan un argumento a
  > quien recurre tarde. **Quién abre sale de la sesión y el destinatario
  > del expediente** —el radicado es secuencial (ADR 0103)—, y un
  > expediente **sin Member detrás no se notifica electrónicamente**:
  > dejaría escrito un término que nadie puede empezar a contar.
  >
  > Es durable desde el primer día, porque un registro que se pierde al
  > reiniciar no sirve para lo único que existe.
  >
  > **Y ya cruza contra `Api.Messaging`** (rebanada 2,
  > `Synergos:Gob:Notifications:Mode=Api`, con el registro local de
  > default). Va por **su propio interruptor** y no por
  > `Synergos:Gob:Mode`: notificar es `Api.Messaging` y decidir es
  > `Api.Workflow`, y juntarlas obligaría a encender las dos para probar
  > una. **Es el primer consumidor de esa capacidad** —llevaba meses
  > construida sin nadie— y **no hizo falta añadirle un endpoint**: hilo,
  > adjunto por referencia, plazo de acuse y acuse con afirmación
  > verificada (#14) ya estaban.
  >
  > **La afirmación la decide la capacidad, no el CMS.** Este lado declara
  > lo más débil (`CmsSession`) y presenta el token si el despliegue sabe
  > emitirlo; quien decide si eso vale como `IdentityToken` es
  > `Api.Messaging`, que lo verifica. Escribirla acá «porque es lo que
  > mandamos» dejaría el registro mintiendo hacia abajo — es el defecto
  > #42 sobre un dato que sostiene un plazo legal. Verificado en vivo: el
  > mismo ciudadano queda anotado con `CmsSession` sin `Api.Identity` y con
  > `IdentityToken` con ella, **en el almacén de la capacidad**.
  >
  > **Y las bandejas se LEEN de este lado**, como el timeline de #46: con
  > `Api.Messaging` caída el ciudadano sigue viendo sus notificaciones y si
  > las abrió; lo que se para es notificar y abrir, y **no se dan por
  > hechos en silencio**. Hay gate (`GovNotificationWiringTests`).

  > **La devolución de la tienda estaba ROTA, no pendiente** (#57), y el mapa
  > tenía mal la razón. Decía que `StubReturnService` iba a un orquestador por
  > «dos pasos con plata en medio»; mirando el código, el segundo paso —marcar
  > el RMA— es una escritura LOCAL, y un reembolso no se compensa: una saga acá
  > tendría un paso irreversible y nada que deshacer. **La pregunta que sí
  > decide no es «¿qué hay que deshacer?» sino «¿QUIÉN TIENE LA PLATA?»**, y con
  > la tienda cableada la tiene el orquestador.
  >
  > Lo que pasaba: el RMA le pedía el reembolso a `IPaymentProvider` con
  > `ShopOrder.PaymentSessionId`, y con `Tienda:Mode=Bff` ese identificador es
  > **el de la saga** —el de `Api.Payments` no sale del orquestador, a
  > propósito—. El proveedor local no lo conocía, así que el caso **no llegaba
  > nunca a reembolsado**, y sin que nada fallara ruidosamente. Verificado en
  > vivo con los ocho procesos: pedírselo al proveedor local con ese id
  > contesta «sesión de pago no encontrada».
  >
  > Hoy la ordena quien vendió, por `POST /v1/purchases/{id}/refund` —el gemelo
  > del de Viajes—, con el monto ya calculado (el orquestador cotiza la compra
  > entera y no sabe cuánto vale una línea) y con **llave de idempotencia
  > obligatoria**, porque devolver plata es un movimiento RELATIVO y un
  > reintento sin llave devuelve dos veces. El expediente del RMA se queda de
  > este lado.
  >
  > **Y destapó un segundo defecto en el camino en proceso**: el proveedor local
  > **ignoraba el monto** y reembolsaba la sesión entera, así que devolver una
  > línea de dos marcaba el pedido completo y la siguiente devolución se
  > rechazaba por un estado que nadie había querido poner. Un test afirmaba eso
  > como si fuera lo correcto — un test que codifica el defecto convierte el
  > arreglo en una regresión. Hoy el estado sólo pasa a `Refunded` cuando no
  > queda saldo. Hay gate (`ShopWiringTests`).

  > **`StubReservationService` pasó a la familia C** (#33), y ésa es la única
  > salida que ha tenido A.
  > Entró en A como «un cableado, seis verticales», y **los seis se cablearon
  > de a uno** —#24, #25, #33a, #35, #36 y #40—: con los cinco flags en su
  > modo cableado no le queda un solo llamador de negocio. Lo que queda no es
  > cablearlo, es que **sea el default**, y eso es deliberado — permite
  > levantar el repo entero sin ningún servicio. Está en `C.4`, una
  > subfamilia nueva porque ninguna de las otras tres le encajaba: tiene
  > almacén propio, no se deriva de nada y no es contenido de demo.
  >
  > **El descuadre que destapó era exactamente este stub**: estaba en A sin
  > estado, ni hecho ni pendiente, porque no era ninguna de las dos cosas, y
  > por eso «nueve más tres» no sumaba los doce de entonces. Hoy la cuenta se
  > comprueba sola (#66): once más dos son trece, y el gate lo cuadra contra
  > la columna «Estado» de la tabla narrativa.
  >
  > La lección se guarda, que costó una HU de prioridad 1: **el
  > apalancamiento de un seam no baja, se evapora**. Cada consumidor que se
  > cablea por separado se lo lleva consigo.

  > **Lo que #44 mudó no es un paso: es una TABLA.** Qué puede pasarle a un
  > expediente estaba escrito en C# y se desplegaba con el sitio, así que
  > añadir un paso de revisión era un cambio de código. `Api.Workflow` las
  > tiene como **dato**. Y el destino de un `outcome` se lee de la
  > definición, no de una copia local: con la tabla en dos sitios, un
  > trámite avanzaría distinto según a quién se le pregunte — peor que no
  > haberla mudado. Hay gate (`GobWiringTests`).
  >
  > **La idempotencia significa cosas distintas a cada lado**, y es el punto
  > más fino. El motor en proceso es idempotente sobre el estado destino
  > («¿hace falta hacer algo?»); la capacidad contesta `instance_closed`
  > («¿es legal esta transición?»). Las dos son correctas. Se resuelve del
  > lado del CMS **antes** de llamar: dejar subir la suya convertiría el
  > doble clic del funcionario, que hoy no hace nada, en un error en
  > pantalla que nadie decidió.
  >
  > **Y los expedientes anteriores al cableado no se adivinan.** Uno recién
  > radicado arranca su proceso solo —no hay historia que inventar—; uno ya
  > en revisión se **rechaza** diciendo que hay que migrarlo. Arrancarle una
  > instancia ahora diría que un expediente casi resuelto acaba de empezar,
  > y adelantarlo a golpe de transiciones escribiría fechas y actores que no
  > ocurrieron: parecería que funciona, que es lo peor que puede hacer una
  > migración.
  >
  > **Publicar la definición es un paso de DESPLIEGUE**, como los recursos
  > de `Api.Booking` en #25 y #33a. Sin ella se rechaza con
  > `definition_not_found`. Versionar es publicar **otra clave**: la
  > capacidad se niega a reescribir una viva, porque cambiarle las
  > transiciones a instancias en marcha las dejaría en estados imposibles.

  > **Y los cuatro pipelines de seguimiento también son datos** (HU #46,
  > `Synergos:Tracking:Mode=Api`). Por dónde pasa un pedido estaba escrito
  > en C# **cuatro veces** —Tienda, Viajes, Eventos, Educación—, cada una
  > un `static readonly` en otra clase. Va **una definición por dominio**:
  > los nombres de estado se repiten entre pipelines (`paid` en tres,
  > `completed` en dos), así que una compartida leería la etapa de un
  > dominio contra el pipeline de otro y «enviado» sería «matriculado» sin
  > que nada fallara.
  >
  > **Acá LEER no sale a la red, al revés que en Gobierno**, y la
  > diferencia es la que importa: el timeline se pinta en cada vista de
  > pedido, así que el CMS conserva su almacén como modelo de lectura y la
  > capacidad sólo valida el avance. Con `Api.Workflow` caída, quien compró
  > **sigue viendo dónde va lo suyo**; sólo se para avanzarlo. En un
  > expediente el riesgo es *decidir* con un proceso que quizá ya no es el
  > vigente; en un pedido es *mostrar* lo que ya pasó, que no decide nada.
  >
  > **Y por eso mismo un pedido en vuelo SÍ se puede poner al día**, cosa
  > que en Gobierno estaba prohibida. Allá la historia de la capacidad es
  > el registro legal y fabricarla escribiría fechas y actores que no
  > ocurrieron; acá la capacidad es un **motor de reglas** y las fechas
  > siguen siendo las del CMS, intactas: se reconstruye *dónde va* el
  > pedido, no *cuándo pasó*. El día que esa historia pase a ser la fuente
  > de las fechas, hay que revisarlo.
  >
  > **Lo que NO mudó son las etiquetas.** «Enviado», «Matriculado» son
  > presentación del dominio (§12), así que añadir una etapa sigue
  > necesitando su rótulo de este lado; lo que deja de necesitar es tocar
  > la tabla de qué sigue a qué. Hay gate (`TrackingWiringTests`).

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
  contesta «quién es este usuario». `Api.Identity` **ya emite y verifica
  tokens** (HU #14, rebanada 2) y **`Api.Messaging` es la primera
  capacidad que los usa como puerta** (rebanada 3): el acuse de un
  mensaje acepta `X-Synergos-Identity`, lo verifica en local y —lo que
  de verdad cambia— **la afirmación la decide la capacidad, no el
  llamador**. Antes se creía lo que venía en `assertion`, así que
  cualquiera con la llave compartida podía anotar un acceso como
  respaldado por un token que nunca existió (defecto #42). Hoy: token
  válido del mismo sujeto → `IdentityToken`; sin token, lo más fuerte
  que se acepta es `CmsSession`; declarar lo fuerte sin presentarlo se
  rechaza. **`Api.Consent` es la tercera** (rebanada 5): otorgar y
  revocar guardan **con qué** se afirmó la identidad de quien lo hizo.
  **`Api.Audit` es la cuarta** (#72), y era la que peor estaba —
  y desde la rebanada 6 es también **la primera a la que el CMS le
  PRESENTA** identidad para escribir, no sólo para leer.

  > **No eran «las otras 16»** (#81), y la nota de más abajo cita esa
  > misma cifra como ejemplo del error que describe —sin corregirla—. Salía
  > de restar cuatro a veinte, no de mirar qué guarda cada capacidad: al
  > barrer las veinte,
  > **trece no guardan actor ninguno** — sus pares `<X>Kind`/`<X>Id`
  > nombran el **objeto** de la operación (`Target`, `Topic`, `Subject`,
  > `To`, `For`), no a quien la hizo. Para ésas «poner identidad como
  > puerta» no es trabajo pendiente: es otra pregunta, **autorizar** en vez
  > de **atribuir**.
  >
  > Quedan **seis**, y tampoco son un grupo. Ninguna está cableada a un
  > consumidor que **presente identidad** —los emisores del repo son tres,
  > `HttpCaseWorkflowService` y `HttpGovActNotificationService` hacia
  > Gobierno y `HttpAuditTrailWriter` hacia `Api.Audit`, y ninguno apunta a
  > ellas—, pero de ahí **no** se sigue que a todas les falte cableado:
  >
  > | capacidad | campo | consumidor hoy |
  > |---|---|---|
  > | `Moderation` | `DecidedBy`, `Reporter` | ninguno |
  > | `Engagement` | `Actor` | ninguno |
  > | `Documents` | `Owner` | sólo nombrada por Gobierno, como adjunto por referencia |
  > | `Cart` | `Owner` | **el CMS, directo** (`POST /v1/carts` en `HttpShopOrderService`) |
  > | `Orders` | `Buyer` | **`Bff.Tienda`** |
  > | `Payments` | `Payer` | **los tres orquestadores** |
  >
  > Y para las tres de abajo el actor **no es un seudónimo opaco**: con
  > sesión iniciada es el `memberKey`, que es exactamente la forma de
  > sujeto que el CMS ya firma (el seudónimo de #47 sustituye al **correo**,
  > no al identificador). Así que su disparador no es un cableado que
  > falte: es **extender `Synergos:Identity:Mode=Api` más allá de
  > Gobierno**, y para `Orders` y `Payments` además decidir si el
  > orquestador **propaga** la cabecera — que hoy no la propaga nadie y no
  > está escrito en ninguna parte que deba.
  >
  > Las tres de arriba sí esperan su primer consumidor, y conviene que sea
  > entonces: se verifica contra un llamador real y no contra un fake, que
  > es lo que hacía que #72 tocara —`Api.Audit` acababa de estrenar
  > consumidor con #15—.

  > **La bitácora estaba blindada contra reescribir el pasado y abierta a
  > FABRICARLO** (#72). No hay `PUT` ni `DELETE` desde el primer día —el
  > propio fichero explica que «una bitácora editable no sirve de
  > bitácora»— pero el actor **y sus roles** llegaban del cuerpo sin que
  > nadie los comprobara: con la llave compartida se escribía un asiento a
  > nombre de quien fuera y quedaba permanente. Un registro inmutable de
  > asientos falsificables es **peor** que no tenerlo — es una mentira con
  > aspecto de prueba, y justo la pieza que alguien va a citar el día que
  > haya que demostrar quién hizo qué.
  >
  > Es el defecto #48 exacto en la capacidad cuyo valor entero es ser
  > creíble, y **el más silencioso de los tres** (#42, #48, éste): un
  > asiento falso no falla, se guarda. Los tres comparten la misma forma
  > —la regla estaba bien y la FUENTE DEL DATO estaba mal— y los tests no
  > lo vieron por la misma razón: construían el `Actor` a mano.
  >
  > **El asiento ahora guarda con qué se afirmó** (`ActedWith`), como el
  > acuse de `Api.Messaging` desde la HU #13. Sin eso el arreglo sería
  > invisible: los asientos nuevos valdrían más que los viejos y nada lo
  > diría. **Los anteriores dicen «no consta», que es la verdad** —
  > rellenarlos con `CmsSession` inventaría una afirmación que nadie hizo,
  > y exigiría reescribir un registro append-only.
  >
  > **Sin llave se sigue auditando.** Parar la bitácora cuando falla la
  > identidad convierte una caída en un hueco en el registro, que es peor
  > que un asiento débil; un token presentado donde no se puede comprobar
  > se **rechaza**, no se ignora. Hay gate (`AuditActorSourceTests`), y
  > vigila las dos formas de deshacerlo: volver a armar el actor con el
  > cuerpo, o resolverlo bien y no guardar con qué.
  >
  > **Y la comprobación subió a `Synergos.Shared` al segundo consumidor**
  > (§0.B.17): nació en `Api.Workflow` con #48 y volvió a hacer falta,
  > igual, acá. Con una tercera copia el mismo token valdría distinto
  > según a quién se le presente — y una capacidad no puede referenciar a
  > otra, así que `Shared` es el único sitio válido.

  > **Y las dos tenían un endpoint fuera de la lista** (#81 y #83), los dos
  > encontrados barriendo las veinte en busca de la forma de #42/#48/#72.
  > `Api.Messaging` **verificaba quién ACUSA y creía a quién ESCRIBE** —el
  > mismo `who`, en la misma capacidad, dieciséis líneas más arriba—, y lo
  > que `CheckPost` sí miraba era otra cosa: si el remitente participa del
  > hilo, que es autorización y no autenticación. Importa porque #62 puso el
  > cuerpo de un acto administrativo en un mensaje de hilo, y no hay `PUT`
  > ni `DELETE`: un acto notificado con autor falsificable es #72 sobre el
  > documento que sostiene un plazo legal. Hoy el mensaje guarda
  > `PostedWith`, y el CMS **presenta** la identidad de la ventanilla al
  > publicarlo.
  >
  > **Y ahí NO se reintenta sin firmar, al revés que en la bitácora.** La
  > diferencia es qué es peor en cada caso: perder un asiento es peor que un
  > asiento débil, porque un rastro que falta no se nota; una notificación
  > débil, en cambio, le dice a la entidad que un término empezó a correr.
  > Cuando no se puede probar quién notifica, es mejor que falle a la vista.
  >
  > **En `Api.Consent` el hueco era el derecho al olvido.** El gate decía,
  > con todas las letras, que «un gate que sólo mirara `grants` dejaría
  > `grants/revoke` de puerta de atrás» — el razonamiento correcto y la
  > **lista corta**: eran tres, y `forget` retira TODOS los permisos de una
  > persona sin pedir nada. La ironía estaba en el propio fichero: revoca en
  > vez de borrar «para poder demostrar que la revocación se atendió», y esa
  > prueba no decía quién la pidió.
  >
  > **Es el mismo error por tercera vez: una lista sacada de la cabeza en
  > vez de medida contra el fichero** —como «los seis de `Synergos.Shared`»
  > y «faltan las otras 16»—. Así que el gate nuevo **no enumera: cuenta**,
  > y deduce qué endpoint escribe mirando si el método del servicio al que
  > llama toca el almacén. `/v1/grants/check` es una LECTURA que usa POST a
  > propósito, para que el sujeto y el propósito no queden en la URL, así
  > que el criterio no podía ser «MapPost». Comprobado mutando: con `forget`
  > fuera de la lista escrita a mano, el gate que cuenta lo caza igual.

  > **«Fulano consintió» no dice nada sin «y así se supo que era
  > fulano»** (rebanada 5). El día que alguien niegue haber dado un
  > permiso, la diferencia entre un token verificado y la palabra del
  > sitio es la diferencia entre poder sostenerlo y no — y el registro
  > no guardaba ninguna de las dos.
  >
  > **Otorgar y revocar llevan la suya por separado**, y no es simetría
  > por gusto: retirar el permiso de otro es tan grave como darlo en su
  > nombre, y un consentimiento dado en ventanilla y retirado desde el
  > portal son dos actos. Un gate que sólo mirara `grants` habría dejado
  > `grants/revoke` de puerta de atrás.
  >
  > **Nulo es «no consta», no un default.** Los permisos anteriores a
  > esta rebanada no llevan afirmación, y ésa es la verdad sobre ellos:
  > rellenarlos con `CmsSession` sería inventar una comprobación que
  > nadie hizo — el defecto #42 con otro disfraz. Siguen siendo válidos.
  >
  > **Y el gate mira el CABLEADO, no la regla.** La primera versión de
  > esta rebanada pasaba en verde con el defecto puesto: reemplazar la
  > llamada del borde por «anotar lo que declaró el llamador» no ponía
  > rojo nada, porque los tests cubrían el helper compartido y el
  > servicio, y el que decide es lo que hay entre los dos. Hoy hay gate
  > (`IdentityGateTests`) sobre los dos endpoints que escriben.
  >
  > Verificado en vivo contra `Api.Consent` + `Api.Identity`: declarar
  > `IdentityToken` sin presentarlo se rechaza; **presentando el token,
  > la capacidad sube la afirmación sola** aunque el llamador declare
  > `CmsSession`; un token de otro sujeto se rechaza con
  > `token_subject_mismatch`; y **sin llave de firma arranca igual** —el
  > camino del clon limpio— pero un token presentado ahí se **rechaza**,
  > no se ignora.

  > **El token de otra persona no sirve para actuar como ésta**, y ése
  > es el caso que justifica la HU entera: sin comprobar que el sujeto
  > del token es el `who` de la petición, la capacidad seguiría creyendo
  > el `who` que le mandan y el token sería decoración.
  >
  > **Quien solo verifica arranca sin llave** —es el camino del clon
  > limpio— pero arranca **sin poder verificar**, que no es lo mismo que
  > verificando mal: un token presentado ahí se **rechaza**
  > (`identity.token_not_verifiable`), no se ignora. Ignorarlo dejaría
  > que alguien mandara cualquier cosa y siguiera adelante como si no
  > hubiera mandado nada.
  >
  > **`Api.Workflow` es la segunda** (defecto #48), y su caso es distinto:
  > lo que verifica no es «con qué fuerza» sino **de dónde salen los
  > roles**. Venían en el CUERPO de la petición, así que cualquiera con la
  > llave compartida se ascendía a funcionario escribiendo una línea de
  > JSON — y `requiredRoles`, que el código presentaba como «lo que hace
  > que esta capacidad sirva a Gobierno», no guardaba nada. Los tests no lo
  > vieron porque construían el `Actor` a mano: la regla estaba bien y la
  > **fuente del dato** estaba mal, igual que en #42.
  >
  > Hoy el token gana sobre lo declarado y su sujeto tiene que ser quien
  > actúa. Y **el agujero ya se puede cerrar del todo con
  > `Workflow:Roles:RequireVerifiedRoles`**: desde la rebanada 4 el CMS
  > **presenta** la identidad del funcionario (`Synergos:Identity:Mode=Api`),
  > así que ese interruptor dejó de ser inútil. Sigue en `false` por defecto,
  > pero por otra razón: el seguimiento de pedidos (#46) todavía declara su
  > actor, y exigir roles verificados a TODO pararía cualquier pipeline cuya
  > transición pida rol. Es la forma de #27 — el despliegue declara su postura.

  > **El CMS emite la identidad de quien decide** (rebanada 4). Lo que cambia no
  > es que se pueda actuar, es **con qué se respalda**: el sujeto viaja firmado y
  > la capacidad deja de creerle al llamador quién actúa y con qué roles.
  >
  > **Y para eso `Api.Identity` tuvo que admitir un principal SIN credencial.**
  > Un token se emite para un principal que exista, y quien actúa desde el CMS ya
  > entró por otra puerta: su sesión de Umbraco. Exigir contraseña obligaba al
  > CMS a inventarse una por persona y a custodiarla — o sea a fabricar
  > credenciales que nadie usa, que es sólo superficie de ataque. Intentar
  > autenticar a uno de esos se rechaza con `identity.no_credential` y **no
  > cuenta como intento fallido**: si contara, cualquiera bloquearía a un
  > funcionario mandando cinco peticiones por una puerta que esa persona no usa.
  > Verificado en vivo: tras siete intentos, sigue decidiendo.
  >
  > **Con `Api.Identity` caída no se para nada**: sin token se sigue declarando,
  > que es lo que se hacía antes. El emisor **nunca lanza**, y hay gate — lanzar
  > devolvería el punto único de fallo que se evitó verificando en local.
  >
  > **Falta el puente con el Member de verdad.** Hoy el funcionario es un actor
  > de demo que la cara de Gobierno tiene cableado; lo que esta rebanada mudó es
  > de dónde salen sus roles, no quién es. El día que la ventanilla tenga
  > miembros reales, el sujeto sale de la sesión y los roles de sus grupos.

  > **Verificación LOCAL, y ésa es la decisión de fondo.** El token se
  > comprueba con la llave, sin llamar a `Api.Identity` — llamarla en cada
  > petición la convertiría en el punto único de fallo de las veinte, y es
  > la peor candidata porque corre sobre fichero JSON con `lock` de
  > proceso. Con esto, `Api.Identity` caída significa «no entran sesiones
  > nuevas», no «se para todo». `IdentityTokens` vive en `Synergos.Shared`
  > desde el primer día porque **no hay un sitio válido con un solo
  > consumidor**: en una capacidad obligaría a que otra la referenciara,
  > que está prohibido de plano (§11).
  >
  > **Y lo que el token NO es, para que nadie lo suponga.** Lo emite un
  > servicio nuestro a partir de la palabra del CMS (camino (b)), así que
  > **no es prueba más fuerte frente a un tercero** que `CmsSession`: la
  > cadena de confianza toca fondo en el mismo sitio. Lo que compra es
  > integridad interna — el sujeto viene firmado y no se puede reapuntar,
  > así que una capacidad deja de creerle al llamador quién actúa. El
  > escalón probatorio de verdad es `GovFederation`, fuera de alcance.
  >
  > 15 minutos de vigencia con renovación y techo de sesión de 8 h; los
  > roles viajan dentro, así que revocar uno tarda lo que quede de
  > vigencia — ése es el precio de no tener punto único de fallo. El `kid`
  > va desde el primer día aunque haya una sola llave. **La llave de firma
  > NO es la compartida**, y sin ella `Api.Identity` **no arranca — y
  > falla al cablear, no en la primera petición**. La distinción la
  > destapó levantar el proceso: la llave se leía dentro de una fábrica
  > de singleton y en una API mínima nadie la resuelve hasta que llega
  > una petición que la inyecta, así que un despliegue sin llave
  > arrancaba **verde**, contestaba `/health` y pasaba la prueba de humo.
  > Reventaba cuando una persona intentaba entrar. Hay gate
  > (`IdentityTokenSetupTests`), y comprueba **cuándo** falla, no solo
  > que falle.
  >
  > **Y el compose ya se había desincronizado por lo mismo**: nombraba
  > `Identity__Tokens__*` de cuando la sección era propia, así que la
  > llave llegaba a una sección que nadie lee y un servidor bien
  > configurado se comportaba como uno sin llave. El gate de la sección
  > miraba código C# y el defecto vivía en un `.mjs`; ahora mira los dos.

  `Api.Messaging` ya guardaba **con qué se afirmó** la
  identidad de quien accede (HU #13) precisamente para que el día que
  esto se arreglara los registros viejos no mintieran sobre su propia
  fuerza. Ese día llegó con la rebanada 3, y **los registros viejos
  siguen diciendo la verdad**: dicen `CmsSession` porque eso es lo que
  eran. Ese `IdentityAssertion` **subió a `Synergos.Core`** al aparecer
  su segundo consumidor (el asiento de auditoría de la HU #15), así que
  hubo **un solo sitio** que pasar de decir `CmsSession` a decir otra
  cosa. Hay gate: declararlo dos veces rompe el build.
- **El diploma ya lo sella una capacidad** (HU #45, `Synergos:Academy:Mode=Api`).
  Lo que se gana es la **custodia**, no el algoritmo: el id ya era un HMAC
  opaco con llave del servidor (ADR 0124), pero esa llave **no sabía
  retirarse** — no había forma de rotar sin invalidar todos los diplomas ni
  registro de con cuál se firmó cada uno. Verificado en vivo: tras retirar la
  llave y crear otra, **un diploma emitido antes de rotar sigue verificando**.

  > **Va a `/v1/seals` y no a `/v1/signatures`**, y ése fue el hallazgo: aquel
  > token vence (≤365 d), no es determinista y **publica su payload sin
  > llave**. Las tres cosas son correctas para lo que ese endpoint hace y
  > ninguna sirve para un diploma, que no vence, se re-emite igual y lleva a
  > su titular dentro del contenido sellado.
  >
  > **El firmante local NO se descarta: queda verificando los ids
  > anteriores.** El sello y el HMAC local no dan el mismo valor, así que sin
  > eso cada QR ya impreso dejaría de valer el día del despliegue — y no
  > ruidosamente: contestando que la credencial no vale, que es lo peor que
  > puede decir. Se reconocen por su forma (32 hex) y ni salen a la red.
  >
  > **Con la capacidad caída no se emite ni se verifica un diploma nuevo, y NO
  > se da por bueno.** Comprobar el sello contra el sujeto es lo único que
  > impide que quien escriba en el almacén fabrique una credencial con el
  > nombre que quiera. Los diplomas viejos se siguen verificando, porque son
  > locales. Hay gate (`AcademyWiringTests`).

- **`Api.Booking` ya deja llegar del sujeto a su recurso** (HU #25):
  `GET /v1/resources?subjectKind=&subjectId=`, calcando lo que
  `Api.Inventory` hacía con `/v1/items`. Faltaba, y obligaba a que el
  identificador interno del recurso viajara hasta el CMS — que ninguna
  convención podía adivinar, porque lo genera la capacidad.
- **Ninguna capacidad llama a otra, y ya hay gate** (#49). Apareció el
  primer caso —mandar un acceso rechazado a `Api.Audit`— y se decidió NO
  abrir esa flecha desde la capacidad (HU #15). `CLAUDE.md` decía que el
  día que se abriera, el gate iría antes que el código: se escribió
  **mientras estaba en verde**, que es cuando es gratis —sin excepciones
  que negociar y sin nadie esperando—. Eran tres dientes y no uno:
  referencia de ensamblado Api→Api, **nombrar** a otra capacidad con los
  comentarios quitados, y `HttpClient` dentro de una capacidad, que va con
  lista y con la razón al lado. El tercero es el que atrapa lo que los
  otros no ven: una URL que llega por variable de entorno sin que el
  nombre aparezca nunca. **No prohíbe hablar con un tercero** —hoy
  `Api.Notifications` sale a Resend (ADR 0131) y está en la lista—; obliga
  a que salir sea una decisión escrita. Y la lista se vigila en los dos
  sentidos: un permiso que ya nadie usa también rompe el build, porque un
  permiso que sobra deja de leerse.

  > **Y se decidió también quién SÍ lo escribe: el orquestador.** Se
  > miraron las tres opciones y las dos que se podían entregar ya —que
  > emita la capacidad, o que emita un middleware compartido— acaban en
  > lo mismo: cliente y llave hacia `Api.Audit` dentro del proceso de
  > cada capacidad, o sea las veinte dejando de ser hojas. La flecha se
  > movería de fichero, no desaparecería. La razón de fondo es que el
  > caso —un acto administrativo notificado— es una regla de Gobierno,
  > no de la plataforma: nadie cree que un 403 de `Api.Cart` merezca
  > asiento de auditoría.

  > **Y el orquestador que lo escribe resultó ser el CMS** (HU #15,
  > `Synergos:Audit:Mode=Api`). El razonamiento de arriba se sostiene
  > entero —es política del dominio, y una capacidad no puede llamar a
  > otra— y lo único que estaba mal era **a quién nombraba**: se dio por
  > hecho que el compositor de Gobierno sería `Bff.Gob`, y quien recibe
  > el rechazo es la cara de Gobierno del CMS, que no es una capacidad y
  > por tanto no abre ninguna flecha lateral. Es la misma corrección que
  > ya se hizo cuatro veces sobre el mapa del cableado, aplicada esta vez
  > a un consumidor en vez de a un `Stub*`. **#15 estuvo bloqueada dos
  > olas por un servicio que el propio código dice que no debe existir.**
  >
  > **Lo que faltaba de verdad era la superficie**, y llegó con #62: hasta
  > que hubo un acto que abrir, no había acceso que rechazar ni que
  > auditar. Hoy el 403 de un acto ajeno deja asiento —quién lo afirmó,
  > sobre qué y por qué se rechazó— y antes salía sin escribirse en
  > ninguna parte.
  >
  > **El asiento dice `CmsSession` y NUNCA más que eso.** Ese camino no
  > presenta ni verifica ningún token: lo único que respalda al actor es
  > nuestra cookie. Que el despliegue *sepa emitir* identidad verificable
  > no cambia lo que ahí se comprobó, y escribir `IdentityToken` porque
  > se podría haber presentado uno guardaría como hecho lo que nadie
  > verificó — el defecto #42 sobre el registro que más se conserva.
  >
  > **Y desde la #72 no hace falta creerle a este lado**: la afirmación
  > viaja en el campo que la capacidad **resuelve** y guarda como
  > `ActedWith`, no en un detalle opaco — tenerla en los dos sitios los
  > dejaría discrepar sobre un mismo hecho, y ganaría el que nadie
  > comprueba. Verificado en vivo: declarar `IdentityToken` sin
  > presentarlo lo rechaza **ella** con `assertion_not_proven`, y el
  > hueco queda anotado de este lado.
  >
  > **El suelo es `CmsSession`, y no es un relleno.** La capacidad exige
  > una afirmación —sin ella rechaza con `access_requires_identity`—, así
  > que un asiento que no registra ninguna se volvería un hueco: ruido en
  > vez de rastro. Se manda el suelo, que significa «nos fiamos de quien
  > llama», o sea la *ausencia* de comprobación. Es la diferencia con el
  > nulo de `Api.Consent` (#14 rebanada 5), donde el campo **sí** admite
  > «no consta» y por eso los permisos viejos lo conservan.
  >
  > **Se escribe LOCAL primero y se reenvía después**, y el JSONL sigue
  > siendo el modelo de lectura: las lecturas del seam son síncronas y la
  > bitácora del backoffice se pinta en cada carga, así que con
  > `Api.Audit` caída el administrador **sigue viendo qué pasó**; lo que
  > se para es que el asiento salga de la máquina. Es la forma del
  > timeline de pedidos (#46). Y **si el reenvío no llega queda escrito
  > que no llegó**: un segundo asiento local nombra al que se quedó y por
  > qué —un rastro que se pierde en silencio se pierde justo el día que
  > hace falta—. Ese asiento del hueco no se reenvía, o sería contarle a
  > la capacidad caída que no se pudo hablar con ella.
  >
  > **El correo no sale de esta máquina**: viaja un seudónimo estable, y
  > el nombre legible se queda en el JSONL. Verificado en vivo contra la
  > capacidad: el asiento llega con `CmsSession`, sin dato personal y sin
  > duplicarse al reintentar; matándola, el asiento sobrevive local, el
  > hueco queda escrito y la bitácora se sigue leyendo. Hay gate
  > (`AuditWiringTests`).
  >
  > **Y desde la rebanada 6 el asiento va FIRMADO** (HU #14). Con la llave
  > compartida sola, quien pueda hablar con la capacidad escribe un asiento a
  > nombre de quien quiera y queda permanente — es el defecto #72 visto desde
  > el lado del que escribe. El sujeto del token es **el mismo seudónimo** que
  > viaja como actor, porque la capacidad rechaza uno que nombre a otro
  > (`token_subject_mismatch`): eso es lo que lo vuelve prueba y no adorno.
  > Verificado en vivo con `Api.Identity` y `Api.Audit`: la capacidad sube la
  > afirmación a `IdentityToken` ella sola, mientras el CMS sigue declarando
  > `CmsSession`.
  >
  > **Y si la capacidad NO puede comprobarlo, el asiento se repite sin firmar.**
  > Es el principio de #72 sostenido —«parar la bitácora cuando falla la
  > identidad convierte una caída en un hueco en el registro, que es peor que un
  > asiento débil»—: sin ese reintento, presentar identidad a una capacidad sin
  > llave de verificación habría perdido **todos** los asientos, y el cambio que
  > venía a fortalecerlos los habría borrado. **Sólo por esa causa**: un token
  > vencido o de otro sujeto son fallos de este lado, y repetirlos sin firma los
  > escondería para siempre detrás de un asiento débil.

  > **Y la bitácora no se podía LEER tras un reinicio** (defecto #82), que lo
  > destapó levantar los procesos para verificar lo de arriba. `Actor.Roles` es
  > un `IReadOnlySet<string>`: `System.Text.Json` lo escribe y **no sabe
  > leerlo**, así que `Api.Audit` guardaba sus asientos y devolvía 500 en toda
  > lectura en cuanto el proceso se reiniciaba. Blindada contra reescribir el
  > pasado y sin poder leerlo — hermano exacto de #72, donde la propiedad que la
  > capacidad anuncia como su razón de ser tampoco se cumplía.
  >
  > **El caché de `JsonCollectionStore` es lo que lo escondía**: mientras el
  > proceso vive, las lecturas salen de memoria y nunca deserializan. La primera
  > que toca el disco es la de después del reinicio. Por eso ningún test lo vio
  > —ninguno reinicia— y por eso el gate nuevo abre un almacén **nuevo** sobre el
  > mismo fichero: comprobado mutando que leer del mismo pasa en verde con el
  > defecto puesto.
  >
  > **Y el arreglo reconstruye por `Actor.Of`, no por el constructor.** Es el
  > punto fino: `Of` arma el conjunto con `OrdinalIgnoreCase`, así que un
  > conversor que devolviera un `HashSet` cualquiera dejaría
  > `HasAnyRole("Funcionario")` en `false` después de reiniciar sin que nada
  > fallara — cambiaría un 500 ruidoso por una decisión de permisos equivocada y
  > callada. Vive en `Synergos.Shared` y no en `Core`: Core es el vocabulario y
  > no sabe qué es un disco; cómo se escribe ese vocabulario es fontanería, y
  > `JsonCollectionStore` es el embudo de las veinte. **Los datos anteriores se
  > recuperan solos** —los bytes siempre estuvieron bien— y se verificó leyendo
  > el mismo almacén que devolvía 500.

  > **Y el vocabulario de la afirmación subió al segundo consumidor**
  > (`CLAUDE.md` §17): `GovActAssertions` nació en #62 con uno y hoy es
  > `IdentityAssertions` en `Synergos.CMS.Interfaces`. Es el gemelo de lo
  > que `IdentityAssertion` hizo en el árbol de servicios subiendo a
  > `Synergos.Core`, y **sigue siendo `string` a propósito**: los dos
  > árboles no se referencian. Hay gate contra la segunda declaración.
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
  > **Y el carrito multi-producto ya cruza también** (HU #40), con el mismo
  > interruptor. Necesitó tres cosas y ninguna era el cliente HTTP. Una:
  > **periodo en `TravelCartItem`** —un apartado de `Api.Booking` ES una ventana
  > sobre un recurso, y sin fechas habría que inventárselas—. Dos:
  > **confirmación PARCIAL** en el orquestador, porque quien compró un vuelo, un
  > hotel y un auto no pierde el vuelo porque el auto se agotó; el default sigue
  > siendo todo-o-nada, que es lo correcto para un paquete. Tres: **una puerta
  > para DEVOLVER lo no cumplido** (`POST /v1/trips/{id}/refund`), y ésa es la
  > que faltaba de verdad: sin ella, «quien vendió ordena la devolución» era una
  > frase sin forma de cumplirse y la plata del ítem caído se quedaba acá, **sin
  > que nada fallara**. El monto llega calculado, igual que la penalidad de
  > cancelar: el orquestador cotiza el viaje entero a propósito, así que no sabe
  > cuánto vale una parte.
  >
  > **Lo que NO se hace, y es lo más fino:** anotar una compensación cuando esa
  > devolución falla. El motor sólo sabe devolver *todo lo devolvible*, así que
  > el barrido acabaría deshaciendo un viaje que SÍ se entregó. El rechazo sale
  > hacia quien vendió, que puede repetir con la misma llave.
  >
  > **Y el cableado destapó que el expediente leía el estado de cada línea del
  > almacén del motor en proceso.** Con un motor que no vive en este proceso ese
  > almacén está vacío: un viaje confirmado habría mostrado sus tres líneas en
  > «Held» para siempre, sin que nada fallara. Ahora el estado se sella al
  > liquidar y la lectura en vivo sólo gana cuando existe.

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
  > una revisión. **Ya lo hacen los cuatro**: `Bff.Tienda` era el último que mandaba
  > el correo entero y se corrigió con el defecto #47 —que además destapó que el
  > listado devolvía ese `buyerId` como si fuera el correo, y para quien tiene
  > sesión eso ya era un `memberKey` en hexadecimal en pantalla.
  >
  > **Y el comprador ya no queda encerrado** (defecto #41). Encontrar la llave
  > de idempotencia no significa «esto ya pasó»: si la compra anterior se
  > deshizo entera no queda nada que duplicar, así que la misma llave abre un
  > intento nuevo —con identidad propia, sin pisar la muerta— y sobre una saga
  > VIVA sigue devolviendo esa, que es lo que impide que un reintento por
  > timeout compre dos veces. Sólo `Compensated` desbloquea: con la
  > compensación fallida algo quedó colgado y necesita una persona. La regla
  > vive en `SagaEngine.Abrir` y no en cada flujo — estaba copiada en los dos
  > orquestadores, y el defecto también.
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
  bloqueo**: el CDN está VIVO (`https://synergos-ui.synergos-labs.workers.dev`)
  y `HttpBundleRegistryClient` resuelve contra él — verificado con el cliente
  real compilado, no con fakes. **Cuántos elementos sirve se mide, no se
  recuerda** (#86): `curl -s $URL/synergos/registry.json | jq '.elements | length'`.

  > **El `| jq length` a secas devuelve `4`, no 130**, y estuvo así el rato que
  > tardó en verse: la raíz del registry es un objeto —`generated`, `version`,
  > `baseUrl`, `elements`— así que `length` cuenta sus CLAVES. Un comando que
  > contesta un número plausible y equivocado es peor que ninguno: la cifra
  > venía con él justamente para que se pudiera comprobar, y comprobarla
  > confirmaba otra cosa. Se corrigió ejecutándolo, que es la única forma de
  > saberlo — mirarlo no lo dice.
  El CDN servía **130 elementos** el 2026-09-05, y el repo hermano declara
  dos más que no publica (`synergos-stat-counter` y `synergos-module-mount`).
  Ésta es la ÚNICA cifra del CDN que da este fichero, y va con el comando al
  lado a propósito: es la única que no se puede cruzar contra el disco de este
  repo —depende de la red y del hermano—, así que copiarla a otro sitio es
  garantizar que se desvíe. Ya pasó, y en tres versiones distintas a la vez.
  Hay gate (`CifrasDeClaudeMdTests`), y no comprueba que sea correcta —desde
  acá no se puede— sino **que no esté copiada**.
  Lo que falta es que el despliegue configure `SYNERGOS_CDN_MODE=Http` +
  `SYNERGOS_CDN_URL` (ver `.env.example`). Es una decisión de entorno del
  arquitecto, no trabajo pendiente de código. Ver §9 y ADR 0132.

  > **Y son las DOS, no una** (#56). Poner el modo y olvidar la URL ya no pasa
  > en silencio: el compose la manda **presente y vacía** —que pisa el default
  > `/cdn-bundles` en vez de dejarlo—, así que el registry se pediría a una URL
  > relativa con un cliente sin `BaseAddress`. **El modo `Http` ahora falla al
  > cablear**, con el mensaje que nombra la variable. Antes arrancaba verde,
  > contestaba `/health` y pasaba la prueba de humo, y reventaba en el primer
  > render con un `<synergos-*>` — la misma forma que ya costó la llave de firma
  > de `Api.Identity` y el `TimeProvider` de la ADR 0132. Se exige absoluta
  > **sólo** en `Http`: en `FileSystem` la relativa es la correcta, porque la
  > sirve el propio sitio.
