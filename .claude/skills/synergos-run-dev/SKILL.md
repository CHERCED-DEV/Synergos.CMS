---
name: synergos-run-dev
description: Arranca el entorno de desarrollo completo de Synergos — CMS Umbraco (http://synergos.local:5000), CDN local (FileSystem mode), y opcionalmente el dev server Angular para un elemento específico. Verifica prerequisitos (hosts, cert, LOCAL_CDN), detecta si ya está corriendo, y hace health check completo al terminar. Usar antes de invocar synergos-cms-author o synergos-cdn-build.
model: claude-opus-4-8
---

# SYNERGOS Run Dev — arrancar el stack completo de desarrollo

Esta skill levanta y verifica el entorno completo de desarrollo de Synergos en orden. Sin este entorno corriendo, `synergos-cms-author` no puede crear contenido y `synergos-cdn-build` no puede verificar que los bundles se sirven.

## Stack completo

```
┌─────────────────────────────────────────────────────────┐
│  http://synergos.local:5000   → Umbraco CMS (dotnet)    │
│  https://synergos.local:5001  → Umbraco CMS (HTTPS)     │
│  /cdn-bundles/*               → CDN local (FileSystem)   │
│  http://localhost:43XX        → Angular dev server       │
└─────────────────────────────────────────────────────────┘
```

El CDN local es servido por el propio Umbraco desde `C:\LOCAL_CDN\` bajo la ruta `/cdn-bundles/` — no necesita proceso separado. Solo necesita que `BundleRegistry:Mode=FileSystem` y que `C:\LOCAL_CDN\` exista.

---

## 1. Verificar prerequisitos

Ejecutar en PowerShell antes de intentar arrancar:

```powershell
$ok = $true

# 1A. Entrada en hosts
$hostsPath = "C:\Windows\System32\drivers\etc\hosts"
$hostsContent = Get-Content $hostsPath -Raw
if ($hostsContent -notmatch "synergos\.local") {
    Write-Warning "PREREQUISITO FALTANTE: synergos.local no está en el hosts file."
    Write-Warning "Agregar como administrador: Add-Content '$hostsPath' '127.0.0.1 synergos.local'"
    $ok = $false
} else {
    Write-Output "hosts OK — synergos.local encontrado"
}

# 1B. Certificado de desarrollo
$certPath = "C:\LOCAL_CDN\synergos-dev.crt"
$keyPath  = "C:\LOCAL_CDN\synergos-dev.key"
if (-not (Test-Path $certPath) -or -not (Test-Path $keyPath)) {
    Write-Warning "PREREQUISITO FALTANTE: cert dev no encontrado en C:\LOCAL_CDN\"
    Write-Warning "Generar con: dotnet dev-certs https -ep C:\LOCAL_CDN\synergos-dev.pfx -p devpwd"
    Write-Warning "Luego exportar .crt y .key desde el .pfx"
    Write-Warning "Alternativa: usar solo HTTP (puerto 5000) — ajustar Kestrel en appsettings.Development.json"
    # No es bloqueante si solo se usa HTTP
} else {
    Write-Output "Cert dev OK — $certPath"
}

# 1C. Directorio LOCAL_CDN
if (-not (Test-Path "C:\LOCAL_CDN")) {
    Write-Warning "PREREQUISITO FALTANTE: C:\LOCAL_CDN no existe."
    New-Item -ItemType Directory "C:\LOCAL_CDN" | Out-Null
    Write-Output "C:\LOCAL_CDN creado."
}

# 1D. registry.json mínimo
$regPath = "C:\LOCAL_CDN\synergos\registry.json"
if (-not (Test-Path $regPath)) {
    Write-Warning "registry.json no encontrado — creando uno vacío mínimo."
    New-Item -ItemType Directory -Force "C:\LOCAL_CDN\synergos" | Out-Null
    '{"generated":"2026-01-01T00:00:00Z","version":"0.1.0","baseUrl":"/synergos","elements":[]}' |
        Set-Content $regPath -Encoding UTF8
    Write-Output "registry.json vacío creado: $regPath"
} else {
    Write-Output "registry.json OK — $regPath"
}

# 1E. appsettings.Development.json — verificar BundleRegistry:Mode
$settingsPath = "Synergos.CMS\Synergos.CMS.Web\appsettings.Development.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    $mode = $settings.Synergos.BundleRegistry.Mode
    if ($mode -ne "FileSystem") {
        Write-Warning "BundleRegistry:Mode es '$mode' — debería ser 'FileSystem' para dev local."
    } else {
        Write-Output "BundleRegistry:Mode OK — FileSystem"
    }
}

