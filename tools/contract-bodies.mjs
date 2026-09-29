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
 * TODO FICHERO SE LEE EN LF, Y NO ES UN DETALLE (#170)
 * ----------------------------------------------------
 * Los regex de abajo están escritos contra líneas LF: `(.*)$` no casa si la línea acaba en
 * `\r`, porque `.` no consume un fin de línea. Con el UI en CRLF —Windows con
 * `core.autocrlf=true`, o cualquier clon con otra configuración— el gate perdía toda llamada
 * `postJson(url, …)` y salía VERDE cruzando **2 claves en 1 ruta** contra 57 en 22 del mismo
 * commit en LF. 2 no es vacío, así que ninguna red de seguridad lo veía. El `.gitattributes`
 * arregla el checkout de ESTE repo; el lector no puede depender de cómo se clonó el otro.
 *
 * USO
 *   node tools/contract-bodies.mjs [--ui-path=/ruta]   # o SYNERGOS_UI_PATH
 *   node tools/contract-bodies.mjs --autoprueba        # sus fixtures, sin repos ni red
 */

import fs from 'node:fs';
import os from 'node:os';
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

/** El ÚNICO lector de fuentes del gate: todo sale en LF, venga como venga del disco (#170). */
const leer = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n?/g, '\n');

const sinComentarios = (s) => s.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/[^\n]*/g, ' ');
const camel = (s) => s.charAt(0).toLowerCase() + s.slice(1);
/**
 * La FORMA de una ruta: sus segmentos, con todo parámetro normalizado a `{}` (#164).
 *
 * Antes esto era `ultimoSegmento`, que descartaba los `{…}` y se quedaba con el último literal —
 * así que `POST /rentals/{id}/{accion}` **colapsaba sobre `rentals`** cuando la acción viajaba
 * interpolada, y el gate leía las claves de `return`/`cancel` como si se mandaran al POST del
 * recurso padre: acusaba una ruta que nadie estaba llamando mal. G-7 es **error y no trinquete**,
 * así que un falso positivo suyo es cómo se aprende a ignorarlo — la lección del #158.
 *
 * Comparar la forma entera conserva lo que el colapso acertaba —`post/{}` sigue ligando con el
 * `[HttpPost("post/{id}")]` del borde, que es el patrón de 15 de las 17 rutas medidas que acaban en
 * interpolado— y deja fuera lo que no puede resolver, en vez de atribuirlo. Pierde cobertura y no
 * miente, que es el orden correcto de preferencias.
 *
 * Lo que sigue sin hacer, y por eso las que no ligan se DICEN en vez de callarse: el gate no
 * resuelve el VALOR de un segmento interpolado —eso es seguir una variable, que G-7 declara que no
 * hace— así que no puede distinguir un id de una acción.
 */
const forma = (ruta) =>
    ruta.split('/').filter(Boolean).map((s) => (s.startsWith('{') ? '{}' : s)).join('/');

/** ¿El último segmento es un parámetro? Entonces la forma puede ser id O acción, y no se sabe. */
const acabaInterpolada = (ruta) => forma(ruta).split('/').at(-1) === '{}';

/** ruta → claves que el record de esa ruta DECLARA. */
function declaradoPorElCms(controllers) {
    const porRuta = new Map();
    let alguno = false;

    for (const c of controllers) {
        const f = path.join(CMS, 'Synergos.CMS.Web', 'Controllers', c);
        if (!fs.existsSync(f)) continue;
        alguno = true;
        const src = sinComentarios(leer(f));

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
            if (claves) porRuta.set(forma(m[1]), claves);
        }
    }

    return alguno ? porRuta : null;
}

/** ruta → claves que el cliente MANDA, de los cuerpos literales. */
function mandadoPorLaUi(app, ui = UI) {
    const dir = path.join(ui, 'platforms', 'angular', 'apps', 'elements', 'modules', app, 'src');
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
            const src = leer(q);
            for (const m of src.matchAll(/(?:interface|type)\s+(\w+)\s*(?:=\s*)?\{([\s\S]*?)\n\}/g)) {
                tipos.set(m[1], m[2]);
            }
        }
    };
    indexar(dir);

    const porRuta = new Map();
    const noLiterales = [];

    for (const f of ficheros) {
        const src = leer(f);
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
                if (m) return forma(m[1].replace(/\$\{[^}]*\}/g, '{x}'));
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

