# Plan de verificación pre-producción — con rollback

- **Status:** Plan. No ejecutado. Redactado el 2026-08-01.
- **Dónde se ejecuta:** máquina local del arquitecto (Windows, PowerShell).
  **No** en el contenedor remoto: hace falta compilar, levantar el server y
  manejar Chrome contra él.
- **Objetivo:** llegar a un veredicto **go / no-go** documentado sobre si
  Synergos.CMS está listo para salir a un entorno público, con evidencia
  reproducible y rollback en cada paso.
- **Relacionado:** [`../hardening/backup-and-recovery.md`](../hardening/backup-and-recovery.md)
  (inventario de persistencia y receta de backup — este plan lo usa, no lo
  duplica), [`run-build-test.md`](run-build-test.md),
  [`hosting-and-deployment.md`](hosting-and-deployment.md).

## Regla de oro

**Ninguna fase avanza si la anterior no cerró con su criterio en verde.**
Un criterio ambiguo cuenta como rojo. Si algo falla, se documenta, se hace
rollback de esa fase y se decide antes de seguir — no se sigue "a ver si
más adelante se arregla".

## Rutas de referencia

| Qué | Dónde |
|---|---|
| Repo | `C:\Users\HITMA\Desktop\synergos\Synergos.CMS\` |
| DB SQLite | `Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db` (+ `-wal`, `-shm`) |
| Backups | `C:\Users\HITMA\Desktop\synergos-backups\` |
| Evidencia de esta corrida | `C:\Users\HITMA\Desktop\synergos-backups\verify-{ts}\` |
| Server dev | `https://synergos.local:5001` |

---

## 1. Registro de riesgos

Ordenado por probabilidad × daño. El #1 es el que se pasa por alto.

| # | Riesgo | Por qué pasa | Mitigación |
|---|---|---|---|
| **1** | **La verificación muta el source of truth** | uSync corre con los defaults del paquete → **`ExportOnSave=All`**. Cualquier guardado en el backoffice **reescribe el XML de `uSync/v9/`**. Una sesión de "tocar todo" puede modificar el schema versionado en silencio. | `git status Synergos.CMS.Web/uSync/v9` antes y después de **cada** fase que toque el backoffice. Es rollback y señal a la vez. |
| 2 | Import destructivo borra DocTypes | uSync puede eliminar lo que no está en los archivos | Report (read-only) **antes** de Import. Backup de DB previo. |
| 3 | Backup de SQLite inconsistente | WAL mode: copiar sólo el `.db` deja writes pendientes afuera | Checkpoint antes de copiar, o copiar los 3 archivos. Ver hardening doc. |
| 4 | Endpoints destructivos de `/dev` | `POST /dev/clear-all-content` y `POST /dev/delete-page` existen y borran | **Prohibidos** durante la verificación salvo reset deliberado. |
| 5 | Modelos stale | `SourceCodeAuto` regenera `.cs` que sólo toma el *siguiente* build | Borrar `umbraco/models/*.generated.cs` y rebuild ante cualquier rareza de tipos. |
| 6 | Falso verde por caché del browser | Assets con `?v=hash` van con `max-age=1y, immutable` | Chrome con perfil limpio y cache deshabilitado. |

---

## 2. Fase 0 — Punto de restauración

**Objetivo:** poder volver al estado exacto previo, sin excepciones.

**Precondición:** server **apagado**. Árbol de git limpio (o cambios
commiteados/stasheados a conciencia).

