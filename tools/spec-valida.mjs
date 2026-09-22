#!/usr/bin/env node
/**
 * G-8 — el validador del spec de un vertical (#140, épica #139, doc 13 §4 y §9).
 *
 * DOS TRABAJOS, y el segundo es el que vale.
 *
 * (1) VALIDAR la forma de `docs/specs/<vertical>/spec.md`: que la cabecera esté, que declare los
 *     tres ejes y las tres preguntas, que lo que dice reusar EXISTA en el disco, que el eje
 *     transaccional hable el vocabulario del molde, y que la prosa no contradiga la cabecera.
 *
 * (2) DERIVAR el plan —la bajada de los doce sub-specs del doc 13 §5— y, en modo `--oraculo`,
 *     cruzarlo contra los ficheros que el vertical REALMENTE tiene. Eso es el piloto 0: lo
 *     construido es el oráculo, y lo que el plan pierde es un paso que el doc 12 no escribió.
 *
 * POR QUÉ LA CABECERA NO ES COSMÉTICA, que es la decisión de diseño de este fichero.
 * `a_gate_that_parses_source_needs_its_own_mutations` ya costó un verde falso en G-6 y un gate que
 * se engañaba con su propia explicación en el #126. Un validador que leyera PROSA con regex sale
 * verde sobre un spec incompleto, y un verde sobre el vacío se hereda. Así que los hechos que se
 * cruzan van en un bloque `---` con una gramática CERRADA, y una línea que el parser no entiende
 * RECHAZA en vez de saltarse: un parser tolerante es el verde sobre el vacío con otra cara.
 *
 * Y POR QUÉ EL PLAN NO SE LEE DE LA CABECERA. Si la cabecera enumerara sus ficheros, el plan
 * sería igual a la cabecera y el porcentaje no mediría nada — la tautología de
 * `feedback_contract_shape_needs_its_own_test`. El plan se deriva de DOS palabras y de las formas
 * de los ejes, aplicando las reglas de nombre del doc 12 §5. Lo que la cabecera declara —`crea:`,
 * `reusa:`— se comprueba contra el disco, no alimenta el plan.
 *
 *   node tools/spec-valida.mjs                       valida todos los specs
 *   node tools/spec-valida.mjs --oraculo=eventos     …y mide el plan contra el disco
 *   node tools/spec-valida.mjs --autoprueba          corre sus fixtures (todos los rechazos)
 *   node tools/spec-valida.mjs --ui-path=/tmp/ui     de dónde sale el registry de elementos
 */

