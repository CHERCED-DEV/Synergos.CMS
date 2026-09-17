#!/usr/bin/env bash
#
# El estado que las capacidades EXIGEN, publicado desde un sitio (#114).
#
# ─────────────────────────────────────────────────────────────────────────────
# POR QUÉ ESTO EXISTÍA SÓLO COMO PROSA, Y POR QUÉ ESO ERA CARO.
#
# Cinco pasos obligatorios vivían escritos en los comentarios de `.env.example`,
# marcados por el propio repo como «OJO, paso de DESPLIEGUE que no es código», y
# ninguno aparecía en el documento titulado «lo que hace el arquitecto a mano,
# una sola vez». `grep -rn "v1/resources\|v1/prices\|v1/definitions" tools/`
# devolvía UNA línea, y era un comentario.
#
# Lo que pasa cuando falta: NADA, al arrancar. Los servicios levantan sanos,
# contestan `/health`, pasan la prueba de humo — y la primera persona que
# intenta agendar una cita, avanzar un pedido o decidir un expediente se lleva
# un rechazo. Los `Rejection` están BIEN hechos (`definition_not_found` dice con
# todas las letras que es un paso de despliegue); el hueco es que se descubre
# tarde, y por eso existe `--verificar`.
#
# ⚠️ TRES DE LOS CINCO SON POR ENTIDAD, Y ESO NO ES BOOTSTRAP.
#
# Un recurso de `Api.Booking` por cada médico. Otro por cada inmueble que acepte
# visitas. Un precio y un recurso por cada oferta de viaje. Eso no se siembra
# una vez: **crece con el catálogo**, cada vez que alguien da de alta un médico
# o publica un inmueble. Un script que los siembre y se vaya deja el problema
# intacto a partir del segundo médico.
#
# Lo que este script hace con ellos, deliberadamente, es RECONCILIAR: lee un
# manifiesto (`tools/provisionar.recursos.json`), mira qué hay publicado, y
# publica lo que falte. Correrlo después de dar altas es correcto y barato.
#
# **El disparador para que deje de ser esto:** el día que el catálogo salga de
# Umbraco en vez de de los stubs, registrar el recurso pasa a ser un seam del
# ALTA —quien publica un inmueble registra su recurso— y este script se queda
# sólo con las definiciones. Mientras el catálogo sea de demo, un manifiesto es
# más honesto que un seam que nadie dispara.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./provisionar.sh [--verificar] [--base <url>] [--manifiesto <fichero>]
#
#   --verificar   no escribe nada: dice qué falta y sale 1 si falta algo.
#
# La llave compartida se lee de SYNERGOS_API_KEY. Las URLs de cada capacidad se
# resuelven contra --base (por defecto los nombres de servicio del compose, o
# sea: esto se corre DENTRO de la red de Docker o con un túnel).
#
# ES IDEMPOTENTE, Y NO POR CASUALIDAD:
#   · Las definiciones se CONSULTAN antes de publicarse. `Api.Workflow` se niega
#     a reescribir una definición viva —cambiarle las transiciones a instancias
#     en marcha las dejaría en estados imposibles— y contesta `key_taken`. Eso
#     es «ya está», no «falló», y confundirlos es lo que hace que un script de
#     despliegue se vuelva imposible de correr dos veces.
#   · Los recursos se buscan por sujeto ANTES de crearse, porque
#     `RegisterResource` NO reusa el id por sujeto: dos POST con llaves distintas
#     dan dos recursos para el mismo médico, y el cupo queda partido en dos sin
#     que nada falle. Además se manda una `Idempotency-Key` derivada del sujeto,
#     que es la segunda red.
#   · Los precios son upsert por sujeto —`SetPrice` reusa el id— PERO su llave
#     de idempotencia lleva la HUELLA DEL MONTO, no sólo el sujeto. Sin eso el
#     precio queda congelado en el primero que se publicó: la capacidad mira el
#     libro antes que nada y devuelve el anterior, contestando 200. Republicar
#     lo mismo sigue sin cambiar nada; cambiarlo, ahora sí se ve.

set -euo pipefail

