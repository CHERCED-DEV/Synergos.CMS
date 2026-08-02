#!/usr/bin/env node
/**
 * Gate de HTML SERVIDO — verifica lo que el navegador recibe, no lo que el
 * controller devuelve.
 *
 * Nace de una constatación incómoda de la auditoría: de los cuatro defectos
 * reales que aparecieron, TRES vivían en el HTML emitido y sobrevivieron a
 * 1608 tests unitarios. En los tres el código estaba bien y el resultado no:
 *
 *   1. La cola de moderación tenía <form> anidados. HTML no lo permite y el
 *      parser descarta el de adentro, así que Aprobar/Rechazar/Spam de cada
 *      comentario quedaban asociados al form de bulk. Un test de controller
 *      no lo ve: el controller nunca se llegaba a invocar.
 *   2. /admin/members mostraba SIEMPRE 0 members (un string.Empty donde iba
 *      null). La página respondía 200 y bien formada — solo que vacía.
 *   3. Denegar emitía 302 a /Account/AccessDenied, ruta inexistente, que caía
 *      al "No published content" de Umbraco y respondía 200. Un 200 sobre una
 *      denegación.
 *
 * Los tres se ven en un segundo mirando la página, y ninguno se ve leyendo el
 * código. Este gate mira la página.
 *
 * Qué comprueba, y de qué defecto sale cada cosa:
 *
 *   ESTRUCTURA (defecto 1)
 *     - Ningún <form> anidado. Es la comprobación barata que no necesita
 *       navegador y atrapa exactamente el caso.
 *     - Si hay Chromium disponible: cuántos forms sobreviven al PARSER real
 *       frente a cuántos hay en la fuente. Es la verdad de campo, no una
 *       lectura del texto — así se confirmó el defecto original.
 *
 *   ANTIFORGERY (sale del mismo trabajo)
 *     - Cada form POST lleva EXACTAMENTE un __RequestVerificationToken. Dos
 *       hidden con el mismo nombre no se suman: StringValues los une con coma
 *       y la validación falla. Cero deja el POST abierto a CSRF.
 *
 *   DATOS (defecto 2)
 *     - Se siembra un member conocido y se exige que /admin/members lo
 *       MUESTRE. Es la única forma de distinguir "no hay datos" de "la
 *       consulta está mal", que es justo lo que nadie distinguió durante olas.
 *
 *   DENEGACIÓN (defecto 3)
 *     - Anónimo: ninguna ruta de admin responde 200, y siguiendo el redirect
 *       se aterriza en una página REAL — no en el placeholder de Umbraco.
 *     - Autenticado sin rol: 403 exacto.
 *
 *   HIGIENE (transversal)
 *     - Sin marcadores de fuga en el HTML servido: "undefined", "NaN",
 *       "[object Object]", Razor sin resolver, trazas de excepción.
 *
 * Decisiones de entorno, calcadas del gate de reconstrucción (ADR 0128):
 *   - DB SQLite en un directorio temporal. El gate JAMÁS toca una DB real.
 *   - Puerto 0: lo asigna el SO y se lee del log. Correrlo con el CMS del
 *     arquitecto levantado es seguro por construcción, no por disciplina.
 *   - uSync ImportAtStartup=None: el dashboard es MVC puro y no necesita el
 *     árbol de contenido. Se verificó que las 8 rutas renderizan con la DB
 *     vacía, y ahorra ~60s por corrida.
 *   - DevSeed=true, al revés que en el gate de reconstrucción: aquí se
 *     NECESITA sembrar roles para poder mirar las páginas protegidas. Es la
 *     única vía sin tocar la DB a mano.
 *
 * Uso:
 *   node tools/ssr-dom-check.mjs [--no-build] [--no-dom] [--timeout <s>]
 *                                [--log <ruta>] [--keep]
 *
 * Necesita el SDK de .NET. --no-dom salta las comprobaciones que requieren
 * Chromium (el resto sigue corriendo).
 */

