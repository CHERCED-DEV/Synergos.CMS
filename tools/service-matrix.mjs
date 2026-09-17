#!/usr/bin/env node
//
// Qué servicios del árbol de capacidades/orquestadores hay que construir como imagen.
//
// ─────────────────────────────────────────────────────────────────────────────
// SE DERIVA DEL DISCO, NO SE ESCRIBE A MANO.
//
// Una lista escrita a mano en el workflow se desincroniza el día que nadie
// mira: alguien añade `Synergos.Api.Invoicing`, el CI sigue verde porque
// construye las 22 de siempre, y la capacidad nueva simplemente no existe en
// producción. No falla — falta. Que es peor.
//
// Es el mismo razonamiento de `ApiMoldTests`, que descubre las capacidades
// recorriendo el disco en vez de enumerarlas: el gate crece con el catálogo
// sin que nadie lo mantenga.
// ─────────────────────────────────────────────────────────────────────────────
//
//   node tools/service-matrix.mjs            → JSON, para el workflow
//   node tools/service-matrix.mjs --list     → uno por línea, para leerlo
//
import { readdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const raiz = process.cwd();

/**
 * Un directorio es un servicio desplegable si es `Synergos.Api.*` o
 * `Synergos.Bff.*` Y tiene `Program.cs`.
 *
 * El `Program.cs` es el filtro que importa, y no es cosmético: `Synergos.Bff.Core`
 * empieza igual que los orquestadores y NO es un servicio — es la máquina de
 * sagas, una biblioteca. Excluirla por nombre exigiría acordarse de la excepción;
 * excluirla por "no tiene punto de entrada" es una propiedad que se mantiene sola.
 */
export function servicios(dir = raiz) {
  return descubrir(dir).map((s) => s.nombre);
}

/**
 * Lo mismo, pero con la RUTA al lado — que es lo que necesita `Dockerfile.service`
 * desde el #136, porque el backend ya no está plano en la raíz.
 *
 * Devuelve `{ nombre, ruta }` con la ruta RELATIVA a la raíz del repo y con `/`
 * siempre, porque quien la consume es Docker y no el sistema de ficheros local.
 *
 * La búsqueda es recursiva y NO enumera `backend/{nucleo,capacidades,orquestadores}`:
 * una lista de carpetas padre acá sería el mismo defecto que este fichero existe
 * para evitar, un nivel más arriba — alguien añade `backend/verticales/` y el CI
 * sigue verde construyendo las 22 de siempre.
 */
export function descubrir(dir = raiz, prefijo = '') {
  const salida = [];

  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (!e.isDirectory()) continue;
    // Ni la salida del build ni el legado (§6).
    if (['bin', 'obj', 'node_modules', '_archive', '.git'].includes(e.name)) continue;

    const rel = prefijo ? `${prefijo}/${e.name}` : e.name;

    if ((e.name.startsWith('Synergos.Api.') || e.name.startsWith('Synergos.Bff.'))
        && existsSync(join(dir, e.name, 'Program.cs'))) {
      salida.push({ nombre: e.name, ruta: rel });
      continue;   // un servicio no contiene otro
    }

    salida.push(...descubrir(join(dir, e.name), rel));
  }

  return salida.sort((a, b) => a.nombre.localeCompare(b.nombre, 'en'));
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const lista = descubrir();

  if (lista.length === 0) {
    // Un descubrimiento roto dejaría el workflow "verde" construyendo cero
    // imágenes. Un gate que no puede fallar es peor que no tener gate.
    console.error('service-matrix: no se encontró NINGÚN servicio. ¿Se corrió desde la raíz del repo?');
    process.exit(1);
  }

  // El JSON lleva la RUTA además del nombre: el workflow le pasa las dos a
  // `Dockerfile.service`, que cruza que concuerden antes de construir.
  console.log(process.argv.includes('--list')
    ? lista.map((s) => `${s.nombre}  ${s.ruta}`).join('\n')
    : JSON.stringify(lista));
}
