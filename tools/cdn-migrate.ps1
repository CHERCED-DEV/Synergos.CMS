<#
.SYNOPSIS
Migra una CDN local existente al canon documentado en
docs/contracts/cdn-bundle-structure.md.

.DESCRIPTION
Cap-280 Batch A.2 (revisado). Toma una CDN root path como input y aplica:

1. Renombra archivos sin extension a .json:
   manifest  ->  manifest.json
   meta      ->  meta.json

2. Calcula SRI integrity hash (sha384) de cada main.js y lo inyecta
   al manifest.json correspondiente si no esta presente.

NO renombra carpetas top-level. La CDN puede usar tanto la forma
corta (column/) como la forma con prefix (synergos-column/) — ambas
son válidas. El CMS soporta los 2 estilos via setting
Synergos:Cdn:StripFolderPrefix. Ver
docs/contracts/cdn-bundle-structure.md regla 1.

Soporta -DryRun para preview sin tocar nada.
Reporta cada cambio + counts agregados al final.

.PARAMETER CdnRoot
Path absoluto a la raiz de la CDN local (e.g. "C:\LOCAL_CDN").

.PARAMETER DryRun
Si presente, solo muestra que cambiaria sin aplicar.

.PARAMETER PrefixFolder
Subfolder bajo CdnRoot donde viven los bundles. Default "synergos".

.EXAMPLE
.\cdn-migrate.ps1 -CdnRoot "C:\LOCAL_CDN" -DryRun

.EXAMPLE
.\cdn-migrate.ps1 -CdnRoot "C:\LOCAL_CDN"

.NOTES
Idempotent: si ya esta canonical, todos los pasos son no-op.
Backup: NO crea backup automatico. El operador debe hacer copy
manual antes de correr en modo apply (DRY-RUN primero).

ASCII-only por encoding safety: PowerShell 5.1 lee .ps1 como ANSI
por default cuando el archivo no tiene BOM. Sin caracteres Unicode
en el script source para evitar mojibake en boxes-drawing y emojis.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CdnRoot,

    [switch]$DryRun,

    [string]$PrefixFolder = "synergos"
)

$ErrorActionPreference = "Stop"

# --- Validation -------------------------------------------------

if (-not (Test-Path $CdnRoot)) {
    Write-Host "[error] CdnRoot path doesn't exist: $CdnRoot" -ForegroundColor Red
    exit 1
}

$bundlesRoot = Join-Path $CdnRoot $PrefixFolder
if (-not (Test-Path $bundlesRoot)) {
    Write-Host "[error] Bundles root doesn't exist: $bundlesRoot" -ForegroundColor Red
    Write-Host "        Expected structure: $CdnRoot\$PrefixFolder\..." -ForegroundColor Yellow
    exit 1
}

if ($DryRun) {
    Write-Host "================ DRY RUN -- no changes will be applied ================" -ForegroundColor Yellow
} else {
    Write-Host "================ APPLY MODE -- changes will be persisted ================" -ForegroundColor Cyan
    Write-Host "Tip: corre con -DryRun primero si no estas seguro." -ForegroundColor DarkGray
}
Write-Host "Root: $CdnRoot"
Write-Host "Bundles: $bundlesRoot"
Write-Host ""

$stats = @{
    FilesRenamed   = 0
    HashesAdded    = 0
    HashesAlready  = 0
    Errors         = 0
}

# --- Step 1: Rename manifest/meta to .json ----------------------

Write-Host "-- Step 1: manifest/meta -> .json extension ------------" -ForegroundColor Green
$candidates = @("manifest", "meta")
foreach ($name in $candidates) {
    $files = Get-ChildItem -Path $bundlesRoot -Recurse -Filter $name -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        # Skip si ya tiene extension (e.g. manifest.json existente)
        if ($file.Name -ne $name) { continue }
        $newName = "$name.json"
        $newPath = Join-Path $file.Directory.FullName $newName
        if (Test-Path $newPath) {
            Write-Host "  [skip] $($file.FullName) -> $newName (target exists)" -ForegroundColor Yellow
            $stats.Errors++
            continue
        }
        $rel = $file.FullName.Substring($bundlesRoot.Length).TrimStart('\', '/')
        Write-Host "  [rename] $rel -> $newName" -ForegroundColor Cyan
        if (-not $DryRun) {
            try {
                Rename-Item -Path $file.FullName -NewName $newName
                $stats.FilesRenamed++
            } catch {
                Write-Host "    [error] $_" -ForegroundColor Red
                $stats.Errors++
            }
        } else {
            $stats.FilesRenamed++
        }
    }
}

