# ADR 0138 — Los widgets de una página se coordinan por eventos DOM, sin framework y sin singleton

- **Estado:** Propuesto — se acepta o se descarta con el piloto (ver al final)
- **Fecha:** 2026-09-29
- **Propone:** la síntesis de la auditoría de reutilización (informe 20 §5.D.1, ola 5 del plan).
  El arquitecto fijó número y estado el 2026-09-29.
- **Parte de:** [#172](../../../../../issues/172) · épica [#139](../../../../../issues/139)
- **Extendería:** `docs/contracts/dom-events.md` (contrato v1, ADR 0083)
- **Relacionada:** ADR 0134 (qué se coloca), ADR 0139 (bundles con varias entradas)

## Contexto

> Marcas de certeza (informe 20): ✔ comprobado en el disco · ◐ medido por un agente con dos
> derivaciones o ejecutando, sin re-derivar · ○ cifra de un agente sin verificar. «Re-leído» = línea
> abierta al escribir esta ADR (CMS `9ec00d81`, UI `98b9233`).

### El problema existe cuando el editor compone varias funcionalidades en una página

En NewShore el editor coloca en la misma página el formulario de pasajeros, el de contacto, el de
facturación y un botón «Continuar» que es **otra macro**. El botón no conoce a los formularios y los
formularios no conocen al botón (informe 15 §8.3). Algo tiene que:

1. **registrar** los widgets que el editor puso;
2. mostrar **un solo** indicador de carga mientras cualquiera carga;
3. **deduplicar** la misma llamada pedida por varios widgets;
4. **enviar en orden** (pasajeros → contacto → emergencia → facturación);
5. **navegar** cuando todos quedaron listos.

En NewShore lo hace `OrchestratorService` ◐ (informe 20 §3.7; informe 15 §8.1 con sus líneas), y se
suscriben a él 146 ficheros ○ (informe 15 §6.4). **Synergos no tiene nada equivalente** (informe 20
§3: «le falta … la coordinación de página»). No se midió si hoy hay una página de Synergos que lo
necesite: hasta ahora cada vertical es una sola app (informe 16 §7.1) y se coordina por dentro.

### Cómo lo resuelve NewShore, y lo que no hay que copiar

- Es un **servicio de Angular** en DI, sobre un `AppModule` raíz compartido por todos los widgets:
  un inyector, un store NgRx, un bus (informe 15 §8.6). Coordina sólo a quien es Angular y de esa
  versión.
- El orden (`componentOrder`) vive en la tabla de despacho: sólo 9 de 116 entradas tienen orden, hay
  empates y el comparador no es estable; y **cambia por cliente** (informe 15 §8.2).
- El orden de los formularios guiados lo calcula el **servidor** con `OrderResolverService`, un
  **Singleton con una lista que nunca se limpia**: el orden es «el primero desde que arrancó el
  proceso», compartido entre páginas, usuarios y clientes (informe 15 §8.5, D4).
- Si un envío falla, sigue con el siguiente (informe 15 §8.1).

### Lo que Synergos ya tiene

- Un contrato de eventos: `docs/contracts/dom-events.md` v1 (ADR 0083). Convención
  `syn:{component}:{event}`, `bubbles` + `composed`, un evento de ciclo de vida `syn:component:ready`,
  y una premisa: *«No hay shared store, no hay window.\* mutations mutables»*.
- **El disco ya no sigue una sola convención.** Buscado al escribir esta ADR (un grep, una sola
  derivación: es una hipótesis a confirmar en el piloto): nadie emite `syn:component:ready`, y los
  eventos que sí se emiten usan otro prefijo —`synergos:form-stepper:complete`
  (`form-stepper.ts:300`) y `synergos:toast` (`toast-center.ts:336`)—.
- Un arnés de tests de contrato en `docs/contracts/tests/` (ADR 0085-0086).

## Decisión (propuesta)

### 1. Un protocolo de eventos DOM, no un servicio

La coordinación es un **contrato de eventos** que cumple igual un elemento Angular, Preact o de
cualquier otro framework. Los cinco eventos que propone la síntesis:

| evento | quién lo emite | para qué |
|---|---|---|
| `synergos:register` | cada widget que participa | «existo, envío o no envío, y en qué grupo» |
| `synergos:ready` | cada widget | «terminé de cargar» / «mi parte está lista» |
| `synergos:submit-request` | el disparador (un botón colocable) | pedir el envío coordinado |
| `synergos:submit-result` | cada participante | resultado de su envío, con el `outcome` tri-estado de `dom-events.md` |
| `synergos:navigate` | el coordinador | todos listos: navegar |

**Los nombres son los de la síntesis y no están cerrados.** `dom-events.md` pide `syn:` y el disco
usa `synergos:`; el piloto elige una convención y el contrato sube de versión con ella.

### 2. El contrato vive en `docs/contracts/`

Como una sección nueva de `dom-events.md` o un documento hermano, con su test en el arnés de
contratos. Es un contrato CMS ↔ UI más (ADR 0083): se especifica, no se comparte código.

### 3. El orden lo aporta el registry o el editor, nunca un singleton del servidor

El grupo y el orden de envío de cada participante vienen de una declaración (el manifiesto por
elemento, o una decisión del editor como selector). Nunca de estado de proceso en el servidor.

### 4. Lo que queda abierto a propósito

- **Dónde vive el coordinador** (el runtime compartido, el bridge) — sin framework, en todo caso.
- **Cómo se deduplica una llamada** entre widgets de bundles distintos: en NewShore es posible
  porque todos comparten un inyector; con eventos hay que diseñarlo.
- **Qué pasa si un envío falla**: seguir con el siguiente (NewShore) o parar.

## Alternativas consideradas

| alternativa | por qué no |
|---|---|
| **Copiar el `OrchestratorService` de NewShore tal cual** | Es un servicio de Angular sobre un `AppModule` raíz compartido: ata a todos los widgets a un framework y una versión. Un elemento Preact no podría participar. |
| **Ordenar en el servidor** | Es el `OrderResolverService` de NewShore: estado por petición en un singleton, un orden que depende de qué página se pintó primero desde el arranque. |
| **Un objeto con estado en `window.synergos`** (un bus con métodos) | Choca con la premisa de `dom-events.md` (sin store compartido ni mutaciones de `window.*`), y acopla las versiones de los bundles que lo usan. |
| **No coordinar: cada widget envía lo suyo** | Funciona mientras cada vertical sea una sola app. Deja de funcionar el día que el editor componga dos funcionalidades que envían juntas: no hay orden, ni un solo indicador de carga, ni un «todos listos». |

## Consecuencias

**A favor**

- El editor puede componer una página con varias funcionalidades y un disparador sin que se
  conozcan, que es lo que NewShore hace bien.
- Lo cumple cualquier framework: «agnóstico» deja de ser intención en esta superficie.
- El orden es un dato declarado, no un efecto del arranque.

**En contra**

- **Deduplicar es más difícil sin un inyector común.** Es el punto donde el diseño por eventos puede
  quedarse corto.
- **Un protocolo más que mantener**, con versiones, y dos convenciones de nombre que reconciliar
  antes de empezar.
- **Depuración**: un envío coordinado por eventos es más difícil de seguir que una llamada.

**Qué la vigilaría:** el arnés de `docs/contracts/tests/`, con un test del protocolo (forma de
`detail`, orden, «todos listos»). Hoy no existe.

## Qué hace falta para aceptarla (el piloto)

Una página con **dos funcionalidades que envían y un botón colocable** (ola 5 del plan). Se mide:

1. El envío respeta el orden declarado, con un empate a propósito.
2. Un solo indicador de carga; la navegación sale sólo cuando todos respondieron.
3. **Un participante Angular y uno Preact** en la misma página: «agnóstico» se prueba con el segundo
   consumidor (informe 20 §5.B.7), no se afirma.
4. El comportamiento ante un envío que falla, el que se decida, con su test.
5. La convención de nombres elegida y `dom-events.md` en su nueva versión.

## Relación con otras ADRs

- **0083** — el protocolo es un contrato más de `docs/contracts/`; cero código compartido.
- **0085 / 0086** — el arnés de tests de contrato es donde se prueba.
- **0015** — el emitter no cambia: la coordinación ocurre entre elementos ya montados.
- **0134** — el disparador es una pieza colocable; los participantes, funcionalidades.
- **0139** — entre entradas del **mismo** bundle el estado se comparte por su store; entre bundles
  distintos, por este protocolo.

## Referencias

- Informes locales de la auditoría: 20 §3 y §5.D.1, 15 §8, §9 (D4) y §10.b.4.
- `docs/contracts/dom-events.md`.
