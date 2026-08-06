# verify-ui — harness de la Fase 4

Automatiza el recorrido de los templates en Chrome que pide la
[Fase 4 del plan pre-producción](../../Synergos.CMS.Web/docs/operations/pre-production-verification-plan.md):
por cada ruta registra status HTTP, errores de consola, requests fallidos,
landmarks semánticos, `<head>` SEO y un screenshot.

Vive bajo `tools/` y **no toca los proyectos .NET**: tiene su propio
`package.json` y no entra en `Synergos.CMS.sln`.

## Uso

```powershell
cd tools\verify-ui
npm ci
npx playwright install chromium      # una sola vez por máquina

# El server dev tiene que estar corriendo
$env:SYNERGOS_BASE_URL = 'https://synergos.local:5001'
npm run verify
npm run report > VEREDICTO-ui.md
```

| Variable | Para qué |
|---|---|
| `SYNERGOS_BASE_URL` | Entorno objetivo. Default `https://synergos.local:5001` |
| `SYNERGOS_CHROMIUM_PATH` | Binario de Chromium ya presente, para saltear la descarga (CI, imágenes con browsers horneados). Opcional |

El cert self-signed del dev server está contemplado
(`ignoreHTTPSErrors`), no hace falta hacérselo confiar a nada.

## Las tres cubetas

El harness **no** clasifica en pasa/falla sino en tres, porque la
distinción es justamente lo que hace concluyente al informe:

| | Significa |
|---|---|
| ✅ **OK** | Responde, sin errores de consola ni requests fallidos, con los landmarks esperados |
| ❌ **ROTO** | Hallazgo real: status inesperado, error de consola, request fallido o landmark faltante |
| ⚪ **NO-SEMBRADO** | No se pudo verificar — 404, o Umbraco sirvió "No published content". **No cuenta como verde** |

La tercera existe por algo concreto y verificado: con la base sin
contenido, **Umbraco devuelve HTTP 200 en todas las rutas** con su página
"No published content". Sin detectar ese caso, el informe acusaría 15
templates rotos por falta de `<main>` cuando lo que falta es el contenido
de la Fase 3. El diagnóstico equivocado es peor que no tener informe.

`summarize.mjs` sale con exit code 1 sólo si hay algún **ROTO**, así que
sirve de gate igual que los otros `tools/*.mjs` del repo.

## Ajustar las rutas

`routes.json` trae slugs *tentativos*. Los reales dependen de lo que
siembre la Fase 3 — hay que ajustarlos antes de correr, o todo va a caer
en NO-SEMBRADO. Campos por ruta:

| Campo | Qué hace |
|---|---|
| `landmarks` | Selectores que deben existir (`main`, `nav`, `aside`) |
| `seo` | Si registra title/canonical/og:image/robots |
| `critical` | Ruta que no depende de contenido sembrado y siempre debe responder |
| `expectStatus` | Status esperado distinto de 2xx (ej. 404 para la página de error) |
| `expectAnyStatus` | Cualquier status cuenta como alcanzable |
| `endpoint` | No es HTML: sólo se evalúa el status, sin consola ni landmarks ni SEO |

## Salidas

```
results/{Template}.json     un registro por ruta
results/screens/*.png       screenshot full-page
.pw-report/                 reporte HTML de Playwright
.pw-artifacts/              traces de los fallos
```

Todo eso está gitignoreado — la evidencia de una corrida va a
`C:\Users\HITMA\Desktop\synergos-backups\verify-{ts}\`, fuera del repo,
según la memoria `feedback_backups_external_to_repo`.

## Estado

Smoke-tested contra el contenedor Docker con base vacía: las 17 rutas
clasificaron correctamente (2 OK, 15 NO-SEMBRADO, 0 falsos ROTO).
**Todavía no se corrió contra un entorno con schema y contenido** — eso es
justamente la Fase 4.
