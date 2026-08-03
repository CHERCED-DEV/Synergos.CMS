# Despiece de APIs — qué necesita cada dominio, y de qué

> **Estado:** **once de las veinte construidas** — `Sessions`, `Booking`, las cinco de nueve
> consumidores (`Identity`, `Audit`, `Notifications`, `Documents`, `Catalog`) y el bloque de
> comercio (`Pricing`, `Cart`, `Orders`, `Payments`). El molde de §4 está construido y gateado,
> y las once lo cumplen.
>
> Es el inventario que hay que acordar antes de escribir la
> primera API. Deriva de los dos filtros de atomicidad del
> [doc 07](07-diseno-atomico-capacidades.md) §1, pero **al revés**: en vez de proponer
> capacidades y buscarles consumidores, se recorre dominio por dominio preguntando qué necesita
> — y la lista sale de la intersección.
>
> **Los BFF no entran acá.** Se construyen después, cuando las APIs existan. Este documento no
> propone ni una regla de negocio de dominio: solo qué capacidad hace falta y para qué.

## 1. El despiece, dominio por dominio

Nueve dominios de negocio. Se omiten Admin, Forms, Flow y Search: son cara de operación del CMS
o quedaron absorbidos (Search → `Catalog`).

### Salud (EHR / PHI)

| API | Qué necesita Salud de ella |
|---|---|
| `Identity` | quién es el paciente y quién el profesional; roles clínicos |
| `Consent` | consentimiento informado vigente antes de agendar, compartir o tratar |
| `Audit` | **obligatorio**: quién vio qué historia y cuándo. Es requisito regulatorio, no higiene |
| `Booking` | citas sobre profesionales y consultorios; reprogramación; no-show |
| `Documents` | resultados, imágenes, adjuntos — con URL firmada y retención propia |
| `Messaging` | in-basket clínica paciente↔profesional |
| `Notifications` | recordatorio de cita, aviso de resultado disponible |
| `Workflow` | ruta clínica: derivación → cita → atención → seguimiento |
| `Orders` | cuenta de cobro de la atención |
| `Payments` | copago |
| `Pricing` | tarifa por servicio y por plan/asegurador |
| `Catalog` | directorio buscable de servicios y especialistas |
| `Signing` | receta y certificado médico firmados |

**13.** El dominio más pesado, y el que más exige de `Consent` y `Audit`.

### Gobierno / Trámites

| API | Qué necesita Gobierno de ella |
|---|---|
| `Workflow` | **el corazón**: estados del expediente, transiciones válidas, historia |
| `Identity` | ciudadano y funcionario |
| `Consent` | tratamiento de datos personales |
| `Audit` | trazabilidad del acto administrativo |
| `Documents` | anexos que radica el ciudadano; certificados que emite la entidad |
| `Signing` | firma del certificado emitido |
| `Notifications` | notificación del acto administrativo (con acuse) |
| `Messaging` | correspondencia con el ciudadano |
| `Orders` + `Payments` | tasas y estampillas. **Un trámite puede radicarse sin pagar** |
| `Pricing` | tarifa del trámite |
| `Catalog` | catálogo buscable de trámites |
| `Booking` | cita presencial en oficina |

**13.**

### Booking (reservas de servicios como negocio)

| API | Qué necesita Booking de ella |
|---|---|
| `Booking` | el núcleo: recurso, disponibilidad, hold, confirmación, cancelación |
| `Catalog` | qué se puede reservar, buscable |
| `Identity` | quién reserva, quién presta |
| `Pricing` | tarifa por franja, temporada, duración |
| `Orders` + `Payments` | cobro y devolución |
| `Notifications` | confirmación y recordatorio |
| `Messaging` | quien reserva ↔ quien presta |
| `Documents` | comprobante |
| `Engagement` | reseña después del servicio |
| `Audit` | quién canceló qué |
| `Geo` | buscar por cercanía |

**12.**

### Viajes

