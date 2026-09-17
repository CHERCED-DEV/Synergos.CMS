# 13 — Auditoría del árbol de servicios

> **Estado: medido contra el disco. Sin código todavía.** Cinco hallazgos, cuatro de ellos
> con issue por abrir. El árbol quedó byte a byte como se encontró.
>
> Alcance: `backend/nucleo/`, `backend/capacidades/` (20) y `backend/orquestadores/` (5).
> El árbol del CMS (`Synergos.CMS.*`) queda fuera: son dos árboles con reglas distintas.
> Documentos hermanos: [06 arquitectura backend](06-arquitectura-backend.md) ·
> [07 diseño atómico](07-diseno-atomico-capacidades.md) ·
> [08 despiece de APIs](08-despiece-apis.md) ·
> [04 cierre de la auditoría anterior](04-cierre-auditoria.md).

## 0. Tres correcciones al encargo, antes de los números

El encargo de esta auditoría traía premisas que no se sostienen contra el fichero. Van acá
porque arrastrarlas habría hecho medir el árbol equivocado.

| Lo que decía el encargo | Lo que hay | Cómo se midió |
|---|---|---|
| `backend/` en la raíz del repo | `Synergos.CMS/backend/` — la raíz del repo **es** `Synergos.CMS/` | `find . -name "*.csproj"` |
| `Synergos.CMS.Web/docs/product/06` a `08` | Correcto | `find . -name "0[6-9]-*.md"` |
| «las tres suites» en verde | **Tres gates rojos** en la máquina del arquitecto | `dotnet test Synergos.CMS.sln` |

Y una cuarta, que decide qué documento manda: **los docs 06 y 07 hablan de
`Synergos.Domain.*` y de «dieciséis capacidades»**. El código y `CLAUDE.md` §0.B dicen
`Synergos.Bff.*` y **veinte**. Se tomó §0.B como vinculante, y los docs 06/07 como lo que
dicen ser en su propio título: *propuesta* de límites, con sus correcciones anotadas en línea.

---

## 1. FASE 0 — los números crudos

### Build y suites

| Medición | Resultado | Comando |
|---|---|---|
| Build de la solución integradora | **0 avisos, 0 errores** · 29 s | `dotnet build Synergos.CMS.sln -v quiet` |
| `Synergos.CMS.Tests` | 2239 / 2239 verde | `dotnet test Synergos.CMS.sln -v quiet` |
| `Synergos.Servicios.Tests` | 633 / 633 verde | ídem |
| `Synergos.Arquitectura.Tests` | **393 / 396 — 3 ROJOS** | ídem |
| Total | 3268 | cuadra con la cifra que afirma `CLAUDE.md` §11 |
| Schema uSync | sano, 0 errores, 0 avisos | `node tools/usync-audit.mjs` |

> **Aviso sobre el comando de verificación.** La primera corrida fue
> `dotnet test … | tail -40`. El `tail` se come el código de salida del pipeline y el comando
> reportó **exit 0 con tres tests en rojo**. El fallo no estaba escondido en el repo: lo
> escondió el comando de verificación. Los resultados de arriba salen de volver a correr la
> suite sin tubería. Es el mismo modo de fallo que `feedback_the_verification_command_can_be_the_one_that_hides_it`.

### Forma del árbol — derivada del disco, no enumerada

| Familia | Proyectos | Comando |
|---|---|---|
| `capacidades/` | 20 | `find capacidades -name "*.csproj" \| wc -l` |
| `nucleo/` | 2 (`Core`, `Shared`) | ídem |
| `orquestadores/` | 5 (`Bff.Core` + 4) | ídem |
| Suite | 1 (`Synergos.Servicios.Tests`) | ídem |

**Referencias de ensamblado** (`grep ProjectReference` sobre cada `.csproj`):

- Las 20 capacidades: exactamente **2** referencias cada una — `Core` + `Shared`. Ninguna
  referencia a otra `Api.*`, a un `Bff.*`, ni al CMS.
