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
  return readdirSync(dir, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => e.name)
    .filter((n) => n.startsWith('Synergos.Api.') || n.startsWith('Synergos.Bff.'))
    .filter((n) => existsSync(join(dir, n, 'Program.cs')))
    .sort();
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const lista = servicios();

  if (lista.length === 0) {
    // Un descubrimiento roto dejaría el workflow "verde" construyendo cero
    // imágenes. Un gate que no puede fallar es peor que no tener gate.
    console.error('service-matrix: no se encontró NINGÚN servicio. ¿Se corrió desde la raíz del repo?');
    process.exit(1);
  }

  console.log(process.argv.includes('--list') ? lista.join('\n') : JSON.stringify(lista));
}