| API | Qué necesita Viajes de ella |
|---|---|
| todo lo de Booking | reserva de alojamiento = mismo núcleo |
| `Inventory` | asientos de vuelo y cupos de tour: es **cantidad**, no tiempo |
| `Cart` | vuelo + hotel + auto en una sola compra |
| `Geo` | destino, cercanía, rutas |
| `Signing` | tiquete y voucher verificables |
| `Documents` | itinerario |
| `Fulfillment` | entrega del voucher, cambios y reprogramaciones |

**15.** Es Booking más comercio multi-ítem.

### Eventos

| API | Qué necesita Eventos de ella |
|---|---|
| `Inventory` | aforo por zona **y mapa de asientos** — cupo con coordenadas, no franja horaria |
| `Booking` | sesiones y funciones con horario |
| `Catalog` | cartelera buscable |
| `Pricing` | tarifas por zona, preventa, promoción |
| `Cart` + `Orders` + `Payments` | compra de varias entradas |
| `Signing` | entrada con código verificable en puerta |
| `Documents` | entrada descargable |
| `Notifications` | recordatorio, cambio de función |
| `Identity`, `Audit` | |
| `Engagement` | valoración post-evento |

**14.**

### Realty / Inmobiliaria

| API | Qué necesita Realty de ella |
|---|---|
| `Catalog` | **el núcleo**: listados con filtros y facetas |
| `Geo` | búsqueda por zona, mapa, radio |
| `Documents` | fotos, planos, documentos del inmueble |
| `Booking` | visitas al inmueble |
| `Messaging` | interesado ↔ agente |
| `Workflow` | el interesado avanza por estados hasta cerrar |
| `Notifications` | avisos de nuevos inmuebles que encajan |
| `Identity`, `Audit` | |
| `Engagement` | favoritos y valoraciones |
| `Pricing` | no el avalúo (eso es del dominio), sí la publicación del precio y su moneda |

**11.**

### Shop / Tienda

| API | Qué necesita Shop de ella |
|---|---|
| `Catalog` | productos, filtros, facetas, búsqueda |
| `Inventory` | existencias y apartados |
| `Cart` | canasta con TTL |
| `Orders` | pedido, líneas, ciclo de vida |
| `Payments` | cobro, captura, devolución |
| `Pricing` | listas, promociones, impuestos |
| `Fulfillment` | envío, guía, seguimiento, direcciones |
| `Engagement` | reseñas y valoraciones |
| `Moderation` | cola de moderación de reseñas |
| `Documents` | factura |
| `Notifications` | confirmación, despacho, entrega |
| `Identity`, `Audit` | |

**13.**

### Academy

| API | Qué necesita Academy de ella |
|---|---|
| `Catalog` | cursos buscables |
| `Orders` + `Payments` + `Pricing` | matrícula |
| `Workflow` | progreso: inscrito → cursando → aprobado |
| `Documents` | material del curso |
| `Signing` | certificado verificable |
| `Booking` | clases en vivo con cupo y horario |
| `Notifications` | recordatorio de clase, aviso de nota |
| `Engagement` | valoración del curso |
| `Identity`, `Audit` | |

**11.**

### Social / Blogs

| API | Qué necesita Social de ella |
|---|---|
| `Engagement` | comentarios, reacciones, favoritos |
| `Moderation` | cola de aprobación, reportes, decisiones |
| `Messaging` | mensajes directos |
| `Identity` | autor, seguidor, bloqueado |
| `Notifications` | menciones, respuestas |
| `Documents` | adjuntos e imágenes de usuario |
| `Catalog` | búsqueda de publicaciones y etiquetas |
| `Sessions` | señales de comportamiento y tendencias |
| `Audit` | |
| `Consent` | perfil público y datos personales |

**10.**

## 2. La matriz — y de ahí sale el número

Filas: la API. Columnas: los nueve dominios. El conteo es lo que decide qué se construye primero.