/**
 * Los fixtures del cruce por forma (#164), ejecutados y no leídos.
 *
 * <b>Qué cubre:</b> la parte PURA, que es donde vivía el defecto — `forma`, `acabaInterpolada` y la
 * decisión de atribuir o no. El fixture lleva **las dos formas sobre el mismo recurso**, que es lo
 * único que lo prueba: con sólo la literal, el colapso por último segmento y el cruce por forma dan
 * el mismo resultado y el defecto pasa en verde.
 *
 * <b>Y el fin de línea del lado UI (#170)</b>: un cliente de cuatro rutas escrito en LF y en CRLF,
 * leído por `mandadoPorLaUi` —descubrimiento de ficheros y regex del lado UI incluidos—, tiene que
 * dar la cifra escrita en los dos. Sin `leer()` normalizando, el CRLF da 1 clave en 1 ruta.
 *
 * <b>Qué NO cubre, dicho para no mentir sobre su alcance:</b> el lado CMS (records y `[HttpPost]`
 * de los controllers) y los clientes reales del UI. Eso necesita los dos repos, y lo ejercita la
 * corrida de verdad.
 */
function autoprueba() {
    const casos = [];
    const caso = (nombre, real, esperado) => casos.push({ nombre, real, esperado });

    // El borde declara las dos: el POST del recurso y el de la acción.
    const cms = new Map([
        ['rentals', new Set(['sku', 'days'])],
        ['rentals/{}/return', new Set(['amount'])],
        ['post/{}', new Set(['type'])],
    ]);

    caso('la forma conserva los parámetros en su sitio', forma('rentals/{id}/return'), 'rentals/{}/return');
    caso('un parámetro con restricción también normaliza', forma('moderation/{nodeId:int}/ok'), 'moderation/{}/ok');
    caso('el id de recurso SIGUE ligando (15 de las 17 rutas medidas)',
        cms.get(forma('post/{x}')) !== undefined, true);

    // El corazón del #164: la acción interpolada NO se atribuye al recurso padre.
    caso('la acción LITERAL liga contra su propio record',
        [...cms.get(forma('rentals/{id}/return'))].join(','), 'amount');
    caso('la acción INTERPOLADA no liga con nada', cms.get(forma('rentals/{id}/{accion}')), undefined);
    caso('…y sobre todo NO se atribuye al recurso padre',
        cms.get(forma('rentals/{id}/{accion}')) === cms.get('rentals'), false);

    // Y el colapso viejo, escrito acá para que se vea DÓNDE estaba el falso positivo. Al escribir
    // estos dos fixtures afirmé que hacía «indistinguibles» las dos formas y el fixture lo
    // desmintió: la literal colapsaba a `return` —su propia clave, y el borde colapsa igual, así
    // que cruzaba bien— y la interpolada a `rentals`. El defecto no era confundirlas: era MANDAR
    // UNA AL RECORD DEL PADRE, que es un record que existe y no declara sus claves.
    const colapsoViejo = (r) => r.split('/').filter((p) => p && !p.startsWith('{')).pop() ?? '';
    caso('el colapso viejo llevaba la acción literal a su propia clave',
        colapsoViejo('rentals/{id}/return'), 'return');
    caso('…y la interpolada al recurso PADRE, que es de donde salía la acusación',
        colapsoViejo('rentals/{id}/{accion}'), 'rentals');
    caso('…un record que existe y no declara sus claves',
        cms.get(colapsoViejo('rentals/{id}/{accion}'))?.has('amount'), false);

    caso('acabaInterpolada ve el parámetro final', acabaInterpolada('post/{id}'), true);
    caso('acabaInterpolada NO marca una acción literal', acabaInterpolada('rentals/{id}/return'), false);

    // ── El lado UI en CRLF da lo MISMO que en LF (#170) ──────────────────────────────────────
    //
    // Se escribe el mismo cliente dos veces en un directorio temporal —LF y CRLF— y se lee con
    // `mandadoPorLaUi`, el código de verdad y no una copia. El fixture lleva las cuatro formas
    // de cuerpo que el cruce sabe leer, y la de `JSON.stringify` es la que exige el caso: es la
    // única que sobrevivía al CRLF, así que sin ella las dos corridas darían 0 y 0 —«iguales»—
    // y sin la cifra esperada, 1 y 1 también lo serían. Por eso se compara contra la cifra
    // escrita Y entre sí.
    const cliente = [
        'export interface NewNote {',
        '  readonly title: string;',
        '  body?: string;',
        '}',
        '',
        'export class FixtureApiClient {',
        '  async lead(apiBase: string, name: string): Promise<unknown> {',
        '    const url = `${apiBase}/lead`;',
        "    return this.postJson(url, { name, phone: '1' });",
        '  }',
        '',
        '  async note(',
        '    apiBase: string,',
        '    draft: NewNote,',
        '  ): Promise<unknown> {',
        '    const url = `${apiBase}/notes/${draft.title}/draft`;',
        '    return this.postJson(url, draft);',
        '  }',
        '',
        '  async visit(',
        '    apiBase: string,',
        '    body: { listingId: string; slot: { date: string; time: string } },',
        '  ): Promise<unknown> {',
        '    const url = `${apiBase}/visit`;',
        '    return this.postJson(url, body);',
        '  }',
        '',
        '  async favorite(apiBase: string, listingId: string): Promise<unknown> {',
        '    const url = `${apiBase}/favorite`;',
        "    return fetch(url, { method: 'POST', body: JSON.stringify({ listingId }) });",
        '  }',
        '}',
        '',
    ];
    const plano = (r) => (r === null ? 'null' : [...r.porRuta]
        .map(([ruta, claves]) => `${ruta}:${[...claves].sort().join('+')}`).sort().join(' '));
    const cifra = (r) => (r === null ? 'null'
        : `${[...r.porRuta.values()].reduce((n, c) => n + c.size, 0)} claves en ${r.porRuta.size} rutas`);

    const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'g7-autoprueba-'));
    try {
        const escribir = (raiz, fin) => {
            const dir = path.join(raiz, 'platforms', 'angular', 'apps', 'elements', 'modules', 'fixture', 'src');
            fs.mkdirSync(dir, { recursive: true });
            fs.writeFileSync(path.join(dir, 'fixture-api.client.ts'), cliente.join(fin));
            return raiz;
        };
        const lf = mandadoPorLaUi('fixture', escribir(path.join(tmp, 'lf'), '\n'));
        const crlf = mandadoPorLaUi('fixture', escribir(path.join(tmp, 'crlf'), '\r\n'));

        caso('el fixture en LF cruza sus cuatro rutas', cifra(lf), '7 claves en 4 rutas');
        caso('…con las claves de cada una',
            plano(lf), 'favorite:listingId lead:name+phone notes/{}/draft:body+title visit:listingId+slot');
        caso('el MISMO fixture en CRLF da la misma cifra (daba 1 clave en 1 ruta)', cifra(crlf), cifra(lf));
        caso('…y las mismas claves por ruta', plano(crlf), plano(lf));
    } finally {
        fs.rmSync(tmp, { recursive: true, force: true });
    }

    let fallos = 0;
    for (const { nombre, real, esperado } of casos) {
        const ok = real === esperado;
        if (!ok) fallos++;
        console.log(`  ${ok ? '✓' : '✗'} ${nombre}${ok ? '' : `\n      esperado ${JSON.stringify(esperado)}, dio ${JSON.stringify(real)}`}`);
    }

    console.log(`\n${fallos === 0 ? '✓' : '✗'} ${casos.length - fallos}/${casos.length} fixtures del cruce por forma y del fin de línea.`);
    process.exit(fallos === 0 ? 0 : 1);
}

