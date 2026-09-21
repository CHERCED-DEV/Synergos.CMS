#!/usr/bin/env node
/**
 * Gate de PORTADA (#119) — prueba la única propiedad que ningún otro gate mira:
 *
 *   que de un clon limpio salga una portada que el navegador PUEDE VER.
 *
 * Los demás gates paran justo antes. `usync-audit.mjs` valida la forma del XML.
 * `usync-rebuild-check.mjs` valida que Umbraco lo ACEPTA — y dice de sí mismo que
 * «no hace ni un request HTTP» y que el comportamiento runtime «es del gate de HTML
 * servido». Éste es ese gate.
 *
 * QUÉ PASÓ POR EL HUECO, porque es lo que justifica el coste de arrancar la app:
 *
 *   1. `uSync/v9/Content/` está vacía y nada creaba una portada, así que `GET /`
 *      servía el cartel «No published content» de Umbraco — con 200 y HTML de
 *      verdad — y el humo público lo daba por bueno hasta #114.
 *   2. Con el sitio en blanco, _Layout NO se renderizaba nunca. Así que cuando el
 *      arreglo del defecto #92 dejó `_SynergosBridge.cshtml` sin el `@using` de
 *      `Microsoft.Extensions.Logging`, esa vista dejó de compilar y CUALQUIER
 *      página de contenido pasó a contestar 500 — diez días, con la suite entera
 *      en verde y el gate de #92 confirmando que la línea estaba escrita.
 *
 * Las vistas de este proyecto se compilan SIEMPRE en caliente
 * (`RazorCompileOnBuild=false`, porque `ModelsMode=InMemoryAuto`), así que un
 * `dotnet build` verde no dice nada sobre si una vista compila. Lo único que lo
 * dice es pedir la página.
 *
 * Qué comprueba, en orden:
 *   1. Con la base recién importada, `/` es el cartel de Umbraco vacío. Si NO lo
 *      es, algo sembró contenido en el arranque — ADR 0013, y se falla.
 *   2. `POST /dev/seed-portada` crea la portada (outcome `Created`).
 *   3. `/` contesta 200, no es el cartel, no es un error, y trae el cuerpo.
 *   4. Repetir la siembra contesta `AlreadyAuthored` y no cambia la página. Pisar
 *      lo que el arquitecto acaba de ajustar es el daño real, no duplicar.
 *
 * Uso:
 *   node tools/humo-portada.mjs [--no-build] [--puerto 5210]
 *        [--timeout <segundos>] [--log <ruta>]
 *
 * Necesita el SDK de .NET: compila y arranca la aplicación real.
 */

import { spawn, execSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, createWriteStream, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const WEB = join(ROOT, 'Synergos.CMS.Web');
const DLL = join(WEB, 'bin', 'Debug', 'net8.0', 'Synergos.CMS.Web.dll');

const args = process.argv.slice(2);
const flag = (n) => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : d; };

const PUERTO = Number(opt('--puerto', '5210'));
const TIMEOUT_MS = Number(opt('--timeout', '900')) * 1000;
const BASE = `http://127.0.0.1:${PUERTO}`;

// El texto del cartel de Umbraco vacío. Es el mismo que mira tools/humo-publico.sh:
// un sitio sin contenido no falla, contesta 200 con esta página.
const CARTEL_VACIO = 'No published content';

let fallos = 0;
const log = (m) => console.log(`[humo-portada] ${m}`);
const paso = (m) => console.log(`[humo-portada] ✓ ${m}`);
const falla = (m) => { console.error(`[humo-portada] ✗ ${m}`); fallos++; };

// ── 1. Build ────────────────────────────────────────────────────────────────
if (!flag('--no-build')) {
  log('compilando Synergos.CMS.Web…');
  execSync('dotnet build Synergos.CMS.Web/Synergos.CMS.Web.csproj -v quiet --nologo',
    { cwd: ROOT, stdio: 'inherit' });
}
if (!existsSync(DLL)) {
  falla(`no existe ${DLL} — compila primero o quita --no-build`);
  process.exit(1);
}

// ── 2. Arranque con DB desechable ───────────────────────────────────────────
const tmp = mkdtempSync(join(tmpdir(), 'humo-portada-'));
const logPath = opt('--log', join(tmp, 'humo-portada.log'));
const appLog = createWriteStream(logPath);
log(`DB temporal: ${join(tmp, 'portada.sqlite.db')}`);
log(`log completo: ${logPath}`);

