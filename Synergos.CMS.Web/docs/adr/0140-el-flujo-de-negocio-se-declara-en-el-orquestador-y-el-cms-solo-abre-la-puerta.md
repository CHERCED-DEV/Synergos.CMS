# ADR 0140 — El flujo de negocio se declara en el orquestador, el contrato se publica, y el CMS sólo abre la puerta

- **Estado:** Propuesto — se acepta o se descarta con el piloto en Eventos ([#201](../../../../../issues/201)), ver al final. **F1 y F2 hechas** (2026-10-07), ver «Avance del piloto» (de la F2, el lado CMS; el generador de tipos del UI la cierra en el repo hermano)
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

## Avance del piloto — F2 hecha, lado CMS (2026-10-07)

**El contrato HTTP de `Bff.Eventos`, `Api.Pricing`, `Api.Inventory` y `Api.Payments` se publica,
sale del código y se vigila.** Cuatro documentos OpenAPI 3.1 en
`docs/contracts/openapi/<Ensamblado>.json`; las 23 operaciones con su 2xx y su esquema (201 donde
crean), `operationId`, `Idempotency-Key` donde se lee, 401 y `Rechazo`. Commits CMS `78d976c0`
(.NET 10), `1da318ac` (runtimes), `2a80940b` (Shared tipado), `6092ba7c` (generación y deriva),
`ade4debc` (el contrato dice la verdad), `acff3786` (peticiones honestas), `3cbc76f3`
(compatibilidad consumidor → capacidad) y el de esta sección. Plan y mediciones previas:
`_informes/73-adr-0140-f2-plan.json`.

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
   `Synergos.Shared` tipa `ToProblem`, `ToHttp`, `ToCreated` y `IdempotencyHeader.TryRead`.
4. **Un esquema `Rechazo`** (ProblemDetails + `code` + `transient`, `title` como enum de
   `RejectionKind`) en las operaciones cuyo tipo de retorno incluye `ProblemHttpResult`; los seis
   estados de `StatusCodeFor`; el 401 sin cuerpo en todas. Los códigos no se enumeran.
5. **`Idempotency-Key` sale de un metadato** (`.ConLlaveDeIdempotencia(siempre)`), y una sonda
   contra el host comprueba que lo declarado es lo exigido.
6. **En una petición, todo anulable lleva `= null`**: el esquema publica la forma del cable; lo
   que el negocio exige lo publican los rechazos con código.
7. **Tres capas de gate, porque la deriva sola no basta**: la deriva (`ContratoOpenApiTests`), el
   suelo que regenerar no arregla (`SueloDelContratoTests`) y las sondas contra el host
   (`SondasDelContratoTests`).
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
- Sondas †: sin la llave declarada en `HoldStock`; declarada en `/void`, que no la lee; `code`
  renombrado en `ToProblem`; un 401 con cuerpo; `Rechazo` sin publicar en los GET.
- Compatibilidad: `forKind→forType` en la capacidad † y en el consumidor, `Available` renombrado
  en la capacidad † y en el consumidor, de int a string, la ruta `holds→hold`, `currency→moneda`,
  `CaptureAsync` sin llave, `amount` sin `currency`, un método sin recorrido. **El oráculo de la F1
  queda 44/44 en verde en ocho de los diez**: es el hueco que este gate cierra.

**Lo que queda, dicho**
- **Lado UI de la F2**: el generador de tipos con `--check` (sólo `Synergos.Bff.*`) y su paso de
  CI van en el repo hermano, después de empujar esta rama. Hasta la F4 nadie importa esos tipos (la
  regla 24 del UI queda abierta con fecha).
- **F3, la puerta**: decide las claves de operación; tiene que **separar** lo que pone la puerta
  (`buyerKind`, `buyerId`, `serviceFeePercent`) de lo que manda el navegador —el documento de
  `Bff.Eventos` los publica hoy como entrada— y no exponer `retry` ni `compensations`.
- **F4**: el cliente en `vitals/core` que importa el tipo generado, con el mutante de renombre
  que rompe `tsc`, y el typecheck de `vitals/core` en `npm test`.
- **Fuera del piloto**: las otras 20 capacidades y 3 orquestadores; el catálogo de códigos por
  pieza (cuando el UI los traduzca, ADR 0136); la obligatoriedad de negocio en el esquema; `status`
  como enum.
- **Riesgos abiertos**: un parche del runtime 10 en CI puede mover el documento sin tocar código (el
  rojo imprime la versión; se fija el parche si pasa); Linux sin medir; los gates de
  `Arquitectura.Tests` ejecutan código del CMS sobre el runtime 10 (al CMS en su runtime lo prueba
  `CMS.Tests`); los 400/415 del framework no traen `Rechazo`.

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
