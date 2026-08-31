#!/usr/bin/env node
/**
 * usync-audit.mjs — Schema validation harness para uSync XMLs.
 *
 * Cap-270 Batch C (Ola 276). Vanilla Node — sin npm deps. Run:
 *
 *   node tools/usync-audit.mjs
 *
 * Exit code 0 si schema healthy; >0 si hay findings (CI gating).
 *
 * Checks:
 *   1. GUID collisions across ContentTypes + DataTypes + Dictionary
 *      (cualquier <Key>{guid}</Key> que aparezca >1 vez).
 *   2. Composition orphans: comp* definidas como ContentType pero
 *      nunca referenciadas Y sin marker [Bloqueado externamente -]
 *      o [Disponible — sin consumers actuales] al inicio del CDATA
 *      Description (memoria feedback_reserved_compositions_marker).
 *   3. Missing composition refs: <Composition Key="..."> con alias
 *      que no existe como ContentType.
 *   4. Iconos inválidos: <Icon> con nombre no presente en
 *      tools/umbraco13-icons-stock.txt (627 stock icons).
 *   5. Dictionary alias PascalCase: <Dictionary Alias="..."> debe
 *      matchear /^[A-Z][a-zA-Z]+(\.[A-Z][a-zA-Z]+)+$/ (canon contract
 *      docs/contracts/i18n-bridge.md).
 *   6. Definition GUID cross-check: cada <Definition>{guid}</Definition>
 *      en GenericProperties de ContentTypes debe apuntar a un DataType
 *      existente (<DataType Key="{guid}">) en uSync/v9/DataTypes/.
 *      Definition rota = property no carga el editor en backoffice.
 *      Cap-290 Batch C (Ola 295).
 *   7. DataType orphan: DataType custom (EditorAlias NO empieza con
 *      "Umbraco.") definido pero nunca referenciado por ningún
 *      <Definition>. Built-ins Umbraco se skipean (siempre legítimos).
 *      Warning level — el operador decide si es intencional.
 *      Cap-300 Batch B (Ola 299).
 *   9. Contenido del seeder: Content/ ya se versiona (ADR 0129), pero
 *      lo que crea DevTestContentSeeder no es trabajo editorial y no
 *      debe commitearse. Reemplaza a la regla de .gitignore que antes
 *      bloqueaba TODO el contenido para no dejar pasar el del seeder.
 *  11. Claves de Dictionary sin respaldo: toda GetDictionaryValue("X")
 *      SIN segundo argumento tiene que existir como <Dictionary Alias="X">.
 *      Sin respaldo y sin definir, Umbraco devuelve la CLAVE y el visitante
 *      ve una cadena técnica en la página — sin error y sin log.
 *   8. Mojibake hygiene: detecta byte sequences típicas de UTF-8 mal
 *      decodificado como Latin-1 y re-encodeado (PowerShell 5.1 trap).
 *      Patrones: Ã¡/Ã©/Ã­/Ã³/Ãº/Ã±/Â¿/Â¡. Error level — los XMLs uSync
 *      con mojibake muestran texto roto en backoffice.
 *      Cap-300 Batch B (Ola 299).
 *  10. Bloqueos externos declarados: todo marker [Bloqueado externamente]
 *      tiene que estar en la línea «**Bloqueos vigentes:**» de CLAUDE.md
 *      §9, y esa línea no puede nombrar bloqueos que el schema no tenga.
 *      Existe porque el check 2 EXIME a lo marcado, así que un bloqueo que
 *      terminó y nadie movió deja de vigilarse en silencio — pasó tres olas
 *      seguidas (#53). Error level: la exención sale gratis, mentir no.
 */

import { promises as fs } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(process.cwd(), 'Synergos.CMS.Web/uSync/v9');
const VIEWS = path.resolve(process.cwd(), 'Synergos.CMS.Web/Views');
const ICONS_STOCK_PATH = path.resolve(process.cwd(), 'tools/umbraco13-icons-stock.txt');

