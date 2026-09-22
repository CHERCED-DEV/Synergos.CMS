#!/usr/bin/env node
/**
 * compilan-las-vistas.mjs — las 401 vistas COMPILAN, y se sabe en seis segundos (#157).
 *
 * ── POR QUÉ EXISTE ─────────────────────────────────────────────────────────────
 *
 * En este repo un `dotnet build` verde NO dice nada sobre si una vista compila:
 * `ModelsMode=InMemoryAuto` fuerza `RazorCompileOnBuild=false`, así que TODA vista se
 * compila en caliente, al servirla. Eso ya costó dos veces:
 *
 *   #92  — un `@using` ausente en `_SynergosBridge.cshtml`: diez días con toda página
 *          de contenido en 500, la suite entera en verde.
 *   #157 — `TreatWarningsAsErrors` viajando a `deps.json` sin `NoWarn` ni `Nullable`:
 *          toda página con un hero en 500 desde el 17-sep.
 *
 * Lo único que lo cazaba era PEDIR LA PÁGINA (`humo-portada`, `humo-conectado`), y eso
 * tiene un límite que este gate cierra: **sólo ve las vistas que la portada renderiza**.
 * De las 401 del árbol, la portada toca un puñado. Las tres que este gate encontró al
 * escribirlo llevaban rotas sin que ninguna página del humo pasara por ellas:
 *
 *   · `Partials/Elements/Engagement/CommentThread.cshtml` — `@inject` de
 *     `Synergos.CMS.Interfaces.ICommentRepository`, un tipo que **ya no existe**: se
 *     partió en `ICommentReader`/`ICommentWriter` y la vista no se movió.
 *   · `Partials/Elements/Member/Gate.cshtml` y `Member/Profile.cshtml` — `@inject` de
 *     `Umbraco.Cms.Web.Common.Security.IMemberManager`, namespace equivocado: vive en
 *     `Umbraco.Cms.Core.Security`. Comprobado con una sonda de un tipo en C#.
 *
 * Las tres contestaban **500 permanente**, y ninguna de las 3288 pruebas lo veía.
 *
 * ── CÓMO ───────────────────────────────────────────────────────────────────────
 *
 * Se le pide al build lo que en el día a día tiene apagado: `RazorCompileOnBuild=true`.
 * El compilador de Razor pasa por las 401 y reporta como cualquier otro error de C#.
 *
 * ── LA ÚNICA EXENCIÓN, Y POR QUÉ NO SE PUEDE EVITAR ────────────────────────────
 *
 * `Synergos.CMS.Web.PublishedModels` **no existe en tiempo de build**: lo GENERA Umbraco
 * en memoria al arrancar, que es lo que `InMemoryAuto` significa. Así que las vistas
 * tipadas contra un modelo publicado dan `CS0234` sobre ese namespace y no hay forma de
 * que no lo den — no es deuda, es la definición del modo.
 *
 * Se exime **por el nombre del namespace**, no por fichero ni por código: un `CS0234`
 * sobre CUALQUIER otra cosa —un tipo borrado, un namespace mal escrito— es exactamente
 * lo que este gate existe para cazar, y eximir por código lo dejaría pasar.
 *
 * ── RED DE SEGURIDAD ───────────────────────────────────────────────────────────
 *
 * Si `RazorCompileOnBuild` dejara de surtir efecto, el build saldría verde sin mirar una
 * sola vista y este gate diría «✓ las 401 compilan» — el verde sobre el vacío que el #136
 * midió (12/12 sin mirar un proyecto). Dos suelos:
 *
 *   1. tiene que haber vistas en el disco (>100), o el descubrimiento está roto;
 *   2. tiene que haber aparecido **al menos un** `CS0234` de `PublishedModels`. Ése es el
 *      recibo de que Razor compiló de verdad: si Razor no corre, desaparecen. Y se
 *      mantiene solo — el día que `ModelsMode` cambie y esos modelos existan, este suelo
 *      se rompe y obliga a mirar, que es justo cuando la exención de arriba también sobra.
 *
 * Lo que este gate NO prueba —y va dicho para no mentir sobre su alcance— es que la vista
 * se RENDERICE: un `Model.Value<T>("aliasQueNoExiste")` compila y devuelve el default
 * (#118). Para eso está `humo-portada`. Y la generación de código del compilador en
 * caliente no es byte a byte la del build, así que un aviso que sólo aparezca allí —como
 * el CS8669 del #157— tampoco se ve acá; lo que lo cierra es que ese bit ya no viaja.
 *
 *   node tools/compilan-las-vistas.mjs [--configuration Release]
 */