- `Core`: **0** referencias. No conoce `Shared`.
- `Shared` → `Core`. Una flecha, no dos — como corrige el [doc 07 §3](07-diseno-atomico-capacidades.md).
- Los 4 BFF: `Core` + `Shared` + `Bff.Core`. Ninguno referencia una capacidad.

**§0.B regla 11 se sostiene a nivel de ensamblado. No hay hueco en el gate de segregación.**

### Tamaño por proyecto (`.cs` de producción)

Mediana de las capacidades: **541 líneas**. Distribución plana salvo tres:

| Proyecto | Ficheros | Líneas | vs. mediana |
|---|---|---|---|
| `Api.Payments` | 13 | **1995** | ×3.7 |
| `Api.Notifications` | 12 | **1552** | ×2.9 |
| `Api.Booking` | 9 | 1160 | ×2.1 |
| `Api.Geo` (la menor) | 7 | 334 | ×0.6 |
| `Shared` | 11 | 1769 | — |
| `Bff.Core` | 10 | 1642 | — |

Las dos grandes lo son por una razón visible en el disco: son las únicas con carpeta
`Transport/` (pasarela Wompi, Resend, verificadores de webhook). El dominio de ambas no es
desproporcionado.

### Olores — conteo y ubicación

| Patrón | Ocurrencias | Dónde |
|---|---|---|
| `DateTime.Now` | **0** | — |
| `GetHashCode()` | **0** | — |
| `catch { }` vacío | **0** | — |
| `new Random` | **0** | — |
| `.GetAwaiter().GetResult()` | **0** | — |
| `.Result` | **1** | `Api.Payments/Domain/Payment.cs:151` — **dentro de un comentario** que explica lo que deliberadamente no se hizo |
| `DateTime.UtcNow` | 5 | solo en `Shared`: `QueryWindow` (2) y `StoreWriteGate` (3) |
| `SemaphoreSlim` | 8 | `Shared/StoreWriteGate` y `Api.Payments/PaymentService` |
| `lock (…)` | 68 | 1 por capacidad + 3 en `Shared` |
| `catch (…)` tipados | 45 | ninguno desnudo; ninguno degrada una escritura (ver §2) |

Comando: `find backend -name "*.cs" … | xargs grep -nE <patrón>`, excluyendo `bin/`, `obj/`
y la suite.

### Tipos públicos e interfaces

Tipos públicos: de 7 (`Core`) a 30 (`Api.Payments`). Nada anómalo.

**30 interfaces en `backend/`. Implementaciones de producción:**

| Interfaz | Impl. producción | Veredicto |
|---|---|---|
| `INotificationSender` | **2** | seam de extensión genuina (por canal) |
| `IPaymentProvider` | **2** | seam de extensión genuina (pasarela real / simulador) |
| Las otras 28 (`I*Store`, `IIdempotencyLedger`, `ISagaLease`, `ISaga*`) | **1** | ver §4 |

De esas 28, **20 no tienen ni un doble de test**. No nacieron «para poder testear».

### Ficheros > 400 líneas

`Bff.Viajes/TripFlow.cs` 624 · `Api.Notifications/NotificationService.cs` 460 ·
`Bff.Tienda/PurchaseFlow.cs` 455 · `Api.Booking/BookingService.cs` 414 ·
`Api.Payments/WompiPaymentProvider.cs` 404. Listados sin juzgar.

---

## 2. FASE 1 — el catálogo, y lo que NO salió

Empieza por lo que **no** hay, porque cuatro de estos habrían sido hallazgos falsos si la
auditoría se hubiera quedado en el primer grep.

### A. Reglas estructurales (§0.B) — limpio

- **A1 · Referencias cruzadas:** ninguna. Medido arriba.
- **A2 · Sustantivos de dominio dentro de `Api.*`:** el grep dio 7 palabras en 20 ficheros.
  **Las 7 son prosa.** XML-docs explicando la agnosticidad («que sirva a una agenda clínica
  y a una habitación de hotel», `Booking/Resource.cs:44`) y dos modismos españoles —«un
  reintento **en curso**», «un envío **en vuelo**»— que no tienen nada que ver con cursos ni
  con vuelos. **Cero en código.**
