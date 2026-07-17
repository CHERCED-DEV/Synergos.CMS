#!/usr/bin/env node
/**
 * check-css-parity.mjs — Gate G-3: paridad clase↔CSS (anti-drift).
 *
 * Ola 2 del roadmap (refactor-docs/architecture/14-roadmap-maestro.md).
 * Vanilla Node — sin npm deps. Run desde la raíz del repo CMS:
 *
 *   node tools/check-css-parity.mjs
 *
 * Exit code 0 si toda clase `syn-*` emitida tiene una regla CSS de
 * respaldo (o está allowlisted); exit 1 si hay orphans no-allowlisted
 * (CI gating). Habría atrapado A-P1-1/2/3 (clases emitidas sin CSS:
 * `.syn-post__related`, gallery, data-table, etc.).
 *
 * ── Contrato ───────────────────────────────────────────────────────
 * "Cada clase `syn-*` emitida en un Razor view tiene una regla CSS de
 *  respaldo en wwwroot/css/*.css, o está declarada en la allowlist
 *  (hidratada por JS / modificador dinámico no-op / componente CDN)."
 *
 * ── Qué hace ───────────────────────────────────────────────────────
 *  (a) Escanea Synergos.CMS.Web/Views/**\/*.cshtml y extrae clases
 *      LITERALES `syn-[a-z0-9-]+` de:
 *        - atributos class="..." (HTML estático)
 *        - arrays/literales de clases C# ("syn-...")
 *      Ignora interpolaciones dinámicas: para tokens con `@` o `{`
 *      embebido (ej. `syn-foo--@variant`) extrae solo el prefijo
 *      estático antes del `@`/`{` (ej. `syn-foo--`), que se trata como
 *      prefijo dinámico (matchea allowlist por prefijo).
 *  (b) Escanea Synergos.CMS.Web/wwwroot/css/*.css y construye el set de
 *      clases con regla CSS (cualquier selector `.syn-...`).
 *  (c) Reporta clases emitidas SIN ninguna regla CSS de respaldo.
 *  (d) Allowlist en tools/css-parity-allowlist.txt — una clase o
 *      prefijo por línea (prefijo = termina en `-`, hace match de
 *      prefijo; clase exacta sino). `#` = comentario, líneas vacías
 *      ignoradas.
 */

import { readFileSync, readdirSync, existsSync, statSync } from 'node:fs';
import { resolve, dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
// tools/ → repo root (Synergos.CMS/)
const repoRoot = resolve(__dirname, '..');

const VIEWS_DIR = resolve(repoRoot, 'Synergos.CMS.Web', 'Views');
const CSS_DIR = resolve(repoRoot, 'Synergos.CMS.Web', 'wwwroot', 'css');
const ALLOWLIST_PATH = resolve(__dirname, 'css-parity-allowlist.txt');

const SYN_CLASS = /syn-[a-z0-9-]+/g; // clase canónica (kebab + BEM `--` `__`)

// ───────────────────────────────────────────────────────────────────
// Helpers
// ───────────────────────────────────────────────────────────────────

/** Recolecta recursivamente todos los archivos que matchean `ext`. */
function walk(dir, ext, acc = []) {
  if (!existsSync(dir)) return acc;
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    const st = statSync(full);
    if (st.isDirectory()) walk(full, ext, acc);
    else if (name.toLowerCase().endsWith(ext)) acc.push(full);
  }
  return acc;
}

/**
 * Dado un token candidato extraído de un class="..." o literal C#,
 * recorta cualquier parte dinámica. Si el token contiene `@` o `{`
 * (interpolación Razor / mustache), devuelve solo el prefijo estático
 * ANTES del primer `@`/`{`. Devuelve null si no queda un `syn-` válido.
 *
 * Ejemplos:
 *   "syn-foo"                  → "syn-foo"            (estático)
 *   "syn-foo--@variant"        → "syn-foo--"          (prefijo dinámico)
 *   "syn-foo--@(x ? \"a\":\"b\")" → "syn-foo--"        (prefijo dinámico)
 *   "syn-spacing-@scale"       → "syn-spacing-"        (prefijo dinámico)
 *   "@CurrentClass(...)"       → null                  (totalmente dinámico)
 */
function staticPrefix(raw) {
  let s = raw;
  const cut = Math.min(
    ...['@', '{'].map((ch) => {
      const i = s.indexOf(ch);
      return i === -1 ? Infinity : i;
    }),
  );
  if (cut !== Infinity) s = s.slice(0, cut);
  s = s.trim();
  if (!s.startsWith('syn-')) return null;
  // El recorte puede dejar basura no-clase; quedarnos con el primer
  // match `syn-...` canónico extendido para conservar el trailing `--`.
  const m = s.match(/^syn-[a-z0-9-]*/);
  if (!m) return null;
  const val = m[0];
  // Un `syn-` pelado o `syn` no aporta nada.
  if (val === 'syn-' || val === 'syn') return null;
  return val;
}

