#!/usr/bin/env bash
#
# Llevarse la copia FUERA de la máquina (HU #31).
#
# ─────────────────────────────────────────────────────────────────────────────
# UNA COPIA QUE VIVE EN EL DISCO QUE VIENE A PROTEGER NO PROTEGE DE NADA.
#
# `respaldo.sh` deja un tar.gz en `/var/backups/synergos`. Eso protege de un
# borrado a mano y de un despliegue torpe. NO protege del caso que duele: el
# disco que falla, el proveedor que cierra la cuenta, la máquina que se pierde.
# Este script es el paso que faltaba — y es un paso APARTE porque tiene otras
# preguntas que contestar: quién puede leer esto, y por cuánto tiempo existe.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./enviar-respaldo.sh <archivo.tar.gz>
#
# Lo llama `respaldo.sh` al terminar. Se puede correr suelto para reenviar una
# copia concreta.
#
# ── SE CIFRA EN ORIGEN, Y NO HAY MODO «SIN CIFRAR» ───────────────────────────
#
# Dentro van direcciones de entrega y nombres de pacientes. Sale de nuestra
# máquina y aterriza en el disco de un tercero, donde lo protege una credencial
# de bucket — y una credencial de bucket se filtra, se copia a un CI, se deja
# con más permisos de los que hacía falta. El respaldo es justo el fichero que
# reúne TODOS los datos personales del producto en un solo objeto descargable.
#
# Se cifra con `age` y con una llave ASIMÉTRICA, y las dos mitades de esa
# decisión importan:
#
#   · ASIMÉTRICA, no una contraseña. El servidor tiene la llave PÚBLICA: puede
#     escribir respaldos y NO puede leer los que ya mandó. Con una contraseña
#     simétrica, quien se lleva el servidor se lleva de paso el histórico
#     entero — que es más de lo que hay vivo en la máquina en ese momento.
#     La llave pública no es un secreto: va en `.env` como un valor más.
#
#   · `age` y no GPG. Un fichero, un destinatario, sin llavero, sin agente y
#     sin `--batch` que se olvida. Las formas de que GPG falle en un servidor
#     sin terminal son exactamente las que acaban en un «lo dejo sin cifrar por
#     ahora».
#
# ⚠️ Y EL PRECIO, DICHO: la llave privada (la «identidad») pasa a ser lo único
# que convierte estos ficheros en datos. Si se pierde, los respaldos son ruido.
# Por eso NO vive en el servidor —ahí sólo está la pública— y por eso existe
# `prueba-restauracion.sh`: es lo único que demuestra que la llave que se
# guardó abre lo que se está mandando. Sin esa prueba, «está cifrado» y «está
# perdido» se ven exactamente igual desde acá.
#
# ── DÓNDE, con rclone, y por qué así ─────────────────────────────────────────
#
# El destino es un remoto de `rclone` (`SYNERGOS_RESPALDO_DESTINO`). Qué
# proveedor y qué bucket es decisión del arquitecto, no de este fichero: S3,
# R2, B2, un SFTP a otra máquina o un disco montado son todos el mismo comando.
# Las credenciales viajan por variables `RCLONE_CONFIG_*` del `.env` del
# servidor — o sea que NINGÚN secreto entra al repo y no hace falta un fichero
# de configuración aparte que alguien tenga que acordarse de proteger.
#
# ── SI FALTA ALGO, REVIENTA ACÁ Y NO DESPUÉS ─────────────────────────────────
#
# Las comprobaciones van ANTES de tocar nada. Es la forma del defecto #56 —el
# modo `Http` del CDN sin URL— y la de la llave de firma de `Api.Identity`:
# arrancar verde, contestar que todo va bien y reventar el día que alguien lo
# necesita es el peor de los fallos posibles. Un respaldo mal configurado que
# termina con código 0 es una copia que nadie tiene y todos creen tener.
#
# Lo que NO hace es fallar por no estar configurado en absoluto: sin
# `SYNERGOS_RESPALDO_DESTINO` este script ni se llama, y `respaldo.sh` dice a
# gritos que lo que dejó NO es un respaldo. Un clon limpio y una máquina de
# desarrollo tienen que poder copiar sus volúmenes sin cuenta de nada — es el
# mismo trato que el default `Stub` de todos los cableados del producto. Lo que
# sí revienta es el caso peligroso: configurado a MEDIAS, que es el que PARECE
# configurado.

