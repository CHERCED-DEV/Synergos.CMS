#!/usr/bin/env node
/**
 * Gate de LOS DOS ÁRBOLES — prueba la única propiedad que ningún gate mira:
 *
 *   que la página que sirve el CMS traiga lo que hace falta para que el CDN
 *   HIDRATE lo que el SSR pintó.
 *
 * `humo-portada.mjs` (#119) llega hasta «la portada se sirve». Ahí para, y con el
 * CDN en `Stub` eso es todo lo que puede decir: la página sale con sus
 * `<synergos-*>` y sin un solo `<script>`, que es exactamente como se ve una que
 * funciona… y una que no.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * QUÉ PASA POR ESTE HUECO, Y ESTÁ MEDIDO, NO SUPUESTO.
 *
 * El defecto #126 de este repo: 200, el SSR entero, y **nada interactivo**. Sin
 * `<script type="importmap">` el navegador no resuelve `@angular/core` ni
 * `preact`, así que ningún bundle arranca y ningún `customElements.define`
 * ocurre. La página se ve BIEN — el SSR pintó el contenido— y no hace nada.
 *
 * Y se llega ahí por caminos que no fallan:
 *
 *   · con el modo del registry mal puesto (o puesto con el NOMBRE de variable
 *     equivocado — ver abajo);
 *   · con dos frameworks publicando el mismo specifier con URLs distintas, que
 *     hace que el compositor devuelva `null` a propósito (#127, Synergos.UI#58);
 *   · con una vista que pinta Block Grid sin heredar el `<head>` (#126 encontró
 *     dos: `PageBare` y `Error`).
 *
 * **La variable equivocada es el caso más fácil de cometer y está medido** el
 * 2026-09-16, con la misma portada y cambiando sólo el nombre:
 *
 *     Synergos__BundleRegistry__Mode=Http  → 21 595 bytes, import map de 23
 *     SYNERGOS_CDN_MODE=Http               → 19 785 bytes, import map: NO HAY
 *
 * Las dos sirven la portada con sus dos `<synergos-*>`. `SYNERGOS_CDN_MODE` sólo
 * existe dentro de `compose.yml`, que lo traduce a `Synergos__BundleRegistry__*`;
 * fuera de compose no lo lee nadie y no se queja nadie.
 *
 * ─────────────────────────────────────────────────────────────────────────────
 * QUÉ COMPRUEBA, EN ORDEN. Nada se SALTA: lo que no se puede medir se rechaza.
 *
 *   1. La portada se sirve (lo de `humo-portada`, porque sin página no hay nada
 *      que mirar).
 *   2. Hay `<script type="importmap">` y es JSON válido.
 *   3. **Trae entradas de CADA framework que el registry declara.** Es el diente
 *      que caza el conflicto de specifiers: con dos plataformas publicando y el
 *      mapa con una sola, el compositor se paró y la mitad del sitio no hidrata.
 *   4. **Cada `<synergos-*>` de la página tiene su `<script type="module">`.**
 *      Un tag sin bundle es un hueco que el SSR disimula.
 *   5. Cada bundle referenciado contesta 200 en el CDN. Un `<script>` a un 404
 *      es lo mismo que no tenerlo, y el HTML se ve idéntico.
 *
 * Uso:
 *   node tools/humo-conectado.mjs [--cdn-path ../Synergos.UI/public]
 *        [--cdn http://…] [--no-build] [--puerto 5212] [--timeout <seg>]
 *
 * Con `--cdn-path` levanta él mismo un servidor estático sobre esa carpeta, así
 * que no hace falta montar nada aparte. Necesita el SDK de .NET y el repo
 * hermano construido (`npm run build:cdn` en Synergos.UI).
 */

import { spawn, execSync } from 'node:child_process';
import { createServer } from 'node:http';
import { createReadStream, existsSync, mkdtempSync, statSync, createWriteStream } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const WEB = join(ROOT, 'Synergos.CMS.Web');
const DLL = join(WEB, 'bin', 'Debug', 'net8.0', 'Synergos.CMS.Web.dll');