| API | Salud | Gob | Booking | Viajes | Eventos | Realty | Shop | Academy | Social | **#** |
|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `Identity` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **9** |
| `Audit` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **9** |
| `Notifications` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **9** |
| `Documents` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **9** |
| `Catalog` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **9** |
| `Orders` | ✓ | ✓ | ✓ | ✓ | ✓ | · | ✓ | ✓ | · | **7** |
| `Payments` | ✓ | ✓ | ✓ | ✓ | ✓ | · | ✓ | ✓ | · | **7** |
| `Pricing` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | · | **8** |
| `Booking` | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | · | ✓ | · | **7** |
| `Workflow` | ✓ | ✓ | · | · | · | ✓ | ✓ | ✓ | · | **5** |
| `Messaging` | ✓ | ✓ | ✓ | ✓ | · | ✓ | · | · | ✓ | **6** |
| `Signing` | ✓ | ✓ | · | ✓ | ✓ | · | · | ✓ | · | **5** |
| `Consent` | ✓ | ✓ | · | · | · | · | · | · | ✓ | **3** |
| `Engagement` | · | · | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **7** |
| `Cart` | · | · | · | ✓ | ✓ | · | ✓ | · | · | **3** |
| `Inventory` (con asientos) | · | · | · | ✓ | ✓ | · | ✓ | · | · | **3** |
| `Geo` | · | · | ✓ | ✓ | · | ✓ | ✓ | · | · | **4** |
| `Fulfillment` | · | · | · | ✓ | · | · | ✓ | · | · | **2** |
| `Moderation` | · | · | · | · | · | · | ✓ | · | ✓ | **2** |
| `Sessions` | · | · | · | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | **6** ✅ existe |

**Veinte** (`Seating` ya fusionada en `Inventory` — §3). Los conteos por dominio: Salud 13 · Gobierno 13 · Shop 13 · Viajes 15 · Eventos 14
· Booking 12 · Realty 11 · Academy 11 · Social 10.

### Lo que la matriz destapa

- **Cinco APIs las usan los nueve.** `Identity`, `Audit`, `Notifications`, `Documents` y
  `Catalog` no son "de un dominio" en ningún sentido. Construir cualquier dominio antes que
  estas cinco es escribirlas mal cinco veces.
- **`Engagement` aparece en siete** y hoy no existe como concepto: los comentarios de Social, las
  reseñas de Shop, las valoraciones de Academy y los favoritos de Realty son **la misma
  capacidad** —un actor opina sobre un `Ref`— escrita cuatro veces con cuatro nombres.
- **`Booking` en siete de nueve**, incluido Gobierno. Confirma que separarla de Viajes era lo
  correcto y no una corazonada.
- **La cola larga es corta.** Solo cuatro APIs bajan de tres consumidores, y de esas, tres tienen
  argumento propio (abajo).

## 3. Las cuatro que yo cuestionaría — y qué haría

Es donde hace falta decidir entre los dos.

| API | # | El problema | Lo que recomiendo |
|---|:-:|---|---|
| ~~`Seating`~~ | 1 | Un solo consumidor. Una capacidad con un consumidor es una carpeta con red por delante. | **FUSIONADA en `Inventory`** ✅: un asiento es una unidad de aforo con coordenadas. La geometría del mapa es *dato*, no capacidad. Se separa el día que Realty pida planos interactivos o Viajes mapa de cabina. |
| `Fulfillment` | 2 | Suena a "lo que le falta a Orders". | **Mantener separada.** Su almacén (guías, direcciones, transportadoras) y su ritmo (eventos externos del transportador) no se parecen a los de un pedido, y fundirlas mete un integrador de terceros dentro del ciclo de vida del pedido. |
| `Moderation` | 2 | Dos consumidores, y ambos podrían vivir dentro de `Engagement`. | **Mantener separada.** Es la que más probable crece: cualquier contenido de usuario —mensajes, documentos, perfiles— acaba necesitando cola de revisión. Fundirla en `Engagement` la ata a "opiniones" justo cuando va a servir a todo lo demás. |
| `Consent` | 3 | Podría ser parte de `Identity`. | **Mantener separada.** Su régimen de retención es distinto y —lo decisivo— Gobierno y Salud necesitan consentimiento **sin** ser dueños de la identidad. Además el derecho al olvido tiene que poder borrar consentimientos sin tocar la identidad. |

Quedan **veinte**.

## 4. El molde — la parte que importa más que la lista