/**
 * Extrae clases emitidas de un archivo .cshtml.
 * Devuelve { exact: Set<string>, prefixes: Set<string> } donde
 * `prefixes` son tokens que terminaron en `-` (prefijos dinámicos,
 * ej. `syn-foo--`) y `exact` son clases literales completas.
 */
function extractEmitted(content, exact, prefixes, emittedAt, file) {
  const record = (token, lineGuess) => {
    if (token.endsWith('-')) {
      prefixes.add(token);
    } else {
      exact.add(token);
      if (!emittedAt.has(token)) emittedAt.set(token, `${file}`);
    }
  };

  // (1) Atributos class="..." y class='...'
  const classAttr = /\bclass\s*=\s*("([^"]*)"|'([^']*)')/g;
  let m;
  while ((m = classAttr.exec(content)) !== null) {
    const value = m[2] !== undefined ? m[2] : m[3];
    // Tokenizar por whitespace; cada token puede tener interpolación.
    for (const rawToken of value.split(/\s+/)) {
      if (!rawToken.includes('syn-')) continue;
      const tok = staticPrefix(rawToken);
      if (tok) record(tok);
    }
  }

  // (2) Literales C# string "syn-..." que construyen clases (arrays
  //     `new[] { "syn-..." }`, ternarios, variables = "syn-...").
  //     Solo string literals que ARRANCAN con syn-.
  //
  //     DISCRIMINACIÓN (clave para evitar falsos positivos): el char
  //     inmediatamente antes de la `"` de apertura decide:
  //       - `=` → es un ATRIBUTO HTML (id="syn-..", for="syn-..",
  //               name=, href=, aria-*=) NO una clase C#. Skip.
  //               (class="..." ya se captura en (1) con whitespace.)
  //       - `(` → primer arg de un call. En este repo eso es siempre
  //               `Add("syn-color-..", value)` = NOMBRE de CSS custom
  //               property (`--syn-color-..`), no una clase. Skip.
  //       Cualquier otro contexto (`{`, `,`, espacio, `?`, `:`, `=`
  //       con espacio) = literal de clase C# legítimo.
  const csLiteral = /"(syn-[^"]*)"/g;
  while ((m = csLiteral.exec(content)) !== null) {
    const inner = m[1];
    const before = m.index > 0 ? content[m.index - 1] : '';
    if (before === '=' || before === '(') continue; // attr HTML / CSS var name
    // Puede ser un literal multi-clase ("syn-a syn-a--x") — split.
    for (const rawToken of inner.split(/\s+/)) {
      if (!rawToken.includes('syn-')) continue;
      const tok = staticPrefix(rawToken);
      if (tok) record(tok);
    }
  }
}

/**
 * Construye el set de clases con regla CSS desde wwwroot/css/*.css.
 * Devuelve:
 *   - defined : Set de clases con selector exacto `.syn-x`.
 *   - blockRoots : Set de "raíces BEM" — para cada clase definida con
 *     `__` o `--`, la parte ANTES del primer `__`/`--`. Permite tratar
 *     una clase de BLOQUE (`syn-nav`) como cubierta si tiene hijos
 *     (`.syn-nav__link`) o modificadores (`.syn-nav--vertical`)
 *     estilizados, aunque el bloque pelado no tenga regla propia. Un
 *     bloque BEM con descendientes estilizados NO es drift.
 */
