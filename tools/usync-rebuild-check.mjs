#!/usr/bin/env node
/**
 * Gate de RECONSTRUCCIÓN (ADR 0128) — prueba la propiedad que hace a la base
 * de datos derivable:
 *
 *   una DB VACÍA + el XML del repo tienen que reproducir el entorno completo.
 *
 * Si esto pasa, la DB deja de ser preciosa: no se protege, se regenera. El
 * gate levanta Umbraco contra una SQLite temporal, deja que uSync importe
 * TODO el árbol uSync/v9/ en el arranque, y falla si el import no aplica
 * limpio y completo.
 *
 * Qué es "limpio y completo", en orden de severidad:
 *   1. El proceso arranca y el import TERMINA (resumen presente).
 *   2. Cero líneas [ERR] en el log — un import que "termina" con errores
 *      adentro es el peor resultado: parece verde y dejó huecos.
 *   3. `processed >= archivos .config TRACKEADOS`. No es una heurística: en
 *      la medición de referencia fueron exactamente 880 = 880. Si uSync se
 *      salta una carpeta entera (el modo de falla que importa), este número
 *      se desploma. Se cuentan los trackeados en git, no el filesystem:
 *      la máquina del arquitecto puede tener export sin commitear todavía
 *      —contenido del seeder, por ejemplo— y eso inflaría el conteo.
 *
 * Decisiones de entorno, todas deliberadas:
 *   - DB en un directorio temporal via ConnectionStrings override — el gate
 *     JAMÁS toca una DB real. Correrlo en la máquina del arquitecto es
 *     seguro por construcción, no por disciplina.
 *   - ExportOnSave=None — el gate lee el repo, nunca lo reescribe. Un gate
 *     que muta lo que verifica no es un gate.
 *   - FailOnMissingParent=true SOLO aquí — estricto en la verificación sin
 *     cambiar el comportamiento del import manual del arquitecto. Un padre
 *     ausente revienta fuerte en CI en vez de crear huérfanos callados.
 *   - DevSeed apagado — la reconstrucción demuestra que el REPO reproduce
 *     el entorno; un seeder maquillaría el resultado.
 *   - Temp storage aislado (EnvironmentTemp + TMPDIR propio) — dos Umbraco
 *     en la misma máquina no se pelean por los índices Examine.
 *
 * Desde el ADR 0129 el gate TAMBIÉN cubre el contenido y los nodos de media:
 * antes Content/ estaba en .gitignore y esta cabecera decía que quedaba fuera
 * "por decisión del arquitecto". Esa decisión se revirtió justamente porque
 * dejaba media verdad en pie — el esquema era derivable y el contenido no, así
 * que las páginas vivían solo en la SQLite que nunca se commitea.
 *
 * Lo que el gate sigue SIN verificar, a propósito: que los BINARIOS de media
 * referenciados existan (eso lo cubre versionar wwwroot/media, no el import),
 * los members (uSync 13 free no trae MemberHandler) y el comportamiento
 * runtime — eso es de los tests xUnit y del gate de HTML servido.
 *
 * Uso:
 *   node tools/usync-rebuild-check.mjs [--no-build] [--keep-db]
 *        [--timeout <segundos>] [--log <ruta>]
 *
 * Necesita el SDK de .NET — a diferencia de usync-audit.mjs, este gate
 * compila y arranca la aplicación real. Es la diferencia entre validar la
 * forma del XML y validar que Umbraco lo ACEPTA.
 */

import { spawn, execSync } from 'node:child_process';
import { mkdtempSync, rmSync, createWriteStream, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const WEB = join(ROOT, 'Synergos.CMS.Web');
const DLL = join(WEB, 'bin', 'Debug', 'net8.0', 'Synergos.CMS.Web.dll');

const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const opt = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};

const NO_BUILD = flag('--no-build');
const KEEP_DB = flag('--keep-db');
const TIMEOUT_MS = Number(opt('--timeout', '900')) * 1000;