Esto es lo que pediste con más énfasis, y con razón: *"no puede ser una API diferente a la otra
en cuanto a construcción — en validaciones, en puntos de entrada, en convenciones, en nombrado de
clases"*.

Veinte APIs con veinte formas distintas es peor que un monolito: el monolito al menos es
consistente. Y esto no se sostiene con un documento — se sostiene con un gate, igual que el
resto.

### 4.1 Estructura de carpetas, idéntica en las veinte

```
Synergos.Api.<Nombre>/
├── Program.cs            arranque y NADA más: DI, middleware, Map*Endpoints()
├── Contracts/            lo que cruza el cable — *Request / *Response
├── Domain/               los tipos, las reglas puras (<Nombre>Rules) y el
│                         <Capacidad>Service que las compone con el almacén
├── Storage/              persistencia — I<Nombre>Store + su implementación
└── Endpoints/            <Nombre>Endpoints.cs — el ruteo, delgado
```

La separación `Contracts/` ↔ `Domain/` **no es ceremonia**: es lo que permite cambiar el modelo
interno sin romper a los clientes. Fusionarlas es cómodo el primer mes y carísimo el segundo,
porque cada renombre interno pasa a ser un cambio de contrato.

### 4.2 Nombrado — la regla, no la lista

| Elemento | Convención | Ejemplo |
|---|---|---|
| Proyecto | `Synergos.Api.<Capacidad>` — la capacidad, **nunca** un dominio ni una tecnología | `Synergos.Api.Booking` |
| Entrada al cable | `<Acción><Recurso>Request` | `CreateHoldRequest` |
| Salida | `<Recurso>Response` | `HoldResponse` |
| Tipo de dominio | el sustantivo, a secas | `Hold`, `Reservation` |
| Reglas puras | `<Recurso>Rules`, estático | `HoldRules` |
| Persistencia | `I<Recurso>Store` + `FileSystem<Recurso>Store`, con `Find` —no `Get`— para lo que puede no estar | `IHoldStore.Find(id)` |
| Composición | `<Capacidad>Service` | `BookingService` |
| Ruteo | `<Capacidad>Endpoints`, con `Map<Capacidad>Endpoints(this ...)` | `BookingEndpoints` |
| Códigos de rechazo | `<capacidad>.<causa>` en minúscula con guión bajo | `booking.slot_taken` |

### 4.3 Puntos de entrada — las mismas cinco formas, siempre

| Verbo y ruta | Devuelve | Nota |
|---|---|---|
| `GET /health` | 200 siempre que el proceso viva | **sin llave** — un chequeo con credenciales no sirve de chequeo |
| `POST /v1/<recursos>` | 201 + `Location`, o el rechazo | exige `Idempotency-Key` |
| `GET /v1/<recursos>/{id}` | 200 o 404 | |
| `GET /v1/<recursos>?offset=&limit=` | 200 con `Page<T>` | `limit` acotado por `QueryWindow` |
| `POST /v1/<recursos>/{id}/<acción>` | 200 o el rechazo | las transiciones son acciones, **no** `PUT` |
| `POST /v1/<señales>` *(ingesta)* | **202**, sin `Idempotency-Key` | ver abajo |

**La sexta forma es una forma, no una excepción.** La ingesta de telemetría —`Api.Sessions`—
no exige llave de idempotencia, y el criterio para aplicarla está escrito para que no se estire:
*el llamador no reintenta por diseño **y** el dato es agregado, no individual*. Un evento de
búsqueda duplicado corre un conteo en uno; un cobro duplicado cobra dos veces. Responde 202 y no
201 porque un dato malo no es error del llamador —ya sirvió su página— y un 4xx solo lograría que
reintentara algo que no debe reintentar.

`/v1/` desde el primer día. Una API sin versión en la ruta obliga a romper clientes o a
inventarse una versión el día que haya que cambiar algo — y ese día siempre llega.

Sin `PUT` ni `PATCH` sobre recursos con ciclo de vida: `POST /{id}/confirm` dice qué pasó;
`PATCH {estado:"confirmado"}` deja que el cliente invente transiciones que la capacidad tendría
que rechazar de a una.

### 4.4 Las siete reglas de construcción