const args = process.argv.slice(2);
const flag = (n) => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : d; };

const PUERTO = Number(opt('--puerto', '5212'));
const PUERTO_CDN = Number(opt('--puerto-cdn', '4398'));
const TIMEOUT_MS = Number(opt('--timeout', '900')) * 1000;
const CDN_PATH = opt('--cdn-path', resolve(ROOT, '..', 'Synergos.UI', 'public'));
const CDN_URL = opt('--cdn', null);
const BASE = `http://127.0.0.1:${PUERTO}`;
const CARTEL_VACIO = 'No published content';

let fallos = 0;
const log = (m) => console.log(`[humo-conectado] ${m}`);
const paso = (m) => console.log(`[humo-conectado] ✓ ${m}`);
const falla = (m) => { console.error(`[humo-conectado] ✗ ${m}`); fallos++; };

const TIPOS = {
  '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.css': 'text/css', '.html': 'text/html', '.map': 'application/json',
};

/** Un estático mínimo sobre la carpeta del CDN. Sirve para no pedir otro proceso. */
function servirCdn(raiz, puerto) {
  const srv = createServer((req, res) => {
    const ruta = join(raiz, decodeURIComponent(req.url.split('?')[0]));
    if (!ruta.startsWith(raiz) || !existsSync(ruta) || statSync(ruta).isDirectory()) {
      res.writeHead(404).end('no');
      return;
    }
    res.writeHead(200, { 'content-type': TIPOS[extname(ruta)] ?? 'application/octet-stream' });
    createReadStream(ruta).pipe(res);
  });
  return new Promise((r) => srv.listen(puerto, '127.0.0.1', () => r(srv)));
}

// ── 0. El CDN ───────────────────────────────────────────────────────────────
let cdnBase = CDN_URL;
let servidor = null;
if (!cdnBase) {
  if (!existsSync(join(CDN_PATH, 'synergos', 'registry.json'))) {
    console.error(
      `[humo-conectado] ✗ no hay CDN en ${CDN_PATH}.\n` +
      `  → Construilo en el repo hermano: npm run build:cdn\n` +
      `  → O apuntá a otro: --cdn-path RUTA, o --cdn http://…`,
    );
    process.exit(1);
  }
  servidor = await servirCdn(CDN_PATH, PUERTO_CDN);
  cdnBase = `http://127.0.0.1:${PUERTO_CDN}`;
  log(`CDN servido desde ${CDN_PATH} en ${cdnBase}`);
} else {
  log(`CDN: ${cdnBase}`);
}

/** Los frameworks que el registry declara. Se DERIVA, no se escribe. */
const registry = await (await fetch(`${cdnBase}/synergos/registry.json`)).json();
const frameworks = [...new Set(
  (registry.elements ?? []).flatMap((e) => Object.keys(e.implementations ?? {})),
)].sort();
if (frameworks.length === 0) {
  falla('el registry no declara ni un framework — el CDN está a medias');
  process.exit(1);
}
log(`el registry declara ${frameworks.length} framework(s): ${frameworks.join(', ')}`);

// ── 1. Build ────────────────────────────────────────────────────────────────
if (!flag('--no-build')) {
  log('compilando Synergos.CMS.Web…');
  execSync('dotnet build Synergos.CMS.Web/Synergos.CMS.Web.csproj -v quiet --nologo',
    { cwd: ROOT, stdio: 'inherit' });
}

const tmp = mkdtempSync(join(tmpdir(), 'humo-conectado-'));
const logPath = opt('--log', join(tmp, 'humo-conectado.log'));
const appLog = createWriteStream(logPath);
log(`log completo: ${logPath}`);

