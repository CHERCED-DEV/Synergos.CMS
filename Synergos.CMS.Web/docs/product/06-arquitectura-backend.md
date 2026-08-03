# Arquitectura del backend — propuesta de límites

> **Estado: PROPUESTA**, salvo el paso 0 que ya está hecho (`Synergos.Shared` + el gate).
>
> Insumo medido: [`03-mapa-segregacion.md`](03-mapa-segregacion.md). Molde ya probado en
> producción: [ADR 0130](../adr/0130-la-analitica-de-busqueda-sale-del-cms-a-un-servicio-de-sesion.md).

## 1. Lo que se pidió

> *"Que no mezclemos CMS con APIs, que las APIs estén en proyectos individuales con sus propios
> programas… por dominio debe existir un API que maneje reglas de negocio, a la cual podamos
> comunicarnos tanto desde la UI como desde las vistas del CMS… monorepo, un solo despliegue…
> un proyecto shared y un proyecto core."*

Y después, la corrección que reordenó todo:

> *"El sistema de Booking no solo sea de traveling… también un sistema de healthcare puede tener
> Booking, porque yo necesito hacer la cuestión de las citas médicas. Separémoslas y que sean
> integrables según con qué negocio vayan a relacionarse… que sean agnósticas, que tengan reglas
> de negocio, pero que sean estables una vez podamos utilizarlas desde una capa media para cada
> dominio. Es como un orquestador por cada dominio."*

Eso no es un ajuste de la lista: **es una capa más**, y es la que hace que la lista se sostenga.

## 2. La tensión con CLAUDE.md §6, dicha de frente

El principio dice, textual: *"No introducir abstracciones prematuras. Sin `Shared/`, `Common/`,
`Utils/`."* Y se pidió un proyecto `Shared`.

No es una contradicción si se lee **qué prohibía la regla**: el proyecto cuyo criterio de
admisión es *"lo que no cupo en otro lado"*. Un `Utils` no se define por lo que contiene sino
por lo que no; siempre crece, nunca encoge, y termina acoplando todo con todo — que es lo que
aquí se quiere evitar.

Un `Shared` con **regla de admisión positiva y verificada por un test** es otra cosa. La
condición es que la verifique el test y no el criterio del que abre el PR; sin eso, en seis
meses es el `Utils` que §6 prohibía con otro nombre. El gate está en §11 y ya corre.

## 3. Tres capas, no dos

Es el corazón de la propuesta. Una capacidad y un dominio **no son pares**.

| Capa | Sabe | No sabe | Ejemplo |
|---|---|---|---|
| **Capacidad** — `Synergos.Api.*` | reservar, cobrar, autenticar, indexar, mensajear | qué es una cita médica | Booking sabe *recurso + ventana + cupo + hold*. No sabe que el recurso es un médico. |
| **Orquestador** — `Synergos.Domain.*` | las reglas del negocio; compone capacidades | cómo se persiste una reserva | Salud sabe que una cita exige consentimiento vigente, un profesional con la especialidad y una franja libre. |
| **Consumidores** — CMS, Angular | qué pintar | todo lo anterior | |

### La regla que hace o rompe esto

> **La capacidad es dueña del *cuándo*. El orquestador es dueño del *qué*.**

Y su versión operativa, que se puede aplicar archivo por archivo:

> Si la capacidad tiene que **leer** un campo para decidir algo, el campo es suyo. Si solo lo
> **guarda y lo devuelve**, es del orquestador y viaja opaco.

### El modo de fallo que hay que nombrar ahora

Una capacidad agnóstica se muere de una de dos maneras, y las dos empiezan igual de bien:

1. **Se vuelve una bolsa.** Para servir a citas médicas, noches de hotel y butacas de teatro, el
   modelo se generaliza hasta ser un `Dictionary<string, object>` con fechas. Entonces no tiene
   reglas, y una API sin reglas es una base de datos con HTTP: costo de red sin ganancia.
2. **Se llena de campos de un dominio.** Aparece `SpecialtyCode` "solo por ahora" y Booking deja
   de ser agnóstica sin que nadie lo decida.

El criterio contra el (1) es corto y verificable:

> **Una capacidad tiene que poder decir NO por sí sola.** Booking rechaza por sobrecupo, ventana
> cerrada, hold vencido o política de cancelación — sin preguntarle a nadie qué es el recurso.
> Si no puede rechazar nada por su cuenta, no tiene reglas de negocio y no merece ser una API.

Contra el (2) hay gate (§11): ningún tipo público de `Synergos.Api.*` nombra un dominio.

## 4. El grafo