function extractCssClasses(cssDir) {
  const defined = new Set();
  const blockRoots = new Set();
  const files = existsSync(cssDir)
    ? readdirSync(cssDir).filter((f) => f.toLowerCase().endsWith('.css'))
    : [];
  for (const f of files) {
    const css = readFileSync(join(cssDir, f), 'utf8');
    // Quitar bloques de comentarios para no capturar `.syn-x` comentado.
    const stripped = css.replace(/\/\*[\s\S]*?\*\//g, '');
    // Capturar cualquier selector de clase `.syn-...`. El charset
    // incluye `\` por si hay escapes raros, pero el set canónico es
    // [a-z0-9_-].
    const sel = /\.(syn-[a-z0-9_-]+)/g;
    let m;
    while ((m = sel.exec(stripped)) !== null) {
      const cls = m[1];
      defined.add(cls);
      // Raíz BEM: corta en el primer `__` (element) o `--` (modifier).
      const cut = Math.min(
        cls.indexOf('__') === -1 ? Infinity : cls.indexOf('__'),
        cls.indexOf('--') === -1 ? Infinity : cls.indexOf('--'),
      );
      if (cut !== Infinity) blockRoots.add(cls.slice(0, cut));
    }
  }
  return { defined, blockRoots, fileCount: files.length };
}

/** Lee la allowlist → { exact: Set, prefixes: Array<string> }. */
function loadAllowlist(path) {
  const exact = new Set();
  const prefixes = [];
  if (!existsSync(path)) return { exact, prefixes };
  const lines = readFileSync(path, 'utf8').split(/\r?\n/);
  for (let line of lines) {
    line = line.trim();
    if (!line || line.startsWith('#')) continue;
    if (line.endsWith('-') || line.endsWith('*')) {
      // prefijo: `syn-foo-` o `syn-foo*` (normalizamos quitando `*`).
      prefixes.push(line.replace(/\*$/, ''));
    } else {
      exact.add(line);
    }
  }
  return { exact, prefixes };
}

function isAllowlisted(cls, allow) {
  if (allow.exact.has(cls)) return true;
  for (const p of allow.prefixes) {
    if (cls.startsWith(p)) return true;
  }
  return false;
}

// ───────────────────────────────────────────────────────────────────
// Main
// ───────────────────────────────────────────────────────────────────

function main() {
  const views = walk(VIEWS_DIR, '.cshtml');
  if (views.length === 0) {
    console.error(`[css-parity] No .cshtml files found under ${VIEWS_DIR}`);
    process.exit(1);
  }

  const emittedExact = new Set();
  const emittedPrefixes = new Set();
  const emittedAt = new Map(); // clase → primer archivo donde aparece

  for (const file of views) {
    const content = readFileSync(file, 'utf8');
    const rel = relative(repoRoot, file).replace(/\\/g, '/');
    extractEmitted(content, emittedExact, emittedPrefixes, emittedAt, rel);
  }

  const { defined, blockRoots, fileCount } = extractCssClasses(CSS_DIR);
  const allow = loadAllowlist(ALLOWLIST_PATH);

  // ¿Está cubierta una clase emitida?
  //   1. Regla exacta `.syn-x`                                    → sí
  //   2. Es raíz de bloque BEM con hijos/modificadores estilizados
  //      (`.syn-x__y` / `.syn-x--z` existen)                       → sí
  //      (bloque BEM semántico cuya pinta vive en descendientes).
  //   3. Es un MODIFICADOR (`syn-x--mod`) cuyo BLOQUE está estilizado
  //      (modificador no-op benigno sobre un bloque con CSS)        → sí
  //   4. Allowlisted                                               → sí
  // Cualquier otra = orphan (bug o FP a revisar).
  function isCovered(cls) {
    if (defined.has(cls)) return true;
    if (blockRoots.has(cls)) return true;
    const cut = Math.min(
      cls.indexOf('__') === -1 ? Infinity : cls.indexOf('__'),
      cls.indexOf('--') === -1 ? Infinity : cls.indexOf('--'),
    );
    if (cut !== Infinity) {
      const root = cls.slice(0, cut);
      if (defined.has(root) || blockRoots.has(root)) return true;
    }
    if (isAllowlisted(cls, allow)) return true;
    return false;
  }

  const orphans = [];
  for (const cls of emittedExact) {
    if (!isCovered(cls)) orphans.push(cls);
  }
  orphans.sort();

  // Reporte
  console.log('[css-parity] Gate G-3 — paridad clase↔CSS');
  console.log(`[css-parity]   views escaneadas : ${views.length} (.cshtml)`);
  console.log(`[css-parity]   CSS files        : ${fileCount} (wwwroot/css/*.css)`);
  console.log(`[css-parity]   clases CSS def.   : ${defined.size}`);
  console.log(`[css-parity]   clases emitidas   : ${emittedExact.size} exactas + ${emittedPrefixes.size} prefijos dinámicos`);
  console.log(`[css-parity]   allowlist         : ${allow.exact.size} exactas + ${allow.prefixes.length} prefijos`);

  if (orphans.length === 0) {
    console.log('[css-parity] OK — 0 orphans. Toda clase emitida tiene CSS o está allowlisted.');
    process.exit(0);
  }

  console.error('');
  console.error(`[css-parity] FAIL — ${orphans.length} clase(s) emitida(s) SIN regla CSS de respaldo:`);
  for (const cls of orphans) {
    const where = emittedAt.get(cls) || '?';
    console.error(`  ✗ .${cls}   (emitida en ${where})`);
  }
  console.error('');
  console.error('[css-parity] Cada orphan es un bug (clase sin estilo) o un falso');
  console.error('[css-parity] positivo legítimo. Si es legítimo (hidratada por JS,');
  console.error('[css-parity] modificador dinámico no-op, componente CDN), agregalo a');
  console.error(`[css-parity] tools/css-parity-allowlist.txt. Si es un bug, escribí el CSS.`);
  process.exit(1);
}

main();
