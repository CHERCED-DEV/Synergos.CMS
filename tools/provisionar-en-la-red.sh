#!/usr/bin/env bash
#
# `provisionar.sh`, corrido DENTRO de la red de Docker del stack — como se corre en
# el servidor (002-produccion, 2026-10-15).
#
# ─────────────────────────────────────────────────────────────────────────────
# POR QUÉ HACE FALTA.
#
# `provisionar.sh` habla con las capacidades por su NOMBRE DE SERVICIO
# (`http://api-workflow:8080`…), y esos nombres sólo resuelven dentro de la red de
# Docker: desde el servidor mismo no se alcanza ninguno —a propósito, las
# capacidades no publican puertos—. La guía mandaba correrlo «en el servidor» sin
# decir cómo entrar a la red, y su cabecera lo dejaba en «dentro de la red de
# Docker o con un túnel».
#
# Esto lo corre en un contenedor efímero enganchado a `<proyecto>_interna`, con el
# directorio del despliegue montado en sólo lectura y la llave compartida pasada
# por entorno (nunca en la línea de órdenes, que se ve en `ps`). Las herramientas
# van en su versión GNU: con las de BusyBox, `grep --extended-regexp` no existe y
# un precio de 320000 salía «no numérico» — medido en el ensayo del despliegue.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./provisionar-en-la-red.sh [--verificar] [lo demás que acepte provisionar.sh]

set -euo pipefail

DIR="${SYNERGOS_DIR:-/opt/synergos}"
cd "$DIR"

[ -f .env ] || { echo "✗ falta $DIR/.env — los secretos se cargan a mano UNA vez (ver docs/despliegue)." >&2; exit 1; }
[ -f provisionar.sh ] || { echo "✗ falta $DIR/provisionar.sh — lo copia el despliegue." >&2; exit 1; }

set -a
# shellcheck disable=SC1091
. ./.env
set +a
[ -n "${SYNERGOS_API_KEY:-}" ] || { echo "✗ $DIR/.env no trae SYNERGOS_API_KEY." >&2; exit 1; }

PROYECTO="$(sed -n 's/^name:[[:space:]]*//p' compose.prod.yml | head -1)"
RED="${PROYECTO:-synergos}_interna"
docker network inspect "$RED" >/dev/null 2>&1 \
  || { echo "✗ no existe la red $RED: ¿está el stack en pie? (deploy-remoto.sh <sha>)" >&2; exit 1; }

# Las que lee provisionar.sh, sólo si el .env las trae: sin ellas usa sus defaults.
# SYNERGOS_GOB_DEFINITION ya no: las claves de Gobierno salen del catálogo de
# procesos (009, NP-1), y la variable queda sólo para el CMS.
PASAR=(--env SYNERGOS_API_KEY)
for v in SYNERGOS_TRACKING_PREFIX; do
  [ -n "${!v:-}" ] && PASAR+=(--env "$v")
done

exec docker run --rm --network "$RED" \
  --volume "$DIR:/opt/synergos:ro" --workdir /opt/synergos \
  "${PASAR[@]}" \
  python:3.12-alpine \
  sh -c 'apk add --no-cache --quiet bash curl grep sed coreutils >/dev/null && exec bash ./provisionar.sh "$@"' _ "$@"