```powershell
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\HITMA\Desktop\synergos\Synergos.CMS'
$ts   = Get-Date -Format 'yyyyMMdd-HHmm'
$ev   = "C:\Users\HITMA\Desktop\synergos-backups\verify-$ts"
New-Item -ItemType Directory -Force -Path $ev | Out-Null
Set-Location $repo

# 1. Estado de git — la línea base a la que se vuelve
git rev-parse HEAD           | Out-File "$ev\git-head.txt"
git status --porcelain       | Out-File "$ev\git-status-before.txt"

# 2. DB: checkpoint del WAL y copia de los 3 archivos
$db = "$repo\Synergos.CMS.Web\umbraco\Data"
sqlite3 "$db\Umbraco.sqlite.db" "PRAGMA wal_checkpoint(TRUNCATE);"
Copy-Item "$db\Umbraco.sqlite.db*" -Destination $ev

# 3. Estado con persistencia que NO está en git
Copy-Item "$repo\Synergos.CMS.Web\App_Data"      "$ev\App_Data"  -Recurse -EA SilentlyContinue
Copy-Item "$repo\Synergos.CMS.Web\wwwroot\media" "$ev\media"     -Recurse -EA SilentlyContinue
```

**Criterio concluyente:** existe `$ev` con `git-head.txt`, los 3 archivos
de SQLite y las copias de `App_Data` y `media`. Si `git-status-before.txt`
no está vacío, **se anota qué había sucio y por qué** antes de seguir.

### Procedimiento de aborto total

Válido en cualquier momento del plan:

```powershell
# 1. Parar el server (Ctrl+C, o matar el proceso)
Get-Process -Name 'Synergos.CMS.Web' -EA SilentlyContinue | Stop-Process -Force

# 2. Restaurar la DB
Remove-Item "$db\Umbraco.sqlite.db*" -Force
Copy-Item "$ev\Umbraco.sqlite.db*" -Destination $db

# 3. Restaurar estado no versionado
Remove-Item "$repo\Synergos.CMS.Web\App_Data" -Recurse -Force -EA SilentlyContinue
Copy-Item "$ev\App_Data" "$repo\Synergos.CMS.Web\App_Data" -Recurse -EA SilentlyContinue

# 4. CRÍTICO — deshacer lo que ExportOnSave haya reescrito
git checkout -- Synergos.CMS.Web/uSync/v9
git clean -fd Synergos.CMS.Web/uSync/v9

# 5. Forzar regeneración de modelos
Remove-Item "$repo\Synergos.CMS.Web\umbraco\models\*.generated.cs" -Force -EA SilentlyContinue
```

---

## 3. Fase 1 — Verificación estática (sin servidor)

**Objetivo:** que el código y el schema estén sanos antes de gastar tiempo
en un browser.

**Rollback:** ninguno necesario — esta fase no muta nada.

```powershell
dotnet build Synergos.CMS.sln                      2>&1 | Tee-Object "$ev\build.txt"
dotnet test  Synergos.CMS.sln                      2>&1 | Tee-Object "$ev\test.txt"
node tools/usync-audit.mjs                         2>&1 | Tee-Object "$ev\usync-audit.txt"
node tools/check-css-parity.mjs                    2>&1 | Tee-Object "$ev\css-parity.txt"
Push-Location Synergos.CMS.Web\docs\contracts\tests
npm ci; npm test                                   2>&1 | Tee-Object "$ev\contracts.txt"
Pop-Location
```

**Criterios concluyentes** — los cinco, sin margen:

| Check | Verde es |
|---|---|
| `dotnet build` | 0 errores, exactamente 1 warning (`NU1902`). Cualquier otro warning es un hallazgo. |
| `dotnet test` | **976 passing**, 0 failing |
| `usync-audit.mjs` | exit code **0** (los 8 checks limpios) |
| `check-css-parity.mjs` | exit code 0 |
| contract tests | exit code 0 |

Si el gate cross-repo del UI aplica (`../Synergos.UI` clonado como
hermano), sumar `npm run cms:validate` y `sync-tokens.mjs --check`.

---

## 4. Fase 2 — Arranque limpio + uSync Import ⚠️

**La fase crítica.** Es la premisa de la que cuelga todo el pipeline de
entornos: si el schema no se aplica sobre una base vacía, no hay releases
posibles.

**Precondición:** Fase 0 y Fase 1 en verde.

### 2.a — Base pristina

```powershell
Remove-Item "$db\Umbraco.sqlite.db*" -Force     # el backup ya está en $ev
dotnet run --project Synergos.CMS.Web
```