const log = (msg) => console.log(`[usync-rebuild] ${msg}`);
const fail = (msg) => {
  console.error(`[usync-rebuild] ✗ ${msg}`);
  process.exitCode = 1;
};

// ── 1. Cuántos ítems DEBE procesar el import ────────────────────────────────
// Solo archivos trackeados: la máquina del arquitecto tiene Content/ y Media/
// locales (gitignored) que no forman parte del contrato del repo.
const tracked = execSync("git ls-files 'Synergos.CMS.Web/uSync/v9/**/*.config'", {
  cwd: ROOT, encoding: 'utf8',
})
  .split('\n')
  .filter((f) => f && !f.endsWith('usync.config'));
const EXPECTED = tracked.length;
log(`archivos uSync trackeados: ${EXPECTED}`);
if (EXPECTED === 0) {
  fail('0 archivos uSync trackeados — ¿se corrió fuera del repo?');
  process.exit(1);
}

// ── 2. Build ─────────────────────────────────────────────────────────────────
if (!NO_BUILD) {
  log('compilando Synergos.CMS.Web…');
  execSync('dotnet build Synergos.CMS.Web/Synergos.CMS.Web.csproj -v quiet --nologo', {
    cwd: ROOT, stdio: 'inherit',
  });
}
if (!existsSync(DLL)) {
  fail(`no existe ${DLL} — compila primero o quita --no-build`);
  process.exit(1);
}

// ── 3. Arranque con DB desechable ────────────────────────────────────────────
const tmp = mkdtempSync(join(tmpdir(), 'usync-rebuild-'));
const dbPath = join(tmp, 'rebuild.sqlite.db');
const logPath = opt('--log', join(tmp, 'usync-rebuild.log'));
const appLog = createWriteStream(logPath);
log(`DB temporal: ${dbPath}`);
log(`log completo: ${logPath}`);

const child = spawn('dotnet', [DLL], {
  cwd: WEB,
  windowsHide: true,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Docker',
    // Puerto 0: el SO asigna uno libre. El gate no hace ni un request HTTP —
    // solo necesita el boot — así que jamás colisiona con un CMS corriendo.
    ASPNETCORE_URLS: 'http://127.0.0.1:0',
    ConnectionStrings__umbracoDbDSN:
      `Data Source=${dbPath};Cache=Shared;Foreign Keys=True;Pooling=True`,
    ConnectionStrings__umbracoDbDSN_ProviderName: 'Microsoft.Data.Sqlite',
    uSync__Settings__ImportAtStartup: 'All',
    uSync__Settings__ExportOnSave: 'None',
    // ADR 0129 — sin esto el gate importaría el esquema y se SALTARÍA el
    // contenido, diciendo "derivable" sobre media verdad. Los handlers vienen
    // apagados de fábrica en uSync; appsettings.json los enciende para la app
    // y aquí se encienden para la verificación.
    uSync__Sets__Default__Handlers__ContentHandler__Enabled: 'true',
    uSync__Sets__Default__Handlers__MediaHandler__Enabled: 'true',
    uSync__Sets__Default__HandlerDefaults__FailOnMissingParent: 'true',
    Synergos__DevSeed__Enabled: 'false',
    Umbraco__CMS__Hosting__LocalTempStorageLocation: 'EnvironmentTemp',
    TMPDIR: tmp, TEMP: tmp, TMP: tmp,
  },
});

const errLines = [];
let summary = null;
let settled = false;
let buffered = '';

const SUMMARY_RE = /uSync Import: (\d+) handlers, processed (\d+) items, (\d+) changes in (\d+)ms/;
const DONE_RE = /uSync: Startup Complete/;
const ERR_RE = /\[\d{2}:\d{2}:\d{2} ERR\]/;

