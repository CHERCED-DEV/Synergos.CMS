#!/usr/bin/env bash
#
# Lo que corre EN EL SERVIDOR para poner en pie una versión (HU #19).
#
# ─────────────────────────────────────────────────────────────────────────────
# PARADA ANTES DE ARRANQUE. NO ES UNA PREFERENCIA.
#
# 19 de las 20 capacidades guardan en fichero JSON con un `lock` de PROCESO
# (`JsonCollectionStore`). Dos instancias de la misma capacidad se pisan y NO
# dan error: corrompen. Un despliegue "sin caída" —rolling, blue/green, canary—
# es, por definición, dos instancias a la vez.
#
# O sea: el despliegue que cualquier plataforma moderna hace por defecto
# ROMPERÍA ESTO. Se para todo, se arranca todo, y se acepta la caída.
#
# El día que el almacén cambie (épica #2), esta decisión se revisa. Hasta
# entonces, quien la "mejore" corrompe datos sin ver un error.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./deploy-remoto.sh <sha>
#
# Se lo copia el workflow junto con compose.prod.yml y el Caddyfile. El servidor
# NO tiene el repo: lo único que vive ahí de forma permanente es `.env` (con los
# secretos, que nunca salen de ahí) y los volúmenes con los datos.
#
# ── EL LEVANTADO ENTERO, NO SÓLO LOS CONTENEDORES (2026-11-01) ──────────────
#
# El arquitecto lo pidió así para la salida a Hetzner: «que haya un programa y ese
# programa sea el encargado de hacer el levantado de todo». Esto ponía las piezas
# en pie y dejaba dos pasos a mano por SSH: el import de uSync y la siembra de las
# capacidades. Hoy los hace él, en este orden y sólo si la versión se pone en pie:
#
#   1. `importar-schema.sh --contenido-la-primera-vez` — el schema SIEMPRE; el
#      contenido versionado entero sólo sobre una base que no lo tiene (la marca vive
#      en el volumen de la base: ver la cabecera de ese script), y después sólo lo
#      NUEVO, sin tocar lo que ya existe (P10). Nunca al arrancar: lo dispara el
#      despliegue, que es un acto explícito (ADR 0008, 0013).
#   2. `provisionar-en-la-red.sh` y después `--verificar` — idempotente: lo que ya
#      está no se toca, lo declarado que falta se publica.
#
# EXCEPTO al volver a una versión que ya estuvo en pie (la vuelta atrás de
# deploy.yml, o un despliegue a mano de una versión vieja): su schema es anterior
# al que la base ya tiene, e importarlo podría quitar lo que la versión nueva
# añadió. Se ponen sus imágenes y sus ficheros, y nada más.
#
# ── LO QUE DEVUELVE ─────────────────────────────────────────────────────────
#
#   0  en pie, importado y sembrado
#   3  NO se tocó nada (falta el .env, el pull falló): lo de antes sigue en pie y
#      no hay a qué volver — deploy.yml no hace la vuelta atrás
#   *  falló con el stack ya tocado: deploy.yml vuelve a la versión anterior

set -euo pipefail

SHA="${1:?falta el SHA a desplegar}"
DIR="${SYNERGOS_DIR:-/opt/synergos}"
COMPOSE="docker compose --file $DIR/compose.prod.yml --project-directory $DIR"

cd "$DIR"

if [ ! -f .env ]; then
  echo "✗ falta $DIR/.env — los secretos se cargan a mano UNA vez (ver docs/despliegue)."
  exit 3
fi

# ── La versión a la que se puede volver ──────────────────────────────────────
#
# Se lee ANTES de tocar nada. Sin esto, la vuelta atrás no tiene destino: con
# imágenes etiquetadas por SHA no hay un `latest` anterior al que caer, que es
# justo lo que el etiquetado por SHA compró (HU #17).
ANTERIOR=""
[ -f tag.actual ] && ANTERIOR="$(cat tag.actual)"
echo "── versión anterior: ${ANTERIOR:-(ninguna, primer despliegue)}"
echo "── versión a poner:  $SHA"