if ($ok) { Write-Output "Todos los prerequisitos satisfechos." }
```

---

## 2. Detectar si el CMS ya está corriendo

```powershell
function Test-CmsRunning {
    try {
        $r = Invoke-WebRequest "http://synergos.local:5000/umbraco/api/keepalive/ping" `
            -UseBasicParsing -TimeoutSec 3
        return $r.StatusCode -eq 200
    } catch { return $false }
}

if (Test-CmsRunning) {
    Write-Output "CMS ya está corriendo en http://synergos.local:5000 — no se necesita arrancar."
} else {
    Write-Output "CMS no está corriendo — iniciando..."
    # Continuar con §3
}
```

---

## 3. Arrancar el CMS

### Opción A — Bash tool con run_in_background (recomendado desde Claude)

Usar el Bash tool con `run_in_background: true`:

```bash
cd /c/Users/HITMA/Desktop/synergos/Synergos.CMS/Synergos.CMS.Web
dotnet run --launch-profile "SynergosLocal"
```

⚠️ El perfil se llama `SynergosLocal` (ver `Properties/launchSettings.json`), NO
"Development". Un nombre de perfil inexistente **no falla ahí**: `dotnet run` sigue,
no setea `ASPNETCORE_ENVIRONMENT`, arranca como **Production** y revienta con
`InvalidOperationException: The factory has not been configured with a proper
connection string` — que parece un problema de DB y es del perfil (el connection
string vive en `appsettings.Development.json`).

Después esperar a que responda (poll):

```powershell
$maxWait = 60  # segundos
$elapsed = 0
Write-Output "Esperando que el CMS arranque..."
while ($elapsed -lt $maxWait) {
    if (Test-CmsRunning) {
        Write-Output "CMS listo en $elapsed segundos."
        break
    }
    Start-Sleep -Seconds 3
    $elapsed += 3
}
if (-not (Test-CmsRunning)) {
    Write-Error "CMS no respondió en $maxWait s. Revisar logs del proceso."
}
```

### Opción B — PowerShell background job

```powershell
$cmsDir = "C:\Users\HITMA\Desktop\synergos\Synergos.CMS\Synergos.CMS.Web"
$cmsJob = Start-Job -Name "SynCMS" -ScriptBlock {
    Set-Location $using:cmsDir
    dotnet run
}
Write-Output "CMS iniciado como job — ID: $($cmsJob.Id)"
```

### Opción C — Instrucción al arquitecto

Si el agente no puede arrancar procesos background, decirle al arquitecto:

```
Abrir una PowerShell como administrador y ejecutar:

cd C:\Users\HITMA\Desktop\synergos\Synergos.CMS\Synergos.CMS.Web
dotnet run

Dejar esa terminal abierta. El CMS estará disponible en:
  http://synergos.local:5000
  https://synergos.local:5001 (si el cert está instalado)

Umbraco backoffice: http://synergos.local:5000/umbraco/
  Usuario: admin@synergos.local
  Password: Synergos2026!
```

---

## 4. Logs de arranque — señales esperadas y errores

### Logs normales (ignorar):
```
warn: Umbraco.Cms.Core.Services.LocalizedTextService[0]
    Could not find localization file
info: UmbracoApplicationStarting in 2.4s
info: BundleRegistry warmup OK: adapter=FileSystemBundleRegistryClient
info: Now listening on: http://synergos.local:5000
```

### Señales de éxito:
- `Now listening on: http://synergos.local:5000` → CMS arrancó
- `BundleRegistry warmup OK: adapter=FileSystemBundleRegistryClient` → CDN local conectado
- `uSync: v14.x.x — imported X items` → schema importado correctamente

### Señales de problema:
| Log | Causa | Solución |
|-----|-------|----------|
| `UNIQUE constraint failed` | GUID duplicado en uSync | Verificar quad-check antes del último import |
| `Address already in use :5000` | Otro proceso usa el puerto | `netstat -ano | findstr :5000` y matar el proceso |
| `Failed to bind to address https://synergos.local:5001` | Cert no encontrado | Usar solo HTTP o corregir rutas cert en appsettings.Development.json |
| `BundleRegistry warmup FAILED` | `C:\LOCAL_CDN` no existe o registry.json corrupto | Verificar §1C y §1D |
| `ModelsBuilder: FlagOutOfDateModels` | Schema cambió sin regenerar models | Normal si solo se leen props untyped — "Running without models" no rompe nada |
| `Database does not exist` | Primer arranque sin SQLite | Esperar — Umbraco instala de forma unattended automáticamente |

---

## 5. Arrancar Angular Dev Server (opcional)

Solo si se está desarrollando un elemento Angular específico:

```powershell
# Desde el workspace NX
$nxRoot = "C:\Users\HITMA\Desktop\synergos\Synergos.UI\platforms\angular"

# Verificar que el elemento existe
$elementName = "{name}"  # ej: accordion, hero, pricing-card
$projectName = "elements-{tier}-$elementName"  # ej: elements-compositions-accordion

# Arrancar dev server (el puerto 43XX está definido en project.json)
Start-Process powershell -ArgumentList "-NoExit", "-Command",
    "cd '$nxRoot'; npx nx serve $projectName"

Write-Output "Angular dev server iniciando para $projectName"
Write-Output "Abrir http://localhost:43XX para ver el componente aislado"
```

**Nota:** El Angular dev server es opcional y solo útil para desarrollar el componente visualmente en aislamiento. El CMS sirve los bundles compilados desde `C:\LOCAL_CDN\` — el dev server NO es la fuente de los bundles para Umbraco.

---

## 6. Health check completo

Ejecutar después de que el CMS esté corriendo:

```powershell
$baseUrl = "http://synergos.local:5000"
$report  = [ordered]@{}

# 6A. Ping CMS
try {
    Invoke-WebRequest "$baseUrl/umbraco/api/keepalive/ping" -UseBasicParsing -TimeoutSec 5 | Out-Null
    $report["CMS Ping"]        = "OK"
} catch { $report["CMS Ping"] = "FAIL — CMS no responde" }

# 6B. Management API token
try {
    $auth = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/security/back-office/token" `
        -Method POST -ContentType "application/x-www-form-urlencoded" `
        -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
    $report["Management API"] = if ($auth.access_token) { "OK — token obtenido" } else { "FAIL — no token" }
    $token = $auth.access_token
} catch { $report["Management API"] = "FAIL — $($_.Exception.Message)" }

# 6C. Bundle registry
try {
    $reg  = Get-Content "C:\LOCAL_CDN\synergos\registry.json" -Raw | ConvertFrom-Json
    $count = @($reg.elements).Count
    $report["Bundle Registry"] = "OK — $count elementos en registry.json"
} catch { $report["Bundle Registry"] = "FAIL — registry.json ilegible" }

# 6D. CDN static files
try {
    if ($count -gt 0) {
        $first = $reg.elements[0]
        $name  = $first.name
        $ver   = $first.implementations.angular.latest
        $url   = "$baseUrl/cdn-bundles/synergos/$name/angular/$ver/main.js"
        $r     = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5
        $report["CDN Static Files"] = "OK — $url ($($r.StatusCode))"
    } else {
        $report["CDN Static Files"] = "SKIP — registry vacío"
    }
} catch { $report["CDN Static Files"] = "FAIL — bundles no accesibles" }

# 6E. Swagger disponible
try {
    Invoke-WebRequest "$baseUrl/umbraco/swagger" -UseBasicParsing -TimeoutSec 5 | Out-Null
    $report["Swagger UI"] = "OK — $baseUrl/umbraco/swagger"
} catch { $report["Swagger UI"] = "WARN — no accesible (no crítico)" }

# Imprimir reporte
Write-Output ""
Write-Output "══════════════════════════════════════"
Write-Output "  SYNERGOS DEV — Health Check"
Write-Output "══════════════════════════════════════"
$report.GetEnumerator() | ForEach-Object { Write-Output "  $($_.Key): $($_.Value)" }
Write-Output "══════════════════════════════════════"
Write-Output "  Backoffice: $baseUrl/umbraco/"
Write-Output "  usuario: admin@synergos.local"
Write-Output "  password: Synergos2026!"
Write-Output "══════════════════════════════════════"
```

---

## 7. Detener el CMS

```powershell
# Si se usó Start-Job:
Get-Job -Name "SynCMS" | Stop-Job | Remove-Job

# Si se inició desde terminal: Ctrl+C en esa terminal.

# Verificar que el puerto quedó libre:
$procs = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
if ($procs) {
    Write-Warning "Puerto 5000 todavía en uso por PID $($procs.OwningProcess)"
    Stop-Process -Id $procs.OwningProcess -Force
}
```

---

## 8. Primer arranque (instalación unattended)

Si es la primera vez en esta máquina:

1. Umbraco detecta que no hay DB y crea `Umbraco.sqlite.db` automáticamente.
2. Crea el usuario admin con las credenciales de `appsettings.Development.json:Unattended`.
3. Puede tardar 30-60 segundos más de lo normal.
4. Una vez arrancado, correr uSync Import para aplicar el schema (ver `synergos-usync-import`).

**Señal de instalación completa:** `Application started. Press Ctrl+C to shut down.` en los logs.

---

## 9. Referencia rápida

| URL | Propósito |
|-----|-----------|
| `http://synergos.local:5000/umbraco/` | Backoffice |
| `http://synergos.local:5000/umbraco/swagger` | Management API docs |
| `http://synergos.local:5000/umbraco/api/keepalive/ping` | Health ping |
| `http://synergos.local:5000/cdn-bundles/synergos/` | CDN bundles estáticos |
| `http://synergos.local:5000/` | Sitio público |
