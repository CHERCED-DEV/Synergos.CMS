#!/usr/bin/env node
/**
 * lecciones-repetidas.mjs — deja una publicación por lección repetida en el stream de la red. Es de una sola vez.
 *
 *   node tools/lecciones-repetidas.mjs --almacen <App_Data del CMS> --ensayo
 *   node tools/lecciones-repetidas.mjs --almacen <App_Data del CMS> --respaldo <carpeta fuera del almacén>
 *
 * Por qué existe (012-blogs-social, el catálogo lento): el stub de Educación (StubCourseCatalogProvider) guardaba en
 * memoria qué lecciones de la demo había sembrado, pero el stream es durable, así que cada arranque del CMS sembraba sus
 * 14 lecciones otra vez. La real llegó a 1358 lecciones, 97 copias de cada una, y el feed y el catálogo de Blogs las leen
 * todas. El stub ya anota en el almacén lo que sembró (syn-academy-demo-lesson-content). Esto quita lo que quedó de antes.
 *
 * Qué hace:
 *   - Lee <almacen>/syn-social-stream/*.json y agrupa las lecciones (Kind lesson) por autor, cuerpo y video.
 *   - De cada grupo con más de una, deja las que nombra otro fichero del almacén: la anotación del catálogo, un
 *     comentario, una reacción. Si ninguna está nombrada, deja la más reciente, que es la que adopta el stub.
 *   - Las demás las MUEVE a <respaldo>/syn-social-stream/ con su nombre, y escribe <respaldo>/lecciones-repetidas.json con
 *     lo que movió. No borra nada: para deshacerlo, se copian de vuelta.
 *   - Con --ensayo sólo cuenta lo que haría y no escribe nada.
 *
 * Puede correr con el CMS arriba: el stream se lee del disco en cada llamada y esto deja la que usa el stub.
 *
 * Sale con 0 si terminó o no había nada que mover, 1 con un argumento mal, 2 si no encuentra el stream, y 3 si el
 * respaldo ya tiene un fichero con el mismo nombre (entonces no mueve ninguno) o si alguno no se pudo mover.
 */

import { promises as fs, constants } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const STREAM = 'syn-social-stream';
const MANIFIESTO = 'lecciones-repetidas.json';

const sinBom = (texto) => (texto.charCodeAt(0) === 0xfeff ? texto.slice(1) : texto);

const dentroDe = (padre, hijo) => {
    const rel = path.relative(padre, hijo);
    return rel === '' || (!rel.startsWith('..') && !path.isAbsolute(rel));
};

/** Los .json de una familia del almacén, con su texto. Los .tmp de una escritura a medias no cuentan. */
async function documentos(dir) {
    const nombres = (await fs.readdir(dir)).filter((n) => n.toLowerCase().endsWith('.json')).sort();
    const docs = [];
    for (const nombre of nombres) {
        const ruta = path.join(dir, nombre);
        docs.push({ nombre, ruta, texto: sinBom(await fs.readFile(ruta, 'utf8')) });
    }
    return docs;
}

/** Más reciente primero; a igual fecha, por id, como ordena el stream del CMS. */
function masRecientePrimero(a, b) {
    const fa = Date.parse(a.creada);
    const fb = Date.parse(b.creada);
    const da = Number.isNaN(fa) ? -Infinity : fa;
    const db = Number.isNaN(fb) ? -Infinity : fb;
    if (da !== db) return db - da;
    return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
}

