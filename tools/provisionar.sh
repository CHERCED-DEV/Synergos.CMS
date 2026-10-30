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
#                    [--plantillas <fichero>] [--procesos <fichero>]
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
#   · Las plantillas se BUSCAN por clave antes de publicarse. Si la publicada
#     coincide con la declarada, no se hace nada. Si difiere —en el texto, o en
#     un marcador que el aviso no manda—, se publica la declarada: Api.Notifications
#     VERSIONA (#179), así que eso crea la versión siguiente y la vieja queda
#     consultable por su id, porque los envíos que salieron con ella la citan.
#     Lo declarado en el repo manda. `--verificar` dice en rojo lo que no
#     coincide, y no publica nada.

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
# Una línea por entrada, con los nueve campos SIEMPRE presentes —vacíos los que
# no apliquen— y 0x1F entre ellos. Ver el aviso de más abajo sobre por qué no es
# un tabulador.
#
# Los dos últimos son la AGENDA de un recurso y su huella (NP-2 de 008): si la
# entrada declara `opening`, `cancellationNoticeMinutes` o `slotMinutes`, el
# octavo es el cuerpo ENTERO de `POST /v1/resources/{id}/revise` —cupo, horario,
# plazo de aviso y franja—, en ASCII y en una línea como el de una plantilla, y
# el noveno, sus 16 primeros hexadecimales de sha256. Sin ninguno de los tres,
# los dos van vacíos y el recurso se registra como siempre: siempre abierto.
#
# La salida se fuerza a UTF-8 y a `\n`: el Python de Windows escribe `\r\n` en
# stdout, y `read` dejaba el `\r` pegado al último campo — `moneda` llegaba como
# «COP\r» (medido, #170).
leer_manifiesto() {
  "${PYTHON[@]}" - "$1" <<'PY'
import hashlib, json, sys
sys.stdout.reconfigure(encoding='utf-8', newline='\n')
for e in json.load(open(sys.argv[1], encoding='utf-8')):
    agenda = huella = ''
    if e.get('tipo') == 'recurso' and any(k in e for k in ('opening', 'cancellationNoticeMinutes', 'slotMinutes')):
        cuerpo = {'capacity': e.get('capacity', 1), 'opening': e.get('opening') or []}
        for k in ('cancellationNoticeMinutes', 'slotMinutes'):
            if k in e:
                cuerpo[k] = e[k]
        agenda = json.dumps(cuerpo, ensure_ascii=True, sort_keys=True, separators=(',', ':'))
        huella = hashlib.sha256(agenda.encode('ascii')).hexdigest()[:16]
    print('\x1f'.join([str(e.get(k, '')) for k in
                       ('tipo', 'subjectKind', 'subjectId', 'capacity',
                        'timeZoneId', 'amount', 'currency')] + [agenda, huella]))
PY
}

