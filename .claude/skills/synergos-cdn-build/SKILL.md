---
name: synergos-cdn-build
description: Compila un elemento Angular con NX y lo publica a C:\LOCAL_CDN\ para que Umbraco lo sirva como bundle CDN. Actualiza registry.json, crea los pointers latest/ y v0/, calcula SRI sha384, y verifica que el CMS detectó el cambio (hot-reload). Usar después de crear o modificar un componente Angular con synergos-cms-author.
model: claude-opus-4-8
---

# SYNERGOS CDN Build — compilar y publicar bundles Angular a LOCAL_CDN

Esta skill cierra el loop entre `synergos-cms-author` (que crea el componente Angular) y el CMS (que lo sirve como Web Component). Sin publicar a `LOCAL_CDN`, el elemento hidrata con el `StubBundleRegistryClient` → placeholder HTML comment.

## Flujo completo

```
Synergos.UI/platforms/angular/apps/elements/{tier}/{name}/
    │
    ▼  nx build (production)
    │
dist/{name}/browser/main-*.js   ← output Angular
    │
    ▼  copiar + renombrar
    │
C:\LOCAL_CDN\synergos\{name}\angular\{version}\main.js
C:\LOCAL_CDN\synergos\{name}\angular\latest\main.js    (pointer)
C:\LOCAL_CDN\synergos\{name}\angular\v0\main.js        (pointer)
    │
    ▼  actualizar registry.json
    │
C:\LOCAL_CDN\synergos\registry.json   ← FileSystemBundleRegistryClient lo lee (hot-reload ~500ms)
    │
    ▼  CMS detecta cambio (FileSystemWatcher)
    │
http://synergos.local:5000/cdn-bundles/synergos/{name}/angular/latest/main.js  ✓
```

---

## 1. Inputs de la skill

Cuando se activa, el usuario debe indicar (o inferir del contexto):

| Parámetro | Descripción | Ejemplo |
|-----------|-------------|---------|
| `$elementName` | Nombre kebab del elemento (sin prefijo `synergos-`) | `pricing-card` |
| `$tier` | Tier Angular (PLURAL, como las carpetas reales): `compositions` / `modules` / `primitives` | `primitives` |
| `$version` | Versión semver a publicar | `0.1.0` |
| `$alias` | Alias CMS del ElementType | `elementSynPricingCard` |
| `$tag` | Custom element tag completo | `synergos-pricing-card` |

Si no se especificó versión, usar `0.1.0` para nuevos elementos.

---

## 2. Prerequisitos

```powershell
$nxRoot    = "C:\Users\HITMA\Desktop\synergos\Synergos.UI\platforms\angular"
$cdnRoot   = "C:\LOCAL_CDN\synergos"
$regPath   = "$cdnRoot\registry.json"

# 2A. Node.js disponible
if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    Write-Error "Node.js no encontrado. Instalar desde https://nodejs.org/"
    exit 1
}

# 2B. NX disponible en el workspace
if (-not (Test-Path "$nxRoot\node_modules\.bin\nx")) {
    Write-Warning "NX no instalado. Ejecutar: cd '$nxRoot'; npm install"
    # Intentar instalar automáticamente
    Push-Location $nxRoot
    npm install --silent
    Pop-Location
}

# 2C. Proyecto existe
$projectPath = "$nxRoot\apps\elements\$tier\$elementName"
if (-not (Test-Path $projectPath)) {
    Write-Error "Proyecto no encontrado: $projectPath"
    Write-Error "Verificar que synergos-cms-author creó el componente en la ruta correcta."
    exit 1
}

# 2D. LOCAL_CDN existe
if (-not (Test-Path $cdnRoot)) {
    New-Item -ItemType Directory -Force $cdnRoot | Out-Null
    Write-Output "Creado: $cdnRoot"
}

Write-Output "Prerequisitos OK"
```

---

## 3. Compilar el elemento con NX

> **Comando verificado (fase SynergosLabs):** el build de un elemento es
> `npx nx build elements-<tier>-<name> --configuration=production` y se corre
> **desde `platforms/angular`** (`$nxRoot`). El `<tier>` real del workspace es
> uno de `compositions` | `modules` | `primitives` (en plural — no `composition`
> singular). El `<name>` es el nombre del elemento tal como aparece en el campo
> `name` del `project.json` (puede o no llevar guiones; ver más abajo).