const child = spawn('dotnet', [DLL], {
  cwd: WEB,
  windowsHide: true,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Docker',
    ASPNETCORE_URLS: `http://127.0.0.1:${PUERTO}`,
    ConnectionStrings__umbracoDbDSN:
      `Data Source=${join(tmp, 'conectado.sqlite.db')};Cache=Shared;Foreign Keys=True;Pooling=True`,
    ConnectionStrings__umbracoDbDSN_ProviderName: 'Microsoft.Data.Sqlite',
    uSync__Settings__ImportAtStartup: 'All',
    uSync__Settings__ExportOnSave: 'None',
    uSync__Sets__Default__Handlers__ContentHandler__Enabled: 'true',
    uSync__Sets__Default__Handlers__MediaHandler__Enabled: 'true',
    Synergos__DevSeed__Enabled: 'true',
    // LAS CLAVES DE VERDAD, y por eso este gate existe: `SYNERGOS_CDN_MODE` sólo
    // lo entiende compose.yml. Ver la cabecera.
    Synergos__BundleRegistry__Mode: 'Http',
    Synergos__BundleRegistry__PublicBaseUrl: cdnBase,
    Umbraco__CMS__Hosting__LocalTempStorageLocation: 'EnvironmentTemp',
    TMPDIR: tmp, TEMP: tmp, TMP: tmp,
  },
});

let listo = false;
let buffered = '';
const esperaArranque = new Promise((res, rej) => {
  const t = setTimeout(() => rej(new Error(`timeout de ${TIMEOUT_MS / 1000}s esperando el arranque`)), TIMEOUT_MS);
  const onLine = (line) => {
    appLog.write(line + '\n');
    // Se espera el FIN del import, no el primer 200. Sembrar antes crea la
    // portada con los DataTypes a medio importar: el dropdown guarda una cadena
    // plana y la vista revienta al leerla — 500 en cada render, y el diagnóstico
    // manda a mirar la vista en vez del orden de arranque.
    if (!listo && /uSync: Startup Complete/.test(line)) { listo = true; clearTimeout(t); res(); }
  };
  for (const s of [child.stdout, child.stderr]) {
    s.setEncoding('utf8');
    s.on('data', (c) => {
      buffered += c;
      let nl;
      while ((nl = buffered.indexOf('\n')) >= 0) {
        onLine(buffered.slice(0, nl).trimEnd());
        buffered = buffered.slice(nl + 1);
      }
    });
  }
  child.on('exit', (code) => {
    if (!listo) { clearTimeout(t); rej(new Error(`el proceso murió (exit ${code}) — revisa ${logPath}`)); }
  });
});

const get = async (ruta) => {
  const r = await fetch(BASE + ruta, { redirect: 'follow' });
  return { status: r.status, body: await r.text() };
};

