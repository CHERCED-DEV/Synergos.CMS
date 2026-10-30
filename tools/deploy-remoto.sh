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
  exit 1
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
echo "✓ $SHA desplegado (el humo público decide si se queda)"
