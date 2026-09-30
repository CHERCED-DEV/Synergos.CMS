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
#
# ⚠️ Y UNA SEXTA QUE NO ESTABA EN NINGUNA LISTA: LA PLANTILLA DEL AVISO (#174).
#
# Cuando una compensación se rinde, el orquestador le pide a Api.Notifications
# la plantilla `bff.compensacion.colgada` POR CLAVE. Nada la creaba: los cuatro
# `Program.cs` decían «autorala a mano» y el doc 09 la documentaba con unos
# marcadores que el código ya no manda. En un servidor limpio el aviso que
# cierra el lazo salía `template_not_found` — justo el día que hay que avisar—
# y `--verificar` no lo veía, porque sólo miraba lo que él mismo sembraba. Hoy
# el texto vive en `provisionar.plantillas.json`, y sus marcadores los fija el
# CÓDIGO: `PlantillaDelAvisoTests` los cruza con lo que el aviso manda.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./provisionar.sh [--verificar] [--base <url>] [--manifiesto <fichero>]
#                    [--plantillas <fichero>]
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
#   · Las plantillas se BUSCAN por clave antes de publicarse, y la que ya está
#     NO se pisa: Api.Notifications no tiene cómo reescribir una plantilla viva
#     (ni PUT ni DELETE; un segundo POST contesta `key_taken`). Si la publicada
#     coincide con la declarada, no se hace nada. Si difiere sólo en el texto,
#     se DICE y se respeta. Si usa un marcador que el aviso no manda, es ROJO:
#     cada aviso saldría `missing_placeholder`, y eso no lo arregla esperar.

set -euo pipefail

# ── El intérprete ────────────────────────────────────────────────────────────
#
# ⚠️ UN `python3` QUE EXISTE NO ES UN `python3` QUE CORRE (#170). En Windows,
# `python3` es el alias de la Microsoft Store: está en el PATH, imprime «no se
# encontró Python» y sale 49 — aunque `python` 3.11 esté instalado al lado. El
# lector devolvía cero líneas y la autoprueba culpaba AL LECTOR, o sea mandaba a
# diagnosticar el fichero equivocado. Por eso se prueba cada candidato
# EJECUTÁNDOLO, en orden, y si ninguno corre se dice eso y no otra cosa.
PYTHON=()
elegir_python() {
  local c
  for c in python3 python "py -3"; do
    # `$c` sin comillas a propósito: `py -3` son dos palabras.
    # shellcheck disable=SC2086
    if $c -c 'import sys; sys.exit(0 if sys.version_info[0] == 3 else 1)' >/dev/null 2>&1; then
      read -r -a PYTHON <<< "$c"
      return 0
    fi
  done
  return 1
}
sin_python() {
  echo "✗ no hay un Python 3 que CORRA: se probaron python3, python y 'py -3'." >&2
  echo "  (En Windows, 'python3' suele ser el alias vacío de la Microsoft Store.)" >&2
  echo "  El lector del manifiesto no está roto: no se pudo ejecutar." >&2
}

# ── El manifiesto ────────────────────────────────────────────────────────────
#
# Una línea por entrada, con los siete campos SIEMPRE presentes —vacíos los que
# no apliquen— y 0x1F entre ellos. Ver el aviso de más abajo sobre por qué no es
# un tabulador.
#
# La salida se fuerza a UTF-8 y a `\n`: el Python de Windows escribe `\r\n` en
# stdout, y `read` dejaba el `\r` pegado al último campo — `moneda` llegaba como
# «COP\r» (medido, #170).
leer_manifiesto() {
  "${PYTHON[@]}" - "$1" <<'PY'
import json, sys
sys.stdout.reconfigure(encoding='utf-8', newline='\n')
for e in json.load(open(sys.argv[1], encoding='utf-8')):
    print('\x1f'.join(str(e.get(k, '')) for k in
                      ('tipo', 'subjectKind', 'subjectId', 'capacity',
                       'timeZoneId', 'amount', 'currency')))
PY
}