- **A3 · `if` sobre `Ref.Kind`:** ninguno. Los 7 `.Kind ==` que aparecen son sobre enums
  **propios** de la capacidad (`EngagementKind`, `DiscountKind`), que es su vocabulario, no
  el `Ref` opaco.
- **A4 · Capacidad sin almacén:** las 20 tienen `Storage/`.
- **A5 · Reglas de orden/compensación mal repartidas:** la compensación vive en
  `Bff.Core` (`Compensator`, `CompensationSweeper`, `SagaLease`); ninguna capacidad la
  menciona.
- **A6 · Idempotencia antes del estado:** **correcto en las 19** capacidades que llevan
  libro — y en las 19 con el mismo desplazamiento exacto (llave en la línea *n*, primer
  rechazo de estado en *n+4*). Que sean 19 y no 20 cuadra con la cifra que da §11.

### B. Fallos invisibles

- **B7 · `catch` que degrada una escritura:** ninguno. Los dos candidatos resisten el examen:
  - `NotificationService.cs:135` captura una excepción del adaptador y la convierte en
    `TransportUnavailable` — **no fabrica acuse**, deja el registro consultable, y ocurre
    **fuera** del cerrojo (respeta C16).
  - `SearchEventStore.cs:80` descarta un evento de telemetría y devuelve `false`; el
    contador `_dropped` se expone en `Counters` y **se lee** en `Program.cs:50`, y la
    respuesta HTTP lleva `IngestAcceptedResponse(bool Stored)` con la verdad. Best-effort
    honesto, no pérdida silenciosa.
- **B8 · Valores inventados:** 21 `?? 0`. **Diecinueve son `Math.Max(0, offset ?? 0)`** de
  paginación — ausencia de offset **sí** significa primera página. De los restantes:
  - `Cart/CartEndpoints.cs:68` (`req.Quantity ?? 0`) es **inofensivo**, y a propósito:
    `CartRules.CheckQuantity` rechaza `<= 0` con el comentario *«Poner cero NO es quitar…
    haría que un error de cálculo del cliente vaciara líneas en silencio»*.
  - `Inventory/InventoryEndpoints.cs:87` (`Hold`) también es inofensivo: `CheckAvailable`
    rechaza `<= 0`.
  - `Inventory/InventoryEndpoints.cs:27` **no**. Es el hallazgo H1.
- **B9 · Campo enterrado / lectura sin camino:** ninguno encontrado.
- **B10 · Omisión que afirma:** **una** — H1.
- **B11 · No-determinismo tratado como determinista:** ninguno. Cero `GetHashCode`, cero
  `Random`, cero `DateTime.Now`; el reloj entra por `TimeProvider` inyectado.
- **B12 · Ruta/puerto/usuario de una máquina en algo versionado:** ninguno. Pero el
  **inverso** aparece dos veces: herramientas versionadas que dependen de cómo es *una*
  máquina — H3 y H4.

### C. Dos procesos / un reinicio

- **C13 · Leer-decidir-escribir sin exclusión entre procesos:** cubierto. `StoreWriteGate`
  toma turno con `FileStream` (entre procesos) **y** `SemaphoreSlim` (dentro del proceso);
  `SagaLease` hace lo propio para las sagas. Y hay un test,
  `El_absoluto_SIGUE_perdiendo_ajustes…`, que **afirma el valor equivocado a propósito**
  (`13`, con `// 15 sería lo correcto`) para dejar escrito por qué el arreglo fue cambiar la
  forma de la API (`AdjustBy` con llave) y no meter otro cerrojo: el leer-sumar-escribir
  ocurre en el **llamador**. Eso es diseño documentado, no un defecto blindado.
- **C14 · Tipo que se serializa y no se deserializa:** `IReadOnlySet<T>` aparece **una vez**,
  y es un `static readonly` (`DocumentRules.AllowedContentTypes`), no un miembro persistido.
  Los `IReadOnlyDictionary`/`IReadOnlyList` de los records sí van por `JsonCollectionStore`
  — y el repo ya pagó esa lección: hay `ActorRoundTripTests` y `ActorJsonConverter` en
  `Shared`.
- **C15 · Marca de «en curso» sin vencimiento:** no encontrada; `SagaLease` relee del disco
  al tomar y vence.
