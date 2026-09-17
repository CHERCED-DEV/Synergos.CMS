#!/usr/bin/env bash
#
# El ensayo de restauración (HU #31).
#
# ─────────────────────────────────────────────────────────────────────────────
# UNA COPIA QUE NADIE HA RESTAURADO NUNCA NO ES UNA COPIA.
#
# `respaldo.sh` sabe crear un archivo y `enviar-respaldo.sh` sabe llevárselo.
# Ninguno de los dos demuestra lo único que importa el día malo: que eso de
# allá VUELVE a ser un sistema que funciona. Entre «el fichero está en el
# bucket» y «el producto volvió» hay cuatro cosas que pueden estar rotas sin
# que nada lo diga:
#
#   1. La llave que guardaste no es la que cifró. Un respaldo que no se puede
#      descifrar y uno que no existe se ven exactamente igual desde el bucket.
#   2. El archivo llegó truncado. Un tar.gz truncado se desempaca «bien» hasta
#      el byte que falta.
#   3. Falta un volumen. Es el defecto que este ensayo destapó la primera vez
#      que se escribió: el filtro `-data$` copiaba los certificados de Caddy
#      —que se vuelven a pedir solos— y dejaba fuera la base del CMS, la
#      biblioteca de medios, `App_Data` y el llavero de DataProtection.
#   4. Los bytes están bien y NO SE PUEDEN LEER. Es el defecto #82 tal cual:
#      `Api.Audit` guardaba sus asientos y devolvía 500 en toda lectura después
#      de un reinicio, porque `System.Text.Json` sabía escribir un
#      `IReadOnlySet<string>` y no sabía leerlo. Ningún test lo vio —ninguno
#      reinicia— y ningún respaldo lo habría visto tampoco: los bytes estaban
#      perfectos.
#
# Por eso esto no comprueba que el fichero llegó: LEVANTA EL PRODUCTO SOBRE LO
# RESTAURADO Y LE PIDE LOS DATOS DE VUELTA, con procesos nuevos, por HTTP y
# contra las mismas rutas que usa un orquestador.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./prueba-restauracion.sh [sello]
#
# Sin argumento toma la más nueva que haya en el destino remoto.
#
# ── DÓNDE SE CORRE, y por qué NO en el servidor ──────────────────────────────
#
# Hace falta la llave privada («identidad») para descifrar, y esa llave NO vive
# en el servidor a propósito: allá está sólo la pública, para que la máquina
# pueda escribir respaldos y no pueda leer los que ya mandó. Correr esto en
# producción obligaría a subir la identidad, o sea a tirar por la borda lo
# único que el cifrado asimétrico compraba.
#
# Se corre en la máquina del arquitecto, o en una de repuesto, o en CI. Lo que
# hace falta ahí: docker, el repo (de donde salen `compose.prod.yml` y las
# rutas de lectura), la identidad, y credenciales de SÓLO LECTURA del bucket.
#
# ── NO TOCA PRODUCCIÓN, y eso es una comprobación, no una intención ──────────
#
# Restaura sobre un proyecto de compose APARTE, con sus propios volúmenes, y se
# niega a arrancar si ese nombre coincide con el de producción. Es la misma
# forma que `humo-publico.sh`: un ensayo que por descuido apunta al sitio vivo
# no es un ensayo, es el incidente.
#
# ── LO QUE ESTE ENSAYO NO CUBRE, dicho de frente ─────────────────────────────
#
# El `.env` del servidor NO está en el respaldo, y es deliberado: ahí viven la
# llave compartida de las 22, la de firma de identidad, la de la pasarela y la
# del correo. Meterlas en el archivo convertiría al respaldo en el objeto más
# valioso del producto y a la identidad de `age` en la llave de TODO — un
# problema de custodia de secretos disfrazado de copia de datos, y con 30
# copias diarias de blanco.
#
# La consecuencia hay que saberla: si se pierde el servidor Y el `.env`, casi
# todo se regenera (llaves compartidas, secretos de sesión) pero hay uno que
# no: `Synergos:Academy:CertificateSigningSecret`. Sin él —o sin el llavero de
# `cms-dpkeys`, que SÍ va en el respaldo— los diplomas ya emitidos dejan de
# verificar, y el propio código lo dice por escrito al pasar. Ese secreto vive
# donde viven los secretos, no donde viven los datos.