# ── El manifiesto ────────────────────────────────────────────────────────────
#
# Una línea por entrada, con los siete campos SIEMPRE presentes —vacíos los que
# no apliquen— y 0x1F entre ellos. Ver el aviso de más abajo sobre por qué no es
# un tabulador.
leer_manifiesto() {
  python3 - "$1" <<'PY'
import json, sys
for e in json.load(open(sys.argv[1], encoding='utf-8')):
    print('\x1f'.join(str(e.get(k, '')) for k in
                      ('tipo', 'subjectKind', 'subjectId', 'capacity',
                       'timeZoneId', 'amount', 'currency')))
PY
}

# ── La autoprueba ────────────────────────────────────────────────────────────
#
# `--autoprueba` corre el lector sobre un manifiesto de mentira y comprueba que
# los campos caen donde deben. No habla con nadie, así que un gate la puede
# ejecutar en CI sin levantar una capacidad — y ejecuta EL CÓDIGO DE VERDAD, no
# una copia suya dentro de un test.
#
# El fixture lleva el caso que falló: una entrada `precio`, que NO tiene
# `capacity` ni `timeZoneId`. Con todos los campos llenos, el tabulador y el
# 0x1F dan el mismo resultado y la prueba pasaría en verde con el defecto puesto
# — es la regla 7 del repo hermano: el dato de prueba tiene que EXIGIR la regla.
if [ "${1:-}" = "--autoprueba" ]; then
  TMP="$(mktemp)"
  cat > "$TMP" <<'JSON'
[{"tipo":"precio","subjectKind":"k","subjectId":"s","amount":320000,"currency":"COP"}]
JSON
  fallos_prueba=0
  vistas=0
  while IFS=$'\x1f' read -r tipo kind id capacidad zona monto moneda; do
    vistas=$((vistas + 1))
    [ "$tipo"      = "precio" ] || { echo "AUTOPRUEBA tipo=[$tipo]";           fallos_prueba=1; }
    [ "$capacidad" = ""       ] || { echo "AUTOPRUEBA capacidad=[$capacidad]"; fallos_prueba=1; }
    [ "$zona"      = ""       ] || { echo "AUTOPRUEBA zona=[$zona]";           fallos_prueba=1; }
    [ "$moneda"    = "COP"    ] || { echo "AUTOPRUEBA moneda=[$moneda]";       fallos_prueba=1; }
    [ "$monto"     = "320000" ] || { echo "AUTOPRUEBA monto=[$monto] — se esperaba 320000. Los campos se CORRIERON: un separador que bash considera whitespace colapsa las rachas, y la oferta acaba publicandose a cero."; fallos_prueba=1; }
  done < <(leer_manifiesto "$TMP")
  rm -f "$TMP"
  # Sin esto la prueba pasaria en verde leyendo CERO lineas, que es justo lo que
  # deja un lector roto.
  [ "$vistas" = "1" ] || { echo "AUTOPRUEBA el lector devolvio $vistas lineas, se esperaba 1"; fallos_prueba=1; }
  [ "$fallos_prueba" = "0" ] || exit 1
  echo "OK: el manifiesto se lee con los campos alineados, tambien sin capacity ni timeZoneId."
  exit 0
fi

VERIFICAR=0
MANIFIESTO="$(dirname "$0")/provisionar.recursos.json"
WORKFLOW_URL="${SYNERGOS_WORKFLOW_URL:-http://api-workflow:8080}"
BOOKING_URL="${SYNERGOS_BOOKING_URL:-http://api-booking:8080}"
PRICING_URL="${SYNERGOS_PRICING_URL:-http://api-pricing:8080}"
GOB_DEFINITION="${SYNERGOS_GOB_DEFINITION:-gov.tramite}"
TRACKING_PREFIX="${SYNERGOS_TRACKING_PREFIX:-tracking}"

while [ $# -gt 0 ]; do
  case "$1" in
    --verificar) VERIFICAR=1; shift ;;
    --manifiesto) MANIFIESTO="$2"; shift 2 ;;
    --base) WORKFLOW_URL="$2"; BOOKING_URL="$2"; PRICING_URL="$2"; shift 2 ;;
    *) echo "argumento desconocido: $1" >&2; exit 2 ;;
  esac
