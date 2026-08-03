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
 *   8. Mojibake hygiene: detecta byte sequences típicas de UTF-8 mal
 *      decodificado como Latin-1 y re-encodeado (PowerShell 5.1 trap).
 *      Patrones: Ã¡/Ã©/Ã­/Ã³/Ãº/Ã±/Â¿/Â¡. Error level — los XMLs uSync
 *      con mojibake muestran texto roto en backoffice.
 *      Cap-300 Batch B (Ola 299).
 */

import { promises as fs } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(process.cwd(), 'Synergos.CMS.Web/uSync/v9');
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

const findings = {
    errors: [],
    warnings: [],
};

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
