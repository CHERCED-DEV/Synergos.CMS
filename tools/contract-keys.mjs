#!/usr/bin/env node
/**
 * G-6 · El cruce de la FORMA del JSON entre los dos árboles.
 *
 * QUÉ VIGILA
 * ----------
 * Que una clave que HOY cruza —el controller la emite y la app la lee— no deje de cruzar.
 * No exige que todo lo que la UI lee exista en el CMS: la mayoría de esas claves son
 * fallbacks legacy, datos internos de la app o respuestas de otros bordes, así que un gate
 * así daría cientos de falsos positivos y se acabaría saltando.
 *
 * Es un TRINQUETE, el mismo patrón que `cdn-size-budget` en Synergos.UI: se mide lo que hay,
 * se congela, y falla el día que baja. La línea base se regenera con `--actualizar`, y su
 * diff va en el commit que lo causó.
 *
 * POR QUÉ HACE FALTA (#102)
 * -------------------------
 * Los tests del CMS ejercitaban cada controller contra sus propios DTOs —comprobaban que la
 * respuesta llevara lo que el controller decidió poner, que es una tautología—, así que doce
 * claves de Academy se desviaron de su consumidor sin que nada se pusiera rojo: el
 * normalizador del cliente es defensivo, y una clave que falta degrada EN SILENCIO (una
 * segunda línea vacía, un plan sin id, media pantalla en modo mock).
 *
 * El gate que ya existía —`tools/validate-cms-contracts.mjs`, en el repo hermano— cruza el
 * registry del CDN contra los DocTypes: otra superficie. La forma del JSON de un controller
 * no la miraba nadie.
 *
 * LO QUE ESTE GATE NO ES
 * ----------------------
 * No comprueba TIPOS ni valores: cruza por NOMBRE DE CLAVE, y está dicho para no mentir sobre
 * su alcance — igual que el cruce de `window.synergos` en CLAUDE.md §3. Un `string` que pasa a
 * `number` bajo la misma clave sigue pasando por aquí.
 *
 * **Y cruza por CONTROLLER ENTERO, no por endpoint.** Si `title` ya sale de `/products`, que
 * FALTE en `/wishlist` no se ve: la clave sigue cruzando. Se descubrió midiendo contra el
 * arreglo de #104 —la wishlist emitía `itemRef`/`owner` y la UI leía `productId`/`title`, así
 * que devolvía una lista vacía con el servidor lleno— y este gate no lo habría cazado.
 *
 * No es un descuido que se pueda tapar afinando el regex: exigiría saber qué DTO devuelve cada
 * ruta, o sea seguir el tipo de retorno de cada acción hasta su `record`. Se puede hacer y es
 * otro trabajo. Mientras tanto queda escrito, porque un gate que se cree más listo de lo que
 * es resulta peor que no tenerlo: alguien deja de mirar confiando en él.
 *
 * USO
 *   node tools/contract-keys.mjs                    # con el hermano en ../Synergos.UI
 *   node tools/contract-keys.mjs --ui-path=/ruta    # o SYNERGOS_UI_PATH
 *   node tools/contract-keys.mjs --actualizar       # regenera la línea base
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const CMS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const BASELINE = path.join(CMS, 'tools', 'contract-keys.baseline.json');

const arg = (name) => process.argv.find((a) => a.startsWith(`--${name}=`))?.split('=')[1];
const UI = path.resolve(
    arg('ui-path') || process.env.SYNERGOS_UI_PATH || path.join(CMS, '..', 'Synergos.UI'));

/**
 * Qué app del catálogo consume qué controller.
 *
 * Es una lista a mano, y aquí sí lo es por una razón: el vínculo no está escrito en ningún
 * sitio del que se pueda deducir —ni el controller nombra la app ni la app el controller—,
 * así que inventarle una convención de nombres sería fabricar una regla que no existe. Lo
 * que NO se queda corta en silencio es la línea base, que es lo que de verdad vigila.
 */
const PARES = [
    { app: 'academy', controllers: ['AcademyController.cs'] },
    // El storefront pide /products, /order y /checkout, que sirve ShopCatalogController;
    // ShopController es SÓLO el carrito (api/shop/cart). Apuntar al segundo cruzaba 3 claves
    // en vez de 70 — ver la guarda de abajo, que es lo que lo destapó.
    { app: 'storefront', controllers: ['ShopCatalogController.cs', 'ShopController.cs'] },
    { app: 'eventos', controllers: ['EventosController.cs'] },
    { app: 'gov', controllers: ['GovController.cs'] },
    { app: 'ehr', controllers: ['EhrController.cs', 'HealthcareApiController.cs'] },
    { app: 'realty', controllers: ['RealtyController.cs'] },
    { app: 'booking-wizard', controllers: ['BookingController.cs'] },
    { app: 'blogs', controllers: ['BlogsController.cs'] },
];