```powershell
# El nombre NX del proyecto sigue el patrón "elements-<tier>-<name>".
# tier ∈ { compositions | modules | primitives }  (PLURAL)
# Verificar siempre contra el campo "name" del project.json (fuente de verdad):
$projectJson = Get-Content "$projectPath\project.json" | ConvertFrom-Json
$projectName = $projectJson.name   # ej: "elements-modules-stat-ticker"

Push-Location $nxRoot   # ← OBLIGATORIO correr desde platforms/angular

Write-Output "Compilando $projectName con NX (producción)..."
$buildResult = & npx nx build $projectName --configuration=production 2>&1
$exitCode    = $LASTEXITCODE

Pop-Location

if ($exitCode -ne 0) {
    Write-Error "Build fallido (exit code $exitCode):"
    $buildResult | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Build exitoso."
```

**Si el nombre del proyecto NX difiere:** El `project.json` generado por `synergos-cms-author` define el nombre exacto en el campo `"name"`. Leer ese campo:

```powershell
$projectJson = Get-Content "$projectPath\project.json" | ConvertFrom-Json
$projectName = $projectJson.name
Write-Output "Nombre NX del proyecto: $projectName"
```

---

## 3B. Publicar con `publish-element.mjs` (vía canónica, recomendada)

Hay un publisher oficial que resuelve nombre/framework/dist desde los tags y la
`synergos-config` del proyecto NX y publica a los 3 slots del CDN (semver / major /
latest) con manifest + meta. **Úsalo en lugar del copiado manual** (§4–§7) salvo que
necesites control fino.

```powershell
# DESDE platforms/angular ($nxRoot) — la ruta del script es relativa: ../../tools/
Push-Location $nxRoot
node ..\..\tools\publish-element.mjs --project=$projectName
Pop-Location
```

**Reglas verificadas (fase SynergosLabs) — fáciles de romper:**

- **Correr SIEMPRE desde `platforms/angular`** (`$nxRoot`). El script invoca
  `npx nx show project ...` internamente; si lo lanzas desde la raíz del repo
  (o desde `Synergos.UI/`), ese `nx show project` **falla** y el publish aborta.
  La ruta del script es `../../tools/publish-element.mjs` precisamente porque el
  cwd esperado es `platforms/angular`.
- **NO existe un target NX `:publish`.** No usar `npx nx run <project>:publish`
  ni `nx run ...:publish` — fallará porque el target no está definido. La única
  invocación válida es `node ../../tools/publish-element.mjs --project=<name>`.
- `--project` debe ser el nombre NX completo `elements-<tier>-<name>`
  (ej. `elements-primitives-stat-ticker`), no el nombre kebab del elemento.
- Flags útiles: `--version=0.2.0` (override semver), `--dry-run` (no escribe),
  `--cdn C:\MY_CDN` (override destino). Para bulk: `node ..\..\tools\publish.mjs`
  o `npx nx run-many -t publish`.

> El resto de esta skill (§4–§9, copiado manual + SRI + registry.json) sigue válido
> como camino de bajo nivel / fallback cuando el publisher no aplica.

---

## 4. Localizar el output del build

Angular `@angular/build:application` genera el output en `dist/{outputBaseName}/browser/`:

```powershell
# El outputPath del project.json define la carpeta base
$projectJson = Get-Content "$projectPath\project.json" | ConvertFrom-Json
$outputBase  = $projectJson.targets.build.options.outputPath  # ej: "dist/pricing-card"
$distPath    = Join-Path $nxRoot $outputBase "browser"

if (-not (Test-Path $distPath)) {
    # Fallback: algunos builds van directo a dist sin /browser
    $distPath = Join-Path $nxRoot $outputBase
}

if (-not (Test-Path $distPath)) {
    Write-Error "Output del build no encontrado en: $distPath"
    exit 1
}

# Encontrar el archivo main.js (puede tener hash)
$mainFile = Get-ChildItem $distPath "main*.js" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $mainFile) {
    Write-Error "No se encontró main*.js en: $distPath"
    exit 1
}

Write-Output "Output encontrado: $($mainFile.FullName)"
```

