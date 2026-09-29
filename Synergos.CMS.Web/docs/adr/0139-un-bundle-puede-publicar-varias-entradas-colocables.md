# ADR 0139 — Un bundle puede publicar varias entradas colocables que comparten su store (aclara la 0113)

- **Estado:** Propuesto — se acepta o se descarta con el piloto (ver al final)
- **Fecha:** 2026-09-29
- **Propone:** la síntesis de la auditoría de reutilización (informe 20 §5.B.3 y §4.2 punto 6:
  «frente a ADR 0113 → pide ADR»). El arquitecto fijó número y estado el 2026-09-29.
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Aclararía, sin romperla:** [ADR 0113](0113-published-elements-are-apps-shared-components-live-in-a-lib.md)
- **Toca:** el registry (ADR 0012 / 0132) y el pipeline de publicación (ADR 0099)

## Contexto

> Marcas de certeza (informe 20): ✔ comprobado en el disco · ◐ medido por un agente con dos
> derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente sin verificar. «Re-leído» = línea
> abierta o fichero parseado al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### El patrón que se quiere: una funcionalidad y, además, sus piezas sueltas

En NewShore **un módulo funcional publica varias entradas colocables**: `HeaderFlow` publica
`HeaderFlowContainer`, `BookingStatusInformation` y `TitleHeading` ✔ (informe 20 §3.6). Las tres
comparten el módulo —servicios y estado— y el editor las coloca por separado; la vista de
`TitleHeading` monta la entrada que vive dentro de `HeaderFlow` (informe 15 §3.2). Lo hacen 9 de 106
módulos ○ (97 con una entrada, 6 con dos, 3 con tres).

Es exactamente «la funcionalidad trae todo armado por dentro, **y además** expone piezas más chicas
que el editor puede colocar sueltas» (ADR 0134: la pieza se coloca cuando le sirve al editor).

### Lo que Synergos permite hoy

- **Un bundle = una app = un custom element.** El `main.ts` de cada elemento hace su
  `customElements.define` (ADR 0113 §1).
- **Lo compartido entre elementos va a una lib** y se compila **dentro de cada bundle** (ADR 0113).
  La 0113 lo dejó escrito para Tienda: *`cart.store` es un singleton de módulo **por bundle**, no un
  store compartido entre bundles*. Dos elementos que importan la misma lib tienen cada uno su
  instancia.
- **La composición entre bundles** existe por `dependencies` del registry: la declaran 3 entradas
  (`booking-wizard` → `pax-selector`, `eventos` → `countdown-clock`, `travel-shell` → `pax-selector`),
  parseado de `vitals/contracts/src/element-registry.json` al escribir esta ADR. Es la vía de las
  piezas embebidas (ADR 0126 / 0127): cada una con su bundle, sin estado compartido.
- **Varios nombres para un tag** ya existe, y no es esto: `synergos-text-block` responde a 7 nombres
  (`heading`, `paragraph`, `rich-text`…), parseado del mismo fichero. Es un alias, un solo elemento.

Con eso, una pieza suelta que tiene que mostrar el estado de su funcionalidad (el total, el paso en
curso) no tiene dónde leerlo: la lib le da el código, no la instancia.

## Decisión (propuesta)

### 1. Un bundle puede definir varios custom elements

Una funcionalidad y N piezas colocables, en el **mismo** bundle, cada una con su `name` y su `tag` en
el registry y, si el editor la coloca, su ElementType. Comparten el `main.js`.

### 2. Comparten estado por un store de módulo interno al bundle

Un store de módulo **dentro** del bundle: la misma semántica que la 0113 ya documentó para
`cart.store` (singleton de módulo por bundle), ahora **a propósito y a la vista**. El store no se
expone en `window` ni es un bus entre bundles: la premisa de `docs/contracts/dom-events.md` («no hay
shared store») sigue en pie.

### 3. Relación con la 0113: aclaración, no ruptura

| qué se comparte | dónde vive | quién lo dice |
|---|---|---|
| **código** entre apps distintas | una lib (`libs/shop`, `libs/shells`…) | 0113, sin cambios |
| **estado** entre entradas del **mismo** bundle | el store interno del bundle | esta ADR |
| **estado o señales** entre bundles distintos | eventos DOM | ADR 0138 |

