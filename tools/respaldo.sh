#!/usr/bin/env bash
#
# La copia de seguridad de los datos de las 22 (HU #31).
#
# ─────────────────────────────────────────────────────────────────────────────
# LAS IMÁGENES SE RECONSTRUYEN EN MINUTOS. LOS DATOS NO SE RECONSTRUYEN NUNCA.
#
# Lo que vive en estos volúmenes —pedidos, citas, mensajes, consentimientos,
# documentos firmados— no está en ningún otro sitio. El despliegue ya protege
# los volúmenes de sí mismo (`down` sin `--volumes`, con gate). Esto protege del
# SERVIDOR: un disco que falla, un borrado a mano, una máquina que se pierde.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./respaldo.sh [destino]
#
# Sin argumento, escribe en $SYNERGOS_BACKUP_DIR o /var/backups/synergos.
#
# ── EN FRÍO, y no es una preferencia ─────────────────────────────────────────
#
# `JsonCollectionStore` escribe con un `lock` de PROCESO. Copiar el volumen con
# la capacidad viva puede atrapar un fichero a medio escribir — y un JSON a
# medias no da error al copiarse: lo da meses después, al restaurar, que es
# justo cuando no hay margen. Se paran los servicios, se copia, se arrancan.
#
# Es la misma decisión que `deploy-remoto.sh` toma por la misma razón, y se
# acepta el mismo coste: una caída corta.
#
# ── Lo que este script NO decide ─────────────────────────────────────────────
#
# DÓNDE se guardan estas copias y POR CUÁNTO TIEMPO. Contienen datos personales
# —direcciones de entrega, nombres de pacientes— así que es una decisión de
# privacidad, no de infraestructura, y es del arquitecto. Este script deja el
# fichero en disco; llevárselo fuera del servidor y decidir su retención es otro
# paso, y sin ese paso la copia sigue muriendo con la máquina.

set -euo pipefail

DIR="${SYNERGOS_DIR:-/opt/synergos}"
DESTINO="${1:-${SYNERGOS_BACKUP_DIR:-/var/backups/synergos}}"
COMPOSE="docker compose --file $DIR/compose.prod.yml --project-directory $DIR"

# La marca de tiempo va en UTC: un servidor que cambia de zona no puede hacer
# que dos copias parezcan la misma ni que la de ayer parezca la de mañana.
SELLO="$(date -u +%Y%m%dT%H%M%SZ)"
ARCHIVO="$DESTINO/synergos-datos-$SELLO.tar.gz"

echo "── Respaldo $SELLO ──"

mkdir -p "$DESTINO"

# Los volúmenes de datos salen del compose, no de una lista a mano: el día que
# aparezca una capacidad nueva, su volumen entra solo. Una lista escrita a mano
# se desincroniza, y lo que se pierde es justo lo que nadie recuerda añadir.
mapfile -t VOLUMENES < <(
  $COMPOSE config --volumes | grep -E -- '-data$' | sort
)

if [ "${#VOLUMENES[@]}" -eq 0 ]; then
  echo "✗ no se encontró ningún volumen de datos. ¿Está bien \$SYNERGOS_DIR ($DIR)?" >&2
  exit 1
fi

echo "  ${#VOLUMENES[@]} volúmenes de datos"

# ── En frío ──────────────────────────────────────────────────────────────────
echo "  parando los servicios…"
$COMPOSE stop >/dev/null

# Se arranca de vuelta pase lo que pase. Un respaldo que falla y deja el sitio
# caído es peor que no haber respaldado.
reanudar() {
  echo "  arrancando de vuelta…"
  $COMPOSE start >/dev/null || echo "✗ NO SE PUDO ARRANCAR. Revisar a mano: $COMPOSE start" >&2
}
trap reanudar EXIT

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"; reanudar' EXIT

for v in "${VOLUMENES[@]}"; do
  # El nombre real lleva el prefijo del proyecto de compose; se resuelve con
  # `volume ls` para no reimplementar esa regla acá.
  real="$(docker volume ls --quiet --filter "name=${v}$" | head -1)"
  if [ -z "$real" ]; then
    echo "  · $v — todavía no existe (capacidad nunca arrancada), se salta"
    continue
  fi

  # `--user 0` porque los datos son del usuario del contenedor, no del que corre
  # esto. Sin eso, el tar sale vacío y en silencio.
  docker run --rm --user 0 \
    --volume "$real:/origen:ro" \
    --volume "$TMP:/salida" \
    alpine:3 tar -czf "/salida/$v.tar.gz" -C /origen . 2>/dev/null

  echo "  · $v — $(du -h "$TMP/$v.tar.gz" | cut -f1)"
done

# Un manifiesto con QUÉ se copió y de qué versión. Sin esto, dentro de seis
# meses hay un tar.gz y ninguna forma de saber si le falta una capacidad.
{
  echo "sello=$SELLO"
  echo "sha=$(grep -E '^SYNERGOS_TAG=' "$DIR/.env" 2>/dev/null | cut -d= -f2- || echo desconocido)"
  echo "volumenes=${#VOLUMENES[@]}"
  printf '%s\n' "${VOLUMENES[@]}"
} > "$TMP/MANIFIESTO"

tar -czf "$ARCHIVO" -C "$TMP" .
chmod 600 "$ARCHIVO"   # datos personales: no legible por cualquiera del servidor

echo "✓ $ARCHIVO ($(du -h "$ARCHIVO" | cut -f1))"
echo
echo "  Restaurar:  ./restaurar.sh $ARCHIVO"
echo "  ⚠️ Esta copia NO está fuera del servidor todavía. Una copia que muere"
echo "     con la máquina no protege de perder la máquina."