---

## 5. Publicar a LOCAL_CDN

```powershell
$versionedPath = "$cdnRoot\$elementName\angular\$version"
$latestPath    = "$cdnRoot\$elementName\angular\latest"
$v0Path        = "$cdnRoot\$elementName\angular\v0"

# Crear directorios
@($versionedPath, $latestPath, $v0Path) | ForEach-Object {
    if (-not (Test-Path $_)) { New-Item -ItemType Directory -Force $_ | Out-Null }
}

# Copiar el bundle principal como main.js (nombre canónico)
Copy-Item $mainFile.FullName "$versionedPath\main.js" -Force
Copy-Item $mainFile.FullName "$latestPath\main.js"    -Force
Copy-Item $mainFile.FullName "$v0Path\main.js"        -Force

# Copiar otros assets si existen (CSS, fonts, chunks)
Get-ChildItem $distPath -Exclude "*.js" | ForEach-Object {
    Copy-Item $_.FullName "$versionedPath\$($_.Name)" -Force
    Copy-Item $_.FullName "$latestPath\$($_.Name)"    -Force
}

Write-Output "Publicado:"
Write-Output "  $versionedPath\main.js"
Write-Output "  $latestPath\main.js  (latest pointer)"
Write-Output "  $v0Path\main.js      (v0 pointer)"
```

---

## 6. Calcular SRI (sha384)

El `FileSystemBundleRegistryClient` tiene `ComputeIntegrityIfMissing=true` — calcula SRI automáticamente si no hay un `.sri` sidecar. Pero si se quiere explícito:

```powershell
function Get-SriHash {
    param([string]$FilePath)
    $bytes = [System.IO.File]::ReadAllBytes($FilePath)
    $sha   = [System.Security.Cryptography.SHA384]::Create()
    $hash  = $sha.ComputeHash($bytes)
    return "sha384-" + [System.Convert]::ToBase64String($hash)
}

$mainJsPath = "$versionedPath\main.js"
$sri        = Get-SriHash $mainJsPath

# Guardar sidecar .sri (el client lo lee si existe, evita recálculo)
Set-Content "$mainJsPath.sri" $sri -Encoding UTF8
Copy-Item "$mainJsPath.sri" "$latestPath\main.js.sri" -Force

Write-Output "SRI: $sri"
```

---

## 7. Actualizar registry.json

```powershell
# Leer registry existente
$reg = if (Test-Path $regPath) {
    Get-Content $regPath -Raw | ConvertFrom-Json -AsHashtable
} else {
    @{ generated=""; version="0.1.0"; baseUrl="/synergos"; elements=@() }
}

# Buscar si el elemento ya existe
$elements = [System.Collections.ArrayList]$reg.elements
$existing = $elements | Where-Object { $_.name -eq $elementName } | Select-Object -First 1

if ($existing) {
    # Actualizar versión
    $existing.implementations.angular.latest = $version
    $existing.alias = $alias
    $existing.tag   = $tag
    Write-Output "Elemento actualizado en registry.json: $elementName"
} else {
    # Agregar nuevo
    $newEntry = [ordered]@{
        name            = $elementName
        alias           = $alias
        tag             = $tag
        tier            = $tier
        implementations = @{
            angular = @{
                latest = $version
                v0     = $version
            }
        }
    }
    $null = $elements.Add($newEntry)
    $reg.elements = $elements.ToArray()
    Write-Output "Elemento agregado a registry.json: $elementName"
}

# Actualizar timestamp
$reg.generated = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ")

# Guardar (el FileSystemWatcher del CMS detectará el cambio en ~500ms)
$regJson = $reg | ConvertTo-Json -Depth 10 -Compress
[System.IO.File]::WriteAllText($regPath, $regJson, [System.Text.Encoding]::UTF8)
Write-Output "registry.json actualizado: $regPath"
```

---

## 8. Esperar hot-reload del CMS

El `FileSystemBundleRegistryClient` tiene un `FileSystemWatcher` con debounce de 500ms. Esperar y verificar:

```powershell
Write-Output "Esperando hot-reload del CMS (~2 segundos)..."
Start-Sleep -Seconds 2

# Verificar que el bundle se sirve correctamente
$bundleUrl = "http://synergos.local:5000/cdn-bundles/synergos/$elementName/angular/latest/main.js"
try {
    $r = Invoke-WebRequest $bundleUrl -UseBasicParsing -TimeoutSec 10
    $cacheControl = $r.Headers["Cache-Control"]
    $contentLength = $r.Headers["Content-Length"] ?? $r.RawContentLength
    Write-Output "Bundle accesible OK:"
    Write-Output "  URL: $bundleUrl"
    Write-Output "  Status: $($r.StatusCode)"
    Write-Output "  Cache-Control: $cacheControl"
    Write-Output "  Tamaño: $contentLength bytes"
    Write-Output ""
    Write-Output "El tag <synergos-$elementName> ahora hidrata en el CMS."
} catch {
    Write-Warning "Bundle no accesible todavía: $bundleUrl"
    Write-Warning "Verificar que el CMS está corriendo y que la ruta LOCAL_CDN es correcta."
    Write-Warning "Error: $($_.Exception.Message)"
}
```

**Comportamiento esperado de Cache-Control:**
- URL con `/latest/` → `public, no-cache, must-revalidate` (pointer mutable)
- URL con `/0.1.0/` (semver exacto) → `public, max-age=31536000, immutable`

---

## 9. Verificación de integración CMS↔Bundle

Si el CMS está corriendo, verificar que el `ISynHostEmitter` resuelve el bundle:

```powershell
# Autenticar
$auth    = Invoke-RestMethod "http://synergos.local:5000/umbraco/management/api/v1/security/back-office/token" `
    -Method POST -ContentType "application/x-www-form-urlencoded" `
    -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
$headers = @{ "Authorization" = "Bearer $($auth.access_token)" }

# Verificar que el bundle client lo puede resolver (indirecto — via registry lookup)
$regCheck = Get-Content $regPath | ConvertFrom-Json
$entry    = $regCheck.elements | Where-Object { $_.name -eq $elementName }
if ($entry) {
    Write-Output "Registry entry OK: $($entry | ConvertTo-Json -Compress)"
} else {
    Write-Warning "Elemento NO encontrado en registry.json — verificar §7"
}
```

---

## 10. Reporte final

```
══════════════════════════════════════════════════════
  SYNERGOS CDN Build — Completado
══════════════════════════════════════════════════════
  Elemento  : synergos-{kebab}
  Alias CMS : elementSyn{Pascal}
  Tier       : {tier}
  Versión    : {version}

  Archivos publicados:
    C:\LOCAL_CDN\synergos\{name}\angular\{version}\main.js
    C:\LOCAL_CDN\synergos\{name}\angular\latest\main.js
    C:\LOCAL_CDN\synergos\{name}\angular\v0\main.js

  URL en el CMS:
    http://synergos.local:5000/cdn-bundles/synergos/{name}/angular/latest/main.js

  SRI: sha384-...

  El Web Component <synergos-{name}> ya hidrata en el browser.
══════════════════════════════════════════════════════
```

---

## 10B. GOTCHA CRÍTICO — el runtime CDN debe bundlear los secondary entry points de @angular/core

> **Síntoma:** publicas el bundle, el registry lo lista, el CMS lo sirve 200 OK…
> y **NINGÚN** custom element hidrata (todos quedan como HTML comment / placeholder).
> No es un problema del elemento ni del registry — es del **runtime compartido** de Angular.

El runtime compartido (`@angular/core` y amigos, servido aparte de cada elemento) debe
incluir explícitamente los **secondary entry points** de `@angular/core`. Si faltan, la
hidratación de Web Components revienta silenciosamente para **todos** los elementos:

- `@angular/core/rxjs-interop`
- `@angular/core/primitives/di`
- `@angular/core/primitives/signals`
- `@angular/core/primitives/event-dispatch`

**Dónde:** la lista de entry points está **DUPLICADA** en dos archivos y hay que
mantener AMBOS sincronizados o el dev funciona y el publish no (o viceversa):

