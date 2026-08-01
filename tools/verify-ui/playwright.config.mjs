import { defineConfig, devices } from '@playwright/test';

// El server dev usa un cert self-signed de C:\LOCAL_CDN, así que
// ignoreHTTPSErrors es obligatorio — si no, cada goto falla antes de
// llegar a evaluar nada.
//
// SYNERGOS_BASE_URL permite apuntar el harness a otro entorno sin tocar
// código: dev local, el contenedor Docker, o mañana staging.
const baseURL = process.env.SYNERGOS_BASE_URL ?? 'https://synergos.local:5001';

// Por default Playwright usa el Chromium que baja `npx playwright install`.
// SYNERGOS_CHROMIUM_PATH permite apuntar a un binario ya presente —CI con
// browsers precargados, o una imagen que los trae horneados— y evitar la
// descarga. Si no está seteada, comportamiento estándar.
const executablePath = process.env.SYNERGOS_CHROMIUM_PATH || undefined;

export default defineConfig({
  testDir: '.',
  testMatch: 'verify.spec.mjs',
  outputDir: './.pw-artifacts',

  // Sin retries a propósito: un test que sólo pasa al segundo intento NO
  // es un resultado concluyente, y esto es un harness de veredicto.
  retries: 0,

  // Un solo worker — el published cache de Umbraco se calienta por ruta y
  // la concurrencia distorsiona tanto los tiempos como los logs de consola.
  workers: 1,

  reporter: [['list'], ['html', { outputFolder: './.pw-report', open: 'never' }]],

  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    screenshot: 'off',   // las tomamos nosotros, con nombre por template
    video: 'off',
    trace: 'retain-on-failure',
    ...devices['Desktop Chrome'],
    launchOptions: { executablePath },
  },
});
