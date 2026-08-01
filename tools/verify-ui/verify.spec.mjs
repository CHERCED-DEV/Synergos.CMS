import { test, expect } from '@playwright/test';
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const RESULTS = join(HERE, 'results');
const SCREENS = join(RESULTS, 'screens');
mkdirSync(SCREENS, { recursive: true });

const { routes } = JSON.parse(readFileSync(join(HERE, 'routes.json'), 'utf8'));

// Ruido conocido que no es un hallazgo. Mantener esta lista CORTA y
// justificada: cada entrada acá es un error que dejamos de ver.
const IGNORED_REQUESTS = [/\/favicon\.ico$/];
const isNoise = (url) => IGNORED_REQUESTS.some((re) => re.test(url));

/**
 * Clasifica en tres cubetas, no en dos. La distinción importa: una ruta
 * que devuelve 404 porque el contenido no se sembró NO es lo mismo que
 * una rota, y el plan exige que no se cuente como verde ninguna de las dos.
 */
function classify(route, status, consoleErrors, failedRequests, missingLandmarks) {
  const reachable = route.expectAnyStatus
    ? true
    : route.expectStatus !== undefined
      ? status === route.expectStatus
      : status >= 200 && status < 400;

  if (!reachable) {
    if (status === 404) return { outcome: 'NO-SEMBRADO', reason: `HTTP 404 en ${route.path}` };
    return { outcome: 'ROTO', reason: `HTTP ${status} inesperado` };
  }
  if (consoleErrors.length) return { outcome: 'ROTO', reason: `${consoleErrors.length} error(es) de consola` };
  if (failedRequests.length) return { outcome: 'ROTO', reason: `${failedRequests.length} request(s) fallido(s)` };
  if (missingLandmarks.length) return { outcome: 'ROTO', reason: `landmarks faltantes: ${missingLandmarks.join(', ')}` };
  return { outcome: 'OK', reason: '' };
}

for (const route of routes) {
  test(`${route.name} — ${route.path}`, async ({ page }, testInfo) => {
    const consoleErrors = [];
    const failedRequests = [];

    page.on('console', (msg) => {
      if (msg.type() === 'error' && !isNoise(msg.location()?.url ?? '')) {
        consoleErrors.push(msg.text());
      }
    });
    page.on('pageerror', (err) => consoleErrors.push(`[pageerror] ${err.message}`));
    page.on('requestfailed', (req) => {
      if (!isNoise(req.url())) failedRequests.push(`${req.url()} — ${req.failure()?.errorText}`);
    });
    page.on('response', (res) => {
      // La respuesta del documento principal NO es un "request fallido": su
      // status ya se evalúa aparte. Comparar contra page.url() no alcanza —
      // cuando el evento dispara, page.url() sigue siendo about:blank.
      const isMainDocument =
        res.request().isNavigationRequest() && res.frame() === page.mainFrame();
      if (res.status() >= 400 && !isNoise(res.url()) && !isMainDocument) {
        failedRequests.push(`${res.url()} — HTTP ${res.status()}`);
      }
    });

    const response = await page.goto(route.path, { waitUntil: 'load', timeout: 30_000 });
    const status = response?.status() ?? 0;

    // Endpoints (JSON, health checks): sólo importa el status. Evaluarles
    // consola, landmarks o SEO produce hallazgos falsos.
    if (route.endpoint) {
      const reachable = route.expectAnyStatus || (status >= 200 && status < 400);
      const result = {
        name: route.name, path: route.path, critical: route.critical === true,
        status, outcome: reachable ? 'OK' : 'ROTO',
        reason: reachable ? '' : `HTTP ${status} inesperado`,
        consoleErrors: [], failedRequests: [], missingLandmarks: [], seo: null,
        screenshot: null, at: new Date().toISOString(),
      };
      writeFileSync(join(RESULTS, `${route.name}.json`), JSON.stringify(result, null, 2));
      expect(result.outcome, `${route.name}: ${result.reason}`).toBe('OK');
      return;
    }

    // Umbraco sirve su página "No published content" con HTTP 200 para
    // CUALQUIER ruta cuando no hay nada publicado. Sin detectarla, una base
    // sin contenido se reporta como 15 templates rotos por falta de <main>,
    // que es exactamente el diagnóstico equivocado: lo que falta es el
    // contenido de la Fase 3, no el markup.
    const isBoilerplate = await page.evaluate(() =>
      document.title === 'Umbraco: No published content' ||
      document.querySelector('link[href*="nonodes"]') !== null);

    // Landmarks semánticos del Layout Composer (ADR 0017): nav/main/aside
    // los emiten los partials por area alias. Su ausencia en un template
    // que debería tenerlos es una regresión de SSR, no un detalle estético.
    const missingLandmarks = [];
    for (const lm of route.landmarks ?? []) {
      if ((await page.locator(lm).count()) === 0) missingLandmarks.push(lm);
    }

    // <head> SEO (Ola 44.2). Se registra siempre; sólo el title ausente
    // cuenta como rotura — canonical y og:image se reportan como aviso
    // porque dependen de que el editor los haya cargado.
    let seo = null;
    if (route.seo) {
      seo = await page.evaluate(() => ({
        title: document.title || null,
        canonical: document.querySelector('link[rel="canonical"]')?.getAttribute('href') ?? null,
        ogImage: document.querySelector('meta[property="og:image"]')?.getAttribute('content') ?? null,
        robots: document.querySelector('meta[name="robots"]')?.getAttribute('content') ?? null,
      }));
      if (!seo.title) consoleErrors.push('[seo] <title> vacío o ausente');
    }

    const screenshot = join(SCREENS, `${route.name}.png`);
    await page.screenshot({ path: screenshot, fullPage: true }).catch(() => {});

    const { outcome, reason } = isBoilerplate
      ? { outcome: 'NO-SEMBRADO', reason: 'Umbraco sirve "No published content" — no hay contenido publicado en esta ruta' }
      : classify(route, status, consoleErrors, failedRequests, missingLandmarks);

    const result = {
      name: route.name,
      path: route.path,
      critical: route.critical === true,
      status,
      outcome,
      reason,
      consoleErrors,
      failedRequests,
      missingLandmarks,
      seo,
      screenshot,
      at: new Date().toISOString(),
    };
    writeFileSync(join(RESULTS, `${route.name}.json`), JSON.stringify(result, null, 2));
    await testInfo.attach(`${route.name}.json`, { body: JSON.stringify(result, null, 2), contentType: 'application/json' });

    // NO-SEMBRADO no revienta la corrida: se reporta y el resumen lo
    // muestra como no verificado. ROTO sí falla — es un hallazgo real.
    if (outcome === 'NO-SEMBRADO') {
      testInfo.annotations.push({ type: 'no-sembrado', description: reason });
      test.skip(true, `NO-SEMBRADO: ${reason}`);
    }
    expect(outcome, `${route.name}: ${reason}`).toBe('OK');
  });
}
