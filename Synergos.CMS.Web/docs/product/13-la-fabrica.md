# 13 — La fábrica: de un spec a un vertical

> **Estado: diseño. Nada de esto está construido**, y está escrito así a propósito — un documento
> que describe lo que todavía no existe es el defecto
> `docs_written_ahead_of_the_code_are_a_defect_with_a_clean_face`, y la única forma honesta de
> escribirlo es decir en la primera línea que la prosa va por delante.
>
> Continúa el [doc 12](12-el-molde-de-un-vertical.md), que escribió **el molde** de un vertical.
> Éste escribe **quién lo ejecuta, con qué fuente, y qué impide que el ejecutor se pudra.**

## 1. Por qué hacía falta, y por qué no bastaba con el doc 12

El doc 08 §4 escribió el molde de una capacidad. El doc 12 escribió el de un vertical: ocho pasos,
cada uno con una decisión y cada decisión con su gate. Los dos están medidos contra el disco y los
dos rompen el build cuando alguien se desvía.

O sea que este repo ya tiene **un compilador** (los moldes), **un verificador de tipos** (los gates de
arquitectura —**59** en el disco, medidos—, más los de Node, más los dos que arrancan la app) y **un enlazador** (los
quince puntos de cableado con sus interruptores).

**Lo que no tiene es un lenguaje fuente.** Hoy la entrada del compilador es una conversación: un
issue en prosa, una sesión, y un agente que lee siete verticales para adivinar qué partes eran
esenciales. El doc 12 dice exactamente eso de la situación anterior a él —«eso es lo que hace que
el siguiente no se parezca a los anteriores»— y lo resolvió **para el molde**. Queda resolverlo
para la **entrada**.

La fábrica no es maquinaria nueva. Es el lenguaje fuente, la bajada que lo posa sobre los moldes
que ya existen, y —lo que de verdad falta— **un gate para el arnés**.

## 2. Primero medir — el arnés de hoy

La lección más repetida del repo: una lista sacada de la cabeza congela un error. Así que antes de
diseñar nada, el arnés existente contra el disco, el 2026-09-21.

| medido | dato |
|---|---:|
| skills en `Synergos.CMS/.claude/skills/` | 24 |
| skills en `Synergos.UI/.claude/skills/` | 21 |
| de las 21 compartidas, **byte a byte idénticas** | **18** |
| de las 21 compartidas, ya **divergidas** | 3 (`architect`, `cms-author`, `guardrails`) |
| skills que cablean `C:\LOCAL_CDN` o `synergos.local` | **19 de 24** |
| skills que nombran **NX** | 7 — y `nx.json` no existe desde la purga |
| líneas totales de arnés | 8 089 |

Y la medición que decide el resto del documento:

| afirmación | portador | dice | disco |
|---|---|---:|---:|
| nº de ADRs | `CLAUDE.md` — **tiene gate** (`CifrasDeClaudeMdTests`) | 132 | **132** |
| nº de ADRs | `synergos-*` skills — **sin gate** | 92 · 92 · 107 | 132 |
| nº de elementos publicados | `synergos-architect/references/ui-elements-catalog.md` | 122 | **132** |

**Mismo repo, mismos hechos, dos portadores. El que tiene gate está exacto; el que no lleva
cuarenta ADRs de retraso.**