try {
  await esperaArranque;
  log('arrancado, import de uSync completo');

  // ── 2. La portada ─────────────────────────────────────────────────────────
  const siembra = await fetch(BASE + '/dev/seed-portada', { method: 'POST' });
  const cuerpo = await siembra.text();
  if (!/"outcome":"(Created|AlreadyAuthored)"/.test(cuerpo)) {
    falla(`POST /dev/seed-portada contestó ${siembra.status} ${cuerpo.slice(0, 200)}`);
    throw new Error('sin portada no hay nada que mirar');
  }

  let pagina = null;
  for (let i = 0; i < 40 && !pagina; i += 1) {
    const r = await get('/');
    if (r.status === 200 && r.body.length > 3000 && !r.body.includes(CARTEL_VACIO)) pagina = r.body;
    else await new Promise((res) => setTimeout(res, 1500));
  }
  if (!pagina) {
    falla(`con la portada sembrada, / no sirvió una página — revisá ${logPath}`);
    throw new Error('sin página no hay nada que mirar');
  }
  paso(`/ sirve la portada (${pagina.length} bytes)`);

  // ── 3. El import map ──────────────────────────────────────────────────────
  const mapaCrudo = pagina.match(/<script type="importmap">([\s\S]*?)<\/script>/);
  if (!mapaCrudo) {
    falla(
      'la página NO trae <script type="importmap"> — el navegador no puede resolver ' +
      'ningún bare import, así que NADA hidrata (200, el SSR entero, y muerto: es el ' +
      'defecto #126). Causas por orden de probabilidad: el registry no está en modo Http ' +
      '(la clave es `Synergos__BundleRegistry__Mode`, NO `SYNERGOS_CDN_MODE`), el CDN no ' +
      'responde, o dos frameworks publicaron el mismo specifier con URLs distintas y el ' +
      'compositor se paró a propósito (#127).',
    );
  } else {
    let mapa = null;
    try { mapa = JSON.parse(mapaCrudo[1]); } catch (e) { falla(`el import map no es JSON: ${e.message}`); }
    if (mapa) {
      const claves = Object.keys(mapa.imports ?? {});
      paso(`import map con ${claves.length} entradas`);

      // EL DIENTE: una entrada por CADA framework publicado. Con el mapa de uno
      // solo, la mitad del sitio no hidrata y la página se ve igual de bien.
      const urls = Object.values(mapa.imports ?? {}).join(' ');
      const sinEntrada = frameworks.filter((f) => !urls.includes(`/runtime/${f}/`));
      if (sinEntrada.length > 0) {
        falla(
          `el import map no resuelve nada hacia ${sinEntrada.join(', ')}, y el registry ` +
          `declara elementos de ese framework. Sus bundles harán bare imports que el ` +
          `navegador no sabe resolver: esos elementos NO hidratan y el resto sí, así que ` +
          `la página se ve a medias sin que nada falle.`,
        );
      } else {
        paso(`resuelve hacia los ${frameworks.length}: ${frameworks.join(', ')}`);
      }
    }
  }

  // ── 4. Cada tag, con su bundle ────────────────────────────────────────────
  const tags = [...new Set([...pagina.matchAll(/<(synergos-[a-z0-9-]+)/g)].map((m) => m[1]))];
  const scripts = [...pagina.matchAll(/<script[^>]*\ssrc="([^"]+)"[^>]*type="module"/g)].map((m) => m[1]);

  if (tags.length === 0) {
    falla('la portada no pinta ni un <synergos-*> — no hay nada que hidratar y este gate ' +
          'no estaría midiendo nada. Revisá que la portada sembrada lleve bloques.');
  } else {
    const sinBundle = tags.filter((t) => {
      const nombre = t.replace(/^synergos-/, '');
      return !scripts.some((s) => s.includes(`/${nombre}/`));
    });
    if (sinBundle.length > 0) {
      falla(
        `${sinBundle.length} tag(s) sin su <script type="module">: ${sinBundle.join(', ')}. ` +
        `El SSR los pintó y nada los va a hidratar — un hueco que la página disimula.`,
      );
    } else {
      paso(`los ${tags.length} tags tienen su bundle: ${tags.join(', ')}`);
    }

    // ── 5. …y el bundle existe de verdad ────────────────────────────────────
    const rotos = [];
    for (const s of scripts) {
      const url = s.startsWith('http') ? s : `${cdnBase}${s}`;
      try {
        const r = await fetch(url, { method: 'GET' });
        if (!r.ok) rotos.push(`${url} → ${r.status}`);
      } catch (e) { rotos.push(`${url} → ${e.message}`); }
    }
    if (rotos.length > 0) {
      falla(`bundles que no se pueden descargar:\n   ${rotos.join('\n   ')}\n` +
            '   Un <script> a un 404 se ve en el HTML igual que uno bueno.');
    } else if (scripts.length > 0) {
      paso(`los ${scripts.length} bundles contestan 200 en el CDN`);
    }
  }
} catch (e) {
  falla(e.message);
} finally {
  child.kill('SIGKILL');
  servidor?.close();
  appLog.end();
}

if (fallos > 0) {
  console.error(`[humo-conectado] ${fallos} fallo(s) — log: ${logPath}`);
  process.exit(1);
}
console.log('[humo-conectado] ✓ los dos árboles conectan: SSR + import map + bundles servidos');