function settle(verdictFn) {
  if (settled) return;
  settled = true;
  clearTimeout(timer);

  // Esperar el exit REAL antes de evaluar y limpiar: borrar el temp mientras
  // Umbraco todavía se apaga deja archivos huérfanos o un rm fallido.
  const finish = () => {
    appLog.end();
    verdictFn();
    cleanup();
  };
  if (child.exitCode !== null) {
    finish();
    return;
  }
  const hardKill = setTimeout(() => {
    try { child.kill('SIGKILL'); } catch { /* ya murió */ }
  }, 8000);
  child.once('exit', () => {
    clearTimeout(hardKill);
    finish();
  });
  child.kill();
}

function onLine(line) {
  appLog.write(line + '\n');
  // Tras settle() el gate ya decidió y mandó SIGTERM: los ERR que lleguen
  // después son ruido del APAGADO (OperationCanceledException de los hosted
  // services cancelados), no errores del import. Contarlos haría fallar una
  // reconstrucción perfecta según el timing del kill — un gate intermitente
  // es peor que no tener gate. El log completo los conserva igual.
  if (!settled && ERR_RE.test(line)) errLines.push(line);
  const m = line.match(SUMMARY_RE);
  if (m) summary = { handlers: +m[1], processed: +m[2], changes: +m[3], ms: +m[4] };
  if (DONE_RE.test(line) && summary) settle(verdict);
}

for (const stream of [child.stdout, child.stderr]) {
  stream.setEncoding('utf8');
  stream.on('data', (chunk) => {
    buffered += chunk;
    let nl;
    while ((nl = buffered.indexOf('\n')) >= 0) {
      onLine(buffered.slice(0, nl).trimEnd());
      buffered = buffered.slice(nl + 1);
    }
  });
}

child.on('exit', (code) => {
  if (!settled) {
    settle(() => {
      fail(`el proceso murió (exit ${code}) antes de completar el import — revisa ${logPath}`);
      for (const l of errLines.slice(0, 5)) console.error('   ' + l);
    });
  }
});

const timer = setTimeout(() => {
  settle(() => fail(`timeout de ${TIMEOUT_MS / 1000}s sin completar el import — revisa ${logPath}`));
}, TIMEOUT_MS);

// ── 4. Veredicto ─────────────────────────────────────────────────────────────
function verdict() {
  log(`import: ${summary.handlers} handlers, ${summary.processed} ítems, ` +
      `${summary.changes} cambios, ${(summary.ms / 1000).toFixed(1)}s`);

  if (errLines.length > 0) {
    fail(`${errLines.length} línea(s) [ERR] durante el import:`);
    for (const l of errLines.slice(0, 5)) console.error('   ' + l);
  }

  if (summary.processed < EXPECTED) {
    fail(`procesó ${summary.processed} ítems pero el repo trackea ${EXPECTED} archivos — ` +
         'uSync se saltó parte del árbol (¿carpeta no montada? ¿handler apagado?)');
  }

  // El import reescribe plantillas Razor en disco (BOM incluido). Es ruido
  // conocido — se AVISA y no se toca nada: restaurar automáticamente pisaría
  // ediciones locales sin commitear.
  try {
    const dirty = execSync(
      'git status --porcelain -- Synergos.CMS.Web/Views Synergos.CMS.Web/uSync/v9',
      { cwd: ROOT, encoding: 'utf8' },
    ).trim();
    if (dirty) {
      log('⚠ el import tocó archivos trackeados (revisa antes de commitear):');
      for (const l of dirty.split('\n').slice(0, 10)) console.log('   ' + l);
    }
  } catch { /* sin git no hay aviso, pero tampoco gate — ya habría fallado arriba */ }

  if (process.exitCode !== 1) {
    log(`✓ reconstrucción OK — una DB vacía + el repo reproducen el entorno ` +
        `(${summary.processed}/${EXPECTED} ítems, 0 errores)`);
  }
}

function cleanup() {
  if (KEEP_DB) {
    log(`--keep-db: se conserva ${tmp}`);
    return;
  }
  if (process.exitCode === 1) {
    log(`falló — se conserva ${tmp} para diagnóstico`);
    return;
  }
  try { rmSync(tmp, { recursive: true, force: true }); } catch { /* temp del SO */ }
}
