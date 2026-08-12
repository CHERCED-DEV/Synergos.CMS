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

/**
 * Las capacidades que VERIFICAN tokens de identidad (HU #14, rebanada 3).
 *
 * Es una lista y no «todas» a proposito: una capacidad que no lee la cabecera no
 * gana nada teniendo la llave, y tenerla la pondria a un descuido de empezar a
 * creerse tokens que nadie decidio que aceptara. Se anade cuando se cablea, no antes.
 */
const VERIFICAN_IDENTIDAD = new Set(['Synergos.Api.Messaging', 'Synergos.Api.Workflow']);

/** `Synergos.Api.Booking` → `api-booking`. Docker no quiere mayúsculas ni puntos. */
const nombreServicio = (proyecto) =>
  proyecto.replace(/^Synergos\./, '').replace(/\./g, '-').toLowerCase();

/** `Synergos.Api.Booking` → `Booking`. Es la sección que lee `<X>:ApiKey`. */
const seccionConfig = (proyecto) => proyecto.replace(/^Synergos\.(Api|Bff)\./, '');

/** `Synergos.Api.Booking` → `ghcr.io/…/synergos.api.booking`. GHCR exige minúsculas. */
const imagen = (proyecto) => `\${SYNERGOS_REGISTRY}/${proyecto.toLowerCase()}:\${SYNERGOS_TAG}`;

// ── Lo que un servicio necesita ADEMAS de la llave y su almacen ─────────────
//
// Se DERIVA del disco, igual que la lista de servicios, y por la misma razon:
// una tabla escrita a mano se desincroniza en la tercera ola. Lo que se añade
// aquí aparece en el diff del generador, no escondido en la linea 400 de un YAML.

/**
 * Las capacidades a las que un orquestador llama, leidas de su `Program.cs`.
 *
 * SIN ESTO EL DESPLIEGUE ESTA ROTO Y NO LO PARECE: `AddSagaMachinery` cae a
 * `http://localhost/{cap}/`, que dentro del contenedor es EL PROPIO BFF. El
 * orquestador arranca sano, pasa su /health, y falla TODAS las sagas.
 */
function capacidadesDe(proyecto) {
  const programa = readFileSync(join(RAIZ, proyecto, 'Program.cs'), 'utf8');

  // `SaludCapabilities.Consent` → `consent`. El identificador de la constante y
  // su valor coinciden en las veinte; si algun dia dejaran de coincidir, la
  // comprobacion contra el disco de mas abajo lo caza.
  const nombres = [...programa.matchAll(/\b\w*Capabilities\.(\w+)/g)].map((m) => m[1].toLowerCase());

  // El motor añade `notifications` por su cuenta: es donde avisa cuando una
  // compensacion se rinde (CompensationAlert.Capability).
  return [...new Set([...nombres, 'notifications'])].sort();
}

/** La llave con la que una capacidad VERIFICA tokens de identidad, si los verifica. */
function entornoIdentidad(proyecto) {
  if (!VERIFICAN_IDENTIDAD.has(proyecto)) return '';
  return [
    '',
    '      # La llave con la que se COMPRUEBAN los tokens (HU #14). La misma que firma y',
    '      # en la MISMA seccion que en Api.Identity: con dos nombres, configurarla en uno',
    '      # y olvidarla en otro da un token valido que la capacidad rechaza.',
    '      #',
    '      # Sin `:?` a proposito: quien solo verifica arranca sin llave — es el camino del',
    '      # clon limpio. Y arranca SIN PODER verificar, que no es verificando mal: un token',
    '      # presentado ahi se rechaza con identity.token_not_verifiable, no se ignora.',
    `      IdentityTokens__Keys__\${SYNERGOS_IDENTITY_ACTIVE_KID:-k1}: \${SYNERGOS_IDENTITY_SIGNING_KEY:-}`,
    '      IdentityTokens__ActiveKeyId: \${SYNERGOS_IDENTITY_ACTIVE_KID:-k1}',
  ].join('\n');
}

