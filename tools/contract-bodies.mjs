#!/usr/bin/env node
/**
 * G-7 · Los CUERPOS DE PETICIÓN, cruzados por ruta.
 *
 * QUÉ VIGILA, Y POR QUÉ ES OTRO GATE
 * ----------------------------------
 * G-6 cruza lo que el borde EMITE contra lo que la app LEE. Este cruza lo que la app MANDA
 * contra lo que el borde DECLARA — y ahí es donde estuvo lo caro en los ocho verticales
 * auditados (#102 a #105), sin excepción:
 *
 *   · un `planId` que el record no declaraba → se cobraba el plan equivocado. PLATA.
 *   · `slot: {date,time}` contra un `string` → JsonException → 400 SIEMPRE.
 *   · `lat`/`lng` planos contra un record que sólo declaraba `geo:{…}` → publicaba en (0,0).
 *   · la dirección de entrega, descartada sin decir nada.
 *   · seis rutas de EHR en 400 permanente; la nota clínica «guardada» sin guardarse.
 *
 * Ninguno fallaba a la vista: System.Text.Json descarta en silencio los miembros que no mapea,
 * y el `catch` del cliente inventa el acuse. **Un fallo que ocurre el 100 % de las veces se ve
 * igual que uno que no ocurre nunca cuando hay un mock detrás.**
 *
 * POR QUÉ ES ERROR Y NO TRINQUETE
 * -------------------------------
 * G-6 es trinquete porque la mayoría de lo que la UI lee no tiene por qué venir del CMS. Aquí
 * es al revés: una clave que el cliente MANDA a una ruta y el record de esa ruta no declara no
 * tiene lectura inocente — o se descarta (y el usuario pierde el dato) o revienta el binding
 * entero (400). Ocho verticales y ni un solo falso positivo legítimo encontrado.
 *
 * LO QUE ESTE GATE NO VE, Y ESTÁ DICHO
 * ------------------------------------
 * Sólo los cuerpos que son un OBJETO LITERAL en la llamada. Los que pasan por un helper
 * (`postJson(url, toCourseDraftWire(body))`) quedan fuera: seguir la función exigiría resolver
 * su return, y es otro trabajo. Los declara al final para que se vean, en vez de contarlos
 * como cubiertos.
 *
 * USO
 *   node tools/contract-bodies.mjs [--ui-path=/ruta]   # o SYNERGOS_UI_PATH
 */

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const CMS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const arg = (n) => process.argv.find((a) => a.startsWith(`--${n}=`))?.split('=')[1];
const UI = path.resolve(
    arg('ui-path') || process.env.SYNERGOS_UI_PATH || path.join(CMS, '..', 'Synergos.UI'));

/** Los mismos pares que G-6: el vínculo app↔controller no se deduce de ningún sitio. */
const PARES = [
    { app: 'academy', controllers: ['AcademyController.cs'] },
    { app: 'storefront', controllers: ['ShopCatalogController.cs', 'ShopController.cs'] },
    { app: 'eventos', controllers: ['EventosController.cs'] },
    { app: 'gov', controllers: ['GovController.cs'] },
    { app: 'ehr', controllers: ['EhrController.cs', 'HealthcareApiController.cs'] },
    { app: 'realty', controllers: ['RealtyController.cs'] },
    { app: 'booking-wizard', controllers: ['BookingController.cs'] },
    { app: 'blogs', controllers: ['BlogsController.cs', 'CommentsController.cs'] },
];

const sinComentarios = (s) => s.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/[^\n]*/g, ' ');
const camel = (s) => s.charAt(0).toLowerCase() + s.slice(1);
const ultimoSegmento = (ruta) => ruta.split('/').filter((p) => p && !p.startsWith('{')).pop() ?? '';