if (process.argv.includes('--autoprueba')) autoprueba();

const problemas = [];
const fuera = [];
const sinResolver = [];
let cruzadas = 0;
let rutas = 0;

for (const { app, controllers } of PARES) {
    const ui = mandadoPorLaUi(app);
    const cms = declaradoPorElCms(controllers);
    if (!ui || !cms) continue;

    fuera.push(...ui.noLiterales);

    for (const [ruta, mandadas] of ui.porRuta) {
        const declaradas = cms.get(ruta);
        if (!declaradas) {
            // Una ruta que este controller no sirve no es asunto del gate y se calla. Pero si su
            // último segmento es un parámetro, puede que el borde SÍ la sirva y que lo que falle sea
            // el cruce: el gate no resuelve el valor del segmento, así que no sabe si es un id o una
            // acción (#164). Eso se DICE, como los cuerpos que construye un helper — callarlo la
            // contaría como cubierta, y atribuirla al recurso padre era el falso positivo de antes.
            // …y sólo si el cliente MANDA algo. Con el cuerpo vacío no hay nada que cruzar, así
            // que declararla «fuera del cruce» nombra a un inocente — le pasa a
            // `blogs:/follow/{}`, que postea `{}` contra un endpoint que no declara `[FromBody]`:
            // las dos puntas de acuerdo, cero claves, nada que decir. Un gate que nombra al bueno
            // enseña a ignorarlo (#158), y esta lista se lee en cada corrida.
            if (acabaInterpolada(ruta) && mandadas.size > 0) sinResolver.push(`${app}:/${ruta}`);
            continue;
        }
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

if (sinResolver.length > 0) {
    console.log(
        `  ! ${sinResolver.length} ruta(s) cuyo último segmento es un parámetro y que ningún POST del `
        + 'borde declara con esa forma — FUERA del cruce (#164):');
    for (const r of sinResolver) console.log(`      ${r}`);
    console.log(
        '      El gate no resuelve el valor de un segmento interpolado, así que no puede distinguir '
        + 'un id de una acción. Antes esto se atribuía al recurso padre y acusaba a la ruta '
        + 'equivocada.');
}

if (problemas.length > 0) {
    console.error(`\n✗ ${problemas.length} cuerpo(s) que el borde no puede recibir:`);
    for (const p of problemas) console.error(`  ${p}`);
    process.exit(1);
}

console.log(`\n✓ ${cruzadas} claves de petición ligan en ${rutas} rutas — ninguna se pierde.`);