Write-Host ""

# --- Step 2: SRI integrity hash injection -----------------------

Write-Host "-- Step 2: SRI integrity (sha384) en manifest.json -----" -ForegroundColor Green
$manifests = Get-ChildItem -Path $bundlesRoot -Recurse -Filter "manifest.json" -File -ErrorAction SilentlyContinue
foreach ($manifest in $manifests) {
    $manifestDir = $manifest.Directory.FullName
    $rel = $manifest.FullName.Substring($bundlesRoot.Length).TrimStart('\', '/')

    try {
        $content = Get-Content -Path $manifest.FullName -Raw -Encoding UTF8
        $json = $content | ConvertFrom-Json
    } catch {
        Write-Host "  [error] $rel parsing failed: $_" -ForegroundColor Red
        $stats.Errors++
        continue
    }

    $mainName = if ($json.PSObject.Properties.Name -contains 'main' -and $json.main) { $json.main } else { 'main.js' }
    $mainPath = Join-Path $manifestDir $mainName

    if (-not (Test-Path $mainPath)) {
        Write-Host "  [warn] $rel -- main file '$mainName' no existe en $manifestDir" -ForegroundColor Yellow
        $stats.Errors++
        continue
    }

    if ($json.PSObject.Properties.Name -contains 'integrity' -and $json.integrity -and ($json.integrity -like 'sha384-*')) {
        Write-Host "  [ok] $rel (integrity already present)" -ForegroundColor DarkGray
        $stats.HashesAlready++
        continue
    }

    # Compute sha384 SRI
    $bytes = [System.IO.File]::ReadAllBytes($mainPath)
    $sha = [System.Security.Cryptography.SHA384]::Create()
    $hash = $sha.ComputeHash($bytes)
    $sri = "sha384-$([Convert]::ToBase64String($hash))"
    $bytesLen = $bytes.Length

    Write-Host "  [hash] $rel" -ForegroundColor Cyan
    Write-Host "         $sri ($bytesLen bytes)" -ForegroundColor DarkCyan

    if (-not $DryRun) {
        try {
            # Inject integrity + size si no estan
            if ($json.PSObject.Properties.Name -notcontains 'integrity') {
                $json | Add-Member -NotePropertyName 'integrity' -NotePropertyValue $sri -Force
            } else {
                $json.integrity = $sri
            }
            if ($json.PSObject.Properties.Name -notcontains 'size') {
                $json | Add-Member -NotePropertyName 'size' -NotePropertyValue $bytesLen -Force
            }
            # Write back con indent + UTF-8 sin BOM
            $newJson = $json | ConvertTo-Json -Depth 20
            [System.IO.File]::WriteAllText($manifest.FullName, $newJson, [System.Text.UTF8Encoding]::new($false))
            $stats.HashesAdded++
        } catch {
            Write-Host "    [error] $_" -ForegroundColor Red
            $stats.Errors++
        }
    } else {
        $stats.HashesAdded++
    }
}

Write-Host ""
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host "Files renamed:            $($stats.FilesRenamed)"
Write-Host "Integrity hashes added:   $($stats.HashesAdded)"
Write-Host "Integrity hashes already: $($stats.HashesAlready)"
$errorColor = if ($stats.Errors -gt 0) { 'Yellow' } else { 'Green' }
Write-Host "Errors / skips:           $($stats.Errors)" -ForegroundColor $errorColor
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN complete. Run without -DryRun to apply changes." -ForegroundColor Yellow
} else {
    Write-Host "Migration complete. Verify with:" -ForegroundColor Green
    Write-Host "  Get-ChildItem -Path '$bundlesRoot' -Recurse | Select-Object FullName"
}