/** Las lineas de entorno propias de cada servicio. */
function entornoExtra(proyecto, disponibles) {
  const seccion = seccionConfig(proyecto);

  if (proyecto.startsWith('Synergos.Bff.')) {
    const caps = capacidadesDe(proyecto);
    const desconocidas = caps.filter((c) => !disponibles.has(c));
    if (desconocidas.length > 0) {
      throw new Error(
        `compose-gen: ${proyecto} nombra capacidades que no existen: ${desconocidas.join(', ')}`);
    }

    const rutas = caps.flatMap((c) => [
      `      ${seccion}__Capabilities__${c}__BaseUrl: "http://api-${c}:8080"`,
      `      ${seccion}__Capabilities__${c}__ApiKey: \${SYNERGOS_API_KEY}`,
    ]);

    return [
      '',
      '      # ── A QUE CAPACIDADES LLEGA ─────────────────────────────────────────',
      '      # Sin esto, AddSagaMachinery cae a http://localhost/{cap}/ — que dentro',
      '      # del contenedor es EL PROPIO BFF. Arrancaria sano, pasaria su /health y',
      '      # fallaria TODAS las sagas. Por nombre de servicio, nunca localhost.',
      ...rutas,
      '',
      '      # El aviso cuando una compensacion se rinde tras ocho intentos. Sin esto',
      '      # queda visible en /v1/compensations con alertedAtUtc en nulo — degrada,',
      '      # no rompe, pero nadie se entera.',
      `      ${seccion}__Sweep__IntervalSeconds: \${SYNERGOS_SWEEP_INTERVAL_SECONDS:-60}`,
      `      ${seccion}__Sweep__AbandonAfterMinutes: \${SYNERGOS_ABANDON_AFTER_MINUTES:-60}`,
      '',
      '      # Cuantas veces se reintenta un AVISO colgado antes de rendirse (HU #29).',
      '      # Va en todos los orquestadores a proposito: elegir uno obligaria a decir',
      '      # cual, y el dia que ese host este caido nadie barreria. Que dos coincidan',
      '      # sobre el mismo envio no manda dos correos — la capacidad rechaza el',
      '      # reintento simultaneo con retry_in_flight.',
      `      ${seccion}__Sweep__DeliveryRetryCeiling: \${SYNERGOS_DELIVERY_RETRY_CEILING:-8}`,
      '',
      `      ${seccion}__Alerts__ToKind: \${SYNERGOS_ALERTS_TO_KIND:-${seccion.toLowerCase()}.guardia}`,
      `      ${seccion}__Alerts__ToId: \${SYNERGOS_ALERTS_TO_ID:-operaciones}`,
      `      ${seccion}__Alerts__Address: \${SYNERGOS_ALERTS_ADDRESS:-}`,
    ].join('\n');
  }

  if (proyecto.endsWith('.Payments')) {
    return [
      '',
      '      # Con que se cobra (HU #27). `logging` dice que si a todo y lo grita; con',
      '      # cualquier otro nombre y sin credencial la capacidad RECHAZA cada cobro a',
      '      # gritos. Lo que no existe es el stub sirviendo en silencio.',
      '      Payments__Provider: \${PAYMENTS_PROVIDER:-logging}',
      '      Payments__wompi__ApiKey: \${PAYMENTS_WOMPI_API_KEY:-}',
    ].join('\n');
  }

  if (proyecto.endsWith('.Identity')) {
    return [
      '',
      '      # La llave que FIRMA los tokens de identidad (HU #14). NO es',
      '      # SYNERGOS_API_KEY: la compartida la tienen los 22 servicios y solo dice',
      '      # «este proceso es de los nuestros». Si con ella se firmaran identidades,',
      '      # cualquiera de los 22 fabricaria personas.',
      '      #',
      '      # Sin llave el servicio NO arranca — un Api.Identity que dice emitir',
      '      # identidades y no puede es peor que uno caido, porque parece que funciona.',
      `      IdentityTokens__Keys__\${SYNERGOS_IDENTITY_ACTIVE_KID:-k1}: \${SYNERGOS_IDENTITY_SIGNING_KEY:?falta SYNERGOS_IDENTITY_SIGNING_KEY}`,
      '      IdentityTokens__ActiveKeyId: \${SYNERGOS_IDENTITY_ACTIVE_KID:-k1}',
      '      IdentityTokens__LifetimeMinutes: \${SYNERGOS_IDENTITY_TOKEN_MINUTES:-15}',
      '      IdentityTokens__MaxSessionMinutes: \${SYNERGOS_IDENTITY_SESSION_MINUTES:-480}',
    ].join('\n');
  }

  // Las capacidades que VERIFICAN tokens (HU #14, rebanada 3). Misma llave que la
  // de firma y MISMA seccion que en Api.Identity: si aca se llamara distinto,
  // configurarla en uno y olvidarla en otro daria un token valido que la capacidad
  // rechaza, que es de los peores sintomas de diagnosticar porque todo «parece bien».
  //
  // Sin `:?` a proposito, al reves que en Api.Identity: quien solo verifica arranca
  // sin llave — es el camino del clon limpio, donde nadie presenta tokens todavia. Y
  // arranca sin poder verificar, que NO es lo mismo que verificando mal: un token
  // presentado ahi se rechaza con identity.token_not_verifiable, no se ignora.
  if (proyecto.endsWith('.Workflow')) {
    return [
      '',
      '      # De donde salen los roles de quien dispara una transicion (defecto #48).',
      '      # Los declarados en el cuerpo guardan contra el ACCIDENTE, no contra quien',
      '      # quiera saltarse la guarda: cualquiera con la llave compartida se asciende',
      '      # escribiendo una linea de JSON. Con esto en true, una transicion que exige',
      '      # rol SOLO acepta roles de un token verificado.',
      '      #',
      '      # Default false a proposito: encenderlo hoy romperia Gobierno (#44) y el',
      '      # seguimiento (#46), que mandan el rol a mano porque NADIE puede presentar un',
      '      # token todavia — el puente Member <-> Principal de la HU #14 no esta hecho.',
      '      Workflow__Roles__RequireVerifiedRoles: \${SYNERGOS_WORKFLOW_REQUIRE_VERIFIED_ROLES:-false}',
    ].join('\n') + entornoIdentidad(proyecto);
  }

  if (proyecto.endsWith('.Notifications')) {
    return [
      '',
      '      # El transporte real (ADR 0131). Faltaba desde que se escribio: el correo',
      '      # se documento en .env.example y el compose nunca lo pasaba, asi que la',
      '      # capacidad rechazaba cada envio en un servidor bien configurado.',
      '      Notifications__Resend__ApiKey: \${Notifications__Resend__ApiKey:-}',
      '      Notifications__Resend__From: \${Notifications__Resend__From:-}',
      '      Notifications__Resend__WebhookSecret: \${Notifications__Resend__WebhookSecret:-}',
    ].join('\n');
  }

  // Fallback: quien verifica tokens y no tiene bloque propio se lleva sólo la llave.
  // Va al FINAL a proposito — puesto arriba se comia el bloque de Api.Workflow, que
  // ademas de la llave necesita su postura de roles.
  return entornoIdentidad(proyecto);
}

