#!/usr/bin/env node
/**
 * usync-ficheros.mjs — los FICHEROS de uSync/v9 se llaman como uSync los lee, y cada medio tiene su binario.
 *
 * Lo llama tools/usync-audit.mjs (checks 14 y 15) y se puede correr solo sobre cualquier árbol:
 *
 *   node tools/usync-ficheros.mjs --usync <ruta a uSync/v9> --wwwroot <ruta a wwwroot>
 *
 * Sale con 1 si hay problemas.
 *
 * Por qué existe (016, 35a1b88b): 30 .config de Media entraron con un retorno de carro al final del nombre
 * («producto-funda.config» + CR; en Windows se ve como U+F00D). uSync sólo lee *.config y nunca los importó, y los
 * ficheros de nombre bueno seguían apuntando a 28 .png que el mismo commit borró: 28 fotos rotas en la real y en
 * producción. Ninguna compuerta lo vio: el XML de dentro estaba bien.
 *
 *   14. Todo fichero de uSync/v9 (salvo .gitkeep) termina en .config y su nombre no tiene caracteres de control (< U+0020, U+007F)
 *       ni del rango U+F000–U+F0FF (donde Windows/Git ponen los caracteres prohibidos en un nombre).
 *   15. El umbracoFile de cada medio de uSync/v9/Media apunta a un binario que está en wwwroot/media.
 */

import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Si el carácter no puede ir en el nombre de un fichero de uSync. */
const esRaro = (c) => {
    const n = c.codePointAt(0);
    return n < 0x20 || n === 0x7f || (n >= 0xf000 && n <= 0xf0ff);
};

/** El nombre visible: los caracteres raros como \u{XXXX}, para que el mensaje se pueda leer. */
const legible = (nombre) => [...nombre].map((c) => (esRaro(c) ? `\\u{${c.codePointAt(0).toString(16).toUpperCase()}}` : c)).join('');

async function ficheros(dir) {
    const out = [];
    let entradas;
    try {
        entradas = await fs.readdir(dir, { withFileTypes: true });
    } catch {
        return out;
    }
    for (const e of entradas) {
        const ruta = path.join(dir, e.name);
        if (e.isDirectory()) out.push(...(await ficheros(ruta)));
        else out.push(ruta);
    }
    return out;
}

/** La ruta pública del binario de un medio (`/media/xxxx/fichero.jpg`), o null si no tiene. */
export function rutaDelBinario(textoDelMedio) {
    const bloque = textoDelMedio.match(/<umbracoFile>\s*<Value>\s*<!\[CDATA\[([\s\S]*?)\]\]>/)?.[1]?.trim();
    if (!bloque) return null;
    let src = bloque;
    if (bloque.startsWith('{')) {
        try {
            src = JSON.parse(bloque).src ?? '';
        } catch {
            src = bloque.match(/"src"\s*:\s*"([^"]+)"/)?.[1] ?? '';
        }
    }
    if (!src || !src.startsWith('/media/')) return null;
    try {
        return decodeURIComponent(src);
    } catch {
        return src;
    }
}

/**
 * Los problemas de los ficheros de un árbol uSync: nombres que uSync no lee y medios sin binario.
 * @param {string} raizUsync  la carpeta uSync/v9
 * @param {string} raizWwwroot la carpeta wwwroot (de la que cuelga media/)
 * @returns {Promise<string[]>} una línea por problema; vacío si todo está bien
 */
export async function problemasDeFicheros(raizUsync, raizWwwroot) {
    const problemas = [];
    const todos = await ficheros(raizUsync);
    for (const f of todos) {
        const nombre = path.basename(f);
        const relativo = legible(path.relative(raizUsync, f));
        if ([...nombre].some(esRaro)) {
            problemas.push(`[nombre-de-fichero] ${relativo}: el nombre tiene un carácter de control o del rango U+F000–U+F0FF; uSync no lo lee como lo que parece.`);
        } else if (!nombre.endsWith('.config') && nombre !== '.gitkeep') {
            // .gitkeep sólo guarda la carpeta en git; uSync no lo mira.
            problemas.push(`[nombre-de-fichero] ${relativo}: uSync sólo lee *.config.`);
        }
    }

    for (const f of todos.filter((f) => path.basename(f).endsWith('.config') && path.relative(raizUsync, f).split(path.sep)[0] === 'Media')) {
        const ruta = rutaDelBinario(await fs.readFile(f, 'utf-8'));
        if (!ruta) continue;
        try {
            await fs.access(path.join(raizWwwroot, ...ruta.split('/').filter(Boolean)));
        } catch {
            problemas.push(`[medio-sin-binario] ${legible(path.relative(raizUsync, f))}: umbracoFile apunta a ${ruta} y no está en wwwroot.`);
        }
    }
    return problemas;
}

// CLI: sólo si se lo invoca a él.
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const arg = (n, def) => {
        const i = process.argv.indexOf(n);
        return i >= 0 ? process.argv[i + 1] : def;
    };
    const usync = path.resolve(arg('--usync', 'Synergos.CMS.Web/uSync/v9'));
    const wwwroot = path.resolve(arg('--wwwroot', 'Synergos.CMS.Web/wwwroot'));
    const problemas = await problemasDeFicheros(usync, wwwroot);
    if (problemas.length === 0) {
        console.log('✓ ficheros de uSync: todos se leen y cada medio tiene su binario.');
        process.exit(0);
    }
    console.log(`✗ ${problemas.length} problema(s) en los ficheros de uSync:`);
    for (const p of problemas) console.log(`  ${p}`);
    process.exit(1);
}
