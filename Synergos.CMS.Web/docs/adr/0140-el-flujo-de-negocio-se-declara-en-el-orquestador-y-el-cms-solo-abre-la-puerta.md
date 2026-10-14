# ADR 0140 — El flujo de negocio se declara en el orquestador, el contrato se publica, y el CMS sólo abre la puerta

- **Estado:** Propuesto — se acepta o se descarta con el piloto en Eventos ([#201](../../../../../issues/201)), ver al final. **F1 y F2 hechas** (2026-10-07), la F2 en los dos repos y endurecida tras su verificación adversarial; **F3 hecha** (2026-10-08): la puerta genérica abre `eventos.compra`, sin el token de identidad (corrección de la decisión 4), verificada en vivo y endurecida (2026-10-09); **F4 hecha** (2026-10-09) en el código de los dos repos: la compra de Eventos va por la puerta con `<synergos-flujo>`, que el CMS coloca desde la vista del bloque (corrección de la decisión 6); ver «Avance del piloto». Falta la verificación de la F4 en el navegador sobre la CDN piloto (hecha el 2026-10-09, `_informes/78`, y otra vez con el cupón: `_informes/80` y `83`). **El cupón de Eventos volvió de verdad** (2026-10-13): el navegador manda el código, Pricing calcula el descuento y la puerta suma «cotizar», la vista previa sin reservar nada. **El retiro de la ruta vieja quedó DESCARTADO** por decisión del arquitecto (2026-10-13): se conserva como pieza detrás de su interruptor; ver «El cupón reconectado». **El cupón endurecido** (2026-10-13): fijado mientras su saga pueda cobrar, sin cambios con el pago en vuelo, con un tope de códigos que no sirven, con estilos AA en los siete temas y con «Tu orden» que explica el total; ver «El cupón endurecido». **Su verificación en vivo** (2026-10-13) dejó cinco defectos —el runtime republicado bajo la misma URL inmutable, el doble cobro eligiendo otra vez las entradas, el tope de códigos reintentado en el acto, el modo oscuro del sistema a 2,39:1 y la fila de cargos de un registro gratis—, arreglados con la fase D del doble cobro en el servidor y verificados en vivo el 2026-10-14 (12 de 12 escenarios, ningún defecto nuevo); el del runtime lo decide la ADR 0141 (propuesta); ver «La verificación en vivo del cupón endurecido y sus cinco defectos». **La ruta vieja, repuesta en el UI detrás del camino de compra que declara el CMS** (2026-10-14, regla del superset): el override de negocio gana; si no, la puerta cuando hay orquestador; si no, la ruta directa. Verificada en vivo; ver «La ruta vieja repuesta detrás del camino declarado»
- **Fecha:** 2026-10-07
- **Propone:** el análisis de estado del arte de 2026-10-05/07 (`_informes/70-estado-del-arte.md`:
  nueve dimensiones, cada una verificada por un segundo agente que intentó refutarla), a partir de
  una pregunta del arquitecto: «¿qué falta para que sea totalmente orquestable desde el back, desde
  el front y desde las APIs?». El arquitecto propuso el coordinador sin render del front y pidió que
  la capa media fuera .NET reutilizando lo existente; aprobó escribirla y arrancar el piloto el
  2026-10-07.
- **Parte de:** [#201](../../../../../issues/201) · épica [#139](../../../../../issues/139)
- **Extiende:** ADR 0138 (el coordinador de página tiene adónde enviar) · CLAUDE.md §0.B, principios 11-18 (los respeta todos)
- **Relacionada:** ADR 0135 (el patrón de contrato generado que se copia), ADR 0137 (config de negocio por despliegue), ADR 0116 (motor de pagos), ADR 0131 (el borde avisa de verdad), ADR 0002 (Application sin Umbraco)

## Contexto

> Marcas de certeza: ✔ comprobado en el disco al escribir esta ADR · ◐ medido por un agente del
> análisis y confirmado por su verificador con otro método · ○ cifra de un agente sin re-derivar.
> Código: CMS `b1c0d332`, UI `17874d1` (lego/integracion).

### Dónde estamos: las piezas son Lego, los flujos no

Del análisis, por dimensión: capacidades **3,3** · orquestación **2,6** · CMS como compositor **2,4** ·
flujos de punta a punta **2,4** · contratos **2,3** · fábrica **2,3** (escala 0-5: 2 = contratos
explícitos y cableado a mano; 3 = piezas con gates, reutilizables; 4 = composición por declaración).

Lo que ya es Lego, y esta ADR no toca:

- **Las 20 capacidades son hojas** ◐: ninguna referencia, nombra ni llama a otra; cada una con su
  almacén; 138 endpoints bajo `/v1`; vigiladas por `BackendSegregationTests`, `ApiMoldTests`,
  `ComposeStackTests`. Payments y Pricing sirven a los 4 orquestadores sin endpoints nuevos.
- **La maquinaria de sagas de `Bff.Core`** ✔ (`SagaEngine.cs`, `Saga.cs`, `CompensationSweeper.cs`,
  `SagaLease.cs`): compensación como dato persistido (principio 18), reintento con retroceso, ocho
  intentos, aviso único, barrido de abandono, arriendo contra la doble compensación, llaves
  deterministas `{sagaId}:{paso}`, `Abrir` para reintentar tras deshacer. La reutilizan los 4
  orquestadores sin tocarla.
- **El despliegue se deriva del disco** ◐ (`compose-gen.mjs`, `service-matrix.mjs`; CI construye y
  empuja la matriz de imágenes).

Lo que no lo es:

1. **El flujo es código, por vertical, y dos veces.** ◐ Cada orquestador tiene su `*Flow.cs` con el
   orden de los pasos y las compensaciones intercaladas a mano (1.648 líneas en los cuatro) y su
   `*CompensationExecutor` con un `switch`. Deshacer un pago se escribe cuatro veces. Además, el CMS
   mantiene un **motor en proceso** (`Stub*Service`) que repite cada flujo con compensación
   best-effort, **es el default incluso en `compose.prod.yml`** (los 15 interruptores en
   Stub/Local/Engine salvo SearchAnalytics) y lleva efectos que la rama del orquestador no tiene: el
   aviso al cliente, el seguimiento y el registro del checkout sólo están cableados en el `else` del
   composer (`SeamComposer.Shop.cs:120-143`).
2. **El CMS hace de BFF.** ◐ 38 controllers con 15.359 líneas en `Synergos.CMS.Web`; cada orquestador
   nuevo exige en el CMS un cliente `Http*`, un POCO de settings, un interruptor y un `*WiringTests`
   (el molde de 10 pasos, `docs/product/12-el-molde-de-un-vertical.md` §5). Una funcionalidad nueva
   toca 8-10 sitios del CMS; un vertical nuevo, ~29 ficheros de cableado.
3. **Los contratos HTTP se copian a mano en las tres costuras.** ◐ Sin OpenAPI en ninguna parte (0
   resultados de `AddOpenApi`/Swagger en `backend/`); `MoneyDto` declarado en 8 ficheros de BFF y 3
   del CMS; ~12.400 líneas de clientes escritos a mano en 10 módulos del UI. `System.Text.Json`
   descarta en silencio lo que no mapea, así que un cambio de un lado devuelve un default del otro.
   Los gates que lo vigilan son regex de nombres de clave (G-6, G-7), y G-7 llevaba semanas verde
   mirando 2 claves en 1 ruta ✔ (arreglado en `b1c0d332`: hoy 49 en 19, con piso y rojo si se ciega).
4. **Los pasos transversales se cortan según la rama.** ◐ El aviso al cliente vive en los `Stub*` del
   CMS (`ITransactionalNotifier`) y se pierde al cablear Tienda al orquestador; el paciente de Salud
   no recibe aviso en ningún modo. `Api.Payments` devuelve `ActionUrl` y el CMS lo mapea a
   `RedirectUrl`, pero ningún controller lo emite y ninguna estrategia del UI lo sigue: ningún flujo
   cobra con una pasarela real en ningún modo.
5. **No hay contrato de flujo en ningún lado.** ◐ `spec.md` declara la forma del vertical y sus
   rechazos, no sus pasos (sólo existe el de eventos). `flow.contract.ts` del UI es espejo de un
   `FlowOrchestrationController` que no existe. `Api.Workflow` es una máquina de estados declarativa
   pero **pasiva**: valida transiciones y guarda la historia, no ejecuta efectos, y ningún
   orquestador la usa.

### Premisas que se corrigieron al escribir esta ADR

- **«El mismo código de flujo puede correr dentro del proceso del CMS para un cliente pequeño.»**
  Falso, y fue dicho en la conversación que originó esta ADR. El principio 11 de §0.B —«todo el
  acople es HTTP; ninguna referencia de ensamblado cruza capas»— lo prohíbe, y con razón: el motor
  en proceso de hoy **es** el defecto 1. «Una sola máquina» significa los mismos contenedores en un
  host (lo que el compose ya hace), no el orquestador metido en el CMS.
- **«Para que front y back compartan tipos hace falta un BFF en TypeScript (NestJS).»** Falso: el
  contrato es el documento OpenAPI, que no depende del lenguaje de quien lo emite. Esta es la razón
  habitual para NestJS y se resuelve generando.
- **«No hay nada declarativo.»** Falso en lo absoluto ◐: `Api.Workflow` define estados como dato y
  `tools/provisionar.sh` reconcilia plantillas, recursos y definiciones. Pero ninguno de los dos
  **ejecuta pasos**: lo declarado es el estado, no el flujo.
- **«Los pasos de pago y de aforo se promueven a `Bff.Core`.»** No se puede ✔: el gate
  `Ningun_tipo_de_Bff_Core_menciona_un_sustantivo_del_dominio` prohíbe allí `Payment`, `Refund`,
  `Order`, `Booking`…, y `Un_orquestador_concreto_no_referencia_a_otro` prohíbe que un orquestador
  referencie cualquier `Synergos.Bff.*` que no sea `Bff.Core`. Los pasos comunes necesitan una capa
  propia con su regla de admisión (decisión 2).
- **«El CMS referencia el núcleo del backend, así que el backend no puede cambiar de .NET sin él.»**
  Falso ✔: `Synergos.CMS.Web` sólo referencia `Application` e `Interfaces`; el CMS no toca
  `Synergos.Core` ni `Synergos.Shared`. El backend puede moverse de versión solo.

## Decisión (propuesta)

### 1. El flujo de negocio es un DATO que interpreta el motor de sagas

Un orquestador **declara** sus flujos; `Bff.Core` los **ejecuta**. La definición es un documento
(JSON en el orquestador, `flujos/<dominio>.<flujo>.json`) con:

- **fases** — la que reserva (`abrir`: cotizar, apartar, autorizar) y la que cierra (`cerrar`:
  capturar, consumir). Un flujo puede tener más; el piloto necesita dos.
- **pasos**, cada uno con su **tipo** (`pricing.cotizar`, `inventory.apartar`, `payments.autorizar`,
  `eventos.revisar-lineas`…), qué lee y qué escribe en el contexto de la saga, y su llave de
  idempotencia derivada (`{sagaId}:{paso}[:{ítem}]`, la que ya usa `Saga.KeyFor`).
- **la reserva en dos tiempos** como forma de primera clase: un paso que reserva declara con qué
  paso se consuma y **cómo se deshace antes y después** del cierre — `autorizar`/`capturar` con
  `anular` antes y `devolver` después; `apartar`/`consumir` con `liberar` antes y `reponer` después.
  Es el «cambio de carácter» que hoy cada `*Flow.cs` reescribe a mano dentro de un `Select`.
- **repetición por ítem** (`para_cada: lineas`), porque apartar aforo es una línea a la vez y cada
  apartado se anota compensable en el instante en que existe.
- **pasos del dominio**: lo que de verdad es propio (validar las líneas de una compra de entradas,
  la comisión de servicio) sigue siendo C# del orquestador, registrado por nombre.

**El orquestador valida sus definiciones al arrancar** (como el validador de la ADR 0137): un tipo de
paso que no existe, una lectura sin escritura previa, una reserva sin consumación declarada → el
proceso **no arranca**. Un flujo mal escrito no llega a la primera compra.

**El mapeo de entradas es mínimo a propósito**: un paso lee por nombre lo que otro escribió en el
contexto. No hay lenguaje de expresiones. Criterio de reapertura: si portar dos flujos más exige
expresiones (condicionales, aritmética en la definición), la definición se está volviendo un lenguaje
de programación y hay que parar a decidir.

### 2. Los pasos sobre capacidades viven en una capa propia, con su regla de admisión

`Synergos.Bff.Pasos` (nombre del piloto): una librería que **sólo** los orquestadores referencian, que
referencia `Core`, `Shared` y `Bff.Core`, y que contiene los pasos tipados sobre cada capacidad (pago,
aforo/existencias, cotización, reserva en Booking) **y sus clientes**. Su regla, en los gates:

- **puede** nombrar el vocabulario propio de las capacidades (`Payment`, `Refund`, `Reservation`,
  `Order`: lo que en una capacidad agnóstica es legítimo, la lista `DomainNouns` menos la de negocio);
- **no puede** nombrar el negocio (`BusinessNouns`: paciente, trámite, curso, vuelo…) ni referenciar
  una capacidad o un orquestador concreto;
- `Un_orquestador_concreto_no_referencia_a_otro` la admite por nombre, junto a `Bff.Core`.

Cumple el principio 17: anular y devolver un pago está hoy en 4 de 4 orquestadores. Los
`*CompensationExecutor` dejan de repetir `VoidPayment`/`RefundPayment`: deshacer lo sabe el paso.

### 3. El contrato HTTP se publica, se genera y se vigila — el patrón de la ADR 0135, en HTTP

- **Cada capacidad y cada orquestador emiten su `openapi.json`** desde el código (records y Minimal
  APIs que ya existen), comiteado en `docs/contracts/openapi/<pieza>.json`.
- **Un gate de deriva** lo regenera y compara: un record cambiado sin regenerar es rojo, como
  `ContratoSynHostTests`.
- **Quien consume, genera**: el UI genera tipos TypeScript y un cliente mínimo (`--check`, como
  `contrato-synhost.mjs`); los orquestadores dejan de transcribir `*Capabilities.cs`/`*Dtos.cs` de las
  capacidades.
- **Backend a .NET 10 LTS.** ASP.NET Core genera el documento OpenAPI sin paquetes de terceros desde
  .NET 9, y .NET 8 sale de soporte en noviembre de 2026. Afecta sólo a capacidades, orquestadores y
  núcleo: el CMS se queda en .NET 8 por el pin de Umbraco 13 (ADR 0001) y no referencia el núcleo.
  Versiones exactas verificadas en api.nuget.org al fijarlas, no de memoria.

El front conoce **el contrato del flujo**, no el de las 20 capacidades.

### 4. El CMS deja de ser BFF: sólo abre la puerta

El navegador no puede hablar con un orquestador directamente: los orquestadores se protegen con la
llave compartida y el miembro se autentica con la cookie de Umbraco. El CMS queda como **la puerta**,
una sola y genérica:

`POST|GET /api/flujos/{flujo}/{operación}` → autentica la sesión del miembro → emite el token de
identidad (`IIdentityTokenIssuer`, que ya existe sobre `Api.Identity`) → reenvía al orquestador dueño
del flujo con la llave → devuelve **su** respuesta sin reinterpretarla.

> **Corregido en la F3 (2026-10-08, decidido por el arquitecto):** la puerta **no** emite ni reenvía
> el token. Declara el sujeto en `X-Synergos-Sujeto`, desde la sesión: ningún orquestador lee el
> token, y leerlo pondría la llave HMAC de `Api.Identity` en cada `Bff.*`. Se reabre cuando una
> capacidad necesite saber **cómo** se identificó la persona. Y la tabla no sale de la configuración:
> sale del contrato de cada orquestador (`x-synergos-flujo`). Ver «Avance del piloto — F3».

- **Cero código por vertical** en la puerta: la tabla `flujo → orquestador` sale de la configuración
  (derivable de los orquestadores que el compose ya descubre).
- **Lo que se queda en el CMS es lo del CMS**: contenido (el aforo de un evento es contenido, ADR
  0117), composición, diccionario, el puente con el UI, la sesión. Las lecturas de contenido siguen
  siendo del CMS; las transacciones pasan por la puerta.
- **El motor en proceso de cada flujo se retira cuando su flujo pasa por la puerta.** Un flujo existe
  una sola vez. Los efectos que hoy dependen de la rama (aviso, seguimiento, registro) pasan a ser
  pasos del flujo (decisión 5).

  > **Corregido el 2026-10-13 (decidido por el arquitecto):** no se retira. Synergos es un SUPERSET de
  > piezas para generar funcionalidades, y una pieza que hoy no se usa no tiene por qué borrarse
  > (CLAUDE.md §0.C.21): el motor en proceso y la ruta vieja de Eventos se conservan detrás de su
  > interruptor (`Synergos:Eventos:Mode`). Lo que sí vale es lo de arriba: el front no vuelve a la ruta
  > vieja, y un flujo nuevo se declara en el orquestador. Ver «El cupón reconectado».

  > **Precisado el 2026-10-14 (regla del superset):** «el front no vuelve a la ruta vieja» dejó de ser
  > cierto. El UI la llama cuando el CMS declara el camino «directa»: sin orquestador, o por el
  > override de negocio. Un flujo nuevo se sigue declarando en el orquestador. Ver «La ruta vieja
  > repuesta detrás del camino declarado».

### 5. Los pasos transversales son estándar, y cualquier flujo los declara

Lo que todo flujo de negocio necesita y hoy se corta según la rama:

| paso / middleware | qué hace | hoy |
|---|---|---|
| `avisar` | entrega por `Api.Notifications` con una plantilla por clave (versionadas, #179) | en los `Stub*` del CMS; se pierde con el orquestador |
| `redirigir-al-pago` | lleva la `ActionUrl` de la pasarela hasta la respuesta del flujo, y el front la sigue | se corta entre el CMS y el UI |
| idempotencia | llave derivada por paso e ítem; reintentar no duplica | ya en el motor; se conserva |
| correlación | el id viaja de la puerta a cada capacidad | ya en `CapabilityHttp`; obligatorio en la puerta |
| auditoría | rastro de cada paso por `Api.Audit` cuando el flujo lo declara | ningún orquestador audita |

`redirigir-al-pago` transporta lo que `Api.Payments` ya devuelve; **no** cambia cómo se cobra ni cómo
se reintenta, que es la decisión reservada del #183.

### 6. En el front, un coordinador sin render por región: `<synergos-flujo>`

Propuesto por el arquitecto: un elemento que vive en el DOM y no pinta nada (`display: contents`),
que hace de capa media entre los elementos y el flujo.

```html
<synergos-flujo flujo="eventos.compra">
  <synergos-eventos-entradas></synergos-eventos-entradas>
  <synergos-eventos-comprador></synergos-eventos-comprador>
  <synergos-boton-pagar></synergos-boton-pagar>
</synergos-flujo>
```

- Los elementos **no llaman a ninguna API**: emiten su intención con sus datos (los eventos de la ADR
  0138: `synergos:register`, `synergos:submit-request`), el coordinador la lleva a la puerta con el
  cliente generado y devuelve `synergos:submit-result` / `synergos:navigate`.
- Cada elemento encuentra a su coordinador por **ancestro en el DOM** (el protocolo de contexto de la
  comunidad de Web Components), así que caben dos flujos en una página.
- **El coordinador no decide nada de negocio.** Manda la clave del flujo y lo que el usuario escribió;
  qué capacidades se llaman, con qué forma y en qué orden lo decide el orquestador. Nada en el DOM
  —que cualquiera edita desde las herramientas del navegador— es frontera de confianza: ni una URL,
  ni un mapeo, ni un precio. Por eso el atributo es una **clave de flujo**, nunca una ruta.
- Lo coloca el CMS como colocable tipado (ADR 0135): el editor elige el flujo de una lista declarada.

  > **Corregido en la F4 (2026-10-09):** en el piloto lo coloca el CMS desde la VISTA del bloque de
  > Eventos, con la clave escrita por el servidor y un gate que la cruza con la tabla de la puerta. No
  > existe un colocable contenedor (0 de 52 records lo son; el emisor sólo saca hojas), y con un solo
  > participante no ejercitaría nada. El colocable tipado llega cuando una página componga dos o más
  > participantes o haya un segundo flujo. Ver «Avance del piloto — F4».

- Los middlewares del front —correlación, reintento sólo de lecturas, errores normalizados y
  traducidos (ADR 0136), telemetría— son una cadena de interceptores en `vitals/core`, sin framework.

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Un BFF nuevo en NestJS** | Tira la maquinaria de sagas (lo más difícil de escribir bien), el arnés que prueba los flujos contra las capacidades reales en proceso, `Synergos.Core`/`Shared` (llave, correlación, resiliencia), los gates de frontera y el despliegue derivado, que asumen .NET. Agrega un tercer runtime a un producto que se instala por cliente. Su argumento —tipos compartidos con el front— lo resuelve el OpenAPI generado. Se reabre si el SSR pasara a Node o el equipo fuera sólo TypeScript. |
| **Un proyecto .NET nuevo desde cero** | Los orquestadores ya existen en `backend/orquestadores` con su motor. Lo que falta es declarar el flujo y mudar lo que el CMS carga, no otra capa. |
| **Elementos de mapeo en el DOM que arman el payload de cada API** | El DOM no es frontera de confianza: el mapeo, el endpoint o el precio se reescriben desde las herramientas del navegador. Y la orquestación (cobrar, apartar, compensar, avisar) necesita idempotencia y sagas en el servidor. Se conserva la idea buena —una capa media sin render—, reducida a un coordinador que sólo conoce la clave del flujo. |
| **Que el navegador hable directo con el orquestador** (Caddy enruta, el orquestador valida al miembro) | El orquestador tendría que entender la cookie de Umbraco o el CMS emitir tokens al navegador; la llave compartida no puede salir al cliente. Se reabre con un gateway propio si la puerta del CMS mide latencia o carga que importe (p95 de la puerta > 50 ms sobre el orquestador). |
| **Usar `Api.Workflow` como motor de flujos** | Es una máquina de estados pasiva sobre HTTP: no ejecuta efectos, no compensa, no tiene arriendo ni barrido. Convertirla en motor la volvería el orquestador de todos, contra el principio 12 (la capacidad es dueña del cuándo, el orquestador del qué). Sigue siendo la capacidad para expedientes con estados declarados. |
| **Un motor de flujos de terceros** (Temporal, Elsa, MassTransit) | Infraestructura y un modelo de programación nuevos para resolver lo que el `SagaEngine` ya resuelve y está probado. Se reabre si hacen falta flujos de larga duración con esperas humanas de días o eventos asíncronos entre capacidades (hoy no hay outbox ni bus: brecha aparte). |
| **Generar los clientes C# de los orquestadores con NSwag/Kiota desde el día uno** | Se decide en el piloto (F2): primero el contrato publicado y su gate; el cliente generado del lado .NET puede ser un paso después si los tipos de `Bff.Pasos` alcanzan. |

## Consecuencias

**A favor**

- **Un flujo nuevo del caso común se declara**: un JSON con los pasos que ya existen, más los pasos
  propios del dominio. Hoy son ~29 ficheros por vertical y 8-10 sitios del CMS.
- **Un flujo existe una vez.** Se acaba la duplicación orquestador ↔ motor en proceso y con ella los
  efectos que dependen de la rama (aviso, seguimiento, redirección de pago).
- **Las tres costuras HTTP quedan con contrato generado y gate**, como ya lo está la del record al TS.
  Un renombre en una capacidad deja de ser un default silencioso en el consumidor.
- **La fábrica tiene qué generar**: un spec que declara pasos produce la definición del flujo; hoy un
  spec sólo valida.
- **El CMS adelgaza a lo suyo**: contenido y composición. Sus controllers de negocio salen al backend
  a medida que cada flujo pasa por la puerta.

**En contra**

- **Una abstracción más que mantener.** Un intérprete de flujos es código que hay que entender para
  depurar: un error en el paso 3 de un flujo declarado es más indirecto que en un `*Flow.cs`. La
  correlación pasa a ser obligatoria, no un extra.
- **La puerta es un salto más** entre el navegador y el orquestador.
- **Migración larga.** Cuatro orquestadores, los controllers de negocio del CMS y los clientes del UI
  se mudan flujo por flujo; durante la migración conviven los dos caminos.
- **Backend en .NET 10 y CMS en .NET 8**: dos versiones de runtime en el mismo producto hasta que se
  decida lo de Umbraco (ADR 0001).
- **El gate de segregación se abre una vez**, para admitir `Bff.Pasos`. Se abre con su regla escrita
  y sus mutantes, no con una excepción.

**Qué la vigilaría:** el validador de definiciones al arrancar; los gates de deriva del OpenAPI en
los dos repos; `BackendSegregationTests` con la regla de `Bff.Pasos`; y los tests de los flujos contra
las capacidades reales, que hoy son el oráculo.

## Qué hace falta para aceptarla (el piloto en Eventos, #201)

Eventos porque es el orquestador más simple: 3 capacidades y 318 líneas de flujo, con tests contra
hosts reales. Cada fase se comitea en verde y se empuja a la rama designada.

1. **F1 — el flujo como dato.** `eventos.compra` declarado e interpretado. Los tests actuales de
   compra (`TicketingCompensationTests`, `ReintentoTrasDeshacerTests`, `ComisionDeServicioTests`)
   pasan **sin tocarlos**. Una definición inválida impide arrancar. Mutantes: un cambio de carácter
   que no se declara, una compensación que no se anota al apartar.
2. **F2 — el contrato publicado.** Backend en .NET 10; `Bff.Eventos` y sus tres capacidades emiten su
   `openapi.json` con gate de deriva; el UI genera los tipos con `--check`.
3. **F3 — la puerta.** `/api/flujos/eventos.compra/...` reemplaza a checkout/confirm de
   `EventosController`; el motor en proceso de la compra de eventos se retira; el comprador recibe el
   aviso por un paso `avisar` (verificado con el `.eml`, no con logs).
4. **F4 — el front.** El cliente generado y `<synergos-flujo>` sustituyen al cliente escrito a mano en
   la compra; recorrido de compra en el navegador sobre la copia piloto, en los temas por-siteRoot.

**Se mide:** líneas escritas a mano retiradas por capa; cuántos ficheros toca declarar un flujo nuevo
(meta: la definición y los pasos propios); y si el mapeo de entradas aguantó sin expresiones.

**Se descarta si:** el intérprete necesita un lenguaje de expresiones para el flujo más simple, o el
flujo declarado no reproduce el comportamiento que los tests actuales fijan.

## Avance del piloto — F1 hecha (2026-10-07)

**El flujo de compra de Eventos es un dato** (`flujos/eventos.compra.json`) que interpreta
`Bff.Core/Flow`; `TicketingFlow` quedó de fachada (318 → 187 líneas) con su API de siempre.
Commits CMS `61ebbeeb` (F1) y el de endurecimiento que lo sigue.

**Cómo se verificó**
- El oráculo —`TicketingCompensationTests`, `ReintentoTrasDeshacerTests`, `ComisionDeServicioTests`,
  `EventosCapabilities`, `EventosCompensationExecutor`— no se tocó: 44/44.
- Un arnés temporal comparó el `TicketingFlow` imperativo de `cf544bd0` con la fachada en 22
  escenarios (feliz, rechazos en cada paso, captura y consumo que fallan a medias, compensaciones
  colgadas con barrido, caída tras capturar, reintento tras deshacer): **22/22 idénticos**, 238
  llamadas HTTP (método, ruta, query, llave, cuerpo) y 140 escrituras de saga. Con un motivo mutado
  en el JSON, 16/22 salen distintos: el arnés ve lo que tiene que ver. Se borró después.
- El JSON que guarda el almacén real se capturó ANTES del cambio y sigue idéntico byte a byte
  (`TicketingSagaPersistidaTests`): las sagas en vuelo se deshacen igual.
- Panel de diseño (3 diseños, 3 jueces, síntesis) y verificación adversarial: 22 mutantes fieles,
  todos los esperados en rojo; 25 hallazgos de revisión, 7 confirmados por un escéptico, más 3 que
  quedaron sin juzgar por límite de uso y resultaron reales. Todos arreglados, cada uno con su test
  y su mutante.

**Lo que la F1 decidió, y entra en la decisión**
1. **Sólo el camino hacia adelante es dato.** El deshacer (`SagaEngine.CompensateAsync` → el
   ejecutor del dominio) no se reescribió, y por eso el oráculo y lo persistido se respetan por
   construcción. Los `Kind` de compensación son datos de la definición (`antes`/`despues`).
2. **Reservas nombradas por el paso que reserva**, N únicas y N por ítem: caben Salud (agenda +
   cobro) y Tienda (pedido + cobro) sin tocar `Bff.Core`. El paso que cierra puede producir el
   objetivo de `despues` (Viajes: cancelar la reserva que devuelve confirmar).
3. **El código y la definición firman un contrato** (`ContratoDelFlujo`) que se cruza al arrancar:
   las fases que la fachada invoca (las mismas y en el mismo orden), lo que pone al abrir (toda la
   `entrada` del JSON tiene que estar), lo que reconstruye al continuar (cada fase arranca con eso
   y nada más), los campos de cada ítem y las reservas que la saga sabe guardar. Y el intérprete
   comprueba lo que el arranque no puede preguntar: que la fachada ponga lo que declara y que la saga
   sepa leer cada reserva declarada al nacer, antes de reservar nada.
4. **Cada tipo de paso declara qué llave necesita** (ninguna, fija o por ítem) y el validador lo
   cruza; un cierre es el `consumado_por` de su reserva y de ninguna otra; el lector rechaza claves
   repetidas en cualquier objeto.
5. **Una fase que continúa sólo corre sobre una saga en curso** (`flow.not_running`), además de las
   guardas con código de dominio de la fachada.
6. **`Bff.Pasos`** nace con los puertos de avance (cotizar, hallar/apartar/consumir,
   autorizar/capturar); su regla de admisión vive en `BackendSegregationTests` (17 → 19) y el barrido
   de identidad la incluye.

**Lo que queda, dicho**
- **Doble costura**: el avance va por los puertos de `Bff.Pasos`; el deshacer, por
  `EventosCapabilities` (lo exige el oráculo). Se cierra cuando los puertos de deshacer existan con
  un segundo consumidor.
- **Diferido**: la confirmación parcial de Viajes (que el cierre respete compensaciones que el
  dominio marque vivas) y `omitir_si_cero`/`al_fallar`. Se construyen al portar ese flujo.
- **Sin red ante excepciones**: una excepción de un paso después de sembrar sale como antes (la saga
  queda `Running` y la compensa el barrido de abandono). No se cambió para no mover la semántica
  ante una cancelación.
- **No se construyó la imagen** de `Bff.Eventos` en local: `Dockerfile.service` ya copia
  `Bff.Pasos`, y la prueba es el build de la matriz en CI.

## Avance del piloto — F2 hecha (2026-10-07)

**El contrato HTTP de `Bff.Eventos`, `Api.Pricing`, `Api.Inventory` y `Api.Payments` se publica,
sale del código y se vigila.** Cuatro documentos OpenAPI 3.1 en
`docs/contracts/openapi/<Ensamblado>.json`; las 23 operaciones con su 2xx y su esquema (201 donde
crean), `operationId`, `Idempotency-Key` donde se lee, 401 y `Rechazo`. Commits CMS `78d976c0`
(.NET 10), `1da318ac` (runtimes), `2a80940b` (Shared tipado), `6092ba7c` (generación y deriva),
`ade4debc` (el contrato dice la verdad), `acff3786` (peticiones honestas), `3cbc76f3`
(compatibilidad consumidor → capacidad) y los de esta sección. Del lado UI, `36fc58d` (la lógica
del generador de tipos, probada sin el hermano) y `ac932af` (el tipo de `Bff.Eventos`, su CLI con
`--check` dentro de `contracts:validate` y el paso G-14 de `design-gates-ui.yml`). El arnés
(`synergos-bff-author`) arranca la capacidad desde `net10.0`: Fabrica `a821892`, re-fijado en CMS
`7a846ce2` y en UI `24a05ae`. Plan y mediciones previas: `_informes/73-adr-0140-f2-plan.json`;
hallazgos de la verificación: `_informes/74-adr-0140-f2-hallazgos.json`.

**Cómo se verificó**
- Cada paso con build en 0 avisos (el primero también en Release) y las tres suites en verde. El
  oráculo de la F1 no se tocó: 44/44 en cada paso, y `TicketingSagaPersistidaTests` 2/2.
- **Los cuerpos HTTP no cambiaron**: una captura temporal de 33 respuestas reales de las cuatro
  piezas (400 y 404 `problem+json` con `code` y `transient`, 401 sin cuerpo, 201 con `Location`,
  200) dio el mismo sha256 antes del paso 3 y después de los pasos 3, 5 y 6. No se comitea.
- Las cuatro piezas, publicadas en Release para net10.0 y arrancadas en Production con su llave:
  `/health` 200 y `/v1` sin llave 401. No hay Docker local: la imagen `aspnet:10.0` la prueba
  `images.yml`.
- Determinismo: dos procesos comparan en verde en Windows. **Linux no está medido**: la primera
  corrida de `build-test.yml` es esa medición, y si difiere no se mergea.

**Lo que la F2 decidió, y entra en la decisión**
1. **Backend a .NET 10, y con él sus dos suites.** Los 28 csproj de `backend/`,
   `Synergos.Servicios.Tests` y `Synergos.Arquitectura.Tests` (que mira los dos árboles y va en el
   mayor de sus runtimes); el árbol del CMS se queda en net8.0. `Dockerfile.service` pasa a
   `aspnet:10.0`. Multi-target se midió y se descartó (el publish de la imagen da NETSDK1129).
   `RuntimesDeLosDosArbolesTests` cruza TFM e imágenes.
2. **El documento se genera en `Synergos.Servicios.Tests` y en ningún otro sitio**: el host real
   con `AddOpenApi` por `ConfigureTestServices` y el documento pedido a `IOpenApiDocumentProvider`,
   sin `MapOpenApi`. Producción no lleva el paquete ni la ruta (`ContratoHttpPublicadoTests`).
3. **La respuesta se declara con el TIPO DE RETORNO** (`TypedResults`), no con `.Produces<T>()`:
   medido, un `.Produces` que miente deja 4194 de 4195 tests en verde; con tipos, no compila.
   `Synergos.Shared` tipa `ToProblem`, `ToHttp`, `ToCreated` y `IdempotencyHeader.TryRead`. (El
   atributo `[ProducesResponseType]` se le escapaba al gate; lo cierra el endurecimiento.)
4. **Un esquema `Rechazo`** (ProblemDetails + `code` + `transient`, `title` como enum de
   `RejectionKind`) en las operaciones cuyo tipo de retorno incluye `ProblemHttpResult`; los seis
   estados de `StatusCodeFor`; el 401 sin cuerpo en todas. Los códigos no se enumeran.
5. **`Idempotency-Key` sale de un metadato** (`.ConLlaveDeIdempotencia(siempre)`), y una sonda
   contra el host comprueba que lo declarado es lo exigido. (Con el endurecimiento, el metadato
   lleva también el largo que el endpoint acepta, y la cabecera del token de identidad sale de su
   propio metadato.)
6. **En una petición, todo anulable lleva `= null`**: el esquema publica la forma del cable; lo
   que el negocio exige lo publican los rechazos con código.
7. **Tres capas de gate, porque la deriva sola no basta**: la deriva (`ContratoOpenApiTests`), el
   suelo que regenerar no arregla (`SueloDelContratoTests`) y las sondas contra el host
   (`SondasDelContratoTests`). El endurecimiento suma una cuarta en el host,
   `RespuestaPorElTipoDeRetornoTests`.
8. **Los orquestadores no generan clientes .NET en la F2**: un gate de compatibilidad
   consumidor → capacidad (`ContratoConsumidorEventosTests`, sobre `SubconjuntoOpenApi`) cruza lo
   que `EventosCapabilities` manda y lee con el JSON comiteado. Se reabre cuando los puertos de
   deshacer vivan en `Bff.Pasos` con un segundo consumidor.

**Premisas de esta ADR que la F2 corrigió**
- «Sin paquetes de terceros desde .NET 9»: sin terceros, sí; sin paquetes, no. Hace falta
  `Microsoft.AspNetCore.OpenApi` 10.0.12, que sólo existe para net10.0 y no viene en el framework.
- «Afecta sólo a capacidades, orquestadores y núcleo» / «el backend puede moverse de versión
  solo»: falso por la vía de los tests. `Arquitectura.Tests` referencia el backend (NU1201 en
  net8.0), así que también se movieron las dos suites, `Mvc.Testing` y la imagen.
- «Emiten su `openapi.json` desde lo que ya existe»: con lo que existía, las 23 operaciones salían
  «200 OK» sin esquema, sin 4xx, sin llave, con números que también eran cadenas y `required`
  falsos. Hubo que tipar respuestas, nombrar operaciones, declarar la llave y poner `= null`.
- «Cada pieza emite»: no en ejecución; lo genera la suite, desde el host real.
- «Los orquestadores dejan de transcribir»: en la F2 no. Siguen transcribiendo subconjuntos a
  propósito; lo que cambia es que la transcripción deja de ser silenciosa.
- **Medido al construirla, y contra el plan:** ASP.NET 10.0.12 **no** sufija dos tipos con el
  mismo nombre en un documento: el segundo apunta en silencio al esquema del primero. La
  generación lo convierte en un rojo (`CreateSchemaReferenceId`). Una capacidad devuelta a net8.0
  **no** compila (referencia el núcleo); lo que sólo ve el gate de runtimes es el núcleo sin
  referencias (`Synergos.Core` en net8.0 compila todo en verde) y la imagen. Y renombrar la ruta
  de un apartado lo ve también el oráculo de la F1 (16 rojos), no sólo el gate de compatibilidad.

**Mutantes** (cada uno aplicado, visto en el diff, rojo y restaurado; los marcados con † siguen
rojos después de regenerar el documento)
- Runtimes: `aspnet:8.0` en `Dockerfile.service`, `aspnet:10.0` en el del CMS, `CMS.Web` en
  net10.0, `Synergos.Core` en net8.0 (compila en verde: sólo lo ve este gate), descubrimiento
  vacío.
- Deriva y forma: un campo nuevo en `TicketPurchaseResponse` sin regenerar; un `openapi/` huérfano
  (con el `ContractsIndexTests` de antes, verde); un enlace a un fichero que no existe; el paquete
  en `Api.Pricing.csproj` o en `Directory.Build.props`; `.Produces<T>()` que miente (el único rojo
  de 4195); sin el `Target` del analizador (`T:Program` repetida).
- Suelo †: `(IResult)` en `GetPrice`; sin `WithName`; sin el transformer de números; un record de
  petición sin `= null`; dos `MoneyDto` en un documento.
- Sondas †: sin la llave declarada en `HoldStock`; declarada en `/void`, que no la lee (los dos con
  la llave requerida: la opcional no la cruzaba nadie hasta el endurecimiento); `code` renombrado en
  `ToProblem`; un 401 con cuerpo; `Rechazo` sin publicar en los GET.
- Compatibilidad: `forKind→forType` en la capacidad † y en el consumidor, `Available` renombrado
  en la capacidad † y en el consumidor, de int a string, la ruta `holds→hold`, `currency→moneda`,
  `CaptureAsync` sin llave, `amount` sin `currency`, un método sin recorrido. **El oráculo de la F1
  queda 44/44 en verde en ocho de los diez**: es el hueco que este gate cierra.

**El endurecimiento, tras la verificación adversarial** (dos revisores y un escéptico que intentó
refutar cada hallazgo: 9 confirmados y 10 descartados). Cada arreglo con su test y su mutante fiel
—aplicado por líneas, visto en el diff, rojo y restaurado—, el oráculo de la F1 sin tocar y en
verde (44/44, y `TicketingSagaPersistidaTests` 2/2), y las tres suites en verde tras cada uno
(4223 → 4246 tests):
- **El gate «ningún `.Produces`» no veía el atributo** (CMS `de0db480`). Pedía un punto delante:
  `[ProducesResponseType<PriceResponse>(200)]` sobre un `GetPrice` que devolvía `(IResult)` con otra
  forma dejaba el documento idéntico byte a byte y todo en verde. El de la fuente busca ahora el
  nombre, y `RespuestaPorElTipoDeRetornoTests` lo mira en el host: tipo de retorno concreto, ningún
  atributo de respuesta de MVC, y toda respuesta de los metadatos explicada por el tipo.
- **La llave publicada para `BuyTickets` era falsa** (CMS `cfd99ee9`). Se publicaba y se aceptaba
  hasta 128, pero la llave es el id de la saga y de ella cuelgan las de cada paso: con 91 o más,
  `{saga}|hold:{item}` no cabía y la compra moría en un 500. `LlaveDeSaga` (en `Bff.Core`) hace la
  cuenta con lo que la saga deriva —128 − `#100` − `|` − el paso más largo, `restock:` y un id de
  32— = 83; el metadato lleva el largo y `TryRead` lo lee de ahí, así que lo publicado y lo aceptado
  no pueden ser dos números. Los cuatro orquestadores abren su saga con `.ConLlaveDeSaga()`. Una
  sonda cruza `maxLength` contra el host, y `LlaveDeSagaTests` recorre la saga más larga posible
  con ids del largo de los de verdad: la llave más larga que sale mide 128. No se acortó la
  derivación con un hash: las llaves de paso ya viajaron a las capacidades en sagas vivas. El
  documento de `Bff.Eventos` cambia en una línea (`maxLength` 128 → 83); el tipo del UI no lleva el
  largo, y su `--check` sigue al día.
- **El gate de compatibilidad ignoraba `format`** (CMS `52b59bce`): una fecha que pasaba a texto
  libre sólo perdía su `date-time` y quedaba en verde. Lo que se lee pide su `format` y lo que se
  manda se lee con él.
- **La llave opcional no la cruzaba nadie** (CMS `a5cca79d`): con `{}` el ajuste de existencias
  contesta `adjust_required` antes de mirarla. Una tabla pequeña de la sonda da el cuerpo que la
  activa (`AdjustStock` → `{"delta":1}`), y quitar o inventar `.ConLlaveDeIdempotencia(siempre:
  false)` sale en rojo después de regenerar.
- **`AuthorizePayment` lee `X-Synergos-Identity` y el contrato lo callaba** (CMS `0143b467`): se
  publica como cabecera opcional desde `.ConTokenDeIdentidad()`, y una sonda la cruza (un token que
  no lo es contesta `identity.*`). Cambia sólo el documento de Payments, que el UI no lee. Lo de
  publicar `assertion` como enum **no** se hizo: sería falso (sin token sólo vale `CmsSession`, y
  `Enum.TryParse` admite la forma numérica).
- **«2 de las 22»** (CMS `5e76a5c9`): la matriz construye 24 imágenes desde antes de la F2. Se
  corrigió en todos los sitios que contaban imágenes, y un gate cuadra la cifra de la prosa contra
  la matriz.
- **El arnés arrancaba la capacidad desde `bin/Debug/net8.0`**: lo arregló el orquestador en Fabrica
  `a821892` (re-fijado arriba).
- **El índice y esta ADR daban por pendiente el lado UI**: esta sección y la fila del índice.

**Lo que se descartó, y por qué** (el detalle, en el informe 74)
- El gate consumidor → capacidad no ve un campo de negocio que se deja de mandar, y el CMS consume
  `Bff.Eventos` sin cruce con el contrato: heredado e idéntico antes de la F2; el esquema publica la
  forma del cable (decisión 6) y el consumidor CMS lo reemplaza la puerta (F3).
- El `Location` de los 201 (rutas sin GET, id sin escapar) y el 401 que `CapabilityHttp` trata como
  transitorio: heredados byte a byte (#136); la F2 no cambió cuerpos ni cabeceras.
- El mapa de operaciones del UI sin rutas, query ni código de éxito, y el tipo del navegador con
  lo que pone la puerta y las operaciones de operación: decisiones del plan, que deja a la F3 cómo
  viajan las entradas y qué se expone.
- G-14 compara contra la rama por defecto del CMS, y el CLI acepta argumentos mal escritos:
  heredados (G-12 y G-13 caen igual; el parseo es el del helper compartido desde #57).
- El enum anulable del generador: ASP.NET 10 no emite esas formas (medido).
- La deuda de la regla 24 «con fecha» sin fecha: la fecha es el hito F4 del piloto (#201).
- Y dos correcciones a hallazgos confirmados: la saga con la llave larga no quedaba para el barrido
  (la excepción saltaba antes de guardarla), y la `JsonException` que `CapabilityHttp` deja escapar
  en el camino 2xx es heredada (#136) y no se tocó.

**Lo que queda, dicho**
- **G-14 en CI, rojo hasta que la rama llegue a master**: el UI hace checkout de la rama por defecto
  del CMS, y ahí no existe `docs/contracts/openapi/`.
- **La deriva previa de `cms:sync:check` en el UI** (`elements-syn.contract.ts` contra el uSync del
  CMS) deja `contracts:validate` en rojo en su último paso. Es de antes de la F2 (medido sobre
  `17874d1`) y va en un ticket aparte.
- **La regla 24 del UI, abierta hasta la F4**: nadie importa todavía el tipo generado.
- **F3, la puerta**: decide las claves de operación; tiene que **separar** lo que pone la puerta
  (`buyerKind`, `buyerId`, `serviceFeePercent`) de lo que manda el navegador —el documento de
  `Bff.Eventos` los publica hoy como entrada— y no exponer `retry` ni `compensations`.
- **F4**: el cliente en `vitals/core` que importa el tipo generado, con el mutante de renombre
  que rompe `tsc`, y el typecheck de `vitals/core` en `npm test`.
- **Fuera del piloto**: las otras 20 capacidades y 3 orquestadores; el catálogo de códigos por
  pieza (cuando el UI los traduzca, ADR 0136); la obligatoriedad de negocio en el esquema; `status`
  como enum. Y del endurecimiento: la cabecera de identidad la leen también `Audit`, `Cart`,
  `Consent`, `Messaging` y `Workflow`, que se declara cuando publiquen contrato;
  `.ExcludeFromDescription()` no tiene gate (hoy sólo lo usa el webhook de Payments); el gate de
  compatibilidad no marca una cabecera que el consumidor manda y la operación no declara; y
  `CapabilityHttp` sigue dejando escapar una `JsonException` en el camino 2xx.
- **Riesgos abiertos**: un parche del runtime 10 en CI puede mover el documento sin tocar código (el
  rojo imprime la versión; se fija el parche si pasa); Linux sin medir; los gates de
  `Arquitectura.Tests` ejecutan código del CMS sobre el runtime 10 (al CMS en su runtime lo prueba
  `CMS.Tests`); los 400/415 del framework no traen `Rechazo`.

## Avance del piloto — F3 hecha (2026-10-08)

**La puerta existe, genérica, y abre `eventos.compra`**: `GET|POST /api/flujos/{flujo}/{operacion}`
arma su tabla de los contratos de los orquestadores que el CMS lleva incrustados, pone de su parte
quién pide (la sesión del miembro), el negocio del sitio y el contacto del aviso, y devuelve la
respuesta del orquestador sin reinterpretarla. El artefacto —asistentes, entradas, QR— se queda en el
CMS, en sus propias rutas. La ruta vieja (`/api/eventos/checkout|confirm`) sigue intacta: el retiro
es un paso de cierre que va DESPUÉS de la F4. Plan y mediciones previas:
`_informes/75-adr-0140-f3-plan.json`.

Commits CMS (rama `lego/integracion`): `33ba1c89` (los tres avisos de ImageSharp, uno por uno),
`9cb59324` (la orden de un invitado ya no se deriva de lo que compra), `b29e16ab` (`al_fallar: seguir` y
lo efímero por fase), `a2fa0322` (`omitir_si_cero`), `f9b0577a` (la carpeta de recogida: el aviso se
verifica abriendo el `.eml`), `22652510` (`notifications.avisar`), `89216df7` (la compra avisa al
comprador), `c7c99fc2` (lo que pone la puerta va en cabeceras, y la compra es de quien la abrió),
`f8f8fde4` (el precio con vigencia y tope), `3d3186fd` (`Bff.Eventos` publica la oferta), `471de27a`
(el CMS la publica), `cbb76993` (la puerta, sin flujos), `57256ba6` (el artefacto), `8d34283f` (se
abre `eventos.compra`) y el de esta sección. UI: `a8f072d` (el generador sabe de la puerta) y `8ce8633`
(el tipo con las cuatro operaciones, par del paso 14).

**Cómo se verificó**
- Cada paso con build en 0 avisos y las tres suites en verde (4404 tests en el árbol del CMS al
  cerrar; 843 en `tools` del UI). El oráculo de la F1 —`TicketingCompensationTests`,
  `ReintentoTrasDeshacerTests`, `ComisionDeServicioTests`— y `TicketingSagaPersistidaTests` no se
  tocaron: 46/46 en cada paso.
- La compra de verdad corre en `Servicios.Tests` contra el `Program` de `Bff.Eventos` y Pricing,
  Inventory, Payments y Notifications reales en el arnés: el cobro, la entrega, el `.eml`, el fichero
  de la saga y el log se miran donde quedan. La puerta y el artefacto, en `CMS.Tests` contra el
  contrato COMITEADO de `Bff.Eventos` —la tabla sale del mismo JSON—, sin referenciar el otro árbol.
- `humo-portada` (puerto 5210) en verde con la validación de la puerta al arrancar, antes y después
  de abrir el flujo.
- **Sin medir en vivo**: la verificación con procesos reales y el navegador (paso 16 del plan) la
  hace otro. Las cifras de esta sección son de las suites.

**Lo que la F3 decidió, y entra en la decisión**
1. **La tabla sale del contrato, no de la configuración.** El orquestador marca cada endpoint que
   expone con `.EnLaPuerta(flujo, operacion)`; el contrato lo publica como `x-synergos-flujo`; el CMS
   incrusta los `Synergos.Bff.*.json` y lo no marcado no existe (el mismo 404 que un flujo
   inventado). El destino va por convención (`Synergos.Bff.X` → `Synergos:X:BaseUrl`); en
   `Synergos:Puerta:Flujos:<clave>` sólo va lo de cada flujo (`Acceso`, `SujetoKind`, `Negocio`,
   `Aviso`), validado al arrancar contra la tabla y las secciones de negocio.
2. **Lo que pone la puerta viaja en cabeceras declaradas** (`x-synergos-puerta`):
   `X-Synergos-Sujeto` (el `Kind` del flujo y el `MemberKey`), `X-Synergos-Negocio` (sólo los campos
   declarados de la sección del sitio) y `X-Synergos-Contacto` (sólo donde la operación lo declara).
   El cuerpo del navegador pasa byte a byte, y del navegador no viaja ninguna otra cabecera. La
   `Idempotency-Key` se reemite atada al sujeto (`pta-` + SHA-256, 44 caracteres).
3. **La compra es de quien la abrió, y lo comprueba el orquestador** (`ISagaConDueno` en
   `Bff.Core`): una saga ajena es el mismo 404 que una que no existe, y abrir con la llave de otro
   abre otra.
4. **El calendario de venta y el tope por compra se mudan a la vigencia y el tope del precio**
   (`Api.Pricing`), y el orquestador publica la oferta —precio, ventana `[abre, min(cierra,
   empieza))`, impuesto 0, aforo declarado con ajustes relativos y llave— que el CMS le manda al
   publicar un `eventPage`, al crear un evento de organizador o a mano.
5. **El aviso es un paso** (`notifications.avisar`, `al_fallar: seguir`): un aviso que no sale no
   devuelve la plata; el contacto es una entrada efímera de `cerrar` y no se guarda en la saga.
6. **El artefacto se queda en el CMS y fuera de la puerta**: asistentes antes de cerrar y entradas
   al leer una compra `Completed` del miembro; sin asistentes, el comprador es portador de todas; lo
   confirmado no toca la red.
7. **Una sola plomería de pagos**: la puerta que abre un flujo hacia un orquestador con destino cuenta
   como «la plata la mueve `Api.Payments`»; con llaves de Wompi elegibles del lado del CMS, no arranca.

**Corrección de la decisión 4 (decidido por el arquitecto, 2026-10-08)**: en la F3 la puerta **no**
emite ni reenvía el token de identidad. Declara el sujeto en `X-Synergos-Sujeto`: ningún orquestador
lee el token, y leerlo pondría la llave HMAC de `Api.Identity` en cada `Bff.*`. **Se reabre** el día
que una capacidad detrás de un orquestador necesite saber **cómo** se identificó la persona y no sólo
quién es (CLAUDE.md §11). Eventos sigue en la lista de los que nombran al sujeto por la palabra de
quien llama, que ahora es el CMS con sesión.

**Premisas de esta ADR que la F3 corrigió**
- «Devuelve su respuesta sin reinterpretarla» junto con «se retira el motor» dejaba la compra cobrada
  y sin entradas: la respuesta del orquestador no trae entradas. El artefacto se quedó en el CMS.
- «Se retira el motor en proceso» en la F3: no aguanta sin la F4 (el cliente actual lee mal las
  respuestas del orquestador) ni sin la oferta publicada (nadie publicaba precio ni aforo). El retiro
  va después de la F4.
- «Emite el token y reenvía»: ver la corrección de arriba.
- «La tabla flujo → orquestador sale de la configuración»: sale del contrato y de la convención.
- «Cero código por vertical» chocaba con el calendario de venta, que sólo aplicaba el CMS: se mudó al
  precio, no a la puerta.
- «El aviso pasa a ser un paso» habría devuelto la plata: hacían falta `al_fallar: seguir` y lo
  efímero, que la F1 había diferido; y el aviso le llegaba al primer asistente, no al comprador.
- Lo gratis no pasaba por el flujo declarado (`payments.zero_amount`): `omitir_si_cero`.
- La correlación se partía en dos con un UUID con guiones: el CMS la limpia ahora con la regla de
  `Synergos.Shared` (`CorrelacionUnaSolaTests`).
- «El consumidor CMS de `Bff.Eventos` lo reemplaza la puerta»: no del todo; el artefacto y la ruta
  vieja leen una compra, y lo cruza `ConsumidorCmsDeBffEventosTests`.
- «Cada paso con las tres suites en verde» exigía antes resolver los tres avisos altos de
  `SixLabors.ImageSharp` 3.1.12 (la última 3.x; el parche es 4.1.2): se suprimen uno por uno, con su
  razón, y no `NoWarn NU1903`.

**Mutantes** (cada uno aplicado por líneas, visto en el diff, rojo y restaurado; el detalle, en el
mensaje de cada commit): `al_fallar` tratado como abortar; lo efímero guardado en la saga; la llave
del aviso con un `Guid`; enviar sin dirección; quitar `al_fallar` del paso (devuelve la plata);
`omitir_si_cero` ignorado; el contacto en un log; la ruta vieja mandando contacto; sin comprobar el
dueño; el sujeto opcional; el comprador otra vez del cuerpo; sin negocio, comisión 0; el diente 4 sin
mirar el sujeto de la puerta; la vigencia con el hasta incluido o ignorada y el tope por línea; la
ventana sin `min(cierra, empieza)`, impuesto 1900, declarar sin buscar, el ajuste por el aforo nuevo y
sin terminar el ajuste a medias; el publicador que lanza o publica al arrancar; la puerta que copia
las cabeceras del navegador, que nombra un flujo, con techo de 30 s, sin normalizar la correlación,
con el `.dockerignore` sin los contratos, el 401 crudo, las páginas de estado encendidas, sin mismo
origen o sin 415 (en la puerta y en el artefacto), la llave del navegador tal cual; emitir sin mirar
`Completed`, sin exigir dueño, sin asistentes ninguna entrada, reconciliar lo confirmado, leer
`holds`; marcar `RetryTicketPurchase`; la plomería sin la condición de la puerta. Todos rojos; los
que resultaron equivalentes en un test se anotaron y se reforzó el test.

**Lo que queda, dicho**
- **Paso 16, la verificación en vivo**: con los procesos reales, `Synergos:Eventos:BaseUrl`, la
  oferta republicada y el navegador del miembro (mismo origen real, `SameSite` de la cookie). La hace
  otro.
- **La F4, en el UI**: el cliente generado y `<synergos-flujo>` sobre abrir, asistentes, cerrar y
  entradas; pedir el login antes de pagar (la compra por la puerta es de miembros); traducir por
  `code` (ADR 0136); una página que sirva el enlace del aviso (`Aviso.Ruta`).
- **El retiro (paso 19)**, sólo después de la F4 en `lego/integracion` y la CDN republicada:
  checkout y confirm salen de `EventosController`, y se borran el motor en proceso de la compra y la
  mitad de compra de `HttpEventTicketingService`. Con el retiro, un clon limpio deja de vender
  entradas sin levantar servicios.

  > **Descartado el 2026-10-13 (decidido por el arquitecto):** no se borra nada de eso. Se conserva
  > como pieza detrás de su interruptor; ver «El cupón reconectado».
- **El token de identidad (paso 18)**: descartado en la F3; se reabre con el disparador de arriba.
- **El oráculo del molde (G-8) pierde dos ficheros**: el publicador de la oferta
  (`IEventOfferPublisher`, `HttpEventOfferPublisher`) no lo predice el doc 12. Es un hallazgo para el
  molde, y la línea base se movió con él (`tools/spec-valida.baseline.json`: 23 → 26 rutas, 92,3 %).
- **El issue público para evaluar `SixLabors.ImageSharp` 4 bajo Umbraco 13** quedó redactado y sin
  abrir: abrir algo público lo decide una persona.
- **Hasta el retiro conviven dos destinatarios del aviso**: la ruta vieja avisa desde el CMS y la
  puerta desde el orquestador; nunca para la misma compra.

## Endurecimiento de la F3 (2026-10-09)

**La verificación de la F3** (`_informes/76-adr-0140-f3-verificacion.json`) corrió 17 escenarios en
vivo —procesos reales en 5871-5875, el CMS sobre la copia piloto, el `.eml` abierto— con tres defectos
bajos, y dejó 21 hallazgos confirmados por un escéptico, varios del mismo defecto visto por dos
revisores. Se arreglaron todos, agrupados por defecto, un commit por arreglo, cada uno con su test y
sus mutantes (en el mensaje de cada commit). Commits CMS (`lego/integracion`): `37e83ca7` (una saga, un
turno), `f4aee49a` (una saga, una orden), `3d07321b` (la oferta es el estado entero del evento),
`4b7818dd` (la reconciliación con techo), `e796d6e1` (los dos gates de la F1), `dc02fd83` (segmentos de
punto en la puerta), `47044789` (sin caché, el nombre, la ruta vieja) y `218e9d38` (los huecos de test).
El UI no cambia: el contrato publicado sumó `RetireEventOffer`, que no lleva marca de la puerta, y el
`--check` de `contrato-http.mjs` sigue al día.

**Lo que se decidió, y entra en la decisión**
1. **Una saga, un turno** (`Bff.Core`, sin sustantivos de dominio). Cada fase del intérprete toma el
   MISMO arriendo con el que se deshace una saga (`ISagaLease`, #34) antes de leerla, y lo suelta al
   salir. Ninguna fase se intercala con otra fase de la misma saga ni con su compensación —la que pide
   quien compra, la del barrido, la de abandono—. Sin turno, «cerrar» escribía `Completed` encima de
   una compra que «cancelar» ya había devuelto: entradas emitibles sobre un cobro devuelto. Quien no
   consigue el turno recibe un transitorio sin tocar nada: `flow.busy` una fase (503,
   `transient:true`, como `compensation_in_flight` y `retry_in_flight`) y `compensation_in_flight`
   una compensación. Se eligió el rechazo inmediato y no la espera, para no colgar una petición del
   navegador detrás de otra; el doble clic o el reintento tras un 504 vuelven y encuentran la saga
   como la dejó la otra, y «cerrar» contesta idempotente si la otra la completó. Vale con una réplica
   y con varias, y vence solo, así que un proceso muerto a media fase no deja la saga trabada más que
   el arriendo (300 s por defecto, más que cualquier fase). La fase que falla deshace con el turno que
   ya tiene; la que abre no pisa una saga que otra llamada con la misma llave escribió; y completar
   relee el disco y no escribe `Completed` sobre una saga que ya no está `Running` (la red para un
   turno que venció).
2. **Una saga, una orden** (`EventTicketLedger.AnotarPendienteAsync`/`ConfirmarAsync`, bajo un
   cerrojo por saga; el almacén de órdenes es de una instancia). Una saga tiene UNA orden y emite sus
   entradas una vez, y la regla vive en el registro: la ruta vieja, el artefacto y la reconciliación
   pasan por él. La guarda de `9cb59324`, que sólo tenía la ruta vieja, se mudó allí.
3. **La oferta es el estado entero del evento.** Lo que una publicación no trae deja de venderse: la
   localidad que falta, con el precio vigente hasta ahora (cotizar la rechaza sin apartar nada); el
   pozo que falta, agotado a lo vendido y apartado. `POST /v1/ofertas/{eventId}/retirar` retira el
   evento entero, y el CMS lo llama al despublicar, mandar a la papelera o borrar un `eventPage` —o el
   nodo que lo contiene—, y cuando el catálogo deja de servirlo. El aforo nunca baja de lo
   comprometido: se recorta, se anota lo que de verdad quedó y se dice en el log. Cada localidad es
   independiente: una que falla no impide las demás, y republicar termina lo que faltó (no «todas o
   ninguna»: Pricing e Inventory no comparten transacción, y deshacer lo escrito sería otra escritura
   que también puede fallar).
4. **La reconciliación de «mis entradas» y de la consola corre en la ruta de lectura, y por eso tiene
   techo**: como mucho 10 órdenes, las más recientes, de a cuatro, y 3 s en total. Lo que no se
   completará queda `Discarded` y no se vuelve a mirar (deshecha, inexistente para ese comprador, o
   gemela de una ya confirmada); colgado o caído no descarta nada. «Mis entradas» reconcilia sólo las
   órdenes del miembro por su `MemberKey`, no por un correo que cualquiera escribe.
5. **La puerta rechaza un parámetro de ruta que es «.» o «..»** (`puerta.parametro_invalido`, también
   codificado) antes de armar la URL, y el reenvío se niega a ponerlo aunque se lo pasen.
6. **El QR no se guarda en caché** (`no-store` en las entradas, los asistentes, «mis entradas»,
   confirmar y transferir); **el nombre del miembro es el de su ficha y nunca su correo**
   (`DefaultMemberAccessGate`); y **la ruta vieja anónima no confirma, no entrega y no re-avisa una
   compra hecha por la puerta** (`PersistedEventOrder.ViaGate`). Con eso vale lo que decía la sección
   anterior: dos destinatarios del aviso, nunca para la misma compra.

**Cómo se verificó**
- Antes del primer arreglo, la base (`c61ebffe`) en verde: build en 0 avisos, 3051 / 863 / 497.
- Tras cada arreglo, build en 0 avisos y las tres suites completas en verde. Al cerrar: **3105 / 878 /
  501 = 4484**. El oráculo de la F1 (`TicketingCompensationTests`, `ReintentoTrasDeshacerTests`,
  `ComisionDeServicioTests`, `TicketingSagaPersistidaTests`) no se tocó y está en verde.
- La exclusión, contra el `Program` real de `Bff.Eventos` y las capacidades reales
  (`UnaSagaUnTurnoTests`): una compuerta detiene UNA llamada del orquestador a una capacidad y la
  segunda petición sale mientras la primera está parada ahí. Con el código de antes, tres casos
  terminan `Completed` con el cobro devuelto y el aforo repuesto, y uno `Compensating` con el cobro
  capturado; con el arreglo, siempre `Completed` con el cobro capturado y el aforo consumido, o
  `Compensated` sin nada cobrado.
- Cada mutante compilado y comprobado que entró antes de leer el resultado (dos «verdes» de una
  mutante que no compilaba se descartaron y se repitieron). Todos en rojo.
- **No se volvió a medir en vivo**: los escenarios de la verificación se reprodujeron en las suites con
  procesos reales del arnés.

**Lo que queda, dicho**
- **Cambiar el slug de un evento publicado** declara la oferta nueva y no retira la del slug viejo,
  que se sigue vendiendo por la puerta hasta que empiece. Hace falta comparar el slug publicado con el
  editado al publicar (el estado entre `Publishing` y `Published`); queda para cuando se toque el
  publicar del editor.
- **Las órdenes de la puerta anotadas antes de `47044789`** no llevan `ViaGate`: la ruta vieja todavía
  las entrega si alguien conoce su referencia interna (aleatoria, de 128 bits, que ninguna respuesta
  expone). Sólo existen en la copia piloto, y se van con el retiro.
- **El cerrojo del registro es de proceso**: correcto mientras el almacén de órdenes sea de una
  instancia, que es lo que dice `FileSystemJsonEntityStore`. Un CMS con réplicas necesitaría una
  creación atómica en el almacén.
- **El turno dura lo que el arriendo** (`CompensationLeaseSeconds`, piso de 30 s): una fase que tarde
  más pierde la exclusión en sus escrituras intermedias; completar relee y no pisa lo deshecho, que es
  la escritura que importa.
- **Un miembro sin nombre en su ficha** recibe «Hola :» en el aviso: el nombre va vacío antes que ser
  su correo.

## Avance del piloto — F4 hecha (2026-10-09)

**La compra de Eventos va por la puerta desde el front.** `<synergos-eventos>` ya no llama a
`/api/eventos/checkout|confirm`: abre y cierra la compra pidiéndosela al `<synergos-flujo>` que lo
envuelve, que la lleva a `/api/flujos/eventos.compra/{abrir,cerrar}` con el cliente generado; los
asistentes y las entradas van al artefacto del CMS. El CMS sigue sirviendo la ruta vieja para el bundle
publicado y las pestañas abiertas, hasta el retiro. Plan y mediciones previas:
`_informes/77-adr-0140-f4-plan.json`.

Commits UI (rama `lego/integracion`): `66e3a42` (el typecheck de `vitals/core` en `npm test`),
`be9abd8` (el generador emite la tabla de ejecución y deja de prometer un `title` falso), `64d4c45` (el
cliente de la puerta y `<synergos-flujo>` en `vitals/core`; la regla 24 se cierra), `2f415a6` (`confirm`
recibe el instrumento y el dominio escribe EL aviso de SH-3), `595b7ec` (el contrato con
`Events.Purchase`), `8def0f9` (la recarga no miente), `4199329` (fuera el cupón), `ab91644` (la sesión
antes de pagar), `1f92757` (la compra por la puerta) y `12d869b` (el enlace del aviso abre la compra).
CMS: `3c019930` (la sección `Events.Purchase`), `e7a2e001` (el CMS coloca el coordinador), `9c35b4c3`
(G-6 y G-7 antes que el UI), `8b05048d` (`Aviso.Ruta`) y el de esta sección.

**Cómo se verificó**
- Cada paso con su repo en verde: en el UI, `npm test` completo —6 tramos desde el primero, que sumó
  `test:tipos-vitals`— y, cuando el paso lo tocaba, `contratos:http:check`, `contratos:synhost:check`,
  `gate:rutas` con el CMS hermano y `contracts:validate` hasta su último tramo (`cms:sync:check`, que ya
  fallaba por deriva ajena); en el CMS, build en 0 avisos y las tres suites. Y en cada paso del UI, G-6
  y G-7 del CMS contra ese árbol. Al cerrar: UI **847 + 107 + 2.035 + 11**; CMS **3107 / 878 / 501 =
  4486**.
- G-6 y G-7 se movieron ANTES que el UI (paso 11) y se midieron contra los dos árboles: el de antes
  (sólo avisos: «+2 cruzando», «la cobertura subió») y el de después, en verde.
- **Rebasada** sobre `origin/claude/adoring-franklin-ustp2q` en los dos repos —el alquiler (#147) y
  G-7 acotada al método (#204)—; los hashes de arriba son los rebasados. Sobre el resultado: CMS build en
  0 avisos y **3170 / 897 / 520 = 4587**; UI `npm test` 6 de 6 (**847 + 107 + 2.061 + 11**) y
  `contracts:validate` entero, con `cms:sync:check` in sync; G-6 ✓ 537 claves en 9 verticales y G-7 ✓
  **55 claves en 21 rutas**, que es su piso: el del alquiler (60 en 23) menos las mismas 2 rutas y 5
  claves de checkout y confirm, sin ninguna ruta ilegible bajo la regla nueva. La línea base de G-6
  pierde además `detail`, que G-6 lee de los `*.model.ts` desde el #147 y era del cupón que se fue.
  `compilan-las-vistas`: las 403 compilan.
- `compilan-las-vistas` (401 vistas) y `humo-portada` (5210) con el envoltorio y con la ruta nueva del
  aviso; el bundle de eventos construido pesa **413.683 B** (71.119 gz) bajo su techo de 420 KB, y
  exportar el módulo de flujos desde `vitals/core` deja `sg-core.js`, `sg-shared.js` y el bundle de
  antes idénticos byte a byte: no hace falta `publish-runtime`.
- **Sin medir en vivo**: publicar en la CDN piloto y recorrer la compra en el navegador en los temas
  (paso 15 del plan) lo hace la verificación. Las cifras de esta sección son de las suites.

**Lo que la F4 decidió, y entra en la decisión**
1. **El CMS coloca `<synergos-flujo>` desde la vista del bloque** (`elementSynEventos.cshtml`, con
   `style="display:contents"` para no mover el layout antes de que el bundle defina la etiqueta), y
   `CoordinadorDelFlujoEnLasVistasTests` cruza cada `flujo="…"` con la tabla de la puerta y con
   `Synergos:Puerta:Flujos`. Es la premisa corregida de la decisión 6; su disparador, dos o más
   participantes en una página o un segundo flujo.
2. **El coordinador vive en `vitals/core`, sin framework, y lo define cada participante al cargar**
   (`definirCoordinador()`, idempotente): como bundle propio llegaba tarde y el pedido se perdía. Es la
   excepción escrita a la obligación 8 de la plataforma. Protocolo v1 con prefijo `synergos:`, en
   `dom-events.md` v2.
3. **El cliente mínimo lo emite el generador**: `OPERACIONES_DE_LA_PUERTA` (método, llave y consulta),
   cruzada con el mapa por `satisfies`, y el cliente genérico se tipa con las dos. Una tabla a mano era
   la segunda copia de la regla 23 y dejaba en verde una operación que gana un parámetro.
4. **Se decide por `code` y `transient`, nunca por `title` ni por el estado**: el `Rechazo` del fichero
   de la puerta ensancha `title` a `string` (la puerta pone la frase HTTP).
5. **SH-3 se conserva**: `pay` = abrir (llave = `sessionId`, la de la intención) → el total del servidor →
   asistentes; `confirm(session, instrument)` = cerrar → entradas. Si cerrar falla sin ser transitorio,
   la saga ya se deshizo y el pago de la sesión se marca `failed`: el siguiente clic reabre con la MISMA
   llave. El host viaja en el instrumento porque hay UNA estrategia por página.
6. **La sesión se pide antes de pagar** (con el CMS delante y sin miembro), y un 401 a mitad lleva al
   mismo panel: la compra por la puerta es de miembros, también lo gratis.
7. **Se traduce por `code`** con la sección `Events.Purchase` (20 claves, es-CO y en-US): UNA tabla
   literal en la funcionalidad, con la clase como respaldo; `vitals/core` sólo clasifica.
8. **El enlace del aviso va a `/eventos/?compra={id}`** y el elemento lo abre leyendo la consulta; un
   despliegue con dominios lo sobrescribe con `/?compra={id}`.
9. **El transporte**: correlación de 32 hex, reintento SÓLO de lecturas transitorias, telemetría como
   evento DOM opt-in, `redirect: 'manual'`, techo de 35 s, y no lanza: una negativa no es una caída.
10. **El UI nunca vuelve a la ruta vieja**, ni ante un 503 de la puerta: sin coordinador o sin puerta, la
    compra dice que no está disponible.

    > **Precisado el 2026-10-14 (regla del superset):** «nunca CAE» sigue en pie: ante un 503 de la
    > puerta el elemento no se pasa a la otra ruta, y una compra que ya salió se termina por el camino
    > por el que salió. «Nunca VUELVE» ya no: cuando el CMS declara el camino «directa» —sin
    > orquestador, o por el override de negocio—, el elemento compra por `/api/eventos/checkout|confirm`.
    > Ver «La ruta vieja repuesta detrás del camino declarado».

**Premisas que la F4 corrigió**
- «La F4 se hace sin tocar el CMS» (frontera del plan de la F3): falso en seis puntos —la sección de
  diccionario, G-6, G-7 (44/17 contra un piso de 49/19, medido; 55/21 contra 60/23 tras rebasar sobre el alquiler), `Aviso.Ruta`, el envoltorio y
  `dom-events.md`—.
- «Lo coloca el CMS como colocable tipado»: ver la decisión 1.
- «Los elementos no llaman a ninguna API»: las lecturas de contenido y el artefacto siguen siendo del
  elemento; por el coordinador pasan sólo las operaciones del flujo.
- «El coordinador devuelve `synergos:navigate`»: no en la F4; sin redirigir-al-pago (#183) nadie lo produce.
- La 0138 hace que el disparador pida y cada participante conteste; acá el participante pide y el
  coordinador contesta. Quedó escrito en las dos.
- «El carrito se pierde al volver del login»: falso, vive en la sesión. Lo que se perdía eran los
  asistentes, lo gratis/pagado y el `eventId`, que salían de la ficha.
- «Hay que inventar una llave por intención»: ya existía, `SessionData.sessionId`.
- «El paso de pago elige tarjeta o PSE»: era decorativo, el método no viajaba. Se quitó, con el cupón
  (que restaba en el navegador un descuento que nadie aplicaba) y los campos del comprador.
- «El registro gratis anónimo sigue funcionando»: no; `Acceso=Miembro` es por flujo.
- «Declarar claves de más no es un error» (paso 6): `gate:diccionario` rechaza una sección declarada que
  el elemento no pide, así que el paso 6 usó ya las dos claves del registro gratis.
- «Una clave sin es-CO pone rojo `usync-audit`»: no lo vigila nadie (medido; las 669 hojas tienen las
  dos culturas hoy).
- «El panel de sesión con `role=status`»: `gate:regiones-vivas` rechaza una región viva que nace dentro
  de un bloque condicional; lleva su encabezado, como el panel del organizador.

**Líneas escritas a mano retiradas en el UI**: del cliente de eventos salen 147 (`checkout`, `confirm`,
`normalizeCheckout`, `applyPromo` y su normalizador) y entran 56 (asistentes y entradas sobre el
transporte); del modelo, 56 (comprador, contrato del checkout y cupón); del componente, el cupón y el
método de pago. La tabla de la puerta y su tipo no se escriben: los genera el contrato.

> **Corregido el 2026-10-14 (regla del superset):** `checkout`, `confirm` y `normalizeCheckout`
> volvieron al cliente de eventos (UI `5d72686`), detrás del camino que declara el CMS. `applyPromo` no
> volvió, porque la ruta directa no recibe códigos. Ver «La ruta vieja repuesta detrás del camino
> declarado».

**Mutantes** (cada uno aplicado por líneas, visto en el diff, rojo y restaurado; el detalle, en el
mensaje de cada commit): `rootDir` de vuelta y un TS2322 inyectado; la tabla generada sin consulta y
`title` otra vez enum; la tabla y el mapa desacordados; los renombres de la regla 24 (`total`, `abrir`,
`eventos.compra`, la llave, la consulta de `cerrar`, `abrir` que gana un parámetro), con sus dos
controles verdes; reintentar POST, sin single-flight, la correlación dentro del reintento, sin
`stopPropagation`, `await body.cancel()`, el code leído de `title`, la clase por estado, `redirect:
'error'`, sin techo y techo de 30 s; el asistente sin el instrumento, sin `mensajeDeFallo` y con dos
alertas; la sección sin sus ficheros y un GUID repetido; el flujo mal escrito, sin envoltorio, cerrado en
`appsettings` y sin `display:contents`; la recarga con `isFreeEvent`, el `eventId` y los asistentes de
antes; el cupón de vuelta en `SIN_BORDE` y sin llamador; el panel sin `hasHost()`, sin
`isAuthenticated()` y sin precarga; la llave con `Date.now()`, sin marcar `failed`, la ruta vieja sin
coordinador, el motivo por estado, `confirm` sin el host y el componente que no mira el coordinador;
`Aviso.Ruta` sin barra (el CMS no arranca); el enlace leído del hash y su 401 como un error más. Todos
rojos. Los que salieron verdes o no compilaron se anotaron en su commit y se reescribieron.

**Lo que queda, dicho**
- **La verificación en el navegador** (paso 15): publicar `eventos` en la CDN piloto con `publish.mjs
  --cdn` (nunca `build-cdn`, nunca `C:\LOCAL_CDN`) y recorrer la compra pagada, la gratis, los rechazos
  provocados, el `.eml` y los temas, con la sesión inyectada como cookie. Hasta entonces no están medidos
  `SameSite` y `Sec-Fetch-Site` en un navegador real, `aria-busy` y `display:contents` sobre el custom
  element, ni los temas.

  > **Hecha el 2026-10-09** (`_informes/78-adr-0140-f4-verificacion.json`; ver «Endurecimiento de la
  > F4»), y otra vez con el cupón: `_informes/80-adr-0140-cupon-en-vivo.json` (2026-10-13) y
  > `_informes/83-adr-0140-defectos-en-vivo-r1.json` (2026-10-14).
- **El import de `Events.Purchase.*` a la base real** es del arquitecto (pregunta 1 del plan): sin él,
  los mensajes salen por su respaldo es-CO.
- **El merge a master del UI** despliega el CDN público con un eventos que sólo vende por la puerta
  (pregunta 2 del plan): es del arquitecto.

  > **Precisado el 2026-10-14:** hoy el merge a `master` del UI NO despliega el CDN público. Lo
  > construye Cloudflare Workers Builds, que sale rojo en cada push de `master`: 60 de las 60 cabezas
  > con su check, del 2026-09-17 al 2026-10-10, entre ellas los cinco últimos pushes que dispararon
  > `despliegue-cdn.yml` (2026-10-01 a 2026-10-10). Lo que sirve es un build del 2026-09-12.
  > `despliegue-cdn.yml` no tiene secrets y se salta todos sus pasos. Las dos cosas son del arquitecto;
  > ver la ADR 0141, «Producción».

  > **Precisado el 2026-10-14 (la ruta vieja repuesta):** el `eventos` de hoy no vende «sólo por la
  > puerta»: vende por el camino que declara el CMS, la puerta si el despliegue tiene orquestador y la
  > directa si no.
- **El retiro** (pasos 19-20 del plan de la F3), después de republicar las CDN que lea cada CMS. M1 tiene
  que buscar la FORMA de la llamada: el bundle nuevo todavía trae `` `${…}/checkout` ``, que es la ruta
  por hash del propio elemento, no la API.

  > **Descartado el 2026-10-13 (decidido por el arquitecto):** la ruta vieja se conserva detrás de su
  > interruptor; ver «El cupón reconectado».
- **El índice §11.2** (repo `synergos`) no se tocó desde esta tarea: el checkout principal lo usan otros.
- G-7 no ve el cuerpo de los asistentes (el artefacto deserializa a mano); un renombre de `attendees`
  falla en voz alta en ejecución (`eventos.asistentes_no_cuadran`) y lo cubre el spec de la compra.
- Ningún gate vigila que cada hoja del diccionario tenga es-CO y en-US.
- `/account/register` no acepta `returnUrl`; cambiar la selección después de abrir deja la saga anterior
  apartada hasta el barrido de abandono; y la `SessionStore` vence a los 30 min, así que un login más
  largo vuelve a un carrito vacío.

  > **Corregido el 2026-10-13 (D2 de la verificación en vivo del cupón endurecido):** después de
  > CERRAR no quedaba un apartado, quedaba un doble cobro. Y el vencimiento de 30 min de la
  > `SessionStore` olvidaba la compra pendiente antes que los 60 min del barrido de abandono. Hoy elegir
  > otra vez la resuelve primero por su id, y la sesión con una compra pendiente tiene su propio plazo
  > (24 h). Ver «La verificación en vivo del cupón endurecido y sus cinco defectos».

## Endurecimiento de la F4 (2026-10-09)

**La verificación de la F4** (`_informes/78-adr-0140-f4-verificacion.json`) recorrió la compra en vivo
—procesos reales en 5871-5875, el CMS sobre la copia piloto, `eventos` 0.1.12 en la CDN piloto,
Chromium— y dejó siete defectos, uno alto, más siete hallazgos de revisión confirmados por un escéptico.
Se arreglaron en orden de severidad, un commit por arreglo, cada uno con su test y su mutante fiel (en
el mensaje de cada commit). Commits UI (`lego/integracion`): `5b9d6c3` (la tinta del aviso de rechazo),
`db8c38d` (el QR y la butaca de Eventos), `a0ff38c` (`?compra=` se consume), `f5e8bb4` (los ids sin
`randomUUID`), `0bda010` (cerrar pregunta a la saga), `d4730e9` (el coordinador de cada participante),
`47f819a` (el protocolo en crudo), `4b9f104` (G-15), `e77ebd8` (el foco tras pagar), `c4ec404` (el
registro gratis) y `a5dee91` (el enlace sin sesión). CMS: `2d728491` (el parámetro del aviso),
`d6b0fc6b` y `c8708b39` (tres claves de `Events.Purchase`) y el de esta sección.

**Lo que se decidió, y entra en la decisión**
1. **Un lavado no es tinta, tampoco por un alias.** El aviso de rechazo del asistente —desde la decisión 5
   el ÚNICO mensaje de cada rechazo— pintaba `color: var(--shw-danger)` con `--shw-danger` = el
   `-surface` de peligro: 1,14 a 1,19:1 en los siete temas, una franja rojiza vacía. Los tres shells de
   la compra (asistente, carrito y acuse) usan el `-text` de su familia —en Chromium sobre el CSS del
   CMS: 5,47 a 10,94 el aviso, 4,81 el reloj urgente en `light`, 6,20 el tick— y `tinta-de-lavado` sigue
   los alias de la hoja, con un censo de 13 heredados que sólo baja.
2. **Una etiqueta cruda trae su bundle.** El QR de las entradas no se dibujaba: `eventos` pinta
   `<synergos-qr-code>` (y `syn-credential-wallet`, que importa) y sólo declaraba `countdown-clock`.
   Declara `qr-code` y `seat-map`, y el gate `etiquetas-crudas` cruza lo que cada elemento pinta —en sus
   plantillas y por las clases de librería que usa— con sus `dependencies`, en los dos sentidos, con un
   censo de 9 heredados de otros verticales que sólo baja.
3. **`?compra=` se usa una vez.** Se lee en `ngOnInit` y sale de la URL con `history.replaceState`; sólo
   el panel del enlace sin sesión lo devuelve al `returnUrl`. Antes, un F5 o la vuelta del login en medio
   de OTRA compra de esa pestaña decía «¡Compra confirmada!» de la vieja.
4. **Los ids del flujo salen de `crypto.getRandomValues`**: `randomUUID` es `[SecureContext]` y en
   http://synergos.local:5000 la compra lanzaba antes de salir a la red. El coordinador contesta
   `cliente.fallo_interno` también cuando el envío lanza: un pedido nunca se cuelga.
5. **Corrige la decisión 5 de la F4: «no transitorio» no es «se deshizo».** Ante un `cerrar` no
   transitorio, la estrategia pregunta a la saga (`consultar`) antes de decidir: `Compensated` marca el
   pago `failed` y el siguiente clic reabre con la misma llave; `Completed` sigue a las entradas; otro
   estado, o sin respuesta, no afirma nada y dice lo que quedó apartado. Un 502
   `puerta.respuesta_invalida` o un 2xx con el cuerpo cortado llegaban con la captura hecha y decían «no
   se te cobró nada» en bucle. Y `pay`, si `abrir` devuelve la saga `Completed`, no anota (sería 409 en
   cada clic): acepta con su referencia y `confirm` cierra, idempotente.
6. **La decisión 2 de la F4 tiene guardián**: el gate `coordinador-de-los-participantes` deriva de
   `vitals/core/src/flujos` la API con la que se habla al coordinador y exige que la entrada de cada
   elemento que la importa llame `definirCoordinador()` antes de registrarse. Borrar esa línea de
   `main.ts` dejaba todo en verde.
7. **El protocolo `synergos:` se fija en crudo** contra la tabla de `dom-events.md` v2, y la guarda
   `cliente.flujo_desconocido` se ejecuta con un pedido del mismo flujo que el atributo. Renombrar un
   evento o quitar la guarda dejaba 27/27 en verde.
8. **El parámetro del enlace del aviso se cruza (G-15)**: `AVISOS_DE_LOS_FLUJOS`
   (`vitals/core/src/flujos/avisos.ts`, de donde `eventos` lee `?compra=`) contra `Aviso:Ruta` del
   `appsettings.json` del CMS, en `contracts:validate` y en `design-gates-ui.yml`; del lado del CMS lo fija
   `CoordinadorDelFlujoEnLasVistasTests`. El validador de la puerta acepta cualquier ruta del sitio.
9. **Lo que se dice**: tras pagar, SH-11 (`enfocar`) lleva la vista y el foco a su encabezado; el registro
   gratis dice «Continuar al registro» y «Registro confirmado» y no avisa de un cobro; y el enlace abierto
   sin haber entrado pide la sesión «para ver tu compra» —«Tu sesión terminó» queda para la que venció—.
   Tres claves nuevas en `Events.Purchase` (`FreeContinue`, `FreeReceived`, `LinkSignIn`): 23 en la
   sección, 789 en el diccionario.

**Cómo se verificó**
- Antes del primer arreglo, la base en verde: UI `npm test` 6 de 6 (847 + 107 + 2.061 + 11) y
  `contracts:validate` con `cms-sync` in sync; G-6 537/9 y G-7 55/21.
- Tras cada arreglo, en el UI `npm test` 6 de 6 y `contracts:validate` entero; en el CMS, cuando se tocó,
  `usync-audit` 0/0, build en 0 avisos y las tres suites; y G-6 y G-7 contra el UI en cada paso. Al
  cerrar: UI **873 + 113 + 2.072 + 11** (874 con el CDN construido); CMS **3171 / 897 / 520 = 4588**;
  G-6 ✓ 537 claves en 9 verticales; G-7 ✓ 55 claves en 21 rutas (su piso, sin moverse); G-15 ✓ 1 enlace.
- **Rebasada otra vez**, el UI, sobre `origin/claude/adoring-franklin-ustp2q` (`6af9486`, UI#96:
  `<synergos-alquiler>` pide su catálogo al montarse); la del CMS no se movió de `51dd8c4a`. Los hashes
  de esta sección y los de la F4 son los rebasados. Sobre el resultado: UI `npm test` 6 de 6
  (**874 + 113 + 2.073 + 11**, con el CDN construido: 2.073 son los 2.072 de arriba más el caso de
  UI#96) y `contracts:validate` entero, con `cms:sync:check` in sync y G-15 ✓ 1 enlace;
  `contratos:http:check` y `contratos:synhost:check` al día; G-6 ✓ 537/9 y G-7 ✓ 55/21; CMS build en
  0 avisos y **3171 / 897 / 520 = 4588**.
- El contraste, por dos métodos: el resolutor de `syn-tokens.css` en las ocho rutas de render (peligro
  ≥ 5,04, aviso ≥ 4,79, éxito ≥ 5,70, contra 1,14) y Chromium con los estilos calculados de las hojas
  compiladas sobre el CSS del CMS en los siete `data-theme`. `audit-themes` sigue en 0 fallos.

  > **Precisado el 2026-10-13 (D4 de la verificación en vivo del cupón endurecido):** ese 0 no cubría
  > los rellenos. `audit-themes` no medía `action-*` ni `text-accent`, y en la octava ruta
  > (`prefers-color-scheme: dark` sin `data-theme`) el relleno `action-primary` daba 2,39:1 contra el
  > panel. Desde UI `bdae4f9`, G-5 los mide en las ocho rutas y aborta si deja de encontrarlos.
- Cada mutante, aplicado por líneas y visto en el diff antes de leer el resultado; todos en rojo (el
  control de lo pagado en el registro gratis, en verde, como debe).
- **Sin medir en vivo otra vez**: publicar el `eventos` endurecido en la CDN piloto y recorrer la compra
  en el navegador (el QR dibujado, el aviso legible, el foco, el enlace) es de la próxima verificación.

**Lo que queda, dicho**
- **Diferidos, heredados y fuera de la F4**: la consola del organizador abre en un `EVT-1` de ejemplo con
  datos falsos (`loadManage` sin evento propio: es la cara del organizador, no la compra); y crear un
  evento deja tildes en el slug y en el código de localidad (`charla-íntima-f4`, `ÚNIC`): se arregla en
  el alta de eventos del CMS, y cambiar cómo se deriva el código cambia la clave de precio de lo ya
  publicado, así que va con su ticket y su migración.
- **Los censos que sólo bajan**: 13 alias de lavado como tinta (consola, autoría, formularios, reseñas,
  form-stepper, pax-selector, seller y las comillas de los testimonios) y 9 etiquetas crudas sin declarar
  en academy, blogs, gov, storefront y travel-shell. Declararlas cambia los `<script>` de otras páginas:
  se mide en cada una.
- **G-15 no ve los `appsettings.<Entorno>.json`** ni una variable de entorno que sobrescriba la ruta.
- **Un 2xx con el cuerpo cortado sigue siendo `cliente.respuesta_ilegible`** (no transitorio) en el
  transporte: la compra ya no se equivoca porque pregunta a la saga, pero una lectura así no se reintenta.
- **La confirmación que abre el enlace del aviso no sabe si la compra fue gratis**: dice «Pago recibido»
  como antes.
- **El import de las 23 claves de `Events.Purchase` a la base real** es del arquitecto; sin él, salen los
  respaldos es-CO.

## El cupón reconectado (2026-10-13)

**El cupón de Eventos vuelve, y esta vez lo aplica el servidor.** La F4 (paso 9, UI `4199329`) lo
había quitado porque restaba en el NAVEGADOR un descuento que nadie cobraba: ni la ruta vieja ni la
puerta recibían un código. `Api.Pricing` ya tenía promociones (`SavePromotion` con vigencia,
`QuoteRequest.promotionCode`); faltaba que la compra las usara. Hoy quien compra escribe un código, ve
el descuento ANTES de pagar —cotizado de verdad por Pricing, sin reservar nada— y lo que se autoriza y
se cobra es el total descontado.

Commits CMS (`lego/integracion`): `a93cb184` (la compra lleva el código y Pricing lo descuenta),
`f336f328` (`FlowRunner.AnticiparAsync`: la fase que abre, anticipada sin saga), `c6441cb0` («cotizar»
por la puerta), `129fdd60` (seis claves de `Events.Purchase`) y el de esta sección. UI: `15db938` (el
tipo con `promotionCode`), `2526544` («cotizar» en el vocabulario del generador), `adf0b33`
(`cotizarCompraDeEventos` en `vitals/core`) y `da4b32a` (el cupón en `<synergos-eventos>`).

**Lo que se decidió, y entra en la decisión**
1. **Del navegador sólo viaja el CÓDIGO** (`BuyTicketsRequest.promotionCode`). El monto lo calcula
   Pricing al cotizar, y la definición no aprende ningún lenguaje de expresiones: `pricing.cotizar` lee
   un nombre más (`promocion`, que puede ser nulo) y `IPricingPort.CotizarAsync` lo pasa. Sin código, el
   cuerpo que va a Pricing es el de antes, byte a byte. Un código que no existe (404
   `pricing.promotion_not_found`) o vencido (410 `pricing.promotion_expired`) rechaza en «cotizar»,
   antes de apartar: sin saga, con el aforo entero, y la misma llave compra después sin él.
2. **La vista previa honesta es la fase que abre, anticipada** (`FlowRunner.AnticiparAsync`, en
   `Bff.Core`): corre la fase hasta antes de su primer tramo que reserva, sin saga, sin turno y sin
   escribir nada, con los MISMOS pasos y la MISMA definición que abrir. No es una fase nueva ni una
   cotización escrita aparte —sería la segunda copia del flujo—. Se apoya en la premisa que abrir ya
   tenía (lo de antes de reservar no escribe) y la hace cumplir: se niega a anticipar un tramo con un
   paso que pide llave o que cierra una reserva, y sólo la fase que abre.
3. **«cotizar» entra en el vocabulario de la puerta** (`EnLaPuertaExtensions.Operaciones`,
   `TablaDeLaPuerta.Operaciones` y el generador del UI, la misma lista en los tres sitios):
   `POST /v1/ticket-quotes` (`QuoteTickets`), con el sujeto y el negocio que pone la puerta y SIN llave,
   que recibe el mismo cuerpo que abrir y contesta `TicketQuoteResponse` —subtotal, descuento,
   impuesto, comisión, total y el código recortado—. Abrir vuelve a cotizar entero: lo que cambie entre
   medias lo rechaza abrir con su `code`.
4. **La comisión sigue siendo sobre el subtotal de las entradas, antes del descuento.** Es la regla de
   la ADR 0137 que muestra el carrito y cruzan los vectores de oro (G-12) en los tres sitios que la
   calculan; cambiarla es cambiarla en los tres. Con 2 × 100.000, un 10 % y un 12 %: 200.000 − 20.000 +
   24.000 = 204.000, medido contra Pricing, Inventory y Payments reales.
5. **El cupón vive en la SESIÓN del front** (la línea `promo:<CÓDIGO>` negativa del pricing, con el
   total del servidor), así sobrevive a la recarga y al login, y `pay` lo manda con la compra. Lo que se
   pinta es lo que contestó el servidor; no queda ninguna resta local.
6. **Con cupón, la intención es otra, y la llave también** (`llaveDeLaIntencion`: la de la sesión más
   una huella del código). Con la misma llave, una saga abierta antes sin el código devolvía su total
   sin descuento y se cobraba lo que la pantalla ya no decía.
7. **El campo vive en el paso «revisar» del checkout**, con `syn-promo-code` del design system: ahí la
   sesión ya está pedida, y la puerta es de miembros. Los rechazos se dicen por `code` con la sección
   `Events.Purchase` (seis claves nuevas, es-CO y en-US); un «no» que no es del código —las entradas
   fuera de venta— se dice con su texto sin culpar al código, y un código que vence entre la vista
   previa y el pago se quita y el asistente lo dice una vez.

**El retiro de la ruta vieja: DESCARTADO por decisión del arquitecto (2026-10-13).** Synergos es un
superset de piezas estratégicas para generar funcionalidades, y lo que hoy no se usa no tiene por qué
borrarse (CLAUDE.md §0.C.21: no se retira por defecto). `/api/eventos/checkout|confirm` de
`EventosController`, el motor en proceso de la compra (`StubEventTicketingService`) y la mitad de
compra de `HttpEventTicketingService` **se conservan como pieza detrás de su interruptor**
(`Synergos:Eventos:Mode`: en proceso por defecto, `Bff` por el orquestador). El UI no la llama —sigue
la decisión 10 de la F4— y la ruta vieja no recibe código: un cupón sólo se cobra por la puerta. Los
pasos 19-20 del plan de la F3 no se ejecutan. Y la ruta `/api/eventos/promo` que llamaba el cupón de
antes no existía en el CMS (`[Route]` de `EventosController` y una búsqueda de «promo» en controllers y
servicios: 0): no había nada que conservar.

> **Complementado el 2026-10-14 (regla del superset):** conservarla en el CMS no bastaba. El UI ya no
> tenía su cliente (la F4 lo borró en `1f927577`) y, sin orquestador, el elemento no vendía. Se repuso
> en el UI detrás del camino que declara el CMS, así que «el UI no la llama» dejó de ser cierto cuando
> el CMS declara «directa». Lo demás sigue: la ruta vieja no recibe código, y un cupón sólo se cobra
> por la puerta. Ver «La ruta vieja repuesta detrás del camino declarado».

**Cómo se verificó**
- Cada paso con su repo en verde: en el CMS, build en 0 avisos y las tres suites (de 3171 / 897 / 520
  = 4588 a **3172 / 909 / 520 = 4601**), el oráculo de la F1 sin tocar; en el UI, `npm test` 6 de 6 (de
  874 + 113 + 2.073 + 11 a **875 + 115 + 2.079 + 11**) y `contracts:validate` en EXIT 0 con
  `contratos:http:check`, `contratos:synhost:check` y `cms-sync` al día; y en cada paso G-6 ✓ 537
  claves en 9 verticales y G-7 ✓ 55 claves en 21 rutas. `usync-audit` 0/0.
- Los pares de los dos repos se midieron juntos: el contrato que cambia en el CMS y el tipo que lo
  regenera en el UI.
- La compra con cupón, contra el `Program` real de `Bff.Eventos` y Pricing, Inventory y Payments reales
  (`CuponDeLaCompraTests`, 8 casos): el cobro autorizado, el aforo, las sagas en el disco y que lo que
  cotiza «cotizar» es lo que abrir autoriza.
- Una corrida de `Arquitectura.Tests` dio 1 rojo en `ComposeStackTests.El_check_ignora_el_fin_de_linea`
  mientras la suite del UI corría al lado (el test reescribe `compose.prod.yml` del árbol); solo pasa,
  y la suite entera repetida sin carga dio 520/520. Va dicho, no embolsado.
- El bundle de `eventos` (dist) pasa de 415.760 a 420.501 B (73.149 gz), bajo su techo de 420 KB.
- **Sin medir en vivo**: publicar `eventos` en la CDN piloto y recorrer el cupón en el navegador (en
  los temas) es de la próxima verificación.

**Mutantes** (cada uno aplicado por líneas, visto en el diff, rojo y restaurado; el detalle, en el
mensaje de cada commit): el paso sin el código, el endpoint sin él, el cliente sin `promotionCode` o
con otro nombre, el código sin recortar; anticipar la fase entera, sin la guarda de la llave o
cualquier fase; «cotizar» sin `.EnLaPuerta` (después de regenerar, `LoQueExponeLaPuertaTests`), la
vista previa sin la comisión o sin el código, el descuento de Pricing sin leer, la puerta del CMS sin
«cotizar» en su vocabulario (41 rojos); el generador sin «cotizar»; el descuento sin leer en `vitals`,
«cotizar» que pide «abrir» y `serviceFee` renombrado en el tipo generado (regla 24, `tsc` en rojo); un
`.config` del diccionario fuera del árbol; y en `eventos`, `pay` sin el cupón o con la llave de
siempre, el total que lo ignora, el descuento restado en el navegador (el cupón de antes), el pago
fallido que no lo quita, el código inexistente leído como fallo de red y la vista previa sin el código.
Todos rojos.

**Datos de demo: cómo se crea una promoción para verificarlo.** La oferta que publica el CMS no crea
promociones, y `tools/provisionar.sh` tampoco (provisiona precios, recursos y plantillas). Se crea en
`Api.Pricing`, con la llave compartida, como la crearía quien opera el sitio:

```bash
curl -X POST "$PRICING_URL/v1/promotions" \
  -H "X-Synergos-Key: $SYNERGOS_API_KEY" -H "Idempotency-Key: promo-BIENVENIDA10" \
  -H "Content-Type: application/json" \
  -d '{"code":"BIENVENIDA10","kind":"Percentage","value":1000,"currency":"COP",
       "validFrom":"2026-10-13T00:00:00Z","validTo":"2026-11-13T00:00:00Z"}'
```

`kind` es `Percentage` —`value` en puntos básicos: 1000 = 10 %— o `Fixed` —`value` en la moneda—; la
vigencia es obligatoria (`validTo` excluido), el código es único (`pricing.code_taken`) y se busca sin
mirar mayúsculas. En el compose, `PRICING_URL` es `http://api-pricing:8080`. `CompraDeEventosReal.Promocion`
hace lo mismo en las suites.

**Lo que queda, dicho**
- **La verificación en el navegador** del cupón sobre la CDN piloto, con la promoción creada como
  arriba y la sesión del miembro.
- **El import de las 6 claves nuevas de `Events.Purchase`** (29 en la sección) a la base real es del
  arquitecto; sin él, salen los respaldos es-CO.
- **Cambiar el código después de abrir** deja la saga anterior apartada hasta el barrido de abandono,
  como cambiar la selección (heredado de la F4).

  > **Corregido el 2026-10-13 (el cupón endurecido, punto 1):** era peor que un apartado —con la
  > saga ya cobrada era un doble cobro—, y ya no se puede: abierta la saga de la intención, el cupón
  > queda fijado hasta que se deshaga.

  > **Precisado el 2026-10-13 (D2 de la verificación en vivo del cupón endurecido):** «ya no se puede»
  > valía para la MISMA intención. Eligiendo otra vez las entradas sí se podía —se midieron en vivo dos
  > cobros—, hasta el arreglo. Ver «La verificación en vivo del cupón endurecido y sus cinco defectos».
- **El impuesto no tiene fila**: la oferta de Eventos publica impuesto 0. Si una localidad lo tuviera,
  el total del servidor lo incluye y las filas no lo explicarían.
- **Pricing no cuenta usos**: un código vale mientras esté vigente, para quien sea y cuantas veces sea.
  Un cupón de un solo uso o por miembro es una regla de la capacidad que no existe.

  > **2026-10-13 (el cupón endurecido, punto 3):** el oráculo de «cotizar» y «abrir» lo frena un tope
  > de códigos que no sirven por comprador en `Bff.Eventos`; el tope de usos sigue diferido.
- **El índice §11.2** (repo `synergos`) no se tocó: el checkout principal lo usan otros.

## El cupón endurecido (2026-10-13)

La verificación en vivo del cupón y su revisión (`_informes/79-adr-0140-cupon.json`) dejaron tres
hallazgos confirmados y cuatro defectos vistos en el navegador. Esta sección dice cómo quedó cada
uno; nada de lo anterior se borra. Commits (`lego/integracion`, locales): CMS `6419dce1` y
`b58b26b3`, más el de esta nota; UI `bc4626f`, `40c17a8`, `6732fd6`, `81ca51c` y `1c4887a`.

**1. Una vez abierta la saga de la intención, el cupón queda FIJADO (hallazgo de severidad alta: el
doble cobro).** Con un pago ya anotado —cerrar capturó y las entradas no llegaron, o cerrar dio un
504 con la captura hecha—, aplicar o quitar el cupón en «revisar» cambiaba el total,
`capturedReference` dejaba de reconocer el cobro y el reintento abría con OTRA llave
(`sessionId~huella`): otra saga, que apartaba, autorizaba y cerrar capturaba. Medido en el spec
antes del arreglo, sobre un borde que abre una saga por llave: dos sagas cobradas. Se decidió
**fijar, no cancelar**: `cancelar` (`CompensateAsync`) rechaza una saga `Completed` con
`already_completed` —deshacer lo cobrado es una devolución con su política, #183—, así que cancelar
no cubre justo el caso del doble cobro, y fijar nunca deja dos sagas cobrables.
- La estrategia RETIENE la llave en las líneas (`holdRef`) en cuanto envía abrir, y cada reintento
  abre con ESA llave (la misma llave devuelve la misma saga). La suelta sólo cuando ninguna saga
  puede cobrar con ella: un «no» del negocio o de la puerta antes de reenviar, o la saga deshecha
  (`Compensated`). Lo transitorio y lo ilegible (504, 502 `puerta.respuesta_invalida`) pueden llegar
  con la saga abierta: la llave se queda y el reintento la recupera.
- El elemento fija el cupón con la llave retenida o con un pago anotado (éste cubre la sesión que
  dejó el bundle anterior), y `syn-promo-code` gana `locked`: no deja escribir, aplicar ni quitar, y
  dice por qué (`Events.Purchase.PromoLocked`).
- Para cambiar el código hay que esperar a que la saga se deshaga, o elegir otra vez las entradas
  (otra sesión, otra intención: lo heredado de la F4, abajo).

  > **Corregido el 2026-10-13 (D2 de la verificación en vivo, `_informes/80-adr-0140-cupon-en-vivo.json`;
  > verificado en vivo el 2026-10-14):** elegir otra vez NO era una salida segura. Con la saga ya
  > cobrada —cerrar capturó y su respuesta se perdió—, la selección nueva reemplazaba la sesión entera
  > (`selectIntoCart` → `SessionStore.reset()`): otra sesión, sin la llave retenida ni el pago anotado,
  > con el cupón desbloqueado. Abrir con la llave nueva abrió y cobró otra saga. Se midió en el almacén
  > de Payments y en el de sagas: `pta-32f13f7f…`, 50.400 COP, y `pta-a2f19519…`, 45.900 COP, del mismo
  > comprador y para el mismo evento; la segunda se abrió 20,9 s después de capturada la primera,
  > debajo de «no se te cobrará dos veces».
  >
  > Desde el arreglo, elegir otra vez con una compra pendiente (llave retenida, o pago anotado sin
  > confirmar) la resuelve primero por su id: completada, se muestran sus entradas; en curso, se termina
  > o se cancela; deshecha, se sigue; sin respuesta, no se abre otra. Y la sesión con una compra
  > pendiente no vence a los 30 min. Para cambiar el código con la saga abierta hay que cancelarla, si
  > no se cobró, o esperar a que se deshaga. Con la saga cobrada el código no cambia: deshacerla es una
  > devolución (#183).
  >
  > El orquestador NO se niega a abrir una segunda compra del mismo comprador y evento: no sabe si quien
  > compra vio el acuse de la primera. Eso lo sabe el artefacto del CMS (orden `Pending` o `Confirmed`),
  > y de ahí sale la fase D. Negarse sólo con la saga en curso no habría evitado lo medido, y negarse
  > con la completada rompería comprar dos veces a propósito (la opción B, declarada y apagada: ver «La
  > verificación en vivo del cupón endurecido y sus cinco defectos»).

**2. Con «Pagar y confirmar» en vuelo el cupón no cambia, y la pantalla nunca muestra un descuento
distinto del autorizado (hallazgo de severidad media).** «revisar» no es válido mientras se valida
un código (el pago espera a la vista previa); una vista previa que vuelve con la compra ya en vuelo
se descarta; y `pay` deja en la sesión lo que ABRIÓ: el total del servidor y sólo la línea del
cupón que viajó. Pagar primero y aplicar después ya lo cierra el punto 1 (la llave se retiene en el
mismo tic del clic).

**3. El tope de códigos que no sirven (hallazgo de severidad baja: «cotizar» como oráculo).**
«cotizar» no pide llave, no aparta nada y contesta distinto a un código que no existe (404
`pricing.promotion_not_found`), a uno vencido (410 `pricing.promotion_expired`) y a uno que vale
(200 con su descuento): un miembro podía mandarla en bucle y enumerar qué códigos existen y cuánto
descuentan. «abrir» con un código que no sirve rechaza igual antes de apartar, con una llave que
inventa el navegador: el mismo oráculo. Se decidió **un tope por comprador y ventana en
`Bff.Eventos`** (`IntentosDeCodigo`), y no en la puerta: la puerta no sabe de ningún vertical ni lee
cuerpos (`PuertaGenericaTests`), y sólo el orquestador ve a la vez el sujeto, el código y lo que
contestó Pricing.
- Cuenta los rechazos de Pricing por el código (`pricing.promotion_*`) de cada sujeto, en «cotizar»
  y en «abrir»; un código que vale no cuenta.
- Agotado el tope, toda cotización o apertura CON código de ese sujeto contesta 503
  `eventos.demasiados_codigos`, **transitorio** (con esperar basta), ANTES de llamar a Pricing: no se
  aparta ni se autoriza nada, y el corte no dice si el código existe.

  > **Corregido el 2026-10-13 (D3 de la verificación en vivo):** contesta 409
  > `eventos.demasiados_codigos`, **NO transitorio** (`Rejection.Conflict`), ANTES de llamar a Pricing.
  > `transient` dice si repetir LO MISMO ya puede salir distinto, y la pieza del CMS repite en el acto
  > todo lo transitorio que lleva llave (#178). Con el 503, cada «abrir» cortado eran 3 llamadas al
  > orquestador (dos reintentos) y un Error en el log (informe 80). Pasada la ventana se vuelve a pedir,
  > como cualquier conflicto, y lo dice `PromoTooMany`. El OpenAPI ya publicaba el 409; la puerta, la
  > pieza y el UI no cambian de código. CMS `5f31dad8`, UI `0973a4c`; en vivo, una sola llamada
  > (informe 83). El resto del punto no cambia.
- Sin código se compra igual, y una apertura cuya llave devuelve su saga viva no se corta (no
  vuelve a cotizar, así que no es oráculo): **la semántica de los pagos no cambia** (#183).
- `Eventos:Cupon:FallosPorVentana` (10) y `Eventos:Cupon:Ventana` (`00:15:00`) los ajusta el
  despliegue. Es memoria del proceso y por réplica, a propósito: un freno contra el bucle de un
  miembro, no una cuenta de usos. **Diferido, con su razón**: un tope de usos por código o por
  miembro es una regla de `Api.Pricing` que no existe, y uno general por miembro para todas las
  operaciones de la puerta sería de la puerta y de todos los flujos, no del cupón.
- El UI traduce el corte con `Events.Purchase.PromoTooMany` (`syn-promo-code` gana el motivo
  `too-many-attempts`), y un «abrir» cortado suelta la llave retenida: no dejó saga.

**4. `syn-promo-code` vuelve a tener estilos, con contraste AA en los siete temas (defecto en vivo,
severidad media).** Las `--syn-promo-*` se declaraban en `.syn-promo-code`, la clase que pone el
host; con encapsulación emulada Angular la compila como `.syn-promo-code[_ngcontent-…]`, un atributo
que el host no lleva: la regla no casaba nunca (es de #29, anterior al cupón, y storefront monta la
misma pieza). Van en `:host`; el borde del campo pasa a `--syn-color-text-muted` (border-default es
decorativo: 1,2–1,5:1) y el texto de ejemplo tiene regla propia. Medido en Chrome headless contra
`syn-tokens.css` en light, dark, eventsNight, silverGold, terraLux, scholar y meridian: 0 fallos
(mínimos: borde del campo 5,88:1; «Aplicar», 4,81:1 de texto y 4,39:1 de relleno contra el panel).
`libs/shared` cambió: hay que reconstruir el runtime (`build:runtime`) y publicarlo donde se
verifique —en la CDN piloto, nunca en `C:\LOCAL_CDN` desde una rama—.

> **Precisado el 2026-10-14 (D1 de la verificación en vivo, ADR 0141):** publicarlo no bastaba. El
> runtime se republicaba bajo la MISMA URL `immutable`, y quien ya lo tenía se quedaba con el viejo
> hasta un año (medido en vivo: `syn-promo-code` sin estilos ni `locked`). Desde la 0141 la URL del
> runtime lleva la huella de sus bytes: publicar con `publish-runtime` basta y nadie tiene que vaciar
> su caché, porque quien tenía el `sg-shared` viejo baja el nuevo: es OTRA URL.

**5. «Tu orden» explica el total, y el signo del descuento no se separa de la cifra (defectos en
vivo, severidad baja).** `syn-checkout-wizard` gana `summaryRows` (las filas que explican el total,
ya formateadas por el dominio) y Eventos le pasa los cargos y el descuento; el descuento lleva
U+2060 tras el «−», y los importes no se parten.

**6. La copia piloto puede no existir: cómo se recrea para verificar.** `%TEMP%\pilot-0137\vivo\`
(`vivo.db` con su `-wal`/`-shm` y `tmp\`) vive en `%TEMP%`, y una limpieza se la llevó el
2026-10-13 a las 07:49 junto con `f4-verif\respaldo\vivo`, dejando la CDN piloto en 11 ficheros. Los
lanzadores de `_informes/scripts/orquestador/` (`integ-sobre-copia.cmd`, y `f3-cms.ps1`, que lo
llama) fijan `Data Source=…\vivo\vivo.db`: **lanzados sin la copia, Umbraco arranca sobre una base
VACÍA** (SQLite crea el fichero) y la verificación mide un sitio que no existe. Antes de verificar:
- **Comprobar** que `%TEMP%\pilot-0137\vivo\vivo.db` existe y que `PRAGMA integrity_check` da `ok`;
  si no, no lanzar nada.
- **Recrear la base sin tocar la real**: una copia CONSISTENTE de la base de trabajo —con la API de
  respaldo de SQLite (`sqlite3 <origen> ".backup <destino>"`, o `Connection.backup` de Python), que
  incluye lo que está en el `-wal` aunque el CMS esté corriendo; o copiar `.db`, `-wal` y `-shm`
  JUNTOS con el CMS parado—, nunca el `.db` solo con el CMS vivo. Lo que no tenga la copia (las
  claves nuevas de un diccionario, un ElementType) se importa SÓLO sobre la copia, desde una carpeta
  fuera del repo, como en la F4. Si no hace falta el contenido del sitio, sirve una copia de
  `%TEMP%\pilot-0137\lote\lote.db` (con su `-wal`/`-shm`), la que usó la verificación del cupón (sin
  las claves de `Events.Purchase`: `t()` pinta los respaldos es-CO).
- **Recrear la CDN piloto**: respaldar lo que tenga, copiar `C:\LOCAL_CDN\synergos` en sólo lectura
  (`robocopy /E`), publicar `eventos` con `tools/publish.mjs --element eventos --cdn <piloto>` y, si
  cambió `libs/shared` —el endurecimiento lo cambió—, el runtime con `publish-runtime --cdn
  <piloto>`; sin él, `syn-promo-code` se queda con los estilos y las entradas viejas.

  > **Precisado el 2026-10-14 (ADR 0141):** el `publish-runtime` de hoy publica el runtime por
  > contenido, y un navegador que ya tuviera el de antes baja el nuevo sin vaciar la caché. La piloto
  > quedó así el 2026-10-14 (informe 83): runtime por contenido con su `latest/huellas.json`, y
  > `eventos` 0.1.11. Su estado anterior está copiado en
  > `%TEMP%\pilot-0137\defectos-verif-r1\cdn-al-empezar`.
- **Respaldar** la copia antes de lanzar (`f3-cms.ps1` lo pide) y, al terminar, parar por PID
  (`f3-parar.ps1`) y borrar las credenciales de los miembros de prueba que hayan quedado en ella.

**Cómo se verificó.** Cada arreglo con su repo en verde: en el CMS, build en 0 avisos y las tres
suites (de 3172 / 909 / 520 a **3172 / 917 / 520 = 4609**), `usync-audit` 0/0 y `spec-valida` ✓; en
el UI, `npm test` 6 de 6 (de 875 + 115 + 2.079 + 11 a **875 + 115 + 2.097 + 11**) y
`contracts:validate` EXIT 0, con `contratos:http:check` al día y `cms-sync` in sync; y en cada paso
G-6 ✓ 537/9 y G-7 ✓ 55/21. Cada arreglo con su mutante fiel (hacia el código de antes), rojo y
restaurado; el detalle, en el mensaje de cada commit. **Sin medir en vivo**: la compra en el
navegador sobre la CDN piloto, porque la copia `vivo` no existe hoy (punto 6).

**Rebasada sobre master** (2026-10-13): la del CMS, de `51dd8c4a` a `09213242` (#205: la poda de
`umbracoCacheInstruction` ya no tranca el import del gate de reconstrucción); la del UI no se movió de
`6af9486`, pero sus mensajes nombraban hashes del CMS y se reescribieron (sólo el mensaje: los árboles,
idénticos). Los hashes de esta ADR son los rebasados. Sobre el resultado: CMS build en 0 avisos,
**3172 / 917 / 520 = 4609**, `usync-audit` 0/0, las 403 vistas compilan y la reconstrucción desde el
XML verde (1517 / 1517 ítems, 0 errores); UI `npm test` 6 de 6 y `contracts:validate` entero, con
`cms-sync` in sync, `contratos:http` y `contratos:synhost` al día y G-15 ✓ 1 enlace; G-6 ✓ 537/9 y
G-7 ✓ 55/21.

**Lo que queda, dicho**
- **La verificación en el navegador** del cupón endurecido, con la copia piloto recreada (punto 6)
  y el runtime publicado en la CDN piloto.

  > **Hecha el 2026-10-13** con la copia recreada (informe 80: cinco defectos) y, con los arreglos, el
  > 2026-10-14 (informe 83: 12 de 12 escenarios). Ver la sección siguiente.
- **El import de `Events.Purchase.PromoLocked` y `PromoTooMany`** a la base real es del arquitecto.

  > **2026-10-14:** se suman las 11 claves de los arreglos: `PendingCompleted`, `PendingOpen`,
  > `PendingUnknown`, `PendingResume`, `PendingCancel`, `PendingChooseOther` y `PendingUnanswered`
  > (CMS `f7e20828`), y `PendingFound`, `PendingOpenElsewhere`, `PendingUnknownElsewhere` y
  > `RefundInProgress` (CMS `c5de8b60`). Son 42 en la sección y 808 en el diccionario. Para verificar se
  > importaron sólo sobre la copia piloto.
- **Una saga abierta y luego abandonada** (otra selección, otra sesión) sigue apartada hasta el
  barrido de abandono, como en la F4; el cupón ya no puede abrir una segunda para la MISMA intención.

  > **Corregido el 2026-10-13 (D2):** «otra selección» ya no abandona una saga que pueda cobrar, porque
  > primero la resuelve. Hasta el barrido sólo queda apartada la saga cuyo abrir no contestó, si se
  > eligen otras entradas igual. Ésa no se puede cobrar —nadie tiene su id—, pero su autorización queda
  > retenida hasta el barrido (medido en vivo: `pta-968d590f…`, autorizada y sin capturar). Fuera de
  > este navegador —otro dispositivo, los datos del sitio borrados, más de 24 h— lo cubre la fase D, la
  > lectura de las compras sin acuse en el artefacto del CMS. Esa lectura no ve una saga sin asistentes
  > anotados, que tampoco se puede cobrar.
- **La octava ruta de render** (`prefers-color-scheme: dark` sin `data-theme`) no se midió para
  `syn-promo-code`: `action-primary` contra la superficie da 2,39:1 ahí, por el token del tema.

  > **Corregido el 2026-10-13 (D4):** está medido —2,39 en vivo, 2,39 con el resolutor de
  > `syn-tokens.css` y 2,388 a mano— y no era de `syn-promo-code`. El bloque `@media
  > (prefers-color-scheme: dark)` de `syn-tokens.css` espejaba 40 de los 83 tokens del tema oscuro y
  > no redeclaraba ningún `action-*` ni `text-accent`. Por eso todo relleno `action-primary` —el botón
  > primario, el CTA del asistente, los shells— daba 2,39:1 contra el panel, y `text-accent`, que es
  > TEXTO, también. La ruta está viva en `/admin` y en la página de error, que no ponen `data-theme`.
  > Hoy el bloque espeja los 83, con un test de paridad en el CMS (`1f7711dc`); en vivo, el relleno da
  > 5,15:1 y el texto 6,29:1.

> **2026-10-13 (D5):** la fila «Cargos por servicio» se omite cuando el CARGO es 0, con una sola regla
> en el carrito, «Tu orden» y «Revisa tu orden» (UI `8b2d1f4`). Es la misma regla por importe que usan
> el pricing de la sesión y el `omitir_si_cero` del flujo, y cubre también un evento pagado en un sitio
> sin comisión. Ninguna línea de esta ADR afirmaba lo contrario.

## La verificación en vivo del cupón endurecido y sus cinco defectos (2026-10-13)

**La verificación en vivo** (`_informes/80-adr-0140-cupon-en-vivo.json`, 2026-10-13) recorrió la compra
sobre la copia piloto recreada: los cinco servicios en 5871-5875, el CMS en 5194 sobre `vivo.db` y la CDN
piloto, CMS `01f3a0ec` y UI `1c4887a`, Chromium. Usó tres miembros de prueba creados por HTTP y dos
promociones creadas en Pricing. De ocho escenarios, seis salieron bien y dos con defecto: **cinco
defectos**, uno alto, uno medio y tres bajos. Nada de lo anterior se borra; donde esta ADR decía algo que
resultó falso va una nota fechada en su sitio.

**Lo que salió bien en el 80:**
- el cupón válido, cobrado una vez con el descuento (BIENVENIDA10: 91.800 COP por dos entradas;
  FIJO5000: 45.400);
- el código desconocido, dicho con el texto del diccionario;
- el cupón fijado con la MISMA llave tras un 504 de cerrar o de abrir, también tras F5 y con dos
  pestañas pagando a la vez (un cobro);
- el pago en vuelo, que no cambia el descuento;
- el tope de códigos por comprador;
- los estilos AA en los siete temas, cuando el runtime llega;
- el registro gratis, con 0 cobros.

**El plan de arreglo** (`_informes/82-adr-0140-defectos-en-vivo-plan.json`) eligió entre opciones medidas
para cada defecto. Cada arreglo es un commit con su test y su mutante fiel, hacia el código de antes, y
el detalle va en el mensaje de cada commit. Todo está en `lego/integracion`, local.

### D1 (alta) — el runtime compartido se republicaba bajo la MISMA URL inmutable

- **Síntoma medido.** En un navegador que ya tenía el runtime, `sg-shared.js` salía de la caché
  (`transferSize` 0, 801.154 B) mientras el servidor servía 802.496 B en la misma URL, con `immutable` de
  un año. `syn-promo-code` quedaba sin estilos y sin `locked`. No hubo doble cobro, porque la guarda vive
  en `eventos`.
- **Causa.** La carpeta `runtime/angular/21.1.6/` lleva la versión de Angular, no la del contenido, y las
  dos políticas de caché la servían `immutable`.
- **Arreglo.** Lo decide la [ADR 0141](0141-el-runtime-compartido-se-publica-por-contenido.md)
  (propuesta): el runtime se publica por contenido. CMS `3a79c36f` y `c6a9973d`; UI `45609b3`, `822e95b`,
  `ce4b73d`, `83f66ae`, `5985436`, `60af1ab`, `6dada50`, `2fee048`, `eaa9338`, `0ed9627` y `ac3e197`.
- **En vivo** (informe 83, escenarios 1, 1b y 1c): el perfil envenenado se rescata sin vaciar nada
  (`sg-shared.18a9decbae7fffb0.js`: 802.796 B transferidos y un SHA-256 que casa con la huella), y
  `syn-promo-code` vuelve a tener estilos y `locked`. La segunda visita no hace ni una petición. Las
  cabeceras y los 304 son los de la 0141, y republicar sin cambios no toca ninguno de los 66 ficheros.

### D2 (media) — el doble cobro eligiendo otra vez las entradas

- **Síntoma medido**, por dos métodos (el almacén de Payments y el de sagas). Cerrar capturó y su
  respuesta se perdió; al volver a la ficha y elegir las mismas entradas, abrir abrió y cobró OTRA saga:
  `pta-32f13f7f…`, 50.400 COP, y `pta-a2f19519…`, 45.900 COP, del mismo comprador y para el mismo evento.
  La segunda se abrió 20,9 s después de capturada la primera, que ya estaba `Completed`.
- **Causa.** `selectIntoCart` reemplazaba la sesión entera (`SessionStore.reset()`) sin mirar la llave
  retenida ni el pago anotado. Además, la sesión vencía 30 min después de creada, antes que los 60 min
  del barrido de abandono.
- **Arreglo A, en este navegador** (UI `a2fda2c` y `a2403bd`, el contrato en `b502c4f`, las 7 claves
  `Pending*` en CMS `f7e20828`). Antes de reemplazar la sesión, una compra pendiente —llave retenida, o
  pago anotado sin confirmar— se resuelve por su id:
  - completada: sus entradas y la vista confirmada (`PendingCompleted`);
  - en curso: «Terminar mi compra» o «Cancelar esa compra» (`cancelarCompraDeEventos`, que la deja
    `Compensated`);
  - deshecha: se sigue con lo elegido;
  - sin respuesta: no se abre otra (`PendingUnknown`);
  - si abrir no contestó: «Terminar» con la MISMA llave, o «Elegir otras entradas»
    (`PendingUnanswered`).

  Retener la llave extiende la sesión a 24 h. Funciona tras F5 y en otra pestaña, porque la sesión vive
  en `localStorage`.
- **La fase D, en el servidor**, para otro dispositivo, los datos del sitio borrados o más de 24 h (CMS
  `8f099371`; G-6 congela `compras` en `7e72a8d6`; UI `3cb872b`).
  - `GET /api/eventos/compras/pendientes?eventId=` devuelve las órdenes `Pending` del miembro para ese
    evento, reconciliadas con su saga con el mismo techo que `ReconciliarAsync` (tope 10, 3 s):
    `{ compras: [{ id, status: Running|Completed|Unknown }] }`.
  - Lo que no alcanza a leer sale `Unknown`: no saber no es «no hay». No emite nada y va sin caché.
    Contesta 400 `eventos.evento_requerido` sin evento.
  - `pay` pregunta antes de abrir; con alguna compra no abre (`eventos.compra_sin_acuse`) y el elemento
    la pinta con el mismo panel. Si la lectura no está —red, 5xx, cuerpo ilegible—, abre como siempre:
    se degrada por AUSENCIA, nunca por negación (ADR 0112).
- **La revisión de los arreglos** (`_informes/84-adr-0140-defectos-pausa.json`) dejó 11 hallazgos. Un
  escéptico refutó 2: la carrera del libro sin cerrojo, imposible en producción, y la vuelta atrás, que
  hace lo que hace toda vuelta atrás. Confirmó 9. Los tres de D1 están en la ADR 0141; los seis de D2:
  - **«Confirmed» no es acuse** (CMS `b901ac65`). Una reconciliación ajena a su comprador confirmaba la
    orden y la fase D dejaba de verla. Hoy sólo da acuse `GET …/compras/{id}/entradas` del comprador.
  - **Una compra que se deshace DESPUÉS de cobrar dice que hay dinero por devolver** (`refundPending` en
    `TicketPurchaseResponse`, CMS `9fba61e5`; UI `fc8a9a2`, `Events.Purchase.RefundInProgress`). No
    impide comprar otra.
  - **`pay` pregunta también con la llave retenida**, y la saga de ESTA sesión, reconocida por su id, no
    frena (UI `7cab962`).
  - **El aviso del asistente** dice `PendingFound` cuando la fase D frena, y no «no se te cobró nada»
    (UI `7e2ed4d`).
  - **Las frases de una compra de otra sesión** dicen sólo lo que el código hace (UI `f59e2a8`).
  - **La guarda compara el evento** (UI `82a8344`).

  Las 4 claves nuevas van en CMS `c5de8b60`, y el contrato en UI `4264786`.
- **Peso.** El bundle de `eventos` pasó su techo de 420 KB (mide 423,1 KB), y el techo sube a 440 KB
  con la razón escrita (UI `9930dfb`). 26 KB de los 423 son escapes `\uXXXX` de esbuild; `charset: 'utf8'` los
  quitaría de todos los elementos, pero es un cambio global de build y queda como decisión aparte.
- **En vivo** (informe 83, escenarios 2 a 2d):
  - el camino exacto del doble cobro ya no abre otra compra: 1 consultar, 0 abrir, sus entradas y
    `PendingCompleted`, con un solo cobro;
  - lo mismo tras F5, desde otra pestaña y con `localStorage` borrado (ahí frena la fase D);
  - cada panel según el estado;
  - y el control, comprar dos veces a propósito tras el acuse, sí se puede.

### D3 (baja) — la puerta reintentaba en el acto el tope de códigos

- **Medido.** El 503 `eventos.demasiados_codigos` era `transient:true`, y la pieza del CMS lo repetía:
  tres entradas `puerta-eventos-reintento` en 308 ms (dos Warning y un Error) por cada clic.
- **Arreglo** (opción B del plan). `Rejection.Conflict`: un 409 `transient:false`. El OpenAPI ya
  publicaba el 409, así que no cambia; la puerta, la pieza y el UI tampoco cambian de código. CMS
  `5f31dad8`; UI `0973a4c` fija que la forma de antes y la de hoy se entienden igual durante el
  despliegue. Se descartaron un 429 con `Retry-After` (la pieza decide por la bandera, no por el
  estado), leer `Retry-After` en el cliente (un segundo dato en la decisión de reintentar) y vetar en la
  puerta todo reintento con cuerpo (perdía el reintento útil de `flow.busy`).
- **En vivo** (informe 83, escenario 3): una sola entrada (409, intento 0, nivel Information),
  `PromoTooMany`, la llave suelta y la compra sin código sale.

### D4 (baja) — la octava ruta de render a 2,39:1

- **Medido** por tres métodos (en vivo, con el resolutor y a mano): en la octava ruta el relleno de
  «Aplicar» daba 2,39:1 contra el panel.
- **Causa.** El bloque `@media (prefers-color-scheme: dark)` sin `data-theme` espejaba 40 de los 83
  tokens del tema oscuro. Afectaba a todo `action-*` y a `text-accent`, no a `syn-promo-code`.
- **Arreglo A**, en el token: el espejo completo con un test de paridad en el CMS, donde vive la fuente
  y el PR que la rompería (CMS `1f7711dc`). El UI regenera su puente (UI `b502c4f`) y G-5 mide el relleno
  y el texto de `action-primary` y el `text-accent` en las ocho rutas (UI `bdae4f9`). Se descartaron el
  espejo mínimo (dejaba un TEXTO a 2,39:1), el arreglo en el componente (uno de unos 60 consumidores) y
  poner `data-theme` en el admin (la ruta sigue existiendo).
- **En vivo** (informe 83, escenario 4): sin `data-theme` y con el sistema en oscuro, el relleno da
  5,15:1 y el texto 6,29:1, como `dark`. Los siete temas dan las cifras de antes (eventsNight 4,39 de
  relleno y 4,81 de texto; el borde del campo, mínimo 5,88 en scholar).

### D5 (baja) — el registro gratis mostraba «Cargos por servicio (12 %) $ 0»

- **Causa.** Tres reglas para la misma fila: el carrito la pintaba si la comisión CONFIGURADA era mayor
  que 0, «Tu orden» lo heredaba y «Revisa tu orden» la pintaba siempre.
- **Arreglo A.** La fila se omite cuando el CARGO es 0, con un solo `computed` para las tres vistas (UI
  `8b2d1f4`).
- **En vivo** (informe 83, escenario 5): lo gratis sale sin la fila en las tres vistas y con 0 pagos; lo
  pagado la muestra (5.400 por una entrada, 10.800 por dos).

### La opción B, declarada y apagada

Que el orquestador rechace abrir con OTRA llave cuando el mismo comprador tiene una saga en curso del
mismo evento queda **declarado y sin construir**: un 409 con el id de esa saga y un arriendo por
comprador y evento contra la carrera. Si el arquitecto la quiere, va detrás de un interruptor
`Eventos:Compra:*` apagado por defecto. No se encendió porque **no habría evitado lo medido**: la primera
saga ya estaba `Completed` 20,9 s antes de que se abriera la segunda (dos métodos). Además:
- el rechazo de la puerta sólo lleva `code` y `transient`, así que devolver el id exige una extensión en
  `Synergos.Shared`, una consulta nueva de sagas vivas en `Bff.Core` y el arriendo;
- cambia la conducta de la ruta vieja en `Mode=Bff`;
- bloquea compras paralelas a propósito desde dos dispositivos.

Su código no puede ser `eventos.compra_en_curso`, porque el artefacto del CMS ya lo usa para otra cosa
(no emitir entradas de una compra en curso). Se descartaron también C, negarse con una `Completed`
reciente (rompe comprar dos veces a propósito, o cambia el contrato y pide un índice nuevo), y E,
conservar el `sessionId` al elegir (con otras líneas devuelve la saga VIEJA).

### Lo que midió la verificación en vivo final (informe 83, 2026-10-14)

**Sobre qué.** La copia piloto con CMS `6b495c9d` y UI `7faa386`. Las 11 claves nuevas se importaron
SÓLO a la copia, desde una carpeta fuera del repo. El runtime por contenido y `eventos` 0.1.11 se
publicaron en la CDN piloto, nunca en `C:\LOCAL_CDN`. Resultado: **12 escenarios, 12 OK, 0 defectos**.

| escenario | lo medido | en el 80 |
|---|---|---|
| D1, runtime en caché | el perfil envenenado baja `sg-shared.<huella>.js` (802.796 B transferidos) sin vaciar nada; vuelven los estilos y `locked` | sin estilos hasta vaciar la caché |
| D1, cabeceras | con huella, `immutable` + ACAO; los planos, los mapas y el libro, `no-cache`; 304 a las dos revalidaciones | todo `immutable` |
| D1, republicar | 0 de 66 ficheros cambian | — |
| D2, el camino del doble cobro | 0 abrir, `PendingCompleted`, un cobro de 45.900 | dos cobros |
| D2, F5, otra pestaña y `localStorage` borrado | 0 abrir en los tres; en el último frena la fase D | — |
| D2, paneles | en curso: terminar o cancelar (un cobro en total); abrir sin respuesta: terminar con la misma saga o elegir otras; sin respuesta: `PendingUnknown` y 0 abrir | — |
| D2, control | comprar dos veces a propósito tras el acuse: dos cobros, sin panel | — |
| D3 | un 409, un intento en el log, `PromoTooMany` | tres intentos con 503 |
| D4 | 5,15 de relleno y 6,29 de texto; los siete temas, iguales | 2,39 |
| D5 | gratis sin la fila; lo pagado con ella | «$ 0» |
| cupón | el válido y el fijado tras un fallo (la MISMA llave), un cobro cada uno; el desconocido, con el texto del diccionario | igual |
| camino de compra (escenario 7) | ver «La ruta vieja repuesta detrás del camino declarado» | — |

**Los cobros, contados por dos métodos** (almacén de Payments y de sagas):
- 14 pagos: 11 capturados, uno por saga, y 3 sin capturar (2 cancelados, `Compensated`, y la
  autorización de la saga abandonada con «Elegir otras»).
- 15 sagas, ninguna con dos capturas.
- Avisos: 12 `.eml`, uno por compra completada por la puerta, y 2 en el buzón del CMS, de la ruta
  directa.

**Observaciones, que no son defectos:**
- Con «Elegir otras» después de que abrir no contestara, la saga que el servidor sí abrió queda
  autorizada y sin capturar hasta el barrido, y la fase D no la ve.
- Cuando la fase D frena, `pendientes` se pide dos veces.
- El cliente reintenta tres veces un consultar sin red.
- La confirmación del registro gratis se titula «¡Compra confirmada!».

**No se midió en esta ronda**, aunque estaba en el plan: de D1, el cambio trivial de `libs/shared`
publicado en la piloto, el humo conectado contra la piloto, el export nuevo con el publicador viejo y la
vuelta atrás; de D2, el reloj adelantado 31 min; de D4, `/admin` y `/error/404`. Y el envenenamiento se
sembró emulando la cabecera exacta del CMS viejo, no con su binario.

**En las suites**, tras los arreglos y su revisión:
- CMS: build con 0 avisos, **3216 / 924 / 520 = 4660**, `usync-audit` 0/0.
- UI: `npm test` 6 de 6 (**998 + 117 + 2.125 + 11**; 999 con el CDN construido) y `contracts:validate`
  EXIT 0.
- Gates cruzados: G-6 ✓ 538 claves en 9 verticales y G-7 ✓ 55 en 21 rutas.
- `build:cdn` sale con 0 y su puerta de enlace pasa 125 bundles de Angular y 1 de Preact.

Las cifras finales, con el camino declarado, van en la sección siguiente.

**Lo que queda, dicho**
- **El import a la base real** de las 11 claves nuevas de `Events.Purchase` es del arquitecto; hasta
  entonces `t()` pinta el respaldo es-CO.
- **El push a `master`** de los dos repos es del arquitecto, en el orden del plan: primero el CMS (el
  job G-1 del design-gates del CMS sale rojo hasta que sube el UI: `gh run rerun --failed`), y nunca el
  UI a medias.
- **El CDN público** no se despliega desde `master` (Workers Builds sale rojo, y `despliegue-cdn.yml` no
  tiene secrets): es del arquitecto. Ver la ADR 0141, «Producción».
- **Los textos `Pending*`** hablan de «compra» también en un registro gratis; las variantes `Free*`
  serían claves nuevas.
- **La autorización de la saga abandonada** con «Elegir otras» queda retenida hasta el barrido.

## La ruta vieja repuesta detrás del camino declarado — regla del superset (2026-10-14)

**La F4 había borrado del UI el cliente de la ruta vieja.** El paso 12 (UI `1f927577`) quitó `checkout`,
`confirm` y `normalizeCheckout` de `eventos-api.client.ts`, y su uso en la estrategia, aunque el CMS
conservaba la ruta y el arquitecto descartó su retiro el 2026-10-13. Así, el interruptor de camino no se
podía conectar de punta a punta: en un despliegue sin orquestador la puerta contesta 503
`puerta.flujo_no_disponible`, y `<synergos-eventos>` no podía vender. Por la regla del superset
(CLAUDE.md §0.C.21: nada se retira por defecto) no se retira ninguno de los dos caminos: se repone el
cliente, y **el CMS declara cuál gana**. Esto complementa el retiro DESCARTADO de «El cupón reconectado».

**La regla de cuál gana** (`CaminoDeCompra.Decidir`, en `Synergos.CMS.Interfaces`; CMS `e82e4a3e`):
1. **El override de negocio**, `Synergos:Features:Eventos:PurchasePath` (`puerta` o `directa`), con su
   override por sitio (`Sitios:{Key}:PurchasePath`, ADR 0137). Se normaliza (sin espacios, en
   minúsculas). Un valor mal escrito no deja arrancar el CMS, y el mensaje dice la regla (CMS
   `6b495c9d`). Un sitio con el valor vacío vuelve a la regla.
2. Si no hay override, **la puerta, si hay orquestador**: si la puerta abre `eventos.compra`, es decir,
   si el flujo está en `Synergos:Puerta:Flujos` y su orquestador tiene destino en
   `Synergos:Eventos:BaseUrl`.
   - Es la MISMA condición con la que `FlujosController` contesta 503 (`IPuertaDeLosFlujos`), y un test
     la cruza con el controller en los cuatro casos.
   - No vale la del composer, «`BaseUrl` o `Mode=Bff`»: con `Mode=Bff` y sin `BaseUrl` la puerta no tiene
     destino, mientras que la ruta directa sí llega al orquestador por su puerto local.
3. Si no, **la directa**: `/api/eventos/checkout|confirm`, con el motor que diga `Synergos:Eventos:Mode`.

Viaja como `purchasePath` dentro del `config` del elemento (`EventosProps.PurchasePath`, origen negocio,
por el resolver tipado de la ADR 0135), y el contrato del SynHost y su tipo TS se regeneraron. La vista
sigue colocando `<synergos-flujo>`, que con «directa» es inocuo.

**En el UI** (UI `5d72686`):
- El cliente se repone como estaba, adaptado a lo que cambió alrededor: lanza `EventosWriteFailedError`
  si el borde no abrió o no emitió.
- Con «directa», `pay` es POST `/checkout` y `confirm` es POST `/confirm`, como antes de la F4. Con
  «puerta», o sin declarar, es lo de hoy. **Nunca cae de un camino al otro.**
- Con «directa» el cupón no se ofrece ni se cotiza, porque esa ruta no recibe códigos: un cupón sólo se
  cobra por la puerta.

**La revisión del camino** dejó cuatro hallazgos, dos de severidad media. Cada arreglo es un commit con
su mutante fiel:
- **Una compra que ya salió se termina por el camino por el que salió** (UI `e05f441`). El camino se
  leía de la página ACTUAL. Si la declaración pasaba de «puerta» a «directa» entre abrir y cerrar —justo
  lo que este cambio deja hacer durante un incidente—, el reintento mandaba a `/confirm` una referencia
  `pta-…` que el borde no conoce, la guarda soltaba la saga y la compra siguiente abría otra al lado: un
  doble cobro. Ahora la estrategia anota el camino en las líneas antes de salir a la red y lo quita con
  la llave. La ruta directa también pregunta por las compras sin acuse (la fase D) antes de abrir.
- **Con «directa», elegir otra vez ya no descarta una orden que pudo cobrar** (UI `67b1039`). `/confirm`
  es idempotente por `orderRef`, así que una orden con un pago anotado y sin confirmar se ofrece
  terminar —confirma la MISMA orden— en vez de reemplazarse. La guarda vale para los dos caminos.
- **Un cupón que quedó en la sesión ya no baja el total del asistente con «directa»** (UI `7faa386`): se
  quita al llegar al checkout, y «Tu orden» y el pago dicen el total que cobra `/checkout`.
- **El arranque que se niega por un `PurchasePath` mal escrito dice la regla** (CMS `6b495c9d`): sin
  valor decide la regla, no «la puerta».

**Los gates vuelven a cruzar la ruta directa** (CMS `76235f6b`, después del UI). El piso de G-7 sube a 60
claves en 23 rutas, el de antes de la F4 con el alquiler, y la línea base de G-6 congela `free` y
`paymentSessionId`. Control: contra el UI anterior a `5d72686`, G-7 sale rojo («la cobertura BAJÓ: 55
claves en 21 rutas, contra un piso de 60 en 23») y G-6 también. **Orden de empuje**: CMS `e82e4a3e` →
UI `5d72686` → CMS `76235f6b`; subir el piso de G-7 antes de que llegue el UI lo pone rojo.

**Medido en vivo** (informe 83, escenario 7):
- **(a) Con orquestador**, el SSR declara `purchasePath` «puerta». En toda esa parte de la verificación
  hubo 43 llamadas a `/api/flujos` y 0 a `/api/eventos/checkout|confirm`.
- **(b) Sin `Synergos__Eventos__BaseUrl`**, el SSR declara «directa» y la puerta contesta 503. La compra:
  - el checkout sale sin `syn-promo-code`;
  - `pendientes` da 503 `eventos.artefacto_no_disponible` y se degrada por ausencia;
  - después, `/api/eventos/checkout` 200 → `/api/eventos/confirm` 200 `Confirmed`, con las entradas
    emitidas, y «¡Compra confirmada!»;
  - 0 llamadas a `/api/flujos`;
  - en el CMS queda la orden (`ViaGate` false), el pago del motor en proceso capturado por 50.400 y un
    `.eml` en su buzón.
- **(c) El override de negocio gana en los dos sentidos.** Sin orquestador y con «puerta», el SSR dice
  «puerta»; con orquestador y «directa», dice «directa» y la compra va por `/checkout` + `/confirm`.
  `PurchasePath=puerto` no deja arrancar el CMS, y el mensaje explica la regla. Al terminar, el CMS se
  relanzó tal cual («puerta») y se paró.

**Cómo se verificó.** Los tests que suma cada commit:
- CMS: `e82e4a3e`, +14 (vacío, feliz, sin orquestador, el override en los dos sentidos y por sitio, el
  sitio vacío, los valores mal escritos, el cruce con el 503 de la puerta y el flujo que coloca la
  vista); `6b495c9d`, +1.
- UI, en Angular: `5d72686`, +12; `e05f441`, +3; `67b1039`, +1; `7faa386`, +1.

Las cifras finales:
- CMS: build con 0 avisos; **3231 / 924 / 520 = 4675**.
- UI: `npm test` 6 de 6 (**998 + 117 + 2.142 + 11**; 999 con el CDN construido) y `contracts:validate`
  EXIT 0 (`gate:clientes` 130 de 131, `gate:rutas` 109 de 124, `gate:literales` en su línea base de
  2.813).
- Gates cruzados: G-6 ✓ 540 claves en 9 verticales y G-7 ✓ 60 claves en 23 rutas.
- El bundle de `eventos` pesa 436.822 B (426,6 KB), bajo su techo de 440 KB.

**Corrige lo que decía esta ADR**, con su nota fechada en cada sitio:
- la decisión 4 («el front no vuelve a la ruta vieja»);
- la decisión 10 de la F4;
- «Líneas escritas a mano retiradas en el UI»;
- «el merge a master del UI despliega … un eventos que sólo vende por la puerta»;
- y el retiro DESCARTADO, que esta reposición completa: el CMS ya conservaba la ruta, y ahora el UI la
  vuelve a llamar cuando el CMS la declara.

**Lo que queda, dicho**
- **Con «directa» la sesión vence a los 30 min**: sólo la puerta la extiende a 24 h. Y la fase D del
  servidor sólo lista sagas de la puerta, porque filtra por miembro y las órdenes directas guardan otro
  comprador. Una orden directa pendiente pasado ese plazo no la ve nadie.
- **Con «directa», un «no» definitivo de `/confirm`** (404 o 400) no suelta la orden: sólo se puede pulsar
  «Terminar» hasta que venza la sesión. No se suelta porque en `Mode=Bff` un 400 también puede llegar
  con el cobro hecho.
- **Un pago anotado cuyo importe deja de coincidir con el total** hace que el asistente vuelva a llamar a
  `pay`, y la directa abre otra orden. Es lo de antes de la F4 y no se tocó; el arreglo del cupón quita
  la causa conocida.
- **Pregunta de producto**: con «directa», ¿se sigue pidiendo la sesión de miembro, aunque la ruta
  directa del CMS acepta compras anónimas?
- **Un spec de la puerta intermitente bajo carga**, ajeno a este cambio («desde OTRO dispositivo, con una
  compra que se deshizo DESPUÉS de cobrar…», que da `cart` en vez de `confirmed`): está anotado en
  `5d72686` y sin investigar.

## Relación con otras ADRs

- **0138** — el coordinador de página tiene ahora adónde enviar: el flujo. Su piloto puede ser la
  misma página de compra.
- **0135** — el mismo patrón (fuente C# → contrato comiteado → tipo generado → gate en los dos
  repos), llevado de los elementos a las costuras HTTP. `<synergos-flujo>` será un colocable con record
  cuando una página componga dos participantes o haya un segundo flujo; en el piloto lo coloca la vista
  del bloque (F4).
- **0137** — la puerta y los orquestadores leen su configuración del despliegue, no del editor.
- **0116** — el motor de pagos sigue siendo la costura de pagos; esta ADR sólo transporta su
  «requiere acción» hasta el front.
- **0131** — el aviso deja de depender del camino: lo emite un paso.
- **0117** — el aforo sigue siendo contenido del CMS; lo que sale es la transacción.
- **0001** — el CMS se queda en .NET 8 mientras siga el pin de Umbraco 13.
- **0002** — los motores de `Synergos.CMS.Application` no dependen de Umbraco: los que sobrevivan como
  pasos se mudan sin reescribirse.
- **0141** (propuesta, 2026-10-14) — el runtime compartido se publica por contenido: la decisión que
  cerró el defecto 1 de la verificación en vivo del cupón endurecido.

## Referencias

- `_informes/70-estado-del-arte.md` (+ `.datos.json`): las nueve dimensiones con sus cifras, métodos
  y lo que cada verificador refutó.
- `backend/orquestadores/Synergos.Bff.Eventos/Domain/TicketingFlow.cs` (el flujo que se porta) y
  `backend/orquestadores/Synergos.Bff.Core/` (el motor que lo interpretará).
- `Synergos.Arquitectura.Tests/Architecture/BackendSegregationTests.cs` (las fronteras que se abren
  una vez, con regla).
- CLAUDE.md §0.B, principios 11-18.