- `Synergos.UI/tools/build-runtime.mjs`
- `Synergos.UI/tools/publish-runtime.mjs`

Además, el navegador necesita el **import map** para resolver esos specifiers. Lo emite
`Synergos.UI/.../Views/Partials/_SynHostRuntime.cshtml`, que lee el `import-map.json`
publicado por el CDN. Si el import map no se emite, los specifiers `@angular/core/*`
no resuelven y, otra vez, nada hidrata.

**Checklist cuando "nada hidrata pero el bundle carga 200":**
1. ¿`build-runtime.mjs` Y `publish-runtime.mjs` listan los 4 secondary entry points? (ambos)
2. ¿`_SynHostRuntime.cshtml` está emitiendo el import map desde `import-map.json` del CDN?
3. Sólo entonces sospechar del elemento individual (SynHostEmitter / registry / BlockAlias).

---

## 10C. GOTCHA CRÍTICO — cambiar `libs/shared` OBLIGA a republicar el RUNTIME (no basta publish-element)

> **Síntoma:** publicás el bundle de la app (build + publish-element), el registry lo lista, el
> CMS lo sirve 200 OK… y el custom element **NO hidrata**: `customElements.get('synergos-<x>')`
> = `false`, el mount queda con `innerHTML` vacío. En consola, uno de estos dos:
> - `SyntaxError: The requested module '@synergos/shared' does not provide an export named 'X'`
> - `TypeError: Failed to fetch dynamically imported module` (al forzar el import a mano)
>
> No es el elemento, ni el registry, ni el SynHostEmitter. Es el **runtime compartido STALE**.

`@synergos/shared` **NO se bundlea-as-source** dentro de cada app: es un módulo runtime
**EXTERNALIZADO** (`sg-shared.js`) que la página carga UNA vez vía import map, compartido por
todos los bundles. Por eso `publish-element` (que sólo re-empaqueta el bundle de la app)
**NO re-bundlea `sg-shared.js`**. Si tocaste `platforms/angular/libs/shared/` (un componente
como `TabsComponent`, un export nuevo como `SegmentedComponent`, mover `SynSkeleton`/`EmptyState`
de `shells`→`shared`, un mixin…), el bundle de la app importa contra un runtime viejo que no
tiene el símbolo → revienta la hidratación **para todos** los elementos que dependan de él.

> Contraste: `@synergos/shells` SÍ es bundled-as-source → ahí publicar solo la app basta.
> El corte es exactamente `shared` (externalizado) vs `shells` (inlined).

**Regla — al tocar `libs/shared` (o al cerrar un lote de republish de apps), en ESTE orden:**

1. `npx nx run shared:build --skip-nx-cache` — reconstruye el dist de la LIB
   (`dist/libs/shared/fesm2022/synergos-shared.mjs`). `build-runtime` lee ese dist
   **pre-compilado**, no el source vivo; si no lo reconstruís, el runtime sale con código viejo.
2. `npm run build:runtime` (`node tools/build-runtime.mjs`) — re-bundlea `sg-shared.js` desde
   el dist fresco + reescribe `import-map.json` con SRI nuevo.
3. `npm run publish:runtime` (`node tools/publish-runtime.mjs`) — copia el runtime al CDN.
4. Republicar los bundles de app que cambiaron (`publish-element`) + **Ctrl+Shift+R** (hard reload).

> Atajo: `npm run release:angular` hace el ciclo completo y consistente (lib → runtime → apps).
> **Lección operativa:** SIEMPRE cerrar un lote de republish de apps con `build:runtime` +
> `publish:runtime` — `publish-element` solo NO alcanza.

**Por qué hace falta Ctrl+Shift+R (no F5):** el `sg-shared.js` se sirve
`Cache-Control: public, max-age=31536000, immutable` y su URL está versionada **solo por la
versión de Angular** (ej. `.../runtime/angular/21.1.6/sg-shared.js`), no por su contenido. Un
F5 normal NO revalida el runtime immutable; el navegador reusa el `sg-shared.js` viejo aunque
el CDN ya tenga el nuevo. Hace falta **Ctrl+Shift+R** (o cache-bust).

