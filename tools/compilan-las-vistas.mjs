#!/usr/bin/env node
/**
 * compilan-las-vistas.mjs — las vistas COMPILAN, con el cuerpo incluido, y se sabe en
 * segundos (#157, #184).
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
 * El compilador de Razor pasa por las vistas y reporta como cualquier otro error de C#.
 * En DOS pasadas, y la segunda existe porque la primera, sola, mentía (#184).
 *
 * ── PRIMERA PASADA: TODAS, Y LA ÚNICA EXENCIÓN ─────────────────────────────────
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
 * ── EL RECIBO QUE TAPABA LO QUE EL GATE EXISTE PARA VER (#184) ─────────────────
 *
 * Esos `CS0234` son errores de DECLARACIÓN —caen en el `@inherits` y en el `@using`, o sea
 * en la clase y no en un método—, y con un error de declaración el compilador no llega a
 * informar los del CUERPO. De NINGUNA vista: las 401 son una sola compilación. Medido
 * sobre `89fe9340`: sin los modelos, **10** diagnósticos, todos exentos → «✓ 401/401»;
 * compilando sin las ocho vistas atadas, **25** errores reales en 17 vistas, siete de
 * ellas rotas en caliente — `BlogTag.cshtml` con las 8 `/blog/tag/*` en **500 en vivo**, y
 * `_GlobalAlert`/`_GlobalBanner`/`_GlobalModal` esperando a que un editor configurara un
 * aviso global para tumbar toda página que lo pintara.
 *
 * O sea que la primera pasada sólo ve lo que se declara: un `@inject` de un tipo borrado
 * (los tres de arriba), un `@model` o un `@using` que no resuelven. Lo que hay DENTRO de
 * una vista —el `Umbraco.` sin `UmbracoViewPage`, un error de sintaxis, un `??` sobre un
 * `int`— le queda debajo del recibo.
 *
 * ── SEGUNDA PASADA: EL CUERPO, SIN LAS ATADAS ──────────────────────────────────
 *
 * Se vuelve a compilar SIN las vistas atadas a `PublishedModels` (que en build no pueden
 * compilar, y en caliente sí) y **todo error que salga es real**, sin exención ninguna.
 *
 *   · Las atadas se derivan del DISCO —toda vista que nombre el namespace fuera de un
 *     comentario `@* *@`— y se cruzan con los ficheros que la primera pasada acusó. Tienen
 *     que ser el MISMO conjunto: si el disco ve una que el compilador no acusó, se la estaría
 *     sacando de la segunda pasada sin razón (y su cuerpo quedaría sin mirar, en silencio);
 *     si el compilador acusa una que el disco no ve, la derivación está rota.
 *   · Se sacan con un `.targets` escrito en un directorio temporal y enganchado con
 *     `CustomAfterMicrosoftCommonTargets`: nada del build de todos los días cambia.
 *   · Compila en su propia configuración (`CompilanLasVistas`), y no por gusto: esta pasada
 *     SALE BIEN, y deja un ensamblado con las vistas precompiladas que ningún build normal
 *     produce. En `bin/Debug` lo levantaría el próximo `dotnet run --no-build` o un humo con
 *     `--no-build`, y se estaría probando otra cosa que la que corre en producción.
 *
 * **Los CS86xx cuentan, y es una decisión.** En caliente NO rompen: allí no hay contexto
 * nullable salvo en las regiones con `#nullable enable`, y desde el #157 el bit de
 * `warningsAsErrors` ya no viaja al `deps.json`, así que como mucho son un aviso. Pero esta
 * pasada compila con la política del repo —`TreatWarningsAsErrors`, y `CS8600;CS8602;
 * CS8603;CS8618` escritos en `Directory.Build.props` como los que «no se negocian»— y ahí
 * son error. Eximirlos sería eximir POR CÓDIGO, que es justo lo que la primera pasada se
 * niega a hacer; y dejar que las vistas sean el único C# del repo donde esos cuatro sí se
 * negocian, sin que nadie lo haya escrito. Cuando el #184 los encontró eran doce: diez líneas
 * que arreglar, y dos más en `FormSubmissionDetail` que caían con su error de sintaxis.
 *
 * ── RED DE SEGURIDAD ───────────────────────────────────────────────────────────
 *
 * Si `RazorCompileOnBuild` dejara de surtir efecto, el build saldría verde sin mirar una
 * sola vista y este gate diría «✓ compilan» — el verde sobre el vacío que el #136 midió
 * (12/12 sin mirar un proyecto). Los suelos:
 *
 *   1. tiene que haber vistas en el disco (>100), o el descubrimiento está roto;
 *   2. la primera pasada tiene que dar **al menos un** `CS0234` de `PublishedModels`: si
 *      Razor no corre, desaparecen. Y se mantiene solo — el día que `ModelsMode` cambie y
 *      esos modelos existan, este suelo se rompe y obliga a mirar, que es justo cuando la
 *      exención de arriba también sobra;
 *   3. la segunda pasada tiene que decir cuántas vistas le entraron al generador (las del
 *      disco menos las atadas, exacto) — la exclusión por ruta ya falló una vez en silencio
 *      al escribirla: con `Remove` y rutas absolutas no sacaba NINGUNA;
 *   4. y, si sale verde, el ensamblado que produjo tiene que llevar dentro el identificador
 *      de CADA vista que se le pasó. Un verde sin las vistas compiladas no es un verde.
 *
 * Lo que este gate NO prueba —y va dicho para no mentir sobre su alcance— es que la vista
 * se RENDERICE: un `Model.Value<T>("aliasQueNoExiste")` compila y devuelve el default
 * (#118). Para eso está `humo-portada`. Tampoco mira el cuerpo de las atadas: sólo su
 * declaración. Y la compilación del build no es la de caliente: aquí están los implicit
 * usings del proyecto y allá no, así que un `@using` ausente para un método de extensión
 * —el #92— compila acá y revienta al servir; y un aviso que sólo aparezca allá —como el
 * CS8669 del #157— tampoco se ve. Lo que cierra ésos es pedir la página.
 *
 *   node tools/compilan-las-vistas.mjs [--configuration Release]
 */

import { execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const WEB = join(ROOT, 'Synergos.CMS.Web');
const PROYECTO = join(WEB, 'Synergos.CMS.Web.csproj');

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

/** En la FUENTE sí va junto: así lo escribe una vista que se ata a un modelo publicado. */
const NOMBRA_LOS_MODELOS = /\bSynergos\.CMS\.Web\.PublishedModels\b/;

/** La configuración de la segunda pasada: su ensamblado no puede caer donde cae el de siempre. */
const CONFIGURACION_DEL_CUERPO = 'CompilanLasVistas';

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

/** Una ruta comparable: Windows no distingue mayúsculas, y MSBuild y `readdir` pueden no coincidir. */
const clave = (p) => (process.platform === 'win32' ? resolve(p).toLowerCase() : resolve(p));

/** Una línea de diagnóstico, sin la ruta de la máquina ni el `[proyecto]` que MSBuild le cuelga. */
const corta = (l) => l.replace(/\s+\[[^\]]+\.csproj\]$/, '').split(ROOT + sep).join('').split(ROOT + '/').join('');

/** El fichero de un diagnóstico `ruta.cshtml(l,c): error …`. */
const ficheroDe = (l) => {
  const m = l.match(/^(?:\d+>)?(.+?\.cshtml)\(\d+,\d+\): (?:error|warning) /);
  return m ? m[1] : null;
};

/** Una línea por diagnóstico de vista, deduplicada: MSBuild repite el resumen al final. */
const diagnosticos = (salida) => [...new Set(
  salida.split('\n')
    .map((l) => l.trim())
    .filter((l) => /\.cshtml\(\d+,\d+\): (error|warning) /.test(l)))];

