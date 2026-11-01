/**
 * Las capacidades de una lib de dominio del UI que lee cada par app↔controller de G-6 y G-7 (014-despiece-tienda, T009).
 *
 * Una app de una vertical ya no tiene sus clientes en su carpeta: viven en `libs/dominio/<vertical>/src/lib/<capacidad>`,
 * uno por capacidad. Un par con `dominio: { nombre }` lee todas las capacidades salvo las que otro par del mismo dominio
 * reclama con `carpetas`; uno con `carpetas`, sólo ésas. Así cada app cruza lo que pide y no lo que pide su vecina —la
 * consola del vendedor no las claves del carrito—, y una capacidad nueva entra sola en el par que no nombra carpetas en
 * vez de quedarse fuera del cruce sin que nada lo diga.
 *
 * Es un módulo aparte, sin efectos al importarse, para que los dos gates lean la MISMA regla: la segunda copia de una
 * regla es la que se desvía.
 */

import fs from 'node:fs';
import path from 'node:path';

/**
 * @param {ReadonlyArray<{ app: string, dominio?: { nombre: string, carpetas?: string[] } }>} pares
 * @param {{ app: string, dominio?: { nombre: string, carpetas?: string[] } }} par
 * @param {string} raizUi la raíz del repo hermano Synergos.UI
 * @returns {string[]} las carpetas de capacidad que el par lee, que existen
 */
export function carpetasDelDominio(pares, par, raizUi) {
    if (!par.dominio) return [];
    const raiz = path.join(raizUi, 'platforms', 'angular', 'libs', 'dominio', par.dominio.nombre, 'src', 'lib');
    if (!fs.existsSync(raiz)) return [];
    const reclamadas = new Set(
        pares.filter((p) => p !== par && p.dominio?.nombre === par.dominio.nombre).flatMap((p) => p.dominio.carpetas ?? []));
    const todas = fs.readdirSync(raiz, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => e.name);
    return (par.dominio.carpetas ?? todas.filter((c) => !reclamadas.has(c)))
        .map((c) => path.join(raiz, c))
        .filter((d) => fs.existsSync(d));
}