import { spawn, execSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, createWriteStream, existsSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const WEB = join(ROOT, 'Synergos.CMS.Web');
const DLL = join(WEB, 'bin', 'Debug', 'net8.0', 'Synergos.CMS.Web.dll');

const args = process.argv.slice(2);
const flag = (n) => args.includes(n);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : d; };

const NO_BUILD = flag('--no-build');
const NO_DOM = flag('--no-dom');
const KEEP = flag('--keep');
const TIMEOUT_MS = Number(opt('--timeout', '240')) * 1000;

const log = (m) => console.log(`[ssr-dom] ${m}`);
const problems = [];
const fail = (m) => { problems.push(m); console.error(`[ssr-dom] ✗ ${m}`); };
const ok = (m) => console.log(`[ssr-dom]   ✓ ${m}`);

// ── Las páginas bajo prueba ─────────────────────────────────────────────────
// `marker` es una cadena que DEBE aparecer: sin ella, una página que devuelve
// 200 con el cuerpo equivocado pasaría el gate. Es lo que separa "responde" de
// "responde lo suyo".
const ADMIN_PAGES = [
  { path: '/admin/', marker: 'syn-admin' },
  // `requires` obliga a que la rama CON items se haya renderizado. Sin él, una cola
  // vacía pintaría el cartel de "Cola limpia" y el gate daría verde sin mirar el
  // marcado que importa.
  { path: '/admin/moderation/comments', marker: 'Moderación de comentarios', requires: 'bulk-form' },
  { path: '/admin/members', marker: 'Members' },
  { path: '/admin/forms', marker: 'syn-admin' },
  { path: '/admin/audit', marker: 'syn-admin' },
  { path: '/admin/analytics/search', marker: 'syn-admin' },
  { path: '/admin/health', marker: 'syn-admin' },
  { path: '/admin/webhooks/test', marker: 'syn-admin' },
];

// El placeholder que Umbraco sirve —con 200— cuando una ruta no resuelve a
// contenido publicado. Aterrizar aquí tras un redirect es el defecto 3.
const UMBRACO_PLACEHOLDER = 'No published content';

// Marcadores de fuga. Se buscan como texto plano en el HTML servido; cada uno
// apareció alguna vez en un render roto de este proyecto o de su UI hermana.
const LEAKS = [
  '[object Object]',
  '>undefined<',
  '>NaN<',
  '@Model.',
  '@Umbraco.',
  'System.NullReferenceException',
  'An unhandled exception',
];

const CREDS = { email: 'ssr-gate@synergos.local', password: 'SsrGate!2345678' };
const NOROLE = { email: 'ssr-gate-norole@synergos.local', password: 'SsrGate!2345678' };
// Testigo: no es el primero registrado ni el que inicia sesión. Existe para que la
// comprobación del roster no se satisfaga con el member "privilegiado" de la sesión.
const WITNESS = { email: 'ssr-gate-testigo@synergos.local', password: 'SsrGate!2345678' };
const SEEDED_EMAILS = [CREDS.email, NOROLE.email, WITNESS.email];

// ── Build ───────────────────────────────────────────────────────────────────
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

// ── Arranque con DB desechable ──────────────────────────────────────────────
const tmp = mkdtempSync(join(tmpdir(), 'ssr-dom-'));
const dbPath = join(tmp, 'ssr.sqlite.db');

