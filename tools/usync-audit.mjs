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
    const dataTypeKeys = new Set(); // lowercase
    const dataTypeKeyRegex = /<DataType\s+Key="([0-9a-fA-F-]{36})"/;
    for (const file of dataTypes) {
        const text = await fs.readFile(file, 'utf-8');
        const m = text.match(dataTypeKeyRegex);
        if (m) dataTypeKeys.add(m[1].toLowerCase());
    }
    const definitionRefRegex = /<Definition>([0-9a-fA-F-]{36})<\/Definition>/g;
    for (const file of contentTypes) {
        const text = await fs.readFile(file, 'utf-8');
        for (const m of matchAll(text, definitionRefRegex)) {
            const guid = m[1].toLowerCase();
            if (!dataTypeKeys.has(guid)) {
                err('missing-datatype-definition',
                    `${guid} referenciado en ${path.relative(ROOT, file)} no existe como <DataType Key>`);
            }
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
