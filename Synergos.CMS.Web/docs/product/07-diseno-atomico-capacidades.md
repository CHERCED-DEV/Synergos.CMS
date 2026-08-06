# Diseño atómico de capacidades — el catálogo y los tipados

> Complementa [`06-arquitectura-backend.md`](06-arquitectura-backend.md), que fijó las tres
> capas. Este documento responde las dos preguntas que quedaban: **cuántas capacidades y dónde
> se corta cada una**, y **qué tipos hablan todas** para que la capa media componga en vez de
> traducir.

## 1. Qué significa "atómico" acá, y dónde para

El diseño atómico funciona porque tiene un piso: un átomo es lo más pequeño que **sigue siendo
algo**. Un botón es un átomo; media sombra de un botón no es nada.

En microservicios el piso equivalente no es el tamaño: es la **autonomía**. Partir de más produce
servicios que no pueden contestar nada sin llamarse entre ellos — un monolito distribuido, que
tiene todos los costos de la red y ninguna de las ventajas de separar. Dos criterios, y hay que
pasar **los dos**:

| Criterio | Por qué |
|---|---|
| **Puede decir NO por sí sola** | Si para rechazar algo tiene que preguntarle a una hermana, no tiene reglas: tiene endpoints. Y una API sin reglas es una base de datos con HTTP. |
| **Es dueña de su propio almacén** | Si dos "capacidades" tienen que leer la misma tabla para hacer su trabajo, son **una**. Compartir almacén es el acople que la red no rompe — solo lo esconde. |

Con esos dos filtros salen **dieciséis**. No es un número que buscara; es lo que queda cuando se
aplican, y hay cortes que sorprenden —`Booking` y `Inventory` separadas, `Messaging` y
`Notifications` separadas— que aparecen precisamente porque el almacén y el "no" son distintos.

**Y hay un tercer filtro, que es el que más se olvida:** lo que no tiene almacén **no es un
servicio, es un tipo**. Una política de cancelación es una función pura de (política, reloj,
reserva) → decisión. Convertirla en microservicio es pagar red por una multiplicación. Va a
`Synergos.Core` y la usa quien la necesite. *Ahí* es donde el diseño atómico entra de verdad: en
los tipos, no en los procesos.

## 2. El catálogo — dieciséis capacidades

Cada una: qué posee, qué puede rechazar **sola**, y quién la consume hoy.

### Identidad y gobierno

| Capacidad | Posee | Rechaza sola | Consumidores |
|---|---|---|---|
| `Api.Identity` | principales, credenciales, roles, 2FA | credencial inválida, rol ausente, 2FA no superado | **14** |
| `Api.Consent` | otorgamientos, revocaciones, derecho al olvido | consentimiento vencido, revocado, nunca otorgado | Salud, Gov, Social |
| `Api.Audit` | bitácora append-only, retención | entrada sin actor, ventana fuera de retención | **9** (`IAuditTrailWriter`) |
| `Api.Signing` | llaves, firma y verificación (HMAC/JWS) | firma inválida, llave vencida, algoritmo no permitido | tickets, certificados Gov/Academy, webhooks |

`Consent` sale de `Identity` porque su régimen de retención es distinto y porque Gov y Salud
necesitan consentimiento **sin** ser dueños de la identidad. `Signing` sale porque hoy la misma
firma HMAC está escrita tres veces —tickets, certificados, webhooks— con tres manejos de llave.

### Tiempo y espacio

| Capacidad | Posee | Rechaza sola | Consumidores |
|---|---|---|---|
| `Api.Booking` | recursos, calendarios, disponibilidad, holds con TTL, reservas | sobrecupo, ventana cerrada, hold vencido, doble confirmación | Salud, Travel, Eventos, Realty |
| `Api.Inventory` | existencias, apartados de stock | stock insuficiente, apartado vencido | Shop, Eventos, Travel |

**Este es el corte que mejor paga.** Booking es *tiempo* (un recurso ocupado de 10:00 a 10:30);
Inventory es *cantidad* (quedan 3). Parecen lo mismo y no lo son: sus almacenes, sus índices y
sus condiciones de carrera no se parecen en nada. Juntarlas produce un modelo que sirve mal a las
dos.

### Dinero

| Capacidad | Posee | Rechaza sola | Consumidores |
|---|---|---|---|
| `Api.Orders` | pedido, líneas, totales, ciclo de vida | pedido ya cerrado, línea sin precio, transición inválida |  Shop, Gov, Academy, Eventos, Travel |
| `Api.Payments` | intención de cobro, captura, devolución, enrutamiento a proveedor | monto inválido, ya capturado, devolución fuera de plazo, reintento duplicado | los mismos |
| `Api.Pricing` | listas de precio, promociones, impuestos, moneda | SKU desconocido, promoción vencida, moneda no soportada | Shop, Academy, Travel, Eventos |
| `Api.Cart` | canasta efímera con TTL | canasta vencida, tope de cantidad | Shop, Travel |