/** ruta → claves que el record de esa ruta DECLARA. */
function declaradoPorElCms(controllers) {
    const porRuta = new Map();
    let alguno = false;

    for (const c of controllers) {
        const f = path.join(CMS, 'Synergos.CMS.Web', 'Controllers', c);
        if (!fs.existsSync(f)) continue;
        alguno = true;
        const src = sinComentarios(fs.readFileSync(f, 'utf8'));

        // Todos los records del fichero, por nombre, con sus claves.
        const records = new Map();
                    // `)` seguido de `;` O de `{`: un record puede llevar cuerpo
            // (`record X(...) { ... }`), y exigir el `;` lo dejaba invisible. Es el caso de
            // BookAppointmentBody, que declara `Slot` y además un helper para leerlo — el gate
            // lo denunciaba como cuerpo no declarado.
for (const m of src.matchAll(/record\s+(\w+)\s*\(([\s\S]*?)\)\s*[;{]/g)) {
            const claves = new Set();
            for (const p of m[2].split(',')) {
                const explicita = p.match(/JsonPropertyName\("([^"]+)"\)/);
                if (explicita) { claves.add(explicita[1]); continue; }
                const nombre = p.split('=')[0].trim().split(/\s+/).pop()?.trim();
                if (nombre && /^[A-Za-z_]\w*$/.test(nombre)) claves.add(camel(nombre));
            }
            records.set(m[1], claves);
        }

        // La ruta de cada POST y el tipo de su [FromBody].
        for (const m of src.matchAll(/\[HttpPost\("([^"]*)"\)\][\s\S]{0,400}?\[FromBody\]\s+(\w+)\??\s+\w+/g)) {
            const claves = records.get(m[2]);
            if (claves) porRuta.set(ultimoSegmento(m[1]), claves);
        }
    }

    return alguno ? porRuta : null;
}

/** ruta → claves que el cliente MANDA, de los cuerpos literales. */
function mandadoPorLaUi(app) {
    const dir = path.join(UI, 'platforms', 'angular', 'apps', 'elements', 'modules', app, 'src');
    if (!fs.existsSync(dir)) return null;

    const ficheros = [];
    const walk = (d) => {
        for (const e of fs.readdirSync(d, { withFileTypes: true })) {
            const p = path.join(d, e.name);
            if (e.isDirectory()) walk(p);
            else if (e.name.endsWith('api.client.ts')) ficheros.push(p);
        }
    };
    walk(dir);
    if (ficheros.length === 0) return null;

    /**
     * Las interfaces y alias de tipo del módulo, por nombre.
     *
     * Hace falta porque el patrón dominante no es un literal ni un tipo inline: el cuerpo
     * llega como un parámetro con TIPO NOMBRADO (`draft: NewMessage`). Sin resolverlo, cuatro
     * rutas de Blogs quedaban fuera del cruce — y eran justo las que daban 400 el 100 % de las
     * veces (#109).
     */
    const tipos = new Map();
    const indexar = (d) => {
        for (const e of fs.readdirSync(d, { withFileTypes: true })) {
            const q = path.join(d, e.name);
            if (e.isDirectory()) { indexar(q); continue; }
            if (!e.name.endsWith('.ts')) continue;
            const src = fs.readFileSync(q, 'utf8');
            for (const m of src.matchAll(/(?:interface|type)\s+(\w+)\s*(?:=\s*)?\{([\s\S]*?)\n\}/g)) {
                tipos.set(m[1], m[2]);
            }
        }
    };
    indexar(dir);

    const porRuta = new Map();
    const noLiterales = [];

    for (const f of ficheros) {
        const src = fs.readFileSync(f, 'utf8');
        const lineas = src.split('\n');

        /**
         * La ruta de una variable: su última asignación `\`${apiBase}/x\`` hacia atrás.
         *
         * Heurístico, y funciona porque el estilo de estos clientes es consistente — `const url`
         * justo encima de la llamada. Se limita a 30 líneas para no cruzar de método.
         */
        const resolverRuta = (variable, desde) => {
            for (let j = desde; j >= Math.max(0, desde - 30); j--) {
                const m = lineas[j].match(
                    new RegExp(`\\b${variable}\\s*=\\s*\`\\$\\{apiBase\\}/([^\`?]*)\``));
                if (m) return ultimoSegmento(m[1].replace(/\$\{[^}]*\}/g, '{x}'));
            }
            return null;
        };

        /**
         * Las claves de PRIMER NIVEL de un objeto, que puede abarcar varias líneas.
         *
         * Los bloques anidados se vacían antes de partir por comas, y no es un detalle: con
         * `slot: { date: string; time: string }` el parser plano sacaba `slot` (bien) y
         * además `time` (mal), y el gate denunciaba un cuerpo correcto. Un gate que grita
         * sobre algo que está bien se desactiva a la tercera.
         */
        const clavesDeLiteral = (texto) => {
            let plano = texto;
            let previo;
            do {
                previo = plano;
                plano = plano.replace(/\{[^{}]*\}/g, '{}');
            } while (plano !== previo);

            return plano
                .split(',')
                .map((p) => p.split(':')[0].trim().replace(/^\.\.\./, ''))
                .filter((k) => /^[A-Za-z_]\w*$/.test(k));
        };

        /**
         * El TIPO INLINE de un parámetro del método, si el cuerpo llega como argumento.
         *
         * Varios clientes no construyen el objeto: lo reciben ya hecho y tipado en la firma
         * —`body: { patientId: string; soap: SoapNote },`— y ahí están las claves, separadas
         * por `;` en vez de por `,`. Es la forma de los seis endpoints de EHR, que fueron seis
         * de los defectos más caros del barrido: sin esto el gate no los vería.
         */
        const tipoInlineDeParametro = (variable, desde) => {
            const tope = inicioDelMetodo(desde);
            for (let j = desde; j >= tope; j--) {
                const m = lineas[j].match(new RegExp(`^\\s*${variable}\\s*:\\s*\\{(.*)\\}`));
                if (m) return m[1].replace(/;/g, ',');
            }
            return null;
        };

        /**
         * Hasta dónde puede mirar hacia atrás una búsqueda: el cierre del método ANTERIOR.
         *
         * Sin este tope, resolver el parámetro de una llamada se colaba al método de arriba y
         * cogía SU cuerpo: `POST /lead` acabó denunciado por mandar `slot` y `mode`, que son
         * del método de agendar visita, veinte líneas más arriba. Un gate que denuncia un
         * cuerpo correcto se desactiva a la tercera, así que el alcance se acota de verdad en
         * vez de subirle el número de líneas.
         */
        const inicioDelMetodo = (desde) => {
            for (let j = desde; j >= 0; j--) {
                if (/^ {2}\}/.test(lineas[j])) return j + 1;
            }
            return 0;
        };

        /** El TIPO NOMBRADO de un parámetro (`draft: NewMessage,`), resuelto en el índice. */
        const tipoNombradoDeParametro = (variable, desde) => {
            const tope = inicioDelMetodo(desde);
            for (let j = desde; j >= tope; j--) {
                const m = lineas[j].match(new RegExp(`^\\s*${variable}\\s*:\\s*(\\w+)\\s*,?\\s*$`));
                if (m && tipos.has(m[1])) {
                    // Los campos de una interfaz van con `;` o con salto, y llevan `readonly`
                    // y `?` que no son parte del nombre.
                    return tipos.get(m[1])
                        .replace(/;/g, ',')
                        .replace(/\n/g, ',')
                        .replace(/\breadonly\s+/g, '')
                        .replace(/\?\s*:/g, ':');
                }
            }
            return null;
        };

        /** El objeto literal asignado a una variable, buscando hacia atrás. */
        const literalDeVariable = (variable, desde) => {
            const tope = inicioDelMetodo(desde);
            for (let j = desde; j >= tope; j--) {
                if (!new RegExp(`\\b(?:const|let|var)\\s+${variable}\\s*(?::[^=]*)?=\\s*\\{`).test(lineas[j])) continue;
                // Junta hasta la llave de cierre al mismo nivel de indentación.
                let bloque = lineas[j].slice(lineas[j].indexOf('{') + 1);
                for (let k = j + 1; k < Math.min(lineas.length, j + 30); k++) {
                    if (/^\s*\}/.test(lineas[k])) break;
                    bloque += '\n' + lineas[k];
                }
                return bloque;
            }
            return null;
        };

        lineas.forEach((linea, i) => {
            // Las DOS formas: el helper del cliente, y un fetch con JSON.stringify.
            const viaHelper = linea.match(/\b(?:post|put|patch)Json\s*\(\s*(\w+)\s*,\s*(.*)$/i);
            const viaStringify = linea.match(/JSON\.stringify\(\s*(.*?)\s*\)/);
            if (!viaHelper && !viaStringify) return;

            // La ruta: del primer argumento del helper, o de la variable `url` más cercana.
            const ruta = viaHelper
                ? resolverRuta(viaHelper[1], i)
                : (resolverRuta('url', i) ?? null);
            if (!ruta) return;

            // Se limpia la cola de la llamada (`body);` → `body`): sin esto el argumento no
            // pasa el test de identificador y el cuerpo caía fuera del cruce en silencio.
            const argumento = (viaHelper ? viaHelper[2] : viaStringify[1])
                .trim()
                .replace(/\)\s*;?\s*$/, '')
                .trim();

            let claves = null;
            if (argumento.startsWith('{')) {
                claves = clavesDeLiteral(argumento.slice(1).split('}')[0]);
            } else if (/^[A-Za-z_]\w*$/.test(argumento)) {
                const bloque = literalDeVariable(argumento, i)
                    ?? tipoInlineDeParametro(argumento, i)
                    ?? tipoNombradoDeParametro(argumento, i);
                if (bloque !== null) claves = clavesDeLiteral(bloque);
            }

            if (claves === null) {
                // Construido por una función: fuera del cruce, y se declara.
                const helper = argumento.match(/^(\w+)\s*\(/);
                noLiterales.push(`${app}:${ruta}${helper ? ` (via ${helper[1]}())` : ''}`);
                return;
            }

            porRuta.set(ruta, new Set([...(porRuta.get(ruta) ?? []), ...claves]));
        });
    }

    return { porRuta, noLiterales };
}