**Verificación (dos niveles):**
```powershell
# 1) El CDN ya tiene el símbolo nuevo (bash del agente):
#    grep -c "NuevoSymbol" /c/LOCAL_CDN/synergos/runtime/angular/<ver>/sg-shared.js   → debe dar > 0
# 2) El navegador NO está sirviendo cache stale (consola del sitio):
#    fetch(url).then(r=>r.text()).then(t=>t.length)                    // cacheado (viejo)
#    fetch(url,{cache:'reload'}).then(r=>r.text()).then(t=>t.length)   // fresco
#    Si difieren en longitud/exports → es cache stale, NO un bug. Ctrl+Shift+R.
# 3) Tras hard reload:  customElements.get('synergos-<x>')  → true
```

**Checklist cuando "el bundle de la app carga 200 pero NO hidrata":**
1. ¿Tocaste algo bajo `libs/shared/` (o moviste un componente hacia `shared`) desde el último `publish-runtime`? → corré los 4 pasos de arriba.
2. ¿`grep` del símbolo nuevo en el `sg-shared.js` del CDN da > 0? Si da 0, no republicaste el runtime.
3. ¿Hiciste **Ctrl+Shift+R**? Compará `fetch(url)` vs `fetch(url,{cache:'reload'})` — si difieren, es cache stale.
4. Sólo si el runtime está fresco Y sin cache stale, recién ahí mirá §10B (import map / secondary entry points) o el elemento individual.

---

## 11. Troubleshooting

| Problema | Causa | Solución |
|----------|-------|----------|
| `nx build` falla con `Cannot find module '@angular/core'` | `npm install` no ejecutado | `cd Synergos.UI\platforms\angular; npm install` |
| `nx build` falla con `Project not found` | Nombre de proyecto incorrecto | Leer `name` del `project.json`: `(Get-Content project.json \| ConvertFrom-Json).name` |
| `publish-element.mjs` falla en `npx nx show project` | Se corrió desde la raíz del repo, no desde `platforms/angular` | Hacer `Push-Location $nxRoot` y correr `node ..\..\tools\publish-element.mjs --project=<name>` |
| `nx run <project>:publish` → target no existe | NO hay target NX `:publish` | Usar `node ..\..\tools\publish-element.mjs --project=<name>` |
| Bundle 200 OK pero **NINGÚN** elemento hidrata | Runtime sin los secondary entry points de `@angular/core`, o falta el import map | Ver §10B — sincronizar `build-runtime.mjs` + `publish-runtime.mjs` y verificar `_SynHostRuntime.cshtml` |
| `main*.js` no encontrado en dist | Build configurado sin `browser/` subfolder | Buscar también en `dist/{name}/` sin el `/browser/` |
| Bundle 404 después de publicar | Ruta `cdnRoot` incorrecta en appsettings | Verificar `Synergos:LocalCdn:LocalPath` = `C:\LOCAL_CDN` |
| Cache-Control dice `immutable` en `/latest/` | CMS caché configurado incorrectamente | Bug en `OnPrepareResponse` — verificar que `/latest/` → `no-cache` en `Program.cs` |
| Elemento no hidrata (placeholder visible) | SynHostEmitter retorna null | Verificar que `registry.json` tiene el `name` correcto (debe coincidir con `BlockAlias` en el Razor) |
| Hot-reload no detecta el cambio | FileSystemWatcher debounce | Esperar 2-3 segundos o hacer un request al bundle URL para forzar recarga |
| `Budget exceeded` en NX build | Bundle > 200kb | Optimizar el componente o ajustar budgets en `project.json` |

---

## 12. Publicar múltiples elementos (batch)

Si `synergos-cms-author` creó varios componentes en una misma sesión:

```powershell
$elements = @(
    @{ name="pricing-card"; tier="composition"; alias="elementSynPricingCard"; tag="synergos-pricing-card" }
    @{ name="feature-grid"; tier="module";      alias="elementSynFeatureGrid";  tag="synergos-feature-grid" }
)

foreach ($el in $elements) {
    Write-Output "Publicando $($el.name)..."
    # Ejecutar §3-§8 para cada elemento
    # (parametrizar $elementName, $tier, $alias, $tag)
}
```