set -euo pipefail

SELLO="${1:-}"
DIR="${SYNERGOS_DIR:-/opt/synergos}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Normalmente el operador exporta lo suyo a mano —esto no corre en el servidor—
# pero si apunta SYNERGOS_DIR a una carpeta con un `.env`, se lee igual que en
# los otros dos. Con una salvedad, que se comprueba más abajo: lo que NO puede
# salir de ahí es la identidad.
if [ -f "$DIR/.env" ]; then
  set -a
  # shellcheck disable=SC1091,SC1090
  . "$DIR/.env"
  set +a
fi

PROYECTO="${SYNERGOS_PRUEBA_PROYECTO:-synergos-prueba}"
PRODUCCION="${COMPOSE_PROJECT_NAME:-$(basename "$DIR")}"
DESTINO="${SYNERGOS_RESPALDO_DESTINO:-}"
IDENTIDAD="${SYNERGOS_RESPALDO_IDENTIDAD:-}"

grita() { echo "✗ $*" >&2; exit 1; }

[ -n "$DESTINO" ] || grita "falta SYNERGOS_RESPALDO_DESTINO: el ensayo trae la copia DEL REMOTO.
  Probar contra el fichero local no prueba el tramo que puede fallar —el cifrado y el envío— y
  es la misma trampa que un humo apuntando a localhost: pasa siempre y no dice nada."

[ -n "$IDENTIDAD" ] || grita "falta SYNERGOS_RESPALDO_IDENTIDAD: la llave privada de age.
  Es el fichero que imprimió 'age-keygen -o identidad.txt'. Si no lo tenés, ESA ES LA
  RESPUESTA DEL ENSAYO: los respaldos que hay en el bucket no los puede abrir nadie."
[ -f "$IDENTIDAD" ] || grita "no existe $IDENTIDAD"

