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
   **3310 passing** en TRES suites (#135), y cada una cuadra su propia cifra
   contra su ensamblado por reflexión (`SuiteCountTests`, enlazado en las tres):

   | suite | tests | qué referencia |
   |---|---:|---|
   | `Synergos.CMS.Tests` | 2252 | **un** proyecto: `Synergos.CMS.Web` |
   | `Synergos.Servicios.Tests` | 633 | Core, Shared, las 20 `Api.*` y los 5 `Bff.*` |
   | `Synergos.Arquitectura.Tests` | 425 | Web, Shared, `Bff.Core`, `Bff.Tienda` |

   **El reparto ES la regla, no organización.** Antes había un solo proyecto con
   **28** referencias, y era el ÚNICO sitio del repo que unía los dos árboles: el
   grafo de producción ya estaba limpio —`Synergos.CMS.Web` depende de
   `Application` e `Interfaces` y de nada más— pero abrir la solución arrastraba
   el backend entero por la vía de los tests. Hoy la suite del CMS **no puede**
   tocar una capacidad aunque alguien quiera: se lo impide el compilador.
   Y la excepción a §0.B.11 —poder ver los dos lados— queda en **un** proyecto y
   con su nombre, en vez de ser una nota dentro del de al lado.
   **55 de los 66 gates no usan un solo tipo de producción**: leen la FUENTE del
   disco, que es lo que les permite vigilar un Razor, un `.mjs`, un compose o un
   `appsettings` — ninguno de los cuales tiene tipos.
   Memoria `feedback_tests_after_full_migration` (status: superseded). En el árbol de servicios el gate es más duro:
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

Umbraco 13.16.2 — **no upgrade** a 14+ sin ADR nuevo. La razón:
Umbraco 14+ descontinuó Macros, cambió el editor de Block Grid a
Lit/TS, y requiere .NET 9+. Ver ADR 0001 y su enmienda.

**Lo que está clavado es la rama 13 LTS, no un parche concreto**, y esa
distinción costó entenderla: subir DENTRO de 13.x no es el upgrade que
ADR 0001 prohíbe — lo prohibido es 14+. Esta línea decía **13.13.1**
desde el andamiaje, y hay gate que la cruza contra
`Directory.Packages.props` (`VersionDeUmbracoTests`), porque la versión
estaba escrita a mano en varios sitios y **ninguno los cruzaba**.

NU1902 (vulnerabilidad moderate) es un conocido-sin-patch dentro
del branch 13.x. Aceptado. Es `GHSA-54mj-vcvj-q3v5`, y su rango
vulnerable —`(, 16.3.3]`— es lo que hace verdad ese «sin patch»: no hay
versión de la rama 13 que lo cierre.

> **Y NU1903 NO se aceptó, que es la mitad que importa** (#149). Apareció
> `GHSA-wr57-hqmp-fgvh` —**high**, rango `[12.0.0, 13.15.1)`— y con
> `TreatWarningsAsErrors` puesto desde el #134 **el build se puso rojo sin
> que nadie tocara un commit**: lo que cambió fue la base de avisos que
> `NuGetAudit` se baja en el restore, así que se puso rojo en todas las
> máquinas a la vez y con el árbol entero compilando en cero avisos.
> Tenía parche **dentro de la rama clavada**, así que la salida no era
> `NoWarn` sino subir de 13.13.1 a 13.16.2 — el último 13.x publicado.
> Medido antes de subirlo: build en 0 avisos con la auditoría ENCENDIDA y
> las tres suites **en las 3280 de entonces**, ni un test movido — los cinco
> que añadió fueron el gate que esa misma HU escribió. (Hoy son **3310**: el #141
> se llevó dos de esos cinco al sacar el arnés de este repo — ver
> `feedback_moving_an_artifact_out_of_a_repo_moves_it_out_of_its_gates_reach`.)

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
│       ├── ContentTypes/        DocTypes + ElementTypes + Compositions (258 archivos)
│       ├── DataTypes/           133 archivos (69 DTSelect*) + UrlPicker/MediaPicker/Tags/ContentPicker
│       ├── Dictionary/          i18n es-CO + en-US (481 keys)
│       ├── Languages/           es-CO (default) + en-US
│       ├── MediaTypes/          synImage + synDocument + synIcon + los stock de Umbraco
│       ├── MemberTypes/         member
│       ├── Templates/           Razor template registry (14)
│       ├── Content/             contenido editorial autorado (ADR 0129) — lo exporta
│       │                        uSync al guardar; el agente NO lo autora
│       └── Media/               nodos de la biblioteca (binarios en wwwroot/media/)
├── Synergos.CMS.Tests/          xUnit — SÓLO el CMS. Referencia UN proyecto (#135)
│   ├── Controllers/ Services/   el borde y los seams del árbol del CMS
│   └── Dto/ Filters/ Proxies/   …y su fontanería
├── Synergos.CMS.Benchmarks/     BenchmarkDotNet (WebhookSigner + BridgeContextSerializer)
├── Synergos.Arquitectura.Tests/ LOS GATES: segregación (17) + molde (12)
│   └── Architecture/            + capas (8) + imagen de contenedor (6)
│                                + compose (12) + despliegue (18, ADR 0133)
│                                + molde del vertical (13, doc 12)
│                                + seudónimo único (3, #120)
│                                + portada de arranque (5, #119)
│                                + configuración del build (3, #151)
│                                + secciones de configuración (2, #154)
│                                + escalado de réplicas (6, #152)
│                                + transitoriedad de un rechazo (2, #129)
│                                + perfil de producción (3, #150)
│                                + claves de Umbraco en el compose (1, #159)
│                                Único que ve los DOS árboles — va en la raíz
│                                justamente para que esa excepción se lea.
│
└── backend/                     EL BACKEND PURO (#136). Nada de acá sabe qué es
    │                            Umbraco, y el CMS no referencia nada de acá.
    ├── nucleo/
    │   ├── Synergos.Core/       EL VOCABULARIO. Ref, Money, TimeWindow,
    │   │                        Rejection, Result, IdempotencyKey, Actor,
    │   │                        Page, IdentityAssertion.
    │   │                        CERO referencias. No sabe qué es ASP.NET.
    │   └── Synergos.Shared/     fontanería de host. Llave compartida,
    │                            Rejection→HTTP, libro de idempotencia,
    │                            JsonCollectionStore, correlación.
    │                            Solo puede referenciar Core — UNA flecha.
    ├── capacidades/             LAS 20 CAPACIDADES, agnósticas. 137 endpoints.
    │     Sessions · Booking · Identity · Audit · Notifications · Documents ·
    │     Catalog · Pricing · Cart · Orders · Payments · Inventory · Workflow ·
    │     Messaging · Signing · Consent · Engagement · Geo · Fulfillment ·
    │     Moderation
    ├── orquestadores/
    │   ├── Synergos.Bff.Core/   la máquina de sagas: deshacer, reintentar,
    │   │                        rendirse, avisar. Promovida al segundo
    │   │                        consumidor; el TERCERO (Eventos) y el CUARTO
    │   │                        (Viajes) entraron sin tocarla.
    │   └── Synergos.Bff.*/      Salud, Tienda, Eventos y Viajes construidos;
    │                            faltan Realty, Gob, Academy, Social.
    └── Synergos.Servicios.Tests/  xUnit — las veinte capacidades y los cuatro
        ├── Api/                   orquestadores. NO ve el CMS, y ésa es la
        ├── Bff/                   propiedad: la separación de §0.B.11 la
        └── Core/                  impone el compilador, no una convención.
```

> **La ruta de un proyecto NO es su nombre, y hay gate** (`RutasDeProyectoTests`, #136). Un
> gate que necesite la fuente de una capacidad la pide por nombre —`Proyectos.Dir(
> "Synergos.Api.Booking", "Domain")`— y `Proyectos` la encuentra en el disco. Escribir la ruta
> a mano **funciona hoy y se rompe en la próxima reorganización**, con el coste pagado por
> quien mueva y no por quien lo escribió; y descubrir proyectos con
> `Directory.EnumerateDirectories(RepoRoot())` es peor, porque devuelve UNA carpeta y el gate
> **pasa en verde sin mirar nada** — medido: con eso puesto, `ComposeStackTests` da 12/12 sobre
> una lista vacía.

> **El árbol de servicios está construido y el producto ya lo consume**, aunque
> con todos los interruptores apagados por defecto. El CMS habla hoy con **nueve**
> capacidades —`Sessions`, `Booking`, `Workflow`, `Messaging`, `Signing`,
> `Identity`, `Audit`, `Cart` y `Payments`— y con los cuatro orquestadores. Esta
> línea decía «UNA» desde antes de las HU #24, #25, #33a, #35, #36, #40, #44,
> #45, #46, #62 y #15: un agente que la leyera concluía que no había nada
> cableado y proponía de cero lo que ya existe. Ver §11, que es donde está el
> detalle.
>
> **Y decía «siete» cuando ya eran nueve** (#114) — le faltaban `Cart`, de la
> séptima rebanada de la HU #14, y `Payments`, del cobro de la tasa de un
> trámite (#27): las dos entradas después de la última vez que alguien contó a
> mano. **Hoy la frase se DERIVA y hay gate** (`CapacidadesConectadasTests`):
> cruza las rutas `/v1/` que declara cada `Synergos.Api.*` contra las que piden
> los clientes HTTP del CMS que algún composer registra, y exige que esta frase
> las **nombre** una por una, no que acierte la cifra. Una cifra correcta con la
> lista incompleta es peor que una cifra equivocada: la lista es lo que alguien
> lee para saber qué ya existe antes de proponerlo de cero.

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
| "¿Por qué el backend está partido así?" | `Synergos.CMS.Web/docs/product/06-arquitectura-backend.md` |
| "¿Dónde para la atomicidad? ¿Qué es una capacidad?" | `Synergos.CMS.Web/docs/product/07-diseno-atomico-capacidades.md` |
| "¿Qué API necesita cada dominio? ¿Cuál es el molde?" | `Synergos.CMS.Web/docs/product/08-despiece-apis.md` — la matriz 20×9 y §4 |
| "¿Cómo se deshace lo que ya se hizo?" | `Synergos.CMS.Web/docs/product/09-compensacion-cruzada.md` |
| "¿Cuándo se promueve algo a una capa compartida?" | `Synergos.CMS.Web/docs/product/10-promocion-bff-core.md` |
| "¿Qué se hace con cada uno de los 49 `Stub*`?" | `docs/product/11-mapa-del-cableado.md` — hay gate (`WiringMapTests`) |
| "¿Cómo se escribe el vertical OCTAVO?" | `Synergos.CMS.Web/docs/product/12-el-molde-de-un-vertical.md` — **diez** pasos: los tres ejes con su tabla medida, las dos formas del eje transaccional, la CUARTA pregunta del eje 3 (§3.1) y qué gate comprueba cada paso. Hay gate (`MoldeDelVerticalTests`, 13) |
| "¿Cómo se pasa de un spec a un vertical? ¿Dónde vive el arnés?" | `Synergos.CMS.Web/docs/product/13-la-fabrica.md` — el spec como fuente, los doce sub-specs derivados del molde, los dos MCPs y el gate del arnés. **Ya no es sólo diseño**: el arnés vive en `Synergos.Fabrica` con su `arnes.lock.json` (#141) y el spec tiene formato y validador (#140) |
| "¿Cómo se escribe el spec de un vertical?" | `docs/specs/<vertical>/spec.md` — cabecera `---` con lo que un gate cruza contra el disco, prosa debajo. Formato en doc 13 §4; hay gate (`tools/spec-valida.mjs`, con `--autoprueba`) |
| "¿El molde da para GENERAR un vertical?" | Sobre Eventos, **sí**: el piloto 0 midió **71,4 %** y hoy da **100 %** (22/22), sin inventar ni perder nada — los cuatro hallazgos cerrados (#153, #154, #155). Eso desbloquea el piloto 1 y **no dice que el molde valga para el octavo**: el spec de Eventos se escribió hacia atrás, contra el disco. Doc 13 §9 · `node tools/spec-valida.mjs --oraculo=eventos` |
| "¿Qué rechaza esta capacidad?" | `Synergos.Api.X/Domain/XRules.cs` — las veinte lo tienen y hay gate (#58). Los códigos se componen de su `CodePrefix`; las excepciones son los cinco de `Api.Notifications/Transport/` y los cinco gemelos de `Api.Payments/Transport/`, que son fallos de la firma de un webhook y no reglas de negocio |

> **La forma de `window.synergos` se declara en TRES sitios, y hay gate** (#88,
> `ContractsIndexTests`): los `record` de `IHostBridgeContextBuilder.cs` que el CMS **emite**, las
> interfaces de `host-bridge.md` + `i18n-bridge.md` que el contrato **documenta**, y las de
> `tests/host-bridge.contract.test.ts` que el harness **prueba**. Se cruzan las tres por **nombre de
> campo** —no por tipo, y está dicho para no mentir sobre el alcance—.
>
> **La tercera pata es la que menos se ve y la que más hace falta.** El harness dice de sí mismo que
> **no instancia el bridge desde el CMS**: arma un mock con la forma canónica y prueba los helpers
> consumidores contra él, o sea **se prueba contra sí mismo**. Comprobado renombrando un campo de su
> interfaz y dejando sus otras tres menciones intactas —una incoherencia interna—: **los 56 tests
> siguen verdes**. Sin este cruce, el UI lee `undefined` en producción y nada se pone rojo.

> ⚠️ **Hay DOS carpetas `docs/product/`, y esta nota describía la equivocada.**
> La de la raíz del repo tiene sólo el doc 11. Los demás —**06 a 10, más el 00
> inventario funcional, `inventario/`, `investigacion-dominios/` e
> `investigacion-pagos/`— SÍ están versionados**, en
> `Synergos.CMS.Web/docs/product/`. Ábrelos ahí.
>
> Esta nota decía que no estaban y que había que pedirlos, y el efecto era el
> que pretendía evitar: un agente en un clon limpio los busca en la raíz, no los
> encuentra, y reconstruye desde cero un inventario funcional de 2.269 líneas
> que ya existe. Casi pasa.
>
> Lo que de verdad NO está versionado es `refactor-docs/` y el `MEMORY.md` del
> agente (§5). Eso sí hay que pedirlo.

> **Fuentes que NO viven en este repo.** `refactor-docs/` (status de la
> migración, inventario del legado) y el `MEMORY.md` del agente son locales de
> la máquina del arquitecto y **no están versionados**. Los docs de producto sí
> están — ver la nota de arriba. Un agente que corra en un clon limpio —CI, contenedor, Claude
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
- `feedback_sweep_lease_where_both_see_it` — un barrido que reintenta
  tiene que marcar lo que está ejecutando, y la marca va **donde la ven
  los dos**: en memoria alcanza cuando varios procesos reintentan contra
  UNA capacidad (`retry_in_flight`), y no alcanza cuando cada proceso
  tiene su propio almacén. **Tiene que vencer** —sin vencimiento cambia
  «se hace dos veces» por «no se hace nunca», que no se nota— y **no
  sirve sin releer el almacén al tomarlo**: el caché del proceso deja que
  el segundo repita un minuto después lo que el primero ya hizo.
- `feedback_a_gate_that_parses_source_needs_its_own_mutations` — un gate que
  LEE CÓDIGO con regex tiene puntos ciegos que no se ven midiendo: sale un
  número plausible y nadie lo cruza. G-6 perdía en silencio todo parámetro con
  valor por defecto (`string X = null` daba la clave `null`) y todo parámetro
  con un `=` en su comentario de arriba. Los dos los destapó **medir el gate
  contra un cambio ajeno y desconfiar del resultado**, no leerlo. Se parsea
  sobre la fuente SIN comentarios, y cada corte se prueba contra el caso raro
  del repo, no contra el bonito.
- `feedback_contract_shape_needs_its_own_test` — un test que construye el DTO
  del controller y comprueba sus campos es una TAUTOLOGÍA: afirma lo que el
  controller decidió poner, no lo que el consumidor lee. Lo que hay que
  vigilar es la CLAVE SERIALIZADA, porque el normalizador del otro lado es
  defensivo y una clave que falta degrada en silencio. **Y la mutación no es
  borrar el campo** —eso rompe el build, que no es lo mismo que un test
  rojo—: es renombrar la clave serializada con `JsonPropertyName`, que
  compila y es la forma real de la deriva.
- `feedback_mutate_every_gate` — un gate que no se vio fallar no está
  vigilando nada. Se reintroduce el defecto y se confirma el rojo.
- `feedback_seeded_content_needs_fingerprint` — un seam que sólo sabe CREAR
  convierte una edición en INVISIBLE. Si un catálogo editable siembra su
  contenido en otro almacén, la clave de siembra lleva la huella del texto
  (o se edita y no se ve), y el mapping es DURABLE (o cada arranque duplica
  el feed entero, creciendo para siempre sin que nada falle).
  **Addendum #114 — lo mismo vale para una LLAVE DE IDEMPOTENCIA, y ahí es
  peor.** El script de aprovisionamiento publicaba la tarifa de una oferta con
  la llave `provisionar:precio:{kind}:{id}`, derivada sólo del sujeto; y
  `SetPrice` mira el libro de idempotencia **antes que nada** y devuelve el
  precio anterior. O sea que cambiar el monto en el manifiesto y volver a
  correr el script **no hacía nada**, contestando 200 y diciendo «puesto». La
  idempotencia y la huella no se estorban: **la llave lleva el valor dentro**,
  y así republicar lo mismo sigue siendo un no-op y cambiarlo se ve. La
  pregunta que lo caza: *¿qué pasa si el dato cambia y la llave no?* — y no se
  ve leyendo el script: se vio mirando el almacén de la capacidad viva después
  de arreglar otra cosa.
- `feedback_property_injection_breaks_at_two_impls` — inyectar por propiedad
  resolviendo un TIPO CONCRETO se rompe en silencio el día que hay dos
  implementaciones detrás de la seam: la dependencia aterriza en la que no
  sirve, y los tests siguen verdes porque cablean la inyección ellos mismos.
  Se resuelve por la SEAM, que es lo que el flag decide.
- `feedback_verify_with_live_processes` — los defectos caros salieron
  todos de levantar los procesos y matar uno, no de los tests: los
  tests codificaban la misma suposición equivocada que el código.
- `feedback_identity_failure_is_not_one_answer` — qué hacer cuando la
  identidad falla **no es la misma respuesta en todas partes**, y se
  decide con dos preguntas: ¿el registro se puede rehacer? y ¿su
  ausencia se nota? La bitácora se repite sin firmar (perder un asiento
  es peor que uno débil: un hueco no se ve); la notificación de
  Gobierno y la canasta **fallan a la vista** (un término que empieza a
  correr, y una atribución que nadie audita nunca). Degradar en
  silencio es siempre la peor de las tres.
- `feedback_pii_decision_lives_in_the_seam_type` — **qué dato personal se
  emite se decide en el TIPO del seam, no en el mapeo del DTO.** Si el
  dato llega hasta el borde, dejarlo fuera es una convención, y una
  convención la olvida el campo que alguien añade el mes que viene. Sin
  campo, emitirlo vuelve a ser una decisión. Y el test va sobre el JSON
  serializado entero —no campo por campo—: lo que hay que impedir no es
  que exista una propiedad con cierto nombre, es que el dato salga, y
  puede salir por el identificador, por el nombre o por un campo nuevo.
  El corte de `ICommentReader`/`ICommentWriter` es el mismo movimiento:
  «hace que eso sea una propiedad del tipo y no una convención».
- `feedback_no_read_without_a_write_path` — **una lectura cuyo único
  camino de escritura es un mock no se emite.** Antes de servir una
  colección nueva se mira si algo puede crearla y si algo puede
  atenderla; si las dos acciones del consumidor son locales y no tocan
  la red, lo que se estaría entregando es mobiliario con aspecto de
  bandeja. **Y tampoco se emite `[]`**: la lista vacía dice «no hay»
  cuando la verdad es «esto no existe», y congela la clave en el
  trinquete de G-6 como si cruzara. Se escribe la decisión y el
  disparador en el `record`, que es donde la va a leer la próxima
  auditoría.
  **Addendum #116 — el espejo: una ESCRITURA cuyo único camino de lectura no existe, y por
  qué no la ve nadie.** El expediente de Gobierno guardaba el estado del cobro de la tasa
  desde la ADR 0116 fase 5 y `CaseDetail` —la única proyección que llega a las dos
  bandejas— no declaraba el campo: se escribía a disco y no salía por ningún endpoint. **Lo
  que lo escondía no era la falta de tests, era su REPARTO**: los del motor miraban el
  ALMACÉN («¿quedó escrito que no se pudo cobrar?», y quedaba) y los del borde miraban el
  RBAC y los códigos de estado. Las dos mitades en verde, y el hueco justo en medio —el sitio
  que ningún test miraba porque cada uno creía que era del otro—. **La pregunta que lo caza:
  ¿qué pantalla enseña este campo?** Si la respuesta es «ninguna», el campo no está guardado,
  está enterrado. Y crecía con el cableado: con el motor de pago en proceso el estado era
  siempre `Captured` y daba igual; el día que detrás hay una capacidad que puede caerse, ese
  campo es el ÚNICO rastro de una tasa que nadie cobró. El test que lo cierra es el que cruza
  las dos mitades: radicar con la pasarela caída y leer el borde.
- `feedback_a_fabrication_can_be_a_derivation` — **lo fabricado no siempre es una
  constante: DERIVARLO de lo que hay a mano se lee como un dato y miente igual, y encima se
  defiende solo** («sale de la bandeja, no me lo inventé»). El home del portal del paciente
  llamaba «mensajes sin leer» a la SUMA de los mensajes de cada hilo clínico —los que el
  propio paciente escribió incluidos—, con `IMessagingService` declarando en su contrato que
  no tiene read-receipts. Es el escalón que le faltaba al addendum #111 de
  `feedback_gethashcode_is_not_a_seed`: allí lo fabricado era un `0` y un `true`, que se ven
  raros; una suma de cosas reales no se ve rara nunca.
  **Y la parte que más cuesta: NOMBRAR el defecto en un comentario no lo arregla — lo
  BLINDA.** La HU #111 arregló el `unread` de cada hilo y escribió en su `<remarks>`, con
  todas las letras, que derivarlo de `MessageCount` «es lo que tienta, y lo que hace el
  `unreadMessages` del home». Se quedó ahí una HU entera: la nota convierte el defecto en algo
  *identificado*, y lo identificado la siguiente auditoría lo lee y pasa de largo. Si el
  comentario nombra un gemelo vivo, o se arregla en el mismo commit o se abre el ticket — no
  hay tercera opción que no sea dejarlo escrito y hecho.
  **El fixture tiene que llevar hilos de VARIOS mensajes**: con uno por hilo la suma y el
  conteo de hilos dan el mismo número y la fabricación pasa en verde.
  **Addendum #123 — el blindaje también funciona hacia ARRIBA: arreglar UNA copia y escribir
  el defecto de las otras es la forma más eficaz de dejarlas vivas.** Aquí no fue un gemelo,
  fueron cinco. `UmbracoProductCatalogSource.TryParsePrice` arregló su lado, y su `<remarks>`
  dejó escrito «el código viejo hace `decimal.TryParse(…, InvariantCulture) ? price : 0m`» y
  «verificado en vivo: el Tote salió a 49» — un diagnóstico exacto, con la medición hecha,
  sobre código que seguía corriendo en el carrito, el listado, la ficha, el bloque de línea y
  el `ld+json`. Dos fuentes más lo copiaron *como prosa* («misma trampa que costó dinero en
  Tienda, misma regla») y volvieron a escribir la regla a mano en vez de compartirla, así que
  al final había **tres copias buenas y cinco malas de lo mismo**. La pregunta que lo caza no
  es «¿está identificado?» sino **¿cuántos sitios leen este dato?** — y se contesta con un
  `grep` del ALIAS del campo, que es literal, no del nombre de la función, que cada copia
  elige distinto (`TryParsePrice`, `TryParsePriceFrom`, `ParsePrice`, un `decimal.TryParse`
  suelto en una vista). Y lo que reemplaza a la nota no es otra nota: es un gate que vaya por
  el alias.
- `feedback_a_failed_tryparse_is_not_a_value` — **un `TryParse` que devuelve `false` no
  autoriza a inventar el valor: el respaldo a `0m` convierte «no se pudo leer» en «vale
  cero», y cero es un precio VÁLIDO que no falla en ninguna parte hasta que alguien compra.**
  Es `feedback_an_omitted_key_can_be_an_assertion` aplicado a un parseo en vez de a una clave
  del contrato, y el disparador se reconoce en la firma: **un método de lectura que devuelve
  el tipo del dato en vez de un `bool` no tiene forma de decir «no se sabe»**, así que
  devuelve algo (`ParsePrice(...) → decimal` era exactamente eso). El arreglo es que la
  ausencia sea una propiedad del TIPO —`bool TryX(..., out)` o `decimal?`— y que **cada
  llamador decida qué significa**, porque no es lo mismo en todos: el catálogo omite el
  producto, Eventos y Educación tratan el vacío como gratis, el carrito omite la línea, la
  ficha no pinta ni precio ni botón de comprar, y el bloque editorial muestra el texto crudo.
  **Y hay un caso peor que el cero: el que SÍ parsea.** `"49.000"` con `NumberStyles.Number`
  e `InvariantCulture` da **49**, porque ahí el punto es separador decimal — un precio
  plausible equivocado por 1000× que ninguna guarda de «> 0» ve, y que el propio producto
  fabrica, porque `EsCoPriceFormatter` pinta `"$ 49.000"` en toda la tienda y el editor copia
  de la pantalla al TextBox. Por eso la regla no es «que parsee» sino **que sea inequívoco**:
  solo dígitos ASCII, `NumberStyles.None`.
  **Cómo se prueba, y es la mitad que cuesta**: el fixture tiene que llevar **los dos** casos
  —uno con símbolo (`"$89000"`, que caía a cero) y uno con separador de miles (`"49.000"`,
  que daba 49)—. Con `"89000"` a secas el parser viejo y el nuevo dan el mismo número y el
  defecto pasa en VERDE.
- `feedback_an_omitted_key_can_be_an_assertion` — **una clave que no se emite no
  siempre deja un hueco: cuando el otro lado la resuelve con un valor por DEFECTO, la
  omisión pasa a AFIRMAR ese valor, y lo afirma el borde sin haberlo decidido.** Educación
  no emitía `status` en la fila del instructor y `readCourseStatus` cae a `'published'`:
  la píldora decía «Publicado» de todo curso y el KPI contaba «N publicados / N en total»
  pasara lo que pasara (#102). Es el escalón de arriba de
  `feedback_contract_shape_needs_its_own_test` —allá la clave que falta deja una pantalla
  pobre, acá deja una pantalla que MIENTE— y el primo de
  `feedback_a_derived_fallback_must_never_overwrite_what_arrived`, con el relleno del otro
  lado de la red. **Cómo se decide cuáles son ésas:** se mira el normalizador del
  consumidor, no el DTO — si el fallback de una clave es un valor del vocabulario (y no
  `null`, `''` o `[]`), omitirla es tomar la decisión por él. **Y G-6 no las caza**: cruza
  por CONTROLLER entero, así que `status` ya cruzaba por el acuse de `/confirm` mientras
  faltaba en la consola. **El fixture tiene que llevar el caso que el default NO produce**
  —un borrador y un publicado—: con todos publicados, emitir la clave o no da el mismo
  JSON. **Y la mutación es escribir el default en el mapeo** (`Status: Published`), que es
  literalmente lo que la omisión hacía, sólo que delegado.
- `feedback_a_two_step_write_must_remember_which_step_landed` — **cuando una escritura
  son DOS pasos y el primero mueve dinero, quitar la fabricación del segundo no basta:
  hay que decidir qué hace «reintentar».** `enroll`/`confirm` de Educación inventaban un
  `MOCK-<ts>`/`ENR-<orderRef>` cuando el borde no contestaba, así que el alumno salía con
  un número de matrícula que no existe en ninguna parte **y había pagado** (#117). Es la
  cuarta de la familia de #116 —el `claimId` de la devolución, el `CERT-<random>`, el
  `CITA-<timestamp>`— y lo que añade es que **la mitad barata del arreglo deja la mitad
  cara puesta**: sin tocar el asistente de compra, volver a pulsar llamaba otra vez al
  paso que cobra y abría una segunda orden con su segundo cargo. Tres cortes:
  **lo que el servidor ya se llevó se lee del carrito persistido y no de un campo del
  componente** —entre cobrar y confirmar la página puede recargarse—, y sólo cuenta si el
  monto capturado sigue siendo el total; **el mensaje lo decide lo que QUEDÓ y no lo que
  falló**, porque «no pudimos completar la compra» dicho a quien acaba de pagar es una
  invitación a pagar dos veces; y **el paso que se repite tiene que ser el idempotente**
  —`ConfirmAsync` devuelve la matrícula sin recapturar, `EnrollAsync` abre otra orden—, o
  lo que hay que arreglar es el borde. **Y el hallazgo que no se ve con la red apagada
  entera**: la rama GRATIS activa la matrícula en `EnrollAsync` y **nunca pasa por
  `ConfirmAsync`**, así que el `orderRef` que el cliente se inventaba para ella pedía
  confirmar una orden inexistente —404— y el `catch` devolvía un id fabricado: **contra
  un servidor VIVO**, la matrícula gratis quedaba registrada del lado del UI con un id
  distinto del que este árbol había emitido. El fixture tiene que ser un borde de mentira
  con la forma del de verdad, apagado **por método y ruta**.
- `feedback_a_derived_fallback_must_never_overwrite_what_arrived` — **un campo que se
  DERIVA para los casos en que no llega no puede pisar el que llegó.** La dirección de un
  inmueble se rellenaba con «{barrio}, {ciudad}» y el borde no declaraba `address`, así que
  el binder la tiraba y el respaldo tapaba el hueco: la ficha salía con algo que PARECE una
  dirección y está a tres cuadras (#110). Es la forma más silenciosa de los defectos de
  contrato —los demás dejan un hueco que alguien reporta, y un hueco plausible no—, y por
  eso el orden importa: **lo recibido gana, la derivación es el suelo**. El test lo exige
  con un dato que la derivación NO puede producir: si la calle del fixture se pareciera a
  «{barrio}, {ciudad}», el defecto pasaría en verde.
- `feedback_gethashcode_is_not_a_seed` — **`GetHashCode()` no es una semilla, y un
  comentario que lo llame «determinista» es el aviso de que nadie lo comprobó.** En .NET
  Core el hash de string está **aleatorizado por proceso**: el resumen de salud del EHR
  derivaba de `person.Id.GetHashCode()` el estado de las vacunas y del cuidado preventivo, y
  el tablero del día sacaba de ahí si el paciente había llegado antes, así que «Influenza: al
  día» pasaba a «vencida» en cada reinicio del servidor sin que nadie tocara nada (#106). Es
  la forma de #72 y #82 — la propiedad que el código anuncia como su razón de ser es la que
  no cumple— y **no se ve en una pantalla**: el valor es plausible siempre.
  **Y derivarlo del id de forma de VERDAD determinista tampoco es la salida**: cambia un dato
  que varía al azar por uno que miente siempre igual. El corte es el de la regla 14 del repo
  hermano, aplicado a un campo en vez de a un artefacto: **si el valor entero de un campo es
  ser cierto —un estado de vacunación, el resultado de un tamizaje, un acuse—, no se rellena;
  sin dato, se dice que no hay dato**, y el `record` deja escrito por qué y cuál es el
  disparador. Lo que SÍ se puede emitir es lo que se deriva de datos reales de la persona: la
  recomendación («a los 45 toca tamizaje de colon») sale de la edad y el sexo del padrón; su
  *estado* exige saber si se lo hizo, y eso aquí no lo sabe nadie.
  **Cómo se prueba, porque el proceso no se reinicia dentro de un test**: por ausencia de
  clave (exacto para el defecto tal cual) **y** por invariancia respecto al identificador —N
  sujetos idénticos salvo el id dan la misma respuesta—, que es lo único que caza una
  fabricación derivada del id que no sea el id mismo. Los ids del fixture tienen que ser de
  **longitudes distintas**, o una derivación de `id.Length` pasa en verde.
  **Addendum #111 — cuando lo fabricado es una CONSTANTE, y no una derivación.** La misma regla
  y dos cosas que #106 no tuvo que resolver. Una: **la ausencia tiene que poder distinguirse de
  la afirmación contraria, y eso vive en el TIPO** —`int? Unread` con `null`, no la clave
  quitada—, porque `0` no dice «no sé»: dice «no tienes mensajes sin leer», que es lo que hace
  que nadie abra el mensaje de su médico; igual `false` no es «no consta» para «este médico
  acepta pacientes». La clave se conserva declarada, como el `ImmunizationDto` de #106, para que
  el día que exista el seam recupere su forma sin reinventarla. Dos: **quitar la constante del
  borde NO arregla nada solo, porque el normalizador del consumidor la repone** —
  `readBoolean(value['active'], true)` fabrica el mismo `true` con la clave ausente Y con la
  clave en `null`—, así que el arreglo cruza los dos árboles y hay que decirlo: un campo emitido
  con honestidad y leído con un default sigue mintiendo, y ahora **sin que nada en este repo lo
  señale**. Y la mutación que de verdad prueba el fixture no es sólo devolver la constante: es
  **refabricar desde lo que hay a mano** (`t.Messages.Count`, `s.MessageCount`), así que un
  fixture sin mensajes no distingue «cero» de «no se sabe» y pasa en verde con el defecto puesto.
- `feedback_a_seam_widens_when_it_meets_the_network` — **una costura escrita
  contra implementaciones en proceso no sobrevive a la primera que habla por
  la red, y la salida barata es la trampa.** `IPaymentProvider` nació síncrona
  porque sus dos únicos proveedores escribían en un log; el adaptador real
  dejaba dos caminos y `.Result` dentro del proveedor **vacía el pool de
  hilos**, porque el cerrojo del servicio rodea la llamada entera y cada cobro
  en vuelo se queda con un hilo durante todo el viaje. **No se lee como un
  problema de pagos**: se lee como que «el servicio se puso lento», que es la
  peor pista posible. Se sube la costura, y el `lock` pasa a `SemaphoreSlim`
  —`await` no cabe en un `lock`, y sacar la llamada fuera del cerrojo rompe
  justo la regla que el cerrojo protegía—. Y se cierra con gate: es reversible
  de una línea.
  **Addendum #111 — lo mismo pasa con la GRANULARIDAD, y se ve todavía menos.**
  `IClinicalSchedulingService` sólo sabía listar **por fecha**, así que la ficha del paciente
  barría −30/+60 días llamando una vez **por día** y tirando el 99 % de lo que traía: 91
  llamadas por carga, y la ficha se carga dos veces por visita al portal. No lo tapaba un
  `catch` ni un mock: lo tapaba **el default en memoria**, donde 91 filtros de LINQ no cuestan
  nada — y el comentario que lo justificaba («mantiene ISP en el seam») **sonaba a decisión de
  diseño**, que es la peor forma de esconder un defecto. Con el adapter real ya escrito, medir
  el coste era leer una línea. La pregunta que lo caza: **¿el llamador está barriendo una
  dimensión porque el seam sólo sabe contestar por la otra?** Y el test no mira el resultado
  —no cambia— sino **cuántas veces se pregunta**, que es lo que ningún test miraba.
- `feedback_only_one_layer_may_do_the_irreversible_thing` — **cuando dos capas
  saben hacer lo que no se deshace, un despliegue con las dos vivas tiene que
  FALLAR AL CABLEAR.** No es teoría: es el defecto #57, donde el CMS y el
  orquestador sabían cobrar y el reembolso se le pedía al proveedor que no
  conocía el identificador, así que el caso **no llegaba nunca a reembolsado
  sin que nada fallara**. La comprobación mira si ese lado **cobraría** —las
  credenciales presentes Y algo que pueda elegirlo— y no si el nombre está
  escrito. Y revienta al arrancar, no en la primera petición: uno que arranca
  verde, contesta `/health` y pasa la prueba de humo **parece uno bueno**, y lo
  desmiente la primera persona que intenta pagar.
- `feedback_an_exemption_needs_a_signature_behind_it` — **una ruta exenta de la
  llave compartida sin verificación detrás es un endpoint abierto que
  escribe**, y la exención se escribe en una línea sin tocar ningún endpoint.
  Por eso el gate tiene dos dientes —todo webhook verifica **antes** de tocar
  el almacén, y toda exención tiene un verificador detrás— y mide que la pieza
  esté **ENCHUFADA** y no que exista: `Api.Notifications` quitó la llamada de
  su lambda y no falló ni un test, porque los suyos probaban el verificador.
  **Y la firma no se coteja contra un valor que también manda quien llama**: el
  `checksum` del cuerpo lo escribe el atacante igual que el resto. El fixture
  tiene que exigirlo poniendo **el mismo valor inventado en los dos sitios** —
  con valores distintos, las dos comparaciones rechazan y el test no prueba
  nada.
  **Addendum #14 — cómo se cuela ESTO en un gate nuevo, escrito por alguien que
  acababa de leer esta misma regla.** El gate de identidad de `Api.Payments`
  afirmaba que `PaymentEndpoints.cs` dijera `IdentityAssertions.Resolve` en alguna
  parte. Al mutarlo —cambiando la llamada del lambda por «créele al llamador»—
  **pasó en verde**, porque el helper `Afirmacion()` seguía veinte líneas más abajo
  con esa cadena dentro. O sea: la afirmación medía que el fichero CONTUVIERA la
  pieza, que es literalmente lo que esta regla prohíbe. **Lo que lo hace fácil de
  cometer es que el helper y su llamada viven en el mismo fichero**, así que
  `Contains` sobre el fichero entero se lee como «lo usa». El corte que lo arregla
  es recortar **el cuerpo del endpoint** —de su `MapPost` al siguiente— y buscar
  ahí la LLAMADA; el mismo movimiento que contar `UseStoreWriteGate(` por fichero
  en vez de la mención. Y no se ve leyendo el gate: **sólo lo destapa mutarlo**,
  que es para lo que existe la disciplina.
- `feedback_restored_mutation_needs_a_touch` — **al mutar un gate, la
  restauración tiene que TOCAR el fichero.** Un `cp`/`mv` devuelve el
  contenido con una fecha ANTERIOR a la de la escritura mutada, así que
  MSBuild no reconstruye y la corrida siguiente ejecuta el **binario
  mutado** con el código bueno en disco. El síntoma es desconcertante y
  no apunta a la causa: un test que pasa suelto y falla en la suite
  entera —o al revés—, y una hora buscando un problema de aislamiento
  que no existe. Es el primo del «una mutación cuyo BUILD falló no es una
  mutación» del repo hermano: ahí la mutación nunca se aplicó, acá
  **nunca se quitó**.
- `feedback_a_store_that_rewrites_the_whole_collection_has_no_document_grain` —
  **un almacén cuya unidad de escritura es la COLECCIÓN no protege un documento: protege
  la foto de quien escribió último.** `JsonCollectionStore` guardaba un JSON por colección
  y `Put` lo reescribía entero desde el caché del proceso, así que dos réplicas no se
  pisaban «un pedido»: la segunda **borraba el almacén de la primera**, sin excepción y sin
  log (#112). No se lee en el código como un defecto —se lee como «un diccionario
  persistido»— y **el caché es lo que lo hace invisible**: mientras el proceso vive, todas
  sus lecturas salen de memoria, así que una réplica nunca ve lo que la otra escribió ni
  descubre que se lo comió. Es la misma sombra del #82 y del addendum #111 de
  `feedback_a_seam_widens_when_it_meets_the_network`: lo que se mide mal no es la regla, es
  **la granularidad**. La pregunta que lo caza: *¿cuánto se reescribe para cambiar una
  cosa?* Un fichero por documento no es una optimización — es lo único que hace que dos
  documentos sean independientes, y el árbol del CMS ya lo había aprendido
  (`FileSystemJsonEntityStore`: «1 archivo por entidad»).
- `feedback_a_lock_moves_out_of_the_process_it_does_not_get_replaced` —
  **la exclusión que un proceso resuelve con `lock` no se arregla borrando el `lock`: se
  SUBE a donde los dos la ven, y con ella aparece un estado que hay que nombrar antes.**
  Leer-decidir-escribir son tres pasos con la regla del negocio en medio, así que ningún
  almacén lo puede cerrar solo — por eso #30 subió la suma a dentro de la capacidad y por
  eso acá el cerrojo sube al borde. **Esperar el turno es `Unavailable` y no `Conflict`**,
  igual que `notifications.retry_in_flight`: no es que no se pueda, es que otro está en
  curso, y con `Conflict` un orquestador deshace una saga sana por quince milisegundos de
  cola. Decidir eso **en el primer incidente** es decidirlo mirando un log.
  **Addendum #112 — dos colas, UN presupuesto.** Al subir el cerrojo quedan dos esperas en
  fila —el semáforo de hilos del proceso y el cerrojo entre procesos— y darle a cada una la
  espera configurada deja a quien llama esperando **el doble**. Eso no se lee como «ocupado»,
  se lee como **colgado**: el llamador se va por su propio plazo antes de ver el 503, así que
  el rechazo transitorio —que existe justamente para que se reintente en vez de deshacer una
  saga sana— **no llega nunca**, y el síntoma es «el servicio se puso lento» (el mismo disfraz
  de `feedback_a_seam_widens_when_it_meets_the_network`). El reloj arranca al entrar y las dos
  colas comen del mismo. Y aun con el presupuesto gastado **se intenta el cerrojo una vez**:
  rendirse sin mirar rechaza un turno que puede estar libre. El test que lo caza tiene que
  hacer cola **las dos veces** —dos llamadas a la MISMA instancia con el fichero tomado por
  otra—, porque con instancias distintas sólo se hace cola una vez y el defecto pasa en verde.
- `feedback_a_file_lock_is_released_by_the_kernel_a_lease_is_not` — **elegir entre cerrojo
  y arriendo es elegir quién limpia cuando el dueño se muere.** Un arriendo (`ISagaLease`,
  #34) **tiene que vencer**, porque lo toma un barrido que puede morirse a media
  compensación y nadie lo soltaría — y por eso arrastra el caso feo: la vuelta que tarda
  más que su vencimiento y deja entrar a otro *mientras todavía escribe*. Un cerrojo de
  fichero (`FileShare.None` → `flock`) lo suelta **el núcleo** al morir el proceso: sin
  reloj, sin marca colgada, sin robo. El arriendo sigue siendo lo correcto donde el trabajo
  dura y es reanudable; el cerrojo, donde el trabajo es una petición. Y los dos van **donde
  los dos los ven** (`feedback_sweep_lease_where_both_see_it`): un `Mutex` con nombre NO
  sirve — en Unix .NET lo resuelve en un temporal del usuario, así que dos contenedores
  sobre el mismo volumen tendrían uno cada uno y los dos se creerían dueños.
- `feedback_a_fixture_built_on_a_neighbouring_defect_expires_with_it` — **un test que para
  montar su escenario AFIRMA el síntoma de un defecto que vive en otra pieza se pone rojo el
  día que esa pieza se arregla, y eso no es una regresión: es la prueba de que se arregló.**
  `ArriendoDeCompensacionTests` escribía `Assert.Empty(otro.WithPendingCompensations())` con
  el comentario «no la ve: es la foto vieja» — cierto entonces, y falso desde que el almacén
  relee. Lo que hay que hacer **no es quitar la línea** (deja el fixture sin decir qué
  reproduce) ni relajar el test: es **escribir la verdad nueva y por qué cambió**, y volver a
  preguntarse si el sujeto del test sigue haciendo falta — acá sí, porque el arriendo nunca
  estuvo tapando el caché: tapa que dos barridos miren el disco *en el mismo instante*. Es el
  primo de «un test que codifica el defecto convierte el arreglo en una regresión» (#57), con
  el defecto una pieza más allá.
- `feedback_docs_written_ahead_of_the_code_are_a_defect_with_a_clean_face` — **una sesión que
  se corta deja el árbol en un estado que NINGÚN gate mira: código a medias con documentación
  que ya lo da por hecho.** Acá el `<remarks>` de `StoreWriteGate` afirmaba que
  «`JsonCollectionStore` pasó a un fichero por documento» y `AlmacenPorDocumentoTests` probaba
  ese reparto — con el almacén **sin tocar**, reescribiendo la colección entera desde su caché.
  Es peor que una guía desactualizada: una desactualizada se queda corta y ésta **afirma de
  más**, así que el siguiente agente lee la mitad segura y construye encima. Lo que lo destapa
  no es leer el `<remarks>` —suena bien— sino **correr la suite antes de tocar nada**: los tres
  tests rojos nombraban la pieza que faltaba. De ahí las dos reglas: **la primera orden de un
  trabajo heredado es medir el árbol, no continuarlo**, y ante la incoherencia hay **dos
  salidas honestas y ninguna intermedia** — escribir el código que la prosa promete, o borrar
  la promesa y los tests que la sostienen.
  **Y su forma más fina: una MUTACIÓN que se quedó puesta.** `Api.Inventory` tenía el
  comentario del turno de escritura y **no la llamada** — la sesión anterior la había quitado
  para ver el gate en rojo y se cortó antes de restaurarla. El comentario afirmaba el cableado
  que no estaba, así que `grep` sobre la explicación decía «19 de 19». Es el reverso de
  `feedback_restored_mutation_needs_a_touch`: allá la restauración no llega al binario, acá no
  llega al fichero. **Lo que lo caza es contar la LLAMADA y no la mención**
  (`grep -c 'UseStoreWriteGate('` por fichero, que da `0` en uno de veinte), y por eso el gate
  de cableado se mutó a propósito contra ese mismo hueco antes de darlo por bueno.
- `feedback_a_vertical_is_three_axes_and_only_one_crosses` — **un vertical son
  TRES ejes y sólo el de en medio cruza al otro árbol**, así que «cablear un
  vertical» nunca quiere decir moverlo entero. El **catálogo** —lo que se
  muestra— sale del contenido de Umbraco y cablearlo a `Api.Catalog` es un
  RETROCESO: el dato ya tiene dueño, y meter una ida a la red le quita además
  al editor la superficie donde publica. La **transacción** —lo que se mueve y
  no se deshace solo— es lo único que cruza. El **artefacto** —la entrada con
  su QR, el diploma, el expediente, el RMA— se queda acá, porque el firmante
  vive de este lado y porque una prueba tiene que poder verse con el otro árbol
  caído. Confundir el primero con el segundo es el error caro de la épica; el
  tercero es el que se parte por accidente al cablear.
- `feedback_the_switch_count_tells_the_form` — **el número de INTERRUPTORES
  dice si alguien contestó bien la primera de las tres preguntas**, y se lee del
  disco. En la forma directa va uno **por capacidad** —Gobierno tiene tres
  porque notificar es `Api.Messaging` y decidir es `Api.Workflow`, y juntarlas
  obligaría a encender las dos para probar una—; en la orquestada va uno **por
  flujo**, porque el ORDEN entre los pasos es precisamente lo que el orquestador
  aporta. Así que **dos capacidades colgando de un solo interruptor `Api` es la
  huella de haber mirado «cuántos pasos compone» en vez de «¿hay algo que
  deshacer?»** — el error que §11 documenta cuatro veces, ahora con gate
  (`MoldeDelVerticalTests`). La salida no es siempre «hacelo `Bff`»: si de
  verdad no hay nada que deshacer, son dos capacidades independientes y llevan
  dos interruptores. Ver `docs/product/12-el-molde-de-un-vertical.md`.
- `feedback_bash_ifs_whitespace_shifts_fields` — **un separador que bash
  considera «whitespace» —espacio, TABULADOR, salto de línea— colapsa las
  rachas aunque se pida `IFS=$'\t'`, así que una línea con campos VACÍOS
  corre todos los siguientes.** El aprovisionamiento leía su manifiesto
  separado por tabuladores; una entrada de tipo `precio` no lleva `capacity`
  ni `timeZoneId`, así que sus campos se corrían dos puestos, el monto
  llegaba vacío y un `${monto:-0}` lo publicaba **a cero**: cada oferta de
  viaje, gratis, con la capacidad contestando **201** (#114). Se usa `0x1F`,
  que no es IFS whitespace. **Y el default no es inocente**: un campo de
  dinero ausente se RECHAZA, porque «cero» es un precio válido y no falla en
  ninguna parte hasta que alguien compra — es
  `feedback_an_omitted_key_can_be_an_assertion` en un script de shell.
  **Cómo se prueba, y es la mitad que cuesta**: el fixture tiene que llevar
  la entrada CON el hueco; con todos los campos llenos, el tabulador y el
  `0x1F` dan el mismo resultado y la prueba pasa en verde con el defecto
  puesto. Y se prueba **ejecutando** el lector —el script trae
  `--autoprueba` y el gate lo corre—, no leyéndolo: una regex sobre el
  `IFS=` no sabe cuáles de los siete campos pueden venir vacíos.
- `feedback_a_seam_that_a_view_bypasses_is_not_a_seam` — **una costura que existe y
  que la VISTA se salta no está protegiendo nada, y lo que se salta se ve en
  producción y no en la máquina de quien lo escribió.** El `<script
  type="importmap">` lo armaba un Razor leyendo un fichero del disco, teniendo
  `IBundleRegistryClient` al lado; en el contenedor ese directorio no está montado,
  así que no salía mapa y **ningún `<synergos-*>` se registraba**, con la página en
  200 y el SSR entero (#126). **Tres cosas que no se deducen leyendo el código:**
  (a) **el reparto de los tests es lo que lo escondía** —los del cliente miraban el
  cliente, los del cableado miraban el cableado, y nadie miraba **el HTML que
  recibe el navegador**: las dos mitades en verde y el hueco justo en medio, como
  el addendum #116 de `feedback_no_read_without_a_write_path`—; (b) **las vistas de
  este repo NO se compilan en el build** (`ModelsMode=InMemoryAuto` fuerza
  `RazorCompileOnBuild=false`), así que una suite verde no dice **nada** sobre lo
  que un Razor hace, y por eso lo que vigila la vista tiene que leer su FUENTE, con
  los comentarios quitados —este gate y la vista nombran `File.ReadAllText` para
  contar qué pasó, y sin quitarlos el gate se engaña con su propia explicación—;
  (c) **el desarrollo lo tapaba**, porque el directorio sí existe en la máquina del
  arquitecto. La pregunta que lo caza: *¿qué hay en esta vista que sólo funcione
  donde la escribieron?* **Y el gate no enumeró los layouts: derivó cuáles pintan
  Block Grid**, y por eso encontró **dos** que nadie había nombrado —`PageBare` y
  `Error`, los dos con `Layout = null`, o sea sin heredar el `<head>` que traía el
  mapa—. Con la lista a mano de los dos que ya se sabían, los otros dos seguirían
  ahí.
- `feedback_a_gate_runs_when_what_it_READS_changes` — **un gate que no se dispara en el
  cambio que lo necesita no falla: se SALTA, y en la lista de checks un «skipped» se lee
  igual que un «no aplica».** `design-gates.yml` corre G-6 y G-7 —los dos que cruzan
  `Synergos.CMS.Web/Controllers/` contra el repo hermano— y su filtro `paths:` **no
  nombraba esa carpeta**, así que un PR que tocaba sólo un controller no disparaba ninguno
  de los dos (#128). Los siete defectos de contrato de #102–#110 vivían todos en un
  controller, y G-7 se escribió como **error y no trinquete** por lo caros que fueron: un
  gate con esa severidad que no corre es peor que no tenerlo, porque da sensación de
  cobertura. **Ningún test lo habría visto de forma natural**: el filtro y el script viven
  en ficheros y lenguajes distintos, y nada cruzaba «qué lee» contra «cuándo corre» — la
  forma de `feedback_a_generator_branch_that_returns_early_swallows_the_shared_tail`, con
  las dos piezas bien escritas y el REPARTO mal. **Añadir la ruta arregla hoy; derivarla
  arregla la próxima**, y por eso el gate parsea los `join(…, 'Synergos.CMS.Web', …)` de
  cada script —sobre la fuente SIN comentarios, que si no mide la prosa— y cruza **en los
  dos sentidos**: una carpeta leída y no nombrada rompe el build, y una nombrada que nadie
  lee también, porque una ruta que sobra convierte el filtro en ruido.
  **Y el segundo diente destapó su propio punto ciego al primer intento**: marcó
  `uSync/**` como ruta muerta porque quien la lee es `validate-cms-contracts.mjs`, que vive
  en el repo HERMANO y el gate se saltaba. La salida fácil era quitar ese diente; la
  honesta fue declarar qué lee cada script remoto **con su razón al lado**, y escribir el
  riesgo que eso deja —si el hermano deja de leer uSync, esto se queda diciendo que sí—
  en vez de insinuar que el cruce es completo.
- `feedback_a_named_list_beats_a_count` — **cuando una frase de la guía dice
  «el CMS habla con N capacidades» y las NOMBRA, el gate tiene que derivar
  la LISTA, no la cifra.** Un gate que cuadre sólo el número se conforma con
  una lista incompleta, y la lista es lo que alguien lee para saber qué ya
  existe antes de proponerlo de cero — que es exactamente el daño que esa
  frase causó diciendo «UNA» durante once HU. Se deriva **por ruta**: un
  cliente HTTP no nombra a su capacidad —su URL llega por configuración— así
  que lo único que la identifica es qué `/v1/…` pide, cruzado contra lo que
  declara cada `Endpoints/`. Dos cortes que hacen falta: se exige que el
  cliente esté **CABLEADO** (una clase que ningún composer registra no es una
  conexión, es código que nadie ejecuta), y **una ruta que declaran dos
  capacidades no cuenta para ninguna** — `/v1/holds` es de `Api.Booking` y de
  `Api.Inventory`, y contarla daría por conectada una a la que nadie habla.
  Al escribirlo, la frase decía siete y eran **nueve**.
- `feedback_an_orchestrator_cites_a_record_it_does_not_relay_a_credential` — **una
  credencial no cruza un orquestador: lo que cruza es la CITA de un registro que otra
  capacidad ya verificó.** Propagar el `X-Synergos-Identity` del CMS a través de un `Bff.*`
  suena a la salida barata y tiene la forma equivocada, porque **un token es una credencial
  con RELOJ y una saga es trabajo con DURACIÓN**. Los tres síntomas, que conviene saber
  reconocer en cualquier otro sitio donde alguien quiera reenviar un bearer: (a) **vence a
  media saga**, y lo que compensa —un barrido, horas después, sin nadie al teclado— llegaría
  sin firmar mientras el paso que no movió nada llegó firmado, o sea la afirmación fuerte
  donde no hace falta y ninguna donde la plata vuelve; (b) **para que sobreviva hay que
  GUARDARLO**, y ahí el almacén de sagas —que se respalda— pasa a ser un llavero, que es la
  misma forma que el repo ya rechazó para el `.env` del servidor; y (c) **el sujeto no
  cuadra**, porque los pasos de una saga nombran a sujetos distintos y un token sólo prueba
  uno, así que lo propagado acabaría siendo identidad **para un campo** con aspecto de estar
  resuelto. La salida es la que `Bff.Tienda` ya tenía sin que nadie la hubiera nombrado:
  **leer al comprador del dueño de la canasta** en vez de aceptarlo en el cuerpo. Un registro
  no vence, no es secreto y lo relee cualquiera. **Y el corolario que decide trabajo:** una
  capacidad a la que sólo se llega por orquestador **no lleva puerta de identidad** — sería
  un campo que nadie puede llenar, o sea `feedback_no_read_without_a_write_path` por el lado
  de la escritura. **Dónde se revierte esto en silencio, y por eso el gate mira ahí:** no en
  el cable —añadir una cabecera se ve— sino en el `record` de la saga, donde guardar el token
  se lee como añadir un campo. El disparador para reabrirlo: que una capacidad detrás de un
  orquestador necesite saber **CÓMO** se identificó la persona y no sólo quién es; y ni
  siquiera entonces se propaga — se cita lo que el otro registro dice que fue
  (`Cart.OpenedWith`), guardado como afirmación **de segunda mano**.
- `feedback_a_generator_branch_that_returns_early_swallows_the_shared_tail` — **un
  generador con un `return` por caso especial se come, en silencio, todo lo que el caso
  general reparte después — y lo que se pierde no falla: queda MAL CONFIGURADO.**
  `tools/compose-gen.mjs` reparte la llave de verificación de identidad en su fallback, al
  final y a propósito (puesto arriba se comía el bloque de `Api.Workflow`, y eso ya estaba
  escrito). Al cablearle identidad a `Api.Payments` (HU #14) resultó que **tiene bloque
  propio** —el de Wompi— cuyo `return` sale antes, así que el compose la dejaba sin
  `IdentityTokens__Keys`: un servidor bien configurado arrancando verde, contestando
  `/health` y **rechazando el primer token que le presenten**, que es la forma de fallo que
  este repo ya pagó en el defecto #83 y con la llave de firma de `Api.Identity`. El comentario
  del fichero avisaba de la mitad —«no lo pongas arriba»— y no de la otra: **el reverso muerde
  igual, y muerde al que AÑADE una capacidad al grupo**, no al que toca el generador. Por eso
  los bloques propios de quien verifica **concatenan** la cola compartida en vez de devolver a
  secas. **Y lo que lo cazó no fue leer el generador: fue que el gate DERIVA la misma lista
  del disco, por su cuenta.** Dos derivaciones independientes de la misma verdad que tienen
  que coincidir es lo único que distingue «el generador lo reparte» de «el generador cree que
  lo reparte» — un gate que leyera el generador habría confirmado el defecto.
- `feedback_a_state_with_no_writer_cannot_be_tested_through_the_seam` — **un valor del
  vocabulario al que NINGÚN camino del código llega no se prueba: se anota.** Al cortar la
  matrícula duplicada (#121) la regla correcta era «sólo una matrícula ACTIVA bloquea», que
  deja fuera a `Cancelled` por la lección del defecto #41 —encontrar un registro no significa
  «esto ya pasó», y quien se dio de baja tiene que poder volver—. Pero **nada en el repo pone
  una matrícula en `Cancelled`**: `IEnrollmentService` no tiene `CancelAsync` y ningún código
  asigna ese estado. Es el **espejo** de `feedback_no_read_without_a_write_path`: allá una
  escritura sin camino de lectura, acá un **estado sin camino de entrada**.
  **Lo que NO se hace es fabricar el estado con un doble para tener el test en verde**: eso
  prueba el doble y no la regla, y deja un test que afirma que el sistema hace algo que no
  puede hacer —la forma de la regla 10 del repo hermano, un fixture que describe un servidor
  que no existe—. Se escribe la rama correcta, se prueba **la mitad alcanzable** (acá: que una
  pendiente de pago tampoco encierre) y el hueco queda nombrado en el `<remarks>` del test,
  que es donde lo va a leer quien añada `CancelAsync`.
  **Cómo se caza, y es una línea**: `grep` del valor del enum por el árbol sin los tests. Si
  sólo aparece en su propia declaración, nadie lo escribe nunca.
- `feedback_the_same_algorithm_is_not_the_same_thing` — **lo que decide si dos trozos
  de código son el mismo son el SUJETO y la POLÍTICA, no el algoritmo**, y por eso una
  promoción se mide leyendo los seis sitios y no contándolos. El seudónimo de una persona
  estaba escrito **seis** veces con tres nombres —`Seudonimo`, `BuyerId`, `TravellerId`— y
  en `Web/Services/` el MISMO `SHA256` truncado sirve además para otras tres cosas: la
  huella del cuerpo de un acto administrativo, una llave de idempotencia y la firma de un
  webhook. Tragárselas todas en un helper llamado «seudónimo» habría sido **peor que las
  seis copias**: el día que una necesite cambiar, cambian las otras (#120).
  **Y lo que de verdad arregla la promoción no es la duplicación: son las VARIACIONES.**
  Dos de las seis no hacían lo mismo —la bitácora devuelve el actor del sistema sin correo,
  la tienda prefiere el `MemberKey`— y eso **no se ve leyendo una copia**: la séptima que
  alguien escriba copia la que tenga más cerca y hereda o pierde una variación sin saberlo.
  **Las variaciones NO se meten dentro del helper**, que haría un helper con banderas y
  escondería las políticas donde nadie las lee: el helper hace una cosa y cada política se
  queda en su sitio.
  **Y hay un efecto secundario que hay que esperar: se pondrán rojos los gates que miraban
  la IMPLEMENTACIÓN donde vivía.** Dos lo hicieron —`AuditWiringTests` y `ViajesWiringTests`
  pedían `SHA256.HashData` dentro de *su* fichero— y **eso no es una regresión**: es un gate
  siguiendo a un fichero en vez de a una propiedad, y se arregla haciéndolo seguir la
  propiedad (que el consumidor no lo calcule, y que quien lo calcula no use `GetHashCode`).
  Es el primo de `feedback_a_fixture_built_on_a_neighbouring_defect_expires_with_it`.
  **Y al escribir el gate nuevo, su primera versión pasó en VERDE con el defecto puesto**:
  el regex `ToHexString\([^)]*\)\[\.\.16\]` no cruza paréntesis anidados, así que veía el
  caso bonito —`ToHexString(hash)[..16])`, el único que NO había que vigilar— y no la forma
  que tenían cuatro de las seis copias. Lo destapó **mutar con el caso feo del repo y no con
  el bonito**, que es lo que ya decía `feedback_a_gate_that_parses_source_needs_its_own_mutations`.
- `feedback_every_authored_field_needs_a_reader` — **un DocType y la fuente que
  lo lee se cruzan en las DOS direcciones, y cada una tapa un defecto
  distinto** (#118). Un campo que el schema declara y la fuente no lee es
  MOBILIARIO: el editor escribe el teléfono del consultorio, guarda, publica, y
  no sale en ningún lado — sin error y sin log, porque nadie preguntó por él. Un
  alias que la fuente lee y el schema no declara es peor y se ve todavía menos:
  `IPublishedContent.Value<T>("noExiste")` **no lanza**, devuelve el default del
  tipo, así que un profesional entero sale sin especialidad y sin horario y la
  ficha se ve «vacía» en vez de «rota». Es la forma de
  `feedback_contract_shape_needs_its_own_test` un escalón antes del JSON: el
  contrato de aquí es entre el editor y el código, y el eslabón —el nombre del
  alias, escrito dos veces— no lo comprueba ningún compilador. **Se cruza por
  nombre de alias y sólo los del prefijo del vertical**: los heredados de las
  compositions no están en `GenericProperties`, y la fuente también lee campos
  del siteRoot. **Y lleva red de seguridad**: si uno de los dos
  descubrimientos deja de ver, las dos listas salen vacías y el cruce pasa en
  verde sin mirar nada.
- **Addendum a `feedback_an_omitted_key_can_be_an_assertion`: un
  `Umbraco.TrueFalse` es un default escrito en el SCHEMA** (#118). Allá la
  decisión la tomaba el normalizador del consumidor; acá la toma el editor de
  Umbraco, que guarda `false` para todo nodo que nadie tocó. «Este médico no
  admite pacientes nuevos», afirmado sobre los cien profesionales que el editor
  aún no revisó, manda a alguien a buscar consulta a otra parte teniendo una
  disponible — y es exactamente el campo que la HU #111 sacó del borde por
  fabricarlo. **Cuando la ausencia tiene que poder distinguirse de la
  afirmación contraria, el campo NO es un booleano**: es un desplegable de dos
  valores donde «sin elegir» sigue siendo «no consta», que es la misma decisión
  que #111 tomó en el TIPO (`bool?`) aplicada al editor. Y la regla para saber
  cuáles son ésas es la de siempre: se mira qué pasa si nadie lo toca.
- `feedback_the_two_trees_connect_only_where_someone_asks_for_the_page` —
  **el CMS y el CDN se pueden tener los dos verdes y no estar conectados, y el
  HTML de una página desconectada es indistinguible del de una buena.** Sin
  `<script type="importmap">` el navegador no resuelve un solo bare import: ningún
  bundle arranca, ningún `customElements.define` ocurre, y la página sale **200 con
  el SSR entero** —el contenido pintado, los `<synergos-*>` en su sitio— y muerta.
  Es el defecto #126, y lo que lo vuelve regla es **por dónde se llega**: no por un
  fallo, sino por una **variable de entorno con el nombre de otra capa**.
  `SYNERGOS_CDN_MODE` / `SYNERGOS_CDN_URL` sólo existen dentro de `compose.yml`,
  que las traduce a `Synergos__BundleRegistry__*`; un `dotnet run` las ignora **sin
  quejarse**. Medido con la misma portada, cambiando sólo el nombre: **21.595 bytes
  con un import map de 23 entradas contra 19.785 sin import map**. El repo hermano
  las documentaba así en dos ficheros, o sea que la guía era la causa.
  **Y la mitad que no se ve: un mapa INCOMPLETO es peor que uno ausente.** Con dos
  frameworks publicando y el mapa de uno, la mitad de los elementos hidrata y la
  otra mitad no — nadie reporta una página que «se ve bien». Por eso el gate
  (`tools/humo-conectado.mjs`) deriva del registry **cuántos** frameworks hay y
  exige una entrada por cada uno, en vez de comprobar que el mapa exista.
  **El tell, para reconocerlo sin este caso delante:** un gate que se detiene en
  «la página se sirve». Servir es la mitad; la otra es que traiga con qué
  ejecutarse, y eso sólo se ve pidiendo la página con el otro árbol enchufado.
- `feedback_a_build_that_compiles_no_view` — **en este repo un `dotnet build`
  verde NO dice que las vistas compilen, y ningún test de la suite lo dice
  tampoco.** `RazorCompileOnBuild=false` y `RazorCompileOnPublish=false` están
  puestos a propósito (`ModelsMode=InMemoryAuto`), así que **toda vista se
  compila en caliente, en todos los entornos** — y esa compilación NO lleva los
  implicit usings que el SDK Web le da al proyecto. Una vista que usa un método
  de extensión sin su `@using` compila en el build y **revienta al servirla**:
  el arreglo del defecto #92 dejó `_SynergosBridge.cshtml` así y **ninguna
  página de contenido renderizó durante diez días**, con la suite entera en verde.
  **Lo que lo escondía era otro hueco**: sin portada publicada, `/` servía el
  cartel de Umbraco vacío y `_Layout` no se invocaba nunca — el hueco de abajo
  tapaba el de arriba, así que cerrar uno es la forma de encontrar el otro. Lo
  único que lo caza es **pedir la página** (`tools/humo-portada.mjs`), y por eso
  ese gate existe aunque tarde tres minutos.
- `feedback_a_seeder_is_idempotent_when_it_refuses_to_overwrite` — **para una
  herramienta que siembra lo que luego se edita, «idempotente» no es «no
  duplica»: es «no PISA».** Sembrar dos veces creando un segundo nodo es
  molesto y se ve; sembrar encima de lo que el arquitecto acaba de ajustar en el
  backoffice —justo antes de exportarlo— se lleva el trabajo **en silencio**. Y
  el test que lo exige no puede comprobar «no se creó otro»: tiene que comprobar
  que **no se llamó a guardar**, porque un seeder que reescribe lo mismo pasa el
  primer criterio y falla el que importa. El corolario del mismo tamaño: lo que
  la herramienta rellena, lo rellena **sólo si está vacío**.
- `feedback_a_dev_tool_that_answers_200_with_success_false_is_broken_forever` —
  **una herramienta que reporta su fallo dentro de un 200 lleva rota desde el día
  que se escribió y nadie lo sabe.** `POST /dev/seed-synergos-identity` contestaba
  `{"success":false,"detail":"platform-save-failed:FailedPublishContentInvalid"}`
  con código **200** —no ponía `brandKey`/`brandDisplayName`, obligatorias de
  `compBranding`, en el `platformRoot`— y encima **no estaba nombrada en ningún
  documento**, así que nadie tenía por qué correrla y descubrirlo. Las dos mitades
  hacen falta: un fallo sale con 4xx, **y** el documento que manda correr un
  endpoint se cruza contra el controller que lo declara (`PortadaDeArranqueTests`).
  Un camino escrito que nadie cruza contra el código es una hora perdida para
  quien lo siga.
- `feedback_a_rejection_named_after_a_field_expires_at_the_second_field` — **un
  código de rechazo que nombra un CAMPO caduca el día que aparece el segundo, y
  el que caduca no falla: MIENTE.** `GET /v1/reservations` exigía `resourceId` y
  rechazaba con `booking.resource_id_required` — el instinto correcto («sin
  filtro esto es un volcado del almacén») dicho sobre el único filtro que
  existía. Al añadir el filtro por actor (#124) ese nombre pasa a afirmar que
  hace falta `resourceId` cuando un `for` también sirve, y **un código de
  rechazo es contrato**: es lo que un orquestador compara y lo que alguien
  escribe en un `if`. La regla es la misma que
  `feedback_an_omitted_key_can_be_an_assertion` un escalón más arriba —allá
  afirmaba un valor, acá afirma cuál es el remedio—, y la salida es nombrar **la
  regla y no el campo**: `filter_required`. La pregunta que lo caza, y se hace
  al AÑADIR y no al escribir: *¿este código sigue siendo verdad con el campo que
  acabo de meter?* **Y no lo ve ningún gate**: `ApiMoldTests` cuenta los códigos
  literales del árbol —la cifra se movió de 241 a 242 y eso fue todo lo que se
  puso rojo—, así que un código que miente cuenta igual que uno que no.
- `feedback_an_empty_list_is_honest_when_something_could_have_filled_it` —
  **la respuesta a «no hay nada» depende de QUIÉN generó el identificador por el
  que preguntan, y es el corte que decide entre `not_found` y `[]`.** Un
  `resourceId` lo generó la capacidad, así que preguntar por uno que no existe es
  un error del llamador y vale la pena nombrarlo; un `Ref` es vocabulario de
  QUIEN LLAMA y la capacidad **no puede** saber si existe —comprobarlo exigiría
  interpretarlo, que es §0.B.13—, así que «esta persona no tiene reservas» es la
  respuesta verdadera y la lista vacía es honesta. Devolver `not_found` obligaría
  a cada portal a tratar una bandeja vacía como un fallo, que es como se acaba
  enseñando un error a quien simplemente todavía no reservó.
  **Y esto NO contradice el «tampoco se emite `[]`» de
  `feedback_no_read_without_a_write_path`: lo AFILA.** Lo que distingue los dos
  casos es si existe un camino de ESCRITURA. Allá no lo había, así que `[]` decía
  «no hay» cuando la verdad era «esto no existe»; acá confirmar un hold crea
  exactamente esta fila, así que `[]` dice «todavía ninguna», que es cierto. La
  pregunta es la misma de siempre —*¿qué puede llenar esta lista?*— y sólo cambia
  la respuesta.
- `feedback_a_clock_test_measures_a_property_it_cannot_own` — **un test que
  comprueba una propiedad TEMPORAL con un reloj de pared no falla por el código
  que vigila: falla por la máquina, y eso enseña a ignorar un rojo.** El de «las
  dos colas del turno de escritura comen del mismo presupuesto» afirmaba «esperó
  menos de 1800 ms con 1000 de presupuesto» —un margen de 1,8×—, se cayó en una
  corrida de la suite entera y pasó suelto y en la siguiente (#125). Este repo ya
  tiene escrito que **«flake» no es una causa raíz**, y el precio es exacto: el
  día que ese test se caiga **por el defecto**, nadie lo va a mirar.
  **La salida NO es subir el margen** —eso lo convierte en un test que no prueba
  nada— ni tampoco «medir el número de esperas», que fue el instinto: acá las dos
  esperas OCURREN las dos y son legítimas; lo que el defecto hacía era
  **reiniciar el reloj** entre ellas. El corte que sí funciona es **convertir el
  plazo en un TIPO** —creado una vez al entrar, con `Restante(ahora)` y
  `Agotado(ahora)` puros— y **dejar que el llamador lo pase**, porque entonces un
  test puede entrar con un presupuesto **ya gastado** y afirmar lo que importa
  sin depender de cuánto tarde la máquina.
  **Y el reloj no desaparece del todo: cambia de MARGEN, y ahí está la mitad que
  cuesta.** Queda un test que cronometra —hace falta, porque la propiedad es
  temporal— pero con el presupuesto gastado bueno y roto se llevan **10×** en vez
  de 1,8×: medido, 5005 ms contra un margen de 500. Un margen de 25× contra el
  caso bueno no es «subir el margen»: es haber cambiado el escenario para que la
  diferencia entre correcto y roto no quepa dentro del ruido.
  **Lo que no se puede perder al hacerlo**: la mutación original —recalcular el
  límite después del semáforo— tiene que seguir poniéndolo rojo. Un test
  determinista que ya no caza el defecto es peor que uno flaky.
  **Y hace falta el test espejo**: con el presupuesto gastado y el cerrojo
  **libre**, tiene que ENTRAR. Sin él, un turno que se rindiera sin intentar el
  fichero pasaría el primero en verde — y rendirse sin mirar rechaza con un 503
  un cerrojo que el de al lado acaba de soltar.
- `feedback_an_omission_becomes_a_defect_when_someone_fills_it` — **omitir un campo
  que la otra punta emite es legítimo hasta que alguien tiene que RELLENAR el hueco;
  ahí deja de ser un DTO mínimo y pasa a ser una fabricación** (#122). El
  `QuoteDto` de `Bff.Tienda` no declaraba `Lines`, así que `System.Text.Json`
  descartaba en silencio los `unitPrice` que `Api.Pricing` ya había mandado, y
  `PurchaseFlow` escribía `Money.Zero` encima — sobre el campo que `Api.Orders`
  **congela** a propósito, porque «un pedido es un acuerdo sobre un monto». El
  pedido quedaba guardado diciendo que cada renglón valía nada.
  **Lo que lo hacía invisible es que el TOTAL seguía siendo el bueno**: ninguna regla
  de la capacidad lo rechaza —`CheckTotal` sólo mira signo y moneda, y con razón,
  porque el impuesto lo pone Pricing—, así que se contestaba **201** y el descuadre
  sólo aparecía leyendo la factura. Verificado con seis procesos vivos: con el
  defecto puesto, el mismo almacén guarda `[('camisa',0),('gorra',0)]` con total
  306 425 y nada falla.
  **Los 33 campos que ese mismo fichero omite a conciencia son la prueba de que la
  omisión NO es el criterio** — y de por qué no hay gate de cruce BFF↔capacidad; el
  argumento medido está en §7. **La pregunta que sí lo caza se hace leyendo:
  ¿este valor lo estoy inventando porque el DTO de al lado no lo trae?** Si la
  respuesta es sí, el campo va al DTO; si de verdad no viene, se **rechaza**, no se
  rellena con cero.
  **Y el fixture tiene que llevar VARIAS líneas con precios distintos entre sí y
  distintos del total**: con una sola, el unitario y el total coinciden y el defecto
  pasa en verde; con cantidades iguales, no se distingue «leí el unitario» de «leí el
  subtotal». **La mutación que vale no es quitar el campo** —eso rompe el build, que
  no es un test rojo—: es renombrar la clave serializada con `JsonPropertyName`, que
  compila y es la forma real de la deriva.
- `feedback_a_default_path_of_one_machine_is_half_the_defect` — **una ruta por defecto
  que sólo existe en la máquina de quien la escribió es la mitad barata; la cara es que
  el modo SIGA CORRIENDO contra ella.** `appsettings.Development.json` traía
  `C:\LOCAL_CDN` en los dos sitios del CDN de disco y `appsettings.Docker.json`
  declaraba `Mode=FileSystem` contra un `/cdn` que la imagen no monta; en las dos, un
  arranque fuera de esa máquina servía la portada en **200 con el SSR entero, sin
  import map y con cero `<script type="module">`** — el defecto #126 exacto, en el modo
  que se usa para desarrollar (#132). Medido con la misma portada, cambiando sólo la
  ruta: **21 577 bytes / 23 entradas / 2 scripts** contra **19 785 / ninguna / 0**, los
  mismos dos números que `humo-conectado` midió para el nombre de variable equivocado.
  **Y no era transitorio: era permanente.** `InitialLoad` ve la carpeta ausente, escribe
  un `LogWarning` y **vuelve sin cargar nada y sin dejar vigilante** —el
  `FileSystemWatcher` sólo se engancha si la carpeta estaba—, así que el estado del
  disco EN EL ARRANQUE decide para siempre. Cuando el mundo decide al arrancar, se dice
  al arrancar: el modo lanza, como `ExigirUrlPublicaAbsoluta` en `Http` (#56).
  **Lo que reemplaza a la ruta de una máquina es una RELATIVA AL REPO, y hay que decir
  contra qué se resuelve**: contra la raíz de **contenido**, nunca contra el directorio
  de trabajo —`dotnet run` lo deja en el proyecto y `dotnet exec bin/…/Web.dll` donde se
  tecleó—, o es el mismo defecto con otra cara. Y se resuelve **antes de enlazar
  opciones**, porque lo leen dos: el composer para decidir si lanzar, y el cliente por
  `IOptions`; resuelto en uno, el otro habla de otra carpeta.
  **Dos cortes que costaron su mutación.** Uno: **no se rechaza toda ruta absoluta** —el
  `/cdn` de la imagen es un contrato, no una máquina—; lo que las distingue es que
  nombren un usuario o una unidad. Dos: **lanzar sin decir cómo salir es un peaje**, así
  que el mensaje nombra las dos salidas (construir el hermano, o `Mode=Stub`), y por eso
  se distingue «la carpeta no está» de «está y no trae el registry»: el remedio es
  distinto. **Ningún test lo veía** porque ninguno lee los `appsettings` — y los del
  probe usaban `C:\LOCAL_CDN` **como literal de fixture**, o sea el defecto escrito en
  los tests como si fuera lo normal. Hay gate (`RaizDelCdnTests`), y es el gemelo del
  `rutas-hermanas` del repo hermano, que ya vigilaba esto de su lado.

- `feedback_a_key_in_the_wrong_section_is_a_key_nobody_reads` — **el binder de
  .NET descarta en SILENCIO lo que no mapea, así que una clave de configuración
  en la sección equivocada no falla: deja al servidor comportándose como uno sin
  configurar.** `appsettings.Development.json` y `appsettings.Docker.json`
  declaraban `Umbraco:CMS:Global:UmbracoApplicationUrl`, y esa clave vive en
  `WebRouting` —`Global` tiene dieciséis propiedades y ninguna es ésa—, así que
  el `KeepAliveJob` escribía «No umbracoApplicationUrl for service (yet), skip»
  **cada sesenta segundos** y nadie lo relacionaba con una clave que estaba, con
  su valor correcto, a la vista en el fichero (#138). Lo mismo con
  `Umbraco:CMS:RuntimeMode`, que va en `Runtime:Mode`.
  Es la familia del compose de `Api.Identity` —«nombraba `Identity__Tokens__*` de
  cuando la sección era propia»— y el disparador es el mismo: **una sección que se
  renombró o se partió en una versión anterior**.
  **Lo que lo escondía es de quién es la señal.** Esos ficheros declaran su
  `$schema`, y el schema **sí** sabe dónde va cada clave — pero lo consume el IDE
  para autocompletar, y **un IDE no falla un build**. Es #133 otra vez: un
  artefacto que mira una herramienta que no usamos para verificar, así que su
  deriva no aparece en ninguna corrida. La pregunta que lo caza es la de #133
  —*¿qué herramienta mira este fichero que nosotros no usamos nunca?*— y la
  respuesta trae el regalo: **cuando el vendedor publica el schema, no hay lista
  que escribir**; las 245 propiedades y su sitio ya están en el repo.
  **Y el corte que costó su mutación: se cruza por RUTA, no por NOMBRE.** Medido
  con el defecto puesto: un cruce que recolecte los 245 nombres y compruebe
  pertenencia **caza `RuntimeMode`** —ese nombre literal no existe, la propiedad
  se llama `Mode`— y **pierde `UmbracoApplicationUrl`**, que SÍ es una propiedad
  que el schema conoce, sólo que de otro padre. O sea que el gate barato habría
  cazado el inocuo y dejado pasar el que tenía el síntoma vivo. Hay que resolver
  los `$ref` y descender el árbol a la par que el JSON, con red de seguridad
  —«se cruzaron menos de N claves»— porque el schema envuelve casi todo y un
  descenso que no los resuelva se para en el primer nivel y **pasa en verde sin
  mirar nada**.
  **Y el mensaje dice DÓNDE sí vive la clave**, que cuesta lo mismo de calcular y
  ahorra la búsqueda: un rojo que sólo dice «no existe» manda a alguien a leer un
  schema de 245 propiedades.
  **Addendum #159 — el gate arregló lo que sabía LEER, y la misma clave siguió mal en los dos
  compose.** `ClavesDeUmbracoTests` enumera `appsettings*.json` y nada más; una variable
  `Umbraco__CMS__…` del bloque `environment:` de un compose es **la misma configuración por otro
  transporte**, así que `compose.prod.yml` y `docker-compose.yml` siguieron escribiendo
  `Global__UmbracoApplicationUrl` cinco tickets más. En producción la URL pública se descartaba y
  quedaba la del perfil —`http://localhost:8080/`—, con el `KeepAliveJob` diciendo su «skip» cada
  sesenta segundos: el síntoma que el #138 documentó, **vivo, en el artefacto que su gate no
  miraba**. Y en el compose de LAN el comentario de arriba de la línea describía exactamente el
  daño que la línea no evitaba. Es `feedback_a_severity_travels_further_than_its_exemption` con el
  sujeto cambiado —allá una severidad de compilación viaja a artefactos que el build no compila,
  acá una clave de configuración viaja a artefactos que el gate no parsea— y la pregunta se hace
  **al escribir el gate**: *¿por qué OTROS transportes llega esta misma configuración?* Se contesta
  con un `grep` del prefijo de la sección por el árbol entero, no sólo por la extensión que el gate
  ya lee. El criterio **no se duplica**: el cruce por ruta se expuso una vez
  (`PorQueNoLaDeclara`) y lo consumen los dos gates.
  **Y el «por RUTA y no por NOMBRE» quedó medido dos veces**: con el defecto puesto y el cruce
  hecho por pertenencia del nombre, el gate nuevo pasa en **VERDE**. La primera versión de esa
  mutación salió roja **por la razón equivocada** —cruzar sólo la hoja contra la raíz del schema
  falla también con la sección correcta— y un rojo que no prueba lo que uno cree es peor que no
  mutar: hubo que rehacerla.
- `feedback_a_census_entry_is_how_a_defect_survives_its_own_gate` — **declarar una
  excepción con su razón es lo correcto cuando de verdad es una excepción, y es
  cómo un defecto sobrevive al gate que lo vio.** El #132 escribió
  `RaizDelCdnTests` contra las rutas de una máquina de los `appsettings`, recorrió
  el JSON entero con el criterio correcto —agnóstico del literal— y encontró
  **tres** que no eran del CDN: las dos del certificado de Kestrel
  (`C:\LOCAL_CDN\synergos-dev.crt`) y el buzón de correo
  (`C:\Users\HITMA\Desktop\…`). Las declaró en su censo con un razonamiento que
  sigue siendo correcto —«si faltan, Kestrel no arranca y el envío falla; fallar a
  la vista es lo contrario de servir una página muerta»— y con un
  **«alguien tendrá que decidir dónde viven»** al final. Nadie fue, cinco tickets,
  hasta que alguien clonó el repo (#137).
  Es `feedback_a_fabrication_can_be_a_derivation` aplicado a un CENSO en vez de a
  un `<remarks>`: **lo identificado la auditoría siguiente lo lee y pasa de
  largo**, y un censo lo blinda mejor que un comentario porque además *parece* la
  forma correcta de dejarlo — tiene su razón al lado y su gate alrededor.
  **Lo que distingue una excepción legítima de un defecto blindado es si su razón
  contesta «por qué esto NO se arregla» o «por qué esto no se arregló TODAVÍA».**
  La primera es una decisión; la segunda es un ticket sin abrir, y ahí la línea
  del censo tiene que ser el ticket.
  **Y la mitad que el censo no vio, que es la que hacía caro el defecto: nada en
  el repo CREABA ese certificado.** «Falla a la vista» era cierto y no servía,
  porque el mensaje de .NET dice «no se encontró el fichero» y ningún documento
  nombraba la dependencia — o sea una dependencia obligatoria sin camino para
  obtenerla, que no se ve en ningún fichero porque **la ausencia de una
  herramienta no está escrita en ninguna parte**. La pregunta que lo caza:
  *¿quién crea esto que la configuración da por hecho?*
  **Y el segundo diente del censo es lo que hace que esto se cierre solo**: una
  entrada declarada que ya no corresponde **rompe el build**, así que el commit
  que arregla las rutas está obligado a vaciar el censo en vez de dejarlo
  afirmando que las tres siguen ahí. Un censo vigilado en un solo sentido habría
  quedado mintiendo.
  **Y al arreglarlo casi escribí la copia que este fichero prohíbe**: mi primer
  gate recorría los `appsettings` con un criterio nuevo —`NombraUnaMaquina`— que
  era el regex del #132 reinventado. Dos gates con el mismo criterio es
  `feedback_the_same_algorithm_is_not_the_same_thing`: el día que uno se afine, el
  otro miente. Lo que de verdad faltaba era **otra pregunta** —no «¿nombra una
  máquina?» sino «¿lo que esa carpeta contiene puede llegar a GitHub?»—, porque
  el arreglo **crea** un riesgo que no existía: la llave privada pasó de vivir
  fuera del árbol a vivir dentro. Ese gate le pregunta a **git**
  (`git check-ignore`) y no al texto del `.gitignore`, que pasaría en verde con la
  línea comentada, con un `!certs/` más abajo o con el fichero renombrado — y
  distingue la salida **128** («no pude preguntar») de la 0, porque tratar «no sé»
  como «está protegida» es el verde sobre el vacío de siempre.
- `feedback_the_verification_command_can_be_the_one_that_hides_it` — **cuando el comando
  que la guía manda correr para verificar es el que tapa el defecto, no hay disciplina que
  lo encuentre: hay que mirar con la OTRA herramienta.** `Synergos.CMS.sln` listaba **23**
  proyectos con **32** en disco, y los nueve ausentes —todos capacidades— los **referencia**
  `Synergos.CMS.Tests`. Abrir la solución en Visual Studio daba **nueve NU1105**; desde la
  CLI, `dotnet build` y `dotnet test Synergos.CMS.sln` salían en verde con 3.257 pasando,
  porque **MSBuild resuelve un `ProjectReference` por RUTA y no por pertenencia a la
  solución** — los compila transitivamente y nadie se entera (#133).
  Es la forma de #126 y #92 **con los papeles cambiados**: allá el desarrollo tapaba lo que
  el contenedor veía, acá la CLI tapa lo que ve el IDE. Y **crece sola**, que es lo que la
  vuelve regla: las capacidades entraron de a una a lo largo de muchas HU, la `.sln` se
  actualizó las once primeras veces y dejó de actualizarse; el daño no llegó de golpe, se
  acumuló sin que ninguna corrida lo delatara.
  **La pregunta que lo caza: ¿qué herramienta mira este artefacto que nosotros no usamos
  nunca?** Un `.sln`, un `.editorconfig`, un `launchSettings.json` son ficheros que la CLI
  lee a medias o ignora, así que su deriva no aparece en ninguna corrida. **Y el cruce se
  hace en los DOS sentidos y derivando las dos listas del disco**: lo que existe y la
  solución no lista es el defecto, y lo que la solución lista y no existe es el mismo con el
  signo cambiado — una entrada muerta rompe la carga del IDE, y escribir la lista a mano en
  el gate sería una tercera copia de algo que ya está en dos sitios.

- `feedback_a_build_with_643_warnings_hides_the_644th` — **un log de build con
  cientos de avisos no es deuda cosmética: es una señal apagada, y lo que entra
  por ahí no se ve nunca.** `dotnet build -t:Rebuild` salía con 0 errores y
  **643 avisos únicos** (#134); dentro había siete defectos de verdad que nadie
  había mirado. El más fino: `RelojFalso.Avanzar` de `NotificationServiceTests`
  era `async Task` —el ÚNICO de los diez relojes de prueba del repo que no es
  `void`— así que sus dos llamadores no lo esperaban y **funcionaba por
  accidente**, porque el cuerpo corre síncrono antes de devolver la `Task`. Es
  la misma forma que este fichero ya tiene escrita sobre un gate siempre rojo
  («deja de leerse»), aplicada al build.
  **Y la familia más grande resultó ser documentación que el compilador TIRA.**
  Cuatro sitios escribían `///` sobre un parámetro posicional de un `record`
  (CS1587), donde no se emite nada: uno eran las **17 líneas** que explican el
  defecto #110 sobre `PropertyDraft.Address` —el campo que existe para
  cerrarlo—, invisibles en la documentación y en IntelliSense. Escribir la
  explicación no basta: hay que escribirla **donde el compilador la recoge**
  (`<param>`), y el aviso que lo decía llevaba sepultado bajo los otros 642.
  **Lo que NO se hace es apagar la familia entera.** CS1573 sí entra a `NoWarn`
  —medido: documentar UN campo subió la cifra de 287 a 308, porque documentar
  uno los exige TODOS, y trescientas líneas de `<param name="Id">El id.</param>`
  es cómo se deja de leer la que sí dice algo— y CS1587/CS1734 **se quedan como
  errores**, que son los que marcan documentación descartada. La distinción es
  la de siempre: una convención que sobra se declara con su razón; una que
  miente se arregla.
  **El trinquete es `TreatWarningsAsErrors=true` y no un contador contra una
  línea base**: con cero avisos es gratis, no hay gate aparte que olvidar, y un
  contador se pondría rojo el día que alguien añade un fichero legítimo y verde
  sobre todo lo que ya estaba — distinguir al revés que el defecto. El precio
  está escrito en `Directory.Build.props`: un SDK más nuevo con analizadores
  nuevos puede ponerlo rojo en una máquina y no en otra, y la salida es entrar
  a `NoWarn` **con su razón al lado**.

- `feedback_the_test_project_is_where_a_clean_graph_gets_dirty` — **un grafo de
  producción impecable no dice nada sobre lo que arrastra abrir la solución: el
  proyecto de TESTS es por donde los dos árboles se vuelven a pegar, y ahí nadie
  mira.** Medido (#135): `Synergos.CMS.Web` referencia **dos** proyectos
  —`Application` e `Interfaces`— y cero capacidades; los dos árboles eran
  **disjuntos**. Y sin embargo `Synergos.CMS.Tests` referenciaba **28**, o sea
  las veinte capacidades, los cinco orquestadores, `Core` y `Shared`: el único
  sitio del repo que los unía. El síntoma que llegó no fue un fallo, fue una
  **percepción** —«para levantar esto hay que levantar todas las APIs»— y era
  razonable, porque el Explorador de soluciones enseña 34 proyectos planos y no
  enseña el grafo.
  **La salida no es relajar la referencia: es partir el proyecto por la pregunta
  que cada test contesta.** Tres suites —CMS, Servicios y Arquitectura— y la del
  CMS pasó de 28 referencias a **una**. Lo que gana no es orden: es que la
  separación de §0.B.11 la impone **el compilador** en vez de una convención.
  **Y la excepción queda NOMBRADA en vez de escondida.** Comprobar que los dos
  árboles están separados exige poder ver los dos, así que la exención existe —
  pero ahora es un proyecto entero que se llama `Synergos.Arquitectura.Tests`, no
  un comentario dentro del proyecto de al lado. Al medirlo apareció el dato que
  lo hace baratísimo: **55 de los 66 gates no usan un solo tipo de producción**
  —leen la FUENTE del disco, que es lo que les permite vigilar un Razor, un
  `.mjs`, un compose o un `appsettings`— así que ese proyecto referencia
  **cuatro** cosas y no veintiocho.
  **Y esa cifra decía «46 de los 57» hasta el #148**, porque era la única de
  `CLAUDE.md` cuyo sujeto es el propio mecanismo de vigilancia y no la miraba
  ningún gate. Se desvió **quedándose corta**, que es la dirección que no se
  nota: una cifra que sobra la persigue alguien, una que falta se lee como si
  estuviera bien — y el argumento de este párrafo envejecía con ella. Hoy la
  cruza `CifrasDeClaudeMdTests` contra el disco, **en los dos sitios donde la
  frase aparece**.
  **Lo que cuesta el corte, y ninguna de las tres se deduce leyendo:** (a) las
  supresiones de nivel de ENSAMBLADO (`CA1707` por los nombres con guion bajo de
  xUnit) se quedan en el proyecto viejo y el nuevo nace con **1886 errores** de
  nomenclatura, así que ese fichero vive una vez y se **enlaza**; (b) los
  `InternalsVisibleTo` nombran al ensamblado, no a la carpeta, y hay que mover
  cada uno **a quien de verdad usa ese internal** —el presupuesto de
  `StoreWriteGate` lo usa Servicios, el cableado del composer lo usan los
  gates—; (c) un gate que cuenta **su propio** `Assembly.GetTypes()` deja de ver
  el 88 % de la suite, y copiarlo tres veces sería el defecto que ese gate existe
  para cerrar: se **enlaza** y cada copia mide su ensamblado contra su fila,
  cuadrando además el total contra la suma.
  **Y las soluciones parciales necesitan su propia regla, que es #133
  generalizada**: toda solución es **cerrada bajo `ProjectReference`** —si lista
  un proyecto, lista aquello a lo que apunta— porque `dotnet build` resuelve por
  RUTA y compila igual mientras Visual Studio da un NU1105. Con una sola solución
  eso fue un defecto; con cuatro, es inevitable sin gate. El segundo diente hace
  falta igual: **las parciales tienen que ser estrictamente menores** que la
  integradora, o la salida barata del primero es meterlo todo en las cuatro y
  perder lo único que las parciales compran.
  **Ojo con el SDK**: `dotnet new sln` en el SDK 10 crea un **`.slnx`** (XML), que
  un Visual Studio viejo no abre. Va con `--format sln`.

- `feedback_a_path_is_not_a_name_and_a_flat_tree_hides_it` — **mientras un árbol
  es plano, la RUTA de un proyecto es su NOMBRE, y todo el mundo escribe la
  primera creyendo escribir el segundo. El día que se reorganiza, no falla una
  cosa: fallan treinta y ocho.** Medido (#136): mover el backend a
  `backend/{nucleo,capacidades,orquestadores}/` dejó **38 rutas equivocadas** en
  los gates, más el Dockerfile de servicio, el generador del compose, la matriz
  de imágenes del CI y el ensayo de restauración.
  **Y el arreglo NO es reescribir los 38 literales**: eso escribe la disposición
  nueva en 38 sitios, o sea la misma deuda con otro valor. Es **invertir la
  dependencia**: un gate no necesita saber DÓNDE está un proyecto, necesita su
  fuente — así que pregunta por nombre (`Proyectos.Dir("Synergos.Api.Booking",
  "Domain")`) y una sola clase lo encuentra en el disco. La próxima
  reorganización cuesta **cero**.
  **El modo de fallo que importa no es el rojo: es el VERDE.** Cinco gates
  descubrían los servicios con `Directory.EnumerateDirectories(RepoRoot())` +
  filtro por prefijo. Tras el movimiento eso devuelve **una** carpeta —`backend`—
  el filtro no casa con nada y el gate **pasa sin mirar un solo proyecto**.
  Medido con la mutación puesta: `ComposeStackTests` da **12/12 en verde sobre
  una lista vacía**. Un rojo se arregla; un verde sobre el vacío se hereda.
  **Cómo se cierra, y son los dos dientes de `RutasDeProyectoTests`**: se prohíbe
  construir la ruta a mano **y** descubrir enumerando la raíz. Se parsea la fuente
  **sin comentarios**, porque los `<remarks>` que explican la prohibición citan las
  formas prohibidas — un gate que se engaña con su propia explicación es
  `feedback_a_gate_that_parses_source_needs_its_own_mutations`. Y sólo se prohíbe
  para las familias que **se movieron**: `Synergos.CMS.*` sigue en la raíz, así que
  nombrarla ahí no es un defecto — un gate que pide un cambio que no arregla nada
  se desactiva.
  **Tres cosas del camino que no se deducen leyendo.** Una: `COPY` de Docker **no
  expande shell**, así que dentro de la capa de restore no hay forma de derivar la
  ruta del nombre sin copiar la fuente entera — que es justo lo que esa capa existe
  para no hacer. Se pasan los dos (`PROJECT`, `PROJECT_DIR`) **con una guarda que
  los cruza** (`basename $PROJECT_DIR == $PROJECT`), porque un descuadre produciría
  una imagen con el nombre de un servicio y **la fuente de otro**: arranca,
  contesta `/health` y sirve el dominio equivocado. Dos: los `paths:` de los nueve
  workflows **no hubo que tocarlos**, porque todos nombran `Synergos.CMS.Web/**`,
  que no se movió — la mitad del susto de «62 líneas de workflows» era ruido del
  `grep`. Tres: `compose.prod.yml` sale **byte-idéntico**, porque la topología
  desplegada va por nombre de imagen y no por ruta; que no cambie es la
  comprobación de que el movimiento es del árbol y no del producto.

- `feedback_a_red_build_can_arrive_without_a_commit` — **`TreatWarningsAsErrors`
  cruzado con `NuGetAudit` hace que el color del build deje de ser una propiedad
  del repo: la base de avisos se baja EN EL RESTORE, así que el día que alguien
  publica un aviso el build se pone rojo en todas las máquinas a la vez, sin que
  nadie toque un commit** (#149). El #134 dejó escrito el precio que sí previó
  —«un SDK más nuevo con analizadores nuevos puede ponerlo rojo en una máquina y
  no en otra»— y ése tiene la forma contraria: es local y se diagnostica mirando
  la máquina. Éste es global y **no se diagnostica mirando nada del árbol**: el
  código compilaba en cero avisos, las 3280 pasaban, y `dotnet build` salía con
  un error sobre un paquete que nadie había tocado en meses.
  **La salida NO es apagar la auditoría** —sería cambiar el único aviso que nadie
  del repo puede prever por uno que nadie va a ver nunca— **y tampoco es `NoWarn`
  por defecto.** Se decide con una pregunta que se contesta en dos minutos y con
  datos, no leyendo: **¿el rango vulnerable del aviso lo cierra alguna versión
  publicada de la rama que tengo clavada?** El índice de vulnerabilidades de NuGet
  lo dice —es el mismo que usa la auditoría, así que no hay que creerle a nadie—
  y la lista de versiones también:
  `curl -s https://api.nuget.org/v3-flatcontainer/<paquete>/index.json`.
  Si **sí** la cierra, se sube y punto: silenciarlo escribiría una razón que
  contesta «por qué esto no se arregló TODAVÍA», que es un ticket sin abrir
  disfrazado de exención —`feedback_a_census_entry_is_how_a_defect_survives_its_own_gate`—.
  Si **no**, ahí sí entra a `NoWarn` con su razón, como NU1902, cuyo rango
  `(, 16.3.3]` de verdad no lo cierra ningún 13.x.
  **Y lo que el susto destapó no era la versión: era que estaba escrita a mano en
  SIETE sitios y ninguno los cruzaba.** Las tres suites pasaban en verde con
  `Directory.Packages.props` ya en 13.16.2 y `CLAUDE.md` §1, la skill de
  guardrails y la tabla de la ADR todavía diciendo 13.13.1 — o sea que la guía
  que alguien lee ANTES de tocar nada mentía y nada se ponía rojo. Hay gate
  (`VersionDeUmbracoTests`), y su primer diente es el que faltaba desde el
  andamiaje: **ADR 0001 prohíbe 14+ y eso era prosa**, así que un `Version="14.0.0"`
  compilaba. **El corte que hace útil al segundo diente es distinguir AFIRMAR de
  NARRAR**: el `CHANGELOG` y la ADR 0093 nombran la versión vieja porque describen
  mediciones hechas contra ella, y reescribir eso para que cuadre con el pin de hoy
  sería peor que la cifra vieja — sería falsear lo que alguien midió. El censo sólo
  lleva a los que dicen «la versión clavada ES».

- `feedback_moving_an_artifact_out_of_a_repo_moves_it_out_of_its_gates_reach` —
  **promover algo a un repo compartido no sólo mueve el fichero: mueve todo lo que
  ese fichero AFIRMABA fuera del alcance de los gates de acá, y eso no falla — deja
  de vigilarse.** El #141 sacó el arnés a `Synergos.Fabrica`, y con él se fueron dos
  filas del censo de `VersionDeUmbracoTests`: las dos frases de
  `synergos-guardrails` que afirman el pin de Umbraco. El gate no las puede leer
  desde otro repo, así que **el pin volvió a estar afirmado en un sitio que nadie
  cruza** — que es literalmente el defecto que el #149 acababa de medir, reintroducido
  por el commit que venía a mejorar las cosas.
  **Lo único que lo destapó fue el segundo diente del censo**: una entrada que ya no
  corresponde ROMPE, así que el commit que borró las copias estuvo obligado a mirar.
  Sin esa mitad, el gate habría seguido en verde vigilando dos ficheros ausentes —
  `feedback_a_census_entry_is_how_a_defect_survives_its_own_gate` con el fichero ido
  en vez de la razón caducada.
  **La pregunta que se hace ANTES de promover, y no después:** *¿qué gate de este
  repo lee lo que me estoy llevando?* Cada uno de ésos se queda sin sujeto, y hay
  exactamente dos salidas honestas: que el repo de destino herede la comprobación
  —con su ticket y su número, no con una intención—, o quedarse. Lo que no vale es
  borrar la fila y seguir: una cobertura que se pierde en silencio se pierde el día
  que hacía falta.
  **Y el reparto es el mismo defecto de siempre visto de lado**: lo que se lleva el
  arnés son AFIRMACIONES (el pin, las cifras, las rutas), y lo que se queda es quien
  las cruzaba. Las dos mitades en verde y el hueco justo en medio.

- `feedback_a_severity_travels_further_than_its_exemption` — **una severidad de
  compilación viaja a artefactos que el build nunca compila, y su exención NO viaja con
  ella: lo que sale de ahí no es un build más estricto, es uno con OTRAS reglas** (#151).
  El #134 puso `TreatWarningsAsErrors=true` con el árbol en cero avisos, así que era
  gratis — y previó **dos** precios, los dos locales («un SDK más nuevo puede ponerlo rojo
  en una máquina y no en otra») y el #149 añadió el tercero, global («la base de avisos se
  baja en el restore»). El cuarto no lo vio nadie, y es el que rompió el producto **dos
  veces a la vez**:
  **(a) La imagen.** Los dos `Dockerfile` copian a su capa de restore los ficheros de
  configuración **uno por uno** —`global.json`, `Directory.Build.props`,
  `Directory.Packages.props`— y **`.editorconfig` no estaba**. Ahí viven ocho severidades
  (`CS1574`, `CA1848`, `CA1873`, `CA1305`, `CA1859`, `CA1861`, `CA1805`, `IDE0011`), así
  que la imagen se compilaba con ocho diagnósticos MÁS que el repo. Antes del #134 eso
  sólo producía avisos que nadie leía; después, **las 26 imágenes dejaron de
  construirse** — medido moviendo el fichero y reconstruyendo en Release:
  `Synergos.CMS.Interfaces` da **8 CS1574** y `Synergos.Api.Consent` **6 CA1848 + 2 CS1574
  + 2 CA1873**, los mismos errores fichero por fichero que el CI.
  **(b) Las vistas.** El SDK copia `$(TreatWarningsAsErrors)` al bloque
  `compilationOptions` del `deps.json`
  (`Microsoft.NET.PreserveCompilationContext.targets`), y de ahí saca sus opciones el
  compilador de Razor **en caliente** — que acá es el único que hay
  (`RazorCompileOnBuild=false`). Resultado: **toda página de contenido en 500** desde el
  #134, por dos avisos de un partial. Medido: `/` contestaba 500 por un `object?`
  (CS8669) y un `GetDictionaryValue(string, string)` obsoleto (CS0618) de
  `SynHost/HeroBanner.cshtml`; cambiando **ese único booleano** del `deps.json`, sin tocar
  una línea de Razor, **19 785 bytes con su cuerpo**.
  **Y la propagación a las vistas está A MEDIAS, que es lo que decide el arreglo.**
  `Nullable` **no** está en la lista que el SDK propaga y `TreatWarningsAsErrors` **sí**,
  así que las vistas se compilan siempre con el contexto nullable apagado mientras el
  proyecto lo tiene encendido: un `object?` en un `.cshtml` es CS8669 **para siempre**, en
  109 de las 401 vistas. Arreglarlas no era una opción; quitar la propagación, sí. **Un
  trinquete que el build no puede medir no es severidad: es una trampa** — sólo se
  descubre con un 500, que es distinguir al revés que el defecto (#128).
  **Cómo se caza, y la pregunta se hace al AÑADIR la severidad**: *¿qué otras
  compilaciones se hacen con este árbol, y les llega también lo que las exime?* Se
  contesta con un `grep` de los `COPY` de cada Dockerfile y con un `jq` del `deps.json`
  — las dos son un minuto, y ninguna se le ocurre a quien acaba de dejar el build en
  verde.
  **Y el gate va sobre los ARTEFACTOS, no sobre el `.csproj` ni sobre la prosa**
  (`ConfiguracionDelBuildTests`): cruza los `COPY` de los dos Dockerfile contra los
  ficheros de configuración que existen en la raíz —**en los dos sentidos**, que uno
  copiado y ausente rompe la imagen en el `COPY`— y lee el `deps.json` generado. Se
  parsea el Dockerfile **sin comentarios**, porque los comentarios que explican por qué
  `.editorconfig` está en la lista lo nombran: medido, con la línea `COPY` mutada y el
  comentario intacto, un gate que leyera el fichero entero pasaría en verde.
  **La cuarta cara, que no la cierra nada de esto**: la señal existía y estaba roja.
  `humo-portada` y `humo-conectado` llevaban **quince días y seis commits** en rojo en
  `master`, y `images.yml` un mes. Este fichero ya tiene escrito que «un gate siempre
  rojo deja de leerse»; el precio esta vez fue el sitio entero caído mientras las tres
  suites pasaban.

- `feedback_a_gate_that_asks_for_more_than_needed_never_goes_red` — **un gate que exige MÁS de lo
  que hace falta no falla nunca: se cumple. Por eso su caducidad es invisible, mientras que la de
  un gate que exige de menos la destapa el primer defecto que deja pasar** (#152). El compose
  pedía `replicas: 1` en los veinticinco servicios con la razón «`JsonCollectionStore` tiene un
  lock de PROCESO: dos instancias corrompen», y la HU #112 quitó exactamente eso —un fichero por
  documento y `StoreWriteGate` subiendo el turno a proceso cruzado, con 400 ajustes medidos en
  vivo dando 400—. El arreglo vivió en `Synergos.Shared` y **nadie volvió al compose**: cinco
  tickets con el gate en verde afirmando lo contrario de lo que el repo acababa de construir.
  Es `feedback_a_fixture_built_on_a_neighbouring_defect_expires_with_it` con el sujeto cambiado
  —allá un test afirmaba el síntoma de un defecto ajeno, acá un gate afirma una limitación ajena—
  y con un agravante: **el compose es lo que alguien lee para saber qué puede levantar**, así que
  le decía veinticuatro veces que no hiciera la única cosa que el #112 construyó, y con la razón
  equivocada.
  **La pregunta que lo caza, y hay que hacerla desde el lado incómodo:** *¿qué gate se quedaría
  en verde si esto que acabo de arreglar nunca hubiera hecho falta?* Un `grep` de la razón vieja
  —acá «lock de PROCESO»— por el árbol entero, no sólo por el código.
  **Y el arreglo NO es borrar el gate**, que fue el primer instinto: la prohibición sigue viva en
  los cuatro orquestadores, que heredaron el fichero por documento pero **no** tienen turno por
  saga (#34). Quitarla entera habría cambiado un gate que miente por ninguno. Se hace que **pida
  lo correcto** y se deriva del disco quién enchufa el turno, nunca de una lista — una lista se
  queda corta el día que un orquestador gane el suyo.
  **Dos cortes del camino.** Uno: el criterio ya existía en `TurnoDeEscrituraWiringTests` y mi
  primera versión lo **reescribió** —peor, barriendo todo el C# en vez del `Program.cs`, o sea
  dando por cableada a la que sólo lo menciona—; es
  `feedback_the_same_algorithm_is_not_the_same_thing` y se resolvió exponiendo la derivación una
  vez. Dos: con los veinticinco servicios en `1`, **el gate no ejercita ni una decisión** — pasa
  en verde sin mirar. Las tres ramas que deciden (capacidad con turno escala, orquestador no,
  `Api.Sessions` escala por ser append-only) viven como tests de una función pura, porque una
  mutación prueba un caso y se lo lleva.

- `feedback_a_convention_stated_as_a_rule_is_measured_before_it_is_taught` — **una convención
  que un documento enseña como regla hay que CONTARLA antes de escribirla, porque la que uno
  recuerda suele ser la que vio primero** (#155). El doc 12 §5.3 decía que la implementación en
  proceso de un seam se llama `Stub<X>`. Medido sobre el árbol: de los **109** seams con
  implementación declarada, **48** la tienen así — el 44 %, una mayoría relativa y nada más. Los
  otros 61 nombran **lo que la implementación ES**: `FileSystem*` (19) donde persiste, `Http*`
  (15) cuando cabla, `Default*` (14) cuando no hay nada más filoso que decir, `Catalog*`,
  `InMemory*`, `Umbraco*`, `Composite*`, `Hmac*`. O sea que el molde describía la minoría como si
  fuera universal, y un generador que lo aplicara al pie de la letra produciría nombres que
  mienten.
  **La salida NO es renombrar los 48**: `Stub` en este repo tampoco quiere decir «en memoria» —19
  de los 49 son durables— así que sería cambiar una convención imprecisa por otra, en 48 sitios y
  sin cerrar nada. Se escribe la verdad medida y **el gate va donde deja de ser cosmético**: un
  `Stub<X>` de un seam de SELLO rompe el build, porque llamar provisional al firmante del QR
  afirma algo falso sobre lo único que hace válida una entrada en la puerta.
  **Y el gemelo del mismo ticket enseña cuándo NO hay regla que escribir.** El doc 12 §5.5
  mostraba el composer parcial como uno por vertical; hay **once** ficheros para siete verticales
  y `SeamComposer.EventsPropertiesGov` lleva tres en 499 líneas. Eso no es un reparto con criterio
  que alguien pueda re-derivar: es acumulación. Cuando la medición no encuentra regla, **lo
  honesto es que el spec lo DECLARE** —igual que declara sus seams— y que el molde diga sólo lo
  que sí es suyo: un vertical nuevo estrena el suyo, porque agrupar se hace cuando hay una razón y
  no por defecto. Inventarse un criterio para poder derivarlo habría sido escribir la acumulación
  como si fuera diseño.
  **La pregunta que lo caza, y se hace antes de escribir la regla, no después:** *¿cuántos del
  árbol la cumplen?* Un `grep` y un contador. Las dos veces que este repo enseñó una convención
  sin contarla —ésta y «los siete lo tienen» del #153— el número estaba mal.

- `feedback_a_key_the_app_reads_needs_a_path_from_whoever_sets_it` — **una clave de
  configuración que el código lee, los ADRs documentan y la guía manda poblar puede no tener
  NINGÚN camino desde el operador hasta el proceso, y eso no falla: el default se queda puesto
  y todo el mundo cree que lo configuró** (#154). Medido: de las seis propiedades `*Secret*`
  de los POCOs del CMS, el compose pasaba **una**. Las que faltaban eran las dos de firma —la
  del QR de las entradas y la del id de los diplomas—, o sea justo las que `CLAUDE.md` §11
  describe como «lo único que el respaldo no puede regenerar […] va donde va la identidad», que
  es el `.env` del servidor. Ponerlas ahí **no llegaba al contenedor**.
  Es el escalón de arriba de `feedback_a_key_in_the_wrong_section_is_a_key_nobody_reads`: allá
  la sección estaba mal, acá la sección está bien y **falta el transporte**. Y las dos mitades
  del daño son silenciosas — sin el secreto la llave se genera y se guarda cifrada, así que las
  entradas siguen firmadas; lo que se pierde es poder ROTARLA y llevarla a otra instalación, y
  eso no lo nota nadie hasta que hay dos, o hasta que se pierde el volumen y todo QR impreso
  deja de validar en la puerta del evento.
  **La pregunta que lo caza, y no se le ocurre a quien acaba de escribir el POCO:** *¿por dónde
  llega al proceso lo que este tipo lee?* Se contesta con un `grep` del nombre de la propiedad
  en el generador del compose y en `.env.example` — un minuto— y **las tres capas tienen que
  nombrarla**: el POCO, el compose y el ejemplo del `.env`.
  **Y lo que NO se hizo, medido antes de decidirlo:** un gate «toda propiedad `*Secret*` viaja
  en el compose». De las seis, **tres no deben viajar** —las de Wompi del lado del CMS, que en
  producción no cobra— así que serían tres exenciones sobre seis: el muro de excepciones que
  deja de leerse, y ya hay gate que vigila esa ausencia por otra vía
  (`PaymentEngineCoexistenceTests`).

  **Addendum — renombrar una sección deja huérfano lo que un servidor ya tiene puesto, y hay
  que PARAR al arrancar.** Mover el secreto de `Synergos:Events` a `Synergos:Eventos:Ticket` es
  correcto y deja a todo despliegue anterior con una clave que el binder descarta: el POCO cae
  a su default —cadena vacía, que aquí significa «generá otra llave»— y el operador cree que
  rotó cuando no tocó nada. Así que el composer **lanza** si la vieja trae valor, con el nombre
  nuevo en el mensaje. Tres cortes que costaron su mutación: se mira la **sección entera** y no
  sólo la clave que se movió (`Synergos__Events__ApiKey` dejaría al cliente sin llave, y ése no
  se me habría ocurrido enumerarlo); se añade a mano la **vecina plana**
  —`Synergos:Eventos:TicketSigningSecret`, lo que teclea quien lee un documento viejo y corrige
  la letra— porque vive bajo la sección buena y el barrido de la retirada no la ve; y **vacío
  NO para**, porque `.env.example` deja la línea vacía a propósito y el compose la pasa
  presente-y-vacía.

  **Y el segundo addendum es sobre el gate, que se quedó ciego en el MISMO commit.** El diente
  que comprueba que el rechazo está *enchufado* recorta el cuerpo de `Compose` hasta
  `"private static void"` —el siguiente miembro— y media hora después pasé el guardia a
  `internal` para poder probarlo: el corte dejó de encontrar nada. **Lo delató su red de
  seguridad**, que es para lo que estaba; sin ella habría medido el fichero entero y pasado en
  verde con la llamada quitada, que es exactamente el addendum #14 de
  `feedback_an_exemption_needs_a_signature_behind_it`. La lección es la de
  `feedback_a_path_is_not_a_name_and_a_flat_tree_hides_it` aplicada a un gate: **seguía un
  MODIFICADOR en vez de una propiedad estructural**; hoy corta por el siguiente miembro de la
  clase, cualquiera que sea su visibilidad.

- `feedback_an_axis_the_mould_does_not_write_gets_solved_twice_differently` — **un eje que el
  molde describe pero no DESCOMPONE lo resuelve cada vertical a su manera, y las dos formas
  conviven sin que nada las cruce** (#153). El doc 12 §3 describía el eje 3 —el artefacto: la
  entrada con su QR, el diploma, el expediente— y su §5 decomponía sólo los ejes 1 y 2, así que
  **los dos únicos verticales que sellan su artefacto lo cablearon distinto**: `AcademySettings`
  llevaba la transacción y el sello en un POCO y una sección, y Eventos los llevaba en dos
  —`EventosSettings` bajo `Synergos:Eventos` y `EventsSettings` bajo `Synergos:Events`— a **una
  letra de distancia, donde el binder descarta en silencio** (#154). Eso no era estilo: era la
  causa del defecto, y se lee al revés de como se encontró — el sello necesitaba sección propia,
  el nombre del vertical ya estaba tomado, y le tocó el que quedaba libre. **Cerrado en el #154**:
  la del sello va ANIDADA (`Synergos:Eventos:Ticket`) y en su propio POCO, y ahí se vio que la
  regla que este mismo commit escribió —«mismo POCO, misma sección»— estaba derivada del ÚNICO
  vertical con un solo eje cableado.
  **Y la frase que lo tapaba era una CIFRA sobre una lista incompleta**: «los siete lo tienen»
  escrito debajo de **cinco** ejemplos, o sea `feedback_a_named_list_beats_a_count` dentro del
  documento que predica medir. Al derivar los siete aparecieron los dos que faltaban, y el
  interesante es Salud: guarda por **`IPhiStore`** y no por `IJsonEntityStore`, porque el dato es
  clínico — con la lista a mano ese caso no existía.
  **Las dos invariantes que el eje sí tenía y nadie había escrito, las dos medibles:** el
  artefacto vive **FUERA** del seam de la transacción y lo comparten sus dos implementaciones (un
  emisor dentro del motor en proceso no lo puede usar el cliente cableado, y copiarlo es «un QR
  con dos definiciones»); y **el REGISTRO nunca sale a la red, el SELLO sí puede** —lo que
  Educación mudó a `Api.Signing` fue la custodia de la llave, no el índice de emitidos—.
  **Lo que decide si hace falta sello es una CUARTA pregunta**, hermana de las tres de §4 y
  separada de ellas: *¿alguien de FUERA tiene que poder comprobar esto sin creernos?* Una entrada
  la lee un portero que no es nuestro; un RMA lo mira quien vendió. Dos de siete contestan que sí,
  y para los otros cinco un sello sería custodiar una llave para nadie.
  **Y el gate que salió de ahí encontró al primer arranque lo que el eje no cumple**: en Realty el
  registro del artefacto y el seam del eje 2 son **la misma clase**, así que con `Mode=Api` no
  queda nada de este lado — la agenda se sigue pintando, porque es una función pura, y quien
  reservó no ve su visita (#158). Dos cortes que costaron su mutación: el diente de «tiene gemelo
  `Http*`» **se quitó** —marca ese caso legítimo y no sabe distinguirlo, y un gate que marca al
  bueno enseña a ignorarlo— y en su lugar la fila del censo que no guarda nada **tiene que nombrar
  su ticket**, porque una razón que contesta «por qué esto no se arregló TODAVÍA» es un ticket sin
  abrir disfrazado de exención.

- `feedback_measure_the_generator_at_its_best_or_the_finding_is_yours` — **cuando se mide si un
  MOLDE da para generar, la cifra depende de lo bien que se haya implementado el molde, no sólo
  del molde — así que hay que darle su MEJOR versión antes de contar, o la lista de hallazgos es
  propia y no suya** (#140). El piloto 0 derivó el plan de Eventos y dio **36,8 %** la primera
  vez, aplicando el doc 12 §5.3 al pie de la letra (`I<X>Service`, en singular y derivado del
  sustantivo). Con la regla correcta —los seams son **varios, uno por operación**, y el cliente se
  nombra por el seam que cablea y no por el sustantivo— la misma medición da **71,4 %**. Los 35
  puntos de diferencia no eran del molde: eran míos.
  **El tell, y es incómodo porque halaga:** una medición cuyo resultado deja bien a quien mide.
  Una derivación flaca produce una lista de hallazgos larga y un informe que parece valioso; es
  abogacía y no medición. La pregunta que lo caza se hace ANTES de publicar la cifra: *¿esto es lo
  peor que el molde puede hacer, o lo mejor?*
  **Y lo que queda después de darle su mejor versión es lo que vale**: acá, que **el eje 3 no
  tiene sub-spec** —cinco de los ocho residuales son suyos— y que `Stub<X>` no es universal cuando
  la implementación en proceso ES la de verdad (`HmacTicketSigner` no es un doble de nada).
  **Corolario sobre el UMBRAL, porque este fichero ya dice lo contrario y hay que saber cuándo
  aplica cada uno.** El #134 eligió trinquete absoluto (`TreatWarningsAsErrors`) **porque hoy se
  cumplía**: con cero avisos era gratis. Acá el criterio del ticket era «≥ 90 %» y la realidad es
  71,4 %, así que un umbral absoluto dejaría `master` rojo hasta cerrar cuatro hallazgos — y un
  gate siempre rojo deja de leerse. La regla que unifica los dos: **un umbral absoluto sólo vale
  cuando el árbol YA lo cumple; con deuda declarada va una línea base**, vigilada en los dos
  sentidos para que el commit que arregla esté obligado a moverla.
  **Y el plan no puede leer las rutas de la cabecera.** Si el spec enumerara sus ficheros, el plan
  sería igual al spec y el porcentaje no mediría nada — `feedback_contract_shape_needs_its_own_test`
  aplicado a un generador. Se declaran DECISIONES (qué DocTypes, qué operaciones) y se derivan
  RUTAS con las reglas de nombre del molde.

- `feedback_a_write_that_validates_after_opening_leaves_zero_bytes` — **`io.open(ruta, 'w', …)` de
  Python TRUNCA el fichero y después valida sus argumentos, así que un `newline='\n'` —inválido:
  el valor legal es `'\n'` de verdad, no la secuencia escapada dos veces— deja el fichero en CERO
  BYTES con una excepción que habla de otra cosa** (`ValueError: illegal newline value`). Costó
  reescribir entero un `tools/*.mjs` de 500 líneas que aún no estaba commiteado (#140).
  Es el primo operativo de `feedback_restored_mutation_needs_a_touch`: el fallo no está donde
  parece, y el árbol queda en un estado que ningún gate mira. **Dos costumbres que lo cierran:**
  escribir a un temporal y mover, y —la que de verdad salva— **`git add` temprano**, porque lo que
  el índice tiene se recupera y lo que no, no. Y al restaurar una mutación sobre un fichero
  **sin commitear**, `git checkout --` es peor que no hacer nada: devuelve el fichero a HEAD, o
  sea se lleva el trabajo. Se salva con `cp` y se restaura con `cp` + `touch`.

- `feedback_a_rule_written_inside_its_only_obedient_copy_is_a_rule_nobody_reads` — **una regla
  escrita en el `<remarks>` del único sitio que la cumple no se difunde: se ENTIERRA, y encima
  parece difundida porque quien la lee la está leyendo ya cumplida.** `HttpPaymentProvider` decía
  con todas las letras *«se mira esa bandera y no el código de estado: repetir aquí la tabla de
  códigos sería una segunda verdad que se desincroniza»* — el razonamiento correcto, completo, y
  **dentro del único de los quince clientes que lo aplicaba** (#129). Los otros catorce no la
  desobedecen: no la ven. Es `feedback_a_fabrication_can_be_a_derivation` addendum #123 con el
  sujeto cambiado —allá lo blindado era un defecto, acá una regla buena— y con la misma salida:
  **lo que reemplaza a la nota no es otra nota, es un TIPO** (`RechazoDelArbolDeServicios`, el
  único sitio del CMS que nombra `transient`) **más un gate que cuente los declarantes**. El tell
  se busca con un `grep`, no leyendo: un `<remarks>` que explica por qué NO hacer algo, dentro de
  un fichero que no lo hace. Si la razón vale para sus vecinos, está en el sitio equivocado.
  **Y el corolario que decide cuánto trabajo es:** el trinquete sale **absoluto** porque hoy ya se
  cumple —cero clientes nombran un código de transitoriedad— que es la condición del #134; con un
  solo infractor habría sido línea base (#140).

- `feedback_a_finding_names_a_symptom_with_a_verb_and_the_verb_is_measured_first` — **un hallazgo
  dice «catorce clientes DECIDEN si reintentar mirando el código», y el verbo es lo primero que
  hay que medir: si se le cree, el arreglo se escribe catorce veces sobre una decisión que nadie
  tomó.** Medido rama por rama (#129): **cero** de los quince tiene bucle de reintento, y lo que
  los catorce deciden no es *si volver a intentar* sino **cómo PRESENTAR el fallo** —un 404 es «no
  existe», un 401 nombra la llave compartida, un 409 sube como rechazo de negocio con su motivo—,
  que es correcto y es de cada cliente. Un 503 ya cae al camino de infraestructura, que es donde
  debe caer. Creerle al verbo habría metido una política de reintentos **inventada** en catorce
  ficheros, con el agravante de que a un `store_busy` (#112) hay que reintentarlo y a una pasarela
  caída no, y ninguno de los catorce tiene con qué distinguirlos.
  Es el hermano de `feedback_measure_the_generator_at_its_best_or_the_finding_is_yours` por el
  otro lado: allá medir mal INFLA la lista de hallazgos, acá creerle al hallazgo infla el arreglo.
  **Y lo que queda después de medir se escribe como pregunta abierta, no se rellena**: quién
  reintenta —el cliente o quien lo llama— el CMS no lo tiene decidido; el árbol de servicios sí
  («la capacidad sabe QUÉ está colgado y CÓMO se reintenta; el orquestador, CUÁNDO y CUÁNTAS
  VECES») y traerlo acá es otro trabajo. Cerrar un ticket con una respuesta fabricada a la mitad
  que no se midió es el defecto que el ticket venía a cerrar, con el sujeto movido.

- `feedback_the_profile_production_runs_with_is_a_development_profile_until_someone_asks` —
  **`ASPNETCORE_ENVIRONMENT: Docker` hace que producción corra con un `appsettings` que se escribió
  para desarrollo, y de ahí sale todo lo que ese perfil traiga de más — incluida la CREDENCIAL del
  administrador, versionada en un repo público** (#150). `appsettings.Docker.json` traía
  `admin@synergos.local` con su contraseña en claro, y `compose.prod.yml` levanta el CMS con ese
  perfil: la cuenta de administrador del backoffice del sitio desplegado se creaba con algo que
  cualquiera puede leer en GitHub. El mismo literal estaba en **siete** sitios; el ticket decía
  seis, porque la lista estaba escrita a mano (`feedback_a_named_list_beats_a_count` otra vez).
  **Lo que lo vuelve regla es que ya había pasado y se arregló a medias**: el #113 encontró UNA
  cosa que ese perfil traía mal para producción —la siembra de desarrollo, con los catorce
  endpoints `[AllowAnonymous]` de `DevController` alcanzables desde internet— la apagó en el
  compose, y **no se hizo la pregunta que cierra la familia**: *¿qué MÁS trae este perfil que no
  sirve para producción?* Arreglar el caso y no la familia es cómo el segundo miembro sobrevive
  cinco tickets. La pregunta se hace al reutilizar un perfil, no al escribirlo, y se contesta con
  un `grep` de los nombres que huelen a credencial más una lectura de las banderas que enciende.
  **Y la parte que no se deduce, que hubo que MEDIR arrancando el proceso** (el ticket pedía
  «dejando la clave presente y vacía o ausente, según lo que Umbraco tolere»): con la clave vacía
  y el nombre puesto, Umbraco revienta al arrancar —«*if any of the UnattendedUserName,
  UnattendedUserEmail, UnattendedUserPassword are set, all of them are required*»—; y con **las
  tres ausentes NO revienta**, que es el caso peligroso: la instalación desatendida **completa** y
  deja el administrador con `userPassword = 'default'` y habilitado, o sea **un sitio que contesta
  200, al que nadie puede entrar y sin instalador que lo arregle**. Así que el `UnattendedUserName`
  se QUEDA en el perfil, y no por descuido: es lo único que convierte la ausencia de los otros dos
  en un fallo ruidoso. Es la forma de #56 y de la llave de firma de `Api.Identity` —fallar al
  cablear y no en la primera petición— aplicada al revés: acá lo que había que conseguir era que
  **falte** de manera ruidosa.
  **Lo que NO se escribió, y va dicho porque el ticket lo pedía:** el gate que cruza «toda clave
  con olor a credencial que el compose no pise **con `:?`**». Medido antes: son **más de cuarenta**
  exenciones en un solo fichero y todas legítimas —las decenas de `${SYNERGOS_API_KEY}` repetidas
  son la MISMA variable que el primer servicio ya exige con `:?`, y los ocho `${…:-}` son secretos
  opcionales a propósito— más cuatro falsos positivos de fábrica (`IdentityTokens__LifetimeMinutes`,
  `Synergos__Gob__DefinitionKey`, `Payments__wompi__PublicKey`). Eso es el muro que deja de leerse
  (`feedback_a_gate_that_asks_for_more_than_needed_never_goes_red`). Lo que sí es airtight y es lo
  que el defecto violaba: **el valor no está ESCRITO**, ni en un `appsettings` ni en un compose.
  Trinquete absoluto porque hoy se cumple, que es la condición del #134.
  **Y el corolario operativo: quitar un secreto del repo le quita el secreto a quien ARRANCABA con
  él.** Los cuatro gates que levantan el CMS de verdad —`usync-rebuild-check`, `humo-portada`,
  `humo-conectado`, `ssr-dom-check`— arrancan con ese perfil contra una base desechable, así que
  pasan por la instalación desatendida. Escribirles la credencial a cada uno habría metido cuatro
  literales nuevos el mismo día que se quitó uno: va en **un** sitio
  (`tools/credencial-de-prueba.mjs`) y es **aleatoria por corrida**, porque «pero es sólo para los
  tests» es exactamente el argumento que dejó la de producción en el repo.

- `feedback_a_precaution_you_did_not_measure_is_a_claim_you_should_not_make` — **una precaución
  copiada de otro gate se escribe en su `<remarks>` como si estuviera haciendo algo, y la mitad de
  las veces no hace nada: medirla cuesta una mutación, y no medirla deja el fichero AFIRMANDO DE
  MÁS** (#159). Los dos gates nuevos decían «se descartan los comentarios antes de parsear, y hace
  falta: la explicación del defecto nombra la clave prohibida». Medido quitando esa línea: **verdes
  los dos**. Y no por suerte — el regex está **anclado** al principio de la línea recortada y un
  comentario de YAML empieza por `#`, así que no puede casar nunca. La precaución es correcta y es
  seguro barato; la **frase** era falsa, que es peor que no decir nada:
  `feedback_docs_written_ahead_of_the_code_are_a_defect_with_a_clean_face` en el `<remarks>` de un
  gate en vez de en el de una pieza. Donde el descarte SÍ es load-bearing es en un gate que busque
  una **subcadena en cualquier parte** del fichero (`TransitoriedadTests`, el del `.editorconfig`
  del #151): ahí la prosa dispara el gate. La regla: **cada cláusula del `<remarks>` que afirme una
  propiedad tiene su mutación, o se escribe como «seguro barato, hoy no cambia el resultado».**
  **Y el hermano operativo, del mismo ticket: una red de seguridad por ENCIMA de lo medido tapa la
  comprobación.** El umbral se puso en «≥ 8 claves» por si crecían, y hoy son **6**: la red saltaba
  primero y la mutación no llegaba nunca al cruce que se quería probar — la regla 25(d) del repo
  hermano («si el gate tiene varios rechazos en cadena, la mutación tiene que LLEGAR al que se
  quiere probar»), cometida en la primera corrida del gate. El umbral va **debajo** de lo medido.

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

### CUATRO soluciones, y cuál abrir (#135)

| solución | proyectos | para qué |
|---|---:|---|
| `Synergos.Web.sln` | 5 | el producto web y sus tests. **No arrastra el backend.** |
| `Synergos.Apis.sln` | 22 | `Core` + `Shared` + las veinte capacidades |
| `Synergos.Bff.sln` | 7 | `Core` + `Shared` + `Bff.Core` + los cuatro orquestadores |
| `Synergos.CMS.sln` | 34 | **la integradora** — lo lanza todo junto, y es la que corre CI |

**La integradora conserva el nombre `Synergos.CMS.sln` a propósito**: es el que nombran el
workflow de CI, los gates que suben buscando la raíz del repo y esta guía. Renombrarla habría
sido el cambio más visible y el menos útil. Dentro va agrupada en cinco **carpetas de solución**
(Producto web · Núcleo compartido · Capacidades · Orquestadores · Tests del backend), que
reflejan el árbol del disco desde el #136.

**Y el disco SÍ se movió** (#136): el backend vive en
`backend/{nucleo,capacidades,orquestadores}/` y el producto web se queda en la raíz. Lo que eso
compra sobre las carpetas virtuales es que **la disposición deja de ser una convención**: hoy se
ve en un `ls` en qué árbol estás, que es lo primero que §0 pide antes de tocar nada. Costó ~153
sitios con ruta cableada, y el arreglo **no fue reescribir 153 literales**: ver la nota de §2
sobre `Proyectos` — los gates pasaron a preguntar por NOMBRE, así que la próxima reorganización
cuesta cero.

**`Synergos.Servicios.Tests` vive SÓLO en la integradora, y no es un olvido**: referencia
capacidades Y orquestadores, así que meterlo en una de las dos parciales arrastraría el otro
grupo. Hay gate, y es la regla de #133 generalizada: **toda solución es CERRADA bajo
`ProjectReference`** —si lista un proyecto, lista aquello a lo que apunta— y las tres parciales
son **estrictamente menores** que la integradora, para que la salida barata (meterlo todo en
las cuatro) no pase. Mutado en los dos sentidos antes de darlo por bueno.

```bash
# TODO el árbol compila en CERO avisos, y desde el #134 un aviso ES un error
# (TreatWarningsAsErrors en Directory.Build.props). Si tu SDK saca uno que acá
# no sale, NO lo ignores: entra a NoWarn con su razón al lado, como 1591/1573.
dotnet build Synergos.CMS.Application/Synergos.CMS.Application.csproj -v quiet

# Web compila clean (solo MSB3021 file-lock esperados si Web corre):
dotnet build Synergos.CMS.Web/Synergos.CMS.Web.csproj -v quiet --no-dependencies

# Las tres suites (3310 tests) — la solución integradora las lanza juntas:
dotnet test Synergos.CMS.sln -v quiet

# …o una sola, que es lo que hace el corte del #135 útil en el día a día:
dotnet test Synergos.CMS.Tests/Synergos.CMS.Tests.csproj -v quiet           # 2252
dotnet test Synergos.Servicios.Tests/Synergos.Servicios.Tests.csproj -v quiet  # 633
dotnet test Synergos.Arquitectura.Tests/Synergos.Arquitectura.Tests.csproj -v quiet  # 425

> **Y ojo con lo que este comando NO ve** (#133). `dotnet build` resuelve un
> `ProjectReference` **por RUTA**, no por pertenencia a la solución, así que compila
> transitivamente lo que la `.sln` no lista y sale verde igual. La solución llegó a
> declarar **23** proyectos con **32** en disco: los nueve que faltaban eran
> capacidades que el proyecto de tests referencia, y **Visual Studio daba nueve
> NU1105** mientras la CLI decía que todo bien. Es la forma de #126 y #92 con los
> papeles cambiados —acá el que ve es el IDE y el que tapa es la CLI—. Hay gate
> (`SolucionCompletaTests`), y cruza en los dos sentidos.

# LOS GATES DE ARQUITECTURA — corren solos dentro de la suite, pero
# conviene correrlos aparte al tocar el árbol de servicios:
dotnet test Synergos.CMS.Tests/Synergos.CMS.Tests.csproj --filter "FullyQualifiedName~Architecture"

# Una capacidad o un orquestador, sueltos:
dotnet build backend/capacidades/Synergos.Api.Booking/Synergos.Api.Booking.csproj -v quiet
dotnet build backend/orquestadores/Synergos.Bff.Tienda/Synergos.Bff.Tienda.csproj -v quiet

# uSync Import: lo hace el arquitecto manualmente desde backoffice —
# agente NO ejecuta import desde CLI ni toca la DB.
```

### Los dos gates que ARRANCAN la aplicación — necesitan el SDK, y valen lo que tardan

```bash
node tools/usync-rebuild-check.mjs   # ADR 0128: base vacía + XML = entorno completo (~2 min)
node tools/humo-portada.mjs          # #119: base vacía + XML + siembra = portada SERVIDA (~3 min)
node tools/humo-conectado.mjs        # …y CONECTADA al CDN del repo hermano (~3 min)
```

**El tercero es el único que mira los DOS árboles juntos**, y mira la propiedad
que ninguno de los otros puede: que la página traiga lo que hace falta para que
el CDN **hidrate** lo que el SSR pintó. `humo-portada` para justo antes — con el
registry en `Stub` la página sale con sus `<synergos-*>` y sin un solo
`<script>`, que es exactamente como se ve una que funciona y una que no.

Levanta él solo un estático sobre `../Synergos.UI/public`, así que no hace falta
montar nada aparte. **Y corre en CI** (`humo-conectado.yml`), que hace checkout
del hermano, lo **construye** —`public/` es salida de build, no basta con
clonarlo— y pide la página. **Sin filtro `paths:` a propósito**: lo que este gate
lee es «qué HTML sale por la puerta», y eso lo rompe una vista, un composer, un
cliente del registry, el schema o un `appsettings`; un filtro que intentara
enumerarlo se equivocaría **en silencio**, que es el defecto #128. El precio del
otro lado ya está medido dos veces: diez días con toda página de contenido en 500
(#92) y el sitio entero sin hidratar (#126), las dos con la suite en verde.

Sus cuatro dientes, y qué caza cada uno:

| | |
|---|---|
| hay `<script type="importmap">` | sin él **nada hidrata** — 200, el SSR entero y muerto (#126) |
| resuelve hacia **cada** framework del registry | con el mapa de uno solo, medio sitio hidrata y la página se ve igual de bien |
| cada `<synergos-*>` tiene su `<script type="module">` | un tag sin bundle es un hueco que el SSR disimula |
| cada bundle contesta 200 | un `<script>` a un 404 se ve en el HTML igual que uno bueno |

> **Y la causa más fácil de cometer es una variable de entorno.**
> `SYNERGOS_CDN_MODE` / `SYNERGOS_CDN_URL` **sólo existen dentro de
> `compose.yml`**, que las traduce; un `dotnet run` las ignora en silencio. Las
> claves de verdad son `Synergos__BundleRegistry__Mode` y
> `Synergos__BundleRegistry__PublicBaseUrl`. Medido con la misma portada: **21.595
> bytes con import map de 23 entradas contra 19.785 SIN import map**. El repo
> hermano las documentaba mal en dos sitios y se corrigió.
>
> **El otro tropiezo, y cuesta tres corridas averiguarlo**: sembrar la portada
> ANTES de que el import de uSync termine (`uSync: Startup Complete`) la crea con
> los DataTypes a medias, un desplegable guarda una cadena plana y **toda página
> de contenido contesta 500** — con una traza que apunta a la vista y no al orden
> de arranque.

> **Y en `Development` no hace falta ninguna variable** (#132). El default de
> `Synergos:BundleRegistry` es `FileSystem` contra `../../Synergos.UI/public`
> —relativa, resuelta contra la raíz de CONTENIDO y no contra el directorio de
> trabajo—, así que con el hermano construido al lado el sitio hidrata solo:
> medido, 21 577 bytes con import map de 23 entradas y sus dos
> `<script type="module">`. Antes decía `C:\LOCAL_CDN`, que existe en una sola
> máquina, y eso **no fallaba**: servía 19 785 bytes sin mapa y sin un script — o
> sea idéntica a la buena, y muerta. Hoy `Mode=FileSystem` sin carpeta (o con una
> carpeta sin `synergos/registry.json`) **lanza al cablear** y el mensaje nombra
> las dos salidas; para levantar el CMS sin CDN,
> `Synergos:BundleRegistry:Mode=Stub`.

**De dos clones a una portada que hidrata** está escrito paso a paso en
`Synergos.CMS.Web/docs/onboarding/arrancar-los-dos-arboles.md`.

**El segundo es el único del repo que PIDE LA PÁGINA**, y por eso existe: las vistas
de este proyecto se compilan **siempre en caliente** —`RazorCompileOnBuild=false`,
obligado por `ModelsMode=InMemoryAuto`— así que **un `dotnet build` verde no dice
nada sobre si una vista compila**. Lo comprobado: el arreglo del defecto #92 dejó
`_SynergosBridge.cshtml` sin el `@using` de `Microsoft.Extensions.Logging` y toda
página de contenido pasó a contestar 500 durante diez días, con la suite entera en
verde. Los dos aceptan `--no-build` si ya compilaste, y **avisan** de los `.cshtml`
que el import ensucia en vez de restaurarlos.

### Los gates de Node — corren sin SDK .NET

Los cuatro son los mismos que gatean los PRs. No necesitan `dotnet`, así que
un agente en un contenedor sin SDK **sí puede** verificarlos:

```bash
node tools/usync-audit.mjs        # 11 checks de schema uSync
node tools/check-css-parity.mjs   # G-3: toda clase syn-* emitida tiene CSS
node tools/spec-valida.mjs --autoprueba   # G-8: el LECTOR del spec, ejecutado (#140)
(cd Synergos.CMS.Web/docs/contracts/tests && npm ci && npm test)  # contratos
```

> **G-8 necesita al hermano CLONADO, no construido**: lo único que lee de allá es
> `element-registry.json`, que está versionado. Y si no lo encuentra **no pasa en silencio** —
> rechaza diciendo que no pudo comprobar los elementos del spec:
> `node tools/spec-valida.mjs --ui-path=/tmp/ui`. La línea base del oráculo se regenera con
> `--actualizar` y el diff va en el commit que lo causó.

**Y DOS más que sí necesitan al hermano, pero aceptan su ruta** (G-6 y G-7, #102):

```bash
node tools/contract-keys.mjs  --ui-path=/tmp/ui   # lo que el borde EMITE  ↔ lo que la app LEE
node tools/contract-bodies.mjs --ui-path=/tmp/ui  # lo que la app MANDA   ↔ lo que el borde DECLARA
```

**G-7 es el que mira donde de verdad dolió.** En los ocho verticales auditados (#102 a #105)
lo caro estuvo SIEMPRE en los cuerpos de petición: un `planId` que el record no declaraba y
cobraba el plan equivocado, un `slot: {date,time}` contra un `string` que daba 400 siempre,
`lat`/`lng` planos que publicaban un inmueble en (0,0), la dirección de entrega descartada,
seis rutas de EHR en 400 permanente, y la calle del inmueble reemplazada por «{barrio},
{ciudad}» (#110). Ninguno fallaba a la vista: System.Text.Json descarta en silencio lo que no
mapea, y el `catch` del cliente inventa el acuse.

**El último es el peor de la lista y por eso está el último**: los otros dejan un hueco —un
pin en (0,0), un 400— y un hueco alguien lo reporta. Ése dejaba una dirección PLAUSIBLE
donde había una calle, así que la ficha se veía bien y el comprador tocaba el timbre a tres
cuadras. Un campo derivado que pisa uno recibido no se detecta mirando la pantalla.

Por eso G-7 es **error y no trinquete**: una clave que se manda a una ruta y cuyo record no
la declara no tiene lectura inocente. Hoy ligan 57 claves en 22 rutas.

> **Lo que G-7 no ve, y lo dice al correr**: los cuerpos que construye una función
> (`postJson(url, toCourseDraftWire(body))`) quedan fuera, porque seguirla exige resolver su
> return. Los lista en cada corrida en vez de contarlos como cubiertos.

> **Y lo que G-6 y G-7 no miran NINGUNO de los dos: el salto BFF↔capacidad.** Los dos cruzan
> el CMS con el UI. Un `Synergos.Bff.*/Clients/*Dtos.cs` que no declara lo que la capacidad
> emite tiene la forma exacta de G-7 y se escapa entero — es lo que pasó en el **#122**, donde
> `QuoteDto` no declaraba `Lines` y el pedido se guardaba con `UnitPrice: 0` en cada renglón
> con el dato ya cruzado el cable.
>
> **Se midió si el mismo cruce se puede aplicar acá, y la respuesta es distinta según la
> dirección. Va escrita porque es el segundo defecto que entra por este hueco.**
>
> **La dirección de la RESPUESTA —lo que la capacidad emite ↔ lo que el DTO declara— no
> admite gate, y no por falta de mapeo.** El mapeo es lo BARATO acá: `Post<QuoteDto>(Pricing,
> "v1/quotes", …)` nombra la ruta, y el `Endpoints/` de la capacidad la liga a su
> `QuoteResponse` — o sea que se deriva por ruta, como `CapacidadesConectadasTests`, sin la
> tabla a mano que a G-6 le costó un verde falso. Lo que no admite gate es el **criterio**:
> un DTO de orquestador declara *sólo lo que usa* a propósito, y medido sobre `Bff.Tienda` eso
> son **33 campos omitidos a conciencia** (4 de `CartResponse`, 1 de `QuoteResponse`, 3 de
> `StockItemResponse`, 4 de `StockHoldResponse`, 5 de `OrderResponse`, 9 de `PaymentResponse`
> y 7 de `ShipmentResponse`), con el defecto siendo **uno** de los 34. Exigir que se declare
> todo son 33 exenciones en un solo orquestador — el muro de excepciones que deja de leerse—.
> Y un **trinquete** contra una línea base es peor, no mejor: se pondría rojo el día que una
> capacidad **agrega** un campo, que es el caso legítimo que el diseño busca, y se quedaría
> verde sobre #122, que estaba en la línea base desde el primer día. **Un trinquete que
> distingue al revés que el defecto no es un gate barato: es uno que enseña a ignorarlo.**
>
> **La dirección de la PETICIÓN —lo que el BFF manda ↔ lo que el `*Request` declara— sí
> admite gate**, y es la de G-7 tal cual: ahí no hay suelo de ruido, porque toda clave que se
> manda y no se declara la tira `System.Text.Json` en silencio. **Hoy no se escribe porque no
> hay qué cazar**: son **diez cuerpos distintos** en los cuatro orquestadores —`/v1/quotes`,
> `/v1/orders`, `/v1/payments`, `…/refund`, `/v1/holds`, `/v1/items/{id}/holds`,
> `…/adjust`, `/v1/shipments`, `/v1/grants/check` y los `null` de los POST sin cuerpo— y se
> cruzaron **a mano, uno por uno, contra su record**: ligan los diez. Escribir un parser de
> objetos anónimos anidados para vigilar diez llamadas que ya se leyeron es la abstracción
> prematura de §6.
>
> **Los dos disparadores, para que esto no se relea como «se decidió que no»:**
> **(a)** el día que aparezca el PRIMER descuadre de petición —una clave mandada que ningún
> `*Request` declara—, se escribe el gate en vez de arreglar la llamada sola; **(b)** el día
> que exista el quinto orquestador (Realty, Gob, Academy o Social), porque cruzar a mano deja
> de ser fiable mucho antes de volverse imposible, y eso no se nota: se nota cuando alguien ya
> confió en el cruce que no hizo.
>
> **Y lo que de verdad cazaba #122 no es ninguno de los dos cruces: es la FABRICACIÓN.**
> Omitir `Lines` era inocente hasta que alguien tuvo que rellenar el hueco con
> `Money.Zero`. La pregunta que lo encuentra —y que se hace leyendo, no corriendo— es
> **¿este campo lo estoy inventando porque el DTO de al lado no lo trae?**; ver
> `feedback_an_omission_becomes_a_defect_when_someone_fills_it` en §5.

Y el de las claves de respuesta:

```bash
node tools/contract-keys.mjs --ui-path=/tmp/ui   # o SYNERGOS_UI_PATH
```

Cruza **la forma del JSON** de cada controller contra las claves que lee su app del
catálogo — la superficie que no miraba nadie, y por la que doce claves de Academy se
desviaron sin que nada se pusiera rojo: el normalizador del cliente es defensivo, así
que una clave que falta **degrada en silencio** — y a veces ni siquiera degrada:
**afirma**. Ver `feedback_an_omitted_key_can_be_an_assertion` en §5. Es un **trinquete** contra
`tools/contract-keys.baseline.json`, como el presupuesto de tamaño del repo hermano: no
falla por deuda vieja, sólo el día que algo que hoy cruza deja de cruzar. Se regenera
con `--actualizar` y el diff va en el commit que lo causó.

> **Cruza por NOMBRE DE CLAVE, no por tipo**, y está dicho para no mentir sobre su
> alcance — igual que el cruce de `window.synergos` de §3. Un `string` que pasa a
> `number` bajo la misma clave sigue pasando por aquí.
>
> **Y cruza por CONTROLLER ENTERO, no por endpoint** — que es el límite que más
> conviene saber. Si `title` ya sale de `/products`, que FALTE en `/wishlist` no se
> ve: la clave sigue cruzando. Se midió contra el arreglo de #104, donde la wishlist
> emitía `itemRef`/`owner` mientras la UI leía `productId`/`title` —una lista vacía
> con el servidor lleno— y **este gate no lo habría cazado**. Afinarlo exigiría seguir
> el tipo de retorno de cada acción hasta su `record`; se puede hacer y es otro
> trabajo. Queda escrito porque un gate que se cree más listo de lo que es es peor que
> no tenerlo: alguien deja de mirar confiando en él.
>
> **La tabla app↔controller es a mano y por eso lleva guarda.** El vínculo no está
> escrito en ningún sitio del que se pueda deducir, así que la lista es inevitable —
> pero una lista mal escrita congela un verde falso para siempre, y ya pasó al
> escribirlo: `storefront` apuntaba a `ShopController` (el del carrito, 110 líneas) en
> vez de a `ShopCatalogController` (1372), y cruzaba **3 claves de 94**. La guarda
> rechaza un vertical que cruce menos de una de cada cinco, **y corre también al
> regenerar la línea base** — salir antes dejaba abierto justo el camino por el que
> entra el error, porque `--actualizar` es lo que uno teclea cuando el gate se queja.

El otro cross-repo valida las dos mitades del acople, así que
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

> ⚠️ **Y esta frase estuvo MINTIENDO, que es peor que el rojo.** `validate-cms-contracts.mjs`
> salía con **exit 1** y dos errores `[E2]` —`elementCourseLesson` y `elementCourseModule`—
> desde la HU #100, mientras acá decía «los dos pasan hoy». Un element type nuevo que no monta
> web component tiene que anotarse en `CMS_INTERNAL_ALIASES`, **una lista que vive en el repo
> HERMANO** (`Synergos.UI/tools/validate-cms-contracts.mjs`); es el paso 7b del molde
> (doc 12), y nadie fue.
>
> **La forma del defecto, para reconocerla:** un paso del molde que se cumple en el OTRO repo
> no lo recuerda nadie, y una guía que afirma verde apaga la única señal que quedaba. Lo
> destapó **correr el gate**, no leerlo — que es lo mismo que enseñó el `| jq length` del CDN
> más abajo en esta sección.

Son **deuda conocida y declarada**: el propio `design-gates.yml` explica que
con la bandera estricta puesta ese job llevaba rojo en `master` cuatro
corridas seguidas, y que un gate siempre rojo deja de leerse.

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

**Construido y verificado:** 20 capacidades (137 endpoints, 242 códigos
de rechazo), `Bff.Core`, `Bff.Salud`, `Bff.Tienda`, `Bff.Eventos`, `Bff.Viajes`. 3310 tests, gates de
segregación y molde en verde.

> **Los 242 se cuentan, y el criterio es parte de la cifra** (#52). Decía **195**
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
> serían 240». Pero hay **tres más que llevan el prefijo de quien llama**:
> `{prefijo}.idempotency_key_required`, que emiten las **19** capacidades que
> exigen la cabecera; `{prefijo}.access_requires_identity`, que emite quien
> deje la afirmación sin declarar —hoy `Api.Audit` y `Api.Consent`;
> `Api.Messaging` declara el suyo y `Api.Workflow` no puede llegar ahí—; y
> `{prefijo}.store_busy` (#112), que emite el turno de escritura de las
> diecinueve que guardan por `JsonCollectionStore` cuando la réplica de al lado
> no soltó el turno a tiempo. Sumarlo
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

> **Y desde el #150 el despliegue exige DOS secretos más: `SYNERGOS_ADMIN_EMAIL` y
> `SYNERGOS_ADMIN_PASSWORD`.** Antes la cuenta de administrador del backoffice se
> creaba con el literal de `appsettings.Docker.json` —el perfil con el que corre
> producción— publicado en un repo público. Hoy `compose.prod.yml` las pide con
> `:?`, así que **`docker compose up` falla antes de arrancar un contenedor** si
> faltan; y el `UnattendedUserName` se queda en el perfil a propósito, porque con
> las tres ausentes Umbraco instala igual y deja un administrador al que nadie
> puede entrar. Hay gate (`PerfilDeProduccionTests`). Si alguna vez se desplegó
> con el literal, **rotarla es lo primero**.

> **Arrancar no es estar listo, y eso faltaba escrito** (#114). Un servidor con
> los 26 contenedores sanos no sirve todavía: hay que sembrar **el schema** y
> **el estado que las capacidades exigen**, y ninguna de las dos cosas la hace
> el arranque. Los dos pasos están en `docs/despliegue/00-montar-el-entorno.md`
> §5.bis, con una herramienta cada uno — `tools/importar-schema.sh` y
> `tools/provisionar.sh --verificar` / `tools/provisionar.sh` — y los dos son
> idempotentes.
>
> **El schema NO se importa al arrancar, y cuatro documentos decían que sí.**
> ADR 0008 deja `ImportAtStartup` en `None` y nadie lo pisa; medido contra una
> base vacía, el arranque dice `uSync: Startup Complete 0ms`. Se consideró
> encenderlo y se descartó por una razón viva, no por el ADR: con
> `ContentHandler` encendido (ADR 0129), `All` **revierte en cada arranque lo
> que un editor publicó**, en silencio. Hay gate
> (`DeployPipelineTests.El_import_de_uSync_NO_ocurre_al_arrancar`), y vigila el
> HECHO y no la prosa: el día que alguien lo encienda, se pone rojo y nombra los
> dos ficheros que hay que reescribir a la vez.
>
> **Y un Umbraco vacío no falla**: sirve «No published content» con 200 y HTML
> de verdad, así que el humo lo daba por bueno y el primer despliegue de una
> máquina nueva se habría puesto **verde con el sitio en blanco**. Hoy
> `humo-publico.sh` lo distingue.
>
> **Y el CONTENIDO ya tiene camino** (#119). Esta nota decía que no lo había, y era
> cierto: `uSync/v9/Content/` sigue vacía en el repo —el agente no la autora, ADR
> 0129— y nada la crea al arrancar —ADR 0013—. Lo que faltaba era la **herramienta**
> que respeta las dos: `POST /dev/seed-portada` siembra en desarrollo una portada de
> arranque compuesta con el Layout Composer, el arquitecto la ajusta, uSync la
> exporta al guardar y el XML entra al repo por su mano. El servidor la recibe con
> `importar-schema.sh`, sin sembrar nada. Los cuatro pasos están en
> `docs/despliegue/00-montar-el-entorno.md` §5.bis.2.
>
> **Es idempotente de la forma que importa**: con portada puesta no la duplica ni la
> reescribe —contesta `AlreadyAuthored`—, porque pisar lo que el arquitecto acaba de
> ajustar, justo antes de exportarlo, es peor daño que un nodo de más.
>
> **Y hay gate que PIDE LA PÁGINA** (`tools/humo-portada.mjs`, workflow
> `humo-portada.yml`): arranca la app contra una base limpia, siembra y hace `GET /`.
> Es el único del repo que lo hace, y la primera vez que se corrió destapó que **el
> sitio entero estaba caído**: `_SynergosBridge.cshtml` usaba `LogWarning` sin su
> `@using`, y con `RazorCompileOnBuild=false` (obligado por `ModelsMode=InMemoryAuto`)
> las vistas se compilan **siempre en caliente**, donde no llegan los implicit usings
> del SDK. Diez días con toda página de contenido en 500, la suite en verde y el gate
> de #92 confirmando que la línea estaba escrita — porque **no había contenido que
> renderizar**: el hueco de la portada tapaba el de la vista.

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
  De los 13, **trece** están hechos: la tienda compra contra `Bff.Tienda`
  (`Synergos:Tienda:Mode=Bff`, HU #24), la cita clínica agenda contra
  `Bff.Salud` (`Synergos:Salud:Mode=Bff`, HU #25), la visita al inmueble
  aparta cupo **directo contra `Api.Booking`, sin orquestador**
  (`Synergos:Realty:Mode=Api`, HU #33a) — una visita no se cobra, así que
  toca una sola capacidad y un BFF sería una saga de un paso — y **el
  expediente decide contra `Api.Workflow`**, también directo
  (`Synergos:Gob:Mode=Api`, HU #44) — y **el acto administrativo se pone
  en conocimiento contra `Api.Messaging`**, por su propio interruptor
  (`Synergos:Gob:Notifications:Mode=Api`, HU #62) — y **el cobro va a
  `Api.Payments`**, por dos interruptores (`Synergos:Payments:Mode=Api`
  para el seam entero, `Synergos:Gob:Payments:Mode=Api` para la tasa de un
  trámite, #27). El default sigue siendo `Stub` —`Local` en el de
  notificaciones y en el de la tasa, `Engine` en el del seam— en todos.
  **Faltan cero**: la familia A está cableada entera.

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

  > **Y el cobro son DOS interruptores porque son dos alcances, no dos
  > capacidades** (#27, la parte que quedó viva). `Synergos:Payments:Mode=Api`
  > cambia el seam ENTERO —los ocho consumidores del motor en proceso— y **se
  > niega a cablear** si Tienda, Salud, Eventos o Viajes siguen comprando de
  > este lado: esos flujos apartan, cobran y confirman en varios pasos, y el CMS
  > no tiene dónde anotar una compensación pendiente. Con plata de verdad
  > detrás, eso es stock apartado que nadie suelta y cobros sin pedido — el
  > atajo que `ShopWiringTests` vigila en compilación, dicho ahora en tiempo de
  > arranque.
  >
  > **La tasa de un trámite no espera a eso, y por eso tiene el suyo**:
  > radicar no compone una saga —el motor decide no abortar el trámite si la
  > captura no sale—, así que es el único consumidor que puede hablarle a la
  > capacidad hoy. Es la misma forma que `Synergos:Gob:Notifications:Mode`.
  >
  > **Y por eso mismo es el único que PRESENTA identidad** (HU #14, lo que
  > quedaba). Con la llave compartida sola, cualquier servicio que pudiera
  > hablarle a `Api.Payments` escribía un cobro a nombre de quien quisiera —
  > el defecto #72 sobre el registro de quién movió plata, que es de los que
  > alguien cita en una disputa. Hoy la capacidad **verifica el token en local
  > y guarda `PaidWith`**, y ese campo **sale por `PaymentResponse`**: una
  > escritura sin camino de lectura está enterrada, no guardada (#116).
  >
  > **El `payerId` pasó a ser el `MemberKey` cuando hay sesión**, y no es
  > cosmético: la capacidad rechaza un token que nombre a otro
  > (`token_subject_mismatch`), así que el sujeto firmado tiene que ser
  > **exactamente** lo que viaja como pagador. Firmar el miembro mientras
  > viaja la huella de su correo no daría un cobro peor firmado: daría un
  > rechazo. **Sin sesión no se pide token** —el correo de un pago de invitado
  > no lo comprobó nadie, y firmarlo sería el #42 con la firma tapándolo
  > mejor— y los cobros anteriores conservan su huella y su `PaidWith` nulo,
  > que es la verdad sobre ellos.
  >
  > **Para eso hubo que ensanchar la costura**
  > (`feedback_a_seam_widens_when_it_meets_the_network`):
  > `PaymentSessionRequest` no sabía decir «quien paga es un miembro» porque
  > nació para proveedores que cobraban DENTRO del proceso, a los que no les
  > hacía falta. Lleva `PayerMemberKey`, aditivo y opcional — los otros siete
  > consumidores compilan y se comportan igual.
  >
  > **Y destapó que el generador del compose se comía la llave.**
  > `Api.Payments` tiene bloque propio (el de Wompi) y ese `return` salía
  > **antes** del fallback que reparte `IdentityTokens__Keys`, así que el
  > despliegue la habría dejado sin llave: arrancando bien y rechazando el
  > primer token que le presentaran. Es el reverso exacto del tropiezo que el
  > propio fichero ya documentaba para `Api.Workflow`. Lo cazó
  > `IdentityGateTests`, que **deriva la lista del disco** y no del generador
  > — dos derivaciones independientes que tienen que coincidir.
  >
  > Verificado con los dos procesos vivos: declarar `IdentityToken` sin
  > presentarlo se rechaza (`assertion_not_proven`); **presentándolo, la
  > capacidad sube la afirmación sola** aunque el CMS declare `CmsSession`; el
  > token de otro sujeto se rechaza (`token_subject_mismatch`); sin afirmación
  > ninguna se rechaza (`payments.access_requires_identity`); y **sin llave de
  > verificación arranca igual** —el camino del clon limpio— pero un token
  > presentado ahí se **rechaza** (`token_not_verifiable`), no se ignora. Hay
  > gate (`PaymentsIdentityTests`).
  >
  > **Y el `actionUrl` ya tiene lector.** Lo emitía `Api.Payments` desde la
  > HU #27 sin consumidor, y sin él no se cobra con checkout hospedado: la
  > transacción nace cuando el comprador la completa, así que una sesión con
  > acción pendiente no tiene nada que capturar. `HttpPaymentProvider` lo
  > traduce a `RequiresAction` + `PaymentAction.Redirect`, y la radicación
  > **no captura** en ese caso — capturar igual dejaba escrito en el
  > expediente un rechazo que nadie dio. **Lo que falta es enseñárselo a
  > quien paga**, y eso vive en el otro árbol: ninguna pantalla lee todavía
  > esa redirección.
  >
  > **Y un hallazgo de camino: la tasa pendiente no se puede LEER.** El
  > expediente guarda `PaymentStatus` desde siempre y **ninguna superficie lo
  > devuelve** —`CaseDetail` no lo declara, así que la bandeja del ciudadano y
  > la cola del funcionario no lo ven—. Con el motor en proceso daba igual,
  > porque siempre decía `Captured`; con la capacidad detrás, un
  > `Unavailable` es exactamente el caso que alguien tendría que perseguir, y
  > queda escrito donde nadie mira. Es una escritura sin camino de lectura, el
  > espejo de `feedback_no_read_without_a_write_path`. No se arregla acá: sacarlo
  > cruza el DTO del borde y la pantalla del otro árbol.
  >
  > **Lo que esa guarda NO cubre, y va dicho**: la matrícula de Educación
  > (`StubEnrollmentService`) también compone y no tiene bandera que mirar,
  > porque `Bff.Academy` no existe. Va en el orden correcto —abre la sesión,
  > guarda la matrícula pendiente y captura en un confirm aparte, que es
  > idempotente—, así que la ventana es «capturado y la activación no se
  > escribió» y se rescata repitiendo el confirm. El día que exista
  > `Bff.Academy`, su bandera entra en la lista. Hay gate
  > (`PaymentsWiringTests`).
  >
  > **Y «se rescata repitiendo el confirm» era una propiedad del motor que NADIE
  > ejercía** (#117). El asistente de compra del otro árbol no repetía nada: cuando
  > el confirm no llegaba, su cliente **fabricaba el acuse** —un `ENR-<orderRef>`— y
  > seguía adelante, así que la ventana no se cerraba nunca y además el alumno se iba
  > con un identificador que este lado no conoce. Una propiedad correcta de este
  > árbol cuya única forma de aprovecharse vive en el otro **no está hecha hasta que
  > el otro la ejerce**: hoy el asistente reconoce el cobro ya capturado, repite
  > **sólo** el confirm y dice lo que quedó. Es la misma forma que el addendum #111
  > de `feedback_gethashcode_is_not_a_seed` —el arreglo cruza los dos árboles y hay
  > que decirlo—, y por eso se anota acá aunque no haya cambiado una línea de C#.

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
- **Los dos bordes ya tienen transporte real, y a los dos les falta la
  credencial.** `Api.Notifications` sale por Resend (ADR 0131) y
  `Api.Payments` cobra por **Wompi** (HU #27) — se eligió porque en Colombia
  PSE y Nequi son la mitad de los pagos, así que una pasarela sin ellos no
  cobra. Sigue distinguiendo **rechazado** (no se reintenta) de **caído**
  (sí) de **sin configurar**, y con un nombre de proveedor puesto rechaza a
  gritos en vez de caer al stub en silencio. Lo que falta **no es código**:
  son las llaves y **una corrida contra el sandbox**, que los `<remarks>` del
  adaptador declaran pendiente y que se puede hacer con llaves de prueba
  antes de la cuenta comercial. Hasta entonces ningún demo de venta corre de
  punta a punta con Wompi puesto.

  > **La pregunta que #27 tenía abierta no era «qué proveedor»: era QUIÉN
  > COBRA**, y está decidida — **`Api.Payments` es lo único que mueve plata de
  > verdad**. La razón es la tercera pregunta del repo, *¿quién tiene la
  > plata?*, y no es hipotética: con `Tienda:Mode=Bff` la tiene el orquestador,
  > y dejar las dos mitades cobrando fue exactamente el defecto **#57** —el RMA
  > le pedía el reembolso al proveedor local con el identificador de la saga,
  > que ese proveedor no conocía, y el caso no llegaba nunca a reembolsado sin
  > que nada fallara—.
  >
  > **El `WompiPaymentProvider` del CMS NO se borra** (ADR 0116, con router por
  > reglas y dos sinks de confirmación asíncrona): sigue sirviendo el camino en
  > proceso —`Tienda:Mode=Stub`—, que es el que permite levantar el repo entero
  > sin ningún servicio. Lo que cambia es su papel. **Y los dos no pueden
  > cobrar de verdad a la vez**: un despliegue con `Tienda:Mode=Bff` y llaves
  > reales de Wompi del lado del CMS **falla al arrancar** en vez de cobrar en
  > silencio por dos plomerías distintas. Es la forma de #56 y la de la llave
  > de firma de `Api.Identity` — arrancar verde, contestar `/health` y reventar
  > cuando una persona intenta pagar es el peor de los tres. Hay gate
  > (`PaymentProviderGateTests`, `PaymentEngineCoexistenceTests`).
  >
  > **El portarlo destapó que la costura era síncrona.** Los dos únicos
  > proveedores que había escribían en un log; una pasarela vive al otro lado
  > de la red. La salida que NO se tomó es `.Result` dentro del proveedor: el
  > cerrojo de `PaymentService` rodea la llamada entera, así que cada cobro en
  > vuelo se habría quedado con un hilo del pool durante todo el viaje, y eso no
  > se lee como un problema de pagos sino como que «el servicio se puso lento».
  > El `lock` es hoy un `SemaphoreSlim` —`await` no cabe en un `lock`, y sacar
  > la llamada fuera del cerrojo rompería lo que `CheckRefundable` protege— y
  > el atajo está cerrado por gate.
  >
  > **Y «autorizar» con checkout hospedado no es «reservar cupo».** Wompi Web
  > Checkout cubre tarjeta, PSE, Nequi y efectivo con un solo flujo y deja los
  > datos de tarjeta fuera de nuestros servidores, pero la transacción **nace
  > cuando el comprador la completa**: autorizar es firmar la intención, y
  > quien constata que la plata se movió es capturar, que contesta
  > *transitorio* mientras la transacción no exista o siga `PENDING`. Eso
  > encaja con la máquina de sagas sin tocarla —capturar se reintenta; al
  > rendirse, se libera una intención que nadie pagó—. **El `actionUrl` ya lo
  > lee alguien**: `HttpPaymentProvider` lo traduce a `RequiresAction` +
  > `PaymentAction.Redirect` y con eso el CMS deja de capturar una intención
  > que nadie completó (#27). **Lo que sigue faltando es enseñárselo a quien
  > paga**, y eso vive en el otro árbol: ninguna pantalla lee todavía esa
  > redirección.
  >
  > **Y el desenlace también llega solo, por `POST /v1/webhooks/wompi`**, que es
  > el segundo endpoint del árbol de servicios fuera de la llave compartida
  > —quien lo llama es un tercero que no la tiene— y lo único que lo protege es
  > la firma: sin verificarla, cualquiera que sepa la URL marca un cobro como
  > pagado y el pedido sale. **No lleva llave de idempotencia y no la
  > necesita**: quien llama es el proveedor y no hay cabecera que exigirle, así
  > que lo que hace de llave es el estado —sólo se avanza desde `Authorized`—,
  > y eso es además lo que impide que un reenvío tardío retroceda un cobro ya
  > capturado. Hay gate (`WebhookGateTests`), y **vigila las dos capacidades
  > que reciben eventos**: mide que el verificador esté ENCHUFADO y no que
  > exista, que es justo lo que `Api.Notifications` descubrió mutando el suyo
  > —quitó la llamada del lambda y no falló ni un test—.
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
- **Las copias YA salen del servidor, cifradas, y hay un ensayo que las
  restaura de verdad** (HU #31). `tools/respaldo.sh` copia en frío —la lista
  sale del compose, no de una lista a mano—, `tools/enviar-respaldo.sh` las
  cifra con `age` y las sube a un remoto de `rclone`, `tools/restaurar.sh`
  las devuelve exigiendo `--si-estoy-seguro`, y `tools/prueba-restauracion.sh`
  las trae del remoto, las descifra, las restaura sobre un proyecto APARTE y
  **le pide los datos de vuelta por HTTP**. Hay gate (`RespaldoTests`), y el
  cron lo pone `bootstrap-servidor.sh`.

  > **Escribir la copia y leerla son dos trabajos, y el segundo es el que
  > vale.** Se midió levantando `Api.Audit` sobre un almacén ilegible:
  > **`/health` contesta 200 y la lectura 500** — o sea que un humo de salud
  > da por buena una restauración inservible. Es el defecto #82 tal cual, y
  > ningún `tar -t` ni ningún `sha256` lo habría visto, porque los bytes
  > estaban perfectos. Y hay un escalón más: un almacén que se lee A MEDIAS
  > contesta **200 con menos registros**, porque el conversor es tolerante,
  > así que el ensayo no mira el código de estado sino que exige que
  > **vuelvan datos**. Una instalación nueva contesta 200 a las dieciocho
  > colecciones.

  > **El respaldo del producto no llevaba el producto, y no fallaba.** El
  > filtro era `grep -E -- '-data$'`, que INCLUYE: copiaba `caddy-data`
  > —certificados que Caddy vuelve a pedir solos— y dejaba fuera `cms-db`
  > (la base de Umbraco), `cms-media` (la biblioteca), `cms-appdata` y
  > `cms-dpkeys`. Sin ese último no se puede descifrar la llave de firma de
  > los diplomas al restaurar, y el propio código avisa de que «los
  > certificados ya emitidos dejarán de verificar». Hoy el default es
  > INCLUIR y lo que se excluye se nombra con su razón: **con un filtro que
  > incluye, el volumen que nadie recordó se pierde en silencio; con uno que
  > excluye, se copia de más.** El gate cruza la lista de exclusiones contra
  > el compose. Y los tres scripts del servidor **tampoco llegaban al
  > servidor**: el despliegue copiaba sólo `deploy-remoto.sh`, así que la
  > máquina que había que proteger era la única sin con qué respaldarse.

  > **Cifrado ASIMÉTRICO, y la mitad que importa es cuál llave NO está
  > acá.** El servidor tiene la pública: puede escribir respaldos y **no
  > puede leer los que ya mandó**. Con una contraseña simétrica, quien se
  > lleva la máquina se lleva el histórico entero, que es bastante más que
  > lo que hay vivo en ella en ese momento. **No hay modo «sin cifrar»** —el
  > modo sin cifrar es el que alguien deja puesto «por ahora»— y por eso
  > `enviar-respaldo.sh` falla **antes de mover un byte** si le falta la
  > llave o el destino: es la forma del #56 y la de la llave de firma de
  > `Api.Identity`. Lo que NO falla es no estar configurado en absoluto: ahí
  > `respaldo.sh` dice a gritos que lo que dejó **no es un respaldo**, que
  > es el otro modo de fallar bien. El precio de la asimetría está dicho: si
  > se pierde la identidad los respaldos son ruido, y **uno ilegible y uno
  > que no existe se ven igual desde el bucket**. Lo único que distingue los
  > dos casos es correr el ensayo.

  > **La retención está escrita y no admite «para siempre»**: 30 días fuera,
  > 7 en el servidor, y un piso de 3 copias que sobreviven pase lo que pase.
  > El `0` se **rechaza** — guardar indefinidamente un archivo con los datos
  > personales de todo el producto es una decisión que nadie tomó. Se poda
  > **después** de comprobar que la de hoy llegó (podar antes es cómo se
  > acaba con cero copias la noche en que el envío falla), la edad sale del
  > sello del NOMBRE y no de la fecha del objeto remoto (copiar un bucket a
  > otro le pone a todo la fecha de hoy), y el piso de 3 es lo que impide
  > que un cron roto durante un mes se lleve por delante la última copia
  > buena, en silencio.

  > **Lo que el respaldo NO lleva, y es deliberado: el `.env` del
  > servidor.** Meter ahí la llave compartida, la de identidad, la de la
  > pasarela y la del correo convertiría la copia en el objeto más valioso
  > del producto y a la identidad de `age` en la llave de todo — custodia de
  > secretos disfrazada de copia de datos. Casi todo eso se regenera; **uno
  > no**: `Synergos:Academy:CertificateSigningSecret`, sin el cual los
  > diplomas ya emitidos dejan de verificar. Va donde va la identidad.
  >
  > **Lo que queda es del arquitecto y no es código**: qué proveedor y qué
  > bucket, y crear la llave. Ver `docs/despliegue/00-montar-el-entorno.md`
  > §6.
- **19 capacidades sobre fichero JSON, y ya aguanta una segunda réplica**
  (#112). Esta línea decía «una sola instancia por capacidad; dos réplicas se
  pisan… es la primera razón para cambiar de almacén», y llevaba olas
  diciéndolo. Lo que se pisaba no era «un documento»: `Put` reescribía el mapa
  ENTERO desde el caché de su proceso, así que **la réplica que escribía
  segunda borraba la colección de la primera** — sin excepción y sin log, y eso
  incluía las sagas de `Bff.Core`, que es el único sitio del repo que ya estaba
  **diseñado** para dos réplicas (`FileSystemSagaLease`, #34). Hoy el almacén es
  **un fichero por documento, sin caché de colección**, que es lo que el árbol
  del CMS ya había aprendido en `FileSystemJsonEntityStore`; y lo que ningún
  almacén podía arreglar solo —leer-decidir-escribir el MISMO documento desde
  dos procesos— lo cierra `StoreWriteGate`, que sube a proceso cruzado el
  `lock (_gate)` de capacidad entera que los dieciocho servicios ya tenían. Hay
  gates (`AlmacenPorDocumentoTests`, `TurnoDeEscrituraTests`,
  `TurnoDeEscrituraWiringTests`).

  > **Lo que esto NO convierte en una base de datos.** Sigue siendo un
  > fichero por documento, así que (a) **listar una colección cuesta N
  > lecturas** —no hay índice, y `Where` recorre el directorio entero—, y
  > (b) **un escritor a la vez por capacidad**, que era ya la regla en
  > proceso y ahora lo es entre réplicas. Los dos disparadores están
  > escritos y son medibles: el día que una colección crezca hasta que
  > listar duela, o el día que una capacidad necesite dos escrituras de
  > verdad simultáneas, **ése** es el momento de cambiar de almacén — y no
  > antes, porque hasta ahí el coste es una tecnología entera a cambio de
  > nada. `Api.Sessions` es la única sin turno, y no es olvido: su almacén
  > AÑADE líneas a un fichero por día y nunca lee-modifica-escribe, que es
  > el único patrón que ya era seguro entre réplicas.
  >
  > **Y el turno vive en el BORDE, no en cada servicio**, que es la
  > decisión discutible y por eso está escrita: hay 59 `lock (_gate)` en
  > dieciocho servicios y ponerles el rechazo por ocupación a los 59 es
  > donde se olvida uno. El precio es que sólo cubre lo que entra por HTTP.
  > Hoy alcanza porque **ningún GET de las veinte escribe** —comprobado
  > recorriendo los 49, y hay gate— y ninguna capacidad tiene un proceso de
  > fondo que guarde por `JsonCollectionStore`. El día que una lo tenga,
  > esto no la cubre.
  >
  > **Lo anterior se migra solo y el fichero viejo NO se borra.** Son las
  > mismas entidades cambiadas de sitio, así que no hay nada que inventar
  > —la diferencia con lo que #44 decidió sobre los expedientes—, y se deja
  > el `{nombre}.json` donde está porque el despliegue tiene vuelta atrás
  > automática (ADR 0133): una versión anterior que arrancara sin él
  > serviría la capacidad **vacía**, en silencio.
  > **Y lo que esa vuelta atrás LEE queda dicho, porque media migración
  > entendida es peor que ninguna**: la versión anterior lee el
  > `{nombre}.json` tal como lo dejó el momento de migrar, o sea **sin lo
  > escrito después**. La vuelta atrás recupera el servicio, no los datos de
  > mientras — y eso es una decisión, no un descuido: escribir en los dos
  > formatos a la vez habría dejado dos verdades sobre el mismo documento y
  > ninguna forma de saber cuál gana.
  >
  > **El objetivo es ACTIVO-ACTIVO, y por eso la respuesta no fue un
  > interruptor de «sólo una escribe».** Dos réplicas sirven a la vez y las
  > dos escriben; lo que se serializa es el turno, no el servicio. Con
  > activo-pasivo habría bastado con que la pasiva no aceptara escrituras
  > —más barato— y habría dejado sin resolver el caso que de verdad duele:
  > **una réplica que se está apagando y otra que ya arrancó**, que es
  > exactamente lo que pasa en cada despliegue con parada-antes-de-arranque
  > (ADR 0133) si el arranque se adelanta un segundo.
  >
  > **Un directorio no tiene orden, así que el almacén deja de fingir que lo
  > tiene**: `All` y `Where` devuelven por identificador. No es cosmético —
  > sin un orden igual en las dos réplicas, dos consultas idénticas contra
  > réplicas distintas paginan distinto y una página 2 se salta filas. Quien
  > necesita orden de negocio ya lo dice (por fecha, por puntaje); se
  > recorrieron los veintitantos sitios que listan y **todos** lo decían.
  >
  > **Lo que queda ABIERTO, y no se tapa: los cuatro orquestadores.** Sus
  > sagas también viven en un `JsonCollectionStore`, así que **heredan la
  > mitad del almacén** —dos réplicas que avanzan sagas distintas ya no se
  > borran— y **no tienen turno de escritura**: dos que avancen la MISMA
  > saga a la vez siguen pudiendo perder una escritura. No se les cableó y
  > no es un olvido — dentro de un paso de saga hay llamadas HTTP a las
  > capacidades, así que un turno de orquestador entero dejaría toda compra
  > haciendo cola detrás de la que está esperando a la pasarela, que es
  > cambiar un defecto raro por uno seguro. Lo que ahí corresponde es un
  > turno **por saga**, del tamaño de `ISagaLease` (#34), y es otro trabajo.
  > Hoy lo que los protege es que `SagaEngine` resuelve la llave de
  > idempotencia antes de abrir y que compensar va bajo arriendo.
  >
  > **Y el compose siguió PROHIBIÉNDOLO cinco tickets más** (#152). `compose.prod.yml`
  > llevaba veinticuatro veces «NO SUBIR DE 1. `JsonCollectionStore` tiene un lock de
  > PROCESO», que es exactamente la razón que esta HU quitó, y su gate lo exigía con la
  > misma frase. El gate **nunca se puso rojo porque el compose lo cumplía**: un gate que
  > afirma de más no falla, se satisface. Hoy distingue —una capacidad con turno escala,
  > un orquestador no— y el comentario dice la verdad nueva. Lo que se quitó no es el
  > valor `1`: es la prohibición.

  > **Verificado con procesos vivos, que es donde se vio** (§10.6): dos
  > `Api.Inventory` sobre el mismo volumen, 400 ajustes RELATIVOS disparados
  > de a dos contra las dos réplicas a la vez. **Sin turno: 400 respuestas
  > 200 y 268 unidades** —132 escrituras perdidas, sin una sola excepción y
  > sin un log—. **Con turno: 400 y 400.** Ningún test lo habría visto: los
  > dos procesos existen o no existen. Y de paso quedó comprobado que el
  > cerrojo es de verdad del sistema —un `flock` desde un python ajeno le
  > saca el turno a la capacidad, que contesta `inventory.store_busy` con
  > 503 y `transient: true`, mientras los `GET` siguen pasando en 12 ms—.
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
  > Quedaban **seis**, y tampoco eran un grupo. **La canasta ya está
  > hecha** —`Api.Cart`, séptima rebanada de la HU #14— y las otras cinco
  > siguen sin estar cableadas a un consumidor que **presente identidad**;
  > los emisores del repo son **cuatro**: `HttpCaseWorkflowService` y
  > `HttpGovActNotificationService` hacia Gobierno, `HttpAuditTrailWriter`
  > hacia `Api.Audit` y `HttpShopOrderService` hacia `Api.Cart`. De ahí
  > **no** se sigue que a las cinco les falte cableado:
  >
  > | capacidad | campo | consumidor hoy |
  > |---|---|---|
  > | `Moderation` | `DecidedBy`, `Reporter` | ninguno |
  > | `Engagement` | `Actor` | ninguno |
  > | `Documents` | `Owner` | sólo nombrada por Gobierno, como adjunto por referencia |
  > | ~~`Cart`~~ | `Owner` | **hecho** — el CMS, directo (`POST /v1/carts`) |
  > | `Orders` | `Buyer` | **`Bff.Tienda`** |
  > | `Payments` | `Payer` | **los tres orquestadores** — y el CMS, **directo**, para la tasa de un trámite (#27) |
  >
  > **Esa última fila decía sólo «los tres orquestadores», y le faltaba el
  > consumidor que importaba.** Desde la HU #27 el CMS le habla a
  > `Api.Payments` **sin orquestador en medio** para cobrar la tasa de un
  > trámite (`HttpPaymentProvider`), y ése es justamente el único que puede
  > presentar identidad sin depender de la decisión de abajo. Es la misma
  > forma que ya costó tres veces: una lista escrita de memoria en vez de
  > medida contra el fichero.
  >
  > Y para las tres de abajo el actor **no es un seudónimo opaco**: con
  > sesión iniciada es el `memberKey`, que es exactamente la forma de
  > sujeto que el CMS ya firma (el seudónimo de #47 sustituye al **correo**,
  > no al identificador).
  >
  > Las tres de arriba sí esperan su primer consumidor, y conviene que sea
  > entonces: se verifica contra un llamador real y no contra un fake, que
  > es lo que hacía que #72 tocara —`Api.Audit` acababa de estrenar
  > consumidor con #15—.

  > **El orquestador NO propaga la cabecera, y la decisión ya está tomada**
  > (HU #14, lo que quedaba). La pregunta era si `Bff.*` debía reenviar el
  > `X-Synergos-Identity` del CMS para que `Api.Orders` y `Api.Payments`
  > supieran quién actuó. **No**, y no por dificultad: **propagar tiene la
  > forma equivocada.** Un token es una credencial CON RELOJ; una saga es
  > trabajo CON DURACIÓN, y las dos cosas no componen.
  >
  > **Uno: el token vence y la saga le sobrevive.** Quince minutos de
  > vigencia; `Sweep:AbandonAfterMinutes` se mide en decenas, y compensar lo
  > hace un barrido horas después **sin nadie al teclado**. Propagar daría un
  > `authorize` firmado y un `void` o un `refund` sin firmar: la afirmación
  > presente justo donde no pasó nada irreversible y ausente justo donde la
  > plata vuelve. Eso es **peor** que no tener ninguna — el asiento fuerte del
  > primer paso hace leer el débil del último como una degradación que alguien
  > eligió.
  >
  > **Dos: para que sobreviviera habría que GUARDARLO.** El barrido no tiene
  > a quién pedirle uno nuevo, así que la única salida sería persistir la
  > credencial en la saga. Eso pone un bearer en el disco del orquestador, por
  > cada saga, en un almacén que **se respalda** — la misma forma que este
  > repo ya rechazó para el `.env` del servidor: custodia de secretos
  > disfrazada de copia de datos. Y no se lee como «reenviar una credencial»:
  > se lee como añadirle un campo a un `record`. Por eso el gate mira **el
  > almacén y no sólo el cable**.
  >
  > **Tres: el sujeto no cuadraría igual.** Lo que vuelve prueba a un token es
  > que la capacidad rechaza el que nombra a otro (`token_subject_mismatch`),
  > y los pasos de una saga nombran a sujetos **distintos**: `authorize` al
  > pagador, `hold` a `tienda.compra/{sagaId}`, el pedido al comprador. Un
  > token reenviado sólo puede probar uno. Propagar no sería «identidad para
  > `Payments`»: sería identidad para **un campo**, con el resto igual que
  > antes y con el aspecto de estar resuelto.
  >
  > **Lo que se hace en vez de eso, y ya estaba a medio construir: DERIVAR.**
  > `Bff.Tienda` no le cree al llamador quién compra — lee el dueño de la
  > canasta de `Api.Cart` (`PurchaseFlow`), y ese dueño se estableció
  > presentando un token verificado (séptima rebanada). O sea que el comprador
  > que llega a `Api.Orders` y a `Api.Payments` **ya está anclado, sin que
  > ninguna credencial cruce el orquestador**. La diferencia es ésa:
  > **reenviar una credencial** contra **citar un registro**. Una credencial
  > vence, hay que custodiarla y sólo prueba un sujeto; un registro no vence,
  > no es secreto y lo relee cualquiera que tenga la llave compartida. La
  > regla, entonces: **un orquestador nunca nombra a una persona por su propia
  > palabra — la nombra citando el registro de una capacidad que ya la
  > verificó.**
  >
  > **Y eso decide que `Api.Orders` y `Api.Payments` hoy NO llevan puerta de
  > identidad por ahí**: sería abrirles un campo que nadie puede llenar —el
  > único que las llama por esa vía es un orquestador que, por esta decisión,
  > no presenta— y un `PaidWith` que dijera siempre lo mismo es
  > `feedback_no_read_without_a_write_path` por el lado de la escritura. La
  > excepción es el cobro que **no** pasa por orquestador —la tasa de un
  > trámite (#27)—, y ése sí se cableó: ver más abajo.
  >
  > **Lo que queda abierto, nombrado en vez de dado por hecho:** Tienda pudo
  > anclarse porque tiene una canasta —un registro con dueño verificado,
  > anterior a la compra—. **Salud, Eventos y Viajes no tienen ninguno**: se
  > entra al flujo nombrando al paciente, al comprador o al viajero, y no hay
  > registro previo que citar. Darles uno es trabajo de verdad —¿dónde vive el
  > «carrito» de una cita?—, no un cableado.
  >
  > **El disparador para reabrir esto**, escrito para no tener que adivinarlo:
  > el día que una capacidad detrás de un orquestador necesite saber **CÓMO**
  > se identificó la persona y no sólo quién es. Ahí citar el registro deja de
  > alcanzar, porque la afirmación vive en la capacidad de al lado — y lo que
  > corresponde **sigue sin ser propagar**: es que el orquestador cite lo que
  > aquel registro dice que fue (`Cart.OpenedWith`) y que la capacidad lo
  > guarde como afirmación **de segunda mano**, distinta de la que ella misma
  > verificó.
  >
  > Hay gate (`PropagacionDeIdentidadTests`), escrito **estando en verde** —
  > que es cuando es gratis, como el de «ninguna capacidad llama a otra»
  > (#49)— y con cuatro dientes: que ningún `Bff.*` nombre la cabecera, que
  > **ninguna saga guarde algo con pinta de credencial** (por donde se
  > revierte esto en silencio), que Tienda **siga derivando** al comprador de
  > la canasta, y que la lista de los tres que todavía nombran por su palabra
  > sea exacta **en los dos sentidos**: uno nuevo rompe el build, y uno que
  > deje de estarlo también — para que esta sección se mueva en el mismo
  > commit en vez de quedarse diciendo que falta algo que ya está hecho.

  > **La canasta ya no se cree de quién es** (HU #14, séptima rebanada).
  > `POST /v1/carts` es el **único** endpoint de `Api.Cart` donde el
  > llamador nombra a una persona; los otros tres llegan con el
  > identificador de una canasta que ya sabe de quién es, y lo que su
  > cuerpo nombra —`subjectKind`/`subjectId`— es el **producto**. Por eso
  > el gate no pide identidad a los cuatro: exigírsela a los otros no
  > añadiría prueba ninguna, sólo movería el campo de sitio. Que alguien
  > con la llave compartida pueda tocar la canasta de otro si adivina su
  > identificador es la otra pregunta —**autorizar**, no atribuir— y esta
  > HU no la contesta en ninguna capacidad.
  >
  > La canasta guarda `OpenedWith`, y nulo es «no consta»: las anteriores
  > no llevan afirmación y no se rellenan. **Sólo se presenta token cuando
  > hay SESIÓN.** El dueño de la canasta de un invitado es un seudónimo de
  > un correo que alguien escribió en un formulario y que nadie comprobó;
  > pedirle token haría que la capacidad anotara `IdentityToken` sobre una
  > identidad que no verificó nadie — el defecto #42 con la firma tapándolo
  > mejor.
  >
  > **Y si falla la identidad, NO se repite sin firmar** — al revés que la
  > bitácora (#72), y por la razón que la bitácora da: allá perder un
  > asiento es peor que un asiento débil porque **un hueco no se nota**.
  > Acá se nota: falla delante de quien está comprando, en ese momento, y
  > se arregla poniendo la llave o apagando el modo. Repetir sin firma
  > dejaría canastas atribuidas a un miembro por la sola palabra de quien
  > llamó, en un registro que **nadie audita** y que vence solo a los siete
  > días: el hueco sería permanente y no lo vería nunca nadie. Hay gates
  > (`IdentityGateTests`, `ShopWiringTests`), y el de la capacidad **cuenta
  > en vez de enumerar**: cruza qué contratos de petición dejan nombrar al
  > dueño contra qué endpoints tocan el almacén.
  >
  > **De paso, la lista del compose ya se había desincronizado**, y así se
  > descubrió: `tools/compose-gen.mjs` llevaba a mano las capacidades que
  > verifican tokens y decía **dos** cuando eran **cuatro** —`Api.Consent`
  > (rebanada 5) y `Api.Audit` (#72) cablearon el verificador y nadie
  > volvió a mirar esa línea—, así que el despliegue no les pasaba la
  > llave. Eso **no falla al arrancar**: la capacidad sirve y rechaza el
  > primer token que le presenten, o sea un servidor bien configurado
  > comportándose como uno sin llave. Hoy la lista se **deriva** del
  > `Program.cs` de cada una, y hay gate.

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
- **El catálogo de Educación ya puede salir del CMS** (#100,
  `Synergos:Catalog:Sources:Academy = cms`, con el seed de demo de default).
  Era **el único vertical cuyo objeto central no se podía autorar**: un curso
  salía de `StubCourseCatalogProvider`, sembrado en C#, así que publicar uno
  exigía un despliegue. Hoy `coursePage` lleva su currículum
  (`elementCourseModule` / `elementCourseLesson` + sus Block List) y
  `UmbracoCourseCatalogSource` lo sirve. El mapa del cableado decía que
  «no existe `coursePage`» y eso era falso ya entonces — el DocType estaba
  con sus 16 campos de ficha; lo que faltaba era el temario, y sin él un curso
  autorado no se podía cursar.

  > **Y añade algo que las otras cuatro fuentes no necesitan: SEMBRAR.** Una
  > lección referencia su cuerpo por `ContentItemId` —un item del
  > `IContentStream` con `Kind=lesson`— y ese id lo asigna el feed, así que la
  > fuente no lo puede rellenar: entrega el cuerpo y el catálogo lo siembra.
  > Sembrar en la FUENTE habría sido el defecto caro y callado:
  > `ICatalogSource.GetAllAsync` se llama en CADA búsqueda —el catálogo no
  > cachea, a propósito, para que el read-your-writes salga gratis—, así que
  > el feed crecería un item por lección y por búsqueda. Hay gate
  > (`AcademyCatalogSourceTests`).
  >
  > **La siembra es durable y su clave lleva la HUELLA del cuerpo**, y las dos
  > mitades hacen falta. Sin lo primero, cada arranque re-siembra el feed
  > entero. Sin lo segundo, editar una lección no se ve nunca: `IContentStream`
  > sólo sabe CREAR —no hay update—, así que un mapping por `lessonId` a secas
  > serviría para siempre el primer cuerpo. El `lessonId` NO cambia al
  > reescribir, que es lo que salva el progreso del alumno
  > (`CompletedLessonIds` guarda el id de la lección, no el del item). El coste
  > es un item huérfano por edición, y es el barato de los dos.
  >
  > **El id de una lección no lleva su número de orden**, aunque sea lo natural
  > de escribir: reordenar el temario es normal, y con el orden dentro,
  > reordenarlo reescribiría la historia de todos los matriculados en silencio.
  > Se deriva del título y el editor puede fijarlo.
  >
  > **Y el descriptor de búsqueda subió al tipo del CONTRATO.** Estaba sobre
  > `SeedCourse` —el tipo del seed de demo—, así que la fuente nueva habría
  > tenido que declarar el suyo: dos descriptores y la búsqueda comportándose
  > distinto según una línea de config. Al moverlo apareció el hueco: ningún
  > test buscaba por instructor, que es el único campo del descriptor que no se
  > resuelve leyendo una propiedad.
  >
  > **Lo que NO cruza todavía**: `PublishCourseAsync` guarda en el overlay
  > durable y el editor lo promueve a `coursePage` cuando quiera — que ese
  > ascenso sea manual es deliberado, igual que en Eventos. Y el instructor
  > sigue siendo tres campos del curso, no una entidad con ficha: el día que
  > haya un `instructorPage`, su id deja de derivarse del nombre.

- **Y el directorio de profesionales de Salud también** (#118,
  `Synergos:Catalog:Sources:Salud = cms`, con el staff sembrado de default).
  Salud era **el último vertical sin el EJE 1 del molde** —doc 12 §7.2 lo
  nombraba como hallazgo de haber medido—: el profesional salía de
  `StubDoctorDirectory`, sembrado en C#, así que dar de alta un médico era un
  cambio de código y un despliegue. Hoy hay `professionalPage`,
  `UmbracoProfessionalDirectorySource` + `ProfessionalContentRules` y
  `CatalogDoctorDirectory`. **Con eso los SIETE verticales cumplen el eje 1 y
  el gate del molde ya lo exige** (`Cada_vertical_tiene_su_EJE_1`): la
  excepción existía porque exigirlo dejaba el build rojo por una decisión de
  producto que nadie había tomado, y se fue con la decisión.

  > **Lo que NO se autora, y es el resto del hallazgo que sigue en pie**: el
  > paciente y la agenda. El destino del EHR-lite es un EHR externo, no una
  > capacidad nuestra. El eje 1 de un vertical es su **objeto central**, y el
  > de Salud es el profesional.
  >
  > **Esta fuente no siembra, al revés que la de Educación**, y por eso es más
  > corta: un profesional no referencia cuerpos en otro almacén, así que no hay
  > `ContentItemId` que resolver. El gate lo exige igual —`IContentStream` y
  > `CreateAsync` fuera de la fuente— para que el día que haya algo que sembrar
  > no se resuelva por el camino corto.
  >
  > **El schema NO lleva el identificador del recurso de `Api.Booking`, y hay
  > gate sobre el XML.** Lo que viaja es el slug como `professionalId`; el
  > recurso lo resuelve el BFF con
  > `GET /v1/resources?subjectKind=&subjectId=` (#25). Un campo en el DocType
  > para teclearlo sería `SaludSettings.ResourceIdPrefix` otra vez y peor,
  > porque lo teclearía un editor — y sobre la prosa no servía vigilarlo: la
  > prosa ya lo decía en `SaludSettings` y el campo habría entrado igual.
  >
  > **Tres campos que el borde escribía a mano ya salen del seam.** `DoctorDto`
  > emitía `acceptingPatients: null` y dos cadenas vacías, con el `<remarks>` de
  > la HU #111 nombrando el disparador —«que `IDoctorDirectory` sepa decir si el
  > médico admite pacientes nuevos»—. Eso es justo lo que
  > `feedback_a_fabrication_can_be_a_derivation` avisa que **blinda** un defecto
  > si se queda escrito: se arregló en vez de dejarlo nombrado. `null` sigue
  > siendo «no consta» para el staff sembrado, que de verdad no lo sabe.

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
- **Dos barridos ya NO compensan dos veces lo mismo** (#34). `CompensateAsync`
  leía, ejecutaba y escribía **sin marca de en-curso**, y lo llaman el barrido
  y cada flujo en línea: la carrera no necesitaba dos réplicas — bastaba un
  barrido y un `AbortarAsync` simultáneos en el mismo proceso. Hoy va bajo
  arriendo (`ISagaLease`, fichero por saga tomado con `rename`, lo único
  atómico), que **vence** —`Sweep:CompensationLeaseSeconds`, con piso duro—
  porque un arriendo eterno cambia «se hace dos veces» por «no se hace
  nunca», que no se nota. Quien no lo consigue recibe
  `compensation_in_flight` **transitorio**, como el `retry_in_flight` de
  `Api.Notifications`: no es que no se pueda, es que el otro está en curso.

  > **Y había una segunda mitad que el ticket no nombraba**: el caché de
  > `JsonCollectionStore` vive en la instancia, así que un arriendo a secas
  > habría sido teatro — la segunda réplica no compensaría *a la vez*, y lo
  > repetiría un minuto después leyendo su propia copia. Es la forma exacta
  > del defecto #82. Por eso bajo arriendo se **relee del disco**, y el
  > barrido relee al empezar la vuelta.
  >
  > **Lo que NO queda arreglado, y está dicho**: `JsonCollectionStore.Put`
  > sigue escribiendo el mapa entero desde el caché de su proceso, así que
  > dos réplicas que escriban a la vez se pisan igual, arriendo o no. Eso es
  > el cambio de almacén, no esto.
  >
  > Y los barridos los levantan los **CUATRO** orquestadores, no dos: esta
  > sección decía «los dos» desde antes de `Bff.Eventos` y `Bff.Viajes`, que
  > los heredaron de `AddSagaMachinery` sin que nadie tocara una línea.
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

  El CDN servía **130 elementos** el 2026-09-05, y el repo hermano declara
  dos más que no publica (`synergos-stat-counter` y `synergos-module-mount`).
  Ésta es la ÚNICA cifra del CDN que da este fichero, y va con el comando al
  lado a propósito: es la única que no se puede cruzar contra el disco de este
  repo —depende de la red y del hermano—, así que copiarla a otro sitio es
  garantizar que se desvíe. Ya pasó, y en tres versiones distintas a la vez.
  Hay gate (`CifrasDeClaudeMdTests`), y no comprueba que sea correcta —desde
  acá no se puede— sino **que no esté copiada**.

  > **El `| jq length` a secas devuelve `4`, no 130**, y estuvo así el rato que
  > tardó en verse: la raíz del registry es un objeto —`generated`, `version`,
  > `baseUrl`, `elements`— así que `length` cuenta sus CLAVES. Un comando que
  > contesta un número plausible y equivocado es peor que ninguno: la cifra
  > venía con él justamente para que se pudiera comprobar, y comprobarla
  > confirmaba otra cosa. Se corrigió ejecutándolo, que es la única forma de
  > saberlo — mirarlo no lo dice.
  Lo que falta es que el despliegue configure `SYNERGOS_CDN_MODE=Http` +
  `SYNERGOS_CDN_URL` (ver `.env.example`). Ver §9 y ADR 0132.

  > **Esta línea decía «decisión de entorno del arquitecto, NO trabajo pendiente
  > de código», y era falso** (#126). Encender esas dos variables dejaba el sitio
  > **sin hidratar ni un solo elemento**: el `<script type="importmap">` lo armaba
  > `_SynHostRuntime.cshtml` leyendo
  > `{LocalPath}/{ns}/runtime/angular/latest/import-map.json` **del disco**, y en
  > la imagen de producción no hay ningún `/cdn` montado —el compose monta cinco
  > volúmenes y ninguno es ése—. Los `<script type="module">` que arma
  > `DefaultSynHostEmitter` salían escritos y el navegador no podía resolver
  > `@angular/core`, así que ningún `<synergos-*>` se registraba: **200, el SSR
  > entero, y todo lo interactivo muerto**.
  >
  > **Lo tapaba el desarrollo**: en la máquina del arquitecto `C:\LOCAL_CDN`
  > existe, así que el mapa salía y el defecto sólo aparecía en el contenedor. Es
  > la regla 3 del `CLAUDE.md` del repo hermano con otra cara.
  >
  > Hoy el mapa lo da la seam —`IBundleRegistryClient.TryGetImportMapAsync`, con
  > sus tres adaptadores— que es lo que ADR 0012 manda: el contrato del CDN se
  > **consume**, no se relee del disco. Y el probe lo exige: en modo `FileSystem`
  > u `Http`, resolver un descriptor **ya no alcanza** para reportar salud. Hay
  > gate (`ImportMapEmissionTests`), y **encontró dos huecos más al primer
  > intento**: `PageBare.cshtml` y `Error.cshtml` pintan Block Grid con
  > `Layout = null`, así que no heredaban el `<head>` de `_Layout` y nunca
  > tuvieron mapa — ni en producción ni en desarrollo.
  >
  > **Y el mapa se COMPONE por framework, no se elige** (#127). El navegador lee
  > **el primer** `<script type="importmap">` de la página e **ignora los
  > siguientes**: no se acumulan, no se funden, y uno presente no se corrige con
  > otro después. Así que con dos frameworks publicando, servir el de uno deja al
  > otro sin resolver sus bare specifiers — el defecto #126 otra vez, esta vez a
  > mitad de página. Se componen **todos los que el registry declara** (las claves
  > de `implementations`, derivadas del disco y no una lista a mano), y no los de
  > la página: un import map sólo **declara** correspondencias, así que las
  > entradas de un framework que nadie usa cuestan unos cientos de bytes de HTML y
  > **cero peticiones** — y saber qué elementos trae la página exigiría conocerlos
  > antes de renderizar el `<head>`, que es justo lo que no se sabe ahí.
  >
  > **El mismo specifier con la MISMA URL no es conflicto** —`rxjs` puede salir del
  > mismo sitio para dos frameworks— y se deduplica. Con URLs **distintas** se
  > **para**: elegir uno en silencio deja al otro framework cargando el runtime
  > equivocado, y eso no se lee como «el mapa está mal» sino como «ese elemento
  > está roto», que manda a alguien a depurar el elemento. Es la decisión de
  > `PaymentEngineCoexistenceTests` aplicada a otra cosa — cuando dos piezas se
  > contradicen sobre un hecho, servir una de las dos es peor que parar.
  >
  > **Pero PARAR no es tirar el mapa que ya funcionaba, y esto decía lo contrario.**
  > El razonamiento de #127 era «si el conflicto es real, el vigente también lo
  > estaba sirviendo mal», y **se desmiente solo**: un mapa vigente sólo puede
  > existir si cuando se compuso NO tenía conflicto —con conflicto, `Componer`
  > devuelve `null` y no hay vigente—. O sea que el conflicto es siempre NUEVO, lo
  > introduce quien acaba de empezar a publicar, y tirar el bueno convierte «los
  > elementos del framework nuevo no hidratan» en **«no hidrata nada»**, el que ya
  > funcionaba incluido: una publicación del otro árbol apagando el sitio entero.
  > Hoy se conserva el vigente y el probe lo dice; en arranque en frío no hay nada
  > que conservar y se devuelve `null`, que es la verdad.
  >
  > **Y el caso es inminente, no hipotético** (`Synergos.UI#58`): el runtime de
  > Angular publica hoy `@synergos/core` y `@synergos/shared` —nombres
  > **agnósticos**— apuntando a `/synergos/runtime/angular/…`, medido contra el CDN
  > vivo. El día que una segunda plataforma publique su mapa con esos mismos
  > nombres, colisionan. La salida vive en el otro árbol y cuesta dos entradas más
  > en un JSON generado: Angular publica el nombre agnóstico **y** el suyo
  > calificado —mismo destino, así que se deduplica— y las plataformas nuevas
  > publican sólo el calificado.
  >
  > **Lo que esto enseña, y por eso está acá:** una regla puede vivir en un árbol y
  > su CAUSA en el otro. El gate de quien vigila no protege a quien rompe — el test
  > del compositor llevaba meses verde con este fixture exacto, y la consecuencia
  > se escribía en el repo de al lado.
  >
  > **Y lo que NO hace falta, medido y escrito para que nadie lo proponga de
  > cero**: un «helper por framework» para el `<script>` del elemento.
  > `DefaultSynHostEmitter` emite `<script src="…" type="module" defer>` y sus
  > dependencias, y ya — un custom element de React o de Svelte arranca igual. La
  > palabra `Framework` aparece **una sola vez** en ese fichero y es un comentario
  > que dice «Framework-agnostic». Lo que es del framework es el mapa.

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
