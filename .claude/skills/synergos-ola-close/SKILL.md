---
name: synergos-ola-close
description: Cierra una Ola de desarrollo de Synergos siguiendo el flujo estándar de 20 pasos — verifica entregables, hace backup, corre health check, commitea uSync XMLs + Razor + Angular con el mensaje canónico, actualiza §11.x en los docs de arquitectura, y genera el resumen de cierre. Invocar al terminar todos los trabajos de una Ola antes de comenzar la siguiente.
model: claude-opus-4-8
---

# SYNERGOS Ola Close — cierre estándar de una Ola

Una Ola (wave/sprint) en Synergos termina solo cuando todos sus entregables están committeados, documentados, y el schema está sincronizado entre uSync XMLs y el DB. Este procedimiento es el que describe `feedback_ola_execution_flow.md` (20 pasos del flujo B).

---

## 0. Inputs necesarios

| Campo | Descripción | Ejemplo |
|-------|-------------|---------|
| `$olaNum` | Número de la Ola (formato `OLA-XXX`) | `OLA-310` |
| `$olaTitle` | Título corto de lo que se hizo | `ElementType synPricingCard + bundle Angular` |
| `$adrsCreated` | Lista de ADRs creados/cerrados | `ADR-0093, ADR-0094` |
| `$deliverables` | Lista de entregables concretos | Ver §1 |

Si el usuario no los proporcionó, inferirlos del contexto de la conversación / git status.

---

## 1. Verificar entregables de la Ola

Antes de cerrar, confirmar que cada entregable está completo:

```powershell
$olaNum = "OLA-310"  # reemplazar

Write-Output "Verificando entregables de $olaNum..."

# 1A. uSync XMLs escritos y presentes
$uSyncRoot = "Synergos.CMS\Synergos.CMS.Web\uSync\v9"
$newXmls   = git diff --name-only HEAD -- "$uSyncRoot/**/*.config" 2>$null
if ($newXmls) {
    Write-Output "XMLs modificados:"
    $newXmls | ForEach-Object { Write-Output "  + $_" }
} else {
    $newXmls = git status --porcelain -- "$uSyncRoot" 2>$null | Where-Object { $_ -match '^\?\?' }
    if ($newXmls) {
        Write-Output "XMLs nuevos (untracked):"
        $newXmls | ForEach-Object { Write-Output "  + $($_.Substring(3))" }
    } else {
        Write-Output "  (sin XMLs nuevos en esta Ola)"
    }
}

# 1B. Razor views
$razorNew = git status --porcelain -- "Synergos.CMS\Synergos.CMS.Web\Views\Partials\SynHost" 2>$null
$razorNew += git diff --name-only HEAD -- "**/*.cshtml" 2>$null
if ($razorNew) {
    Write-Output "Razor views:"
    $razorNew | Where-Object { $_ } | ForEach-Object { Write-Output "  + $_" }
}

# 1C. Angular components
$angularNew = git status --porcelain -- "Synergos.UI\platforms\angular\apps\elements" 2>$null
if ($angularNew) {
    Write-Output "Angular components:"
    $angularNew | Where-Object { $_ } | ForEach-Object { Write-Output "  + $($_.Substring(3))" }
}

# 1D. LOCAL_CDN actualizado
$regPath = "C:\LOCAL_CDN\synergos\registry.json"
if (Test-Path $regPath) {
    $reg = Get-Content $regPath | ConvertFrom-Json
    Write-Output "Bundle registry: $(@($reg.elements).Count) elementos — generado $($reg.generated)"
}

# 1E. Tests
$testNew = git status --porcelain -- "Synergos.CMS\Synergos.CMS.Tests" 2>$null
if ($testNew) {
    Write-Output "Tests:"
    $testNew | Where-Object { $_ } | ForEach-Object { Write-Output "  + $($_.Substring(3))" }
}
```

**Si un entregable esperado no está:** no cerrar la Ola — completarlo primero.

---

## 2. Correr tests antes de commitear

```powershell
$testProject = "Synergos.CMS\Synergos.CMS.Tests\Synergos.CMS.Tests.csproj"
if (Test-Path $testProject) {
    Write-Output "Ejecutando suite de tests..."
    $testResult = dotnet test $testProject --no-build --logger "console;verbosity=normal" 2>&1
    $passed = ($testResult | Select-String "passed").Count -gt 0
    $failed = ($testResult | Select-String "Failed:" | Where-Object { $_ -match "Failed: [^0]" }).Count -gt 0

    if ($failed) {
        Write-Error "Tests fallidos — no cerrar la Ola hasta resolver."
        $testResult | Select-String "FAIL" | ForEach-Object { Write-Error "  $_" }
        exit 1
    } else {
        Write-Output "Tests OK — todos pasando."
    }
}
```

---

## 3. Backup pre-cierre

```powershell
# Backup del DB antes de commitear (por si acaso)
$dbPath    = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
$backupDir = "C:\Users\HITMA\Desktop\synergos-backups"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if (Test-Path $dbPath) {
    $dest = "$backupDir\Umbraco-ola-close-$olaNum-$timestamp.sqlite.db"
    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory $backupDir | Out-Null }
    Copy-Item $dbPath $dest -Force
    $sizeMB = [Math]::Round((Get-Item $dest).Length / 1MB, 2)
    Write-Output "Backup pre-cierre: $dest ($sizeMB MB)"
}
```

---

## 4. Health check rápido

