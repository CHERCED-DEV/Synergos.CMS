---
name: synergos-health-check
description: Diagnóstico rápido del stack completo de Synergos — CMS ping, Management API, bundle registry, CDN estático, DB integridad, Swagger. Genera un reporte semáforo (OK/WARN/FAIL) en menos de 30 segundos. Punto de partida de cualquier sesión de trabajo o verificación post-deploy.
model: claude-opus-4-8
---

# SYNERGOS Health Check — diagnóstico completo del stack

Ejecutar al inicio de cada sesión y después de cualquier cambio de infraestructura.

---

## Script completo (ejecutar todo junto)

```powershell
$base    = "http://synergos.local:5000"
$cdnRoot = "C:\LOCAL_CDN\synergos"
$dbPath  = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
$report  = [System.Collections.Generic.List[PSCustomObject]]::new()

function Add-Check {
    param([string]$Name, [string]$Status, [string]$Detail)
    $report.Add([PSCustomObject]@{ Check=$Name; Status=$Status; Detail=$Detail })
}

# ─── 1. CMS Ping ─────────────────────────────────────────────────────────────
try {
    $r = Invoke-WebRequest "$base/umbraco/api/keepalive/ping" -UseBasicParsing -TimeoutSec 5
    Add-Check "CMS Ping" "OK" "HTTP $($r.StatusCode)"
} catch {
    Add-Check "CMS Ping" "FAIL" "CMS no responde — ejecutar synergos-run-dev"
}

# ─── 2. Management API ───────────────────────────────────────────────────────
$token = $null
try {
    $auth = Invoke-RestMethod "$base/umbraco/management/api/v1/security/back-office/token" `
        -Method POST -ContentType "application/x-www-form-urlencoded" `
        -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21" `
        -TimeoutSec 8
    $token = $auth.access_token
    Add-Check "Management API" "OK" "Token obtenido (expira en $($auth.expires_in)s)"
} catch {
    Add-Check "Management API" "FAIL" $_.Exception.Message
}

# ─── 3. Swagger UI ───────────────────────────────────────────────────────────
try {
    Invoke-WebRequest "$base/umbraco/swagger" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Add-Check "Swagger UI" "OK" "$base/umbraco/swagger"
} catch {
    Add-Check "Swagger UI" "WARN" "No accesible (no crítico en prod)"
}

# ─── 4. Bundle Registry ──────────────────────────────────────────────────────
try {
    $reg = Get-Content "$cdnRoot\registry.json" -Raw | ConvertFrom-Json
    $count = @($reg.elements).Count
    Add-Check "Bundle Registry" "OK" "$count elementos — generado $($reg.generated)"
} catch {
    Add-Check "Bundle Registry" "FAIL" "registry.json no legible en $cdnRoot"
}

# ─── 5. CDN Static Files ─────────────────────────────────────────────────────
try {
    if ($count -gt 0) {
        $el  = @($reg.elements)[0]
        $ver = $el.implementations.angular.latest
        $url = "$base/cdn-bundles/synergos/$($el.name)/angular/$ver/main.js"
        $r2  = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 8
        $kb  = [Math]::Round($r2.RawContentLength / 1024, 1)
        Add-Check "CDN Static" "OK" "$($el.name) → $($r2.StatusCode) ($kb KB) — Cache: $($r2.Headers['Cache-Control'])"
    } else {
        Add-Check "CDN Static" "WARN" "Registry vacío — sin elementos para probar"
    }
} catch {
    Add-Check "CDN Static" "FAIL" "Bundle no accesible: $url"
}

# ─── 6. Sitio Público ────────────────────────────────────────────────────────
try {
    $pub = Invoke-WebRequest "$base/" -UseBasicParsing -TimeoutSec 8
    $hasHtml = $pub.Content -match '<html'
    Add-Check "Sitio Público" "OK" "HTTP $($pub.StatusCode) — HTML: $hasHtml"
} catch {
    Add-Check "Sitio Público" "WARN" "Sitio no responde — puede no tener content aún"
}

