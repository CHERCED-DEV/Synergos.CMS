<#
.SYNOPSIS
Migra una CDN local existente al canon documentado en
docs/contracts/cdn-bundle-structure.md.

.DESCRIPTION
Cap-280 Batch A.2. Toma una CDN root path como input y aplica:

1. Renombra carpetas top-level de elementos:
   {tag-sin-prefix}/  →  synergos-{tag}/

   (e.g. column/ → synergos-column/, accordion/ → synergos-accordion/)
   Skip si ya empieza con "synergos-".

2. Renombra archivos sin extensión a .json:
   manifest  →  manifest.json
   meta      →  meta.json

3. Calcula SRI integrity hash (sha384) de cada main.js y lo inyecta
   al manifest.json correspondiente si no está presente.

Soporta -DryRun para preview sin tocar nada.
Reporta cada cambio + counts agregados al final.

.PARAMETER CdnRoot
Path absoluto a la raíz de la CDN local (e.g. "C:\LOCAL_CDN").

.PARAMETER DryRun
Si presente, solo muestra qué cambiaría sin aplicar.

.PARAMETER PrefixFolder
Subfolder bajo CdnRoot donde viven los bundles. Default "synergos".

.EXAMPLE
.\cdn-migrate.ps1 -CdnRoot "C:\LOCAL_CDN" -DryRun

.EXAMPLE
.\cdn-migrate.ps1 -CdnRoot "C:\LOCAL_CDN"

.NOTES
Idempotent: si ya está canonical, todos los pasos son no-op.
Backup: NO crea backup automático. El operador debe hacer copy
manual antes de correr en modo apply (DRY-RUN primero).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$CdnRoot,

    [switch]$DryRun,

    [string]$PrefixFolder = "synergos"
)

$ErrorActionPreference = "Stop"

# ─── Validation ──────────────────────────────────────────────────

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
    Write-Host "════════════════ DRY RUN — no changes will be applied ════════════════" -ForegroundColor Yellow
} else {
    Write-Host "════════════════ APPLY MODE — changes will be persisted ════════════════" -ForegroundColor Cyan
    Write-Host "Tip: corre con -DryRun primero si no estás seguro." -ForegroundColor DarkGray
}
Write-Host "Root: $CdnRoot"
Write-Host "Bundles: $bundlesRoot"
Write-Host ""

$stats = @{
    FoldersRenamed = 0
    FilesRenamed   = 0
    HashesAdded    = 0
    HashesAlready  = 0
    Errors         = 0
}

# ─── Step 1: Rename top-level element folders to synergos-* ──────

Write-Host "── Step 1: Element folder naming ───────────────────────" -ForegroundColor Green
$elements = Get-ChildItem -Path $bundlesRoot -Directory
foreach ($folder in $elements) {
    if ($folder.Name -like "synergos-*") {
        Write-Host "  ✓ $($folder.Name) (already canonical)" -ForegroundColor DarkGray
        continue
    }
    $newName = "synergos-$($folder.Name)"
    $newPath = Join-Path $folder.Parent.FullName $newName
    if (Test-Path $newPath) {
        Write-Host "  ! $($folder.Name) → $newName SKIP (target exists)" -ForegroundColor Yellow
        $stats.Errors++
        continue
    }
    Write-Host "  $($folder.Name) → $newName" -ForegroundColor Cyan
    if (-not $DryRun) {
        try {
            Rename-Item -Path $folder.FullName -NewName $newName
            $stats.FoldersRenamed++
        } catch {
            Write-Host "    [error] $_" -ForegroundColor Red
            $stats.Errors++
        }
    } else {
        $stats.FoldersRenamed++
    }
}

Write-Host ""

# ─── Step 2: Rename manifest/meta to .json ───────────────────────

Write-Host "── Step 2: manifest/meta → .json extension ─────────────" -ForegroundColor Green
$candidates = @("manifest", "meta")
foreach ($name in $candidates) {
    $files = Get-ChildItem -Path $bundlesRoot -Recurse -Filter $name -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        # Skip si ya tiene extensión (e.g. manifest.json existente)
        if ($file.Name -ne $name) { continue }
        $newName = "$name.json"
        $newPath = Join-Path $file.Directory.FullName $newName
        if (Test-Path $newPath) {
            Write-Host "  ! $($file.FullName) → $newName SKIP (target exists)" -ForegroundColor Yellow
            $stats.Errors++
            continue
        }
        $rel = $file.FullName.Substring($bundlesRoot.Length).TrimStart('\', '/')
        Write-Host "  $rel → $newName" -ForegroundColor Cyan
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

# ─── Step 3: SRI integrity hash injection ────────────────────────

Write-Host "── Step 3: SRI integrity (sha384) en manifest.json ─────" -ForegroundColor Green
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
        Write-Host "  [warn] $rel — main file '$mainName' no existe en $manifestDir" -ForegroundColor Yellow
        $stats.Errors++
        continue
    }

    if ($json.PSObject.Properties.Name -contains 'integrity' -and $json.integrity -and ($json.integrity -like 'sha384-*')) {
        Write-Host "  ✓ $rel (integrity already present)" -ForegroundColor DarkGray
        $stats.HashesAlready++
        continue
    }

    # Compute sha384 SRI
    $bytes = [System.IO.File]::ReadAllBytes($mainPath)
    $sha = [System.Security.Cryptography.SHA384]::Create()
    $hash = $sha.ComputeHash($bytes)
    $sri = "sha384-$([Convert]::ToBase64String($hash))"

    $bytesLen = $bytes.Length
    Write-Host "  $rel ← $sri ($bytesLen bytes)" -ForegroundColor Cyan

    if (-not $DryRun) {
        try {
            # Inject integrity + size si no están
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
Write-Host "════════════════ Summary ════════════════" -ForegroundColor Cyan
Write-Host "Folders renamed:       $($stats.FoldersRenamed)"
Write-Host "Files renamed:         $($stats.FilesRenamed)"
Write-Host "Integrity hashes added: $($stats.HashesAdded)"
Write-Host "Integrity hashes already: $($stats.HashesAlready)"
Write-Host "Errors / skips:        $($stats.Errors)" -ForegroundColor $(if ($stats.Errors -gt 0) { 'Yellow' } else { 'Green' })
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN complete. Run without -DryRun to apply changes." -ForegroundColor Yellow
} else {
    Write-Host "Migration complete. Verify with:" -ForegroundColor Green
    Write-Host "  Get-ChildItem -Path '$bundlesRoot' -Recurse | Select-Object FullName"
}