# ── La agenda de un recurso que ya está ──────────────────────────────────────
#
#   comparar <agenda-declarada> <fichero>  contra el recurso que devolvió la
#                                          capacidad: IGUAL · DIFIERE 0x1F
#                                          campos 0x1F id-del-recurso
#   fuera <fichero>                        cuántas reservas vigentes dejó fuera
#                                          una revisión (no se cancela ninguna)
#
# Compara lo que la revisión cambia —cupo, horario, plazo de aviso y franja—.
# El horario, como conjunto de (día, desde, hasta): ni el orden ni las
# mayúsculas del día ni «9:00» frente a «09:00» son una diferencia. Si lo
# fueran, cada corrida revisaría otra vez lo mismo.
agenda_py() {
  "${PYTHON[@]}" - "$@" <<'PY'
import json, sys
sys.stdout.reconfigure(encoding='utf-8', newline='\n')

def hora(h):
    try:
        partes = [int(x) for x in str(h).split(':')[:2]]
        return '%02d:%02d' % (partes[0], partes[1] if len(partes) > 1 else 0)
    except ValueError:
        return str(h)

def horario(o):
    return sorted((str(x.get('day', '')).lower(), hora(x.get('from', '')), hora(x.get('to', ''))) for x in (o or []))

if sys.argv[1] == 'comparar':
    deseada = json.loads(sys.argv[2])
    actual = json.load(open(sys.argv[3], encoding='utf-8'))
    distintos = []
    if int(actual.get('capacity') or 0) != int(deseada.get('capacity', 1)):
        distintos.append('capacity')
    if horario(actual.get('opening')) != horario(deseada.get('opening')):
        distintos.append('opening')
    if int(actual.get('cancellationNoticeMinutes') or 0) != int(deseada.get('cancellationNoticeMinutes', 0)):
        distintos.append('cancellationNoticeMinutes')
    if actual.get('slotMinutes') != deseada.get('slotMinutes'):
        distintos.append('slotMinutes')
    print('DIFIERE\x1f%s\x1f%s' % (' '.join(distintos), actual.get('id', '')) if distintos else 'IGUAL')
elif sys.argv[1] == 'fuera':
    print(len(json.load(open(sys.argv[2], encoding='utf-8')).get('outside') or []))
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

# ── El catálogo de procesos ──────────────────────────────────────────────────
#
# Las definiciones que Api.Workflow guarda como dato salen de UN fichero,
# `provisionar.procesos.json` (009, NP-1): el mismo que bff-gobierno monta como
# su catálogo y contra el que cruza sus pasos al arrancar. La v1 vivía en un
# heredoc de este guion y la v2 sólo en el fichero, así que un servidor nuevo
# quedaba con la v1 y el orquestador con la v2: la primera radicación por la
# puerta habría salido `definition_not_found`.
#
# Una línea por definición: clave 0x1F cuerpo-del-POST, o `!` 0x1F de-dónde
# 0x1F qué-pasa si la entrada no se puede publicar. El cuerpo, en ASCII y en una
# línea, por lo mismo que el de una plantilla (ver arriba).
procesos_py() {
  "${PYTHON[@]}" - "$1" <<'PY'
import json, re, sys
sys.stdout.reconfigure(encoding='utf-8', newline='\n')

CAMPOS = ('key', 'initialState', 'finalStates', 'transitions')

try:
    entradas = json.load(open(sys.argv[1], encoding='utf-8'))
except (OSError, ValueError) as e:
    print('!\x1f%s\x1fno se puede leer como JSON (%s)' % (sys.argv[1], e))
    sys.exit(0)

vistas = set()
for e in entradas:
    if not isinstance(e, dict) or set(e) <= {'_'}:
        continue
    clave = e.get('key') if isinstance(e.get('key'), str) else ''
    falta = [c for c in CAMPOS if not e.get(c)]
    if falta:
        print('!\x1f%s\x1ffaltan %s' % (clave or '?', ','.join(falta)))
    elif not re.fullmatch(r'[A-Za-z0-9._-]+', clave):
        print('!\x1f%s\x1fla clave no es valida' % clave)
    elif clave in vistas:
        print('!\x1f%s\x1festa dos veces' % clave)
    else:
        vistas.add(clave)
        print('%s\x1f%s' % (clave, json.dumps({c: e[c] for c in CAMPOS}, ensure_ascii=True, separators=(',', ':'))))
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
[{"tipo":"precio","subjectKind":"k","subjectId":"s","amount":320000,"currency":"COP"},
 {"tipo":"recurso","subjectKind":"r","subjectId":"x","capacity":2,"opening":[{"day":"Monday","from":"09:00","to":"10:00"}],"slotMinutes":60},
 {"tipo":"recurso","subjectKind":"r","subjectId":"y"}]
JSON
  fallos_prueba=0
  vistas=0
  while IFS=$'\x1f' read -r tipo kind id capacidad zona monto moneda agenda huella; do
    vistas=$((vistas + 1))
    case "$tipo:$id" in
      precio:s)
        [ "$capacidad" = ""       ] || { echo "AUTOPRUEBA capacidad=[$capacidad]"; fallos_prueba=1; }
        [ "$zona"      = ""       ] || { echo "AUTOPRUEBA zona=[$zona]";           fallos_prueba=1; }
        [ "$moneda"    = "COP"    ] || { echo "AUTOPRUEBA moneda=[$moneda]";       fallos_prueba=1; }
        [ "$monto"     = "320000" ] || { echo "AUTOPRUEBA monto=[$monto] — se esperaba 320000. Los campos se CORRIERON: un separador que bash considera whitespace colapsa las rachas, y la oferta acaba publicandose a cero."; fallos_prueba=1; }
        [ "$agenda$huella" = ""   ] || { echo "AUTOPRUEBA un precio no tiene agenda: [$agenda] [$huella]"; fallos_prueba=1; } ;;
      recurso:x)
        # La agenda es el cuerpo ENTERO de la revisión: con el cupo, y sin el plazo que no se declaró.
        [ "$agenda" = '{"capacity":2,"opening":[{"day":"Monday","from":"09:00","to":"10:00"}],"slotMinutes":60}' ] \
          || { echo "AUTOPRUEBA agenda=[$agenda]"; fallos_prueba=1; }
        [ "${#huella}" = "16" ] || { echo "AUTOPRUEBA huella de la agenda=[$huella]"; fallos_prueba=1; } ;;
      recurso:y)
        [ "$agenda$huella" = "" ] || { echo "AUTOPRUEBA un recurso sin agenda declarada la trae: [$agenda]"; fallos_prueba=1; } ;;
      *) echo "AUTOPRUEBA tipo=[$tipo] id=[$id]"; fallos_prueba=1 ;;
    esac
  done < <(leer_manifiesto "$TMP")
  rm -f "$TMP"
  # Sin esto la prueba pasaria en verde leyendo CERO lineas, que es justo lo que
  # deja un lector roto.
  [ "$vistas" = "3" ] || { echo "AUTOPRUEBA el lector devolvio $vistas lineas, se esperaba 3"; fallos_prueba=1; }

  # ── La agenda de un recurso que ya está: cada veredicto ─────────────────────
  #
  # El de IGUAL lleva el horario en otro orden, el día en minúsculas y «9:00»:
  # si eso fuera una diferencia, cada corrida revisaría otra vez el recurso.
  ACT="$(mktemp)"
  DECL='{"capacity":1,"cancellationNoticeMinutes":120,"opening":[{"day":"Monday","from":"09:00","to":"10:00"},{"day":"Monday","from":"11:00","to":"12:00"}],"slotMinutes":60}'
  agenda_es() {  # agenda_es <recurso-json> <esperado>
    local v
    printf '%s' "$1" > "$ACT"
    v="$(agenda_py comparar "$DECL" "$ACT" | tr '\037' '|')"
    [ "$v" = "$2" ] || { echo "AUTOPRUEBA agenda comparar dio [$v], se esperaba [$2] para $1"; fallos_prueba=1; }
  }
  agenda_es '{"id":"r1","capacity":1,"opening":[{"day":"monday","from":"11:00","to":"12:00"},{"day":"Monday","from":"9:00","to":"10:00"}],"cancellationNoticeMinutes":120,"slotMinutes":60}' "IGUAL"
  agenda_es '{"id":"r1","capacity":1,"opening":[],"cancellationNoticeMinutes":0,"slotMinutes":null}' "DIFIERE|opening cancellationNoticeMinutes slotMinutes|r1"
  agenda_es '{"id":"r2","capacity":3,"opening":[{"day":"Monday","from":"09:00","to":"10:00"},{"day":"Monday","from":"11:00","to":"12:00"}],"cancellationNoticeMinutes":120,"slotMinutes":30}' "DIFIERE|capacity slotMinutes|r2"
  printf '%s' '{"resource":{"id":"r1"},"outside":[{"id":"res-1"},{"id":"res-2"}]}' > "$ACT"
  [ "$(agenda_py fuera "$ACT")" = "2" ] || { echo "AUTOPRUEBA agenda fuera no cuenta las reservas que quedan fuera"; fallos_prueba=1; }
  rm -f "$ACT"

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

  # ── El catálogo de procesos: el lector, sin red ─────────────────────────────
  #
  # Una entrada que es sólo comentario no cuenta; una sin transiciones y una clave
  # repetida se dicen y no se publican; la buena sale entera, en una línea.
  TMPC="$(mktemp)"
  cat > "$TMPC" <<'JSON'
