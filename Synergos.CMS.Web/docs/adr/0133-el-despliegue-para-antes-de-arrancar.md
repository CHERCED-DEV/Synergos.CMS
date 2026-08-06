# ADR 0133 — El despliegue para antes de arrancar, y el humo mira desde fuera

- **Estado:** Aceptado
- **Fecha:** 2026-08-04
- **Parte de:** [HU #19](../../../../issues/19) · épica [#16](../../../../issues/16)
- **Depende de:** ADR 0128 (arranque desatendido), HU #17 (imágenes por SHA), HU #18 (el compose)

## Contexto

Un `git push` a `master` compilaba, corría 2063 tests y siete gates… y ahí se acababa. Lo
verificado no llegaba a ningún sitio donde alguien pudiera verlo.

La HU dejaba una sola cosa abierta —**dónde corre**— y se resolvió: un VPS con `docker compose`.
Las plataformas gestionadas cumplen los cuatro requisitos *si* se les fija el mínimo y el máximo
en 1, y ahí el riesgo es que alguien lo suba sin saber lo que rompe. Un servidor propio no tiene
ese botón.

## Decisión

Cuatro decisiones, y las cuatro existen porque su alternativa **deja el despliegue en verde**.

### 1. Parada antes de arranque. La caída es a propósito

`docker compose down` y después `up`. No es pereza ni falta de sofisticación:

> 19 de las 20 capacidades guardan en fichero JSON con un `lock` de **proceso**
> (`JsonCollectionStore`). Dos instancias de la misma capacidad se pisan y **no dan error**:
> corrompen. Un despliegue «sin caída» —rolling, blue/green, canary— es, por definición, dos
> instancias a la vez.

O sea: **el despliegue que cualquier plataforma moderna hace por defecto rompería esto.** La
decisión no es «aceptamos una caída porque no sabemos hacerlo mejor», es «la caída es la única
forma correcta mientras el almacén sea este».

Lo que sí se optimiza es el tramo largo: las imágenes se **bajan antes** de parar nada. La caída
dura lo que tarda el arranque, no lo que tarde la red — que además es el tramo que puede fallar.

### 2. El humo corre en el runner, contra la URL pública

**Es la decisión con más consecuencias y la que más fácil se hace mal.**

> Un humo contra `localhost` **pasa siempre**. Contra el propio runner no hay nada que pueda
> fallar: ni DNS, ni certificado, ni proxy, ni el servidor. El action se pone verde con el sitio
> caído, y el único síntoma es que nunca falla.

Por eso vive en `tools/humo-publico.sh`, en su **propio fichero**: así `DeployPipelineTests` puede
leerlo y exigir que no aparezcan `localhost` ni `127.0.0.1`. Metido dentro del YAML del workflow,
ese gate tendría que leer YAML y distinguir el humo del resto de pasos — o sea, no existiría.

Comprueba tres cosas, y ninguna es «responde 200»:

| | Por qué esa y no otra |
|---|---|
| La **portada** devuelve 200 y es HTML | `/health` puede contestar con el sitio inservible: no toca contenido, ni plantillas, ni base |
| La **versión** que contesta es el commit subido | ver §3 |
| El **árbol de servicios** no contesta desde internet | `ComposeStackTests` vigila el fichero; esto vigila la realidad |

### 3. `/_health` publica el SHA de la imagen

`Dockerfile` → `ARG VERSION` → `SYNERGOS_BUILD_SHA` → `HealthController`.

> **Es lo que separa «el sitio responde» de «el sitio responde con lo que acabo de subir».** Un
> reinicio que falla en silencio deja viva la versión anterior: el sitio responde, el humo pasa, y
> el despliegue se da por bueno **habiendo desplegado nada**.

El `ARG` va en la etapa de *runtime* y no en la de *build*: cambiar el SHA no puede invalidar el
caché de compilación, o cada commit reconstruiría las 23 imágenes enteras.

Y el campo va **fuera** de `checks`: una versión no es una condición de salud. Entre las probes,
podría poner el endpoint en 503. Fuera, el humo la lee **aunque `/_health` esté en 503** — que es
el caso por defecto mientras el registry del CDN no esté configurado.

### 4. Si el humo falla: volver **y** ponerse rojo. Las dos

- Volver sin ponerse rojo → un despliegue «exitoso» que no desplegó nada.
- Ponerse rojo sin volver → el sitio caído.

Es lo que hace que el etiquetado por SHA de la HU #17 valga para algo: con `latest` no hay imagen
anterior que nombrar. El servidor guarda la etiqueta viva en `/opt/synergos/tag.actual`, y el
workflow la lee **antes** de tocar nada.

## Y una decisión que la HU pedía y no era del despliegue

`SharedKeyAuth` degradaba a **abierto** con un `LogWarning` cuando faltaba la llave. La razón era
buena y sigue siéndolo: exigirla en un `dotnet run` recién clonado empuja a poner la llave en el
repo, que es peor que no tenerla.

> Pero en un despliegue alcanzable son veintidós capacidades abiertas y un renglón de log que
> nadie va a leer, **porque el sitio funciona**. Un agujero que no se nota es el que se queda.

Ahora el degradado sigue existiendo, atado al único entorno donde su razón es cierta:
`IsDevelopment()` abre con el aviso; **cualquier otro entorno no arranca**. Y falla cerrado
también cuando no hay `IHostEnvironment` que preguntar: lo desconocido no puede resolverse a favor
de abrir.

Verificado con procesos reales, no sólo con tests:

```
sin llave, Production   → InvalidOperationException, el proceso muere
con llave               → /health 200 · /v1/… sin cabecera 401 · con la llave 200 · con otra 401
sin llave, Development  → arranca, /v1/… 200, y lo grita en el log
```

## Consecuencias

**A favor**

- Un push a `master` llega al servidor sin que nadie toque un servidor.
- 14 gates nuevos (`DeployPipelineTests`), los 14 mutados y confirmados en rojo.
- El servidor no necesita el repo: el compose, el Caddyfile y el script viajan en cada despliegue.
  Lo único permanente allá es `.env` —con los secretos, que nunca salen de ahí— y los volúmenes.
- Mientras no existan los secretos, el workflow **se salta solo** en vez de ponerse rojo. Un rojo
  permanente que todos saben ignorar entrena a ignorar los rojos de verdad.

**En contra, y dicho de frente**

- **Hay caída en cada despliegue.** Es el §1, y no se arregla sin cambiar el almacén (épica #2).
- **Una sola máquina.** No hay alta disponibilidad y no la va a haber mientras el almacén sea
  fichero con `lock` de proceso.
- **Sin `DEPLOY_HOST_KEY`, el primer saludo SSH es confianza ciega.** Se acepta la huella que el
  host presente en el momento. Alcanza para arrancar y no es equivalente; queda dicho en el
  workflow para que sea una decisión y no un descuido.
- **No hay staging.** Primero uno que funcione.
- **Los datos de las capacidades no tienen copia.** Merece ticket propio el día que haya algo que
  perder.

## Lo que se descartó

| | Por qué no |
|---|---|
| Rolling / blue-green | Dos instancias a la vez = corrupción silenciosa del almacén |
| Humo desde el servidor | Prueba el último salto; lo que falla en un despliegue es todo lo demás |
| El registro del SHA en `checks` | Una versión no es una condición de salud, y desaparecería en 503 |
| Clonar el repo en el servidor | Dos fuentes de verdad sobre qué está corriendo |
| `latest` en las imágenes | Sin nombre para la versión anterior no hay vuelta atrás |