function compilar(configuracion, extra = []) {
  try {
    // `DOTNET_CLI_UI_LANGUAGE=en` y no es cosmético (#170): el recibo de la primera pasada se
    // reconoce por el TEXTO del CS0234, y con el SDK en español el compilador dice «El tipo o
    // el nombre del espacio de nombres 'PublishedModels' no existe…» (medido). Sin esto, en la
    // máquina del arquitecto el gate caía en su red de seguridad y culpaba a
    // `RazorCompileOnBuild` con Razor compilando perfectamente. Se fija acá, en el entorno del
    // hijo, para que el gate no dependa de cómo está configurado quien lo corre: ninguna
    // herramienta reconoce una salida por texto localizado.
    const salida = execFileSync('dotnet', [
      'build', PROYECTO, '-t:Rebuild', '-p:RazorCompileOnBuild=true', '--nologo',
      '-c', configuracion, ...extra,
    ], {
      cwd: ROOT, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024,
      env: { ...process.env, DOTNET_CLI_UI_LANGUAGE: 'en' },
    });
    return { salida, ok: true };
  } catch (e) {
    // Que no haya `dotnet` NO es un build que falla, y sin distinguirlo el gate cae en su red
    // de seguridad y culpa a `RazorCompileOnBuild` — o sea manda a diagnosticar el fichero
    // equivocado. Es la distinción del #137: el problema no era que no avisara, era QUÉ avisaba.
    if (e.code === 'ENOENT') {
      console.error(
        '[vistas] ✗ no se encontró `dotnet`. Este gate COMPILA, así que necesita el SDK en el '
        + 'PATH:\n        export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"');
      process.exit(1);
    }
    return { salida: `${e.stdout ?? ''}${e.stderr ?? ''}`, ok: false };
  }
}

const encontradas = vistas(WEB);
log(`${encontradas.length} vistas en el disco`);

if (encontradas.length < 100) {
  console.error(
    `[vistas] ✗ sólo se vieron ${encontradas.length} .cshtml y el árbol tiene cientos. ` +
    'El descubrimiento está roto: sin esto, todo lo de abajo pasaría en verde sin mirar nada.');
  process.exit(1);
}

// ── PRIMERA PASADA ───────────────────────────────────────────────────────────────

log('primera pasada: todas las vistas (RazorCompileOnBuild=true)…');
// Que falle es lo NORMAL: los CS0234 de los modelos en memoria lo tumban.
const primera = compilar(arg('--configuration', 'Debug'));
const lineas = diagnosticos(primera.salida);

const esModeloEnMemoria = (l) => l.includes(MODELOS_EN_MEMORIA);
const exentos = lineas.filter(esModeloEnMemoria);
const reales = lineas.filter((l) => !esModeloEnMemoria(l));

if (exentos.length === 0) {
  // El caso que más se ve NO es el de abajo: en `Development` el modo es `SourceCodeAuto`, y
  // si `umbraco/models/*.generated.cs` está en el árbol (en la máquina del arquitecto lo está;
  // en CI no, lo ignora `.gitignore`) el build lo compila y `PublishedModels` SÍ existe. Medido
  // al escribir el #184: sin esta rama el gate culpaba a `RazorCompileOnBuild`, que es mandar a
  // mirar el fichero equivocado (#137).
  const modelos = join(WEB, 'umbraco', 'models');
  let generados = [];
  try { generados = readdirSync(modelos).filter((f) => f.endsWith('.generated.cs')); } catch { /* no hay */ }
  if (generados.length > 0) {
    console.error(
      `[vistas] ✗ hay ${generados.length} modelos generados en ${relative(ROOT, modelos)}: en este árbol\n` +
      `        ${RAIZ}.PublishedModels existe en build, y el recibo de la primera pasada no puede\n` +
      '        aparecer. Este gate mide lo que mide CI, que no los tiene: correlo en un árbol sin\n' +
      '        ellos (un worktree limpio) o apartalos mientras corre.');
    process.exit(1);
  }
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
  console.error(`\n[vistas] ✗ ${reales.length} diagnóstico(s) de DECLARACIÓN en vistas:\n`);
  for (const l of reales) console.error('  ' + corta(l));
  console.error(
    '\n[vistas] Estas vistas contestan 500 al servirlas, y NO las ve ningún `dotnet build`\n' +
    '        ni ninguna de las tres suites: `ModelsMode=InMemoryAuto` fuerza\n' +
    '        `RazorCompileOnBuild=false`, así que se compilan en caliente. Ver #92 y #157.\n' +
    '        Y mientras estén, el compilador no informa los errores del CUERPO de ninguna\n' +
    '        vista (#184): arreglalas y volvé a correr, que puede haber más debajo.');
  process.exit(1);
}

