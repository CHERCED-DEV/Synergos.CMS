#!/usr/bin/env bash
#
# El import de uSync de un servidor NUEVO — una vez, a mano, a propósito (#114).
#
# ─────────────────────────────────────────────────────────────────────────────
# POR QUÉ ESTO ES UN PASO Y NO UNA LÍNEA DEL COMPOSE.
#
# ADR 0008 fija que el schema vive en XML y que el import NO ocurre al arrancar:
# el default del paquete es `None` y nadie lo pisa. Estuvo tres documentos
# diciendo lo contrario —«se instala desatendido e importa … ítems … es lo que
# hace que no haya que correr el import a mano»— y era falso: medido contra una
# DB vacía, la app arranca con `uSync: Startup Complete 0ms`, o sea CERO ítems.
#
# La salida obvia —poner `uSync__Settings__ImportAtStartup=All` en el compose—
# se consideró y se DESCARTÓ, y la razón no es el ADR sino lo que pasa:
# `appsettings.json` tiene encendidos `ContentHandler` y `MediaHandler`, así que
# `All` re-importa `uSync/v9/Content/` EN CADA ARRANQUE. Un `restart` del
# contenedor, un despliegue, un reinicio del servidor — y lo que un editor
# publicó se revierte al contenido del repo, en silencio y sin error. Se cambia
# «el primer despliegue necesita un comando» por «cada reinicio es una vuelta
# atrás editorial», que es el peor de los dos y además del tipo que no se nota.
#
# Y `ImportAtStartup=Settings` —sólo el schema, que es lo que ADR 0008 deja como
# vía de escape con ADR sucesor— NO resuelve el problema que venía a resolver:
# deja el sitio con schema y sin contenido, o sea el mismo cartel de «No
# published content» en la portada.
#
# Así que el import se queda siendo un acto deliberado, y lo que cambia es que
# deja de ser un ritual de backoffice imposible en un VPS sin pantalla.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./importar-schema.sh [--compose <fichero>] [--espera <segundos>]
#
# Se corre EN EL SERVIDOR, con el stack ya levantado. Es idempotente: uSync
# compara y aplica lo que difiere, así que correrlo dos veces no duplica nada.
#
# Lo que hace: PARA el CMS, levanta un contenedor efímero con la misma imagen y
# los mismos volúmenes y con `ImportAtStartup=All` puesto SÓLO para él, y cuando
# el import termina lo borra y vuelve a arrancar el CMS. La imagen que sirve el
# sitio no cambia y sigue con el guardrail de ADR 0008 intacto.
#
# ⚠️ PARADA ANTES DE ARRANQUE, Y NO ES POR PRUDENCIA: la primera versión de este
# script importaba con el CMS ARRIBA, y se midió lo que pasa. **Dos Umbraco
# sobre la misma base se matan por MainDom**: el que entra se lo queda, el que
# servía el sitio se apaga solo —«Application is shutting down», exit 0, sin un
# solo error— y a los pocos segundos el import se cae con él. El síntoma es que
# el sitio se cayó durante un import que tampoco terminó, y nada en los logs
# dice por qué. Es la misma regla que el compose ya declara para las capacidades
# («una instancia, y parada antes de arranque»), que también vale para el CMS.

set -euo pipefail

COMPOSE_FILE="compose.prod.yml"
# El import medido en CI son ~74s (ADR 0128); el techo es holgado a propósito,
# porque un servidor pequeño tarda más y cortar un import a medias es peor que
# esperar.
ESPERA="${SYNERGOS_IMPORT_TIMEOUT:-900}"
while [ $# -gt 0 ]; do
  case "$1" in
    --compose) COMPOSE_FILE="$2"; shift 2 ;;
    --espera) ESPERA="$2"; shift 2 ;;
    *) echo "argumento desconocido: $1" >&2; exit 2 ;;
  esac
done

[ -f "$COMPOSE_FILE" ] || { echo "✗ no existe $COMPOSE_FILE — corré esto desde donde vive el compose" >&2; exit 1; }

COMPOSE="docker compose --file $COMPOSE_FILE"
LOG="$(mktemp)"

# Pase lo que pase, el CMS vuelve a levantarse. Sin esto, un import que falla a
# medias deja el sitio caído sin decirlo — y el operador se entera por la calle.
levantar_de_vuelta() {
  echo "── arrancando el CMS de vuelta…"
  $COMPOSE up --detach cms >/dev/null 2>&1 || true
}
trap levantar_de_vuelta EXIT

echo "── parando el CMS (dos Umbraco sobre la misma base se matan por MainDom)…"
$COMPOSE stop cms >/dev/null