// ── Cola de moderación con contenido ────────────────────────────────────────
// Sin esto el gate no vale para lo que se escribió. La página de moderación
// tiene DOS ramas: cola vacía pinta un cartel, cola con items pinta el form de
// bulk y las acciones por comentario — y era en ESA rama donde vivían los
// <form> anidados. Con una DB recién creada la cola sale vacía, así que el
// gate habría pasado en verde sin renderizar jamás el marcado defectuoso.
//
// El store de comentarios es un JSON por nodo bajo StorageRoot, y StorageRoot
// se resuelve con Path.Combine(ContentRoot, ...): una ruta ABSOLUTA gana, así
// que apuntándolo al temp el gate siembra sin tocar el App_Data del repo.
const commentsRoot = join(tmp, 'syn-comments');
const SEEDED_NODE = 1063;
mkdirSync(commentsRoot, { recursive: true });
writeFileSync(
  join(commentsRoot, `${SEEDED_NODE}.json`),
  JSON.stringify([1, 2, 3].map((i) => ({
    Id: `ssrgate${i}`.padEnd(32, '0'),
    NodeId: SEEDED_NODE,
    MemberKey: null,
    AuthorName: `Visitante ${i}`,
    Body: `Comentario ${i} sembrado por el gate de HTML servido.`,
    CreatedAtUtc: `2026-03-0${i}T12:00:00.0000000Z`,
    Approved: false,   // pendiente: es lo que hace que la cola pinte la rama con items
    ParentId: null,
    Likes: 0,
  }))),
  'utf8');

const logPath = opt('--log', join(tmp, 'ssr-dom.log'));
const appLog = createWriteStream(logPath);
log(`DB temporal: ${dbPath}`);
log(`log completo: ${logPath}`);

const child = spawn('dotnet', [DLL], {
  cwd: WEB,
  windowsHide: true,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Docker',
    ASPNETCORE_URLS: 'http://127.0.0.1:0',
    ConnectionStrings__umbracoDbDSN:
      `Data Source=${dbPath};Cache=Shared;Foreign Keys=True;Pooling=True`,
    ConnectionStrings__umbracoDbDSN_ProviderName: 'Microsoft.Data.Sqlite',
    uSync__Settings__ImportAtStartup: 'None',
    uSync__Settings__ExportOnSave: 'None',
    Synergos__DevSeed__Enabled: 'true',
    Synergos__Comments__StorageRoot: commentsRoot,
    Umbraco__CMS__Hosting__LocalTempStorageLocation: 'EnvironmentTemp',
    TMPDIR: tmp, TEMP: tmp, TMP: tmp,
  },
});

let baseUrl = null;
let buffered = '';
const LISTEN_RE = /Now listening on:\s*(http:\/\/[^\s]+)/;

const port = new Promise((res, rej) => {
  const timer = setTimeout(() => rej(new Error('timeout esperando el puerto')), TIMEOUT_MS);
  const onLine = (line) => {
    appLog.write(line + '\n');
    const m = line.match(LISTEN_RE);
    if (m && !baseUrl) {
      baseUrl = m[1].replace(/\/$/, '');
      clearTimeout(timer);
      res(baseUrl);
    }
  };
  for (const s of [child.stdout, child.stderr]) {
    s.setEncoding('utf8');
    s.on('data', (chunk) => {
      buffered += chunk;
      let nl;
      while ((nl = buffered.indexOf('\n')) >= 0) {
        onLine(buffered.slice(0, nl).trimEnd());
        buffered = buffered.slice(nl + 1);
      }
    });
  }
  child.on('exit', (code) => { clearTimeout(timer); rej(new Error(`el proceso murió (exit ${code})`)); });
});

// ── Cliente HTTP con tarro de cookies ───────────────────────────────────────
// Mínimo a propósito: el gate no debe traer dependencias nuevas (CLAUDE.md §6).
function makeJar() {
  const jar = new Map();
  return {
    header: () => [...jar].map(([k, v]) => `${k}=${v}`).join('; '),
    absorb: (res) => {
      for (const raw of res.headers.getSetCookie?.() ?? []) {
        const [pair] = raw.split(';');
        const i = pair.indexOf('=');
        if (i > 0) jar.set(pair.slice(0, i).trim(), pair.slice(i + 1).trim());
      }
    },
  };
}

async function req(url, { jar, method = 'GET', form, follow = false } = {}) {
  const headers = {};
  if (jar) {
    const c = jar.header();
    if (c) headers.cookie = c;
  }
  let body;
  if (form) {
    headers['content-type'] = 'application/x-www-form-urlencoded';
    body = new URLSearchParams(form).toString();
  }
  const res = await fetch(url, { method, headers, body, redirect: 'manual' });
  if (jar) jar.absorb(res);
  const text = await res.text();
  if (follow && res.status >= 300 && res.status < 400) {
    const loc = res.headers.get('location');
    if (loc) {
      const next = new URL(loc, url).toString();
      const hop = await req(next, { jar, follow: false });
      return { ...hop, status: hop.status, url: next, redirectedFrom: res.status };
    }
  }
  return { status: res.status, text, url, location: res.headers.get('location') };
}