- **C16 · Costura síncrona con adaptador de red / `.Result` bajo cerrojo:** ninguno. El
  único `.Result` del árbol está en un comentario que explica justamente que no se hizo.
- **C17 · Granularidad que obliga a barrer:** no encontrada.

### D. Lo que se mide distinto acá

- **D18 · Duplicación:** los 20 `Program.cs` son el molde, y el molde **es** la arquitectura
  (§0.B 15). No cuenta.
- **D19 · Interfaces con una implementación:** 28. Ver §4 — decisión, no ticket.
- **D20 · Parámetro `fallback*`:** **cero**. Grep limpio.

---

## 3. FASE 2 — hallazgos por coste de no arreglarlo

### H1 · Cubeta 1 · Miente

**`Api.Inventory` declara «conté y hay cero» cuando el llamador no contó.**

- **Ruta:** `backend/capacidades/Synergos.Api.Inventory/Endpoints/InventoryEndpoints.cs:27`
- **Qué pasa:** el endpoint hace `svc.Declare(subject, req.OnHand ?? 0, units, key)`.
  `DeclareStockRequest` declara `int? OnHand` — genuinamente opcional en el JSON.
  `InventoryService.Declare` rechaza `onHand < 0` y **acepta `0`**. Un `POST /v1/items` sin
  `onHand` crea el ítem con existencias en cero y contesta **201 Created**.
- **Qué rompe en producción:** el ítem queda afirmando que se contó y no hay nada. Y no se
  puede rehacer: `subject_taken` bloquea volver a declarar el mismo `Ref`, así que
  corregirlo exige pasar por `adjust`. El contrato dice, en su propio XML-doc, que *«`OnHand`
  es absoluto («conté y hay 47»)»* — omitirlo es exactamente **no** haber contado.
- **Lo que lo vuelve un hallazgo y no una opinión:** **el mismo fichero, 60 líneas más abajo,
  trata la misma omisión al revés.** `POST /v1/items/{id}/adjust` rechaza con
  `adjust_required` («Hace falta delta u onHand») y rechaza con `ambiguous_adjust` si van los
  dos. Una misma capacidad, un mismo nombre de campo, dos semánticas opuestas para la
  ausencia.
- **¿Por qué ningún test lo vio?** **Reparto, no falta de test.** `InventoryServiceTests`
  tiene 40+ casos, pero todos entran por `svc.Declare(subject, onHand, …)` con el valor
  explícito: el helper es `ConExistencias(int onHand)`. El `?? 0` vive en el **endpoint**, y
  para `Api.Inventory` no hay ni un test de endpoint ni filtro de validación registrado. Las
  dos mitades están verdes y el hueco cae justo en la costura entre ellas.
- **Mutación que lo reproduce:** cambiar `req.OnHand ?? 0` por `req.OnHand ?? -1` y correr la
  suite entera: **sigue verde**. Eso prueba que nada ejerce esa línea. En vivo:
  `POST /v1/items` con `{"subjectKind":"x","subjectId":"y"}` y sin `onHand` → 201 con
  `onHand: 0`.

### H2 · Cubeta 3 · Cuesta

**`tools/service-matrix.mjs` es un no-op silencioso en Windows, y su red de seguridad es
código muerto.**

- **Ruta:** `tools/service-matrix.mjs:73`
- **Qué pasa:** el guard de entrada CLI es
  ``if (import.meta.url === `file://${process.argv[1]}`)``. En Windows `argv[1]` llega como
  `C:\Users\…\tools\service-matrix.mjs` (barras invertidas) e `import.meta.url` es
  `file:///C:/Users/…`. **Nunca iguala**, invocado con ruta relativa o absoluta. El bloque
  CLI no corre: salida vacía, **exit 0**.
- **Qué rompe:** en CI no rompe nada — `ubuntu-latest` sí iguala, y el workflow
  `images.yml:46` construye sus imágenes. **El despliegue no está roto.** Lo que rompe es
  local: `ContainerBuildTests` queda en rojo permanente en la máquina del arquitecto, con un
  `JsonException` que no nombra la causa. El coste real es el de convivir con tres rojos: el
  día que uno sea de verdad, ya nadie mira.