# ─── 7. DB Integrity ─────────────────────────────────────────────────────────
try {
    if (Test-Path $dbPath) {
        $sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
        if ($sqlite3) {
            $integrity = & sqlite3 $dbPath "PRAGMA integrity_check;" 2>&1
            $status    = if ($integrity -eq "ok") { "OK" } else { "FAIL" }
            $sizeMB    = [Math]::Round((Get-Item $dbPath).Length / 1MB, 2)
            Add-Check "DB Integrity" $status "PRAGMA: $integrity — Tamaño: $sizeMB MB"
        } else {
            $sizeMB = [Math]::Round((Get-Item $dbPath).Length / 1MB, 2)
            Add-Check "DB Integrity" "WARN" "sqlite3 CLI no disponible — DB existe ($sizeMB MB)"
        }
    } else {
        Add-Check "DB Integrity" "WARN" "DB no encontrada — ¿primer arranque?"
    }
} catch {
    Add-Check "DB Integrity" "WARN" $_.Exception.Message
}

# ─── 8. Backups recientes ────────────────────────────────────────────────────
try {
    $backups = Get-ChildItem "C:\Users\HITMA\Desktop\synergos-backups" "*.sqlite.db" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($backups) {
        $age = [Math]::Round(((Get-Date) - $backups.LastWriteTime).TotalHours, 1)
        $status = if ($age -gt 48) { "WARN" } else { "OK" }
        Add-Check "Último Backup" $status "$($backups.Name) — hace $age h"
    } else {
        Add-Check "Último Backup" "WARN" "Sin backups — ejecutar synergos-db-ops backup"
    }
} catch {
    Add-Check "Último Backup" "WARN" "Directorio de backups no encontrado"
}

# ─── 9. Content types en DB ──────────────────────────────────────────────────
if ($token) {
    try {
        $hdrs = @{ "Authorization" = "Bearer $token" }
        $cts  = Invoke-RestMethod "$base/umbraco/management/api/v1/document-type?skip=0&take=1" -Headers $hdrs
        Add-Check "Schema en DB" "OK" "$($cts.total) DocTypes registrados"
    } catch {
        Add-Check "Schema en DB" "WARN" "No se pudo consultar vía API"
    }
}

# ─── Reporte ─────────────────────────────────────────────────────────────────
$oks   = @($report | Where-Object Status -eq "OK").Count
$warns = @($report | Where-Object Status -eq "WARN").Count
$fails = @($report | Where-Object Status -eq "FAIL").Count

$overall = if ($fails -gt 0) { "DEGRADADO" } elseif ($warns -gt 0) { "PARCIAL" } else { "SALUDABLE" }

Write-Output ""
Write-Output "═══════════════════════════════════════════════════════════"
Write-Output "  SYNERGOS Health Check — $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "  Estado: $overall  [$oks OK | $warns WARN | $fails FAIL]"
Write-Output "═══════════════════════════════════════════════════════════"
$report | ForEach-Object {
    $icon = switch ($_.Status) { "OK" { "✓" } "WARN" { "⚠" } "FAIL" { "✗" } }
    Write-Output "  $icon $($_.Status.PadRight(5)) $($_.Check.PadRight(18)) $($_.Detail)"
}
Write-Output "═══════════════════════════════════════════════════════════"
Write-Output "  Backoffice : $base/umbraco/"
Write-Output "  Swagger    : $base/umbraco/swagger"
Write-Output "  Registry   : $cdnRoot\registry.json"
Write-Output "═══════════════════════════════════════════════════════════"
```

---

## Interpretación del reporte

| Estado global | Significado | Acción |
|---------------|-------------|--------|
| `SALUDABLE` | Todo OK | Proceder con el trabajo |
| `PARCIAL` | Hay WARNs pero nada roto | Revisar WARNs; puede trabajarse |
| `DEGRADADO` | Al menos un FAIL | Resolver FAILs antes de continuar |

### Acciones rápidas por síntoma

| Check fallido | Acción inmediata |
|---------------|-----------------|
| CMS Ping FAIL | `/synergos-run-dev` |
| Management API FAIL | CMS corriendo pero credenciales erróneas — revisar appsettings.Development.json |
| CDN Static FAIL | `/synergos-cdn-build` para el elemento fallido |
| Bundle Registry FAIL | Crear registry.json mínimo (ver synergos-run-dev §1D) |
| DB Integrity FAIL | Detener CMS + restore desde backup (`synergos-db-ops` §9) |
| Último Backup WARN >48h | Hacer backup ahora: `synergos-db-ops` función `Backup-SynergosSqlite` |
