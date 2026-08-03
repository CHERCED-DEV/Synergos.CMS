---
name: synergos-ticket-first
description: El proceso de trabajo de Synergos — nada se codifica sin ticket. Cubre los cuatro tipos (Defecto, Evolutivo, Mejora, Hallazgo), las cuatro preguntas del refinamiento, el umbral de qué bloquea y qué no, la regla anti-descarrilamiento (lo que encontrás haciendo otra cosa se anota como comentario del ticket abierto y se sigue; issue aparte solo si es trabajo separado de verdad), y las dos escrituras que hacen que el proyecto aprenda. Aplica a los TRES árboles — CMS, capacidades/orquestadores y Synergos.UI. Invocar al encontrar un bug o una mejora, antes de abrir un PR, o al empezar cualquier trabajo que no tenga ticket.
---

# SYNERGOS Ticket-First — el ticket va antes que el código

**Nada se codifica sin ticket.** Se abre, se discute, y recién ahí se escribe.

No es burocracia: es la misma disciplina que produjo los gates de arquitectura. Un proceso
escrito como prosa se olvida; uno que rompe el build se cumple. Por eso hay un gate
(`.github/workflows/ticket-first.yml`) que rechaza un PR sin issue referenciado.

---

## 0. El umbral — leer esto primero

Exigir ticket para *todo* es lo que hace que la gente abra issues basura para saltar el gate.

| | |
|---|---|
| **Bloquea** | cambia comportamiento · cambia un contrato · cambia schema · es un defecto |
| **No bloquea** | typo, comentario, formato, documentación → etiqueta `sin-ticket` en el PR |

**Capturá siempre. Bloqueá solo cuando importa.**

---

## 1. La regla que hace que esto no estorbe

> ### Lo que encontrás haciendo otra cosa se **ANOTA** y se sigue. Por defecto en un **comentario del ticket que ya está abierto** — no en uno nuevo.

Un ticket nuevo es una espera nueva: alguien lo tiene que leer, refinar y aprobar. Eso vale la
pena cuando hay trabajo de verdad separado, y es puro peaje cuando no.

**El umbral, y ante la duda es comentario:**

| | |
|---|---|
| **Comentario en el ticket abierto** | una dificultad · una decisión tomada sobre la marcha · algo que no cumpliste y por qué · una duda que resolviste solo |
| **Issue aparte** | otro puede tomarlo sin tocar lo tuyo · vive en otra área del código · se decidió NO hacerlo ahora y hay que poder encontrarlo dentro de seis meses |

> ### Y el trabajo se termina igual.
>
> Encontrar algo **no autoriza a entregar a medias**. Se sube el PR completo con lo hallado
> anotado. Si de verdad hace falta un issue, se abre — pero **después de subir, no en vez de**.

Sin esta regla pasan las tres cosas malas: el hallazgo descarrila el trabajo, se pierde, o —la
más difícil de ver— **se convierte en un ticket que nadie pidió y que ahora hay que refinar**.

Los enlaces van en la última sección del PR: *«lo que encontré y NO arreglé acá»*.

---

## 2. Los cuatro tipos, y qué obliga cada uno

### 🐛 Defecto

Lo importante **no** es «qué falla» — eso se ve. Es:

- **Por qué los tests no lo vieron.** ¿No había? ¿Probaba otra cosa? ¿Pasaba **por accidente**?
- **Qué mutación lo reproduce.** El cambio de una línea que lo reintroduce. Si no se puede
  escribir, el arreglo no está entendido.

> Los defectos más caros de este repo los encontró un proceso vivo, no un test — porque los tests
> codificaban la misma suposición equivocada que el código. Esa casilla existe por eso.

### ✨ Evolutivo — las cuatro preguntas del refinamiento

1. **¿Qué problema del negocio resuelve?** El problema, no la feature. *Si no se puede escribir
   sin decir un nombre de clase, todavía no lo entendimos.*
2. **¿Dónde vive, y por qué ahí?** Con el filtro de atomicidad del doc 07 aplicado **por
   escrito**: ¿puede decir NO sola? ¿es dueña de su almacén? **Lo que no tiene almacén es un
   tipo, no un servicio.**