[{"_":"un comentario"},
 {"key":"p.uno","initialState":"a","finalStates":["b"],"transitions":[{"name":"ir","from":"a","to":"b","requiredRoles":["rol"]}]},
 {"key":"p.dos","initialState":"a","finalStates":["b"]},
 {"key":"p.uno","initialState":"a","finalStates":["b"],"transitions":[{"name":"ir","from":"a","to":"b"}]}]
JSON
  LEIDO="$(procesos_py "$TMPC" | tr '\037' '|')"
  ESPERADO='p.uno|{"key":"p.uno","initialState":"a","finalStates":["b"],"transitions":[{"name":"ir","from":"a","to":"b","requiredRoles":["rol"]}]}
!|p.dos|faltan transitions
!|p.uno|esta dos veces'
  [ "$LEIDO" = "$ESPERADO" ] || { echo "AUTOPRUEBA el catalogo de procesos se leyo asi:"; echo "$LEIDO"; echo "y se esperaba:"; echo "$ESPERADO"; fallos_prueba=1; }
  printf '%s' '[{"key":' > "$TMPC"
  # La ruta no se compara: en Git Bash, Python la recibe ya traducida a la de Windows.
  case "$(procesos_py "$TMPC" | tr '\037' '|')" in
    "!|"*"|no se puede leer como JSON"*) ;;
    *) echo "AUTOPRUEBA un catalogo que no es JSON no se dice: el guion no publicaria ninguna definicion y no avisaria"; fallos_prueba=1 ;;
  esac
  rm -f "$TMPC"

  [ "$fallos_prueba" = "0" ] || exit 1
  echo "OK: el manifiesto se lee con los campos alineados, tambien sin capacity ni timeZoneId."
  echo "OK: la agenda de un recurso se lee entera y se compara sin depender del orden ni del formato."
  echo "OK: las plantillas se leen en ASCII y en una linea, y el comparador da cada veredicto."
  echo "OK: el catalogo de procesos se lee entero y dice la entrada que no se puede publicar."
  exit 0