```
   Synergos.Shared              Synergos.Core
 (fontanería de host,      (vocabulario y reglas,
  sin dominio)              sin host)
          ↖                        ↗
           ╲                      ╱
      Synergos.Api.*   ← CAPACIDADES (agnósticas, pocas, estables)
            ▲
            │  HTTP
            │
    Synergos.Domain.*  ← ORQUESTADORES (uno por negocio)
            ▲
            │  HTTP   ── el CMS y Angular hablan SOLO con esta capa
            │
   Synergos.CMS.Web ──→ .Application ──→ .Interfaces        Angular (Synergos.UI)
```

Reglas, todas verificables por test:

| Regla | Por qué |
|---|---|
| `Api.*` **no** referencia `Domain.*` | Una capacidad que conoce un dominio dejó de ser agnóstica. Es la flecha que se invierte sola si nadie mira. |
| `Api.*` y `Domain.*` **no** referencian `Synergos.CMS.*`, ni al revés | Lo que hizo real la separación de `Synergos.Sessions` (ADR 0130): mudarla a su repo no tiene nada que desenredar. |
| El CMS y Angular **no** llaman una capacidad directa | Si pudieran, la regla de dominio se reimplementaría en la vista — el mismo error de forma que el `if (brand.Key == "X")` que prohíbe el ADR 0010. |
| `Shared` ⊥ `Core` | La frontera de §5. |

Esto **extiende** el ADR 0002, no lo reemplaza: `Interfaces ← Application ← Web ← Tests` sigue
siendo el grafo interno del CMS. Al lado crece un segundo árbol, unido solo por HTTP.

## 5. Las dos reglas de admisión: `Core` y `Shared`

**`Synergos.Core` — el vocabulario y las reglas del negocio.**

> Admite un tipo si, al borrarlo, **una regla de negocio deja de ser expresable**.

`Money`, `TimeWindow`, `Outcome`. Cero infraestructura: no referencia `Microsoft.AspNetCore.*`,
no referencia Umbraco, no habla HTTP ni toca disco.

**`Synergos.Shared` — la fontanería que todo host repite.**

> Admite un tipo si, al borrarlo, **un host deja de arrancar igual**, y el tipo **no menciona
> ningún sustantivo del negocio**.

Llave compartida, `ProblemDetails`, `/health`, ventanas de consulta, políticas de `HttpClient`.
Referencia `Microsoft.AspNetCore.*` sin culpa — es su oficio.

**La frontera, en una frase:**

> `Core` no sabe qué es un host. `Shared` no sabe qué es un pedido. **Ninguno referencia al
> otro.**

Un tipo que parece pertenecer a los dos no existe: está mal cortado y hay que partirlo. Ese es
el caso que el test tiene que hacer doler, porque es el primer paso hacia el `Utils`.

## 6. Las capacidades — seis, y por eso pocas

Son el activo caro: se diseñan una vez, las usan todos, y equivocarse ahí se paga ocho veces.

| Capacidad | De qué es dueña | Qué puede rechazar SOLA | Consumidores medidos |
|---|---|---|---|
| **`Api.Booking`** | recurso, disponibilidad, hold con TTL, confirmación, política de cancelación | sobrecupo, ventana cerrada, hold vencido, cancelación fuera de plazo | **6 controllers** / Salud, Travel, Eventos, Realty |
| **`Api.Commerce`** | pedido, carrito, cobro, devolución, seguimiento | monto inválido, pedido ya cobrado, devolución fuera de plazo, reintento duplicado | **10 controllers** / Shop, Gov, Academy, Eventos, Travel, Realty |
| **`Api.Identity`** | identidad, roles, 2FA, consentimiento, derecho al olvido | credencial inválida, rol ausente, consentimiento vencido | **14** (todos) |
| **`Api.Catalog`** | indexar y buscar cualquier cosa publicable, búsquedas guardadas | consulta vacía, filtro desconocido, página fuera de rango | Shop, Realty, Eventos, Academy, Travel, Gov |
| **`Api.Messaging`** | hilo, mensaje, adjunto, acuse de lectura | hilo cerrado, participante ajeno, adjunto sobre el límite | Salud, Gov, Social |
| **`Api.Sessions`** | señales de sesión y comportamiento | evento vacío, ventana invertida, límite sobre el tope | **Ya existe** (ADR 0130) |

Dos notas que salen de la medición y no del gusto:

- **`Api.Sessions` ya era una capacidad**; solo no teníamos la palabra. Nació agnóstica —no sabe
  qué es una búsqueda de inmuebles ni de cursos— y por eso encajó sin retocarla. Es la prueba de
  que el molde funciona, no una analogía.
- **`Api.Messaging` es el caso más urgente de partir.** Hoy **un solo `IMessagingService`** sirve
  la in-basket clínica, la correspondencia gubernamental y los DMs sociales: *tres regímenes
  regulatorios sobre un stub*. Como capacidad, el transporte es uno y **el régimen lo pone cada
  orquestador** — que es exactamente la separación que hoy no existe.

