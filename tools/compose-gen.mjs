#!/usr/bin/env node
//
// Genera `compose.prod.yml` — las 23 piezas del producto, detrás de un proxy.
//
// ─────────────────────────────────────────────────────────────────────────────
// SE GENERA, NO SE ESCRIBE A MANO.
//
// Veintitrés bloques de YAML casi idénticos escritos a mano son veintitrés
// oportunidades de que uno se quede sin volumen —y pierda sus datos en cada
// despliegue, sin fallar y sin avisar— o publique un puerto que no debería.
// Es el mismo razonamiento de `service-matrix.mjs` y de `ApiMoldTests`: lo
// uniforme se deriva, y el gate crece con el catálogo sin que nadie lo
// mantenga.
//
//   node tools/compose-gen.mjs            → escribe compose.prod.yml
//   node tools/compose-gen.mjs --check    → falla si el fichero no está al día
// ─────────────────────────────────────────────────────────────────────────────
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { servicios } from './service-matrix.mjs';

const RAIZ = process.cwd();
const DESTINO = join(RAIZ, 'compose.prod.yml');
const CHECK = process.argv.includes('--check');

/** `Synergos.Api.Booking` → `api-booking`. Docker no quiere mayúsculas ni puntos. */
const nombreServicio = (proyecto) =>
  proyecto.replace(/^Synergos\./, '').replace(/\./g, '-').toLowerCase();

/** `Synergos.Api.Booking` → `Booking`. Es la sección que lee `<X>:ApiKey`. */
const seccionConfig = (proyecto) => proyecto.replace(/^Synergos\.(Api|Bff)\./, '');

/** `Synergos.Api.Booking` → `ghcr.io/…/synergos.api.booking`. GHCR exige minúsculas. */
const imagen = (proyecto) => `\${SYNERGOS_REGISTRY}/${proyecto.toLowerCase()}:\${SYNERGOS_TAG}`;

// ── El bloque de un servicio del árbol ───────────────────────────────────────
//
// Todos idénticos salvo el nombre. Que sean idénticos ES la propiedad: el día
// que uno necesite algo distinto, se ve en el diff del generador y no escondido
// en la línea 400 de un YAML.
function bloqueServicio(proyecto) {
  const nombre = nombreServicio(proyecto);
  const seccion = seccionConfig(proyecto);

  return `  ${nombre}:
    image: ${imagen(proyecto)}
    restart: unless-stopped
    networks: [interna]
    # SIN \`ports\`: esta capacidad NO se expone a internet. La protege una llave
    # compartida que CLAUDE.md §11 dice claramente que "no es identidad" —
    # publicarla seria contradecir por configuracion lo que el codigo dice de si
    # mismo. Se alcanza por nombre de servicio desde la red interna.
    environment:
      ${seccion}__ApiKey: \${SYNERGOS_API_KEY:?falta SYNERGOS_API_KEY}
      ${seccion}__Storage__Root: /app/data/${seccion.toLowerCase()}
    volumes:
      # SIN ESTO, CADA DESPLIEGUE BORRA LOS DATOS. No falla y no avisa: el
      # contenedor nuevo arranca con el directorio vacio y la capacidad se
      # comporta como recien instalada.
      - ${nombre}-data:/app/data
    deploy:
      # NO SUBIR DE 1. JsonCollectionStore tiene un lock de PROCESO: dos
      # instancias se pisan, y no da error — corrompe. Y un rolling deploy son,
      # por definicion, dos instancias a la vez. Ver epica #16 y CLAUDE.md §11.
      replicas: 1
`;
}