const RESERVED_MARKERS = [
    /\[Bloqueado externamente/,
    /\[Disponible — sin consumers actuales/,
    /\[Disponible - sin consumers actuales/, // ASCII dash variant
];

// Naming canónico real (no spec doc): primer segmento PascalCase
// (sección), resto alfanum case-insensitive (allow enum values like
// "Blog.PostType.article" o "Form.Stepper.next").
const PASCAL_CASE_DICT = /^[A-Z][a-zA-Z0-9]+(\.[a-zA-Z0-9]+)*$/;

const GUIA_PATH = path.resolve(process.cwd(), 'CLAUDE.md');

const findings = {
    errors: [],
    warnings: [],
};

/**
 * Los bloqueos que CLAUDE.md §9 declara VIGENTES hoy.
 *
 * Se lee UNA línea —`**Bloqueos vigentes:** ninguno.` o con los alias entre
 * backticks— y no la sección entera, a propósito: §9 es prosa, y esa prosa
 * nombra bloqueos pasados para explicarlos. Leer la sección completa haría que
 * contar la historia de un bloqueo levantado se leyera como declararlo vigente,
 * que es lo contrario de lo que dice.
 *
 * Devuelve `null` si no hay fichero, no hay §9 o no está la línea: contestar
 * «no hay bloqueos» cuando lo que pasa es que no se pudo leer la lista dejaría
 * el cruce verde por la razón equivocada — el defecto exacto que esto cierra.
 */
async function bloqueosDeclarados() {
    let texto;
    try {
        texto = await fs.readFile(GUIA_PATH, 'utf-8');
    } catch {
        return null;
    }

    const desde = texto.indexOf('## 9. Tareas bloqueadas externamente');
    if (desde < 0) return null;
    const hasta = texto.indexOf('\n## ', desde + 1);
    const seccion = hasta < 0 ? texto.slice(desde) : texto.slice(desde, hasta);

    const linea = seccion.split('\n').find((l) => l.includes('**Bloqueos vigentes:**'));
    if (!linea) return null;

    return [...matchAll(linea, /`([A-Za-z0-9_.]+)`/g)].map((m) => m[1]);
}

function err(category, message) {
    findings.errors.push(`[${category}] ${message}`);
}

function warn(category, message) {
    findings.warnings.push(`[${category}] ${message}`);
}

async function readFiles(dir, ext = '.config') {
    const out = [];
    let entries;
    try {
        entries = await fs.readdir(dir, { withFileTypes: true });
    } catch {
        return out;
    }
    for (const e of entries) {
        const p = path.join(dir, e.name);
        if (e.isDirectory()) {
            out.push(...(await readFiles(p, ext)));
        } else if (e.name.endsWith(ext)) {
            out.push(p);
        }
    }
    return out;
}

function* matchAll(text, re) {
    const r = new RegExp(re.source, re.flags.includes('g') ? re.flags : re.flags + 'g');
    let m;
    while ((m = r.exec(text)) !== null) yield m;
}

async function loadIconStock() {
    try {
        const content = await fs.readFile(ICONS_STOCK_PATH, 'utf-8');
        return new Set(
            content
                .split('\n')
                .map((l) => l.trim())
                .filter(Boolean),
        );
    } catch {
        warn('icon-stock', `Cannot read ${ICONS_STOCK_PATH} — skipping icon validation`);
        return null;
    }
}

async function audit() {
    const contentTypes = await readFiles(path.join(ROOT, 'ContentTypes'));
    const dataTypes = await readFiles(path.join(ROOT, 'DataTypes'));
    const dictionary = await readFiles(path.join(ROOT, 'Dictionary'));
    const iconsStock = await loadIconStock();

    // ─── 1. GUID collisions ─────────────────────────────────────────
    // SOLO el root element (primer match en el file). Refs nested como
    // <Structure><ContentType Key="..."/> son allowed-content
    // references, NO definiciones — false positives.
    const keyOccurrences = new Map(); // key → [filenames]
    const allFiles = [...contentTypes, ...dataTypes, ...dictionary];
    const rootKeyRegex = /<(?:ContentType|DataType|Dictionary)\s+Key="([0-9a-fA-F-]{36})"/;
    for (const file of allFiles) {
        const text = await fs.readFile(file, 'utf-8');
        const m = text.match(rootKeyRegex);
        if (!m) continue;
        const key = m[1].toLowerCase();
        if (!keyOccurrences.has(key)) keyOccurrences.set(key, []);
        keyOccurrences.get(key).push(file);
    }
    for (const [key, files] of keyOccurrences) {
        if (files.length > 1) {
            err('guid-collision', `${key} en: ${files.map((f) => path.relative(ROOT, f)).join(', ')}`);
        }
    }

    // ─── 2 + 3. Compositions defined / referenced ──────────────────
    const definedTypes = new Map(); // alias → { file, isComposition, descriptionFirstChars }
    const referencedComps = new Set();

    for (const file of contentTypes) {
        const text = await fs.readFile(file, 'utf-8');
        const aliasMatch = text.match(/<ContentType[^>]*\sAlias="([^"]+)"/);
        if (!aliasMatch) continue;
        const alias = aliasMatch[1];
        const isComposition = alias.startsWith('comp');
        const descMatch = text.match(/<Description><!\[CDATA\[([^\]]{0,200})/);
        definedTypes.set(alias, {
            file,
            isComposition,
            description: descMatch ? descMatch[1] : '',
        });

        for (const m of matchAll(text, /<Composition\s+Key="[^"]+">([^<]+)<\/Composition>/g)) {
            referencedComps.add(m[1]);
        }
    }

    // 2. Orphan compositions (defined but never referenced + no marker)
    for (const [alias, info] of definedTypes) {
        if (!info.isComposition) continue;
        if (referencedComps.has(alias)) continue;
        const isReserved = RESERVED_MARKERS.some((re) => re.test(info.description));
        if (isReserved) continue;
        warn('orphan-composition',
            `${alias} (${path.relative(ROOT, info.file)}) sin consumers Y sin marker [Bloqueado/Disponible —]`);
    }

    // 3. Missing composition refs
    for (const ref of referencedComps) {
        if (!definedTypes.has(ref)) {
            err('missing-composition-ref',
                `<Composition>${ref}</Composition> referenciada pero no existe ContentType con ese Alias`);
        }
    }

    // ─── 10. Los bloqueos externos, contra la lista que los declara ────
    //
    // El check 2 EXIME del chequeo de huérfanas a lo que lleve marker, y esa
    // exención es load-bearing: mientras el marker esté puesto, esa composition
    // deja de vigilarse. El defecto que esto cierra (#53) es que el marker
    // afirmaba un bloqueo que ya no existía —el contrato que decía esperar se
    // había entregado tres olas antes— así que la auditoría estaba verde por
    // una razón falsa, y nadie se enteró porque nada cruzaba las dos listas.
    //
    // Se cruzan en los DOS sentidos, y la segunda mitad importa tanto como la
    // primera: §9 llegó a nombrar tres artefactos que NUNCA existieron, que es
    // peor que no decir nada — parece trabajo identificado y es una hora
    // perdida antes de descubrir que no hay nada ahí.
    const bloqueadas = [...definedTypes]
        .filter(([, info]) => /\[Bloqueado externamente/.test(info.description))
        .map(([alias]) => alias);

    const declarados = await bloqueosDeclarados();
    if (declarados === null) {
        err('bloqueo-sin-lista',
            'CLAUDE.md §9 no tiene su línea «**Bloqueos vigentes:**». Sin ella no hay contra qué '
            + 'cruzar los markers del schema, y la exención del check 2 vuelve a ser invisible.');
    } else {
        for (const alias of bloqueadas) {
            if (!declarados.includes(alias)) {
                err('bloqueo-sin-declarar',
                    `${alias} lleva marker [Bloqueado externamente] y no está en los bloqueos `
                    + 'vigentes de CLAUDE.md §9. Un bloqueo fuera de la lista deja de vigilarse '
                    + 'sin que nadie lo decida.');
            }
        }

        for (const alias of declarados) {
            if (!bloqueadas.includes(alias)) {
                err('bloqueo-fantasma',
                    `CLAUDE.md §9 declara ${alias} como bloqueo vigente y el schema no lo tiene `
                    + 'marcado (o no existe). Mandar a alguien a buscar algo que no está es peor '
                    + 'que callarse: parece trabajo identificado y es una hora perdida.');
            }
        }
    }

    // ─── 4. Iconos inválidos ───────────────────────────────────────
    if (iconsStock) {
        for (const file of contentTypes) {
            const text = await fs.readFile(file, 'utf-8');
            for (const m of matchAll(text, /<Icon>([^\s<]+)/g)) {
                const icon = m[1];
                if (!iconsStock.has(icon)) {
                    err('invalid-icon',
                        `${icon} en ${path.relative(ROOT, file)} — no existe en stock 627`);
                }
            }
        }
    }

    // ─── 5. Dictionary alias PascalCase ────────────────────────────
    for (const file of dictionary) {
        const text = await fs.readFile(file, 'utf-8');
        const m = text.match(/<Dictionary[^>]*\sAlias="([^"]+)"/);
        if (!m) continue;
        const alias = m[1];
        if (!PASCAL_CASE_DICT.test(alias)) {
            err('dictionary-naming',
                `${alias} en ${path.relative(ROOT, file)} — debe ser PascalCase con dots ({Section}.{SubSection}.{Key})`);
        }
    }

    // ─── 6. Definition GUID cross-check (Cap-290 Batch C) ──────────
    // Cada <Definition>{guid}</Definition> en GenericProperty de un
    // ContentType debe matchear el <DataType Key="{guid}"> de un
    // DataType file. Definition rota → property silenciosamente cae
    // a un editor inválido en backoffice.
    const dataTypeMeta = new Map(); // key lowercase → { file, alias, editorAlias }
    const dataTypeKeyRegex = /<DataType\s+Key="([0-9a-fA-F-]{36})"\s+Alias="([^"]+)"/;
    const editorAliasRegex = /<EditorAlias>([^<]+)<\/EditorAlias>/;
    for (const file of dataTypes) {
        const text = await fs.readFile(file, 'utf-8');
        const m = text.match(dataTypeKeyRegex);
        if (!m) continue;
        const editorMatch = text.match(editorAliasRegex);
        dataTypeMeta.set(m[1].toLowerCase(), {
            file,
            alias: m[2],
            editorAlias: editorMatch ? editorMatch[1] : '',
        });
    }
    const referencedDefinitions = new Set();
    const definitionRefRegex = /<Definition>([0-9a-fA-F-]{36})<\/Definition>/g;
    for (const file of contentTypes) {
        const text = await fs.readFile(file, 'utf-8');
        for (const m of matchAll(text, definitionRefRegex)) {
            const guid = m[1].toLowerCase();
            referencedDefinitions.add(guid);
            if (!dataTypeMeta.has(guid)) {
                err('missing-datatype-definition',
                    `${guid} referenciado en ${path.relative(ROOT, file)} no existe como <DataType Key>`);
            }
        }
    }

    // ─── 7. DataType orphan (Cap-300 Batch B) ──────────────────────
    // Custom DataTypes (EditorAlias no empieza con "Umbraco.") sin
    // consumers son potencialmente dead weight. Built-ins Umbraco se
    // skipean siempre — son parte del runtime aún cuando un site no
    // los use directamente.
    for (const [guid, meta] of dataTypeMeta) {
        if (meta.editorAlias.startsWith('Umbraco.')) continue;
        if (referencedDefinitions.has(guid)) continue;
        warn('orphan-datatype',
            `${meta.alias} (${path.relative(ROOT, meta.file)}) editor=${meta.editorAlias} sin consumers`);
    }

    // ─── 9. Contenido del SEEDER fuera del repo (ADR 0129) ──────────
    //
    // Content/ y Media/ dejaron de estar en .gitignore para que el trabajo
    // editorial se versione. La razón por la que se ignoraban tenía DOS
    // partes, y solo una caducó: el agente sigue sin ser dueño del contenido
    // (no lo autora), pero el seeder de dev SÍ escribiría basura en el repo
    // en cuanto alguien corra /dev/seed-test-site con el export al guardar
    // activado — que es el comportamiento por defecto de uSync.
    //
    // La regla de .gitignore resolvía eso a martillazos: bloqueaba TODO el
    // contenido para no dejar pasar el del seeder. Este check es el bisturí:
    // deja pasar lo editorial y rechaza lo sembrado, por nombre de nodo.
    //
    // Se comprueba el <Alias> del nodo raíz, que es lo que uSync escribe como
    // nombre. Si mañana un seeder nuevo crea otro árbol de pruebas, se agrega
    // aquí — y hasta entonces el repo queda limpio sin bloquear al arquitecto.
    const SEEDED_CONTENT_ALIASES = new Set(['Test Site']);
    const contentFiles = await readFiles(path.join(ROOT, 'Content'));
    for (const file of contentFiles) {
        const text = await fs.readFile(file, 'utf-8');
        const alias = /<Content\b[^>]*\bAlias="([^"]+)"/.exec(text)?.[1];
        if (alias && SEEDED_CONTENT_ALIASES.has(alias)) {
            err('seeded-content',
                `${path.relative(ROOT, file)} es contenido del SEEDER ("${alias}"), no editorial. ` +
                'Se regenera con DevTestContentSeeder: bórralo del working tree antes de commitear.');
        }
    }

    // ─── 11. Claves de Dictionary sin respaldo (#60) ───────────────
    //
    // Las vistas piden i18n de dos formas y solo una es segura:
    //
    //   GetDictionaryValue("X", "texto")  → si X falta, sale "texto". Inofensivo.
    //   GetDictionaryValue("X")           → si X falta, sale "X". Al visitante.
    //
    // Lo segundo no da error ni log: pone una cadena técnica en medio de una
    // página. Este check mira SOLO las llamadas sin respaldo, y eso importa:
    // hay 145 claves con respaldo que a propósito no están en uSync —son
    // textos por defecto que el editor puede sobrescribir— y meterlas aquí
    // haría nacer el chequeo con 145 falsos positivos.
    //
    // Una clave que no sea un literal se IGNORA en silencio. Hoy las 234
    // llamadas lo son, pero un chequeo que se pone rojo con lo que no
    // entiende obliga a desactivarlo, y desactivado no vigila nada.
    const aliasDefinidos = new Set();
    for (const file of dictionary) {
        const text = await fs.readFile(file, 'utf-8');
        const m = text.match(/<Dictionary[^>]*\sAlias="([^"]+)"/);
        if (m) aliasDefinidos.add(m[1]);
    }

    const vistas = await readFiles(VIEWS, '.cshtml');
    for (const file of vistas) {
        const text = await fs.readFile(file, 'utf-8');
        for (const m of matchAll(text, /GetDictionaryValue\(\s*"([^"]+)"\s*(,)?/g)) {
            if (m[2]) continue; // lleva respaldo: no puede fallar
            if (aliasDefinidos.has(m[1])) continue;
            err('dictionary-sin-respaldo',
                `${path.relative(process.cwd(), file)} pide "${m[1]}" sin valor por defecto y esa `
                + 'clave no está en uSync/v9/Dictionary/. Umbraco devuelve la clave, así que el '
                + 'visitante vería esa cadena en la página. Añadí la clave, o un respaldo en la vista.');
        }
    }

    // ─── 8. Mojibake hygiene (Cap-300 Batch B) ─────────────────────
    // Detecta byte sequences típicas de UTF-8 mal decodificado como
    // Latin-1 y re-encodeado. PowerShell 5.1 default ANSI encoding
    // causa este artifact al editar XMLs uSync (memoria
    // feedback_powershell_utf8_bulk_edits). Patrones comunes para
    // español es-CO + alemán/francés inadvertidos.
    const MOJIBAKE_PATTERNS = ['Ã¡', 'Ã©', 'Ã­', 'Ã³', 'Ãº', 'Ã±', 'Ã¼', 'Â¿', 'Â¡', 'Ã‘'];
    for (const file of allFiles) {
        const text = await fs.readFile(file, 'utf-8');
        const found = MOJIBAKE_PATTERNS.filter((p) => text.includes(p));
        if (found.length > 0) {
            err('mojibake',
                `${path.relative(ROOT, file)} contiene ${found.join(', ')} (UTF-8 mal decodificado — re-grabar con encoding correcto)`);
        }
    }
}

await audit();

const okIcon = '✓';
const errIcon = '✗';

console.log(`uSync audit results — root: ${path.relative(process.cwd(), ROOT)}`);
console.log('');

if (findings.errors.length === 0 && findings.warnings.length === 0) {
    console.log(`${okIcon} Schema healthy — 0 errors, 0 warnings.`);
    process.exit(0);
}

if (findings.errors.length > 0) {
    console.log(`${errIcon} ${findings.errors.length} error(s):`);
    for (const e of findings.errors) console.log(`  ${e}`);
    console.log('');
}
if (findings.warnings.length > 0) {
    console.log(`! ${findings.warnings.length} warning(s):`);
    for (const w of findings.warnings) console.log(`  ${w}`);
    console.log('');
}

process.exit(findings.errors.length > 0 ? 1 : 0);