## 7. Los orquestadores — uno por negocio

Aquí vive el vocabulario, y es donde el producto crece. Son **delgados a propósito**: componen
capacidades y aplican las reglas que solo tienen sentido en su negocio.

| Orquestador | Ejemplo de regla que es SUYA y de nadie más |
|---|---|
| `Domain.Salud` | una cita exige consentimiento vigente y un profesional con la especialidad; el PHI no sale de aquí |
| `Domain.Travel` | una noche de hotel y un vuelo se cancelan con políticas distintas y se cobran juntos |
| `Domain.Eventos` | el aforo por zona y el mapa de asientos deciden qué es "disponible" |
| `Domain.Realty` | una visita necesita al propietario notificado; la hipoteca no es un cobro |
| `Domain.Gov` | un trámite avanza por estados y puede exigir tasa antes de radicar |
| `Domain.Academy` | matricularse cierra cupo y emite certificado al aprobar |
| `Domain.Shop` | el inventario y la promoción deciden el precio antes de que Commerce cobre |
| `Domain.Social` | la moderación decide qué se publica; los DMs tienen bloqueo entre personas |

Que sean delgados es el punto: **si un orquestador engorda, casi siempre es una capacidad que le
falta a la fila de arriba** — y esa es la señal para promover, no para copiar el código al
noveno dominio.

### Lo que NO sale del CMS, a propósito

Poner un piso al alcance importa tanto como poner el techo.

- **Plataforma SSR** — `ISynHostEmitter`, `IBundleRegistryClient`, `ICompositionReader`, Layout
  Composer, el wrapper de `compDom*`. Sacar esto es sacar el CMS del CMS.
- **Admin/Dashboard, Forms, Flow** — son la cara de operación del propio CMS. El dashboard
  *consume* los orquestadores; no es uno.
- **El schema uSync** — sigue siendo del CMS y sigue siendo la fuente de verdad (ADR 0008).
  Ninguna API autora DocTypes.
- **Branding** — `IBrandingProvider` es presentación.

## 8. De dónde sale el código

No hay que inventarlo: **46 de los 80 archivos de `Application/Services/Impl/` son `Stub*`.** Son
la regla de negocio con una implementación de mentira esperando casa. La migración de cada API
es, en su mayor parte, mover su puñado de stubs y darles una implementación de verdad.

Esto también fija qué queda en el CMS: la **seam** (`IPaymentProvider`, `IReservationService`) se
queda en `Synergos.CMS.Interfaces`, y su implementación pasa a ser un cliente HTTP en
`Web/Services/` — exactamente la forma de `HttpSearchAnalyticsStore`. Los controllers y las
vistas **no se enteran**. Ese es el punto entero de que las seams existieran.

## 9. El costo: ahora son dos saltos, no uno

Con tres capas, la precarga de una vista Razor es `CMS → Domain → Api`. Hay que mirarlo de
frente, porque es el precio del modelo.

1. **El orquestador existe justamente para pagar ese salto.** Su trabajo es colapsar N llamadas a
   capacidades en **una** llamada de dominio, abanicando en paralelo por dentro. Una vista que
   hoy resuelve seis seams hace **un** salto, no seis: el segundo tramo es interno y concurrente.
   Bien hecho, tres capas son *menos* latencia de usuario que dos mal cortadas.
2. **Endpoints gruesos, no genéricos.** La vista pide *lo que necesita la página*, no seis cosas
   que ella junta. Obliga a diseñar el orquestador para su consumidor real, que es sano.
3. **Degradar, no reventar.** El molde de ADR 0130 ya está probado: si el otro lado no responde,
   la sección sale vacía y la página se sirve.
4. **Adaptador en proceso, solo si la medición lo pide.** Un `InProc*Client` elegido por
   configuración, como `Synergos:SearchAnalytics:Mode`. **No se propone para arrancar**: exige que
   el CMS referencie el ensamblado, y eso rompe la regla que hace real la separación. Se guarda
   como escape con precio conocido, y se paga solo si un número lo justifica.

Y una precisión sobre "un solo despliegue": **límite de proyecto ≠ límite de proceso.** Catorce
proyectos con su `Program.cs` pueden desplegarse como catorce procesos o como uno; el código no
lo decide, y por eso no hay que decidirlo hoy.

## 10. Orden de extracción