1. **Toda mutación exige `Idempotency-Key`** (salvo la sexta forma). Cuando la API no contesta, el
   llamador **no sabe** si se ejecutó; sin llave, reintentar duplica y no reintentar pierde.
   Agregarla después obliga a auditar cada cliente.

   **Y la llave se consulta ANTES que cualquier regla que dependa del estado.** Al revés, un
   reintento se topa con el estado que él mismo creó —el cupo que ya tomó, el hold que ya
   confirmó— y sale rechazado por `Conflict`: exactamente lo que la llave existía para evitar.
   Booking lo tuvo mal y lo destapó un run con el proceso vivo, no un test — el test usaba un
   recurso con capacidad de sobra y pasaba por casualidad.
2. **Validar en el borde, devolviendo `Rejection.Invalid`.** Nunca lanzar por entrada mala: una
   excepción es un 500, y un 500 le dice al cliente "reintentá" cuando la respuesta correcta es
   "arreglá lo que mandaste".
3. **`Result<T>` hacia adentro, `RejectionResults.ToHttp()` en el borde.** Un único punto de
   desempaque por endpoint. Es lo que evita el `200` con cuerpo vacío cuando hubo rechazo.
4. **El reloj se inyecta.** Nada de `DateTimeOffset.UtcNow` dentro de una regla: la mitad de los
   errores de estas capacidades son de borde temporal, y sin reloj inyectable no se reproducen.
5. **Todo UTC, todo `DateTimeOffset`.** Una hora sin offset es una hora sin lugar.
6. **La API es dueña de su almacén y nadie más lo lee.** Ni un `JOIN`, ni un fichero compartido.
7. **`Ref` se guarda y se devuelve; nunca se ramifica sobre su `Kind`.** Ya tiene gate.

### 4.5 El gate del molde

`Synergos.CMS.Tests/Architecture/ApiMoldTests.cs`, **corriendo**. Recorre `Synergos.Api.*` y
exige, para cada una:

- que exista `Program.cs` y las cuatro carpetas;
- que llame a `UseSharedKeyAuth`;
- que exponga `GET /health`;
- que toda ruta mapeada empiece por `/v1/` o sea `/health`;
- que ningún `Map(Post|Put|Patch|Delete)` viva fuera de `Endpoints/`;
- que no haya `DateTime.Now` ni `DateTimeOffset.UtcNow` fuera de `Program.cs`;
- que no exista `MapPut` ni `MapPatch`;
- que referencie `Synergos.Core` y `Synergos.Shared`.

Tosco y suficiente — como los demás. No atrapa a un adversario: atrapa el atajo de un martes, que
es lo que de verdad erosiona veinte proyectos.

## 5. Qué hay que decidir entre los dos

1. ~~**¿Veinte, o se recorta?**~~ **Resuelto:** `Seating` fusionada en `Inventory`; las otras tres
   se mantienen con los argumentos de §3. Quedan veinte.
2. **¿Falta alguna?** Candidatas que consideré y descarté: `Api.Search` (es `Catalog`),
   `Api.Media` (es `Documents`), `Api.Reviews` (es `Engagement`), `Api.Leads` (es `Workflow` +
   `Engagement`), `Api.Policies` (no tiene almacén: es un tipo en `Core`).
3. ~~**¿El orden de construcción?**~~ **Hecho hasta las cinco de nueve consumidores.** Booking
   estrenó el molde y lo pagó —dos defectos salieron de construirla, no de discutirla—, y las
   cinco siguientes costaron bastante menos, que era la señal de que el molde no les quedaba mal.
   Lo que ganó `Shared` en el camino: `JsonCollectionStore`, `IIdempotencyLedger` y la lectura de
   la cabecera, promovidos cuando tuvieron seis consumidores y no antes.

   **Queda: `Inventory`, `Workflow`, `Messaging`, `Signing`, `Consent`, `Engagement`, `Geo`,
   `Fulfillment`, `Moderation`.**
4. ~~**¿`Api.Sessions` se adapta al molde?**~~ **Resuelto: se alinea.** Un molde que nace con una
   excepción es un molde que no existe.
