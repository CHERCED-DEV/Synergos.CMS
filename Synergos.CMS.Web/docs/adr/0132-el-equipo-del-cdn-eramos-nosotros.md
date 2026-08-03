# ADR 0132 — El equipo del CDN éramos nosotros

- **Estado:** Aceptado
- **Fecha:** 2026-08-03
- **Desbloquea:** ADR 0012 (el contrato del CDN se CONSUME, no se posee) y
  `docs/umbraco/cdn-contract.md`
- **Parte de:** [HU #20](../../../../issues/20) · épica [#16](../../../../issues/16)

## Contexto

`docs/umbraco/cdn-contract.md` decía, desde hacía meses:

> *«**Status:** BLOCKED externally — waiting for the CDN team to publish the registry
> contract.»*

Y listaba cinco puntos que «el equipo del CDN» tenía que congelar antes de escribir el adapter
real. Mientras tanto `StubBundleRegistryClient` devolvía `null` siempre y los 71 `elementSyn*`
emitían un comentario HTML de relleno.

> ### El equipo del CDN somos nosotros, y el bloqueo dejó de ser externo mucho antes de que alguien moviera la etiqueta.

Al ir a construirlo aparecieron dos cosas que nadie había juntado:

| | |
|---|---|
| **El publicador ya existía** | `Synergos.UI` tiene `publish.mjs`, `release:cdn`, `publish-runtime` — elementos por framework, tres slots de versión, `registry.json` global |
| **El consumidor también** | `FileSystemBundleRegistryClient` lee exactamente eso, del disco, con hot-reload y SRI |

**Publicaba a una carpeta local de la máquina del arquitecto** (`C:\LOCAL_CDN`). Funcionaba — y
por eso nadie notó que lo único que faltaba era **que esa carpeta fuera alcanzable por HTTP**.

El contrato «ajeno» que había que esperar era una propuesta que este mismo repo ya había escrito
(la de la Ola 171, en el mismo fichero que declaraba el bloqueo).

## Decisión

**`HttpBundleRegistryClient`: el gemelo remoto del cliente de filesystem.** Mismo mapeo
—registry → framework → slot → manifiesto → URL—, distinto transporte. Se activa con
`Synergos:BundleRegistry:Mode=Http`.

Que el mapeo salga tal cual del cliente que ya funcionaba no es pereza: **si las dos formas de
leer el mismo registry divergieran, una de las dos estaría leyendo mal**, y el fallo aparecería
solo al cambiar de modo.

### Las tres decisiones que obliga a tomar el transporte de red

**1. Nunca se busca en la red durante un render.** Se sirve del último snapshot bueno y se
refresca cuando vence (60 s por defecto, lo mismo que la caché del `registry.json` publicado).

> Ir a la red por elemento y por página metería una ida y vuelta en el camino crítico de cada
> visita, y convertiría un CDN lento en un **sitio** lento. Veinte bloques en una página leen el
> registry **una** vez.

**2. Si el CDN se cae, se sigue sirviendo el último snapshot bueno.**

> Un registry caído no puede vaciar una página que se venía pintando bien. El sitio dejaría de
> mostrar sus componentes de golpe, y no por un cambio nuestro sino porque un tercero dejó de
> contestar.

Lo mismo vale para un registry que llega **vacío**: es indistinguible de uno roto desde el punto
de vista del sitio —todos los elementos dejarían de resolver a la vez— así que se rechaza el
reemplazo y se conserva el anterior.

**3. Los manifiestos se cachean para siempre, por su URL versionada.** Vale **solo** porque esa
ruta es inmutable por contrato. Se emite siempre la ruta con versión exacta y nunca el puntero
móvil: con `/latest/` el navegador tendría que revalidar en cada navegación, un viaje por bundle
y por página.

### Lo que NO hace, y por qué

**No calcula SRI.** El manifiesto que publica hoy `publish.mjs` no trae `integrity`, y
calcularlo acá exigiría descargarse cada bundle entero para hacerle el hash **en el render**.

> **El sitio correcto para arreglarlo es el publicador, no el consumidor** — y ya sabe hacerlo:
> calcula SRI para el import-map del runtime. Es una línea en `publish.mjs`, no un rediseño acá.

Mientras tanto el descriptor sale con `Integrity = null`, que es un valor previsto y documentado
en `BundleDescriptor`.

## Consecuencias

**Se puede quitar de la lista de bloqueos externos** (`CLAUDE.md` §9) el `HttpBundleRegistryClient`.
Los 9 DocTypes de *Experience CDN* + `compBehaviorTracking` + `compBehaviorInteraction`
heredaban ese bloqueo: hay que revisarlos, porque probablemente sean **trabajo y no espera**.

**Se registró `TimeProvider` en el contenedor del CMS.** No estaba, y el cliente lo pide para
decidir si su snapshot venció. Sin registrarlo el fallo habría sido **en el arranque y no en la
compilación** — la clase de error que no se ve hasta que alguien cambia el modo en producción.

**Sigue habiendo tres modos y el default no cambia.** `Stub` es el default; un despliegue sin
CDN se comporta exactamente igual que antes de esta ADR.

## Cómo se verificó

**11 tests contra un CDN de mentira que se puede tirar y levantar a mitad de test.** Lo que
prueban no es «resuelve un bundle» —eso lo comparte con el cliente de filesystem— sino lo que el
transporte obliga: que un CDN caído no vacíe la página, que un render no espere a la red, y que
la URL emitida sea la versionada.

**Cinco mutaciones, cuatro en rojo.** La quinta sobrevivió, y vale la pena decir por qué:

> Quitar el atajo rápido de la caché no rompe ningún test — **y es correcto que no lo rompa**. La
> corrección la garantiza la comprobación de dentro del cerrojo; el atajo solo evita que veinte
> bloques de una página se serialicen contra un semáforo para no hacer nada. Es un mutante
> equivalente, no un test flojo, y quedó anotado en el código para que nadie borre una de las dos
> creyendo que sobra.

## Lo que falta para verlo funcionando

Que `Synergos.UI` esté publicado (misma HU) y dos valores de configuración:

```
Synergos:BundleRegistry:Mode          Http
Synergos:BundleRegistry:PublicBaseUrl https://<el-cdn-publicado>
```
