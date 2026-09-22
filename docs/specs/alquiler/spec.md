---
vertical: alquiler
sustantivo: Equipment
alias:       [Rental, Agreement]    # el eje 2 dice Rental y el eje 3 dice Agreement — ninguno se deduce del otro
epica: 139                          # NO tiene épica de dominio, y ése es el punto del #147
oraculo: no                         # se escribe ANTES del código, como el de Social
ejes:
  catalogo:    { doctype: equipmentpage, fuente: UmbracoEquipmentCatalogSource, interruptor: "Synergos:Catalog:Sources:Alquiler" }
  transaccion: { forma: Bff, interruptor: "Synergos:Alquiler:Mode", seam: IEquipmentRentalService, orquestador: Synergos.Bff.Alquiler }
  artefacto:   { que: "el contrato: qué se llevó, cuándo, con qué garantía retenida y cómo volvió", sustantivo: Agreement, sella: si, sello: IAgreementSigner }
preguntas:
  deshacer:      si          # ventana apartada + garantía autorizada → orquestador
  recurso_ajeno: si          # la disponibilidad la lleva Api.Booking
  quien_cobra:   Bff.Alquiler
reusa:
  capacidades: [Api.Booking, Api.Payments, Api.Signing]
  elementos:   [booking-wizard, calendar, lightbox-gallery, file-uploader]
crea:
  doctypes:    [equipmentpage, elementequipmentrate, elementequipmentspec]
  seams:       [IEquipmentCatalogProvider, IEquipmentRentalService, IAgreementSigner]
  capacidades: []
  artefacto:   [EquipmentAgreementIssuer, EquipmentAgreementLedger]
  composer:    SeamComposer.Alquiler
ui:
  app: booking-wizard
rechazos:                                 # leidos de StubEquipmentRentalService, no inventados
  - "alquiler.idempotency_key_required · reservar o cerrar sin llave · Invalid · NO transitorio"
  - "alquiler.equipment_required · no se dijo qué equipo · Invalid · NO transitorio"
  - "alquiler.equipment_not_found · el slug no existe · NotFound · NO transitorio"
  - "alquiler.bad_window · la devolución no es posterior al retiro · Invalid · NO transitorio"
  - "alquiler.window_too_long · el DESPLIEGUE no retiene una garantía tantos días · Invalid · NO transitorio"
  - "alquiler.window_out_of_bounds · fuera del mínimo o el máximo de ESE equipo · Invalid · NO transitorio"
  - "alquiler.bad_quantity · cantidad menor que 1, o más de las libres · Invalid · NO transitorio"
  - "alquiler.no_units · ninguna unidad libre en esa ventana · Conflict · NO transitorio"
  - "alquiler.not_returnable · devolver algo que ya se cerró · Conflict · NO transitorio"
  - "alquiler.not_cancellable · cancelar algo que ya salió: eso se devuelve · Conflict · NO transitorio"
  - "alquiler.bad_amount · monto negativo contra la garantía · Invalid · NO transitorio"
  - "alquiler.damage_exceeds_deposit · el daño supera la garantía retenida · Invalid · NO transitorio"
---

# Alquiler de equipos — el spec del PILOTO 2