import { execFileSync } from 'node:child_process';
import { readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const PROYECTO = join(ROOT, 'Synergos.CMS.Web', 'Synergos.CMS.Web.csproj');

/**
 * El namespace que Umbraco genera EN MEMORIA al arrancar (`ModelsMode=InMemoryAuto`).
 *
 * Se reconoce por las DOS mitades y no por el nombre completo, porque el compilador nunca
 * lo escribe junto: dice «the type or namespace name 'PublishedModels' does not exist in
 * the namespace 'Synergos.CMS.Web'». Buscar `Synergos.CMS.Web.PublishedModels` no casa con
 * NADA — y la primera versión de este gate hacía exactamente eso. No pasó en verde de
 * milagro: lo cazó el suelo de abajo, que exige que la exención haya disparado. Es la
 * lección de `feedback_a_gate_that_parses_source_needs_its_own_mutations` cobrada por su
 * propia red.
 */
const RAIZ = 'Synergos.CMS.Web';
const MODELOS_EN_MEMORIA = `'PublishedModels' does not exist in the namespace '${RAIZ}'`;

const log = (m) => console.log(`[vistas] ${m}`);

const arg = (n, d) => {
  const i = process.argv.indexOf(n);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : d;
};

/** Las vistas del disco, recorridas y no enumeradas. */
function vistas(dir, acc = []) {
  let entradas;
  try { entradas = readdirSync(dir); } catch { return acc; }
  for (const e of entradas) {
    const p = join(dir, e);
    if (statSync(p).isDirectory()) { if (e !== 'obj' && e !== 'bin') vistas(p, acc); }
    else if (e.endsWith('.cshtml')) acc.push(p);
  }
  return acc;
}

const encontradas = vistas(join(ROOT, 'Synergos.CMS.Web'));
log(`${encontradas.length} vistas en el disco`);

if (encontradas.length < 100) {
  console.error(
    `[vistas] ✗ sólo se vieron ${encontradas.length} .cshtml y el árbol tiene cientos. ` +
    'El descubrimiento está roto: sin esto, todo lo de abajo pasaría en verde sin mirar nada.');
  process.exit(1);
}

log('compilando las vistas (RazorCompileOnBuild=true)…');
let salida = '';
try {
  salida = execFileSync('dotnet', [
    'build', PROYECTO, '-t:Rebuild', '-p:RazorCompileOnBuild=true', '--nologo',
    '-c', arg('--configuration', 'Debug'),
  ], { cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024 });
} catch (e) {
  // Un build que falla es lo NORMAL acá: los CS0234 de los modelos en memoria lo tumban.
  salida = `${e.stdout ?? ''}${e.stderr ?? ''}`;

  // Pero que no haya `dotnet` NO es normal, y sin distinguirlo el gate cae en su red de
  // seguridad de más abajo y culpa a `RazorCompileOnBuild` — o sea manda a diagnosticar el
  // fichero equivocado. Es la distinción del #137: el problema no era que no avisara, era QUÉ
  // avisaba.
  if (e.code === 'ENOENT') {
    console.error(
      '[vistas] ✗ no se encontró `dotnet`. Este gate COMPILA, así que necesita el SDK en el '
      + 'PATH:\n        export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"');
    process.exit(1);
  }
}

// Una línea por diagnóstico, deduplicada: MSBuild repite el resumen al final.
const lineas = [...new Set(
  salida.split('\n')
    .map((l) => l.trim())
    .filter((l) => /\.cshtml\(\d+,\d+\): (error|warning) /.test(l)))];

const esModeloEnMemoria = (l) => l.includes(MODELOS_EN_MEMORIA);
const exentos = lineas.filter(esModeloEnMemoria);
const reales = lineas.filter((l) => !esModeloEnMemoria(l));

if (exentos.length === 0) {
  console.error(
    '[vistas] ✗ no apareció un solo CS0234 de ' + RAIZ + '.PublishedModels.\n' +
    '        Ése es el recibo de que Razor compiló: con `ModelsMode=InMemoryAuto` las\n' +
    '        vistas tipadas contra un modelo publicado SIEMPRE lo dan en tiempo de build.\n' +
    '        Si no está, o `RazorCompileOnBuild` dejó de surtir efecto —y entonces este\n' +
    '        gate estaría diciendo «compilan» sin haber compilado ninguna—, o el modo de\n' +
    '        modelos cambió, y entonces sobra también la exención de este fichero.');
  process.exit(1);
}

log(`${exentos.length} diagnóstico(s) exentos (modelos en memoria) — Razor compiló`);

if (reales.length > 0) {
  console.error(`\n[vistas] ✗ ${reales.length} vista(s) NO compilan:\n`);
  for (const l of reales) console.error('  ' + l.replace(ROOT + '/', ''));
  console.error(
    '\n[vistas] Estas vistas contestan 500 al servirlas, y NO las ve ningún `dotnet build`\n' +
    '        ni ninguna de las tres suites: `ModelsMode=InMemoryAuto` fuerza\n' +
    '        `RazorCompileOnBuild=false`, así que se compilan en caliente. Ver #92 y #157.');
  process.exit(1);
}

console.log(`[vistas] ✓ las ${encontradas.length} vistas compilan`);