`Orders` y `Payments` se separan porque **un pedido puede existir sin cobro**: un trámite
gubernamental se radica y puede pagarse después, o no pagarse nunca. Hoy están fundidas
(`StubApplicationService` lee `PersistedOrder`), y esa fusión es la que hace que Gov arrastre
semántica de tienda.

### Contenido y comunicación

| Capacidad | Posee | Rechaza sola | Consumidores |
|---|---|---|---|
| `Api.Catalog` | índice de lo publicable, búsqueda, búsquedas guardadas | consulta vacía, filtro desconocido, página fuera de rango | Shop, Realty, Eventos, Academy, Travel, Gov |
| `Api.Documents` | ficheros, versiones, URLs firmadas, retención | tipo no permitido, tamaño excedido, URL vencida | Salud, Gov, Academy, Shop |
| `Api.Messaging` | hilos, mensajes, adjuntos, acuses | hilo cerrado, participante ajeno, adjunto sobre el límite | Salud, Gov, Social |
| `Api.Notifications` | envíos salientes, plantillas, bitácora de entrega | plantilla inexistente, destinatario sin canal, tope de frecuencia | **todos** |

`Messaging` es humano↔humano y bidireccional; `Notifications` es sistema→humano y de una vía.
Comparten la palabra "mensaje" y nada más: distinto almacén, distinta retención, distinto modo de
fallo. Hoy **un solo `IMessagingService`** sirve la in-basket clínica, la correspondencia Gov y
los DMs sociales — tres regímenes regulatorios sobre un stub.

### Proceso y comportamiento

| Capacidad | Posee | Rechaza sola | Consumidores |
|---|---|---|---|
| `Api.Workflow` | máquina de estados genérica: estados, transiciones, guardas, historia | transición no permitida, guarda no satisfecha, instancia cerrada | Gov, Salud, Shop, Academy |
| `Api.Sessions` | señales de sesión y comportamiento | evento vacío, ventana invertida, límite sobre el tope | **ya existe** (ADR 0130) |

`Api.Workflow` es la más reutilizable de todas y la que hoy está copiada más veces: un trámite
que avanza por estados, una ruta clínica, el cumplimiento de un pedido y una matrícula son la
misma máquina con distintos rótulos. **Los rótulos los pone el orquestador**; las transiciones
válidas y la historia las guarda Workflow.

### Lo que se evaluó y NO quedó como capacidad

- **`Api.Policies`** (cancelación, reembolso, retención). Sin almacén: es función pura. Va a
  `Core` como tipo. Ver §1, tercer filtro.
- **`Api.Forms`**. Las formas editoriales son del CMS (ADR 0008). La *ingesta* que necesitan Gov y
  Salud es `Api.Documents` + `Api.Workflow`.
- **`Api.Availability`** separada de `Booking`. Leería el mismo almacén de ocupación: es la misma
  capacidad, no dos.
- **`Api.Refunds`** separada de `Payments`. No puede decidir nada sin el estado de la captura.

## 3. Los tipados — `Synergos.Core`

Aquí es donde el diseño atómico manda de verdad. Si cada capacidad inventa su propio dinero y su
propia forma de decir "no", la capa media deja de componer y pasa a **traducir** — y traducir a
mano entre dieciséis servicios es donde viven los errores que ningún test de una capacidad
encuentra.

### Los átomos

| Tipo | Regla que carga | Por qué es un tipo y no un `decimal`/`string` |
|---|---|---|
| `Money(Amount, Currency)` | no se suman monedas distintas; redondeo por unidad mínima | un `decimal` suelto permite sumar pesos con dólares y nadie se entera hasta el estado de cuenta |
| `TimeWindow(Start, End)` | `End > Start`; sabe solaparse y contener | dos `DateTimeOffset` sueltos se pasan al revés, y el error se ve como "no hay disponibilidad" |
| `Ref(Kind, Id)` | **opaca**: se guarda y se devuelve, nunca se interpreta | es el tipo que hace posible la agnosticidad — ver abajo |
| `IdempotencyKey(Value)` | no vacía, estable | sin tipo, el "reintentar es seguro" es una promesa en un comentario |
| `Actor(Principal, Roles)` | quién actúa, en un solo vocabulario | para que auditoría y autorización no discutan qué es un usuario |

### Las moléculas

| Tipo | Para qué |
|---|---|
| `Rejection(Kind, Code, Message)` | **la forma única de decir NO.** Dieciséis capacidades rechazando igual es lo que permite que un orquestador maneje fallos sin un `switch` por servicio. |
| `Result<T>` | *o el valor, o el rechazo*. Nunca las dos, nunca ninguna. |
| `Page<T>(Items, Total, Offset)` | paginar una vez y no dieciséis. |

`Rejection.Kind` es un enum corto y cerrado (`Invalid`, `NotFound`, `Conflict`, `Forbidden`,
`Expired`, `Unavailable`). Cerrado a propósito: es lo que permite que el borde HTTP mapee a un
código de estado sin que cada API decida el suyo, y que un orquestador sepa **si reintentar o
rendirse** sin leer el texto del mensaje.

