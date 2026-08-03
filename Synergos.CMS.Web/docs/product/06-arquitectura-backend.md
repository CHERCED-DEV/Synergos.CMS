# Arquitectura del backend — propuesta de límites

> **Estado: PROPUESTA.** Nada de esto está implementado. Es el documento que hay que discutir
> *antes* de crear el primer `.csproj`, porque escribir catorce proyectos es barato y equivocarse
> en los límites obliga a rehacer los catorce.
>
> Insumo medido: [`03-mapa-segregacion.md`](03-mapa-segregacion.md). Molde ya probado en
> producción: [ADR 0130](../adr/0130-la-analitica-de-busqueda-sale-del-cms-a-un-servicio-de-sesion.md).

## 1. Lo que se pidió

> *"Que no mezclemos CMS con APIs, que las APIs estén en proyectos individuales con sus propios
> programas… por dominio debe existir un API que maneje reglas de negocio, a la cual podamos
> comunicarnos tanto desde la UI como desde las vistas del CMS para precarga de cosas y para
> interacción continua desde las features de Angular… me gusta el concepto de manejarlo en un
> monorepo, ya que sería un solo despliegue… tengamos como un proyecto shared para el backend y
> un proyecto core para el backend."*

Cinco requisitos, y conviene tenerlos separados porque no todos cuestan lo mismo:

1. El CMS deja de ser el sitio donde vive la regla de negocio.
2. Cada API es un proyecto con su propio `Program.cs` — arrancable y probable sola.
3. Dos consumidores por igual: Angular (UI) y las vistas Razor (precarga SSR).
4. Monorepo, un solo despliegue.
5. `Core` + `Shared` para no repetir código.

El (3) es el que tiene un costo escondido y del que hay que hablar sin adornos — §7.

## 2. La tensión con CLAUDE.md §6, dicha de frente

El principio dice, textual: *"No introducir abstracciones prematuras. Sin `Shared/`, `Common/`,
`Utils/`."* Y ahora se pide un proyecto `Shared`.

No lo trato como una contradicción que haya que tapar, porque **no lo es si se lee qué prohibía
la regla**: prohibía el proyecto cuyo criterio de admisión es *"lo que no cupo en otro lado"*.
Un `Utils` no se define por lo que contiene, sino por lo que no. Ese proyecto siempre crece,
nunca encoge, y termina acoplando todo con todo — que es exactamente lo que aquí se quiere
evitar.

Un `Shared` con **regla de admisión positiva y verificable** es otra cosa. La condición para
que la regla §6 siga viva es que la regla de admisión exista, sea corta, y **la verifique un
test** — no el criterio del que abre el PR. Sin eso, en seis meses `Shared` es el `Utils` que
§6 prohibía, con otro nombre.

Lo escribo así en §3 y lo cierro con un gate en §10. Si no aceptamos el gate, mi recomendación
es no crear `Shared`.

## 3. Las dos reglas de admisión

**`Synergos.Core` — el vocabulario y las reglas del negocio.**

> Admite un tipo si, al borrarlo, **una regla de negocio deja de ser expresable**.

Aquí viven `Order`, `Reservation`, `Money`, `PaymentOutcome`, `CancellationPolicy`, y las
funciones puras que operan sobre ellos. Cero infraestructura: **no** referencia
`Microsoft.AspNetCore.*`, **no** referencia Umbraco, **no** habla HTTP ni toca disco. Solo
`Microsoft.Extensions.*.Abstractions` cuando haga falta (logging, opciones).

**`Synergos.Shared` — la fontanería que todo host de API repite.**

> Admite un tipo si, al borrarlo, **un host deja de arrancar igual**, y el tipo **no menciona
> ningún sustantivo del negocio**.

Aquí viven la autenticación por llave compartida, las convenciones de `ProblemDetails`, el
`/health`, el `JsonSerializerOptions` común, las políticas de `HttpClient`, el
`JsonEntityStore`. Referencia `Microsoft.AspNetCore.*` sin culpa — es su oficio.