import { readFileSync, existsSync, readdirSync, mkdtempSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const AQUI = dirname(fileURLToPath(import.meta.url));
const RAIZ = dirname(AQUI);

const args = process.argv.slice(2);
const flag = (n) => args.includes(n);
const opt = (n, d) => {
  const i = args.findIndex((a) => a === n || a.startsWith(n + '='));
  if (i < 0) return d;
  return args[i].includes('=') ? args[i].split('=').slice(1).join('=') : (args[i + 1] ?? d);
};

// ── Los rechazos ────────────────────────────────────────────────────────────

/**
 * Son CÓDIGOS y no mensajes porque un código es contrato: es lo que alguien escribe en un `if` y
 * lo que la autoprueba muta uno por uno.
 *
 * `cabecera_ilegible` NO estaba en la tabla del ticket y se añadió al escribirlo, por la razón de
 * arriba: un parser que se salta lo que no entiende deja pasar un spec incompleto, y eso es el
 * verde sobre el vacío que este fichero existe para no tener.
 */
export const RECHAZOS = [
  'spec.cabecera_ausente',
  'spec.cabecera_ilegible',
  'spec.eje_sin_declarar',
  'spec.pregunta_sin_contestar',
  'spec.reusa_inexistente',
  'spec.vocabulario',
  'spec.prosa_contradice_cabecera',
];

/** El vocabulario del eje transaccional. `ninguna` es el caso de Social (doc 12 §8). */
const FORMAS = ['Api', 'Bff', 'ninguna'];
const EJES = ['catalogo', 'transaccion', 'artefacto'];
const PREGUNTAS = ['deshacer', 'recurso_ajeno', 'quien_cobra'];

/**
 * La CUARTA pregunta (doc 12 §3.1) vive en el eje 3 y no en `preguntas:` a propósito: las tres de
 * §4 eligen la FORMA del eje 2 y ésta enciende un sub-spec del eje 3. Fundirlas sería el error que
 * el propio doc 12 §4 documenta cuatro veces.
 */
const SELLA = ['si', 'no'];

class Rechazo extends Error {
  constructor(codigo, detalle) {
    super(`${codigo}: ${detalle}`);
    this.codigo = codigo;
  }
}

// ── La cabecera ─────────────────────────────────────────────────────────────

/** Un escalar: desnudo, entre comillas, un mapa en línea o una lista en línea. */
function escalar(bruto) {
  const s = bruto.trim();
  if (s === '') return '';
  if (s.startsWith('[')) {
    if (!s.endsWith(']')) throw new Rechazo('spec.cabecera_ilegible', `lista sin cerrar: ${s}`);
    const dentro = s.slice(1, -1).trim();
    return dentro === '' ? [] : dentro.split(',').map((x) => escalar(x));
  }
  if ((s.startsWith('"') && s.endsWith('"')) || (s.startsWith("'") && s.endsWith("'"))) {
    return s.slice(1, -1);
  }
  if (s.startsWith('{')) {
    if (!s.endsWith('}')) throw new Rechazo('spec.cabecera_ilegible', `mapa sin cerrar: ${s}`);
    const o = {};
    // Se parte por comas que NO estén dentro de comillas: un `razon: "a, b"` es un valor.
    const partes = [];
    let actual = '';
    let comilla = null;
    for (const c of s.slice(1, -1)) {
      if (comilla) { if (c === comilla) comilla = null; actual += c; continue; }
      if (c === '"' || c === "'") { comilla = c; actual += c; continue; }
      if (c === ',') { partes.push(actual); actual = ''; continue; }
      actual += c;
    }
    partes.push(actual);
    for (const p of partes) {
      if (p.trim() === '') continue;
      const i = p.indexOf(':');
      if (i < 0) throw new Rechazo('spec.cabecera_ilegible', `par sin ':' dentro de un mapa: ${p.trim()}`);
      o[p.slice(0, i).trim()] = escalar(p.slice(i + 1));
    }
    return o;
  }
  return s;
}

/** Quita el comentario de fin de línea, pero NO lo que va dentro de comillas. */
function sinComentario(cruda) {
  let out = '';
  let comilla = null;
  for (const c of cruda) {
    if (comilla) { if (c === comilla) comilla = null; out += c; continue; }
    if (c === '"' || c === "'") { comilla = c; out += c; continue; }
    if (c === '#') break;
    out += c;
  }
  return out;
}

/**
 * Parsea el bloque `---`. Gramática CERRADA — cuatro formas y nada más:
 *   `clave: escalar` · `clave:` + hijos con sangría 2 · `clave: {…}` / `[...]` · `  - escalar`
 * Todo lo demás lanza `spec.cabecera_ilegible`.
 */
export function parsearCabecera(texto) {
  const lineas = texto.split('\n');
  if (lineas[0].trim() !== '---') {
    throw new Rechazo('spec.cabecera_ausente', 'el fichero no empieza con un bloque `---`');
  }
  const fin = lineas.findIndex((l, i) => i > 0 && l.trim() === '---');
  if (fin < 0) throw new Rechazo('spec.cabecera_ausente', 'el bloque `---` no se cierra');

  const cab = {};
  let padre = null;
  for (let i = 1; i < fin; i++) {
    if (lineas[i].trim() === '' || lineas[i].trim().startsWith('#')) continue;
    const linea = sinComentario(lineas[i]);
    if (linea.trim() === '') continue;

    const sangria = linea.length - linea.trimStart().length;
    const cuerpo = linea.trim();

    // Una secuencia de bloque: `  - escalar` bajo el padre. Hace falta para `rechazos:`, que lleva
    // «código · cuándo · transitorio» y no cabe en una lista en línea sin que las comas del texto
    // se coman los campos. Mezclar `- x` con `k: v` bajo el mismo padre es ilegible a propósito:
    // dos formas para un mismo valor es por dónde un parser empieza a adivinar.
    if (cuerpo === '-' || cuerpo.startsWith('- ')) {
      if (sangria !== 2) {
        throw new Rechazo('spec.cabecera_ilegible',
          `línea ${i + 1} es un elemento de lista con sangría ${sangria}: sólo 2`);
      }
      if (padre === null) {
        throw new Rechazo('spec.cabecera_ilegible', `línea ${i + 1} es un elemento sin padre: ${cuerpo}`);
      }
      const actual = cab[padre];
      if (Array.isArray(actual)) actual.push(escalar(cuerpo.slice(1)));
      else if (actual && typeof actual === 'object' && Object.keys(actual).length === 0) {
        cab[padre] = [escalar(cuerpo.slice(1))];
      } else {
        throw new Rechazo('spec.cabecera_ilegible',
          `línea ${i + 1}: «${padre}» ya tiene pares clave/valor y ahora recibe un elemento de lista`);
      }
      continue;
    }

    const j = cuerpo.indexOf(':');
    if (j < 0) throw new Rechazo('spec.cabecera_ilegible', `línea ${i + 1} sin ':': ${cuerpo}`);
    const clave = cuerpo.slice(0, j).trim();
    const valor = cuerpo.slice(j + 1);

    if (sangria === 0) {
      padre = null;
      if (valor.trim() === '') { cab[clave] = {}; padre = clave; }
      else cab[clave] = escalar(valor);
    } else if (sangria === 2) {
      if (padre === null || !cab[padre] || typeof cab[padre] !== 'object' || Array.isArray(cab[padre])) {
        throw new Rechazo('spec.cabecera_ilegible', `línea ${i + 1} indentada sin padre: ${cuerpo}`);
      }
      cab[padre][clave] = escalar(valor);
    } else {
      throw new Rechazo('spec.cabecera_ilegible',
        `línea ${i + 1} con sangría ${sangria}: sólo 0 y 2 (la gramática es cerrada a propósito)`);
    }
  }
  return { cabecera: cab, prosa: lineas.slice(fin + 1).join('\n') };
}

// ── Lo que el disco tiene ───────────────────────────────────────────────────

const dir = (...p) => join(RAIZ, ...p);
const listar = (d, filtro = () => true) => (existsSync(d) ? readdirSync(d).filter(filtro) : []);

/** Las veinte, derivadas del disco y no de una lista. */
export function capacidadesEnDisco(raiz = RAIZ) {
  return listar(join(raiz, 'backend', 'capacidades'), (n) => n.startsWith('Synergos.Api.'))
    .map((n) => n.replace(/^Synergos\./, ''))
    .sort();
}

/** Los del registry del repo hermano. Sin él no se puede comprobar, y se DICE. */
export function elementosPublicados(uiPath) {
  const f = join(uiPath, 'vitals', 'contracts', 'src', 'element-registry.json');
  if (!existsSync(f)) return null;
  const d = JSON.parse(readFileSync(f, 'utf8'));
  return (Array.isArray(d) ? d : (d.elements ?? [])).map((e) => e.name).filter(Boolean).sort();
}

// ── La bajada: el plan que el MOLDE predice ─────────────────────────────────

const mayus = (s) => s.charAt(0).toUpperCase() + s.slice(1);

/**
 * Los catorce sub-specs del doc 13 §5, cada uno con la regla de nombre del doc 12 §5.
 *
 * Se deriva de DOS palabras —`vertical` (la del interruptor: `Eventos`) y `sustantivo` (la del
 * árbol de contenido: `Event`)— y de las formas de los ejes. Las dos hacen falta y ninguna se
 * deduce de la otra: es lo que `Cada_vertical_tiene_su_EJE_1` dejó escrito al negarse a escribir
 * la tabla de siete filas — «el interruptor dice Eventos y la fuente dice Events».
 */
export function derivarPlan(cab) {
  const V = mayus(cab.vertical ?? '');
  const N = cab.sustantivo ?? '';
  const ejes = cab.ejes ?? {};
  const forma = (ejes.transaccion ?? {}).forma;
  const plan = [];
  const paso = (s, ruta, porque) => plan.push({ s, ruta, porque });

  // S1 · doc 12 §5.1 — el objeto central se puede autorar.
  for (const dt of cab.crea?.doctypes ?? []) {
    paso('S1', `Synergos.CMS.Web/uSync/v9/ContentTypes/${String(dt).toLowerCase()}.config`,
      'doc 12 §5.1 — un DocType por objeto autorable');
  }

  // S2 · doc 12 §5.2 — la fuente de contenido y su interruptor.
  if ((ejes.catalogo ?? {}).que !== 'ninguno') {
    paso('S2', `Synergos.CMS.Web/Services/Catalog/Umbraco${N}CatalogSource.cs`, 'doc 12 §5.2');
    paso('S2', `Synergos.CMS.Web/Services/Catalog/${N}ContentRules.cs`, 'doc 12 §5.2');
  }

  // S3 · doc 12 §5.3 — los seams, y la implementación EN PROCESO por defecto.
  //
  // El molde dice «un `I<X>Service`», en singular y derivado del sustantivo. Eso NO es lo que un
  // vertical tiene, y es el primer hallazgo del piloto 0: los seams son VARIOS —uno por
  // operación— y su nombre lleva la operación, no el sustantivo (`IEventTicketingService`,
  // `IEventManagementService`, `IEventCatalogProvider`). Así que cuáles hay es una DECISIÓN que
  // la cabecera declara —igual que `crea.doctypes`— y lo que se deriva es su RUTA y su
  // implementación en proceso, con las reglas de nombre del molde.
  //
  // Y el seam del SELLO no entra acá aunque el vertical lo declare: es S8, y su implementación en
  // proceso NO se llama `Stub<X>` (doc 12 §5.3, nota). `HmacTicketSigner` no es un doble de nada,
  // así que derivarlo por esta rama inventaba un `StubTicketSigner` que no existe ni debe (#155).
  const sello = (ejes.artefacto ?? {}).sello;
  for (const s of cab.crea?.seams ?? []) {
    if (sello && String(s) === String(sello)) continue;
    const sin = String(s).replace(/^I/, '');
    paso('S3', `Synergos.CMS.Interfaces/${s}.cs`, 'doc 12 §5.3 — el seam vive en Interfaces');
    paso('S3', `Synergos.CMS.Application/Services/Impl/Stub${sin}.cs`,
      'doc 12 §5.3 — la implementación en proceso es el default');
  }

  // S4–S6 los apaga `forma: ninguna` (doc 13 §5; el doc 12 §8 ya lo predice de Social).
  if (forma !== 'ninguna') {
    // S4 · doc 12 §5.4 — el POCO con los cuatro campos.
    paso('S4', `Synergos.CMS.Application/Configuration/${V}Settings.cs`, 'doc 12 §5.4');
    // S5 · doc 12 §5.5 — el interruptor, y ENLAZAR la sección.
    //
    // La RUTA de este paso no se deriva, y eso es el hallazgo #155: el doc 12 §5.5 enseña el
    // cableado como si cada vertical tuviera su fichero, y el árbol AGRUPA —`SeamComposer.
    // EventsPropertiesGov.cs` cablea tres—. Derivar `SeamComposer.<V>.cs` inventaba un fichero
    // que no existe ni debe existir; y quitar el paso sería peor, porque un vertical sin ningún
    // cableado sacaría 100 %.
    //
    // Así que el entregable de este paso no es un NOMBRE, es una PROPIEDAD: que algún composer
    // parcial enlace la sección del vertical. Se cruza por contenido — ver `composerQueEnlaza`.
    paso('S5', composerParcial(V), 'doc 12 §5.5 — el nombre del fichero no es del vertical (#155)');
    // S6 · doc 12 §5.6 — el cliente Http*, que se nombra por el SEAM que cablea y no por el
    // sustantivo: es el mismo hallazgo que S3 visto del otro lado.
    const seam = (ejes.transaccion ?? {}).seam;
    if (seam) {
      paso('S6', `Synergos.CMS.Web/Services/Http${String(seam).replace(/^I/, '')}.cs`, 'doc 12 §5.6');
    }
  }

  // ── S7/S8 · doc 12 §5.7 y §5.8 — el EJE 3, que hasta el #153 el molde no escribía ──────────
  //
  // Va aquí y no al final por el orden del doc 13 §5.1: el artefacto existe antes que la pantalla
  // que lo enseña, y —lo que de verdad lo fija— tiene que estar FUERA del seam de la transacción
  // antes de que haya dos implementaciones que compartirlo (doc 12 §3.2).
  //
  // Qué piezas tiene el artefacto es una DECISIÓN que la cabecera declara, igual que
  // `crea.doctypes` y `crea.seams`; lo que se deriva es su RUTA y su carpeta. Poner las rutas en
  // la cabecera sería el plan copiando al spec, y el porcentaje dejaría de medir nada (#140).
  const art = ejes.artefacto ?? {};
  const A = art.sustantivo ?? '';
  for (const pieza of cab.crea?.artefacto ?? []) {
    paso('S7', `Synergos.CMS.Application/Services/Impl/${pieza}.cs`,
      'doc 12 §5.7 — el artefacto vive FUERA del seam: lo comparten sus dos implementaciones');
  }
  // El gate del artefacto se nombra por su EMISIÓN (`EventTicketIssuanceTests`). `${N}${A}` se
  // colapsa cuando el artefacto ES el sustantivo del vertical, o saldría `EventEventIssuance`.
  if (A) {
    const NA = A === N ? A : `${N}${A}`;
    paso('S7', `Synergos.Arquitectura.Tests/Architecture/${NA}IssuanceTests.cs`, 'doc 12 §5.7');
  }
  // S8 · condicional — lo enciende la CUARTA pregunta (doc 12 §3.1), no `preguntas:`.
  if (art.sella === 'si' && sello) {
    const sinI = String(sello).replace(/^I/, '');
    paso('S8', `Synergos.CMS.Interfaces/${sello}.cs`, 'doc 12 §5.8 — el seam del sello');
    paso('S8', `Synergos.CMS.Application/Services/Impl/Hmac${sinI}.cs`,
      'doc 12 §5.8 — la del proceso es la de VERDAD, no un `Stub`');
    paso('S8', `Synergos.CMS.Web/Services/${A}SigningKeyProvider.cs`,
      'doc 12 §5.8 — la custodia: la llave, cifrada con IDataProtector');
    // Y su POCO, que es PROPIO y no un campo del de la transacción (doc 12 §5.8, #154): el
    // cliente del eje 2 recibe ese POCO y no tiene por qué llevar dentro la llave de firma. Su
    // sección va anidada —`Synergos:<V>:<A>`— porque una hermana acaba a UNA letra de la del
    // vertical, que fue el defecto #154.
    paso('S8', `Synergos.CMS.Application/Configuration/${A}Settings.cs`,
      'doc 12 §5.8 — el secreto del sello vive en su propio POCO');
  }

  // S9 · doc 12 §5.9 — la pantalla y las claves que cruzan.
  paso('S9', `Synergos.CMS.Web/Controllers/${V}Controller.cs`, 'doc 12 §5.9');
  // S10 · doc 12 §5.10 — el gate del vertical.
  paso('S10', `Synergos.Arquitectura.Tests/Architecture/${V}WiringTests.cs`, 'doc 12 §5.10');

  // S11/S12 · doc 13 §5 — el otro árbol. Se nombra la CARPETA: el plan del CMS no predice cuántos
  // ficheros tiene una app de Angular, y fingirlo sería inventar.
  if (cab.ui?.app) {
    paso('S11', `«UI» platforms/angular/apps/elements/modules/${cab.ui.app}/`, 'doc 13 §5 S11+S12');
  }

  // S13 · condicional — capacidad nueva sólo si pasa el filtro de atomicidad.
  for (const c of cab.crea?.capacidades ?? []) {
    paso('S13', `backend/capacidades/Synergos.${c}/`, 'doc 13 §5 S13 — condicional');
  }
  // S14 · condicional — orquestador sólo si hay algo que deshacer.
  if (forma === 'Bff') {
    paso('S14', `backend/orquestadores/Synergos.Bff.${V}/`,
      'doc 13 §5 S14 — `preguntas.deshacer: si` lo enciende');
  }
  return plan;
}

// ── El oráculo: lo que el vertical REALMENTE tiene ──────────────────────────

/**
 * Las carpetas donde vive un vertical, una por sub-spec. Es la única lista escrita a mano de este
 * fichero, y es del MOLDE y no de un vertical: si el doc 12 añade un paso, esta lista crece UNA
 * vez y los siete specs lo heredan.
 */
const CARPETAS = [
  ['S1', 'Synergos.CMS.Web/uSync/v9/ContentTypes', (n) => n.endsWith('.config')],
  ['S2', 'Synergos.CMS.Web/Services/Catalog', (n) => n.endsWith('.cs')],
  ['S3', 'Synergos.CMS.Interfaces', (n) => n.endsWith('.cs')],
  ['S3', 'Synergos.CMS.Application/Services/Impl', (n) => n.endsWith('.cs')],
  ['S4', 'Synergos.CMS.Application/Configuration', (n) => n.endsWith('.cs')],
  // S5 NO está acá, y no es un olvido: se cruza por CONTENIDO. Ver `composerQueEnlaza`. La
  // entrada que había —`Composers/` filtrada por prefijo de alias— no encontraba nada NUNCA,
  // porque todos los composers parciales se llaman `SeamComposer.*`: era un barrido muerto que
  // además hacía parecer que el paso estaba cubierto por nombre.
  ['S6', 'Synergos.CMS.Web/Services', (n) => n.endsWith('.cs')],
  ['S9', 'Synergos.CMS.Web/Controllers', (n) => n.endsWith('.cs')],
  ['S10', 'Synergos.Arquitectura.Tests/Architecture', (n) => n.endsWith('.cs')],
];

/**
 * Los prefijos de ROL del molde. No son un censo de un vertical: son las cuatro palabras con que
 * el doc 12 nombra los papeles — seam (§5.3), implementación en proceso (§5.3), cliente cableado
 * (§5.6) y fuente de catálogo (§5.2). Un fichero del vertical cuyo prefijo NO esté acá sale como
 * PERDIDO, y eso es el entregable: un papel que el molde no nombró.
 */
const ROLES = ['', 'I', 'Stub', 'Http', 'Umbraco', 'Hmac', 'Lazy'];

/**
 * El token canónico del paso S5. Lo emiten los DOS lados —el plan y el disco— porque lo que se
 * compara es una propiedad y no una ruta: el nombre del fichero no es del vertical (#155).
 */
const composerParcial = (V) =>
  `Synergos.CMS.Web/Composers/SeamComposer.*.cs (enlaza ${V}Settings)`;

/**
 * El composer parcial que ENLAZA la sección de este vertical, buscado por contenido.
 *
 * La señal es `services.Configure<{V}Settings>(…)`, que es literalmente lo que el doc 12 §5.5
 * manda hacer en este paso —«el interruptor, y ENLAZAR la sección»— y lo único que distingue
 * cablear un vertical de mencionarlo.
 *
 * SE QUITAN LOS COMENTARIOS, y es la mitad que importa: `SeamComposer.EventsPropertiesGov.cs`
 * dice «Eventos» una docena de veces en su prosa. Un cruce que mirara el fichero entero daría
 * por cableado a cualquier vertical que alguien haya nombrado de pasada, que es cómo un paso se
 * cumple sin hacerse. La autoprueba lleva ese caso exacto.
 */
function composerQueEnlaza(V, raiz) {
  const dir = join(raiz, 'Synergos.CMS.Web', 'Composers');
  const senal = new RegExp(`Configure<\\s*${V}Settings\\s*>`);
  for (const n of listar(dir, (f) => f.endsWith('.cs'))) {
    const crudo = readFileSync(join(dir, n), 'utf8');
    const desnudo = crudo
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .split('\n').filter((l) => !l.trimStart().startsWith('//')).join('\n');
    if (senal.test(desnudo)) return n;
  }
  return null;
}

/** ¿El basename es de este vertical? Prefijo tras un rol del molde, sin distinguir mayúsculas. */
function esDelVertical(base, alias) {
  const n = base.replace(/\.(cs|config)$/i, '').toLowerCase();
  return alias.some((a) => ROLES.some((r) => n.startsWith((r + a).toLowerCase())));
}

export function ficherosReales(cab, raiz = RAIZ) {
  const V = mayus(cab.vertical ?? '');
  // `alias:` son los OTROS sustantivos con que el árbol nombra a este vertical. Hacen falta y es
  // el segundo hallazgo del piloto 0: un vertical tiene más de un sustantivo —`Eventos` en el
  // interruptor, `Event` en el catálogo, `Ticket` en el artefacto— y sin declararlos el barrido
  // no VE el eje 3 y la medición saldría mejor de lo que es. Van con guarda: un alias que no casa
  // con ningún fichero rompe el oráculo, porque un censo vigilado en un solo sentido miente.
  const alias = [...new Set([V, cab.sustantivo, ...(cab.alias ?? [])].filter(Boolean))];
  const reales = [];
  for (const [s, carpeta, filtro] of CARPETAS) {
    for (const n of listar(join(raiz, carpeta), filtro)) {
      if (esDelVertical(n, alias)) reales.push({ s, ruta: `${carpeta}/${n}` });
    }
  }
  // S5 por contenido: el nombre del composer parcial no es del vertical (#155).
  const composer = composerQueEnlaza(V, raiz);
  if (composer) reales.push({ s: 'S5', ruta: composerParcial(V), fichero: `Composers/${composer}` });

  const bff = join(raiz, 'backend', 'orquestadores', `Synergos.Bff.${V}`);
  if (existsSync(bff)) reales.push({ s: 'S14', ruta: `backend/orquestadores/Synergos.Bff.${V}/` });
  return { reales, alias };
}

// ── La validación ───────────────────────────────────────────────────────────

export function validar(cab, prosa, ctx) {
  const fallos = [];
  const falla = (codigo, detalle) => fallos.push({ codigo, detalle });

  for (const c of ['vertical', 'sustantivo']) {
    if (!cab[c]) falla('spec.cabecera_ilegible', `falta \`${c}:\` — sin él no se deriva el plan`);
  }

  // Los tres ejes, y su `razon` cuando el eje es `ninguna`/`ninguno`.
  const ejes = cab.ejes;
  if (!ejes || typeof ejes !== 'object' || Array.isArray(ejes)) {
    falla('spec.eje_sin_declarar', 'falta el bloque `ejes:`');
  } else {
    for (const e of EJES) {
      const v = ejes[e];
      if (!v || typeof v !== 'object') { falla('spec.eje_sin_declarar', `falta el eje \`${e}\``); continue; }
      if ((v.forma === 'ninguna' || v.que === 'ninguno') && !v.razon) {
        falla('spec.eje_sin_declarar',
          `el eje \`${e}\` es ninguna y no dice por qué — un eje ausente es una decisión, no un hueco`);
      }
    }
  }

  // La CUARTA pregunta (doc 12 §3.1) y lo que enciende. Va en el eje 3 y no en `preguntas:`
  // porque las tres de §4 eligen la FORMA del eje 2 y ésta enciende S8.
  const art = (ejes && !Array.isArray(ejes) ? (ejes.artefacto ?? {}) : {});
  if (art.que !== 'ninguno') {
    if (art.sella === undefined || art.sella === '') {
      falla('spec.pregunta_sin_contestar',
        'falta `ejes.artefacto.sella` — ¿alguien de FUERA tiene que poder comprobar esto sin '
        + 'creernos? (doc 12 §3.1). Sin contestarla, S8 no se puede ni encender ni apagar');
    } else if (!SELLA.includes(String(art.sella))) {
      falla('spec.vocabulario',
        `\`ejes.artefacto.sella: ${art.sella}\` no es ${SELLA.join(' / ')}`);
    } else if (String(art.sella) === 'si') {
      if (!art.sello) {
        falla('spec.eje_sin_declarar',
          'con `sella: si` hace falta `sello:` — el NOMBRE del seam que firma, porque de él salen '
          + 'su `Hmac*` y su custodia (doc 12 §5.8)');
      }
      if (!art.sustantivo) {
        falla('spec.eje_sin_declarar',
          'con `sella: si` hace falta `sustantivo:` — la custodia se llama `<sustantivo>SigningKeyProvider`');
      }
    }
  }

  // Las tres preguntas, POR SEPARADO (doc 12 §4).
  const pr = cab.preguntas;
  if (!pr || typeof pr !== 'object' || Array.isArray(pr)) {
    falla('spec.pregunta_sin_contestar', 'falta el bloque `preguntas:`');
  } else {
    for (const p of PREGUNTAS) {
      if (pr[p] === undefined || pr[p] === '') {
        falla('spec.pregunta_sin_contestar',
          `falta \`${p}\` — fundir las tres es el error que costó cuatro olas (doc 12 §4)`);
      }
    }
  }

  // El vocabulario del eje transaccional (doc 12 §6.10).
  const forma = (ejes && !Array.isArray(ejes) ? (ejes.transaccion ?? {}) : {}).forma;
  if (forma !== undefined && !FORMAS.includes(forma)) {
    falla('spec.vocabulario',
      `\`ejes.transaccion.forma: ${forma}\` no es ${FORMAS.join(' / ')} — «si de verdad no es `
      + 'ninguna de las dos, lo que se queda corto es el doc 12» (§6.10)');
  }

  // `reusa:` contra el disco.
  for (const c of cab.reusa?.capacidades ?? []) {
    if (!ctx.capacidades.includes(c)) {
      falla('spec.reusa_inexistente',
        `la capacidad \`${c}\` no está en el disco. Hay ${ctx.capacidades.length}: ${ctx.capacidades.join(', ')}`);
    }
  }
  if (ctx.elementos === null) {
    if ((cab.reusa?.elementos ?? []).length > 0) {
      falla('spec.reusa_inexistente',
        'el spec declara elementos y NO se pudo leer el registry del repo hermano: pasá '
        + '--ui-path=/ruta o SYNERGOS_UI_PATH. Dar por bueno lo que no se pudo comprobar es el '
        + 'verde sobre el vacío');
    }
  } else {
    for (const e of cab.reusa?.elementos ?? []) {
      if (!ctx.elementos.includes(e)) {
        falla('spec.reusa_inexistente',
          `el elemento \`${e}\` no está entre los ${ctx.elementos.length} del registry`);
      }
    }
  }

  // La prosa no puede contradecir la cabecera. Es el diente que vale: sin él las dos mitades se
  // desvían y gana la que nadie cruza. Se leen las señales FUERA de código en línea y fuera de
  // las citas —un `>` que explique el molde nombra las formas prohibidas—, que es la misma razón
  // por la que el gate del #126 parsea la vista sin comentarios.
  const sinCitas = prosa.replace(/`[^`]*`/g, ' ').replace(/^\s*>.*$/gm, ' ');
  const SENALES = {
    transaccion: /\borquestador(?:es)?\b|\bsaga(?:s)?\b|\bcompensaci[oó]n\b/i,
    artefacto: /\bQR\b|\bdiploma\b|\bcertificad[oa]\b|\bradicado\b|\bexpediente\b|\bfirmante\b/i,
    catalogo: /\bDocType\b|\bbackoffice\b|\beditor\b/i,
  };
  for (const e of EJES) {
    const v = ejes && !Array.isArray(ejes) ? ejes[e] : null;
    if (!v || typeof v !== 'object') continue;
    if (!(v.forma === 'ninguna' || v.que === 'ninguno')) continue;
    const m = sinCitas.match(SENALES[e]);
    if (m) {
      falla('spec.prosa_contradice_cabecera',
        `la cabecera declara el eje \`${e}\` en ninguna y la prosa dice «${m[0]}»`);
    }
  }
  return fallos;
}