# ── Las plantillas ───────────────────────────────────────────────────────────
#
# Dos modos del MISMO programa, para que leer y comparar entiendan igual qué es
# una entrada:
#
#   leer <fichero>                         una línea por plantilla declarada:
#                                          clave 0x1F cuerpo-del-POST 0x1F huella
#                                          (o `!` 0x1F clave 0x1F qué-falta)
#   comparar <fichero> <clave> <página>    contra una página de GET /v1/templates:
#                                          IGUAL · DIFIERE 0x1F campos ·
#                                          CHOCA 0x1F marcadores · SIGUE 0x1F offset ·
#                                          AUSENTE · ROTA
#
# ⚠️ EL CUERPO SALE EN ASCII (`\u00f3` en vez de «ó») Y EN UNA SOLA LÍNEA. Viaja
# como argumento de `curl`, y un argumento no ASCII depende de la página de
# códigos de quien lo pasa — medido en Git Bash de Windows con `LANG` en UTF-8:
# «ó» le llegaba al servidor como el byte 0xF3 de cp1252, no como UTF-8. Y
# viaja por un `read` de líneas, así que un salto de línea crudo en el cuerpo
# partiría la entrada en dos. JSON escapa las dos cosas si se le pide, y la
# capacidad recibe exactamente el mismo texto.
#
# Lo que decide el veredicto son los MARCADORES, con el mismo patrón que
# `NotificationRules.Marcador` de Api.Notifications: `{nombre}`. Los declarados
# son los que el aviso manda —eso lo garantiza `PlantillaDelAvisoTests`—, así
# que una publicada que use otro es una plantilla que rechaza cada aviso.
plantillas_py() {
  "${PYTHON[@]}" - "$@" <<'PY'
import hashlib, json, re, sys
sys.stdout.reconfigure(encoding='utf-8', newline='\n')

CAMPOS = ('key', 'channel', 'subject', 'body')
MARCADOR = re.compile(r'\{(\w+)\}')

def declaradas(ruta):
    for e in json.load(open(ruta, encoding='utf-8')):
        if set(e) <= {'_'}:
            continue
        yield e

def marcadores(p):
    return set(MARCADOR.findall(p.get('subject') or '')) | set(MARCADOR.findall(p.get('body') or ''))

modo, ruta = sys.argv[1], sys.argv[2]

if modo == 'leer':
    vistas = set()
    for e in declaradas(ruta):
        clave = e.get('key') if isinstance(e.get('key'), str) else ''
        falta = [c for c in CAMPOS if not isinstance(e.get(c), str) or not e[c].strip()]
        if not falta and not re.fullmatch(r'[A-Za-z0-9._-]+', clave):
            falta = ['key']
        if not falta and clave in vistas:
            falta = ['duplicada']
        if falta:
            print('!\x1f%s\x1f%s' % (clave or '?', ','.join(falta)))
            continue
        vistas.add(clave)
        cuerpo = json.dumps({c: e[c] for c in CAMPOS}, ensure_ascii=True, sort_keys=True, separators=(',', ':'))
        print('\x1f'.join((clave, cuerpo, hashlib.sha256(cuerpo.encode('ascii')).hexdigest()[:16])))

elif modo == 'comparar':
    clave = sys.argv[3]
    deseada = next(e for e in declaradas(ruta) if e.get('key') == clave)
    pagina = json.load(open(sys.argv[4], encoding='utf-8'))
    items = pagina.get('items') or []
    for p in items:
        if p.get('key') != clave:
            continue
        ajenos = sorted(marcadores(p) - marcadores(deseada))
        if ajenos:
            print('CHOCA\x1f' + ' '.join('{%s}' % m for m in ajenos))
        else:
            distintos = [c for c in ('channel', 'subject', 'body')
                         if (p.get(c) or '') != deseada[c]
                         and not (c == 'channel' and (p.get(c) or '').lower() == deseada[c].lower())]
            print('DIFIERE\x1f' + ' '.join(distintos) if distintos else 'IGUAL')
        break
    else:
        if pagina.get('hasMore') and items:
            print('SIGUE\x1f%d' % (int(pagina.get('offset') or 0) + len(items)))
        elif pagina.get('hasMore'):
            print('ROTA')
        else:
            print('AUSENTE')
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
  # Sin intérprete, lo que sigue diría «el lector devolvió 0 líneas» y culparía al
  # lector. Se dice ANTES, y sale rojo igual: no poder comprobarlo no es un verde.
  if ! elegir_python; then
    sin_python
    echo "AUTOPRUEBA no se pudo ejecutar el lector: falta el intérprete, no el lector."
    exit 1
  fi
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

  # ── Las plantillas: el lector y el comparador, sin red ──────────────────────
  #
  # El fixture EXIGE las dos reglas del lector: un cuerpo con salto de línea (si
  # viajara crudo, la entrada se partiría en dos líneas) y con «í» (si no viajara
  # en ASCII, el argumento de curl dependería de la página de códigos). Y una
  # entrada que es sólo comentario, que no cuenta.
  TMPP="$(mktemp)"
  PAG="$(mktemp)"
  cat > "$TMPP" <<'JSON'
[{"_":"un comentario"},
 {"_":"nota","key":"k.x","channel":"Email","subject":"S {a}","body":"línea 1\nlínea 2 con \"comillas\" y {a} {b}"}]
JSON
  vistas=0
  while IFS=$'\x1f' read -r clave cuerpo huella; do
    vistas=$((vistas + 1))
    [ "$clave" = "k.x" ] || { echo "AUTOPRUEBA plantilla clave=[$clave]"; fallos_prueba=1; }
    [ "$cuerpo" = '{"body":"l\u00ednea 1\nl\u00ednea 2 con \"comillas\" y {a} {b}","channel":"Email","key":"k.x","subject":"S {a}"}' ] \
      || { echo "AUTOPRUEBA plantilla cuerpo=[$cuerpo] — tiene que salir en ASCII y en una línea"; fallos_prueba=1; }
    [ "${#huella}" = "16" ] || { echo "AUTOPRUEBA plantilla huella=[$huella]"; fallos_prueba=1; }
  done < <(plantillas_py leer "$TMPP")
  [ "$vistas" = "1" ] || { echo "AUTOPRUEBA el lector de plantillas devolvio $vistas lineas, se esperaba 1: un salto de linea en el cuerpo tiene que viajar escapado"; fallos_prueba=1; }

  # El comparador, con cada veredicto. El de CHOCA es la plantilla que el doc 09
  # enseñaba a crear: `{cita}` ya no lo manda nadie.
  veredicto() {  # veredicto <página-json> <esperado>
    local v
    printf '%s' "$1" > "$PAG"
    v="$(plantillas_py comparar "$TMPP" k.x "$PAG" | tr '\037' ' ')"
    [ "$v" = "$2" ] || { echo "AUTOPRUEBA comparar dio [$v], se esperaba [$2] para $1"; fallos_prueba=1; }
  }
  veredicto '{"items":[{"key":"k.x","channel":"email","subject":"S {a}","body":"línea 1\nlínea 2 con \"comillas\" y {a} {b}"}],"offset":0,"hasMore":false}' "IGUAL"
  veredicto '{"items":[{"key":"k.x","channel":"Email","subject":"Otro {a}","body":"{b}"}],"offset":0,"hasMore":false}' "DIFIERE subject body"
  veredicto '{"items":[{"key":"k.x","channel":"Email","subject":"S {cita}","body":"{a} {b}"}],"offset":0,"hasMore":false}' "CHOCA {cita}"
  veredicto '{"items":[{"key":"otra","channel":"Email","subject":"s","body":"b"}],"offset":3,"hasMore":true}' "SIGUE 4"
  veredicto '{"items":[],"offset":0,"hasMore":true}' "ROTA"
  veredicto '{"items":[],"offset":0,"hasMore":false}' "AUSENTE"
  rm -f "$TMPP" "$PAG"

  [ "$fallos_prueba" = "0" ] || exit 1
  echo "OK: el manifiesto se lee con los campos alineados, tambien sin capacity ni timeZoneId."
  echo "OK: las plantillas se leen en ASCII y en una linea, y el comparador da cada veredicto."
  exit 0
fi

VERIFICAR=0
MANIFIESTO="$(dirname "$0")/provisionar.recursos.json"
PLANTILLAS="$(dirname "$0")/provisionar.plantillas.json"
WORKFLOW_URL="${SYNERGOS_WORKFLOW_URL:-http://api-workflow:8080}"
BOOKING_URL="${SYNERGOS_BOOKING_URL:-http://api-booking:8080}"
PRICING_URL="${SYNERGOS_PRICING_URL:-http://api-pricing:8080}"
NOTIFICATIONS_URL="${SYNERGOS_NOTIFICATIONS_URL:-http://api-notifications:8080}"
GOB_DEFINITION="${SYNERGOS_GOB_DEFINITION:-gov.tramite}"
TRACKING_PREFIX="${SYNERGOS_TRACKING_PREFIX:-tracking}"

while [ $# -gt 0 ]; do
  case "$1" in
    --verificar) VERIFICAR=1; shift ;;
    --manifiesto) MANIFIESTO="$2"; shift 2 ;;
    --plantillas) PLANTILLAS="$2"; shift 2 ;;
    --base) WORKFLOW_URL="$2"; BOOKING_URL="$2"; PRICING_URL="$2"; NOTIFICATIONS_URL="$2"; shift 2 ;;
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

