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

| sub-spec | predicho | real | por qué (predicción) |
|---|---|---|---|
| **S1** DocTypes | **3 nuevos** — `equipmentpage` + dos element types | **3** ✓ **+ 2 DataTypes** que no predijo | no existe un solo DocType de alquiler; medido |
| **S2** fuente + reglas + interruptor | **2 ficheros**, forma A | **2** ✓ | es el noveno `Umbraco*Source`; los ocho anteriores son el molde |
| **S3** seams | **2 × 2 ficheros** (catálogo y alquiler) + el del sello en S8 | **8** — 2 interfaces, **4** implementaciones y 2 `record` | uno por operación, no por sustantivo (#140) |
| **S4** `AlquilerSettings` | **1** | **2** — el sello se llevó el suyo | `Mode`/`BaseUrl`/`ApiKey`/`TimeoutSeconds`, más `MaxRentalDays` |
| **S5** composer | **1 nuevo** — `SeamComposer.Alquiler` | **1** ✓ | un vertical nuevo estrena el suyo (#155) |
| **S6** cliente | **1** — `HttpEquipmentRentalService` | **1** ✓ y con ese nombre | se nombra por el seam, no por el sustantivo |
| **S7** artefacto | **2** + su gate de emisión | **2** ✓ | emisor y registro, FUERA del seam |
| **S8** sello | **4** | **4** ✓ | seam, `Hmac*`, custodia de llave y POCO propio anidado (#154) |
| **S9** controller | **1** + sus DTOs | **1** ✓ | `AlquilerController` |
| **S10** gate | **1** — `AlquilerWiringTests` | **2** — y el segundo no es del vertical | |
| **S11/S12** UI | **0 elementos nuevos** | **1 elemento nuevo**, 9 ficheros | `booking-wizard`, `calendar`, `lightbox-gallery` y `file-uploader` publicados |
| **S13** capacidad | **0** | **0** ✓ — ni un endpoint nuevo | `Api.Booking` + `Api.Payments` + `Api.Signing` cubren todo |
| **S14** orquestador | **1** — `Synergos.Bff.Alquiler`, el quinto | **1** ✓, 8 ficheros | |

**Once de las trece aciertan y dos fallan. Las dos que fallan son el resultado del piloto**, y no
se parecen entre sí:

- **S11/S12 es una predicción que se hizo leyendo el registry y no el elemento.** `booking-wizard`
  existe, está publicado y **tiene forma de hotel**: noches, huéspedes, habitaciones. Un alquiler
  es unidades de un equipo por días con una garantía retenida, y eso no es el mismo asistente con
  otras etiquetas. La pregunta que habría acertado no es «¿hay un elemento que haga esto?» sino
  **«¿qué DATO pide este elemento, y es el mío?»** — y se contesta abriendo su `element-inputs`,
  que cuesta un minuto. El coste de creerle: la mitad del tiempo del piloto.
- **S4 falló por una regla que este mismo spec cita en S8.** El #154 dejó escrito que el sello
  lleva POCO propio y sección anidada; S4 predijo un POCO y S8 cuatro ficheros —uno de los cuales
  **es ese POCO**—, así que la tabla se contradecía consigo misma sin que nadie lo notara al
  escribirla. Una predicción por sub-spec no cruza con la de al lado, y nada la obliga.

**Y la predicción de RESULTADO —los dos gates del eje 2— acertó**: los dos están, mutados diente
por diente, dentro de `AlquilerWiringTests`. Lo que no predijo nadie es el **tercero**
(`AfirmacionDeOrquestadorTests`), que no salió del molde sino de levantar los procesos: ver abajo.

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

## El cierre del piloto 2 — las cuatro cifras, medidas

El ticket #147 puso el criterio de salida en el **tiempo** y pidió cuatro cifras. Van con cómo se
midió cada una, porque una cifra sin su método es una opinión con decimales.

### 1. Horas de pared, del spec al PR verde

**3,5 h**, del commit del spec (15:18) al último (18:51), sobre doce commits.

**Y la comparación que el ticket pedía —contra Social— NO se puede hacer desde el repo, así que
no se hace.** El piloto 1 entró en **un** commit, de modo que su rango medido es cero: el sello de
git mide cuándo se escribió el último byte, no cuánto se tardó. Fabricar el dato habría sido el
defecto que esta misma épica documenta (`a_fabrication_can_be_a_derivation`): saldría de datos
reales y mentiría igual. Si alguien quiere el número, la fuente es el historial de la sesión, que
no vive acá.

**Lo que el rango sí dice, y es lo útil:** las 3,5 h se repartieron aproximadamente en **1,3 h de
eje 1 + eje 2 + eje 3** (lo que el molde sí describe), **1,2 h de UI** (el elemento que el spec
predijo que no haría falta) y **1 h de los dos defectos que encontraron los procesos vivos**. O
sea que **el molde costó poco más de un tercio**, y los otros dos tercios los pagaron una
predicción equivocada y un paso de verificación que ningún test sustituye.

### 2. Cuántas veces el molde se quedó corto — y CUÁLES, que es lo que importa

**Cuatro**, y sólo dos merecen trabajo:

1. **El eje 1 no predice si un elemento publicado SIRVE.** El molde manda mirar el registry;
   mirar el registry dice que `booking-wizard` existe, no que pida los datos correctos. Es un
   sub-spec que falta (S11 sólo cuenta elementos, no los cruza contra el dato que el vertical
   necesita).
2. **Nada cruza una predicción con la de al lado.** S4 y S8 se contradecían en la misma tabla.
3. *(menor)* S3 cuenta «ficheros por seam» y no distingue interfaces de implementaciones, así que
   un seam con dos implementaciones —que es lo normal, stub + cableado— sale al doble.
4. *(menor)* S1 no cuenta los DataTypes que un element type nuevo arrastra si va en Block List.

### 3. Gates nuevos: **3**

`AlquilerWiringTests` (12) y `AfirmacionDeOrquestadorTests` (3). El ticket decía que un gate nuevo
*es* el resultado; los dos del eje 2 los predijo el spec, y **el tercero no salió del molde: salió
de levantar los procesos**, que es el dato más caro del piloto.

### 4. Hallazgos abiertos: **2 defectos cerrados acá + 4 anotados**

Los dos que se cerraron en esta HU porque el producto no funcionaba sin ellos están en
`CLAUDE.md` §5 con su regla. Los otros cuatro quedan anotados en el ticket.

### Y la conclusión que el piloto 2 deja escrita

**El molde llega hasta el borde y se detiene justo donde empieza lo que cuesta.** Los once
aciertos son todos de estructura —dónde va un fichero, cómo se llama, qué sección lee—; los dos
fallos y los dos defectos son todos de **contrato con algo que ya existe**: un elemento publicado
que no sirve, una capacidad que exige un campo que nadie manda, una proyección que no tiene caso
para un estado que sí ocurre. Generar la estructura es lo que el molde ya sabe hacer; **cruzarla
contra lo construido es lo que todavía hace una persona levantando los procesos.**