fi

VERIFICAR=0
MANIFIESTO="$(dirname "$0")/provisionar.recursos.json"
PLANTILLAS="$(dirname "$0")/provisionar.plantillas.json"
PROCESOS="$(dirname "$0")/provisionar.procesos.json"
WORKFLOW_URL="${SYNERGOS_WORKFLOW_URL:-http://api-workflow:8080}"
BOOKING_URL="${SYNERGOS_BOOKING_URL:-http://api-booking:8080}"
PRICING_URL="${SYNERGOS_PRICING_URL:-http://api-pricing:8080}"
NOTIFICATIONS_URL="${SYNERGOS_NOTIFICATIONS_URL:-http://api-notifications:8080}"
TRACKING_PREFIX="${SYNERGOS_TRACKING_PREFIX:-tracking}"

while [ $# -gt 0 ]; do
  case "$1" in
    --verificar) VERIFICAR=1; shift ;;
    --manifiesto) MANIFIESTO="$2"; shift 2 ;;
    --plantillas) PLANTILLAS="$2"; shift 2 ;;
    --procesos) PROCESOS="$2"; shift 2 ;;
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

# Y el catálogo de procesos, por lo mismo y con un lector más: bff-gobierno lo
# monta y no arranca sin él, así que un servidor sin el fichero tampoco tiene
# orquestador (009, NP-1).
if [ ! -f "$PROCESOS" ]; then
  echo "✗ no existe el catálogo de procesos $PROCESOS." >&2
  echo "  Ahí se declaran las definiciones que Api.Workflow guarda como dato —la del" >&2
  echo "  trámite de Gobierno, v1 y v2—. Sin él no se decide ningún expediente." >&2
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
# estado sale por separado del cuerpo: se necesita distinguir 201 —«se creó»— de
# 200 —«ya estaba»—, el 409 `key_taken` de una definición —«ya está»— de un 4xx
# de verdad, y un cuerpo no alcanza para eso.
pedir() {  # pedir GET|POST <url> [cuerpo] [llave-idem]
  local metodo="$1" url="$2" cuerpo="${3:-}" idem="${4:-}"
  local args=(--silent --show-error --max-time 30 --write-out '\n%{http_code}'
              --header "X-Synergos-Key: $LLAVE" --request "$metodo")
  [ -n "$idem" ]  && args+=(--header "Idempotency-Key: $idem")
  [ -n "$cuerpo" ] && args+=(--header 'Content-Type: application/json' --data "$cuerpo")
  curl "${args[@]}" "$url" 2>/dev/null || printf '\n000'
}
codigo() { printf '%s' "$1" | tail -n1; }