> **Este spec se escribe antes del código y su criterio de salida es de TIEMPO** (#147, épica
> #139). Lo que mide no es si la fábrica produce lo correcto —eso lo contestaron los pilotos 0 y
> 1— sino si produce **más rápido que una persona leyendo ocho verticales**, que es la única razón
> por la que vale la pena tenerla.
>
> **El vertical lo eligió el arquitecto**, y que lo eligiera él es parte del piloto: uno elegido
> por la fábrica se habría elegido por lo bien que encaja, y entonces la medición no diría nada.
>
> **Reloj: `2026-09-22T15:13:34Z`** — la hora en que el vertical quedó nombrado. La marca de
> llegada va al final, cuando el PR esté verde. No lleva gate y hay razón escrita: de los tres
> specs que existirán, dos no tienen hora de salida, así que un gate nacería rojo sobre sus únicos
> sujetos — la condición del #134 al revés.

## Qué problema del negocio resuelve

Una empresa que alquila equipos —andamios, equipo audiovisual, bicicletas, maquinaria ligera—
publica su catálogo, recibe reservas por ventana de fechas, **retiene una garantía** y la libera
(o se cobra parte de ella) cuando el equipo vuelve.

Lo que hoy no se puede hacer sin un despliegue: **publicar un equipo**. Y lo que el repo no sabe
hacer en absoluto: **retener plata que no es un cobro**.

## Por qué este vertical y no otro — lo que mide

Los ocho verticales construidos cubren tres formas del eje 2: ninguna (Social), directa (Realty,
Gobierno) y orquestada (Tienda, Salud, Eventos, Viajes). Alquiler entra por la tercera, así que
**la forma no es nueva**. Lo nuevo son dos cosas que ningún vertical anterior tiene, y las dos se
pueden medir:

1. **Un movimiento de dinero que se AUTORIZA para no cobrarse.** La garantía se retiene y, si todo
   vuelve bien, **se anula** — nunca se captura. Los cuatro flujos existentes autorizan para
   capturar; éste autoriza para *no* capturar, y eso es un estado que la máquina de sagas no ha
   ejercido.
2. **Un desenlace que ocurre DÍAS después.** Entre que alguien se lleva el equipo y lo devuelve
   pasan días. Eso choca con una pieza construida, y el choque se predice antes de tocarla — ver
   «la saga se cierra al confirmar», abajo.

## Los tres ejes

### Eje 1 · el catálogo — forma A

`equipmentPage` con su ficha, sus fotos, su tarifa por día y su garantía. Lo lee
`UmbracoEquipmentCatalogSource`; el interruptor es `Synergos:Catalog:Sources:Alquiler` y el
default es el seed de demo, como en los ocho anteriores.

**Forma A y no B** (doc 13 §5.bis): la colección es **propia** —nadie más lee equipos— y de sólo
lectura —el producto no publica equipos desde la app—. La pregunta que lo decide es *¿el almacén
del que lee este vertical es suyo?*, y aquí sí.

### Eje 2 · la transacción — `Api.Booking` SOLO, y `Api.Payments`

Las tres preguntas, por separado:

- **¿Hay algo que deshacer?** Sí. Apartar la ventana y autorizar el cobro son dos pasos, y **la
  autorización no vence sola** —el apartado de `Api.Booking` sí: `DefaultHoldTtl` son 10 minutos,
  medido—. Es exactamente lo que la HU #29 dejó escrito: *«lo que NO rescata es el stock […] sino
  la autorización del cobro, que no vence sola»*. → **orquestador**.
- **¿El recurso lo lleva alguien más?** Sí, `Api.Booking`. → hay que cablearlo.
- **¿Quién tiene la plata?** `Bff.Alquiler`. → él ordena capturar, anular y devolver.

### Eje 3 · el artefacto — y la CUARTA pregunta contesta que SÍ

El contrato de alquiler: qué se llevó, cuándo, en qué estado, con qué garantía retenida, y cómo
volvió. *¿Alguien de FUERA tiene que poder comprobar esto sin creernos?* — **sí**, y es el caso
más limpio del repo después de la entrada: la disputa por una garantía es literalmente alguien
diciendo que devolvió el equipo completo. Un registro sin sello no vale nada ahí.

Se sella con `Api.Signing` por `/v1/seals` y no por `/v1/signatures`, por la misma razón que el
diploma (#45): un contrato **no vence**, se re-emite igual, y lleva al titular dentro de lo
sellado.

## Lo que este spec PREDICE, escrito antes del código

Ésta es la mitad que el piloto 2 mide. La tabla se llena ahora y **no se toca**: al cerrar se
escribe una columna «real» al lado.

| sub-spec | predicho | por qué |
|---|---|---|
| **S1** DocTypes | **3 nuevos** — `equipmentpage` + dos element types | no existe un solo DocType de alquiler; medido |
| **S2** fuente + reglas + interruptor | **2 ficheros**, forma A | es el noveno `Umbraco*Source`; los ocho anteriores son el molde |
| **S3** seams | **2 × 2 ficheros** (catálogo y alquiler) + el del sello en S8 | uno por operación, no por sustantivo (#140) |
| **S4** `AlquilerSettings` | **1** | `Mode`/`BaseUrl`/`ApiKey`/`TimeoutSeconds`, más `MaxRentalDays` |
| **S5** composer | **1 nuevo** — `SeamComposer.Alquiler` | un vertical nuevo estrena el suyo (#155) |
| **S6** cliente | **1** — `HttpEquipmentRentalService` | se nombra por el seam, no por el sustantivo |
| **S7** artefacto | **2** + su gate de emisión | emisor y registro, FUERA del seam |
| **S8** sello | **4** | seam, `Hmac*`, custodia de llave y POCO propio anidado (#154) |
| **S9** controller | **1** + sus DTOs | `AlquilerController` |
| **S10** gate | **1** — `AlquilerWiringTests` | |
| **S11/S12** UI | **0 elementos nuevos** | `booking-wizard`, `calendar`, `lightbox-gallery` y `file-uploader` publicados |
| **S13** capacidad | **0** | `Api.Booking` + `Api.Payments` + `Api.Signing` cubren todo |
| **S14** orquestador | **1** — `Synergos.Bff.Alquiler`, el quinto | |

**Y una predicción que no es de tamaño sino de resultado:** el ticket dice que si hace falta un
gate nuevo, *eso es el resultado*. Se predicen **dos**, los dos por el eje 2:

1. que la garantía se **anule** y no se capture cuando no hay daño;
2. que la saga **no siga viva** durante el alquiler.

## Cómo sabemos que quedó bien

- `Synergos:Catalog:Sources:Alquiler = cms` sirve los `equipmentPage` autorados; el default sirve
  el seed. El rollback es esa línea.
- `Synergos:Alquiler:Mode = Bff` alquila contra el orquestador; el default es el motor en proceso.
- `AlquilerWiringTests`, **mutado diente por diente**.
- Verificación con **procesos vivos** (§10.6): matar `Api.Payments` a mitad de la reserva tiene que
  devolver la ventana a `Api.Booking` sola, y no dejar contrato emitido.

## La lista de rechazos cambió al codificar, y eso es un dato

La cabecera se escribió con **ocho** rechazos derivados por analogía con Eventos, y de ésos
**dos no sobrevivieron al código**: `no_lines` —Eventos compra varias líneas y un alquiler es un
equipo— y `deposit_required`, que resultó ser una invariante interna del motor y no una regla que
alguien pueda violar desde fuera. Aparecieron **seis** que la analogía no daba, y las dos que
valen la pena nombrar son:

- **`window_too_long` contra `window_out_of_bounds`.** Los dos rechazan una ventana y **el remedio
  es distinto**: el primero dice que este despliegue no puede retener una garantía tanto tiempo, y
  el segundo que ese equipo no se alquila tan corto o tan largo. Fundirlos habría sido el defecto
  `a_rejection_named_after_a_field_expires_at_the_second_field` al revés — un código que tapa dos
  causas con remedios opuestos.
- **`not_cancellable` aparte de `not_returnable`.** Cancelar es antes de que el equipo salga;
  devolver es después. Un solo código habría dejado a quien llama sin saber cuál de las dos
  puertas usar.

**Que la analogía acierte el 75 % y falle en el 25 % es exactamente lo que el piloto mide**: la
cabecera de un spec no puede derivar los rechazos, porque son el diseño y no la forma.

## Lo que este spec NO hace

- **No cobra recurrente ni suscripción.** Un alquiler es una ventana con principio y fin.
- **No mueve consumibles.** Combustible y discos de corte son stock que se consume, o sea
  `Api.Inventory`, y meterlos ahora sería una segunda línea de producto sin pedirlo nadie.
- **No decide la política de daño.** El monto del daño llega **ya calculado** por quien recibe el
  equipo, igual que la penalidad de cancelación de Viajes y la devolución parcial de Tienda.
