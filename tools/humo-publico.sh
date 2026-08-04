#!/usr/bin/env bash
#
# El humo de un despliegue, contra la URL PÚBLICA (HU #19).
#
# ─────────────────────────────────────────────────────────────────────────────
# ESTE FICHERO EXISTE SEPARADO POR UNA RAZÓN: PARA QUE UN GATE PUEDA LEERLO.
#
# El fallo más fácil de escribir en un despliegue —y el más difícil de notar—
# es un humo que apunta a `localhost`. Pasa SIEMPRE: contra el propio runner no
# hay nada que pueda fallar, así que el action se pone verde con el sitio
# caído. Y un despliegue verde con el sitio caído es peor que uno rojo, porque
# nadie lo va a mirar.
#
# Al vivir en su propio fichero, `DeployPipelineTests` puede exigir que acá no
# aparezcan `localhost` ni `127.0.0.1`, y que la URL venga de fuera. Metido
# dentro del YAML del workflow, ese gate tendría que leer YAML y distinguir el
# humo del resto de pasos — o sea, no existiría.
# ─────────────────────────────────────────────────────────────────────────────
#
#   ./humo-publico.sh <dominio> <sha-esperado>
#
# Corre en el runner de GitHub, no en el servidor: así atraviesa lo mismo que
# atraviesa un visitante —DNS, Cloudflare, el certificado, Caddy— y no sólo el
# último salto.

set -euo pipefail

DOMINIO="${1:?falta el dominio público}"
ESPERADO="${2:?falta el SHA que debería estar contestando}"
BASE="https://${DOMINIO}"

fallo=0
paso() { echo "✓ $1"; }
falla() { echo "✗ $1"; fallo=1; }

# ── 1. Una página de verdad, no sólo un endpoint de salud ────────────────────
#
# `/health` puede contestar con el sitio inservible: es un endpoint que no toca
# ni el contenido, ni las plantillas, ni la base de datos. La portada sí.
echo "── la portada"
# `|| true` y no `|| echo 000`: cuando curl falla YA imprimió `000` por el
# --write-out, así que el echo lo concatenaba y salía `000000`. Con un código
# real de por medio —`200` más un fallo posterior— la comparación de abajo
# habría dado `200000` y el humo habría gritado por una portada que estaba bien.
CODIGO="$(curl --silent --show-error --location --max-time 30 \
          --output /tmp/portada.html --write-out '%{http_code}' "$BASE/" || true)"

if [ "$CODIGO" = "200" ]; then
  paso "GET / → 200 ($(wc -c < /tmp/portada.html) bytes)"
else
  falla "GET / → $CODIGO (se esperaba 200)"
fi

# Un 200 que devuelve la página de error de Umbraco sigue siendo un 200. Se
# mira que haya HTML de verdad y no una excusa.
if grep --quiet --ignore-case "<html" /tmp/portada.html 2>/dev/null; then
  paso "la respuesta es HTML"
else
  falla "la respuesta no parece HTML — puede ser una página de error del proxy"
fi

# ── 2. La versión que contesta es la que se acaba de subir ───────────────────
#
# EL CRITERIO QUE SEPARA "responde" DE "se actualizó". Un reinicio que falla en
# silencio deja viva la versión anterior: el sitio responde, el humo pasa, y el
# despliegue se da por bueno habiendo desplegado nada.
#
# Sin `--fail` a propósito: `/_health` devuelve 503 cuando alguna probe está
# roja —la del registry del CDN lo está por diseño mientras no se configure— y
# el cuerpo con la versión llega igual. Lo que se comprueba acá es la identidad
# de lo que corre, no su salud.
echo "── la versión"
SALUD="$(curl --silent --show-error --location --max-time 30 "$BASE/_health" || echo '{}')"
VERSION="$(printf '%s' "$SALUD" | sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"

if [ -z "$VERSION" ]; then
  falla "/_health no dijo qué versión corre — el humo no puede verificar nada"
elif [ "$VERSION" = "$ESPERADO" ]; then
  paso "contesta $VERSION, que es el commit desplegado"
else
  falla "contesta $VERSION y se desplegó $ESPERADO — el reinicio no tomó efecto"
fi

# ── 3. El árbol de servicios NO está expuesto ────────────────────────────────
#
# Se comprueba desde fuera y no leyendo el compose: el gate de `ports:` vigila
# el fichero, esto vigila la realidad. Un firewall mal puesto, un `ports` que
# alguien añadió a mano en el servidor, o un Caddyfile con una ruta de más se
# ven acá y en ningún otro sitio.
#
# El webhook del proveedor de correo es la ÚNICA ruta del árbol que responde a
# la fuerza (ADR 0131) — lo llama un tercero sin la llave compartida.
echo "── lo que NO debería estar abierto"
for ruta in /v1/booking/slots /v1/orders /v1/sessions/search; do
  CODIGO="$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 15 "$BASE$ruta" || true)"
  # 404 = el CMS no conoce esa ruta, que es lo correcto: el proxy no la enruta.
  # Un 200 o un 401 significan que HAY una capacidad contestando ahí afuera.
  if [ "$CODIGO" = "200" ] || [ "$CODIGO" = "401" ]; then
    falla "$ruta contestó $CODIGO — hay una capacidad alcanzable desde internet"
  else
    paso "$ruta → $CODIGO (no enrutada, como debe ser)"
  fi
done

echo
if [ "$fallo" -eq 0 ]; then
  echo "✓ el humo pasó contra $BASE"
else
  echo "✗ el humo FALLÓ contra $BASE — se vuelve a la versión anterior"
fi
exit "$fallo"