set -euo pipefail

ARCHIVO="${1:?falta el archivo a enviar}"
DIR="${SYNERGOS_DIR:-/opt/synergos}"

# Esto lo dispara `cron`, que no hereda el entorno de nadie. Sin leer el `.env`
# del servidor, un respaldo programado se quedaría sin destino y sin llave —
# y con las comprobaciones de abajo, gritando todas las noches por algo que
# está bien configurado.
if [ -f "$DIR/.env" ]; then
  set -a
  # shellcheck disable=SC1091,SC1090
  . "$DIR/.env"
  set +a
fi

DESTINO="${SYNERGOS_RESPALDO_DESTINO:-}"
LLAVE="${SYNERGOS_RESPALDO_LLAVE_PUBLICA:-}"
DIAS="${SYNERGOS_RESPALDO_RETENCION_DIAS:-30}"
MINIMO="${SYNERGOS_RESPALDO_MINIMO:-3}"

grita() { echo "✗ $*" >&2; exit 1; }

[ -f "$ARCHIVO" ] || grita "no existe $ARCHIVO"

# ── Las comprobaciones, TODAS antes de mover un byte ─────────────────────────

[ -n "$DESTINO" ] || grita "falta SYNERGOS_RESPALDO_DESTINO.
  Este script no sabe hacer otra cosa que llevarse la copia fuera. Sin destino
  no hay nada que hacer, y terminar en verde sería decir que la copia salió."

[ -n "$LLAVE" ] || grita "falta SYNERGOS_RESPALDO_LLAVE_PUBLICA.
  NO HAY MODO SIN CIFRAR, a propósito: acá dentro van direcciones de entrega y
  nombres de pacientes, y esto aterriza en el disco de un tercero. Se genera
  con 'age-keygen -o identidad.txt' EN LA MÁQUINA DEL ARQUITECTO; al servidor
  va SOLO la línea 'age1…' que imprime (la pública). El fichero 'identidad.txt'
  NO se copia acá: si estuviera, el servidor podría leer su propio histórico y
  el cifrado dejaría de comprar nada."

command -v age >/dev/null 2>&1 \
  || grita "falta 'age'. En el servidor: apt-get install --yes age (lo deja puesto tools/bootstrap-servidor.sh)."
command -v rclone >/dev/null 2>&1 \
  || grita "falta 'rclone'. En el servidor: apt-get install --yes rclone (lo deja puesto tools/bootstrap-servidor.sh)."

# ── La retención, que es una decisión de PRIVACIDAD y por eso no admite «nunca»
#
# No existe el valor «para siempre». Un archivo con los datos personales de
# todo el producto guardado indefinidamente en un bucket es una decisión que
# nadie tomó y que nadie recuerda haber tomado — y dentro de dos años es una
# filtración esperando a que alguien encuentre la credencial. Acá hay que decir
# un número de días, y el default (30) es un número escrito, no una omisión.
case "$DIAS" in
  ''|*[!0-9]*) grita "SYNERGOS_RESPALDO_RETENCION_DIAS tiene que ser un entero de días: '$DIAS'." ;;
esac
[ "$DIAS" -ge 1 ] \
  || grita "SYNERGOS_RESPALDO_RETENCION_DIAS=0 sería «guardar para siempre», y eso no es una opción
  de este script: llevan datos personales. Poné los días que de verdad hacen falta."

case "$MINIMO" in
  ''|*[!0-9]*) grita "SYNERGOS_RESPALDO_MINIMO tiene que ser un entero: '$MINIMO'." ;;
esac
[ "$MINIMO" -ge 1 ] \
  || grita "SYNERGOS_RESPALDO_MINIMO=0 deja el bucket vacío el día que el respaldo lleve
  más de $DIAS días sin correr — justo el día en que hace falta. Mínimo 1."

NOMBRE="$(basename "$ARCHIVO")"
CIFRADO="$NOMBRE.age"