// ── El oráculo (piloto 0) ───────────────────────────────────────────────────

export function medirContraElDisco(cab, raiz = RAIZ) {
  const plan = derivarPlan(cab);
  const { reales, alias } = ficherosReales(cab, raiz);

  const enPlan = new Set(plan.map((p) => p.ruta.replace(/^«UI» /, '')));
  const enDisco = new Set(reales.map((r) => r.ruta));

  const cubiertos = [...enDisco].filter((r) => enPlan.has(r)).sort();
  const perdidos = [...enDisco].filter((r) => !enPlan.has(r)).sort();
  const inventados = plan
    .filter((p) => !p.ruta.startsWith('«UI» '))
    .filter((p) => !enDisco.has(p.ruta) && !existsSync(join(raiz, p.ruta)))
    .map((p) => `${p.s} ${p.ruta}`)
    .sort();

  const cobertura = enDisco.size === 0 ? 0 : cubiertos.length / enDisco.size;

  // Un alias que no casa con NADA es un censo mintiendo: se declaró para ver algo y no ve nada.
  const muertos = alias.filter((a) => ![...enDisco].some((r) => {
    const base = r.split('/').pop().replace(/\.(cs|config)$/i, '').toLowerCase();
    return ROLES.some((rol) => base.startsWith((rol + a).toLowerCase()));
  }));

  // Los pasos cuyo entregable es una PROPIEDAD y no una ruta dicen qué fichero los cumple: sin
  // esto, la salida afirmaría «S5 cruza» sin decir dónde, que es pedirle al lector que confíe.
  const notas = reales.filter((r) => r.fichero).map((r) => `${r.s} ← ${r.fichero}`).sort();

  return { plan, alias, muertos, cubiertos, perdidos, inventados, cobertura, notas, reales: enDisco.size };
}