/** Lo que haría: los grupos de lecciones repetidas, qué queda y qué se mueve. No escribe nada. */
export async function planDeLecciones(almacen) {
    const dirStream = path.join(almacen, STREAM);
    const stream = await documentos(dirStream);

    const lecciones = [];
    let ilegibles = 0;
    for (const doc of stream) {
        let item;
        try {
            item = JSON.parse(doc.texto);
        } catch {
            ilegibles++;
            continue;
        }
        if (String(item?.Kind ?? '').toLowerCase() !== 'lesson') continue;
        lecciones.push({
            id: String(item.Id ?? path.basename(doc.nombre, '.json')),
            nombre: doc.nombre,
            ruta: doc.ruta,
            autor: item.Author?.Id ?? null,
            cuerpo: item.Body ?? '',
            video: item.MediaUrl ?? null,
            creada: item.CreatedUtc ?? '',
        });
    }

    const grupos = new Map();
    for (const l of lecciones) {
        const clave = JSON.stringify([l.autor, l.cuerpo, l.video]);
        if (!grupos.has(clave)) grupos.set(clave, []);
        grupos.get(clave).push(l);
    }
    const repetidos = [...grupos.values()].filter((g) => g.length > 1);

    // Quién nombra a cada candidata: cualquier fichero de una familia syn-* del almacén, salvo el suyo.
    const candidatas = new Map(repetidos.flat().map((l) => [l.id, l]));
    const nombradaPor = new Map();
    const familias = (await fs.readdir(almacen, { withFileTypes: true }))
        .filter((e) => e.isDirectory() && e.name.startsWith('syn-'))
        .map((e) => e.name)
        .sort();
    for (const familia of familias) {
        const docs = familia === STREAM ? stream : await documentos(path.join(almacen, familia));
        for (const doc of docs) {
            for (const [id, l] of candidatas) {
                if (familia === STREAM && doc.nombre === l.nombre) continue;
                if (!doc.texto.includes(id)) continue;
                if (!nombradaPor.has(id)) nombradaPor.set(id, new Set());
                nombradaPor.get(id).add(familia);
            }
        }
    }

    const plan = repetidos.map((g) => {
        const orden = [...g].sort(masRecientePrimero);
        const nombradas = orden.filter((l) => nombradaPor.has(l.id));
        const quedan = nombradas.length > 0 ? nombradas : [orden[0]];
        const seQuedan = new Set(quedan.map((l) => l.id));
        return {
            autor: orden[0].autor,
            inicio: orden[0].cuerpo.slice(0, 60),
            copias: g.length,
            quedan: quedan.map((l) => ({ id: l.id, nombradaPor: [...(nombradaPor.get(l.id) ?? [])] })),
            movidas: orden.filter((l) => !seQuedan.has(l.id)),
        };
    });

    return {
        publicaciones: stream.length,
        ilegibles,
        lecciones: lecciones.length,
        distintas: grupos.size,
        plan,
    };
}

/** Mueve un fichero; entre discos distintos lo copia, comprueba que quedó igual y quita el original. */
async function mover(origen, destino) {
    try {
        await fs.rename(origen, destino);
    } catch (e) {
        if (e.code !== 'EXDEV') throw e;
        await fs.copyFile(origen, destino, constants.COPYFILE_EXCL);
        const [a, b] = await Promise.all([fs.readFile(origen), fs.readFile(destino)]);
        if (!a.equals(b)) throw new Error(`la copia de ${origen} no quedó igual`);
        await fs.unlink(origen);
    }
}

async function existe(ruta) {
    try {
        await fs.access(ruta);
        return true;
    } catch {
        return false;
    }
}

