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

set -euo pipefail

SHA="${1:?falta el SHA a desplegar}"
DIR="${SYNERGOS_DIR:-/opt/synergos}"
COMPOSE="docker compose --file $DIR/compose.prod.yml --project-directory $DIR"

cd "$DIR"

if [ ! -f .env ]; then
  echo "✗ falta $DIR/.env — los secretos se cargan a mano UNA vez (ver docs/despliegue)."
  exit 1
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

# ── Bajar las imágenes ANTES de parar nada ───────────────────────────────────
#
# La descarga es el tramo largo. Hacerla con el sitio abajo alarga la caída por
# el tiempo que tarde la red, que además es el tramo que puede fallar.
echo "── bajando imágenes…"
SYNERGOS_TAG="$SHA" $COMPOSE pull --quiet

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
# El primer arranque instala Umbraco desatendido e importa 880 ítems de uSync —
# unos 74 s medidos en CI (ADR 0128). Esperar poco convierte un arranque lento
# en un despliegue "fallido" que en realidad iba bien.
# El estado se lee con `docker inspect` y NO con `docker compose ps --format`:
# el `--format` de compose v2 sólo acepta `table` y `json`, no plantillas Go.
# Escrito con una plantilla, no falla — devuelve la plantilla como texto, el
# `awk` no encuentra nada raro, y el bucle da por sanas a las 23 en el primer
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
echo "── comprobando que las 23 corren la etiqueta pedida…"
IDS="$(SYNERGOS_TAG="$SHA" $COMPOSE ps --quiet)"
# shellcheck disable=SC2086
VIEJAS="$(docker inspect --format '{{.Name}} {{.Config.Image}}' $IDS \
          | grep --invert-match "caddy:" | grep --invert-match ":$SHA" || true)"
if [ -n "$VIEJAS" ]; then
  echo "✗ estas NO están en $SHA:"
  echo "$VIEJAS"
  exit 1
fi

echo "$SHA" > tag.actual
echo "✓ $SHA desplegado (el humo público decide si se queda)"