- **Lo grave está al lado:** el script tiene su red de seguridad escrita —
  `if (lista.length === 0) { console.error(…); process.exit(1) }` — con el comentario *«Un
  gate que no puede fallar es peor que no tener gate»*. **Esa red está dentro del bloque que
  no corre.** Es exactamente el defecto que el fichero existe para evitar, un nivel más
  arriba.
- **Y el gate tampoco lo nombra:** `ContainerBuildTests.cs:166` afirma `ExitCode == 0` — que
  **se cumple** — y revienta después al parsear el JSON vacío. Su propio diagnóstico nunca
  dispara.
- **¿Por qué ningún test lo vio?** El gate corre en CI Linux, donde el guard sí iguala. El
  gate es correcto; el entorno donde falla es el único donde nadie lo mira.
- **Mutación:** ejecutar `node tools/service-matrix.mjs` en Windows — salida vacía, exit 0.
  El arreglo ya está en el repo, siete veces: los otros 7 `.mjs` usan
  `fileURLToPath(import.meta.url)`.

### H3 · Cubeta 3 · Cuesta

**`compose-gen --check` no tolera el fin de línea, y da «desactualizado» sobre un fichero que
está al día.**

- **Ruta:** `tools/compose-gen.mjs:45` (`--check`) contra `compose.prod.yml`
- **Qué pasa:** el generador emite `\n`; el fichero en el árbol de trabajo está en CRLF
  (`core.autocrlf`). `--check` compara cadenas crudas → no coinciden → gate rojo.
- **Probado, no inferido:** se respaldó el fichero, se regeneró y se comparó. `git diff` sale
  vacío (git normaliza). Byte a byte difieren en **exactamente 1017 bytes para 1017 líneas**.
  Y la prueba directa: `tr -d '\r' < respaldo` es **idéntico** a lo generado. *La única
  diferencia era el fin de línea.* El árbol quedó restaurado byte a byte y `git status`
  limpio.
- **Qué rompe:** el mensaje del gate dice *«se añade una capacidad y nadie regenera: el
  servicio nuevo simplemente NO se despliega»* — una frase seria, dicha sobre un falso
  positivo. Segundo rojo permanente, y este además **entrena a desconfiar del gate que sí
  importa**.
- **Corolario:** el criterio de cierre «`compose.prod.yml` sale byte-idéntico» **no es
  verificable hoy en Windows**. Hay que arreglar H3 antes de poder usarlo como prueba.

### H4 · Cubeta 3 · Cuesta

**`provisionar.sh --autoprueba` depende de `python3`, que en Windows es el stub de la
Microsoft Store — y el stub sale con 0.**

- **Ruta:** `tools/provisionar.sh:73`
- **Qué pasa:** Python **sí** está instalado (3.11.9, como `python`). Lo que resuelve
  `python3` es `…/WindowsApps/python3`, el alias de la Store, que imprime *«no se encontró
  Python»* y **devuelve exit 0**. El lector recibe 0 líneas donde esperaba 1.
- **Qué rompe:** nada en producción — el script corre en el servidor Linux, donde `python3`
  viene de fábrica, y el propio comentario de la línea 240 lo dice. Rompe el tercer rojo
  local. El agravante es el exit 0 del stub: un fallo que se presenta como éxito.
- **¿Por qué ningún test lo vio?** Lo vio: `ProvisionWiringTests` está rojo y su mensaje es
  el mejor de los tres. El problema no es la detección, es que el gate no distingue «el
  lector está mal» de «acá no hay `python3`».

### H5 · Cubeta 4 · Incomoda — **no se toca en este trabajo**

Dos derivas de documentación, anotadas para que la próxima auditoría no las redescubra desde
cero:

1. Los docs 06/07 dicen `Synergos.Domain.*` y «dieciséis capacidades» frente a
   `Synergos.Bff.*` y veinte.