**La frontera es una sola frase, y es la que hace que esto no colapse:**

> `Core` no sabe qué es un host. `Shared` no sabe qué es un pedido. **Ninguno referencia al
> otro.**

Un tipo que parece pertenecer a los dos no existe: está mal cortado y hay que partirlo. Ese es
el caso que el test tiene que hacer doler, porque es el primer paso hacia el `Utils`.

## 4. El grafo

```
        Synergos.Shared            Synergos.Core
     (fontanería de host,        (dominio y reglas,
      sin dominio)                sin host)
              ↖                        ↗
               ╲                      ╱
                Synergos.Api.*  (Program.cs propio)
                        ▲
                        │  SOLO HTTP  ── sin referencia de ensamblado
                        │
              Synergos.CMS.Web ──→ .Application ──→ .Interfaces
```

Tres reglas, las tres verificables por test:

| Regla | Por qué |
|---|---|
| `Synergos.Api.*` **no** referencia `Synergos.CMS.*` | Es lo que hizo real la separación de `Synergos.Sessions`. El día que una API se mude a su repo, no hay nada que desenredar. |
| `Synergos.CMS.*` **no** referencia `Synergos.Api.*` | Simétrico. Si el CMS pudiera llamar en proceso, la API sería una carpeta con ínfulas. |
| `Synergos.Shared` ⊥ `Synergos.Core` | La frontera de §3. |

Esto **extiende** el ADR 0002, no lo reemplaza: `Interfaces ← Application ← Web ← Tests` sigue
siendo el grafo interno del CMS. Lo que se agrega es un segundo árbol al lado, unido solo por
HTTP.

## 5. Qué APIs — y por qué no catorce

El mapa F3 midió catorce verticales, pero **verticales no son APIs**. Tres de los catorce no
tienen negocio propio, y dos "engines" que el mapa marcó como enredo son en realidad los que
más merecen ser API.

El criterio que usé: **una API por conjunto de reglas que cambian juntas y tienen un dueño
único**. No por menú del sitio.

### Los dos motores primero (lo que el mapa F3 ya recomendaba)

| API | De qué es dueña | Consumidores medidos |
|---|---|---|
| **`Synergos.Api.Commerce`** | pedido, carrito, pago, devolución, seguimiento, precio, tarifas Gov, ticketing, matrícula | **10 controllers**: Shop, ShopCatalog, Eventos, Gov, Academy, Booking, Travel, Realty, Ehr, PaymentWebhook |
| **`Synergos.Api.Scheduling`** | reserva, disponibilidad, política de cancelación, agenda, mapa de asientos | **6 controllers**: Booking, Travel, Eventos, Realty, Ehr, HealthcareApi |

Estos dos no son verticales: son lo que hoy **acopla** a los verticales. El mapa lo dijo con
todas las letras — *"partir un vertical enredado antes de extraer el núcleo reintroduce el
acople por otra vía"*. Un trámite gubernamental hoy se persiste como un pedido
(`StubApplicationService` lee `PersistedOrder`); eso no es un accidente que haya que arreglar,
es la señal de que hay **un** motor de pedido y seis clientes.

### Después, los verticales con negocio propio

