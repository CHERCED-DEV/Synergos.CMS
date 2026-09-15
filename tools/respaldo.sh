#!/usr/bin/env bash
#
# La copia de seguridad de los datos de las 22 (HU #31).
#
# ─────────────────────────────────────────────────────────────────────────────
# LAS IMÁGENES SE RECONSTRUYEN EN MINUTOS. LOS DATOS NO SE RECONSTRUYEN NUNCA.
#
# Lo que vive en estos volúmenes —pedidos, citas, mensajes, consentimientos,
# documentos firmados, la base del CMS y la biblioteca de medios— no está en
# ningún otro sitio. El despliegue ya protege los volúmenes de sí mismo (`down`
# sin `--volumes`, con gate). Esto protege del SERVIDOR: un disco que falla, un
# borrado a mano, una máquina que se pierde.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./respaldo.sh [destino]
#
# Sin argumento, escribe en $SYNERGOS_BACKUP_DIR o /var/backups/synergos. Y si
# hay destino remoto configurado, se la lleva fuera cifrada (enviar-respaldo.sh).
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
# ── QUÉ SE COPIA: todo, menos lo que se nombra ───────────────────────────────
#
# La lista sigue saliendo del compose y no de una lista a mano. Lo que cambió
# es el SENTIDO del filtro, y ahí había un defecto caro: el criterio era
# `-data$`, así que copiaba `caddy-data` —certificados que Caddy vuelve a
# pedir solo— y dejaba fuera `cms-db` (la base de Umbraco), `cms-media` (la
# biblioteca), `cms-appdata` (entre otras cosas la bitácora JSONL) y
# `cms-dpkeys` (sin los cuales se rompen los secretos TOTP de los members,
# ADR 0084). O sea: el respaldo del producto no llevaba el producto, y no
# fallaba — el tar salía bien, con las veinte capacidades dentro.
#
# Hoy el default es INCLUIR. Lo que se excluye se nombra acá con su razón, y el
# gate cruza esta lista contra el compose, así que un volumen nuevo entra solo
# y quien quiera dejarlo fuera tiene que escribir por qué. El sentido importa
# más que la lista: con un patrón que incluye, lo que se olvida se pierde en
# silencio; con uno que excluye, lo que se olvida se copia de más.
RECONSTRUIBLES='^(cms-logs|caddy-data|caddy-config)$'
#   cms-logs      · registros de la aplicación: no reconstruyen ningún estado y
#                   son el volumen que más crece.
#   caddy-data    · certificados y cuenta ACME. Caddy los vuelve a sacar solo, y
#                   además son llaves privadas: meterlas en un archivo que viaja
#                   a un bucket añade un sitio más donde se pueden perder.
#   caddy-config  · estado autogenerado del proxy.

set -euo pipefail

DIR="${SYNERGOS_DIR:-/opt/synergos}"
DESTINO="${1:-${SYNERGOS_BACKUP_DIR:-/var/backups/synergos}}"
COMPOSE="docker compose --file $DIR/compose.prod.yml --project-directory $DIR"
AQUI="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Lo dispara `cron`, que no hereda el entorno de nadie.
if [ -f "$DIR/.env" ]; then
  set -a
  # shellcheck disable=SC1091,SC1090
  . "$DIR/.env"
  set +a
fi

# Cuántos días se quedan las copias EN EL SERVIDOR. Es otra decisión que la de
# fuera —y más corta— porque cumplen cosas distintas: la local es la que se
# restaura rápido cuando alguien se lleva por delante una tabla esta mañana; la
# de fuera es el archivo. Guardar meses de datos personales en el mismo disco
# que ya se está protegiendo no añade seguridad, sólo añade dónde perderlos.
LOCAL_DIAS="${SYNERGOS_RESPALDO_LOCAL_DIAS:-7}"
MINIMO="${SYNERGOS_RESPALDO_MINIMO:-3}"
REMOTO="${SYNERGOS_RESPALDO_DESTINO:-}"

# La marca de tiempo va en UTC: un servidor que cambia de zona no puede hacer
# que dos copias parezcan la misma ni que la de ayer parezca la de mañana.
SELLO="$(date -u +%Y%m%dT%H%M%SZ)"
ARCHIVO="$DESTINO/synergos-datos-$SELLO.tar.gz"