Una app sigue sin poder importar otra app. Lo que cambia es que una app puede **definir** más de un
elemento.

### 4. El registry sabe que dos nombres comparten bundle

Cómo se escribe (qué campo, qué forma del manifiesto) lo decide el piloto, junto con el publicador y
los dos clientes del registry. El mapeo nombre → framework → slot → manifiesto → URL de los clientes
de filesystem y HTTP tiene que resolver las dos entradas a la misma URL versionada.

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Un bundle por elemento y el estado en una lib** (lo de hoy) | La lib se compila dentro de cada bundle: cada uno tiene su instancia (0113). Compartir estado obligaría a sincronizar instancias por eventos, que es usar la coordinación (0138) como almacén. |
| **Copiar NewShore tal cual** | Sus entradas múltiples son `provide: 'components'` de un `NgModule`, arrancadas por `data-module-path` sobre un `AppModule` raíz compartido: el estado se comparte porque **todo** comparte inyector, y eso ata todos los widgets a un framework y una versión (informe 15 §10.c). Aquí se comparte sólo dentro de un bundle. |
| **Un store global en `window`** | Rompe la premisa de `dom-events.md` y acopla las versiones de bundles que se publican por separado. |
| **Meter las piezas dentro de la funcionalidad**, sin colocarlas sueltas | Le quita al editor la pieza donde le sirve, que es la mitad de lo que decidió la ADR 0134. |

## Consecuencias

**A favor**

- Una funcionalidad puede ofrecer sus piezas al editor sin duplicar estado ni código.
- La excepción de la 0113 (`cart.store` singleton por bundle) pasa de nota de implementación a regla
  con nombre.

**En contra**

- **Colocar sólo la pieza carga el bundle entero** de la funcionalidad. El piloto tiene que medir
  cuánto pesa.
- **Las entradas de un bundle se versionan juntas**: no se puede publicar una pieza sin republicar su
  funcionalidad.
- **Cambia la forma del registry**, que el CMS consume (ADR 0012): es contrato, y los dos clientes
  (filesystem y HTTP) y el publicador se mueven a la vez.
- **Puede duplicar la emisión de scripts**: si dos entradas del mismo bundle están en la página, el
  emitter pediría el mismo `main.js` dos veces. El piloto tiene que comprobar que se evalúa una vez
  (es lo que promete el mapa de módulos del navegador para una misma URL) y que no se duplican
  etiquetas.

**Qué la vigilaría:** la regla de módulos de Nx (`@nx/enforce-module-boundaries`) sigue prohibiendo
`app → app`; hace falta un test del registry que compruebe que las entradas que declaran compartir
bundle resuelven a la misma URL y SRI.

## Qué hace falta para aceptarla (el piloto)

Una funcionalidad con una o dos piezas colocables en su bundle (ola 5 del plan de la auditoría). Se
mide:

1. **Estado compartido**: la pieza refleja el cambio que hace la funcionalidad, en la misma página.
2. **Un solo `main.js` evaluado** con las dos entradas presentes; SRI intacto.
3. **Registry**: los clientes de filesystem y HTTP resuelven las dos entradas; `hot-reload` sigue
   funcionando.
4. **Peso** de una página que sólo coloca la pieza, contra su bundle propio de hoy.
5. El lint de Nx sigue en rojo ante un `app → app` (mutación).

Si se acepta, la 0113 recibe en su cabecera la marca «aclarada por la 0139», como hizo la 0126 con la
0127. Mientras sea propuesta, la 0113 no se toca.

## Relación con otras ADRs

- **0113** — la aclara: lo compartido **entre apps** sigue en una lib; lo compartido **entre
  entradas del mismo bundle**, en su store.
- **0012 / 0132 / 0099** — el registry y el pipeline aprenden que un bundle puede tener varios
  nombres.
- **0126 / 0127** — la otra vía, piezas embebidas con su propio bundle, sigue valiendo cuando no hay
  estado que compartir.
- **0096** — un module-mount grande puede exponer así piezas suyas.
- **0134** — por qué hay piezas colocables. **0138** — cómo hablan los bundles entre sí.

## Referencias

- Informes locales de la auditoría: 20 §3.6, §4.2 y §5.B.3; 15 §3.2, §7.5 y §10.b.5.