const child = spawn('dotnet', [DLL], {
  cwd: WEB,
  windowsHide: true,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Docker',
    ASPNETCORE_URLS: `http://127.0.0.1:${PUERTO}`,
    ConnectionStrings__umbracoDbDSN:
      `Data Source=${join(tmp, 'portada.sqlite.db')};Cache=Shared;Foreign Keys=True;Pooling=True`,
    ConnectionStrings__umbracoDbDSN_ProviderName: 'Microsoft.Data.Sqlite',
    uSync__Settings__ImportAtStartup: 'All',
    // El gate LEE el repo, nunca lo reescribe: con el export al guardar encendido,
    // sembrar la portada escribiría uSync/v9/Content/ en el working tree — que es
    // justo el XML que un agente no autora (ADR 0129).
    uSync__Settings__ExportOnSave: 'None',
    uSync__Sets__Default__Handlers__ContentHandler__Enabled: 'true',
    uSync__Sets__Default__Handlers__MediaHandler__Enabled: 'true',
    // Encendido a propósito, al revés que en usync-rebuild-check: lo que se prueba
    // acá es precisamente la herramienta que vive detrás del flag.
    Synergos__DevSeed__Enabled: 'true',
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
const post = async (ruta) => {
  const r = await fetch(BASE + ruta, { method: 'POST' });
  return { status: r.status, body: await r.text() };
};

try {
  await esperaArranque;
  log('arrancado, import de uSync completo');

  // ── 3. El punto de partida: Umbraco vacío ─────────────────────────────────
  const antes = await get('/');
  if (antes.status !== 200) {
    falla(`antes de sembrar, / contestó ${antes.status} en vez de 200`);
  } else if (!antes.body.includes(CARTEL_VACIO)) {
    // Esto NO es «qué bien, ya hay portada»: significa que algo creó contenido
    // durante el arranque, y eso es el seeder prohibido (ADR 0013).
    falla('antes de sembrar, / NO es el cartel de Umbraco vacío — algo sembró contenido '
      + 'en el arranque, que es exactamente lo que ADR 0013 prohíbe');
  } else {
    paso(`de salida el sitio está en blanco (${antes.body.length} bytes, cartel de Umbraco)`);
  }

  // ── 4. Sembrar ────────────────────────────────────────────────────────────
  const siembra = await post('/dev/seed-portada');
  if (siembra.status !== 200 || !siembra.body.includes('"outcome":"Created"')) {
    falla(`POST /dev/seed-portada contestó ${siembra.status} ${siembra.body.slice(0, 200)}`);
  } else {
    paso('POST /dev/seed-portada creó la portada');
  }

  // ── 5. Y la portada se SIRVE ──────────────────────────────────────────────
  //
  // El corte que faltaba. Las vistas se compilan en caliente, así que un build
  // verde no dice si compilan: lo dice pedir la página.
  const despues = await get('/');
  if (despues.status !== 200) {
    falla(`con la portada sembrada, / contestó ${despues.status} — mirá ${logPath}: `
      + 'una vista que no compila se ve así, y un build verde no la caza');
  } else if (despues.body.includes(CARTEL_VACIO)) {
    falla('con la portada sembrada, / sigue siendo el cartel de Umbraco vacío');
  } else if (!despues.body.includes('syn-site-root__sections')) {
    falla('la portada se sirve pero sin su cuerpo: el Block Grid no pintó ningún bloque '
      + '(¿la Key del area cambió?)');
  } else {
    paso(`/ sirve la portada (${despues.body.length} bytes, con su cuerpo)`);
  }

  // ── 6. Y repetir la siembra no la pisa ────────────────────────────────────
  const otra = await post('/dev/seed-portada');
  if (!otra.body.includes('"outcome":"AlreadyAuthored"')) {
    falla(`la segunda siembra no fue un no-op: ${otra.body.slice(0, 200)}`);
  } else {
    paso('la segunda siembra no toca nada (AlreadyAuthored)');
  }

  const tercera = await get('/');
  if (tercera.body.length !== despues.body.length) {
    falla(`la segunda siembra cambió la página (${despues.body.length} → ${tercera.body.length} bytes)`);
  } else {
    paso('la página servida es la misma tras la segunda siembra');
  }
} catch (e) {
  falla(e.message);
} finally {
  child.kill();
  await new Promise((r) => {
    const hard = setTimeout(() => { try { child.kill('SIGKILL'); } catch { /* ya murió */ } }, 8000);
    if (child.exitCode !== null) { clearTimeout(hard); r(); return; }
    child.once('exit', () => { clearTimeout(hard); r(); });
  });
}

// El import reescribe plantillas Razor en disco (BOM incluido) — ADR 0128. Se AVISA
// y no se toca nada: restaurar pisaría ediciones sin commitear.
try {
  const sucio = execSync('git status --porcelain -- Synergos.CMS.Web/Views Synergos.CMS.Web/uSync',
    { cwd: ROOT, encoding: 'utf8' }).trim();
  if (sucio) {
    log('⚠ el import tocó archivos trackeados (revisá antes de commitear):');
    for (const l of sucio.split('\n').slice(0, 10)) console.log('   ' + l);
  }
} catch { /* sin git no hay aviso */ }


/**
 * Igual que en `humo-conectado.mjs`, y por la misma razón: el fallo más caro de este
 * gate es el que no deja pista. Sin esto, una corrida de CI dice «el proceso murió
 * (exit null)» y lo que lo explica se queda dentro del temporal del runner, que se
 * borra con la máquina. Los dos gates son gemelos y el volcado también — escribirlo
 * en uno y dejar nota en el otro es cómo un defecto identificado sobrevive (§5).
 */
async function volcarLog(etiqueta, ruta, stream, lineas = 40) {
  await new Promise((r) => {
    const t = setTimeout(r, 2000);
    stream.once('close', () => { clearTimeout(t); r(); });
    stream.end();
  });

  if (!existsSync(ruta)) { console.error(`[${etiqueta}] no hay log que volcar en ${ruta}`); return; }
  const todo = readFileSync(ruta, 'utf8').split('\n');
  const cola = todo.slice(-lineas).join('\n').trim();
  console.error(`[${etiqueta}] ── últimas ${Math.min(lineas, todo.length)} líneas de ${ruta} ──`);
  console.error(cola || '(vacío)');
  console.error(`[${etiqueta}] ── fin del log ──`);
}

if (fallos > 0) {
  await volcarLog('humo-portada', logPath, appLog);
  console.error(`[humo-portada] ✗ ${fallos} fallo(s) — se conserva ${tmp}`);
  process.exit(1);
}
appLog.end();
try { rmSync(tmp, { recursive: true, force: true }); } catch { /* temp del SO */ }
log('✓ hay camino: clon limpio → schema → portada sembrada → portada SERVIDA');