# ── 1. Las definiciones del trámite de Gobierno ──────────────────────────────
#
# Salen del catálogo de procesos (ver arriba): la v1 —que vivía aquí, en un
# heredoc— y la v2, con las claves que el fichero declara. Los ESTADOS son los
# slugs públicos del expediente, no nombres nuevos: es lo que hace que traducir
# de vuelta sea una lectura y no una segunda tabla.

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
while IFS=$'\x1f' read -r clave cuerpo motivo; do
  if [ "$clave" = "!" ]; then
    falla "catálogo de procesos, $cuerpo: $motivo"
    continue
  fi
  definicion "$clave" "$cuerpo"
done < <(procesos_py "$PROCESOS")
definicion "$TRACKING_PREFIX.shop"    "$(pipeline_json "$TRACKING_PREFIX.shop"    paid preparing shipped delivered)"
definicion "$TRACKING_PREFIX.travel"  "$(pipeline_json "$TRACKING_PREFIX.travel"  paid confirmed upcoming completed)"
definicion "$TRACKING_PREFIX.events"  "$(pipeline_json "$TRACKING_PREFIX.events"  paid confirmed attended)"
definicion "$TRACKING_PREFIX.academy" "$(pipeline_json "$TRACKING_PREFIX.academy" enrolled in-progress completed)"