// ── Comprobaciones sobre el HTML servido ────────────────────────────────────

/**
 * Profundidad de anidamiento de <form>. HTML no permite anidarlos: el parser
 * descarta el de adentro y sus controles pasan a pertenecer al de afuera —
 * exactamente el defecto de la cola de moderación. Esta comprobación no
 * necesita navegador, que es lo que la hace viable en cualquier CI.
 */
function nestedForms(html) {
  const tags = [...html.matchAll(/<\/?form\b/gi)];
  let depth = 0;
  let maxDepth = 0;
  for (const t of tags) {
    depth += t[0][1] === '/' ? -1 : 1;
    maxDepth = Math.max(maxDepth, depth);
  }
  return maxDepth;
}

/** Los <form ...> de la fuente, con su tag de apertura. */
function formTags(html) {
  return [...html.matchAll(/<form\b[^>]*>/gi)].map((m) => m[0]);
}

/**
 * Tokens de antiforgery por form. Se emparejan apertura y cierre en orden;
 * como el gate ya exige que NO haya anidamiento, emparejar con el siguiente
 * </form> es correcto.
 */
function tokensPerForm(html) {
  const out = [];
  for (const m of html.matchAll(/<form\b[^>]*>/gi)) {
    const close = html.indexOf('</form>', m.index);
    const body = html.slice(m.index, close < 0 ? html.length : close);
    out.push({
      tag: m[0],
      isPost: /method=["']post["']/i.test(m[0]),
      tokens: (body.match(/name="__RequestVerificationToken"/g) ?? []).length,
    });
  }
  return out;
}

function leaks(html) {
  return LEAKS.filter((l) => html.includes(l));
}

// ── DOM real vía Chromium (opcional) ────────────────────────────────────────
function findChromium() {
  const candidates = [
    process.env.CHROME_PATH,
    process.env.CHROMIUM_PATH,
    '/opt/pw-browsers/chromium-1194/chrome-linux/chrome',
    '/usr/bin/chromium', '/usr/bin/chromium-browser', '/usr/bin/google-chrome',
  ].filter(Boolean);
  for (const c of candidates) if (existsSync(c)) return c;
  // Cualquier build de chromium bajo PLAYWRIGHT_BROWSERS_PATH.
  const base = process.env.PLAYWRIGHT_BROWSERS_PATH;
  if (base && existsSync(base)) {
    try {
      const hit = execSync(`find ${base} -maxdepth 3 -name chrome -type f 2>/dev/null | head -1`, { encoding: 'utf8' }).trim();
      if (hit) return hit;
    } catch { /* sin find, se sigue sin DOM */ }
  }
  return null;
}

/**
 * Cuántos <form> sobreviven al parser REAL. Es la verdad de campo: la fuente
 * puede declarar doce y el navegador quedarse con uno, que es literalmente lo
 * que pasaba. Se sirve desde un archivo local — no se cargan estilos ni CDN,
 * y no hace falta: lo que se mide es la ESTRUCTURA que arma el parser.
 */
function domFormCount(chrome, html, name) {
  const file = join(tmp, `${name}.html`);
  const probe = `<script>document.title='SSRGATE:'+document.forms.length;</script>`;
  writeFileSync(file, html.replace(/<\/body>/i, probe + '</body>'), 'utf8');
  const out = execSync(
    `"${chrome}" --headless --disable-gpu --no-sandbox --virtual-time-budget=2000 --dump-dom "file://${file}" 2>/dev/null`,
    { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
  const m = out.match(/SSRGATE:(\d+)/);
  return m ? Number(m[1]) : null;
}

// ── Ejecución ───────────────────────────────────────────────────────────────
function cleanup() {
  try { child.kill(); } catch { /* ya murió */ }
  if (!KEEP) { try { rmSync(tmp, { recursive: true, force: true }); } catch { /* best effort */ } }
}

async function waitReady(base) {
  const deadline = Date.now() + TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const r = await fetch(`${base}/account/login`, { redirect: 'manual' });
      if (r.status === 200) return;
    } catch { /* todavía arrancando */ }
    await new Promise((r) => setTimeout(r, 1000));
  }
  throw new Error('la app no respondió a tiempo');
}

async function main() {
  const base = await port;
  log(`escuchando en ${base}`);
  await waitReady(base);

  // ── Sembrado: un member CON rol y otro SIN rol ────────────────────────────
  const admin = makeJar();
  await req(`${base}/account/register`, {
    jar: admin, method: 'POST',
    form: { email: CREDS.email, password: CREDS.password, displayName: 'SSR Gate' },
  });
  const seed = await req(
    `${base}/dev/seed-member-roles?email=${encodeURIComponent(CREDS.email)}&roles=admin`,
    { method: 'POST' });
  if (!seed.text.includes('"rolesAssigned":["admin"]')) {
    fail(`no se pudo asignar el rol admin al member de prueba: ${seed.text.slice(0, 200)}`);
    return;
  }
  await req(`${base}/account/login`, {
    jar: admin, method: 'POST',
    form: { emailOrUsername: CREDS.email, password: CREDS.password, returnUrl: '/' },
  });

  const noRole = makeJar();
  await req(`${base}/account/register`, {
    jar: noRole, method: 'POST',
    form: { email: NOROLE.email, password: NOROLE.password, displayName: 'SSR Gate Sin Rol' },
  });
  // Tarro PROPIO: con uno compartido la sesión queda iniciada tras el primer registro y los
  // siguientes no crean member. Se aprendió midiendo — un A/B con el tarro compartido daba
  // 1 vs 1 y hacía parecer que no había defecto.
  await req(`${base}/account/register`, {
    jar: makeJar(), method: 'POST',
    form: { email: WITNESS.email, password: WITNESS.password, displayName: 'SSR Gate Testigo' },
  });
  await req(`${base}/account/login`, {
    jar: noRole, method: 'POST',
    form: { emailOrUsername: NOROLE.email, password: NOROLE.password, returnUrl: '/' },
  });

  const chrome = NO_DOM ? null : findChromium();
  log(chrome ? `DOM real vía ${chrome}` : 'sin Chromium — se omiten las comprobaciones de DOM');

  // ── 1. Cada página, como admin ────────────────────────────────────────────
  log('páginas de admin (como admin):');
  for (const page of ADMIN_PAGES) {
    const res = await req(`${base}${page.path}`, { jar: admin });
    const name = page.path.replace(/\W+/g, '_');

    if (res.status !== 200) { fail(`${page.path} → ${res.status} (se esperaba 200)`); continue; }
    if (!res.text.includes(page.marker)) {
      fail(`${page.path} → 200 pero sin el marcador "${page.marker}": responde, pero no lo suyo`);
      continue;
    }
    if (page.requires && !res.text.includes(page.requires)) {
      fail(`${page.path} → falta "${page.requires}". El gate siembra los datos justo para que ` +
           'esta rama se renderice; sin ella no se está mirando el marcado que importa.');
      continue;
    }

    const before = problems.length;

    const depth = nestedForms(res.text);
    if (depth > 1) {
      fail(`${page.path} → <form> ANIDADOS (profundidad ${depth}). El parser descarta el de ` +
           'adentro y sus botones pasan a pertenecer al de afuera.');
    }

    // El conteo de tokens empareja cada apertura con el siguiente </form>, lo cual solo es
    // correcto SIN anidamiento. Con forms anidados el emparejamiento se corre y reporta
    // duplicados que no existen: un segundo error engañoso encima del real enseña a
    // desconfiar del gate, así que se calla hasta que la estructura esté sana.
    if (depth <= 1) {
      for (const f of tokensPerForm(res.text)) {
        if (f.isPost && f.tokens !== 1) {
          fail(`${page.path} → form POST con ${f.tokens} antiforgery token(s), debe ser 1 · ${f.tag.slice(0, 80)}`);
        }
        if (!f.isPost && f.tokens > 0) {
          fail(`${page.path} → form NO-POST con token (ruido) · ${f.tag.slice(0, 80)}`);
        }
      }
    }

    const found = leaks(res.text);
    if (found.length) fail(`${page.path} → fugas en el HTML: ${found.join(', ')}`);

    if (chrome) {
      const inSource = formTags(res.text).length;
      const inDom = domFormCount(chrome, res.text, name);
      if (inDom !== null && inDom !== inSource) {
        fail(`${page.path} → la fuente declara ${inSource} form(s) y el parser deja ${inDom}. ` +
             'El navegador está descartando marcado.');
      }
    }

    if (problems.length === before) ok(`${page.path}`);
  }

  // ── 2. Los datos sembrados se VEN, TODOS (defecto 2) ──────────────────────
  //
  // Exigir "aparece un member" NO alcanza, y esto se midió: con el defecto original
  // (memberTypeAlias: string.Empty) el roster mostraba 1 de 3 members, y el que sobrevivía
  // era precisamente el primero registrado —el mismo con el que el gate se autentica—. Una
  // comprobación de presencia habría dado verde sobre el defecto que este gate existe para
  // atrapar. Se exige el CONJUNTO completo, y por eso se siembra un tercer member que no es
  // ni el primero ni el que inicia sesión.
  const members = await req(`${base}/admin/members`, { jar: admin });
  const missing = SEEDED_EMAILS.filter((e) => !members.text.includes(e));
  if (missing.length) {
    fail(`/admin/members oculta ${missing.length} de ${SEEDED_EMAILS.length} members sembrados ` +
         `(${missing.join(', ')}). La página responde 200 y bien formada; la consulta es la que ` +
         'no trae todo — que es exactamente cómo se veía el defecto del roster.');
  } else {
    ok(`/admin/members muestra los ${SEEDED_EMAILS.length} members sembrados`);
  }

  // ── 3. Denegar de verdad (defecto 3) ──────────────────────────────────────
  log('denegación:');
  for (const page of ADMIN_PAGES) {
    const anon = await req(`${base}${page.path}`, { follow: false });
    if (anon.status === 200) {
      fail(`ANÓNIMO recibe 200 en ${page.path}`);
      continue;
    }
    const landed = await req(`${base}${page.path}`, { follow: true });
    if (landed.text.includes(UMBRACO_PLACEHOLDER)) {
      fail(`${page.path} → el anónimo aterriza en el placeholder de Umbraco ("${UMBRACO_PLACEHOLDER}"): ` +
           'la denegación redirige a una ruta que no existe.');
      continue;
    }
    if (landed.text.includes(page.marker) && page.marker !== 'syn-admin') {
      fail(`${page.path} → tras el redirect el anónimo termina viendo la propia página`);
      continue;
    }
    const denied = await req(`${base}${page.path}`, { jar: noRole });
    if (denied.status !== 403) {
      fail(`${page.path} → autenticado SIN rol recibe ${denied.status}, se esperaba 403`);
      continue;
    }
    ok(`${page.path} — anónimo ${anon.status} → página real, sin rol 403`);
  }
}

const timer = setTimeout(() => {
  fail(`timeout global (${TIMEOUT_MS / 1000}s) — revisa ${logPath}`);
  cleanup();
  process.exit(1);
}, TIMEOUT_MS);

main()
  .catch((e) => fail(e.message))
  .finally(() => {
    clearTimeout(timer);
    appLog.end();
    cleanup();
    if (problems.length) {
      console.error(`\n[ssr-dom] ✗ ${problems.length} problema(s) en el HTML servido.`);
      process.exit(1);
    }
    console.log('\n[ssr-dom] ✓ el HTML servido pasa: estructura, antiforgery, datos y denegación.');
  });