// ── Autoprueba ──────────────────────────────────────────────────────────────

/**
 * Se EJECUTA el lector en vez de leerlo, que es lo que enseñó
 * `feedback_bash_ifs_whitespace_shifts_fields`: una regex sobre el parser no sabe cuáles de sus
 * campos pueden venir vacíos.
 *
 * Y el fixture base lleva DOS ejes en `ninguna` —el caso de Social—: con los tres ejes llenos,
 * «exige el eje» y «exige su razón» dan el mismo verde y el defecto pasa.
 */
const BASE = `---
vertical: social
sustantivo: Post
ejes:
  catalogo:    { doctype: postpage, fuente: UmbracoSocialCatalogSource }
  transaccion: { forma: ninguna, razon: "no hay nada que deshacer" }
  artefacto:   { que: ninguno, razon: "no queda prueba de nada" }
preguntas:
  deshacer:      no
  recurso_ajeno: no
  quien_cobra:   nadie
reusa:
  capacidades: []
  elementos:   []
crea:
  doctypes: [postpage]
---

## Qué problema del negocio resuelve
Publicar y comentar.
`;

function autoprueba() {
  const ctx = { capacidades: capacidadesEnDisco(), elementos: [] };
  const casos = [];
  const caso = (nombre, texto, esperado) => casos.push({ nombre, texto, esperado });

  caso('el fixture base valida', BASE, null);
  caso('cabecera ausente', BASE.replace(/^---\n/, ''), 'spec.cabecera_ausente');
  caso('cabecera sin cerrar', BASE.replace(/^---$/m, '').replace('---\n\n## Qué', '\n\n## Qué'),
    'spec.cabecera_ausente');
  caso('cabecera ilegible (sangría de 4)',
    BASE.replace('  deshacer:      no', '    deshacer:      no'), 'spec.cabecera_ilegible');
  caso('cabecera ilegible (línea sin dos puntos)',
    BASE.replace('  doctypes: [postpage]', '  doctypes'), 'spec.cabecera_ilegible');
  caso('falta el sustantivo', BASE.replace(/^sustantivo:.*$/m, ''), 'spec.cabecera_ilegible');
  caso('eje sin declarar (falta artefacto)',
    BASE.replace(/^  artefacto:.*$/m, ''), 'spec.eje_sin_declarar');
  caso('eje en ninguna SIN razón',
    BASE.replace('{ forma: ninguna, razon: "no hay nada que deshacer" }', '{ forma: ninguna }'),
    'spec.eje_sin_declarar');
  caso('pregunta sin contestar (sin quien_cobra)',
    BASE.replace(/^  quien_cobra:.*$/m, ''), 'spec.pregunta_sin_contestar');
  caso('reusa inexistente (capacidad)',
    BASE.replace('capacidades: []', 'capacidades: [Api.Reputation]'), 'spec.reusa_inexistente');
  caso('reusa inexistente (elemento)',
    BASE.replace('elementos:   []', 'elementos:   [no-existe]'), 'spec.reusa_inexistente');
  caso('vocabulario', BASE.replace('forma: ninguna, razon', 'forma: Directa, razon'),
    'spec.vocabulario');

  // La CUARTA pregunta (doc 12 §3.1). El fixture BASE tiene el eje 3 en `ninguno` —el caso de
  // Social—, así que estos cuatro tienen que DARLE un artefacto de verdad primero: con el eje
  // apagado la comprobación se salta y los cuatro pasarían en verde con la regla quitada.
  const CON_ARTEFACTO = (extra) => BASE.replace(
    'artefacto:   { que: ninguno, razon: "no queda prueba de nada" }',
    `artefacto:   { que: "la entrada con su QR"${extra} }`);
  caso('el eje 3 existe y NADIE contestó la cuarta pregunta',
    CON_ARTEFACTO(''), 'spec.pregunta_sin_contestar');
  caso('`sella` con una tercera palabra',
    CON_ARTEFACTO(', sella: quizá'), 'spec.vocabulario');
  caso('`sella: si` sin nombrar el seam que firma',
    CON_ARTEFACTO(', sella: si, sustantivo: Ticket'), 'spec.eje_sin_declarar');
  caso('`sella: si` sin sustantivo — la custodia no tendría nombre',
    CON_ARTEFACTO(', sella: si, sello: ITicketSigner'), 'spec.eje_sin_declarar');
  caso('un eje 3 bien declarado es válido',
    CON_ARTEFACTO(', sella: si, sello: ITicketSigner, sustantivo: Ticket'), null);
  caso('un eje 3 que NO sella también es válido',
    CON_ARTEFACTO(', sella: no'), null);
  caso('una secuencia de bloque es válida (hace falta para `rechazos:`)',
    BASE.replace('  doctypes: [postpage]',
      '  doctypes: [postpage]\nrechazos:\n  - "social.hilo_cerrado · al comentar · NO transitorio"'),
    null);
  caso('mezclar `- x` con `k: v` bajo el mismo padre',
    BASE.replace('  doctypes: [postpage]', '  doctypes: [postpage]\n  - suelto'),
    'spec.cabecera_ilegible');
  caso('la prosa contradice la cabecera (transaccion)',
    BASE + '\nLa compra la coordina el orquestador de Social.\n',
    'spec.prosa_contradice_cabecera');
  caso('la prosa contradice la cabecera (artefacto)',
    BASE + '\nAl publicar queda un certificado de autoría.\n',
    'spec.prosa_contradice_cabecera');
  caso('una CITA que nombra la forma prohibida NO la contradice',
    BASE + '\nA diferencia de Eventos, acá no hay `orquestador` ninguno.\n', null);

  let malos = 0;
  for (const c of casos) {
    let obtenido = null;
    try {
      const { cabecera, prosa } = parsearCabecera(c.texto);
      const fallos = validar(cabecera, prosa, ctx);
      obtenido = fallos.length ? fallos[0].codigo : null;
    } catch (e) {
      obtenido = e.codigo ?? `EXCEPCIÓN: ${e.message}`;
    }
    const ok = obtenido === c.esperado;
    if (!ok) malos++;
    console.log(`  ${ok ? '✓' : '✗'} ${c.nombre} → ${obtenido ?? 'válido'}`
      + `${ok ? '' : `  (se esperaba ${c.esperado ?? 'válido'})`}`);
  }

  // La guarda del oráculo: apuntar el spec a un vertical y medirlo contra los ficheros de OTRO
  // tiene que dar cero. Sin esta prueba, la cobertura podría estar midiendo cualquier cosa.
  const tmp = mkdtempSync(join(tmpdir(), 'spec-guarda-'));
  try {
    for (const [, carpeta] of CARPETAS) mkdirSync(join(tmp, carpeta), { recursive: true });
    writeFileSync(join(tmp, 'Synergos.CMS.Web/Controllers', 'EventosController.cs'), '// fixture');
    writeFileSync(join(tmp, 'Synergos.CMS.Application/Configuration', 'EventosSettings.cs'), '// fixture');
    const suyo = medirContraElDisco({ vertical: 'eventos', sustantivo: 'Event', ejes: {}, crea: {} }, tmp);
    const ajeno = medirContraElDisco({ vertical: 'realty', sustantivo: 'Property', ejes: {}, crea: {} }, tmp);
    const ok = suyo.reales === 2 && suyo.cobertura === 1 && ajeno.reales === 0 && ajeno.cobertura === 0;
    if (!ok) malos++;
    console.log(`  ${ok ? '✓' : '✗'} el oráculo mide el vertical que el spec NOMBRA `
      + `(eventos ${Math.round(suyo.cobertura * 100)} % sobre ${suyo.reales}; `
      + `realty ${ajeno.reales} fichero(s))`);

    // S5 se cruza por CONTENIDO (#155), así que hace falta el caso que el nombre no distingue:
    // un composer que MENCIONA al vertical en un comentario y no lo cablea. Sin este fixture, el
    // cruce pasaría en verde sobre un paso que nadie hizo — y `SeamComposer.EventsPropertiesGov.cs`
    // nombra «Eventos» una docena de veces en su prosa, así que no es un caso inventado.
    const comp = join(tmp, 'Synergos.CMS.Web/Composers');
    mkdirSync(comp, { recursive: true });
    const cab5 = { vertical: 'eventos', sustantivo: 'Event', ejes: { transaccion: { forma: 'Bff' } }, crea: {} };

    writeFileSync(join(comp, 'SeamComposer.Varios.cs'),
      '// OLA 6 Eventos — la app de eventos. Configure<EventosSettings> iría aquí.\n'
      + 'public sealed class X { }\n');
    const soloMencion = medirContraElDisco(cab5, tmp).plan
      .some((p) => p.s === 'S5') && medirContraElDisco(cab5, tmp).cubiertos
      .some((r) => r.includes('SeamComposer.*'));

    writeFileSync(join(comp, 'SeamComposer.Varios.cs'),
      '// OLA 6 Eventos\n'
      + 'public sealed class X { void C() { services.Configure<EventosSettings>(s); } }\n');
    const cablea = medirContraElDisco(cab5, tmp).cubiertos.some((r) => r.includes('SeamComposer.*'));

    const okS5 = !soloMencion && cablea;
    if (!okS5) malos++;
    console.log(`  ${okS5 ? '✓' : '✗'} S5 se cumple CABLEANDO y no mencionando `
      + `(sólo en comentario: ${soloMencion ? 'cuenta ✗' : 'no cuenta'}; `
      + `con Configure<>: ${cablea ? 'cuenta' : 'no cuenta ✗'})`);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }

  console.log(malos === 0
    ? '\n[spec-valida] ✓ autoprueba: todos los rechazos se ven'
    : `\n[spec-valida] ✗ ${malos} caso(s) mal`);
  return malos;
}