# ── Los ficheros de la versión, no sólo sus imágenes ─────────────────────────
#
# Una versión son sus imágenes Y el compose y el Caddyfile con que se puso en
# pie. Volver a las imágenes de antes con el compose de ahora muere en el `pull`
# en cuanto la versión nueva trae un servicio que la anterior no tenía: pide una
# imagen que con esa etiqueta nunca se publicó, y la vuelta atrás no vuelve
# (medido en el ensayo de `ed634d55`, el primero tras sumar `bff-reservas`).
# Por eso cada versión que queda en pie guarda los suyos en `versiones/<sha>/`
# (al final), y poner una que ya estuvo repone los de ella ANTES de bajar nada.
#
# Y lo que su compose MONTA del servidor es de la versión igual que el Caddyfile:
# el catálogo de procesos que lee bff-gobierno (009, NP-1). Una versión anterior
# a él no lo guardó, y entonces se queda el que hay — las definiciones sólo se
# añaden, nunca se reescriben, así que el de ahora contiene el de antes.
CATALOGO="provisionar.procesos.json"
GUARDADOS="$DIR/versiones/$SHA"
# Volver = poner una versión que ya estuvo en pie y no es la que corre. Redesplegar
# la que corre (otra vez el mismo SHA) NO es volver: importa y siembra como siempre,
# que es idempotente.
VUELTA=no
if [ -f "$GUARDADOS/compose.prod.yml" ] && [ "$SHA" != "$ANTERIOR" ]; then
  VUELTA=si
fi
if [ -f "$GUARDADOS/compose.prod.yml" ] && [ -f "$GUARDADOS/Caddyfile" ]; then
  echo "── reponiendo el compose y el Caddyfile con que $SHA estuvo en pie"
  cp "$GUARDADOS/compose.prod.yml" "$GUARDADOS/Caddyfile" "$DIR/"
  if [ -f "$GUARDADOS/$CATALOGO" ]; then
    cp "$GUARDADOS/$CATALOGO" "$DIR/"
  fi
fi

# ── Bajar las imágenes ANTES de parar nada ───────────────────────────────────
#
# La descarga es el tramo largo. Hacerla con el sitio abajo alarga la caída por
# el tiempo que tarde la red, que además es el tramo que puede fallar.
echo "── bajando imágenes…"
if ! SYNERGOS_TAG="$SHA" $COMPOSE pull --quiet; then
  echo "✗ el pull de $SHA falló (arriba, por qué); no se paró nada."
  [ -f "$GUARDADOS/compose.prod.yml" ] || echo "  $SHA no tiene ficheros guardados en $GUARDADOS: si es una vuelta atrás, los suyos están en el repo (git show $SHA:compose.prod.yml)."
  exit 3
fi

# ── Parar, arrancar ──────────────────────────────────────────────────────────
#
# `down` SIN `--volumes`: los datos viven en volúmenes con nombre y tienen que
# sobrevivir a esto. Un `-v` de más acá borra las veinte capacidades, la DB del
# CMS y la biblioteca de medios, y no hay vuelta atrás que lo arregle.
echo "── parando el stack…"
SYNERGOS_TAG="$SHA" $COMPOSE down --remove-orphans

echo "── arrancando $SHA…"
SYNERGOS_TAG="$SHA" $COMPOSE up --detach --remove-orphans