| # | Qué | Por qué ahí |
|---|---|---|
| 0 | `Shared` + el gate | **HECHO.** Sin cambio de comportamiento: la red antes del trapecio. |
| 1 | **`Api.Booking` + `Domain.Salud`**, de punta a punta | Ver abajo. Es la plantilla y a la vez la prueba de la tesis. |
| 2 | `Domain.Travel` sobre la **misma** `Api.Booking` | El momento de la verdad: si hay que tocar Booking para que sirva a un hotel, el corte estaba mal. Barato de corregir con dos dominios; carísimo con ocho. |
| 3 | `Api.Commerce` + `Domain.Shop` | El motor que acopla a seis. Con la plantilla ya validada. |
| 4 | `Api.Identity` | 14 consumidores; cuanto más tarde, más caro. |
| 5 | `Domain.Eventos`, `Domain.Realty`, `Domain.Gov` | Archivos casi exclusivos: baratos, y ejercitan Booking + Commerce a la vez. |
| 6 | `Api.Messaging` + partir los tres regímenes | Se hace cuando Salud, Gov y Social ya existen como orquestadores. |
| 7 | `Api.Catalog`, `Domain.Academy`, `Domain.Social` | Cierre. |

**Por qué Booking primero y con Salud, y no con Travel.** La afirmación a probar es *"Booking no
es de Travel"*. Estrenarla con Travel no probaría nada: es de donde salió, y encajaría por
construcción. Con Salud sí. Si `Api.Booking` sirve una cita médica sin saber qué es un médico, la
tesis se sostiene; y si no, se descubre en el primer dominio en vez del octavo.

Después del paso 2 hay que **parar y revisar**, con los dos dominios corriendo sobre la misma
capacidad. Si la plantilla está mal, corregirla ahí cuesta una capacidad y dos dominios.

## 11. El gate

Sin esto, lo de arriba es un dibujo. Vive en `Synergos.CMS.Tests/Architecture/
BackendSegregationTests.cs`, junto a `LayerRuleTests`, y falla el build.

Lee los `.csproj` **del disco** y no los ensamblados: interesa lo *declarado* —una referencia
entre una API y el CMS ya es el acople, la use o no todavía— y así **cubre los proyectos que aún
no existen**. El día que aparezca `Synergos.Api.Booking`, estas reglas ya lo están vigilando.

Corriendo hoy:

- `Shared` no referencia ningún proyecto del repo; `Core` tampoco, ni conoce ASP.NET ni Umbraco.
- Ningún tipo público de `Shared` menciona un sustantivo del negocio — lista explícita y corta.
- Ninguna API referencia el CMS, ni el CMS una API.
- Un test que verifica **que el gate ve los proyectos**: sin él, un descubrimiento roto dejaría
  todos los demás en verde sobre una lista vacía. Un gate que no puede fallar es peor que no
  tener gate, porque da la señal de que se está vigilando.

Pendiente de agregar con la primera capacidad:

- `Api.*` no referencia `Domain.*`.
- Ningún tipo público de `Api.*` nombra un dominio (lista distinta de la de `Shared`: en una
  capacidad, `Reservation` y `Order` **sí** son vocabulario propio; `Patient` y `Tramite` no).

## 12. Decisiones abiertas

1. ~~**¿Booking y Travel son uno o dos?**~~ **Resuelto:** dos, y Booking sube a capacidad —
   Salud la necesita para citas médicas. Es lo que motivó la capa de orquestadores.
2. **¿Angular pega directo a los orquestadores, o siempre a través del CMS?** La más cara de las
   que quedan: cambia si los orquestadores necesitan CORS, autenticación de visitante y borde
   público propio. No bloquea los pasos 1 y 2.
3. **¿Se renombra `Synergos.Sessions` → `Synergos.Api.Sessions`?** Hoy cuesta nada —un proyecto,
   cero consumidores por ensamblado— y deja la capa legible en el nombre. Después cuesta.
4. **¿Seis capacidades y ocho orquestadores, o menos?** Se pueden fusionar; yo no lo haría, pero
   es decisión de tamaño de equipo, no de código.

## 13. Lo que este documento no resuelve

- **Datos.** Catorce servicios sobre un SQLite compartido es un monolito con más puertos. Cada
  capacidad dueña de su almacén es la respuesta correcta y no está diseñada aquí.
- **Transacciones que cruzan capacidades.** Pagar un evento toca Commerce *y* Booking: se cobra y
  después hay que confirmar el cupo, y si lo segundo falla hay que devolver. Hoy es una llamada
  en memoria; mañana son dos servicios y una consistencia que alguien tiene que definir. **Es el
  problema difícil de verdad de esta arquitectura**, y hay que nombrarlo ahora, no descubrirlo en
  el paso 5. La respuesta probable es *hold primero, cobro después, compensación explícita* — y
  es precisamente por eso que `Api.Booking` tiene holds con TTL en §6.
- **`Synergos.Sessions` sigue sin tests propios.** Lo que se movió a `Shared` sí quedó cubierto.