// ── Main ────────────────────────────────────────────────────────────────────

if (flag('--autoprueba')) process.exit(autoprueba() === 0 ? 0 : 1);

const uiPath = opt('--ui-path', process.env.SYNERGOS_UI_PATH ?? join(dirname(RAIZ), 'Synergos.UI'));
const ctx = { capacidades: capacidadesEnDisco(), elementos: elementosPublicados(uiPath) };

const especesDir = dir('docs', 'specs');
const specs = listar(especesDir, (n) => existsSync(join(especesDir, n, 'spec.md')));

if (specs.length === 0) {
  console.log('[spec-valida] no hay ningún docs/specs/<vertical>/spec.md — nada que validar');
  process.exit(0);
}

console.log(`[spec-valida] ${specs.length} spec(s) · ${ctx.capacidades.length} capacidades en disco`
  + ` · ${ctx.elementos === null ? 'registry NO leído (pasá --ui-path)' : `${ctx.elementos.length} elementos publicados`}`);

let fallos = 0;
for (const v of specs) {
  let cabecera, prosa;
  try {
    ({ cabecera, prosa } = parsearCabecera(readFileSync(join(especesDir, v, 'spec.md'), 'utf8')));
  } catch (e) {
    console.error(`  ✗ ${v}: ${e.message}`);
    fallos++;
    continue;
  }
  const malos = validar(cabecera, prosa, ctx);
  if (malos.length === 0) console.log(`  ✓ ${v}`);
  else {
    fallos += malos.length;
    for (const m of malos) console.error(`  ✗ ${v} · ${m.codigo}: ${m.detalle}`);
  }
}