**Criterio:** el server arranca, hace el install desatendido y responde en
`https://synergos.local:5001`. Se anota el tiempo hasta la primera
respuesta.

### 2.b — Report (no destructivo, primero)

Desde el backoffice, en la sección de uSync, ejecutar la acción de
**reporte** — la que *analiza sin aplicar*. Guardar la salida en
`$ev\usync-report.txt`.

**Criterio:** el reporte lista los cambios que aplicaría y **no reporta
borrados inesperados**. Si anuncia eliminaciones de DocTypes existentes,
**se detiene el plan** y se investiga antes de importar.

### 2.c — Import

Recién con el reporte revisado, ejecutar la acción de **importar**.

**Criterios concluyentes:**

| Qué | Verde es |
|---|---|
| ContentTypes en el backoffice | **243** |
| DataTypes | **109** |
| Dictionary keys | **443** |
| Idiomas | es-CO (default) + en-US |
| `node tools/usync-audit.mjs` post-import | exit 0 |
| `git status Synergos.CMS.Web/uSync/v9` | **limpio** |

La última fila es la que más informa: un import puro **no debería** dejar
el árbol sucio. Si lo deja, `ExportOnSave` reescribió algo y eso es un
hallazgo real — capturar el `git diff` completo en `$ev\usync-drift.diff`.

**Rollback de la fase:** aborto total (§2).

---

## 5. Fase 3 — Contenido de prueba

Tras el import hay schema pero **no hay contenido**, así que el sitio no se
puede verificar todavía. `DevController` (`/dev`, gated por
`Synergos:DevSeed:Enabled=true`) provee los endpoints para generarlo.

**Prohibido en esta fase:** `POST /dev/clear-all-content` y
`POST /dev/delete-page`.

Orden sugerido, verificando entre cada uno:

```
POST /dev/ping                     → confirma que el gate está abierto
POST /dev/seed-synergos-identity
POST /dev/seed-test-site
POST /dev/fill-synergos-pages
POST /dev/seed-test-form
POST /dev/seed-product-reviews
POST /dev/seed-paid-order
POST /dev/seed-member-roles
```

**Criterio concluyente:** cada endpoint devuelve 2xx y el árbol de
contenido crece de forma observable. Un 404 significa que el flag no está
activo; un 500 es un hallazgo que se documenta y detiene la fase.

**Rollback:** restaurar la DB desde `$ev` (§2).

---

## 6. Fase 4 — Verificación funcional en Chrome