```powershell
# Ping CMS
try {
    $ping = Invoke-WebRequest "http://synergos.local:5000/umbraco/api/keepalive/ping" `
        -UseBasicParsing -TimeoutSec 5
    Write-Output "CMS OK — $($ping.StatusCode)"
} catch {
    Write-Warning "CMS no responde — continuar de todas formas con el commit"
}
```

---

## 5. Commitear los cambios — mensaje canónico

El formato de commit de este proyecto sigue el patrón `docs(ola-NNN): ...` o `feat(ola-NNN): ...`:

```powershell
# 5A. Ver qué hay para commitear
git status --short

# 5B. Staging selectivo (NUNCA git add -A)
# Agregar solo los archivos del proyecto — NO el DB, NO node_modules, NO LOCAL_CDN

# uSync XMLs
git add Synergos.CMS/Synergos.CMS.Web/uSync/

# Razor views y C# (si aplica)
git add Synergos.CMS/Synergos.CMS.Web/Views/
git add Synergos.CMS/ --update  # actualiza tracked files, no agrega untracked sensibles

# Angular (si aplica)
git add Synergos.UI/platforms/angular/apps/elements/

# Docs de arquitectura (si se actualizaron)
git add refactor-docs/

# Tests (si se escribieron)
git add Synergos.CMS/Synergos.CMS.Tests/

# 5C. Verificar qué está staged
git diff --cached --name-only
```

```powershell
# 5D. Commit con mensaje canónico
# Formato según recent commits del repo:
# docs(olas-NNN-NNN): descripción corta
# feat(ola-NNN): descripción del feature
# fix(ola-NNN): descripción del fix

$olaTag = $olaNum.ToLower() -replace 'ola-', 'ola-'
$commitMsg = "feat($olaTag): $olaTitle"

git commit -m $commitMsg
Write-Output "Commit creado: $commitMsg"

# Ver el commit
git log --oneline -3
```

---

## 6. Actualizar documentación §11.x

Si la Ola generó ADRs nuevos, actualizar `refactor-docs/architecture/00-current-state-synergos-cms.md`:

```powershell
$currentStatePath = "refactor-docs\architecture\00-current-state-synergos-cms.md"

if (Test-Path $currentStatePath) {
    $content = Get-Content $currentStatePath -Raw

    # Verificar la sección §11.2 (conteo de ADRs)
    if ($content -match '(\d+) ADRs') {
        $currentCount = [int]$Matches[1]
        Write-Output "Conteo actual de ADRs en §11.2: $currentCount"
        Write-Output "Si se crearon ADRs nuevos en esta Ola, actualizar a $($currentCount + $adrsCreated.Count)"
    }
}
```

**Instrucción para el arquitecto si hay ADRs nuevos:**
```
Actualizar refactor-docs/architecture/00-current-state-synergos-cms.md:

§11.2 — Cambiar el conteo de ADRs de X a Y
§11.2 — Agregar los nuevos ADRs a la tabla de índice
§11.XX — Agregar la sección de cierre de esta Ola con:
  - Número y nombre de la Ola
  - ADRs creados/cerrados
  - Entregables: schema, Razor, Angular, bundles
  - Estado final (CLOSED / MERGED)
```

---

## 7. Verificar que el siguiente Import está listo

Si la Ola incluyó cambios de schema, verificar que el Import ya se ejecutó:

```powershell
# Si hay token disponible, verificar que los tipos están en DB
$baseUrl = "http://synergos.local:5000"
try {
    $auth = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/security/back-office/token" `
        -Method POST -ContentType "application/x-www-form-urlencoded" `
        -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
    $headers = @{ "Authorization" = "Bearer $($auth.access_token)" }

    # Verificar cada ElementType nuevo de la Ola
    foreach ($alias in $newElementTypes) {  # reemplazar con los aliases reales
        $cts   = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/document-type?skip=0&take=200" -Headers $headers
        $found = $cts.items | Where-Object { $_.alias -eq $alias }
        if ($found) {
            Write-Output "  ✓ $alias en DB"
        } else {
            Write-Warning "  ✗ $alias NO en DB — ejecutar synergos-usync-import"
        }
    }
} catch {
    Write-Warning "No se pudo verificar vía API — CMS puede no estar corriendo"
}
```

---

## 8. Reporte de cierre

```
════════════════════════════════════════════════════════
  SYNERGOS — Cierre de $olaNum
════════════════════════════════════════════════════════
  Título     : $olaTitle
  Fecha      : $(Get-Date -Format 'yyyy-MM-dd HH:mm')
  Commit     : $(git log --oneline -1)

  Entregables:
    Schema (uSync XMLs) : $($newXmls.Count) archivos
    Razor views         : $($razorNew.Count) archivos
    Angular components  : presentes / ausentes
    Bundles CDN         : $(@($reg.elements).Count) en registry.json
    Tests               : todos pasando

  ADRs de esta Ola:
    $adrsCreated

  Backup pre-cierre:
    $dest

  Siguiente Ola: ¿qué sigue?
════════════════════════════════════════════════════════
```

---

## 9. Logs a ignorar durante el cierre (ruido normal)

```
warn: Umbraco.Cms.Core.Services.LocalizedTextService — Could not find localization file
info: ModelsBuilder: FlagOutOfDateModels (si IsElement cambió)
warn: uSync: Skipping [no changes detected] (para tipos que no cambiaron)
```

## 10. Señales que sí detienen el cierre

```
error: Tests fallidos antes del commit
error: UNIQUE constraint failed — GUID collision en uSync
error: DB integrity check failed
error: git commit rechazado por pre-commit hook
```

Si alguna se activa: resolver antes de marcar la Ola como cerrada.