// ── El oráculo, y por qué es TRINQUETE y no un umbral ───────────────────────
//
// El criterio del #140 es «≥ 90 %», y hoy Eventos da 71,4 %: al molde le falta el eje 3. Poner el
// 90 % como umbral absoluto dejaría `master` ROJO hasta que se cierren los hallazgos, y este repo
// ya tiene medido lo que pasa entonces —«un gate siempre rojo deja de leerse», y el propio
// `design-gates.yml` lo explica con cuatro corridas de ejemplo—. Así que la cifra va contra una
// LÍNEA BASE, como `contract-keys.baseline.json` y el presupuesto de tamaño del hermano: no falla
// por la deuda declarada, falla el día que la deuda CRECE.
//
// Y se vigila en los DOS sentidos. Un residual que deja de corresponder rompe igual, para que el
// commit que arregla el molde esté obligado a mover la línea base en vez de dejarla afirmando una
// deuda que ya no existe — es el segundo diente de
// `feedback_a_census_entry_is_how_a_defect_survives_its_own_gate`.
const BASELINE = dir('tools', 'spec-valida.baseline.json');
const OBJETIVO = 0.9;

const forzado = opt('--oraculo', null);
const conOraculo = specs.filter((v) => {
  if (forzado) return v === forzado;
  try {
    const { cabecera } = parsearCabecera(readFileSync(join(especesDir, v, 'spec.md'), 'utf8'));
    return cabecera.oraculo === 'si';
  } catch { return false; }
});