/**
 * Bajo qué proporción de claves cruzadas se sospecha que el par apunta al fichero equivocado.
 *
 * ES LA GUARDA QUE HACE QUE LA LISTA DE ARRIBA NO SE QUEDE CORTA EN SILENCIO, y existe porque
 * ya pasó: `storefront` empezó apuntando a `ShopController` —el del carrito— y cruzaba 3 claves
 * contra las 72 que su cliente lee. La línea base se habría congelado en 3 y el gate habría
 * dado verde para siempre sobre el vertical con más dinero del repo.
 *
 * Un vertical cuyo cliente lee 70 claves y cruza 2 no es un vertical desacoplado: es un par mal
 * escrito. El umbral es deliberadamente bajo —basta con que cruce una de cada cinco— porque lo
 * que caza es el error grosero, no la deriva fina; esa la caza el trinquete.
 */
const PROPORCION_MINIMA = 0.2;

const camel = (s) => s.charAt(0).toLowerCase() + s.slice(1);

/** Las claves que el cliente de una app LEE de las respuestas. */
function clavesQueLeeLaUi(app) {
    const dir = path.join(UI, 'platforms', 'angular', 'apps', 'elements', 'modules', app, 'src');
    if (!fs.existsSync(dir)) return null;

    const clientes = [];
    const walk = (d) => {
        for (const e of fs.readdirSync(d, { withFileTypes: true })) {
            const p = path.join(d, e.name);
            if (e.isDirectory()) walk(p);
            else if (e.name.endsWith('api.client.ts')) clientes.push(p);
        }
    };
    walk(dir);
    if (clientes.length === 0) return null;

    const claves = new Set();
    for (const f of clientes) {
        const src = fs.readFileSync(f, 'utf8');
        // `value['x']`, `record['x']`, `raw['x']` — la forma que usan todos los normalizadores.
        for (const m of src.matchAll(/\b[A-Za-z_$][\w$]*\['([A-Za-z0-9_]+)'\]/g)) {
            claves.add(m[1]);
        }
    }
    return claves;
}

/** Las claves que uno o varios controllers EMITEN, camelizadas como las serializa ASP.NET. */
function clavesQueEmiteElCms(controllers) {
    const claves = new Set();
    let alguno = false;

    for (const controller of controllers) {
        const f = path.join(CMS, 'Synergos.CMS.Web', 'Controllers', controller);
        if (!fs.existsSync(f)) continue;
        alguno = true;
        recogerClaves(fs.readFileSync(f, 'utf8'), claves);
    }

    return alguno ? claves : null;
}