echo "── Respaldo $SELLO ──"

mkdir -p "$DESTINO"

mapfile -t VOLUMENES < <(
  $COMPOSE config --volumes | grep -E -v "$RECONSTRUIBLES" | sort
)

if [ "${#VOLUMENES[@]}" -eq 0 ]; then
  echo "✗ no se encontró ningún volumen de datos. ¿Está bien \$SYNERGOS_DIR ($DIR)?" >&2
  exit 1
fi

echo "  ${#VOLUMENES[@]} volúmenes"

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

PRESENTES=()
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

  PRESENTES+=("$v")
  echo "  · $v — $(du -h "$TMP/$v.tar.gz" | cut -f1)"
done

# Un manifiesto con QUÉ se copió y de qué versión. Sin esto, dentro de seis
# meses hay un tar.gz y ninguna forma de saber si le falta una capacidad.
#
# El `sha256` de cada pieza va acá porque el archivo viaja: cifrarlo y subirlo
# a un tercero mete dos tramos más donde se puede truncar, y un tar.gz truncado
# se desempaca «bien» hasta el byte que falta.
{
  echo "sello=$SELLO"
  echo "sha=$(grep -E '^SYNERGOS_TAG=' "$DIR/.env" 2>/dev/null | cut -d= -f2- || echo desconocido)"
  echo "volumenes=${#PRESENTES[@]}"
  for v in "${PRESENTES[@]}"; do
    echo "vol=$v sha256=$(sha256sum "$TMP/$v.tar.gz" | cut -d' ' -f1) bytes=$(stat -c %s "$TMP/$v.tar.gz")"
  done
} > "$TMP/MANIFIESTO"

tar -czf "$ARCHIVO" -C "$TMP" .
chmod 600 "$ARCHIVO"   # datos personales: no legible por cualquiera del servidor

echo "✓ $ARCHIVO ($(du -h "$ARCHIVO" | cut -f1))"

# ── Fuera de la máquina ──────────────────────────────────────────────────────
if [ -n "$REMOTO" ]; then
  # Si esto falla, el respaldo ENTERO falla. Terminar en verde habiendo dejado
  # la copia en el disco que venía a proteger es la forma exacta de creer que
  # hay respaldo y no tenerlo — y un `cron` nocturno que sale 0 no lo mira
  # nadie nunca más.
  "$AQUI/enviar-respaldo.sh" "$ARCHIVO"
else
  echo
  echo "  ⚠️ ESTA COPIA NO SALIÓ DEL SERVIDOR, así que NO es un respaldo: no"
  echo "     protege del caso que duele, que es perder la máquina. Poné"
  echo "     SYNERGOS_RESPALDO_DESTINO y SYNERGOS_RESPALDO_LLAVE_PUBLICA en"
  echo "     $DIR/.env (ver .env.example)."
fi

# ── Y lo que borra las de acá ────────────────────────────────────────────────
#
# Se poda al final, nunca antes del envío: podar primero es cómo se acaba sin
# copias la noche en que el envío falla. Y el piso de $MINIMO es lo que impide
# que un respaldo que lleva días sin correr se lleve por delante la última
# buena — el fallo silencioso de toda retención por edad.
echo "  podando las locales (más de $LOCAL_DIAS días, nunca menos de $MINIMO)…"
i=0
while read -r viejo; do
  [ -n "$viejo" ] || continue
  i=$((i + 1))
  [ "$i" -le "$MINIMO" ] && continue
  if [ -n "$(find "$viejo" -maxdepth 0 -mtime "+$LOCAL_DIAS" 2>/dev/null)" ]; then
    rm -f "$viejo" && echo "  · $(basename "$viejo") — borrada"
  fi
done < <(ls -1 "$DESTINO"/synergos-datos-*.tar.gz 2>/dev/null | sort --reverse)

echo
echo "  Restaurar acá:    ./restaurar.sh $ARCHIVO"
echo "  Probar de verdad: ./prueba-restauracion.sh"