2. **La serie numerada de `product/` está partida en dos carpetas**: `00`–`10` y `12` viven
   en `Synergos.CMS.Web/docs/product/`, y el `11` en `Synergos.CMS/docs/product/`. Los dos
   últimos se tocaron el mismo día (2026-09-15), así que la partición está viva y no es una
   migración a medias. El siguiente agente tiene que adivinar dónde poner el suyo.

Se anota y se deja: §4.3 prohíbe mezclar arreglo con cosmética.

### Cubeta 2 · Pierde — **vacía**

No se encontró ningún hallazgo que pierda una escritura, un rastro o dinero con dos procesos
o tras un reinicio. Es el resultado que menos se esperaba y el que más se verificó: turno de
escritura entre procesos, arriendo de saga que relee del disco, idempotencia antes del estado
en las 19, y la red por fuera del cerrojo en las dos capacidades que hablan con un tercero.

---

## 4. Lo que se decide NO hacer — con la razón, no con un «todavía»

- **Las 28 interfaces de una sola implementación no se tocan.** No es «todavía»: el molde es
  idéntico en las veinte por decisión (§0.B 15), y quitar `I*Store` en 18 y dejarla en las 2
  que sí tienen dos implementaciones rompe la uniformidad que `ApiMoldTests` vigila. Además
  la prohibición de §6 apunta a interfaces inventadas *para poder testear*, y **20 de las 28
  no tienen ni un doble de test**: no son ese caso.
- **Los 68 `lock` no se unifican.** El turno de escritura vive en el borde a propósito, y la
  razón está escrita.
- **Los 20 `Program.cs` no se deduplican.** Son la arquitectura.
- **El almacén JSON no se cambia por una base de datos.** Los dos disparadores están escritos
  y ninguno se cumplió.
- **Los `Synergos.Api.*/` y `Synergos.Bff.*/` sueltos en la raíz de `Synergos.CMS/` no se
  borran como parte de esta auditoría.** Son 15 directorios con un `launchSettings.json` cada
  uno, **no versionados** (`git ls-files` vacío) — basura local de `dotnet run` con el layout
  anterior al #136. No es un defecto del repo; es el escritorio de una máquina.
- **`Api.Payments` no se parte** pese a ser ×3.7 la mediana. El exceso está en `Transport/`
  (pasarela real), no en el dominio, y partir por tamaño sin que cambie el «no» que la
  capacidad sabe decir sola contradice el [doc 07 §1](07-diseno-atomico-capacidades.md).

---

## 5. Orden de ejecución propuesto — una fase por commit

Ninguno se codifica antes de abrir su issue (`ticket-first.yml` lo rechaza).

| # | Qué | Hallazgo | Plantilla | Por qué va en ese orden |
|---|---|---|---|---|
| 1 | Guard CLI de `service-matrix.mjs` → `fileURLToPath` | H2 | `1-defecto` | Desbloquea el gate y **devuelve la vida a la red de seguridad**, que es lo que protege al resto |
| 2 | Gate que muta H2: red de seguridad vista en rojo | H2 | (mismo commit) | Un gate que no se vio fallar no está |
| 3 | `--check` de `compose-gen` normaliza fin de línea | H3 | `1-defecto` | Hasta acá no se puede usar «byte-idéntico» como prueba |
| 4 | `provisionar.sh`: resolver el intérprete y **fallar si no hay**, nunca salir 0 | H4 | `1-defecto` | Cierra el tercer rojo; las suites quedan verdes y vuelven a significar algo |
| 5 | `Declare` deja de convertir la ausencia en cero | **H1** | `1-defecto` | Va al final **a propósito**: es el único que toca producto, y el diff tiene que ser limpio sobre un árbol verde |
| 6 | Gate de endpoint para `Api.Inventory` + mutación | H1 | (mismo commit) | El hueco era de reparto; el gate tiene que vivir donde estaba el hueco |
| 7 | Regla nueva a `CLAUDE.md` §5 | — | (mismo commit que 5) | «Una omisión afirma también cuando el borde la resuelve con `?? 0` y la regla solo mira el negativo» |

**Las fases 1–4 no cambian la topología desplegada**, así que `compose.prod.yml` debe salir
byte-idéntico — y desde la fase 3, eso por fin se puede comprobar.