function recogerClaves(fuente, claves) {
    // Los comentarios se quitan ANTES de parsear, y no es cosmética: este repo documenta cada
    // clave de contrato en la línea de arriba —«// Contrato UI: la app lee `verb` (= tipo del
    // evento)»— y ese `=` dentro del comentario partía el parámetro por el sitio equivocado,
    // así que `verb` desaparecía del cruce. Un comentario nunca declara una clave; dejarlo
    // dentro sólo añade formas de equivocarse.
    const src = fuente
        .replace(/\/\*[\s\S]*?\*\//g, ' ')
        .replace(/\/\/[^\n]*/g, ' ');


    // Los `record` de respuesta: cada parámetro es una clave del JSON.
                // `)` seguido de `;` O de `{`: un record puede llevar cuerpo
            // (`record X(...) { ... }`), y exigir el `;` lo dejaba invisible. Es el caso de
            // BookAppointmentBody, que declara `Slot` y además un helper para leerlo — el gate
            // lo denunciaba como cuerpo no declarado.
for (const m of src.matchAll(/record\s+\w+\s*\(([\s\S]*?)\)\s*[;{]/g)) {
        for (const p of m[1].split(',')) {
            // Una clave explícita gana sobre el nombre del parámetro — es como se renombra
            // sin tocar C#, y es la forma que toma una deriva de verdad.
            const explicita = p.match(/JsonPropertyName\("([^"]+)"\)/);
            if (explicita) { claves.add(explicita[1]); continue; }
            // El valor por defecto se corta ANTES de partir por espacios. Al revés —que es
            // como estaba— `DateTimeOffset? Date = null` daba la clave `null` y PERDÍA `Date`:
            // el gate ignoraba en silencio todo parámetro con default, que son muchos, y de
            // paso inventaba un falso positivo al ponerle un default a un campo existente.
            // Lo destapó el propio gate al medir el arreglo de #103.
            const nombre = p.split('=')[0].trim().split(/\s+/).pop()?.trim();
            if (nombre && /^[A-Za-z_]\w*$/.test(nombre)) claves.add(camel(nombre));
        }
    }

    // Los objetos anónimos de respuesta (`new { error = ... }`).
    for (const m of src.matchAll(/new\s*\{([^}]*)\}/g)) {
        for (const asignacion of m[1].split(',')) {
            const nombre = asignacion.split('=')[0]?.trim();
            if (nombre && /^[A-Za-z_]\w*$/.test(nombre)) claves.add(camel(nombre));
        }
    }

}

const medido = {};
const huerfanas = {};
const problemas = [];
const avisos = [];

for (const { app, controllers } of PARES) {
    const ui = clavesQueLeeLaUi(app);
    const cms = clavesQueEmiteElCms(controllers);

    if (ui === null) { avisos.push(`[app] no se encontró el cliente de '${app}' bajo ${UI}`); continue; }
    if (cms === null) { avisos.push(`[cms] no existe ninguno de ${controllers.join(', ')}`); continue; }

    const cruzan = [...ui].filter((k) => cms.has(k)).sort();
    medido[app] = cruzan;
    huerfanas[app] = [...ui].filter((k) => !cms.has(k)).sort();

    // La guarda: un par mal escrito cruza casi nada, y congelarlo así da verde para siempre.
    const proporcion = ui.size === 0 ? 1 : cruzan.length / ui.size;
    if (proporcion < PROPORCION_MINIMA) {
        problemas.push(
            `[${app}] sólo ${cruzan.length} de las ${ui.size} claves que su cliente lee salen de `
            + `${controllers.join(' + ')} (${Math.round(proporcion * 100)} %).\n    → casi seguro el par `
            + `apunta al controller equivocado. Mirá qué rutas pide el cliente antes de congelar esto.`);
    }
}

if (process.argv.includes('--huerfanas')) {
    // INFORMATIVO, nunca falla: son CANDIDATAS a revisar, no defectos.
    //
    // La mayoría es ruido legítimo —estado interno de la app, fallbacks legacy, respuestas de
    // otros bordes— y por eso el gate NO las exige: hacerlo daría cientos de falsos positivos y
    // acabaría saltándose. Pero esta lista es exactamente lo que produjo #102 al mirarse a
    // mano vertical por vertical, así que tenerla a un comando ahorra esa vuelta: lo que hay
    // que hacer con ella es cruzarla con lo que la app PINTA, no arreglarla entera.
    console.log('Claves que cada app LEE y ningún controller emite — candidatas, no defectos:\n');
    for (const [app, claves] of Object.entries(huerfanas)) {
        console.log(`  ${app} (${claves.length})`);
        if (claves.length > 0) console.log(`    ${claves.join(', ')}\n`);
    }
    process.exit(0);
}

if (process.argv.includes('--actualizar')) {
    // La guarda corre TAMBIÉN al regenerar, y se niega a congelar. Salir antes de evaluarla
    // dejaba abierto justo el camino por el que entra el error que existe para evitar:
    // `--actualizar` es lo que uno teclea cuando el gate se queja, así que un par mal escrito
    // se habría congelado con un comando y habría dado verde para siempre.
    if (problemas.length > 0) {
        console.error('✗ No se congela una línea base sospechosa:');
        for (const p of problemas) console.error(`  ${p}`);
        process.exit(1);
    }
    fs.writeFileSync(BASELINE, JSON.stringify(medido, null, 2) + '\n', 'utf8');
    console.log(`Línea base regenerada: ${Object.entries(medido).map(([a, k]) => `${a}=${k.length}`).join(' · ')}`);
    process.exit(0);
}

if (!fs.existsSync(BASELINE)) {
    console.error('No hay línea base. Genérala con: node tools/contract-keys.mjs --actualizar');
    process.exit(1);
}

const base = JSON.parse(fs.readFileSync(BASELINE, 'utf8'));

for (const [app, esperadas] of Object.entries(base)) {
    const ahora = new Set(medido[app] ?? []);
    if (!(app in medido)) {
        problemas.push(`[${app}] ya no se puede medir — falta el cliente o el controller.`);
        continue;
    }
    const perdidas = esperadas.filter((k) => !ahora.has(k));
    if (perdidas.length > 0) {
        problemas.push(
            `[${app}] el borde dejó de emitir ${perdidas.length} clave(s) que la app LEE: `
            + `${perdidas.join(', ')}\n    → el normalizador del cliente degrada en silencio; `
            + `esto no se ve como un error, se ve como una pantalla pobre.`);
    }
    const nuevas = [...ahora].filter((k) => !esperadas.includes(k));
    if (nuevas.length > 0) {
        avisos.push(`[${app}] +${nuevas.length} clave(s) cruzando: ${nuevas.join(', ')} — corré --actualizar`);
    }
}

console.log('G-6 · cruce de claves CMS ↔ UI');
for (const a of avisos) console.log(`  ! ${a}`);

if (problemas.length > 0) {
    console.error(`\n✗ ${problemas.length} regresión(es) de contrato:`);
    for (const p of problemas) console.error(`  ${p}`);
    process.exit(1);
}

const total = Object.values(medido).reduce((n, k) => n + k.length, 0);
console.log(`\n✓ ${total} claves cruzan en ${Object.keys(medido).length} verticales — ninguna se perdió.`);
