#!/usr/bin/env bash
#
# Devolver los datos de las 22 a como estaban (HU #31).
#
# ─────────────────────────────────────────────────────────────────────────────
# UNA COPIA QUE NADIE RESTAURÓ NUNCA NO ES UNA COPIA.
#
# Por eso este script existe a la vez que `respaldo.sh` y no «después»: lo que
# hay que verificar no es que el respaldo se cree —eso lo hace cualquier tar—
# sino que restaurarlo devuelve un sistema que funciona. Sin este fichero, la
# copia es una promesa que nadie comprobó.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./restaurar.sh <archivo.tar.gz> [--si-estoy-seguro]
#
# Sin la bandera solo INSPECCIONA: dice qué traería y no toca nada. Restaurar
# PISA los datos vivos, y un comando destructivo que se dispara con un solo
# argumento se dispara solo alguna vez.

set -euo pipefail

ARCHIVO="${1:?falta el archivo de respaldo}"
SEGURO="${2:-}"
DIR="${SYNERGOS_DIR:-/opt/synergos}"
COMPOSE="docker compose --file $DIR/compose.prod.yml --project-directory $DIR"

[ -f "$ARCHIVO" ] || { echo "✗ no existe $ARCHIVO" >&2; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

tar -xzf "$ARCHIVO" -C "$TMP"

[ -f "$TMP/MANIFIESTO" ] || { echo "✗ $ARCHIVO no tiene MANIFIESTO: no es un respaldo de Synergos" >&2; exit 1; }

echo "── Respaldo ──"
sed -n 's/^sello=/  fecha:   /p;s/^sha=/  version: /p;s/^volumenes=/  volumenes: /p' "$TMP/MANIFIESTO"
echo

mapfile -t VOLUMENES < <(grep -E -- '-data$' "$TMP/MANIFIESTO" | sort)

for v in "${VOLUMENES[@]}"; do
  if [ -f "$TMP/$v.tar.gz" ]; then
    echo "  · $v — $(du -h "$TMP/$v.tar.gz" | cut -f1)"
  else
    echo "  · $v — NO ESTÁ en la copia (la capacidad nunca había arrancado)"
  fi
done

if [ "$SEGURO" != "--si-estoy-seguro" ]; then
  echo
  echo "Esto fue una INSPECCIÓN: no se tocó nada."
  echo "Para restaurar de verdad —PISA los datos vivos—:"
  echo "  $0 $ARCHIVO --si-estoy-seguro"
  exit 0
fi

echo
echo "── Restaurando ──"

# En frío, por lo mismo que el respaldo: escribir dentro de un volumen que una
# capacidad viva está usando deja el almacén en un estado que nadie eligió.
echo "  parando los servicios…"
$COMPOSE stop >/dev/null

restaurados=0
for v in "${VOLUMENES[@]}"; do
  [ -f "$TMP/$v.tar.gz" ] || continue

  real="$(docker volume ls --quiet --filter "name=${v}$" | head -1)"
  if [ -z "$real" ]; then
    # El volumen no existe todavía: se crea, para que restaurar en una máquina
    # nueva funcione. Es el caso que de verdad importa — el servidor que se
    # perdió.
    real="$(docker volume create "$v")"
  fi

  # Se VACÍA antes de desempacar. Sin esto, un fichero que existía en el
  # servidor y no en la copia sobreviviría, y el resultado sería un estado
  # mezclado que no es ni el de ayer ni el de hoy — y que nadie puede razonar.
  docker run --rm --user 0 \
    --volume "$real:/destino" \
    --volume "$TMP:/entrada:ro" \
    alpine:3 sh -c 'rm -rf /destino/..?* /destino/.[!.]* /destino/*; tar -xzf "/entrada/'"$v"'.tar.gz" -C /destino' 2>/dev/null

  echo "  · $v restaurado"
  restaurados=$((restaurados + 1))
done

echo "  arrancando…"
$COMPOSE start >/dev/null

echo "✓ $restaurados volúmenes restaurados."
echo
echo "  Y AHORA COMPROBALO: que el respaldo se desempaque no dice que el"
echo "  sistema funcione. Corré ./humo-publico.sh y mirá que los datos que"
echo "  esperabas estén ahí."