# Y las plantillas, por la misma razón y con una diferencia: acá `[]` NO es un
# estado legítimo del repo —el aviso de compensación colgada la pide siempre—,
# pero sí lo es de quien corra esto con su propio fichero, así que se admite.
if [ ! -f "$PLANTILLAS" ]; then
  echo "✗ no existe el fichero de plantillas $PLANTILLAS." >&2
  echo "  Ahí se declara el texto de los avisos que el código pide por clave a" >&2
  echo "  Api.Notifications. Sin él, el aviso de una compensación colgada sale" >&2
  echo "  notifications.template_not_found el día que hace falta." >&2
  exit 1
fi

# Y el intérprete que lo lee, por la misma razón: sin él se publicaban las
# definiciones y el paso 3 no leía nada.
if ! elegir_python; then
  sin_python
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

# ── 3. Las plantillas de aviso ───────────────────────────────────────────────
#
# Se busca POR CLAVE recorriendo `GET /v1/templates` página a página: la
# capacidad no tiene «búscame por clave» y sólo sirve por id, que genera ella.
# Una búsqueda que mirara sólo la primera página diría «falta» con la plantilla
# puesta en la segunda, y el POST de después contestaría `key_taken`.
PAGINA="$(mktemp)"
trap 'rm -f "$PAGINA"' EXIT