| API | De qué es dueña | Nota |
|---|---|---|
| **`Synergos.Api.Identity`** | member gate, auth, roster, 2FA, consentimiento, RTBF | `IMemberAccessGate` lo usan los **14**. Va temprano. |
| **`Synergos.Api.Health`** | EHR, PHI, in-basket clínica, prescripción, resultados | El caso más fuerte de todos, y **no por código sino por régimen**: PHI tiene retención, auditoría y control de acceso propios. Merece frontera de proceso aunque el código no lo pidiera. |
| **`Synergos.Api.Gov`** | trámites, expediente, workflow, certificados, correspondencia | Archivos casi exclusivos. |
| **`Synergos.Api.Realty`** | inmuebles, hipoteca, visitas | Casi exclusivo, poca superficie. Buen segundo ensayo. |
| **`Synergos.Api.Eventos`** | evento, aforo, zonas, sesiones | Se apoya en Commerce + Scheduling. |
| **`Synergos.Api.Academy`** | curso, matrícula, certificado | Certificado se solapa con Gov: **misma firma HMAC, distinto documento.** El firmador va a `Shared`, el documento a cada API. |
| **`Synergos.Api.Social`** | blog, comentario, reacción, grafo social, DMs | Hoy un solo `IMessagingService` sirve in-basket clínica, correspondencia Gov y DMs sociales — **tres regímenes regulatorios sobre un stub**. Se parte al extraer. |
| **`Synergos.Api.Catalog`** | fuente e índice de catálogo, búsqueda, búsquedas guardadas, prueba social | Absorbe el vertical "Search". |
| **`Synergos.Sessions`** | señales de sesión y comportamiento | **Ya existe.** ADR 0130. |

Diez APIs. Que suene a muchas es correcto para el conteo y engañoso para el costo: en un
monorepo con un despliegue, una API de más es un `.csproj` y una entrada de configuración, no
un pipeline nuevo.

### Lo que NO sale del CMS, a propósito

Poner un piso al alcance importa tanto como poner el techo.

- **Plataforma SSR** — `ISynHostEmitter`, `IBundleRegistryClient`, `ICompositionReader`, Layout
  Composer, el wrapper de `compDom*`. Sacar esto es sacar el CMS del CMS.
- **Admin/Dashboard, Forms, Flow** — son la cara de operación del propio CMS. El dashboard
  *consume* las APIs; no es una.
- **El schema uSync** — sigue siendo del CMS y sigue siendo la fuente de verdad (ADR 0008).
  Ninguna API autora DocTypes.
- **Branding** — `IBrandingProvider` es presentación.

## 6. De dónde sale el código de las APIs

No hay que inventarlo: **46 de los 80 archivos de `Application/Services/Impl/` son `Stub*`.**
Son la regla de negocio con una implementación de mentira esperando casa. La migración de cada
API es, en su mayor parte, mover su puñado de stubs y darles una implementación de verdad.

Esto también fija qué queda en el CMS: la **seam** (`IPaymentProvider`, `IReservationService`)
se queda en `Synergos.CMS.Interfaces`, y su implementación pasa a ser un cliente HTTP en
`Web/Services/` — exactamente la forma de `HttpSearchAnalyticsStore`. Los controllers y las
vistas **no se enteran**. Ese es el punto entero de que las seams existieran.

## 7. El costo que hay que mirar de frente: el salto de red en SSR

El requisito (3) dice que las vistas Razor precargan desde la API. Hoy eso es una llamada en
memoria. Mañana es HTTP a localhost. Para una página que hoy resuelve seis seams, son seis
saltos en el camino del usuario, más seis maneras nuevas de que la página falle.

Es real y no lo voy a minimizar. Tres formas de tratarlo, en orden de lo que recomiendo:

1. **Endpoints de precarga gruesos.** La vista no pide seis cosas: pide *una*, la que necesita
   la página. Un salto, no seis. Obliga a diseñar la API para su consumidor real — que es sano
   — y es lo que hace que el (3) no sea caro.
2. **Degradar, no reventar.** El molde de ADR 0130 ya está probado: si la API no responde, la
   sección sale vacía y la página se sirve. Es la diferencia entre un incidente de una feature
   y uno del sitio.
3. **Adaptador en proceso, solo si la medición lo pide.** Un `InProc*Client` junto al
   `Http*Client`, elegido por configuración igual que `Synergos:SearchAnalytics:Mode`. **No lo
   propongo para arrancar**: exige que el CMS referencie el ensamblado de la API, y eso rompe la
   regla 2 de §4 que es justamente lo que hace real la separación. Se guarda como escape con
   precio conocido, y solo se paga si un número lo justifica.