echo "── Enviando fuera del servidor ──"
echo "  destino:   $DESTINO"
echo "  retención: $DIAS días, nunca menos de $MINIMO copias"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# ── Cifrar ───────────────────────────────────────────────────────────────────
#
# Antes de salir y no «en el destino»: el cifrado del proveedor lo hace el
# proveedor, con su llave, y lo desactiva quien tenga su consola. Este cifrado
# sólo lo deshace quien tenga la identidad, que no está ni acá ni allá.
age --recipient "$LLAVE" --output "$TMP/$CIFRADO" "$ARCHIVO" \
  || grita "no se pudo cifrar. ¿Es 'age1…' lo que hay en SYNERGOS_RESPALDO_LLAVE_PUBLICA?"

BYTES="$(stat -c %s "$TMP/$CIFRADO")"
echo "  · cifrado — $(du -h "$TMP/$CIFRADO" | cut -f1)"

# ── Subir ────────────────────────────────────────────────────────────────────
rclone copyto "$TMP/$CIFRADO" "$DESTINO/$CIFRADO" \
  || grita "no se pudo subir a $DESTINO. La copia local SIGUE EN $ARCHIVO — no se borró nada."

# Y comprobar que está. `copyto` puede terminar en verde habiendo escrito en un
# sitio que nadie lee: un prefijo con un typo, un bucket con una política que
# descarta en silencio, un remoto que se quedó apuntando a /dev/null después de
# una migración. Un envío que no se vuelve a mirar es una promesa, que es
# exactamente lo que este ticket vino a quitar de en medio.
ALLA="$(rclone lsf "$DESTINO/$CIFRADO" --format s 2>/dev/null | head -1)"
[ -n "$ALLA" ] || grita "subió sin error y allá no hay nada en $DESTINO/$CIFRADO."
[ "$ALLA" = "$BYTES" ] \
  || grita "allá pesa $ALLA y acá $BYTES: llegó a medias. La copia local sigue en $ARCHIVO."

echo "  · $DESTINO/$CIFRADO — comprobado ($BYTES bytes)"

# ── La retención, que es lo que BORRA ────────────────────────────────────────
#
# Se poda DESPUÉS de haber comprobado que la de hoy está allá, y nunca antes:
# podar primero es cómo se acaba con cero copias la noche en que el envío
# falla.
#
# La edad sale del SELLO DEL NOMBRE y no de la fecha del objeto remoto. Copiar
# un bucket a otro —que es lo que se hace al cambiar de proveedor— le pone a
# todo la fecha de hoy: con la fecha del objeto, el día de la migración o no se
# borra nunca nada, o se borra todo de golpe. El nombre lleva la verdad.
podar() {
  local ahora nombre sello epoca edad i=0
  ahora="$(date -u +%s)"

  while read -r nombre; do
    [ -n "$nombre" ] || continue
    i=$((i + 1))

    # Las N más nuevas se quedan pase lo que pase. Sin este piso, la retención
    # por edad vacía el bucket el día que el respaldo lleva $DIAS sin correr —
    # o sea que el trabajo programado que se rompió y nadie vio se lleva por
    # delante la última copia buena, en silencio y justo cuando hace falta.
    if [ "$i" -le "$MINIMO" ]; then
      echo "  · $nombre — se queda (de las $MINIMO más nuevas)"
      continue
    fi

    sello="${nombre#synergos-datos-}"
    sello="${sello%%.*}"
    epoca="$(date -u -d "${sello:0:8} ${sello:9:2}:${sello:11:2}:${sello:13:2}" +%s 2>/dev/null || echo 0)"
    if [ "$epoca" -eq 0 ]; then
      # Un nombre que no se sabe leer NO se borra. Borrar lo que no se entiende
      # es cómo un cambio de convención de nombres se lleva el archivo entero.
      echo "  · $nombre — nombre ilegible, NO se toca"
      continue
    fi

    edad=$(( (ahora - epoca) / 86400 ))
    if [ "$edad" -gt "$DIAS" ]; then
      rclone deletefile "$DESTINO/$nombre" && echo "  · $nombre — borrada ($edad días)"
    fi
  done
}

echo "  podando…"
rclone lsf "$DESTINO" --include 'synergos-datos-*.tar.gz.age' 2>/dev/null | sort --reverse | podar

echo "✓ fuera del servidor y cifrada."
echo
echo "  Y AHORA COMPROBALO DE VERDAD: que el fichero esté allá no dice que se"
echo "  pueda restaurar, ni que la llave que guardaste sea la que lo abre."
echo "    ./prueba-restauracion.sh"