done

LLAVE="${SYNERGOS_API_KEY:?falta SYNERGOS_API_KEY — es la llave compartida entre servicios}"

# ⚠️ EL MANIFIESTO SE COMPRUEBA ANTES DE ESCRIBIR NADA, y ausente es un ERROR.
# Antes esto se miraba a mitad del camino y seguía adelante avisando: publicaba
# las cinco definiciones, se saltaba los recursos y los precios, y terminaba
# diciendo «✓ el estado que las capacidades exigen está publicado» — un verde
# con la mitad del trabajo sin hacer. Y era alcanzable de verdad: el despliegue
# copiaba `provisionar.sh` al servidor y NO su manifiesto (#114).
#
# «No hay entidades todavía» se escribe `[]`. Decirlo no es lo mismo que que el
# fichero falte, y los dos casos se pueden distinguir, así que se distinguen.
if [ ! -f "$MANIFIESTO" ]; then
  echo "✗ no existe el manifiesto $MANIFIESTO." >&2
  echo "  Ahí se declaran los recursos por médico / inmueble / oferta y sus precios." >&2
  echo "  Si de verdad no hay ninguno todavía, escribí un fichero con []." >&2
  exit 1
fi

faltan=0
fallos=0
ok()     { echo "✓ $1"; }
falta()  { echo "· FALTA  $1"; faltan=$((faltan + 1)); }
puesto() { echo "+ puesto $1"; }
falla()  { echo "✗ $1"; fallos=$((fallos + 1)); }

# `curl` con la llave y, cuando escribe, con llave de idempotencia. El código de
# estado sale por separado del cuerpo: se necesita distinguir 409 `key_taken`
# —«ya está»— de un 4xx de verdad, y un cuerpo no alcanza para eso.
pedir() {  # pedir GET|POST <url> [cuerpo] [llave-idem]
  local metodo="$1" url="$2" cuerpo="${3:-}" idem="${4:-}"
  local args=(--silent --show-error --max-time 30 --write-out '\n%{http_code}'
              --header "X-Synergos-Key: $LLAVE" --request "$metodo")
  [ -n "$idem" ]  && args+=(--header "Idempotency-Key: $idem")
  [ -n "$cuerpo" ] && args+=(--header 'Content-Type: application/json' --data "$cuerpo")
  curl "${args[@]}" "$url" 2>/dev/null || printf '\n000'
}
codigo() { printf '%s' "$1" | tail -n1; }

# ── 1. La definición del trámite de Gobierno ─────────────────────────────────
#
# Los ESTADOS son los slugs públicos del expediente, no nombres nuevos: es lo
# que hace que traducir de vuelta sea una lectura y no una segunda tabla.
# Copiado de `.env.example`, que es donde vivía.
DEF_GOB=$(cat <<JSON
{"key":"$GOB_DEFINITION","initialState":"submitted",
 "finalStates":["approved","rejected"],
 "transitions":[
   {"name":"approve","from":"submitted","to":"approved","requiredRoles":["funcionario"]},
   {"name":"approve","from":"in-review","to":"approved","requiredRoles":["funcionario"]},
   {"name":"approve","from":"info-requested","to":"approved","requiredRoles":["funcionario"]},
   {"name":"reject","from":"submitted","to":"rejected","requiredRoles":["funcionario"]},
   {"name":"request-info","from":"submitted","to":"info-requested","requiredRoles":["funcionario"]}]}
JSON
)