### La regla del `Ref` — la que sostiene toda la agnosticidad

> Una capacidad **guarda y devuelve** un `Ref`. **Nunca ramifica** sobre `Ref.Kind`.

`Api.Booking` reserva `Ref("salud.profesional", "dr-123")` de 10:00 a 10:30. No sabe qué es un
profesional, no le importa, y **si algún día le importara, dejaría de servirle a Travel**. Ese
`if (kind == "salud.profesional")` es exactamente cómo muere una capacidad agnóstica, y por eso
tiene gate propio (§5).

El corolario práctico: **el dato específico del dominio no viaja a la capacidad.** La
especialidad del médico, la tarifa del hotel y la fila de la butaca viven en su orquestador. Lo
que viaja es el `Ref` y lo que la capacidad sí entiende: una ventana, un cupo, un monto.

### Por qué `Shared` sí puede referenciar `Core` (corrijo el doc 06)

En el doc 06 escribí que ninguno referencia al otro. Al bajar a los tipos apareció el costo real:
`Rejection` tiene que convertirse en una respuesta HTTP, y ese mapeo es fontanería de host que
usarían las dieciséis. Con la regla simétrica, o se copiaba dieciséis veces o se metía dominio en
`Shared`.

Queda **una flecha, no dos**: `Shared → Core`, nunca al revés. Core sigue sin saber qué es un
host —es lo que importaba— y el mapeo `Rejection → ProblemDetails` se escribe una vez. La
simetría era más bonita en el diagrama y peor en el código.

## 4. Cómo compone un orquestador — el ejemplo que hace falta ver

`Domain.Salud`, `POST /citas`. En **negrita** lo que es de Salud y de nadie más:

1. `Api.Identity` → ¿quién actúa? → `Actor`
2. `Api.Consent` → ¿consentimiento vigente para agendar? → `Result<Grant>`
3. **el profesional tiene la especialidad pedida** ← dato de Salud; ninguna capacidad lo sabe
4. `Api.Booking` → `hold` sobre `Ref("salud.profesional","dr-123")` en la ventana → puede decir
   NO por sobrecupo o ventana cerrada
5. si hay copago: `Api.Pricing` → monto; `Api.Payments` → intención + captura
6. `Api.Booking` → confirmar el hold → reserva
7. si (6) falla: **compensar** — devolver en `Payments`, liberar el hold
8. `Api.Notifications` → recordatorio · `Api.Audit` → quién agendó qué

Siete llamadas a capacidades, **una** regla de Salud, y **el orden es la regla**. Eso es un
orquestador: el orden y la compensación son su producto, no un detalle de implementación.

Y explica por qué `Api.Booking` tiene holds con TTL: el hold es el paso barato y reversible. Se
toma primero, se cobra después, y si algo falla se suelta — en vez de cobrar y descubrir que el
cupo se fue.

## 5. Los gates que faltan

Sobre lo que ya corre (`BackendSegregationTests`):

- **`Core` no referencia nada**; `Shared` solo puede referenciar `Core`.
- **Ninguna capacidad ramifica sobre `Ref.Kind`** — se busca `Kind ==`, `Kind is`, `switch` sobre
  `Kind`, `Kind.StartsWith` dentro de `Synergos.Api.*`. Tosco y suficiente: no atrapa a un
  adversario, atrapa el atajo de un martes.
- Ningún tipo público de `Api.*` nombra un negocio concreto *(ya corre)*.

## 6. Los datos

Dieciséis capacidades sobre un solo almacén compartido sería un monolito con más puertos. La
regla, desde el primer día:

> Cada capacidad es dueña de su almacén y **nadie más lo lee**. Ni un `JOIN`, ni un fichero
> compartido. Si un orquestador necesita cruzar datos de dos capacidades, los cruza **él**, con
> dos llamadas.

Para v1 eso no cuesta infraestructura: cada capacidad tiene su directorio de ficheros, como
`Api.Sessions` hoy. El día que una necesite una base de verdad, la cambia sin que nadie se
entere — el contrato es HTTP.

## 7. Lo que sigue sin resolver

- **Compensación.** §4 paso 7 dice "compensar" como si fuera gratis. No lo es: ¿quién reintenta si
  la devolución también falla? La respuesta probable es una bitácora de compensaciones pendientes
  en el orquestador, y hay que diseñarla — pero después de que `Api.Booking` y `Domain.Salud`
  existan, con un caso real en la mano en vez de en abstracto.
- **Angular directo o vía CMS.** Sigue abierta. Define si los orquestadores necesitan CORS,
  autenticación de visitante y borde público propio.
- **Dieciséis procesos.** Límite de proyecto ≠ límite de proceso (doc 06 §9). Cómo se despliegan
  es decisión de operación y no la fuerza el código.