plantilla() {  # plantilla <clave> <cuerpo-del-POST> <huella>
  local clave="$1" cuerpo="$2" huella="$3" desde=0 r veredicto="" detalle=""
  while :; do
    r="$(pedir GET "$NOTIFICATIONS_URL/v1/templates?offset=$desde&limit=500")"
    if [ "$(codigo "$r")" != "200" ]; then
      falla "plantilla $clave → no se pudo consultar Api.Notifications ($(codigo "$r"))"
      return
    fi
    printf '%s' "$r" | head -n-1 > "$PAGINA"
    IFS=$'\x1f' read -r veredicto detalle < <(plantillas_py comparar "$PLANTILLAS" "$clave" "$PAGINA") || true
    [ "$veredicto" = "SIGUE" ] || break
    desde="$detalle"
  done

  case "$veredicto" in
    IGUAL) ok "plantilla $clave" ;;
    # Se respeta, y se DICE. Sus marcadores son de los que el aviso manda, así
    # que el aviso sale — con otro texto. Pisarla no se puede, y aunque se
    # pudiera, sería borrar lo que alguien ajustó a mano en el servidor.
    DIFIERE)
      echo "≠ plantilla $clave — la publicada difiere de la declarada en: $detalle."
      echo "    Se respeta: Api.Notifications no reescribe una plantilla viva. Sus marcadores"
      echo "    son de los que el aviso manda, así que el aviso sale, con el texto publicado."
      ;;
    CHOCA)
      falla "plantilla $clave — la publicada usa $detalle y el aviso no lo manda: cada aviso saldría notifications.missing_placeholder. Api.Notifications no reescribe ni borra una plantilla viva (no hay PUT ni DELETE): hay que retirarla de su almacén y volver a correr esto."
      ;;
    AUSENTE)
      if [ "$VERIFICAR" = "1" ]; then falta "plantilla $clave"; return; fi
      # La llave lleva la HUELLA del contenido, por lo mismo que la de un precio:
      # `SaveTemplate` mira el libro de idempotencia antes que nada.
      r="$(pedir POST "$NOTIFICATIONS_URL/v1/templates" "$cuerpo" "provisionar:plantilla:$clave:$huella")"
      case "$(codigo "$r")" in
        200|201) puesto "plantilla $clave" ;;
        # La publicó otro entre la búsqueda y el POST: «ya está», como una definición.
        409) if printf '%s' "$r" | grep --quiet 'key_taken'; then
               ok "plantilla $clave (ya estaba)"
             else
               falla "plantilla $clave → 409: $(printf '%s' "$r" | head -n-1)"
             fi ;;
        *)   falla "plantilla $clave → $(codigo "$r"): $(printf '%s' "$r" | head -n-1)" ;;
      esac
      ;;
    ROTA) falla "plantilla $clave → Api.Notifications dijo que hay más páginas y no devolvió filas" ;;
    *)    falla "plantilla $clave → no se pudo comparar la publicada con la declarada" ;;
  esac
}

echo "── plantillas de aviso"
while IFS=$'\x1f' read -r clave cuerpo huella; do
  [ -z "${clave:-}" ] && continue
  if [ "$clave" = "!" ]; then
    falla "una plantilla de $PLANTILLAS no se puede publicar: $cuerpo (le falta: $huella)"
    continue
  fi
  plantilla "$clave" "$cuerpo" "$huella"
done < <(plantillas_py leer "$PLANTILLAS")

# ── 4. Los recursos y precios POR ENTIDAD ────────────────────────────────────
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