// ── El fichero entero ────────────────────────────────────────────────────────
function generar() {
  const proyectos = servicios(RAIZ);
  if (proyectos.length === 0) throw new Error('compose-gen: no se encontró ningún servicio');

  // `Api.Notifications` recibe el webhook del proveedor de correo (ADR 0131), y
  // es lo UNICO del arbol de servicios alcanzable desde fuera. Se busca en vez
  // de cablearse para que el dia que cambie de nombre esto falle acá y no en
  // produccion.
  const notificaciones = proyectos.find((p) => p.endsWith('.Notifications'));
  if (!notificaciones) throw new Error('compose-gen: no está Synergos.Api.Notifications, que recibe el webhook');

  const sesiones = proyectos.find((p) => p.endsWith('.Sessions'));
  if (!sesiones) throw new Error('compose-gen: no está Synergos.Api.Sessions, que consume el CMS');

  const cabecera = `# GENERADO POR tools/compose-gen.mjs — NO EDITAR A MANO.
#
# Se regenera con \`node tools/compose-gen.mjs\` y hay un gate que verifica que
# esté al día (ComposeStackTests). Editarlo a mano funciona hasta el siguiente
# \`compose-gen\`, que lo pisa sin avisar.
#
# El producto entero: el CMS, las 20 capacidades, los 2 orquestadores y un proxy.
#
#   docker compose -f compose.prod.yml up -d
#
# Lo que hay que tener en el entorno (ver .env.example):
#   SYNERGOS_REGISTRY  ghcr.io/cherced-dev
#   SYNERGOS_TAG       el SHA del commit que se despliega — NUNCA \`latest\`
#   SYNERGOS_API_KEY   la llave compartida entre servicios
#   SYNERGOS_DOMAIN    el dominio publico
#
# ⚠️ UNA INSTANCIA POR CAPACIDAD, Y PARADA ANTES DE ARRANQUE.
# 19 de las 20 capacidades guardan en fichero JSON con un lock de PROCESO. Dos
# instancias se pisan y NO dan error: corrompen. Un rolling deploy son dos
# instancias a la vez, asi que el despliegue "normal" de cualquier plataforma
# moderna rompe esto. Mientras no cambie el almacen (epica #2), no se toca.

name: synergos

networks:
  # Una sola red, y sin \`ports\` en nada que no sea el proxy: lo que no se
  # publica, no se alcanza desde internet. Es la unica frontera que hay.
  interna:
    driver: bridge

services:
  # ── El portero ────────────────────────────────────────────────────────────
  proxy:
    image: caddy:2-alpine
    restart: unless-stopped
    networks: [interna]
    ports:
      - "80:80"
      - "443:443"
    environment:
      SYNERGOS_DOMAIN: \${SYNERGOS_DOMAIN:?falta SYNERGOS_DOMAIN}
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile:ro
      # Los certificados. Sin persistirlos, cada despliegue pide unos nuevos a
      # Let's Encrypt y se choca con su limite de peticiones — que se nota como
      # "el sitio dejo de tener HTTPS" varias horas.
      - caddy-data:/data
      - caddy-config:/config
    depends_on:
      cms:
        condition: service_healthy

  # ── El CMS ────────────────────────────────────────────────────────────────
  cms:
    image: \${SYNERGOS_REGISTRY}/synergos-cms:\${SYNERGOS_TAG}
    restart: unless-stopped
    networks: [interna]
    environment:
      ASPNETCORE_ENVIRONMENT: Docker
      Umbraco__CMS__Global__UmbracoApplicationUrl: "https://\${SYNERGOS_DOMAIN}/"
      Synergos__Notifications__PublicBaseUrl: "https://\${SYNERGOS_DOMAIN}"
      Synergos__Cart__SecretKey: \${SYNERGOS_CART_SECRET:?falta SYNERGOS_CART_SECRET}

      # La UNICA capacidad que el CMS consume hoy (ADR 0130). Por nombre de
      # servicio, no por localhost: dentro de la red de Docker, localhost es el
      # propio contenedor del CMS.
      Synergos__SearchAnalytics__Mode: Http
      Synergos__SearchAnalytics__BaseUrl: "http://${nombreServicio(sesiones)}:8080"
      Synergos__SearchAnalytics__ApiKey: \${SYNERGOS_API_KEY}

      # El CDN (ADR 0132). Sin PublicBaseUrl se queda en Stub y los elementSyn*
      # emiten su comentario de relleno — degradado y visible, no roto.
      Synergos__BundleRegistry__Mode: \${SYNERGOS_CDN_MODE:-Stub}
      Synergos__BundleRegistry__PublicBaseUrl: \${SYNERGOS_CDN_URL:-}
    volumes:
      - cms-db:/app/umbraco/Data
      - cms-logs:/app/umbraco/Logs
      - cms-appdata:/app/App_Data
      - cms-media:/app/wwwroot/media
      # Sin esto, cada despliegue desloguea el backoffice y rompe los secretos
      # TOTP de los members (ADR 0084).
      - cms-dpkeys:/root/.aspnet/DataProtection-Keys
    depends_on:
      ${nombreServicio(sesiones)}:
        # Por salud y no por \`sleep\`: el CMS pregunta a Sessions al arrancar, y
        # un arranque por temporizador acierta hasta el dia que la maquina esta
        # cargada.
        condition: service_healthy
    deploy:
      replicas: 1

  # ── El arbol de servicios ─────────────────────────────────────────────────
`;

  const cuerpo = proyectos.map(bloqueServicio).join('\n');

  const volumenes = [
    'cms-db', 'cms-logs', 'cms-appdata', 'cms-media', 'cms-dpkeys',
    'caddy-data', 'caddy-config',
    ...proyectos.map((p) => `${nombreServicio(p)}-data`),
  ];

  const pie = `
volumes:
${volumenes.map((v) => `  ${v}:`).join('\n')}
`;

  return cabecera + cuerpo + pie;
}

// ── Salida ───────────────────────────────────────────────────────────────────
const generado = generar();

if (CHECK) {
  let actual = '';
  try { actual = readFileSync(DESTINO, 'utf-8'); } catch { /* no existe */ }

  if (actual !== generado) {
    console.error(
      '[compose-gen] ✗ compose.prod.yml no está al día.\n' +
      '  → Corré: node tools/compose-gen.mjs\n' +
      '  Pasa cuando se añade una capacidad y nadie regenera: el servicio nuevo\n' +
      '  simplemente NO se despliega. No falla — falta.',
    );
    process.exit(1);
  }
  console.log('[compose-gen] ✓ compose.prod.yml al día');
} else {
  writeFileSync(DESTINO, generado);
  console.log(`[compose-gen] ✓ escrito ${DESTINO}`);
}