Y una precisión sobre "un solo despliegue": **límite de proyecto ≠ límite de proceso.** Diez
proyectos con su `Program.cs` pueden desplegarse como diez procesos en un contenedor, o como
uno solo por ahora. El código no lo decide, y por eso no hay que decidirlo hoy.

## 8. Orden de extracción

El orden lo fija el mapa F3, no el gusto:

| # | Qué | Por qué ahí |
|---|---|---|
| 0 | `Shared` + `Core` + el gate de §10 | Sin cambio de comportamiento. Es la red antes del trapecio. |
| 1 | **`Api.Commerce`** de punta a punta | Es el motor que acopla a seis verticales, y es **la plantilla**: la primera paga el diseño y las nueve siguientes lo copian. |
| 2 | `Api.Scheduling` | El segundo enredo medido. |
| 3 | `Api.Identity` | 14 consumidores; cuanto más tarde, más caro. |
| 4 | `Api.Realty`, `Api.Eventos`, `Api.Gov` | Los de archivos casi exclusivos. Barato, y valida la plantilla en verticales de verdad. |
| 5 | `Api.Health` | El más pesado y el de más régimen. Se hace cuando la plantilla ya no se discute. |
| 6 | `Api.Social`, `Api.Academy`, `Api.Catalog` | Cierre. |

Después del paso 1 hay que **parar y revisar**, con el circuito corriendo. Si la plantilla está
mal, corregirla ahí cuesta una API; en el paso 6, diez.

## 9. Lo que es decisión del arquitecto, no mía

1. **¿Booking y Travel son uno o dos?** Medido: **Booking no tiene ni un archivo exclusivo** en
   Interfaces ni en Application. En código son uno. Si el producto dice que son dos, hay que
   separarlos a mano y eso cuesta; si dice que son uno, `BookingController` y `TravelController`
   son dos caras de `Api.Travel`. **No creo ninguna API de Booking hasta que esto se responda.**
2. **¿Diez APIs o menos?** Se pueden fusionar `Academy` en `Catalog`, o `Eventos` en
   `Scheduling`. Yo no lo haría —la ganancia es un `.csproj` y la pérdida es un límite—, pero es
   una decisión de tamaño de equipo, no de código.
3. **¿Aceptamos el gate de §10?** Si no, no creo `Shared` (ver §2).
4. **¿La UI de Angular pega directo a las APIs, o siempre a través del CMS?** Cambia si las APIs
   necesitan CORS, su propia autenticación de visitante y su borde público. Es la pregunta más
   cara de las cuatro y no depende de nada de lo anterior — se puede responder en el paso 3.

## 10. El gate

Sin esto, lo de arriba es un dibujo. Va como test de arquitectura junto a `LayerRuleTests`, y
falla el build:

- `Synergos.Core` no referencia `Microsoft.AspNetCore.*` ni `Umbraco.Cms.*`.
- `Synergos.Shared` no referencia `Synergos.Core`, y `Core` no referencia `Shared`.
- Ningún tipo público de `Shared` menciona un sustantivo del dominio — lista explícita y corta
  (`Order`, `Payment`, `Reservation`, `Patient`, `Cart`, `Enrollment`, `Case`…). Es tosco a
  propósito: un test que se puede leer en diez segundos es un test que nadie desactiva "un
  momentito".
- `Synergos.Api.*` no referencia `Synergos.CMS.*`, y al revés tampoco.

## 11. Lo que este documento no resuelve

- **Datos.** Diez APIs sobre un SQLite compartido es un monolito con más puertos. Cada API dueña
  de su almacén es la respuesta correcta y no está diseñada aquí. Es el siguiente documento.
- **Transacciones que cruzan APIs.** Pagar un evento toca Commerce *y* Scheduling. Hoy es una
  llamada en memoria; mañana son dos servicios y una consistencia que alguien tiene que definir.
  Es el problema difícil de verdad de esta arquitectura y hay que nombrarlo ahora, no
  descubrirlo en el paso 4.
- **`Synergos.Sessions` sigue sin tests.** Deuda propia, anotada, anterior a esto.