case "$(cd "$(dirname "$IDENTIDAD")" && pwd)" in
  "$DIR"|"$DIR"/*)
    grita "la identidad está en $DIR, o sea EN EL SERVIDOR. Si está ahí, quien se lleve la
  máquina se lleva el histórico entero y el cifrado no compra nada. Sacala de ahí." ;;
esac

[ "$PROYECTO" != "$PRODUCCION" ] \
  || grita "SYNERGOS_PRUEBA_PROYECTO es '$PROYECTO', que es el proyecto de PRODUCCIÓN.
  Esto restaura y después BORRA sus volúmenes. Poné otro nombre."

for cmd in docker rclone age; do
  command -v "$cmd" >/dev/null 2>&1 || grita "falta '$cmd'."
done

COMPOSE="docker compose --file $REPO/compose.prod.yml --project-directory $REPO --project-name $PROYECTO"

echo "══ Ensayo de restauración sobre '$PROYECTO' ══"

TMP="$(mktemp -d)"

# El desmontaje va SIEMPRE, falle donde falle. Dejar 24 volúmenes con los datos
# personales de producción en una máquina que no es el servidor es una segunda
# copia que nadie decidió — justo lo que este ticket vino a que no pasara.
desmontar() {
  echo "── desmontando el ensayo…"
  # `--volumes` acá es obligatorio y sólo es seguro por la comprobación de
  # arriba: este proyecto NO es el de producción.
  $COMPOSE down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap desmontar EXIT

# ══ 1. Traerla del remoto ════════════════════════════════════════════════════
if [ -z "$SELLO" ]; then
  NOMBRE="$(rclone lsf "$DESTINO" --include 'synergos-datos-*.tar.gz.age' 2>/dev/null | sort --reverse | head -1)"
  [ -n "$NOMBRE" ] || grita "no hay ningún respaldo en $DESTINO."
else
  NOMBRE="synergos-datos-$SELLO.tar.gz.age"
fi

echo "── 1. trayendo $NOMBRE"
rclone copyto "$DESTINO/$NOMBRE" "$TMP/$NOMBRE" || grita "no se pudo traer $DESTINO/$NOMBRE."

# ══ 2. Descifrar — la prueba de que la llave guardada es la que abre ═════════
echo "── 2. descifrando"
age --decrypt --identity "$IDENTIDAD" --output "$TMP/copia.tar.gz" "$TMP/$NOMBRE" \
  || grita "NO SE PUEDE DESCIFRAR con $IDENTIDAD.
  Esto es lo peor que puede decir este ensayo: hay respaldos, ocupan espacio, se pagan, y
  son ruido. La llave que cifra (SYNERGOS_RESPALDO_LLAVE_PUBLICA) no corresponde con esta
  identidad. Hasta arreglarlo, el producto NO tiene copia de seguridad."

# ── Con qué versión se escribieron estos datos ──────────────────────────────
#
# Se levanta el producto en LA VERSIÓN QUE ESCRIBIÓ LOS DATOS, no en la de hoy.
# Restaurar datos de hace seis meses sobre las imágenes de esta mañana mezcla
# dos preguntas: «¿la copia sirve?» y «¿el código de hoy lee lo de entonces?».
# La primera es la de este ensayo; la segunda es la de una migración, y si se
# contestan juntas un fallo no dice cuál de las dos falló.
VERSION="$(tar -xzOf "$TMP/copia.tar.gz" ./MANIFIESTO 2>/dev/null | sed -n 's/^sha=//p' | head -1)"
[ -n "$VERSION" ] && [ "$VERSION" != "desconocido" ] \
  || grita "el MANIFIESTO no dice con qué versión se escribieron estos datos, así que no hay
  imágenes que levantar. Un respaldo sin versión sólo se puede restaurar adivinando."

# Los secretos del ensayo son de mentira A PROPÓSITO, y se generan ANTES de
# restaurar porque `restaurar.sh` ya llama a compose. Los de producción no
# están acá —el `.env` no viaja en el respaldo— y no hacen falta para LEER: lo
# que hace falta es que las 22 arranquen. Que el ensayo pase con secretos
# inventados es justamente lo que demuestra que lo restaurado son DATOS y no un
# estado que dependía de la máquina que se perdió.
export SYNERGOS_REGISTRY="${SYNERGOS_REGISTRY:-ghcr.io/cherced-dev}"
export SYNERGOS_TAG="$VERSION"
export SYNERGOS_DOMAIN="ensayo.invalid"
export SYNERGOS_API_KEY="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
export SYNERGOS_CART_SECRET="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
export SYNERGOS_IDENTITY_SIGNING_KEY="$(head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n')"
LLAVE="$SYNERGOS_API_KEY"

# ══ 3. Restaurar sobre el proyecto de ensayo ════════════════════════════════
#
# Se reusa `restaurar.sh` en vez de desempacar acá: si el ensayo tuviera su
# propia forma de restaurar, estaría probando esa y no la que se va a usar el
# día malo. Es el mismo argumento por el que el humo va contra la URL pública.
echo "── 3. restaurando sobre '$PROYECTO'"
SYNERGOS_DIR="$REPO" SYNERGOS_COMPOSE_PROJECT="$PROYECTO" \
  "$REPO/tools/restaurar.sh" "$TMP/copia.tar.gz" --si-estoy-seguro

# ══ 4. Levantar el producto SOBRE lo restaurado ═════════════════════════════
echo "── 4. levantando el producto en $VERSION"
# Todos menos el proxy: es el único que publica puertos, y en la máquina donde
# se ensaya el 80 y el 443 son de otro. La lista sale del compose, no de acá.
mapfile -t SERVICIOS < <($COMPOSE config --services | grep -vx proxy)

$COMPOSE pull --quiet "${SERVICIOS[@]}" || grita "no se pudieron bajar las imágenes de $VERSION."
$COMPOSE up --detach "${SERVICIOS[@]}" || grita "no arrancó el stack del ensayo."

echo "   esperando a que reporten sanas…"
for i in $(seq 1 72); do
  ids="$($COMPOSE ps --quiet)"
  # shellcheck disable=SC2086
  enfermos="$(docker inspect --format \
    '{{.Name}} {{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}sin-chequeo{{end}}' $ids \
    | awk '$2 != "running" || ($3 != "healthy" && $3 != "sin-chequeo")' || true)"
  [ -z "$enfermos" ] && break
  [ "$i" -eq 72 ] && { echo "$enfermos"; grita "a los 6 min seguían enfermas."; }
  sleep 5
done

# ══ 5. PEDIRLE LOS DATOS DE VUELTA ══════════════════════════════════════════
#
# Acá es donde esto deja de ser «el fichero llegó». Las rutas SE DERIVAN del
# código de cada capacidad —el `MapGet` de su colección— por la misma razón por
# la que los volúmenes salen del compose: una lista a mano se desincroniza, y
# la capacidad que nadie añadió a la lista es la que nadie descubre que no
# restauró.
#
# ── Lo que se mira, medido contra un proceso vivo ───────────────────────────
#
# Levantando `Api.Audit` sobre un almacén intacto y luego sobre uno ilegible se
# vio que hay TRES respuestas distintas y que ninguna sola alcanza:
#
#   · `/health` contesta 200 con el almacén ilegible. Un humo de salud da por
#     buena una restauración que no sirve — es el defecto #82 exacto.
#   · Un fichero que no es la colección que el tipo espera da **500** en la
#     lectura. Eso es un fallo, y es lo que este paso caza.
#   · Pero un almacén que se lee A MEDIAS —un campo con el tipo cambiado—
#     contesta **200 con menos datos**, porque el conversor de
#     `JsonCollectionStore` es tolerante. Por eso no basta con mirar el código:
#     hay que exigir que VUELVAN DATOS, que es el corte de más abajo.
#
# Y hay una cuarta: algunas colecciones se niegan a listarse sin filtro
# (`Api.Audit` contesta 400, «sin filtro esto es un volcado de la bitácora»), y
# el filtro que querrían es un dato del dominio que este ensayo no conoce. Para
# ésas se cae a pedir una ficha que NO EXISTE: un 404 exige haber leído el
# almacén, y un 500 ahí es el mismo defecto. Queda dicho en la salida cuáles
# quedaron cubiertas sólo así, porque un ensayo que se cree más listo de lo que
# es es peor que no tenerlo.
echo "── 5. leyendo los datos de vuelta"
RUTAS=""
SIN_LECTURA=""
# Las capacidades ya no están planas en la raíz (#136): se BUSCAN por su
# Endpoints/, que es la propiedad que este bucle necesita, en vez de por su sitio.
for d in $(find "$REPO" -type d -name "Synergos.Api.*" -not -path "*/bin/*" -not -path "*/obj/*" | sort); do
  [ -d "$d/Endpoints" ] || continue
  svc="$(basename "$d" | sed 's/^Synergos\.//' | tr '.' '-' | tr '[:upper:]' '[:lower:]')"
  cols="$(grep -rhoE 'MapGet\("/v1/[a-z-]+"' "$d/Endpoints/" 2>/dev/null \
          | sed 's/MapGet("//;s/"//' | sort -u)"
  if [ -z "$cols" ]; then
    SIN_LECTURA="$SIN_LECTURA $svc"
    continue
  fi
  for c in $cols; do RUTAS="$RUTAS $svc|$c"; done
done

# Desde DENTRO de la red del ensayo: las capacidades no publican puertos, y no
# se los va a publicar para esto — un ensayo que cambia la forma del despliegue
# prueba otra cosa.
SALIDA="$(docker run --rm --network "${PROYECTO}_interna" \
  --env "LLAVE=$LLAVE" --env "RUTAS=$RUTAS" alpine:3 sh -c '
  pedir() {
    if cuerpo="$(wget -q -O- --header="X-Synergos-Key: $LLAVE" "$1" 2>/tmp/e)"; then
      echo "200 $cuerpo"
    else
      cod="$(sed -n "s|.*HTTP/1\.[01] \([0-9][0-9][0-9]\).*|\1|p" /tmp/e | head -1)"
      echo "${cod:-000} $(tr "\n" " " < /tmp/e)"
    fi
  }
  for r in $RUTAS; do
    svc="${r%%|*}"; ruta="${r#*|}"
    lista="$(pedir "http://$svc:8080$ruta")"
    ficha="$(pedir "http://$svc:8080$ruta/no-existe-en-el-ensayo")"
    echo "$svc$ruta LISTA=${lista%% *} FICHA=${ficha%% *} ${lista#* }"
  done')"

fallos=0
total=0
solo_ficha=""
while read -r linea; do
  [ -n "$linea" ] || continue
  ruta="${linea%% *}"; resto="${linea#* }"
  lista="${resto%% *}"; lista="${lista#LISTA=}"
  resto="${resto#* }"; ficha="${resto%% *}"; ficha="${ficha#FICHA=}"
  cuerpo="${resto#* }"
  case "$cuerpo" in FICHA=*) cuerpo="" ;; esac

  case "$lista" in
    2*)
      n="$(printf '%s' "$cuerpo" | sed -n 's/.*"total":\([0-9]*\).*/\1/p' | head -1)"
      n="${n:-0}"
      total=$((total + n))
      echo "   ✓ $ruta — $n"
      ;;
    4*)
      # La colección no se deja listar sin filtro. Vale la ficha inexistente:
      # un 404 sólo se puede contestar habiendo leído el almacén.
      case "$ficha" in
        404|4*) echo "   · $ruta — no se lista sin filtro; la ficha contesta $ficha (almacén leído)"
                solo_ficha="$solo_ficha $ruta" ;;
        *)      echo "   ✗ $ruta — lista $lista y ficha $ficha"; fallos=$((fallos + 1)) ;;
      esac
      ;;
    *)
      echo "   ✗ $ruta — $lista · $cuerpo"
      fallos=$((fallos + 1))
      ;;
  esac