# ── 2. Los CUATRO pipelines de seguimiento ───────────────────────────────────
#
# Una definición POR DOMINIO, y no una compartida: los nombres de estado se
# repiten entre pipelines (`paid` en tres, `completed` en dos), así que una sola
# leería la etapa de un dominio contra el pipeline de otro y «enviado» sería
# «matriculado» sin que nada fallara.
#
# Cada transición se llama COMO LA ETAPA DESTINO: un nombre propio obligaría a
# una segunda tabla de este lado para traducirlo, que es justo lo que el
# cableado de la HU #46 quita.
pipeline_json() {  # pipeline_json <clave> <etapa1> <etapa2> …
  local clave="$1"; shift
  local inicial="$1"; shift
  local anterior="$inicial" final="" transiciones=""
  for etapa in "$@"; do
    [ -n "$transiciones" ] && transiciones="$transiciones,"
    transiciones="$transiciones{\"name\":\"$etapa\",\"from\":\"$anterior\",\"to\":\"$etapa\"}"
    anterior="$etapa"; final="$etapa"
  done
  printf '{"key":"%s","initialState":"%s","finalStates":["%s"],"transitions":[%s]}' \
         "$clave" "$inicial" "$final" "$transiciones"
}

definicion() {  # definicion <clave> <json>
  local clave="$1" cuerpo="$2"
  local r; r="$(pedir GET "$WORKFLOW_URL/v1/definitions/$clave")"
  if [ "$(codigo "$r")" = "200" ]; then ok "definición $clave"; return; fi

  if [ "$VERIFICAR" = "1" ]; then falta "definición $clave"; return; fi

  r="$(pedir POST "$WORKFLOW_URL/v1/definitions" "$cuerpo" "provisionar:def:$clave")"
  case "$(codigo "$r")" in
    200|201) puesto "definición $clave" ;;
    # `key_taken` es «ya está», no «falló» — y es lo que contesta la capacidad
    # cuando alguien la publicó con otra llave de idempotencia. Tratarlo como
    # error haría que este script no se pudiera correr dos veces.
    409)     ok "definición $clave (ya estaba)" ;;
    *)       falla "definición $clave → $(codigo "$r"): $(printf '%s' "$r" | head -n-1)" ;;
  esac
}

echo "── definiciones de proceso"
definicion "$GOB_DEFINITION" "$DEF_GOB"
definicion "$TRACKING_PREFIX.shop"    "$(pipeline_json "$TRACKING_PREFIX.shop"    paid preparing shipped delivered)"
definicion "$TRACKING_PREFIX.travel"  "$(pipeline_json "$TRACKING_PREFIX.travel"  paid confirmed upcoming completed)"
definicion "$TRACKING_PREFIX.events"  "$(pipeline_json "$TRACKING_PREFIX.events"  paid confirmed attended)"
definicion "$TRACKING_PREFIX.academy" "$(pipeline_json "$TRACKING_PREFIX.academy" enrolled in-progress completed)"