// CLI: sólo si se lo invoca a él.
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const args = process.argv.slice(2);
    const valor = (n) => {
        const i = args.indexOf(n);
        return i >= 0 && i + 1 < args.length && !args[i + 1].startsWith('--') ? args[i + 1] : null;
    };
    const ensayo = args.includes('--ensayo');
    const almacenArg = valor('--almacen');
    const respaldoArg = valor('--respaldo');
    const uso = 'Uso: node tools/lecciones-repetidas.mjs --almacen <App_Data del CMS> (--ensayo | --respaldo <carpeta>)';

    if (!almacenArg || (!ensayo && !respaldoArg)) {
        console.error(`✗ ${!almacenArg ? 'Falta --almacen.' : 'Falta --respaldo: sin --ensayo, mueve las repetidas ahí.'}`);
        console.error(uso);
        process.exit(1);
    }
    const almacen = path.resolve(almacenArg);
    const respaldo = respaldoArg ? path.resolve(respaldoArg) : null;
    if (respaldo && (dentroDe(almacen, respaldo) || dentroDe(respaldo, almacen))) {
        console.error(`✗ El respaldo va fuera del almacén: ${respaldo}`);
        process.exit(1);
    }
    if (!(await existe(path.join(almacen, STREAM)))) {
        console.error(`✗ No encuentro el stream: ${path.join(almacen, STREAM)}`);
        process.exit(2);
    }

    const r = await planDeLecciones(almacen);
    const movidas = r.plan.flatMap((g) => g.movidas);
    const quedan = r.plan.flatMap((g) => g.quedan);
    const nombradas = quedan.filter((q) => q.nombradaPor.length > 0).length;

    console.log(`Stream: ${path.join(almacen, STREAM)}`);
    console.log(`  ${r.publicaciones} publicaciones, ${r.lecciones} lecciones, ${r.distintas} distintas` +
        (r.ilegibles === 1 ? ', 1 ilegible (no se toca)' : r.ilegibles > 1 ? `, ${r.ilegibles} ilegibles (no se tocan)` : '') + '.');
    if (r.plan.length === 0) {
        console.log('  ✓ Ninguna lección repetida: no hay nada que mover.');
        process.exit(0);
    }
    console.log(`  ${r.plan.length} lecciones repetidas: quedan ${quedan.length} ` +
        `(${nombradas} porque otro fichero del almacén las nombra, ${quedan.length - nombradas} por ser las más recientes).`);
    for (const g of r.plan.filter((g) => g.quedan.length > 1)) {
        console.log(`  · Quedan ${g.quedan.length} de «${g.inicio}…» (${g.autor}): ` +
            g.quedan.map((q) => `${q.id} en ${q.nombradaPor.join(', ')}`).join('; '));
    }

    const destino = respaldo ? path.join(respaldo, STREAM) : null;
    const choques = [];
    if (destino) {
        for (const l of movidas) {
            if (await existe(path.join(destino, l.nombre))) choques.push(l.nombre);
        }
    }

    if (ensayo) {
        console.log(`  Ensayo: movería ${movidas.length}${destino ? ` a ${destino}` : ''}. No movió nada.`);
        if (choques.length > 0) console.log(`  ✗ El respaldo ya tiene ${choques.length} con el mismo nombre (${choques[0]}…).`);
        process.exit(choques.length > 0 ? 3 : 0);
    }
    if (movidas.length === 0) {
        console.log('  ✓ No hay nada que mover.');
        process.exit(0);
    }
    if (choques.length > 0) {
        console.error(`✗ El respaldo ya tiene ${choques.length} con el mismo nombre (${choques[0]}…). No moví ninguna.`);
        process.exit(3);
    }

    await fs.mkdir(destino, { recursive: true });
    const hechas = [];
    const fallos = [];
    for (const l of movidas) {
        try {
            await mover(l.ruta, path.join(destino, l.nombre));
            hechas.push(l);
        } catch (e) {
            fallos.push(`${l.nombre}: ${e.message}`);
        }
    }

    const sello = new Date().toISOString();
    let manifiesto = path.join(respaldo, MANIFIESTO);
    if (await existe(manifiesto)) manifiesto = path.join(respaldo, `lecciones-repetidas-${sello.replace(/[:.]/g, '-')}.json`);
    const movidasPorId = new Set(hechas.map((l) => l.id));
    await fs.writeFile(manifiesto, JSON.stringify({
        cuando: sello,
        almacen,
        lecciones: r.lecciones,
        distintas: r.distintas,
        quedan: quedan.length,
        movidas: hechas.length,
        grupos: r.plan.map((g) => ({
            autor: g.autor,
            inicio: g.inicio,
            copias: g.copias,
            quedan: g.quedan,
            movidas: g.movidas.filter((l) => movidasPorId.has(l.id)).map((l) => l.nombre),
        })),
        fallos,
    }, null, 2) + '\n', 'utf8');

    console.log(`  Movió ${hechas.length} a ${destino}. Lo que movió está en ${manifiesto}.`);
    if (fallos.length > 0) {
        console.error(`✗ ${fallos.length} no se pudieron mover:`);
        for (const f of fallos) console.error(`  ${f}`);
        process.exit(3);
    }
    process.exit(0);
}