done <<< "$SALIDA"

if [ -n "$SIN_LECTURA" ]; then
  echo "   (sin colección que leer, no se comprueban:$SIN_LECTURA)"
fi
if [ -n "$solo_ficha" ]; then
  echo "   (cubiertas SÓLO por la ficha inexistente, sin comprobar su contenido:$solo_ficha)"
fi

echo
[ "$fallos" -eq 0 ] || grita "$fallos lecturas fallaron SOBRE DATOS RESTAURADOS.
  Los bytes pueden estar perfectos y la capacidad no poder leerlos — es el defecto #82 exacto,
  y es lo único que este ensayo existe para cazar. Se comprobó levantando Api.Audit sobre un
  almacén ilegible: /health contesta 200 y la lectura 500. El respaldo NO sirve hasta que esto
  esté en cero."

# Y que hayan vuelto DATOS, no sólo doscientos. Un sistema recién instalado
# contesta 200 a las dieciocho colecciones, y un almacén que se lee a medias
# también — el conversor es tolerante, así que un campo con el tipo cambiado
# devuelve 200 con menos registros. Sin este corte, el ensayo pasaría en verde
# sobre un respaldo vacío, que es justo el que no se quiere tener.
[ "$total" -gt 0 ] || grita "todas las colecciones contestaron y TODAS están vacías.
  Eso es lo que contesta una instalación nueva, así que este ensayo no prueba nada: o el
  respaldo no llevaba datos, o se restauró sobre los volúmenes equivocados."

echo "✓ Restaurado y LEÍDO: $total registros devueltos por procesos nuevos."
echo "  Copia: $NOMBRE — versión $VERSION"