const problemas = [];
const fuera = [];
let cruzadas = 0;
let rutas = 0;

for (const { app, controllers } of PARES) {
    const ui = mandadoPorLaUi(app);
    const cms = declaradoPorElCms(controllers);
    if (!ui || !cms) continue;

    fuera.push(...ui.noLiterales);

    for (const [ruta, mandadas] of ui.porRuta) {
        const declaradas = cms.get(ruta);
        if (!declaradas) continue;   // ruta que este controller no sirve: no es asunto del gate
        rutas++;

        const perdidas = [...mandadas].filter((k) => !declaradas.has(k));
        cruzadas += mandadas.size - perdidas.length;

        if (perdidas.length > 0) {
            problemas.push(
                `[${app}] POST /${ruta} — el cliente manda ${perdidas.length} clave(s) que el `
                + `record NO declara: ${perdidas.join(', ')}\n    → System.Text.Json las descarta `
                + `SIN DECIR NADA (el dato se pierde), o revienta el binding entero (400 siempre). `
                + `Las dos cosas las tapa el catch del cliente.`);
        }
    }
}

console.log('G-7 · cuerpos de petición CMS ↔ UI');
if (fuera.length > 0) {
    console.log(`  ! ${fuera.length} cuerpo(s) construido(s) por un helper — FUERA del cruce:`);
    for (const f of fuera) console.log(`      ${f}`);
}

if (problemas.length > 0) {
    console.error(`\n✗ ${problemas.length} cuerpo(s) que el borde no puede recibir:`);
    for (const p of problemas) console.error(`  ${p}`);
    process.exit(1);
}

console.log(`\n✓ ${cruzadas} claves de petición ligan en ${rutas} rutas — ninguna se pierde.`);
