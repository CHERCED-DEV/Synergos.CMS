#!/usr/bin/env node
/**
 * summarize.mjs — agrega los results/*.json en el informe de la Fase 4.
 *
 *   node summarize.mjs [> VEREDICTO-ui.md]
 *
 * Exit code 0 si no hay ROTO; 1 si hay alguno. Pensado para que la Fase 4
 * pueda gatear igual que los otros gates del repo.
 */
import { readFileSync, readdirSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const RESULTS = join(HERE, 'results');

if (!existsSync(RESULTS)) {
  console.error('No hay results/ — corré `npm run verify` primero.');
  process.exit(1);
}

const items = readdirSync(RESULTS)
  .filter((f) => f.endsWith('.json'))
  .map((f) => JSON.parse(readFileSync(join(RESULTS, f), 'utf8')))
  .sort((a, b) => a.name.localeCompare(b.name));

const bucket = (o) => items.filter((i) => i.outcome === o);
const ok = bucket('OK');
const roto = bucket('ROTO');
const noSembrado = bucket('NO-SEMBRADO');

const icon = { OK: '✅', ROTO: '❌', 'NO-SEMBRADO': '⚪' };

const out = [];
out.push('# Fase 4 — verificación funcional en Chrome');
out.push('');
out.push(`- Generado: ${new Date().toISOString()}`);
out.push(`- Rutas evaluadas: **${items.length}**`);
out.push(`- ✅ OK: **${ok.length}** · ❌ ROTO: **${roto.length}** · ⚪ no verificadas: **${noSembrado.length}**`);
out.push('');
out.push('> ⚪ = la ruta no se pudo verificar: devolvió 404, o Umbraco sirvió su');
out.push('> página "No published content". En ambos casos falta el contenido de la');
out.push('> Fase 3, o el slug de `routes.json` no coincide. **No cuenta como verde.**');
out.push('');
out.push('| | Template | Ruta | HTTP | Detalle |');
out.push('|---|---|---|---|---|');
for (const i of items) {
  const crit = i.critical ? ' 🔴' : '';
  out.push(`| ${icon[i.outcome]}${crit} | ${i.name} | \`${i.path}\` | ${i.status || '—'} | ${i.reason || ''} |`);
}
out.push('');
out.push('🔴 = ruta crítica: no depende de contenido sembrado, siempre debe responder.');

if (roto.length) {
  out.push('');
  out.push('## Hallazgos');
  for (const i of roto) {
    out.push('');
    out.push(`### ❌ ${i.name} — \`${i.path}\` (HTTP ${i.status})`);
    out.push(`**${i.reason}**`);
    if (i.consoleErrors.length) {
      out.push('');
      out.push('Consola:');
      out.push('```');
      i.consoleErrors.slice(0, 20).forEach((e) => out.push(e));
      out.push('```');
    }
    if (i.failedRequests.length) {
      out.push('');
      out.push('Requests fallidos:');
      out.push('```');
      i.failedRequests.slice(0, 20).forEach((e) => out.push(e));
      out.push('```');
    }
    if (i.missingLandmarks.length) {
      out.push('');
      out.push(`Landmarks faltantes: \`${i.missingLandmarks.join('`, `')}\``);
    }
  }
}

const conSeo = items.filter((i) => i.seo);
if (conSeo.length) {
  out.push('');
  out.push('## `<head>` SEO');
  out.push('');
  out.push('| Template | title | canonical | og:image | robots |');
  out.push('|---|---|---|---|---|');
  const mark = (v) => (v ? '✅' : '—');
  for (const i of conSeo) {
    out.push(`| ${i.name} | ${mark(i.seo.title)} | ${mark(i.seo.canonical)} | ${mark(i.seo.ogImage)} | ${i.seo.robots ?? '—'} |`);
  }
}

out.push('');
out.push(`## Veredicto de la fase: ${roto.length ? '❌ NO-GO' : noSembrado.length ? '⚠️ PARCIAL' : '✅ GO'}`);
if (noSembrado.length && !roto.length) {
  out.push('');
  out.push(`Nada roto, pero **${noSembrado.length} ruta(s) quedaron sin verificar**. La fase`);
  out.push('no cierra en verde hasta que se siembre el contenido o se corrijan los slugs.');
}

console.log(out.join('\n'));
process.exit(roto.length ? 1 : 0);