> **Y el corte es más fino que «guía contra skills»: es cifra gateada contra cifra sin gatear,
> dentro del MISMO fichero.** `CifrasDeClaudeMdTests` vigila cuatro familias —las del Layout
> Composer, las por área, las del schema y la del CDN— y **no vigila la cuenta de gates**.
> Medido hoy: `CLAUDE.md` dice «**57** gates» y en
> `Synergos.Arquitectura.Tests/Architecture/` hay **59** ficheros, los 59 con `[Fact]` o
> `[Theory]`. Los dos últimos en entrar —`RutasDeProyectoTests` (#136) y `RespaldoTests`— no
> los contó nadie.
>
> No es una errata: es la misma ley operando dentro del documento que mejor se cuida del repo.
> Donde el gate llega, la cifra es exacta; donde no llega, se desvía, y **se desvía en la
> dirección de quedarse corta**, que es la que no se nota. Queda como hallazgo, sin arreglar
> aquí: arreglarlo es una línea y mezclar un arreglo con un diseño es lo que `§4.3` de
> `CLAUDE.md` prohíbe. No es una anécdota sobre unas skills viejas: es la demostración, dentro
de este repo y con su propia disciplina, de que **una afirmación sin gate se desvía siempre** — que
es lo que `CLAUDE.md` §5 lleva treinta memorias documentando sobre el código, aplicado al único
sitio donde nadie lo aplicó.

### 2.1 Tres defectos concretos del arnés, para que no se lean como «deuda cosmética»

**(a) El defecto #132 está vivo en el arnés.** Diecinueve de veinticuatro skills mandan correr algo
contra `C:\LOCAL_CDN` o `http://synergos.local:5000`. El #132 sacó esa ruta de los `appsettings`
porque «una ruta por defecto que sólo existe en la máquina de quien la escribió» servía una portada
**200, con el SSR entero y sin hidratar nada**. La misma ruta, en el arnés, manda a un agente en un
contenedor a una carpeta que no existe — y ahí no degrada en silencio: **el agente concluye que no
se puede verificar y se inventa otra cosa**, que es la forma de la regla 11 del repo hermano («"no
se puede desde este contenedor" se COMPRUEBA antes de decirlo», escrita después de haberlo afirmado
cuatro veces en falso).

**(b) Una `description` que contradice su propio cuerpo.** `synergos-cms-author` —1 054 líneas, la
más grande— se describe como «crea contenido Editorial **vía Management API**». Veinte líneas
dentro, con todas las letras y citando la ADR 0093: «Umbraco 13 **NO tiene Management API**
(`/umbraco/management/api/*` → 404); el paquete empieza en v14. **Olvida el flujo token + POST
/v1/document** — no existe aquí». La `description` es **lo único que el modelo lee para decidir si
dispara**. O sea que la pieza está escrita para disparar hacia un flujo que contesta 404, y el
desmentido vive donde sólo se llega si ya disparó.

**(c) La única pieza del arnés que se deriva del disco no está enchufada.**
`Synergos.UI/tools/refresh-skill-catalog.mjs` regenera el catálogo de elementos desde el registry
vivo, y su propia cabecera afirma: «Run automáticamente al final del `release:angular` (ver
package.json)». **No existe ningún script `release:angular`**, y `release` no lo llama: el único
enganche es `skill:refresh`, manual, que nadie teclea. De ahí salen los 122 contra 132.

Es el **addendum #14** exacto —medir que la pieza esté ENCHUFADA y no que exista— y el reverso de
`restored_mutation_needs_a_touch`: aquí lo que no llegó no fue la restauración, fue el cable.

### 2.2 Y la copia: 18 ficheros idénticos en dos repos

`the_same_algorithm_is_not_the_same_thing` dice que lo que decide si dos trozos son el mismo son el
**sujeto** y la **política**, no el algoritmo — y que la promoción se mide leyendo, no contando.
Leídas: el sujeto y la política de las dieciocho son idénticos. Son copias, no variantes.

Y ya empezaron a pagar el precio que esa memoria anuncia: **tres divergieron**. Nadie decidió que
divergieran; alguien arregló una y no fue a la otra. El día que se afine una cuarta, el otro repo
miente, y —como en la regla 26 del hermano— **el que miente no es el que se arregló**.

## 3. La fábrica son tres tramos

```
UPSTREAM ─────────────────────────────────────────────────────────────
  spec.md          prosa + cabecera legible     ← lo escribe una persona
  refinamiento     3 preguntas × 3 ejes         ← skill + persona
  plan.md          N sub-specs                  ← DETERMINISTA, del molde
BAJADA ───────────────────────────────────────────────────────────────
  un sub-spec = un paso del molde = un commit = un gate
DOWNSTREAM ───────────────────────────────────────────────────────────
  gates → mutación → procesos vivos → humo conectado → PR
```

El tramo de abajo **ya está construido entero** y es la mitad cara. El de arriba no existe. El del
medio es una tabla, no un algoritmo — y que sea una tabla es el punto: §5.

## 4. El spec (upstream)

**Markdown en `docs/specs/<vertical>/spec.md`**, que es lo que este repo hace bien: prosa que
explica el *por qué*, no una ficha que sólo un parser entiende. Los docs 06 a 12 son la prueba de
que aquí la prosa sostiene decisiones que un YAML no sostendría.

**Con una salvedad que no es un cambio de formato, es el gate.** `a_gate_that_parses_source_needs_its_own_mutations` está escrito precisamente sobre esto: G-6 perdía en silencio todo parámetro con un `=` en su comentario, y el gate del #126 se engañaba con su propia explicación hasta que se le quitaron los comentarios. Un gate que valide prosa con regex **va a salir verde sobre un spec incompleto**, y un verde sobre el vacío se hereda.

Así que el spec es **un fichero con dos mitades**: cabecera `---` con los hechos que un gate cruza
contra el disco, y prosa debajo con todo lo demás. La cabecera no reemplaza la prosa; la prosa no
puede contradecir la cabecera, y hay gate para eso (§8, diente 4).

```markdown
---
vertical: social
epica: 11
ejes:
  catalogo:    { doctype: postpage, fuente: UmbracoSocialCatalogSource, interruptor: "Synergos:Catalog:Sources:Social" }
  transaccion: { forma: ninguna, razon: "no hay nada que deshacer — ver §3" }
  artefacto:   { que: ninguno }
preguntas:
  deshacer:      no      # → sin orquestador
  recurso_ajeno: no      # → sin cableado
  quien_cobra:   nadie
reusa:
  capacidades: []                       # de las 20 — el agente las CONSULTA, no las recuerda
  elementos:   [comments-widget, poll]  # de los 132 del registry
crea:
  doctypes: [postpage, postcategorypage, authorpage]
  seams:    []
rechazos: []                            # código · cuándo · transitorio
---

## Qué problema del negocio resuelve
…

## Cómo sabemos que quedó bien
Cada criterio con **la mutación que lo pone en rojo**.
```

Tres cosas de esa cabecera que no son decoración:

1. **`reusa:` va antes que `crea:`, y se llena consultando.** `CLAUDE.md` §2 documenta el daño de
   lo contrario: una frase que decía «UNA capacidad conectada» cuando eran nueve hizo que un agente
   «propusiera de cero lo que ya existe». El spec obliga a nombrar lo reusado, y el MCP de §7 hace
   que nombrarlo cueste una consulta en vez de una hora de lectura.
2. **`preguntas:` son las tres del repo, por separado.** El error de clasificación que costó cuatro
   olas —mirar «cuántos pasos compone»— deja de ser un criterio que alguien recuerda y pasa a ser
   tres campos con un gate detrás (`En_modo_Api_un_interruptor_gobierna_UNA_capacidad` ya existe).
3. **`rechazos:` con su columna transitorio.** Es el diseño, no el manejo de errores: «en este repo
   las reglas de rechazo SON el diseño» (plantilla de Evolutivo), y `Rejection.IsTransient` decide
   si algo se reintenta o se grita una vez.

> **El issue sigue siendo upstream del spec, no al revés.** `ticket-first` no cambia: el issue es
> donde pasó la conversación, y el spec es lo que quedó de ella en una forma que se versiona **con
> el código que produce**. El issue referencia el spec; el PR referencia el issue.

## 5. La bajada — doce sub-specs, y por qué no son creativos

Esto es lo que el arquitecto llamó «los SPs», y lo que hace que la fábrica sea una fábrica: la
descomposición **no la inventa el agente**. Son los ocho pasos del doc 12 §5, más los dos del otro
árbol, más los dos condicionales del doc 08. Cada uno toma una decisión, cabe en un commit atómico,
y tiene **un gate que ya existe**.

| # | sub-spec | árbol | lo prueba |
|---|---|---|---|
| **S1** | DocType + compositions del objeto central | CMS · uSync | `usync-audit.mjs` (11 checks) |
| **S2** | `Umbraco<X>CatalogSource` + `<X>ContentRules` + `Catalog:Sources:<X>` | CMS · Web | `Cada_vertical_tiene_su_EJE_1` · `El_catalogo_de_un_vertical_NO_sale_a_la_red` |
| **S3** | El seam `I<X>Service` + implementación **en proceso** por defecto | CMS · Interfaces | `LayerRuleTests` |
| **S4** | `<X>Settings` con `Mode`/`BaseUrl`/`ApiKey`/`TimeoutSeconds` | CMS · Application | `DefaultsDeConfiguracionTests` |
| **S5** | El interruptor **y el `Configure<>` enlazado** | CMS · Composers | `Cada_punto_de_cableado_ENLAZA_su_seccion` · `El_default_NUNCA_es_el_valor_cableado` |
| **S6** | El cliente `Http<X>`: llave, correlación, idempotencia, seudónimo | CMS · Web/Services | `…manda_la_llave_compartida` · `…propaga_la_correlacion` |
| **S7** | Controller + DTOs — las claves que cruzan | CMS · Controllers | **G-6** (`contract-keys`) · **G-7** (`contract-bodies`) |
| **S8** | El `<X>WiringTests` propio: lo que el vertical **rechaza** | CMS · Arquitectura | `…tiene_un_gate_que_nombra_su_cliente` |
| **S9** | Elementos UI — **reusar del registry** o, si no hay, crear | UI · apps | `css-parity` · `cdn-size-budget` · `platform-contract` |
| **S10** | Cliente de la app + normalizadores (en `vitals/`) | UI · apps + vitals | `normalizador-unico` · G-6 · G-7 |
| **S11** | *(condicional)* capacidad nueva — sólo si pasa el filtro de atomicidad | backend/capacidades | `ApiMoldTests` |
| **S12** | *(condicional)* orquestador nuevo — sólo si hay algo que deshacer | backend/orquestadores | `BackendSegregationTests` · `BarridoSegregationTests` |

**S11 y S12 los decide la cabecera del spec, no el agente.** `preguntas.deshacer: no` los apaga, y
`ejes.transaccion.forma: ninguna` apaga además S3–S6 — que es lo que el doc 12 §8 ya predice de
Social sin haberlo construido: «Social no tiene eje 2 y hoy no lo pide nadie».

Y la otra mitad de la bajada es `reusa:`, que apaga por **existir**. Medido hoy para Social:

| sub-spec | estado en el disco | queda |
|---|---|---|
| S1 DocTypes | `postpage`, `postcategorypage`, `authorpage` **existen** | nada, salvo que el spec pida campos |
| S2 fuente + interruptor | hay **siete** `Catalog:Sources:*` y **`Social` no está** | **el delta de verdad** |
| S3–S6 cableado | apagados por la cabecera | — |
| S7 controller | `BlogsController`, 1 376 líneas, **con 60 claves cruzando G-6** | ajuste, no autoría |
| S9 elementos | `blogs`, `comments-widget`, `poll`, `social-share`, `share-bar`, `social-proof` **publicados** | reuso |
| S10 cliente + normalizadores | existen con el controller | ajuste |

O sea que **el octavo vertical no es un vertical entero: es un sub-spec y tres ajustes** — y eso
no se ve leyendo la épica #11, que habla del dominio. Es el primer resultado útil de la fábrica y
se obtuvo **antes** de construirla, llenando la cabecera contra el disco. La bajada no es sólo
reparto de trabajo: es también la respuesta a *¿cuánto de esto ya está?*, que es la pregunta que
`CLAUDE.md` §2 dice que un agente contesta mal cuando lee una frase congelada.

> **Que la bajada sea determinista es lo que la hace auditable.** Si el plan de un vertical no se
> puede derivar de su spec sin creatividad, lo que se queda corto es el molde — y eso es un
> hallazgo del doc 12, no una licencia para improvisar. Vale la frase del doc 12 §6.10 tal cual:
> *si de verdad no es ninguna de las dos, lo que se queda corto es este documento.*

### 5.1 El orden importa, y el doc 12 ya dice por qué

S1 antes que S2 (una fuente sin DocType no tiene qué leer). S3 antes que S4–S6 (**el seam se corta
por atomicidad y pensando en la red aunque todavía no la haya** — `IPaymentProvider` nació síncrona
y la primera pasarela real obligó a subirla entera). S7 después de S6 y **antes** de S10, porque
G-6/G-7 cruzan los dos y el lado que se escribe segundo es el que se adapta.

Y una que no está en el doc 12 y sale de la regla 25 del hermano: **S9 empieza consultando el
registry, no creando.** Ciento treinta y dos elementos publicados; un vertical nuevo que escriba su
propio acordeón es peso muerto en el CDN y una clase `syn-*` más que `css-parity` tendrá que
perseguir.

## 6. El arnés — dónde vive, y por qué no es un submódulo

La pregunta era: submódulo en el CMS, o repo aparte. Se contesta con tres medidas, no con gusto.

**Un submódulo del CMS dentro de la UI arrastra el CMS entero.** Git no tiene submódulos
parciales: para compartir **716 KB** de arnés, el checkout de la UI se lleva **2 790 ficheros y
39 MB** de un árbol que no referencia. Es la forma exacta del **#135** —«el proyecto de tests era el
único que pegaba los dos árboles»— y ahí la conclusión fue partir, no relajar.

**Y el atajo para evitarlo rompe un gate que ya existe.** La forma habitual de que Claude Code
descubra skills que viven en otro sitio es un enlace simbólico dentro de `.claude/`.
`FormaDelArbolTests.Ningun_fichero_versionado_es_un_symlink` **rompe el build** con cualquier
symlink versionado, y no es teoría: el #90 metió uno y tumbó **los seis gates de segregación de
golpe**, porque .NET sigue los enlaces al recorrer directorios.

**Y el acoplamiento va en la dirección equivocada.** Un arnés dentro del CMS hace que la UI dependa
del CMS para saber cómo se trabaja. Los dos árboles se hablan **sólo por HTTP** y hay gates que lo
verifican (§0.B.11); meter una dependencia de repositorio para repartir prosa la abre por la puerta
de atrás.

### 6.1 La forma: un repo, un pin, un gate

**`Synergos.Fabrica`** — repo propio, con su CI, su versión y sus tests. Los dos árboles lo
**consumen**, ninguno lo contiene. Es el mismo movimiento que `Synergos.Shared`: se promueve al
segundo consumidor, y aquí hay dos desde el primer día.

```
Synergos.Fabrica/
├── .claude-plugin/marketplace.json    el plugin que instalan los dos repos
├── skills/                            UNA copia de cada skill
├── mcp/                               los dos servidores de §7
├── plantillas/spec.md                 la cabecera de §4 + la prosa
├── gates/arnes-derivado.mjs           el gate de §8 — corre en los TRES repos
└── tests/                             sus propias mutaciones
```

Y en cada repo consumidor, **dos líneas**:

- `arnes.lock.json` — nombre y SHA del arnés que ese repo declara. Es el pin, y es **dato**, no
  configuración de una máquina.
- Un gate que cruza el lock contra el arnés presente y **falla al arrancar la sesión**, no en la
  primera consulta. Es la lección de la llave de firma de `Api.Identity`: arrancar verde, contestar
  `/health` y reventar delante de alguien es el peor de los tres modos de fallar.

> **Por qué un pin y no «la última».** Un arnés que se actualiza solo cambia el comportamiento del
> agente entre dos corridas del mismo PR, y eso no se lee como «cambió el arnés»: se lee como «el
> agente es inconsistente». El pin lo hace un cambio con commit, con diff y con revisión — que es
> lo que este repo le exige a todo lo demás.
>
> **Y por qué el lock es un fichero y no la instalación.** Un plugin se instala por máquina, así que
> un runner de CI no lo tiene. El lock se versiona, CI clona el SHA que dice, y el gate cruza lo
> clonado contra lo declarado. Sin eso, el gate del arnés sería un gate que sólo corre donde ya
> estaba bien — `a_gate_that_runs_when_what_it_READS_changes` con otra cara.

## 7. Los MCPs — los dos que ganan su sitio, y los tres que no

§6 de `CLAUDE.md` prohíbe abstracciones prematuras. Un MCP se gana el sitio cuando sirve algo
**vivo** o **con estado** — lo que un script al hacer commit no puede dar. Los demás son un `.mjs`
con ínfulas.

### 7.1 `synergos-catalogo` — **sí**

Sirve, **derivado del disco en cada llamada y sin snapshot ninguno**: las 20 capacidades con sus
137 endpoints y sus 242 códigos de rechazo; los 132 elementos con sus inputs y su tier; los
DocTypes, DataTypes y las 481 claves de Dictionary del uSync.

Gana su sitio por dos razones medidas. Una: **es demasiado grande para el contexto** — el catálogo
congelado de hoy son cientos de líneas de Markdown que hay que cargar enteras para responder «¿ya
existe un acordeón?». Un MCP contesta esa pregunta con una consulta. Dos: **es exactamente lo que
se desvió**. El snapshot dice 122 y el disco dice 132; un servidor que deriva no puede desviarse,
porque no tiene dónde guardar el error.

Herramientas: `capacidades(dominio?)` · `endpoints(capacidad)` · `rechazos(capacidad)` ·
`elementos(intencion?)` · `inputs(elemento)` · `doctype(alias)` · `dictionary(clave)`.

**Y reemplaza a `refresh-skill-catalog.mjs`, no lo acompaña.** Dejar los dos sería mantener el
snapshot que el MCP existe para matar.

### 7.2 `synergos-entorno` — **sí**

Levanta, consulta y **mata** los procesos del árbol de servicios.

Gana su sitio porque es **estado**, y porque es lo que convierte
`verify_with_live_processes` —«los defectos caros salieron todos de levantar los procesos y matar
uno, no de los tests»— en algo que el agente puede *hacer* en vez de *recordar que debería*. Los
dos defectos más caros del repo los encontró un proceso vivo. Hoy eso es un README y una disciplina;
una disciplina que depende de la memoria es la que se salta el agente con prisa.

Herramientas: `levantar(perfil)` · `estado()` · `matar(servicio)` · `logs(correlationId)`.

`matar(servicio)` es la que paga: comprobar que el aforo vuelve al pozo cuando `Api.Payments` se cae
a mitad de la confirmación **es** la prueba de una compensación, y no hay test que la dé.

### 7.3 Los tres que NO se hacen, con su disparador

Escritos con disparador porque, si no, alguien los propone de cero en seis meses — el patrón que el
repo ya usa para el vuelo de Viajes contra `Api.Inventory`.

| propuesto | por qué no hoy | disparador para reabrirlo |
|---|---|---|
| **MCP de gates** | `dotnet test --filter` y `node tools/x.mjs` ya lo hacen y el agente tiene Bash. Envolverlo no añade nada. La parte con estado —**mutar, ver rojo, restaurar tocando el fichero**— es un procedimiento, o sea una **skill** | que la disciplina de mutación se salte dos veces por olvido del `touch` |
| **MCP de Umbraco** | ADR 0093: en 13 no hay Management API. La autoría es `IContentService` tras `DevSeed`, y envolver `/dev/*` duplicaría el `DevController` | un ADR nuevo que suba a Umbraco 14+ (§1 lo pinea) |
| **MCP de GitHub** | ya existe y está conectado. El upstream (issues) y el downstream (PRs) no hay que construirlos | — |

## 8. El gate del arnés — lo único que impide que esto se pudra

Si el arnés no queda sujeto a la misma disciplina que el resto, en doce olas estará como está hoy.
`arnes-derivado.mjs` corre en los tres repos, y son **cinco dientes**. Cada uno con su mutación,
porque un gate que no se vio fallar no vigila nada.

| # | diente | mutación que lo prueba |
|---|---|---|
| 1 | **Ninguna skill cablea una ruta de una máquina.** No reescribe el criterio: **llama** al del #132/#137 | meter `C:\LOCAL_CDN` en una skill |
| 2 | **Toda cifra que una skill afirma se deriva del disco.** Es `CifrasDeClaudeMdTests` generalizado | bajar «132 elementos» a 122 — el defecto de hoy, tal cual |
| 3 | **Toda herramienta que una skill manda correr existe y está enchufada.** Es `PortadaDeArranqueTests` generalizado, y lo que caza no es que exista: es que **la llame alguien** | declarar un `release:angular` que no existe — el defecto de hoy |
| 4 | **La `description` no contradice el cuerpo.** Si el cuerpo niega un término con un aviso, la description no lo afirma | el caso `cms-author` / Management API, tal cual |
| 5 | **Una sola copia.** El `arnes.lock.json` de cada repo contra el arnés presente, **en los dos sentidos** | declarar una skill que ya no está; dejar una skill que nadie declara |

Dos cosas que el diseño del gate se juega, y las dos son lecciones escritas:

**Los dientes 2 y 3 se parsean sobre la fuente SIN comentarios.** El gate del #126 y el del #136 lo
aprendieron igual: las explicaciones citan las formas prohibidas, y un gate que se engaña con su
propia prosa es `a_gate_that_parses_source_needs_its_own_mutations`.

**El diente 1 no reescribe el criterio: lo importa.** Dos gates con el mismo criterio es
`the_same_algorithm_is_not_the_same_thing` —«el día que uno se afine, el otro miente»— y ese error
ya casi se comete una vez, en el #137, con este mismo criterio.

## 9. Los tres pilotos, acotados

Los tres, en orden, y **cada uno es la condición de entrada del siguiente**. Una fábrica que nunca
produjo nada es un diseño.

### Piloto 0 — Eventos re-derivado. *Lo construido es el oráculo.*

Se escribe el spec de un vertical **que ya existe** y se mide cuánto de él reproduce la bajada.
**No se escribe una línea de producción** y no se toca nada: el entregable es una comparación.

- **Criterio de salida:** el plan derivado del spec nombra ≥ 90 % de los ficheros reales del
  vertical Eventos, y **cada fichero que inventa o que se pierde es un hallazgo con su issue**.
- **Lo que de verdad mide:** si el molde está completo. Si el plan pierde un fichero que Eventos
  tiene, ese fichero es un paso que el doc 12 no escribió — y saberlo cuesta una tarde en vez de
  costar el octavo vertical.
- Riesgo: cero. Coste: bajo.

### Piloto 1 — Social, el octavo vertical *(épica #11)*

El primero que produce producto. Su cabecera de spec baja a **cuatro** sub-specs, y eso es una
predicción falsable escrita antes de construirlo.

- **Entrada:** piloto 0 cerrado.
- **Criterio de salida:** los 59 gates + G-6/G-7 + `humo-conectado` en verde, y el vertical
  sirviendo. **Si Social necesita un sub-spec que la tabla de §5 no tiene, el molde lo gana.**
- **Por qué éste y no otro:** medido en §5, su delta es **un** sub-spec (S2) y tres ajustes, así
  que la fábrica se prueba con el dominio metiendo el mínimo ruido posible. Y el tamaño de ese
  delta es en sí mismo una predicción falsable: si al construirlo resulta que era mucho más,
  **la cabecera del spec no estaba midiendo lo que dice medir**, y eso es más valioso que Social.

### Piloto 2 — un vertical de negocio nuevo

- **Entrada:** piloto 1 cerrado.
- **Criterio de salida: es de TIEMPO, no de corrección.** Que pase los gates ya lo prueban los dos
  anteriores; lo que falta saber es si la fábrica es más rápida que un humano leyendo siete
  verticales. Se mide contra Social, que es el punto de comparación honesto.

## 10. Lo que NO hay que hacer

1. **No dejar que el arnés afirme algo que no se derive del disco.** Es el defecto que este
   documento mide en §2 y la razón de ser de §8. Una cifra en una skill es una cifra que se va a
   desviar; lo que va en una skill es **cómo consultarla**.
2. **No copiar una skill al segundo repo.** Es lo que ya pasó dieciocho veces, y la divergencia no
   llega de golpe: llega cuando alguien arregla una.
3. **No hacer del spec un YAML.** La prosa es la que sostiene el *por qué*, y los docs 06–12 son la
   prueba. La cabecera es para el gate, no para la persona.
4. **No dejar que el agente invente la descomposición.** Si el plan no sale del molde, lo que falta
   es molde. Improvisarlo es volver a «el siguiente no se parece a los anteriores», con más pasos.
5. **No construir un MCP para envolver un script.** Un MCP es para lo vivo y lo que tiene estado.
   Los tres descartados de §7.3 están ahí con su disparador justamente para que nadie los reabra
   por costumbre.
6. **No saltarse el piloto 0 porque «Eventos ya está hecho».** Es el único de los tres donde
   equivocarse es gratis, y es el único que puede decir que al molde le falta un paso.
7. **No dar por buena una skill nueva sin mutar su gate.** Vale entera la regla del repo: se
   reintroduce el defecto, se confirma el rojo, se restaura **tocando el fichero**.
8. **No escribir la fábrica sin ticket.** `ticket-first` aplica: esto es una épica con sus HU, y el
   propio documento no es la conversación — es lo que quedó de ella.

## 11. Lo que este diseño no contesta

- **Si un spec puede describir algo que no sea un vertical.** Una capacidad nueva tiene su molde
  (doc 08 §4) y cabría en el mismo formato; un cambio de schema suelto, probablemente no. Se sabrá
  en el piloto 0, y forzarlo antes sería escribir el caso general desde un solo caso.
- **Quién escribe el spec.** Aquí se asume que una persona, refinando con una skill. Que un agente
  escriba el spec de otro agente es una vuelta más y **no se toma hoy**: el spec es donde viven las
  tres preguntas, y contestarlas mal es el error que costó cuatro olas.
- **Qué pasa cuando el spec cambia después de construido.** El *downstream* de verdad —re-derivar
  un vertical vivo desde su spec editado— exige saber qué de lo construido es del spec y qué es de
  una decisión posterior, y eso hoy no está escrito en ninguna parte. **Disparador:** el primer
  vertical que tenga que rehacerse por un cambio de negocio.
- **Cuánto se tarda.** No se estima aquí a propósito. El piloto 2 lo mide; una estimación escrita
  antes del piloto 0 es una cifra sin gate, que es de lo que trata §2.