// ── SEGUNDA PASADA ───────────────────────────────────────────────────────────────

// Las atadas, del DISCO. Sin los comentarios `@* *@`: las vistas explican en prosa por qué
// hacen lo que hacen, y un corte que no los quite se engaña con su propia explicación.
const atadas = encontradas.filter((v) =>
  NOMBRA_LOS_MODELOS.test(readFileSync(v, 'utf8').replace(/@\*[\s\S]*?\*@/g, '')));

// Y cruzadas con lo que acusó el compilador: dos métodos, un solo conjunto.
const acusadas = new Map(exentos.map((l) => [clave(ficheroDe(l)), ficheroDe(l)]));
const delDisco = new Set(atadas.map(clave));
const sinAcusar = atadas.filter((v) => !acusadas.has(clave(v)));
const sinAtar = [...acusadas].filter(([k]) => !delDisco.has(k)).map(([, f]) => f);

if (sinAcusar.length > 0 || sinAtar.length > 0) {
  console.error('[vistas] ✗ las vistas atadas a los modelos en memoria no cuadran con lo que acusó el compilador.');
  for (const v of sinAcusar) {
    console.error(`  · ${relative(ROOT, v)} nombra ${RAIZ}.PublishedModels y la primera pasada NO la acusó.`);
  }
  for (const f of sinAtar) {
    console.error(`  · ${relative(ROOT, f)} dio el CS0234 de los modelos y el disco no la ve atada.`);
  }
  console.error(
    '        Las atadas salen de la segunda pasada, que es la que mira el cuerpo: sacar una de\n' +
    '        más deja su cuerpo sin mirar EN SILENCIO. Si la nombra en un comentario de C# o en\n' +
    '        el cuerpo —la primera pasada sólo acusa declaraciones—, decidí qué es antes de\n' +
    '        tocar este gate.');
  process.exit(1);
}

const esperadas = encontradas.length - atadas.length;
log(`segunda pasada: el cuerpo de ${esperadas} vistas, sin las ${atadas.length} atadas a ${RAIZ}.PublishedModels…`);