if (conOraculo.length > 0) {
  const base = existsSync(BASELINE) ? JSON.parse(readFileSync(BASELINE, 'utf8')) : {};
  const nueva = { objetivo: OBJETIVO, verticales: {} };

  for (const v of conOraculo) {
    const { cabecera } = parsearCabecera(readFileSync(join(especesDir, v, 'spec.md'), 'utf8'));
    const m = medirContraElDisco(cabecera);
    nueva.verticales[v] = {
      cobertura: Number(m.cobertura.toFixed(4)),
      reales: m.reales,
      inventados: m.inventados,
      perdidos: m.perdidos,
    };

    console.log(`\n[spec-valida] ORÁCULO · ${v} — lo construido es la verdad (piloto 0, #140)`);
    console.log(`  alias derivados del spec: ${m.alias.join(', ')}`);
    console.log(`  el plan deriva ${m.plan.length} ruta(s); el disco tiene ${m.reales}`);
    console.log(`  cobertura: ${(m.cobertura * 100).toFixed(1)} %  · objetivo ${OBJETIVO * 100} %`
      + `  (${m.cubiertos.length}/${m.reales})`);

    if (m.notas.length) {
      console.log(`\n  pasos cuyo entregable es una PROPIEDAD y no una ruta — quién los cumple:`);
      for (const n of m.notas) console.log(`      ${n}`);
    }
    if (m.inventados.length) {
      console.log(`\n  el plan INVENTA ${m.inventados.length} (no existen en el disco):`);
      for (const i of m.inventados) console.log(`      ${i}`);
    }
    if (m.perdidos.length) {
      console.log(`\n  el plan PIERDE ${m.perdidos.length} que el vertical sí tiene:`);
      for (const p of m.perdidos) console.log(`      ${p}`);
    }
    console.log('\n  Cada línea de esas dos listas es un HALLAZGO con su issue: un fichero que el');
    console.log('  plan pierde es un paso que el doc 12 no escribió (doc 13 §9, piloto 0).');

    if (m.muertos.length) {
      console.error(`\n  ✗ alias declarados que no casan con ningún fichero: ${m.muertos.join(', ')}`
        + ' — un censo vigilado en un solo sentido miente');
      fallos++;
    }

    // La cobertura se compara REDONDEADA a las mismas cuatro decimales con que se guarda: con
    // el valor crudo, 15/21 es «menor» que el 0.7143 del fichero y el trinquete se pone rojo
    // contra sí mismo. Es el primo del `feedback_a_clock_test_measures_a_property_it_cannot_own`
    // — un gate que falla por su propia aritmética enseña a ignorarlo.
    const cobertura = Number(m.cobertura.toFixed(4));
    const anterior = base.verticales?.[v];
    if (!anterior) {
      console.error(`\n  ✗ ${v} declara \`oraculo: si\` y no está en la línea base.`
        + ' Corré `node tools/spec-valida.mjs --actualizar` y el diff va en el commit que lo causó.');
      fallos++;
      continue;
    }
    if (cobertura < anterior.cobertura) {
      console.error(`\n  ✗ la cobertura BAJÓ: ${(cobertura * 100).toFixed(1)} % < `
        + `${(anterior.cobertura * 100).toFixed(1)} % de la línea base. Algo del vertical dejó de`
        + ' estar en lo que el molde predice.');
      fallos++;
    }
    for (const [etiqueta, ahora, antes] of [
      ['INVENTA', m.inventados, anterior.inventados ?? []],
      ['PIERDE', m.perdidos, anterior.perdidos ?? []],
    ]) {
      const nuevos = ahora.filter((x) => !antes.includes(x));
      const idos = antes.filter((x) => !ahora.includes(x));
      if (nuevos.length) {
        console.error(`\n  ✗ ${etiqueta}: ${nuevos.length} residual(es) NUEVO(s) — la deuda creció:`);
        for (const n of nuevos) console.error(`      ${n}`);
        fallos++;
      }
      if (idos.length) {
        console.error(`\n  ✗ ${etiqueta}: ${idos.length} residual(es) de la línea base que ya NO`
          + ' corresponde(n). Es lo que se quería — mové la línea base con `--actualizar`:');
        for (const i of idos) console.error(`      ${i}`);
        fallos++;
      }
    }
  }

  if (flag('--actualizar')) {
    nueva.generado = new Date().toISOString().slice(0, 10);
    writeFileSync(BASELINE, JSON.stringify(nueva, null, 2) + '\n');
    console.log(`\n[spec-valida] línea base regenerada en ${BASELINE}`);
    process.exit(0);
  }
}

if (fallos > 0) {
  console.error(`\n[spec-valida] ✗ ${fallos} fallo(s)`);
  process.exit(1);
}
console.log('\n[spec-valida] ✓ todo cruza');
