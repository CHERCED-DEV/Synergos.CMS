# ADR 0140 — El flujo de negocio se declara en el orquestador, el contrato se publica, y el CMS sólo abre la puerta

- **Estado:** Propuesto — se acepta o se descarta con el piloto en Eventos ([#201](../../../../../issues/201)), ver al final. **F1 y F2 hechas** (2026-10-07), la F2 en los dos repos y endurecida tras su verificación adversarial; **F3 hecha** (2026-10-08): la puerta genérica abre `eventos.compra`, sin el token de identidad (corrección de la decisión 4); ver «Avance del piloto». Faltan la verificación en vivo de la F3, el front (F4) y el retiro de la ruta vieja, que va después
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

## Relación con otras ADRs

- **0138** — el coordinador de página tiene ahora adónde enviar: el flujo. Su piloto puede ser la
  misma página de compra.
- **0135** — el mismo patrón (fuente C# → contrato comiteado → tipo generado → gate en los dos
  repos), llevado de los elementos a las costuras HTTP. `<synergos-flujo>` es un colocable con record.
- **0137** — la puerta y los orquestadores leen su configuración del despliegue, no del editor.
- **0116** — el motor de pagos sigue siendo la costura de pagos; esta ADR sólo transporta su
  «requiere acción» hasta el front.
- **0131** — el aviso deja de depender del camino: lo emite un paso.
- **0117** — el aforo sigue siendo contenido del CMS; lo que sale es la transacción.
- **0001** — el CMS se queda en .NET 8 mientras siga el pin de Umbraco 13.
- **0002** — los motores de `Synergos.CMS.Application` no dependen de Umbraco: los que sobrevivan como
  pasos se mudan sin reescribirse.

## Referencias

- `_informes/70-estado-del-arte.md` (+ `.datos.json`): las nueve dimensiones con sus cifras, métodos
  y lo que cada verificador refutó.
- `backend/orquestadores/Synergos.Bff.Eventos/Domain/TicketingFlow.cs` (el flujo que se porta) y
  `backend/orquestadores/Synergos.Bff.Core/` (el motor que lo interpretará).
- `Synergos.Arquitectura.Tests/Architecture/BackendSegregationTests.cs` (las fronteras que se abren
  una vez, con regla).
- CLAUDE.md §0.B, principios 11-18.