**Objetivo:** que cada template renderice y cada superficie interactiva
responda. Chrome con **perfil limpio y caché deshabilitada** (riesgo #6).

### Cobertura mínima — los 14 templates

`SiteRoot`, `PlatformRoot`, `PageBase`, `PageBasic`, `PageBare`,
`PageLanding`, `PostPage`, `PostCategoryPage`, `AuthorPage`, `ProductPage`,
`ProductCategoryPage`, `SearchPage`, `FlowDefinition`, `FlowStep`, más
`Error`.

Por cada uno se registra:

| Dato | Cómo |
|---|---|
| Status HTTP | Network tab |
| Errores de consola | Console — **cero** es el criterio |
| Requests fallidos | Network, filtro por status ≥ 400 |
| Landmarks semánticos | Que existan `nav`/`main`/`aside` (Layout Composer, ADR 0017) |
| `<head>` SEO | title, canonical, og:image (Ola 44.2) |
| Screenshot | A `$ev\screens\{template}.png` |

### Superficies interactivas

Búsqueda, carrito (agregar/quitar), envío de formulario con honeypot,
login/registro de member, comentarios, y los `<synergos-*>` — que sin CDN
montada deben emitir el **placeholder HTML comment**, no romper.

### Ejecución — harness automatizado

```powershell
cd tools\verify-ui
npm ci; npx playwright install chromium     # una vez por máquina
$env:SYNERGOS_BASE_URL = 'https://synergos.local:5001'
npm run verify
npm run report > "$ev\VEREDICTO-ui.md"
```

Ajustar antes los slugs de `routes.json` a los que haya sembrado la Fase 3.
El harness clasifica en **tres** cubetas —OK / ROTO / NO-SEMBRADO— y sale
con exit 1 sólo si hay ROTO, igual que los demás gates del repo. Detalle en
su [README](../../../tools/verify-ui/README.md).

⚠️ Verificado empíricamente: con la base sin contenido **Umbraco responde
HTTP 200 en todas las rutas** con su página "No published content". El
harness la detecta y la marca NO-SEMBRADO; sin eso, el informe acusaría 15
templates rotos por falta de `<main>` cuando lo que falta es contenido.

**Criterio concluyente:** 15/15 templates con status 2xx y **cero errores
de consola**. Cualquier template que no se pueda alcanzar por falta de
contenido se marca **explícitamente como no verificado** — no se cuenta
como verde.

**Rollback:** ninguno si sólo se navega. Si se editó contenido, restaurar
DB y revisar `git status` de `uSync/v9`.

---

## 7. Fase 5 — Backoffice y Layout Composer

Es el feature más maduro (CLAUDE.md §8) y el que más superficie tiene.

Verificar, describiendo intención y no rutas de UI: que los 14 presets de
layout aparezcan con sus thumbnails, que se pueda dropear un preset en el
root de `sections` y contenido dentro de sus áreas, que los defaults JS
pre-drop se apliquen, que los snippets reutilizables resuelvan, y que
guardar y publicar funcione.

⚠️ **Acá es donde el riesgo #1 se materializa**: cada guardado reescribe
`uSync/v9`. Correr `git status Synergos.CMS.Web/uSync/v9` **después de cada
guardado** y capturar el diff.

**Criterio concluyente:** los 14 presets renderizan en el editor y en SSR,
y **todo cambio en `uSync/v9` está explicado** — o es un cambio esperado
del schema, o es drift y es un hallazgo.

**Rollback:** `git checkout -- Synergos.CMS.Web/uSync/v9` + restaurar DB.

---

## 8. Fase 6 — Cierre

Se produce un informe en `$ev\VEREDICTO.md` con:

1. Tabla de fases con verde/rojo y evidencia enlazada.
2. Lista de hallazgos, cada uno con severidad y si bloquea producción.
3. Diff acumulado de `uSync/v9` (debería ser vacío; si no, explicado).
4. **Veredicto go / no-go** con justificación.
5. Estado final del árbol de git y confirmación de que se volvió a la
   línea base — o de qué se decidió conservar.

---

## 9. Decisiones tomadas

Las tres quedaron resueltas antes de ejecutar:

1. **El uSync Import lo corre el arquitecto.** Se mantiene CLAUDE.md §7:
   el agente no toca la DB. El agente prepara la base pristina, ejecuta el
   Report, avisa, y verifica conteos y drift **antes y después** del clic.
   Las fases 2.b y 2.c son del arquitecto.
2. **Chrome se automatiza con Playwright.** Harness en
   [`tools/verify-ui/`](../../../tools/verify-ui/README.md), con su propio
   `package.json`, fuera de `Synergos.CMS.sln`. Ya está construido y
   smoke-tested; baja la Fase 4 de ~90 a ~30 min y queda como regresión
   reutilizable.
3. **Se arranca de una DB pristina.** Es lo único que prueba el import de
   verdad. El backup de la Fase 0 permite volver al estado actual.

## 10. Estimación

| Fase | Tiempo |
|---|---|
| 0 — Punto de restauración | 10 min |
| 1 — Verificación estática | 15–20 min |
| 2 — Arranque + import ⚠️ | 30–45 min |
| 3 — Contenido de prueba | 20 min |
| 4 — Chrome, 15 templates | 30 min (harness automatizado) |
| 5 — Backoffice | 45–60 min |
| 6 — Cierre e informe | 30 min |
| **Total** | **3,5–5 horas** |

Realista para una sesión larga. Los cortes naturales son al final de la
Fase 2 y de la Fase 4 — ahí se puede parar y retomar sin perder estado,
siempre que el punto de restauración siga intacto.