# ── 3. Las plantillas de aviso ───────────────────────────────────────────────
#
# Se busca POR CLAVE recorriendo `GET /v1/templates` página a página: la
# capacidad no tiene «búscame por clave» y sólo sirve por id, que genera ella.
# La lista trae la versión VIGENTE de cada clave no retirada (#179). Una
# búsqueda que mirara sólo la primera página diría «falta» con la plantilla
# puesta en la segunda, y la corrida publicaría otra vez lo mismo.
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
    AUSENTE|DIFIERE|CHOCA)
      if [ "$VERIFICAR" = "1" ]; then
        case "$veredicto" in
          AUSENTE) falta "plantilla $clave" ;;
          DIFIERE) falta "plantilla $clave — la publicada difiere de la declarada en: $detalle. Correr sin --verificar publica la declarada como versión nueva." ;;
          # La que enseñaba el doc 09 ({cita}): cada aviso saldría rechazado.
          CHOCA) falla "plantilla $clave — la publicada usa $detalle y el aviso no lo manda: cada aviso saldría notifications.missing_placeholder. Correr sin --verificar publica la declarada como versión nueva." ;;
        esac
        return
      fi
      # La llave lleva la HUELLA del contenido, por lo mismo que la de un precio:
      # `SaveTemplate` mira el libro de idempotencia antes que nada, y con la
      # misma llave devolvería la versión vieja.
      r="$(pedir POST "$NOTIFICATIONS_URL/v1/templates" "$cuerpo" "provisionar:plantilla:$clave:$huella")"
      case "$(codigo "$r")" in
        201) if [ "$veredicto" = "AUSENTE" ]; then puesto "plantilla $clave"
             else puesto "plantilla $clave (versión nueva: la publicada $( [ "$veredicto" = CHOCA ] && echo "usaba $detalle" || echo "difería en $detalle"))"; fi ;;
        # 200: la clave ya decía exactamente esto (la publicó otro entre la
        # búsqueda y el POST).
        200) ok "plantilla $clave (ya estaba)" ;;
        # 409 `channel_change`: la publicada sale por otro canal, y eso es otra
        # plantilla. No se arregla publicando: se dice.
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
  ACTUAL="$(mktemp)"
  while IFS=$'\x1f' read -r tipo kind id capacidad zona monto moneda agenda huella; do
    [ -z "${tipo:-}" ] && continue
    case "$tipo" in
      recurso)
        r="$(pedir GET "$BOOKING_URL/v1/resources?subjectKind=$kind&subjectId=$id")"
        if [ "$(codigo "$r")" = "200" ]; then
          # Ya está. Sin agenda declarada, como siempre: no se toca. Con ella, la
          # de la capacidad tiene que ser ÉSA: un recurso registrado antes de
          # declararla —con horario vacío, siempre abierto— se REVISA (NP-2 de
          # 008). La llave lleva la huella de la agenda, como la de un precio la
          # del monto: cambiarla en el manifiesto revisa otra vez y repetirla no
          # hace nada. La revisión no cancela reservas: dice cuáles quedan fuera.
          if [ -z "$agenda" ]; then ok "recurso $kind/$id"; continue; fi
          printf '%s' "$r" | head -n-1 > "$ACTUAL"
          IFS=$'\x1f' read -r veredicto campos rid < <(agenda_py comparar "$agenda" "$ACTUAL")
          case "${veredicto:-}" in
            IGUAL) ok "recurso $kind/$id" ;;
            DIFIERE)
              if [ "$VERIFICAR" = "1" ]; then falta "recurso $kind/$id: su agenda no es la del manifiesto ($campos)"; continue; fi
              r="$(pedir POST "$BOOKING_URL/v1/resources/$rid/revise" "$agenda" "provisionar:revisar:$kind:$id:$huella")"
              case "$(codigo "$r")" in
                200)
                  printf '%s' "$r" | head -n-1 > "$ACTUAL"
                  puesto "recurso $kind/$id revisado ($campos)"
                  fuera="$(agenda_py fuera "$ACTUAL")"
                  [ "${fuera:-0}" = "0" ] || echo "  ⚠ $fuera reserva(s) vigente(s) de $kind/$id quedan fuera de la agenda nueva: no se cancela ninguna, decidilo vos."
                  ;;
                *) falla "recurso $kind/$id → revisar $(codigo "$r"): $(printf '%s' "$r" | head -n-1)" ;;
              esac
              ;;
            *) falla "recurso $kind/$id: no se pudo comparar su agenda con la del manifiesto" ;;
          esac
          continue
        fi
        if [ "$VERIFICAR" = "1" ]; then falta "recurso $kind/$id"; continue; fi
        # Sin agenda declarada, horario VACÍO = siempre abierto, que es lo
        # correcto para una noche de hotel: cruza la medianoche. Quien necesite
        # franjas las declara en el manifiesto (`opening`,
        # `cancellationNoticeMinutes`, `slotMinutes`) y se registra con ellas.
        if [ -n "$agenda" ]; then
          cuerpo="$(printf '{"subjectKind":"%s","subjectId":"%s","timeZoneId":"%s",%s' \
                    "$kind" "$id" "${zona:-America/Bogota}" "${agenda#\{}")"
        else
          cuerpo="$(printf '{"subjectKind":"%s","subjectId":"%s","capacity":%s,"timeZoneId":"%s","opening":[]}' \
                    "$kind" "$id" "${capacidad:-1}" "${zona:-America/Bogota}")"
        fi
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
  rm -f "$ACTUAL"

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