echo "── importando el schema de uSync en un contenedor efímero…"

# `run --rm` y no `exec`: el que sirve el sitio arrancó sin ImportAtStartup y
# tiene que seguir así. Éste nace con la variable puesta, importa, y desaparece.
#
# `--no-deps` porque el import no habla con ninguna capacidad: sólo con la DB.
# Sin esto, `run` levantaría media pila para nada.
#
# El proceso NO termina solo —es un servidor web— así que se lee su salida hasta
# ver el resumen del import y ahí se corta. Esperar a que salga colgaría para
# siempre; esperar un tiempo fijo daría por bueno un import que no ocurrió.
set +e
timeout --signal=INT "$ESPERA" \
  $COMPOSE run --rm --no-deps --entrypoint "" \
    --env "uSync__Settings__ImportAtStartup=All" \
    --env "uSync__Settings__ExportOnSave=None" \
    --env "ASPNETCORE_URLS=http://127.0.0.1:8099" \
    cms \
    dotnet Synergos.CMS.Web.dll > "$LOG" 2>&1 &
IMPORT=$!

# Se vigila el fichero en vez de encadenar tuberías: con `tee | while … break`,
# el corte llega como SIGPIPE y el estado del import se pierde.
while kill -0 "$IMPORT" 2>/dev/null; do
  if grep --quiet "uSync: Startup Complete" "$LOG" 2>/dev/null; then
    break
  fi
  sleep 2
done
grep --extended-regexp "uSync Import:|uSync: Startup Complete| ERR\]" "$LOG" | sed 's/^/   /' || true
$COMPOSE rm --stop --force cms >/dev/null 2>&1 || true
set -e

# ── El veredicto ─────────────────────────────────────────────────────────────
#
# Los mismos tres criterios que `usync-rebuild-check.mjs` (ADR 0128), y en el
# mismo orden de severidad. El tercero es el que importa: un import que
# «termina» habiéndose saltado una carpeta entera parece verde y deja huecos.
RESUMEN="$(grep --only-matching 'uSync Import: [0-9]* handlers, processed [0-9]* items, [0-9]* changes' "$LOG" | tail -1 || true)"
ERRORES="$(grep --count ' ERR]' "$LOG" || true)"
# `-mindepth 2` y no `-name '*.config'` a secas: en la raíz de `uSync/v9` vive
# `usync.config`, que es la CONFIGURACIÓN de uSync y no un ítem que se importe.
# Contarlo da 897 contra 896 procesados y acusa de incompleto un import perfecto
# — medido, la primera corrida de este script falló justo por eso. Es lo mismo
# que `usync-rebuild-check.mjs` consigue con `git ls-files '…/**/*.config'`,
# que tampoco casa un fichero de la raíz; acá no se puede usar git porque en el
# servidor no hay clon.
ESPERADOS="$(find "$(dirname "$0")/../Synergos.CMS.Web/uSync/v9" -mindepth 2 -name '*.config' 2>/dev/null | wc -l | tr -d ' ')"

if [ -z "$RESUMEN" ]; then
  echo "✗ el import no llegó a terminar. El log completo está en $LOG"
  exit 1
fi

PROCESADOS="$(printf '%s' "$RESUMEN" | sed -n 's/.*processed \([0-9]*\) items.*/\1/p')"
echo "── $RESUMEN"

if [ "$ERRORES" != "0" ]; then
  echo "✗ el import terminó con $ERRORES líneas [ERR] — parece verde y dejó huecos. Log: $LOG"
  exit 1
fi

# El conteo esperado sólo se puede calcular si el árbol uSync está al lado (o
# sea, si esto se corre desde un clon). En el servidor puede no estarlo, y
# entonces no se inventa un número: se dice que no se pudo cruzar.
if [ "$ESPERADOS" -gt 0 ] 2>/dev/null; then
  if [ "$PROCESADOS" -lt "$ESPERADOS" ]; then
    echo "✗ procesó $PROCESADOS de $ESPERADOS ficheros .config — se saltó algo. Log: $LOG"
    exit 1
  fi
  echo "✓ $PROCESADOS ítems, 0 errores (hay $ESPERADOS ficheros .config en el árbol)"
else
  echo "✓ $PROCESADOS ítems, 0 errores (sin árbol uSync al lado: no se cruzó el conteo)"
fi

rm -f "$LOG"
echo
echo "── ahora la portada tiene que dejar de ser el cartel de Umbraco vacío."
echo "   Comprobalo con:  tools/humo-publico.sh <dominio> <sha>"