// ── El bloque de un servicio del árbol ───────────────────────────────────────
//
// Todos idénticos salvo el nombre. Que sean idénticos ES la propiedad: el día
// que uno necesite algo distinto, se ve en el diff del generador y no escondido
// en la línea 400 de un YAML.
function bloqueServicio(proyecto, disponibles) {
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
      ${seccion}__Storage__Root: /app/data/${seccion.toLowerCase()}${entornoExtra(proyecto, disponibles)}
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

      # Contra quien compra la tienda (HU #24) y contra quien agenda la cita
      # (HU #25). El MODO sale del entorno; las direcciones NO — dentro de esta
      # red son fijas, y hacerlas configurables solo invita a que alguien ponga
      # localhost, que aca es el propio contenedor del CMS.
      #
      # El default de los dos es Stub: un despliegue que no lo diga sirve los
      # motores en proceso, igual que un clon limpio.
      Synergos__Tienda__Mode: \${SYNERGOS_TIENDA_MODE:-Stub}
      Synergos__Tienda__BaseUrl: "http://bff-tienda:8080"
      Synergos__Tienda__CartBaseUrl: "http://api-cart:8080"
      Synergos__Tienda__ApiKey: \${SYNERGOS_API_KEY}
      Synergos__Tienda__Carrier: \${SYNERGOS_TIENDA_CARRIER:-default}

      Synergos__Salud__Mode: \${SYNERGOS_SALUD_MODE:-Stub}
      Synergos__Salud__BaseUrl: "http://bff-salud:8080"
      Synergos__Salud__ApiKey: \${SYNERGOS_API_KEY}

      # Y contra quien se aparta la visita al inmueble (HU #33a). Aca el modo se
      # llama 'Api' y no 'Bff': una visita no se cobra, asi que toca UNA sola
      # capacidad y va DIRECTO a Api.Booking. Un orquestador seria una saga de un
      # paso. Hay gate (RealtyWiringTests) por si algun dia le entra un cobro.
      Synergos__Realty__Mode: \${SYNERGOS_REALTY_MODE:-Stub}
      Synergos__Realty__BaseUrl: "http://api-booking:8080"
      Synergos__Realty__ApiKey: \${SYNERGOS_API_KEY}

      # Y contra quien se compran las entradas (HU #35). Aca vuelve a decir 'Bff'
      # porque SI hay algo que deshacer: si el cobro falla hay que soltar el aforo
      # apartado, y si el consumo falla despues de capturar hay que devolver la
      # plata. Lo que NO viaja es el artefacto: la entrada, su QR y el check-in se
      # quedan en el CMS, donde vive el firmante.
      Synergos__Eventos__Mode: \${SYNERGOS_EVENTOS_MODE:-Stub}
      Synergos__Eventos__BaseUrl: "http://bff-eventos:8080"
      Synergos__Eventos__ApiKey: \${SYNERGOS_API_KEY}

      # Y contra quien se reserva un hotel (HU #36). SOLO la via hotel: el
      # carrito multi-producto no lleva fechas y un apartado de Api.Booking ES
      # una ventana sobre un recurso. Encenderlo exige ademas cargar el precio
      # en Api.Pricing y el recurso en Api.Booking por cada tipo/tarifa — ver
      # .env.example.
      Synergos__Viajes__Mode: \${SYNERGOS_VIAJES_MODE:-Stub}
      Synergos__Viajes__BaseUrl: "http://bff-viajes:8080"
      Synergos__Viajes__ApiKey: \${SYNERGOS_API_KEY}

      # Contra que avanza un expediente (HU #44). Dice Api y no Bff, igual que la
      # visita al inmueble: decidir es UN paso, sin plata en medio y sin nada que
      # deshacer si algo falla. Un orquestador seria una saga de un paso.
      #
      # Encenderlo exige PUBLICAR LA DEFINICION en Api.Workflow (POST /v1/definitions)
      # — ver .env.example. Sin ella, decidir se rechaza con definition_not_found, que
      # es mejor que adivinar un proceso.
      Synergos__Gob__Mode: \${SYNERGOS_GOB_MODE:-Stub}
      Synergos__Gob__BaseUrl: "http://api-workflow:8080"
      Synergos__Gob__ApiKey: \${SYNERGOS_API_KEY}
      Synergos__Gob__DefinitionKey: \${SYNERGOS_GOB_DEFINITION:-gov.tramite}

      # Contra que se valida el avance de un pedido (HU #46). Los cuatro dominios
      # comparten esta seccion y cada uno pide SU definicion: tracking.shop,
      # tracking.travel, tracking.events, tracking.academy. Una compartida leeria
      # la etapa de un dominio contra el pipeline de otro — «enviado» convertido
      # en «matriculado» sin que nada falle.
      #
      # Encenderlo exige publicar LAS CUATRO definiciones — ver .env.example. Y
      # ojo: leer el timeline NO sale a la red, asi que con la capacidad caida se
      # sigue viendo donde va un pedido; solo se para avanzarlo.
      Synergos__Tracking__Mode: \${SYNERGOS_TRACKING_MODE:-Stub}
      Synergos__Tracking__BaseUrl: "http://api-workflow:8080"
      Synergos__Tracking__ApiKey: \${SYNERGOS_API_KEY}
      Synergos__Tracking__DefinitionPrefix: \${SYNERGOS_TRACKING_PREFIX:-tracking}
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

  // Las capacidades que EXISTEN, para que un orquestador no pueda nombrar una
  // que no está: seria una URL a un servicio inexistente, y el fallo saldria en
  // produccion y no acá.
  const disponibles = new Set(
    proyectos
      .filter((p) => p.startsWith('Synergos.Api.'))
      .map((p) => seccionConfig(p).toLowerCase()));

  const cuerpo = proyectos.map((p) => bloqueServicio(p, disponibles)).join('\n');

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