# ── Esperar a que estén sanos ────────────────────────────────────────────────
#
# El primer arranque instala Umbraco desatendido —crea tablas y usuario— y
# compila los modelos: son minutos, no segundos. Esperar poco convierte un
# arranque lento en un despliegue "fallido" que en realidad iba bien.
#
# ⚠️ Lo que este comentario decía y era falso (#114): que el arranque «importa
# 880 ítems de uSync». NO los importa — ADR 0008 deja `ImportAtStartup` en
# `None` y nadie lo pisa; medido contra una base vacía, el arranque dice
# `uSync: Startup Complete 0ms`. El import es un paso aparte y tiene su
# herramienta: `tools/importar-schema.sh`
# (docs/despliegue/00-montar-el-entorno.md §5.bis).
# El estado se lee con `docker inspect` y NO con `docker compose ps --format`:
# el `--format` de compose v2 sólo acepta `table` y `json`, no plantillas Go.
# Escrito con una plantilla, no falla — devuelve la plantilla como texto, el
# `awk` no encuentra nada raro, y el bucle da por sanas a todas en el primer
# intento. Un chequeo que siempre pasa es peor que no tenerlo.
estado() {
  local ids
  ids="$(SYNERGOS_TAG="$SHA" $COMPOSE ps --quiet)"
  [ -n "$ids" ] || return 0
  # shellcheck disable=SC2086
  docker inspect --format \
    '{{.Name}} {{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}sin-chequeo{{end}}' $ids
}

echo "── esperando a que las piezas reporten sanas (hasta 6 min)…"
for i in $(seq 1 72); do
  # Sano = corriendo Y (healthy o sin healthcheck definido). `starting` no
  # cuenta como sano: es justo el estado del CMS mientras importa uSync.
  ENFERMOS="$(estado | awk '$2 != "running" || ($3 != "healthy" && $3 != "sin-chequeo")' || true)"
  if [ -z "$ENFERMOS" ]; then
    echo "✓ todas las piezas arriba y sanas (a los $((i * 5))s)"
    break
  fi
  if [ "$i" -eq 72 ]; then
    echo "✗ a los 6 min seguían así:"
    echo "$ENFERMOS"
    exit 1
  fi
  sleep 5
done

# ── Que lo que corre sea lo que se pidió ─────────────────────────────────────
#
# El fallo silencioso que esto caza: una imagen no se pudo bajar, Docker reusa
# la que ya tenía, el contenedor arranca, todo reporta sano — y está corriendo
# la versión anterior. Sano no es actualizado.
echo "── comprobando que todas corren la etiqueta pedida…"
IDS="$(SYNERGOS_TAG="$SHA" $COMPOSE ps --quiet)"
# shellcheck disable=SC2086
VIEJAS="$(docker inspect --format '{{.Name}} {{.Config.Image}}' $IDS \
          | grep --invert-match "caddy:" | grep --invert-match ":$SHA" || true)"
if [ -n "$VIEJAS" ]; then
  echo "✗ estas NO están en $SHA:"
  echo "$VIEJAS"
  exit 1
fi

# Lo que quedó en pie, con los ficheros con que se puso en pie: el destino de
# una vuelta atrás futura (ver «Los ficheros de la versión», arriba).
mkdir -p "$GUARDADOS"
cp "$DIR/compose.prod.yml" "$DIR/Caddyfile" "$GUARDADOS/"
if [ -f "$DIR/$CATALOGO" ]; then
  cp "$DIR/$CATALOGO" "$GUARDADOS/"
fi
echo "$SHA" > tag.actual

# ── El CMS con su schema, y lo que las capacidades exigen ───────────────────
#
# Después de `tag.actual`: la versión ya corre, y si esto falla, deploy.yml vuelve a
# la anterior (que es una vuelta, así que no importa nada encima).
if [ "$VUELTA" = "si" ]; then
  echo "── $SHA ya estuvo en pie y no es la que corría: es una vuelta atrás, así que ni se importa"
  echo "   su schema (anterior al de la base) ni se siembra nada."
else
  echo "── el schema de uSync (y el contenido, sólo si la base es nueva)…"
  SYNERGOS_TAG="$SHA" "$DIR/importar-schema.sh" --contenido-la-primera-vez

  echo "── lo que las capacidades exigen (definiciones, plantillas, recursos y precios)…"
  "$DIR/provisionar-en-la-red.sh"
  "$DIR/provisionar-en-la-red.sh" --verificar
fi

echo "✓ $SHA desplegado (el humo público decide si se queda)"