3. **¿Qué rechaza, y con qué código?** En este repo **las reglas de rechazo son el diseño**.
   Y marcá cuáles son transitorios: `Rejection.IsTransient` decide si algo se reintenta o se
   grita una vez.
4. **¿Cómo sabemos que quedó bien?** Criterios de aceptación **y la mutación que pone cada uno
   en rojo**.

Más una que decide dónde va: **¿cuántos de los nueve dominios lo consumen?** 5+ ⇒ se construye
una vez, en la épica de Plataforma. 1 ⇒ casi siempre es feature de su BFF.

### 🔧 Mejora

La pregunta que importa es **¿por qué ahora y no después?** Mata a la mayoría, y está bien que
las mate. «Porque me molesta» deja el ticket abierto y sin prioridad — un destino digno.

### 🔍 Hallazgo

**Cómo lo verifiqué** es obligatorio: fichero y línea, salida de comando, o URL consultada. Un
hallazgo sin verificación es una sospecha — se puede abrir igual, pero hay que decir que lo es.

Y **cuándo hay que decidirlo**: antes de escribir código relacionado · antes de que existan datos
en producción · cuando toque esa área · solo queda anotado.

---

## 3. Qué hace que el PROYECTO aprenda

Dos escrituras obligatorias, **en el mismo commit** que las enseñó:

1. **Regla nueva aprendida → `CLAUDE.md` §5.** El índice de memorias es lo único que sobrevive a
   que se cierre una sesión. Una sesión nueva arranca fría: lo que no esté ahí, no existe.
2. **Algo del `CLAUDE.md` quedó obsoleto → se corrige acá mismo.** Un `CLAUDE.md` que miente es
   peor que uno corto: el siguiente agente propone lo que ya existe o da por hecho lo que no.

> Evidencia de que hace falta: `CLAUDE.md` llegó a tener **cero menciones** al árbol de servicios
> —20 capacidades, `Bff.Core`, dos orquestadores— y a declarar 976 tests cuando había 1978.

---

## 4. El flujo, de punta a punta

```
hallazgo/necesidad → ticket (tipo correcto) → refinamiento en comentarios
                          ↓
              ¿preguntas abiertas? → se cierran ANTES de codificar
                          ↓
         rama + PR con "Cierra #N" → gate ticket-first en verde
                          ↓
   definición de hecho: gates · tests + MUTACIÓN de cada uno ·
   procesos reales si cruza servicios · CLAUDE.md al día
```

**Una HU con preguntas abiertas queda `en-refinamiento` y no se codifica.** Ver #12 y #13 como
ejemplo: en las dos el refinamiento cambió el alcance *antes* de escribir una línea — una resultó
ser dos HU, y la otra destapó que `Message.ReadBy` no guarda el instante del acceso.

**Pero el ticket no congela el alcance.** Si al codificar aparece que un criterio no se puede
cumplir donde decía —le pasó a #13 con `Api.Audit`— eso se escribe en el ticket, se entrega lo
demás completo, y recién ahí se decide si hace falta un issue. Lo que **no** se hace es parar y
esperar: el ticket garantiza que la conversación pasó antes que el código, no que cada cosa que
aparezca después tenga su propia cola.

---

## 5. Los tres árboles

El proceso es el mismo en **Synergos.CMS** (Umbraco), en el **árbol de servicios**
(`Synergos.Api.*` / `Synergos.Bff.*`) y en **Synergos.UI** (Angular/NX). Lo que cambia por repo
es la definición de hecho:

| Árbol | Además de tests y mutación |
|---|---|
| CMS | `node tools/usync-audit.mjs` si toca schema · verificación en navegador |
| Servicios | gates de arquitectura · **procesos reales** si cruza servicios |
| UI | contratos ↔ registry · 7 temas por siteRoot · sin overflow a 375px |

Cada repo tiene su propio `.github/ISSUE_TEMPLATE/` y su gate: las plantillas de GitHub no se
comparten entre repositorios.