# ── 3. Los recursos y precios POR ENTIDAD ────────────────────────────────────
echo "── recursos y precios por entidad"

  # Se lee con `python3` y no con `jq`: el servidor lo trae de fábrica y `jq` no,
  # y `bootstrap-servidor.sh` no lo instala. Un script de despliegue que exige un
  # paquete que el bootstrap no pone es un paso no documentado más.
  #
  # ⚠️ EL SEPARADOR ES 0x1F Y NO UN TABULADOR, Y ESO COSTÓ UN DEFECTO CALLADO.
  # Bash trata espacio, TABULADOR y salto de línea como «IFS whitespace»: aunque
  # se pida `IFS=$'\t'`, una RACHA de tabuladores cuenta como UN separador. Una
  # entrada de tipo `precio` no lleva `capacity` ni `timeZoneId`, así que su
  # línea trae tres tabuladores seguidos y los campos se CORREN dos puestos: el
  # monto aterrizaba en `capacidad`, la moneda en `zona`, y `monto` llegaba
  # vacío. Con el `${monto:-0}` que había, la oferta se publicaba a CERO —201
  # Created, «+ puesto precio», y cada viaje gratis—. Lo destapó correr esto
  # contra las tres capacidades vivas y mirar el almacén, no leer el script.
  # 0x1F (unit separator) no es IFS whitespace: las rachas NO se colapsan.
  while IFS=$'\x1f' read -r tipo kind id capacidad zona monto moneda; do
    [ -z "${tipo:-}" ] && continue
    case "$tipo" in
      recurso)
        r="$(pedir GET "$BOOKING_URL/v1/resources?subjectKind=$kind&subjectId=$id")"
        if [ "$(codigo "$r")" = "200" ]; then ok "recurso $kind/$id"; continue; fi
        if [ "$VERIFICAR" = "1" ]; then falta "recurso $kind/$id"; continue; fi
        # Horario VACÍO = siempre abierto, que es lo correcto para una noche de
        # hotel: cruza la medianoche. Quien necesite franjas las pone en el
        # manifiesto el día que haga falta.
        cuerpo="$(printf '{"subjectKind":"%s","subjectId":"%s","capacity":%s,"timeZoneId":"%s","opening":[]}' \
                  "$kind" "$id" "${capacidad:-1}" "${zona:-America/Bogota}")"
        r="$(pedir POST "$BOOKING_URL/v1/resources" "$cuerpo" "provisionar:res:$kind:$id")"
        case "$(codigo "$r")" in
          200|201) puesto "recurso $kind/$id" ;;
          *)       falla "recurso $kind/$id → $(codigo "$r"): $(printf '%s' "$r" | head -n-1)" ;;
        esac
        ;;
      precio)
        # Un monto ausente o no numerico NO cae a cero: se rechaza. Es la otra
        # mitad del defecto de arriba — con el corrimiento arreglado, a un
        # manifiesto al que se le olvide `amount` le seguiria publicando la
        # oferta gratis, y eso no falla en ninguna parte hasta que alguien
        # compra.
        if ! printf '%s' "${monto:-}" | grep --quiet --extended-regexp '^[0-9]+([.][0-9]+)?$'; then
          falla "precio $kind/$id -> el manifiesto no trae un 'amount' numerico (llego: '${monto:-}'). No se publica a cero: una oferta a cero se vende gratis y no falla en ninguna parte."
          continue
        fi
        if [ "$VERIFICAR" = "1" ]; then
          # `Api.Pricing` sólo sabe buscar un precio POR ID, y el id lo genera
          # ella. Así que desde fuera no hay forma de preguntar «¿tiene precio
          # esta oferta?» sin cotizarla. Se dice, en vez de inventar un verde.
          echo "? precio $kind/$id — no comprobable: Api.Pricing no lista por sujeto"
          continue
        fi
        cuerpo="$(printf '{"subjectKind":"%s","subjectId":"%s","amount":{"amount":%s,"currency":"%s"},"taxRateBasisPoints":0}' \
                  "$kind" "$id" "$monto" "${moneda:-COP}")"
        # ⚠️ LA LLAVE LLEVA LA HUELLA DEL VALOR, y sin eso el precio se congela.
        # `SetPrice` mira el libro de idempotencia ANTES de nada y devuelve el
        # precio anterior si la llave ya se usó. Con una llave derivada sólo del
        # sujeto, cambiar el monto en el manifiesto y volver a correr esto NO
        # HACE NADA: contesta 200, dice «+ puesto precio», y sigue vendiendo al
        # precio viejo. Lo destapó arreglar el corrimiento de campos de arriba y
        # ver que el precio SEGUÍA en cero contra la capacidad viva. Es
        # `feedback_seeded_content_needs_fingerprint` sobre una tarifa: con la
        # huella dentro, republicar lo mismo sigue siendo un no-op y cambiarlo se
        # ve.
        r="$(pedir POST "$PRICING_URL/v1/prices" "$cuerpo" "provisionar:precio:$kind:$id:$monto:${moneda:-COP}")"
        case "$(codigo "$r")" in
          200|201) puesto "precio $kind/$id" ;;
          *)       falla "precio $kind/$id → $(codigo "$r"): $(printf '%s' "$r" | head -n-1)" ;;
        esac
        ;;
      *) falla "tipo desconocido en el manifiesto: $tipo" ;;
    esac
  done < <(leer_manifiesto "$MANIFIESTO")

echo
if [ "$fallos" -gt 0 ]; then
  echo "✗ $fallos cosas fallaron al publicarse."
  exit 1
fi
if [ "$VERIFICAR" = "1" ] && [ "$faltan" -gt 0 ]; then
  echo "✗ faltan $faltan. Corré esto mismo sin --verificar."
  exit 1
fi
echo "✓ el estado que las capacidades exigen está publicado."