// MSBuild: `%`, `$`, `@`, `'`, `;`, `?` y `*` se escapan como `%XX`; después, XML.
const msbuild = (p) => p
  .replace(/[%$@';?*]/g, (c) => `%${c.charCodeAt(0).toString(16).toUpperCase().padStart(2, '0')}`)
  .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

// `MatchOnMetadata="FullPath"` y no `Remove` a secas: dentro de un target, `Remove` compara el
// texto del item, y el item del glob es relativo (`Views\X.cshtml`). Con rutas absolutas no
// sacaba NINGUNA — medido al escribirlo: `RazorGenerate` seguía en 401 —, y de ahí el suelo 3.
const temporal = mkdtempSync(join(tmpdir(), 'compilan-las-vistas-'));
const enganche = join(temporal, 'sin-las-atadas.targets');
writeFileSync(enganche, `<Project>
  <!-- Lo escribe tools/compilan-las-vistas.mjs (#184) en un directorio temporal; no vive en el repo. -->
  <Target Name="CompilanLasVistas_SinLasAtadasAModelos" AfterTargets="ResolveRazorGenerateInputs">
    <ItemGroup>
${atadas.map((v) => `      <_AtadaAModelos Include="${msbuild(resolve(v))}" />`).join('\n')}
      <RazorGenerate Remove="@(_AtadaAModelos)" MatchOnMetadata="FullPath" MatchOnMetadataOptions="PathLike" />
    </ItemGroup>
    <Message Importance="high" Text="[vistas-2] entran=@(RazorGenerate->Count()) ensamblado=@(IntermediateAssembly->'%(FullPath)')" />
  </Target>
</Project>
`);

const inicio = Date.now();
let segunda;
try {
  segunda = compilar(CONFIGURACION_DEL_CUERPO, [
    `-p:CustomAfterMicrosoftCommonTargets=${enganche}`,
    // El suelo 3 lee un `Message`: que el logger de terminal no decida si se imprime.
    '-tl:off',
  ]);
} finally {
  rmSync(temporal, { recursive: true, force: true });
}

const recibo = segunda.salida.match(/\[vistas-2\] entran=(\d+) ensamblado=(.+?)\s*$/m);
if (!recibo) {
  console.error(
    '[vistas] ✗ la segunda pasada no dijo cuántas vistas le entraron al generador: el target que\n' +
    '        saca las atadas no corrió (`ResolveRazorGenerateInputs` cambió de nombre, o\n' +
    '        `CustomAfterMicrosoftCommonTargets` dejó de importarse). Sin eso no se sabe qué\n' +
    '        se compiló, y un verde de acá no diría nada.');
  process.exit(1);
}
if (Number(recibo[1]) !== esperadas) {
  console.error(
    `[vistas] ✗ a la segunda pasada le entraron ${recibo[1]} vistas y tenían que ser ${esperadas} ` +
    `(${encontradas.length} del disco menos ${atadas.length} atadas). La exclusión no hizo lo que dice.`);
  process.exit(1);
}

const reales2 = diagnosticos(segunda.salida).sort();
if (reales2.length > 0) {
  const vistasRotas = new Set(reales2.map((l) => clave(ficheroDe(l))));
  console.error(`\n[vistas] ✗ ${reales2.length} error(es) en el CUERPO de ${vistasRotas.size} vista(s):\n`);
  for (const l of reales2) console.error('  ' + corta(l));
  if (reales2.some(esModeloEnMemoria)) {
    console.error(
      `\n[vistas] Entre ellos hay un CS0234 de ${RAIZ}.PublishedModels: una vista atada que el disco\n` +
      '        no reconoció como tal (la nombra de otra forma). Tampoco ése se exime acá.');
  }
  console.error(
    '\n[vistas] La primera pasada no los ve: con un error de DECLARACIÓN —y los CS0234 de los\n' +
    '        modelos en memoria lo son— el compilador no informa los del cuerpo de NINGUNA\n' +
    '        vista (#184). Éstos son reales: en caliente la vista no compila y contesta 500\n' +
    '        (salvo los CS86xx, que rompen el build con la política del repo; ver la cabecera).');
  process.exit(1);
}

if (!segunda.ok) {
  console.error('[vistas] ✗ la segunda pasada falló sin un solo diagnóstico de vista. Lo último que dijo:\n');
  for (const l of segunda.salida.trim().split('\n').slice(-25)) console.error('  ' + corta(l.trim()));
  process.exit(1);
}

// Suelo 4: un verde sólo vale si lo compilado está DENTRO. Razor deja en el ensamblado un
// `RazorCompiledItem` por vista con su identificador (`/Views/…/X.cshtml`), en UTF-8.
const ensamblado = recibo[2].trim();
let bytes;
try {
  if (statSync(ensamblado).mtimeMs < inicio - 5000) throw new Error('viejo');
  bytes = readFileSync(ensamblado);
} catch {
  console.error(`[vistas] ✗ la segunda pasada salió verde y no dejó un ensamblado nuevo en ${ensamblado}.`);
  process.exit(1);
}
const compiladas = encontradas.filter((v) => !delDisco.has(clave(v)) && basename(v) !== '_ViewImports.cshtml');
const ausentes = compiladas.filter((v) =>
  !bytes.includes(Buffer.from('/' + relative(WEB, v).split(sep).join('/'), 'utf8')));
if (ausentes.length > 0) {
  console.error(
    `[vistas] ✗ la segunda pasada salió verde y ${ausentes.length} de ${compiladas.length} vistas no ` +
    'están en el ensamblado. Un verde sin las vistas compiladas no es un verde:');
  for (const v of ausentes.slice(0, 20)) console.error('  ' + relative(ROOT, v));
  process.exit(1);
}

log(`las ${compiladas.length} están en el ensamblado de la segunda pasada`);
console.log(
  `[vistas] ✓ las ${encontradas.length} vistas compilan: ${compiladas.length} con el cuerpo mirado ` +
  `(más ${esperadas - compiladas.length} _ViewImports), y las ${atadas.length} atadas a ` +
  `${RAIZ}.PublishedModels sólo en su declaración — su cuerpo, sólo en caliente.`);
