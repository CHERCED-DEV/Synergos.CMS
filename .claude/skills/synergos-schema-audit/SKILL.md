---
name: synergos-schema-audit
description: Auditoría completa del schema de Synergos — cruza uSync XMLs contra Razor views, Angular projects, registry.json y código C# para encontrar orphans, ElementTypes incompletos, DataTypes sin uso, compositions sin consumers, GUIDs rotos en BlockLists, y elementos "a medias". Genera un reporte accionable por categoría.
model: claude-opus-4-8
---

# SYNERGOS Schema Audit — auditoría cruzada del schema

Esta skill detecta inconsistencias entre las 5 fuentes de verdad del schema:
1. `uSync/v9/ContentTypes/` — XMLs de DocTypes/ElementTypes/Compositions
2. `uSync/v9/DataTypes/` — XMLs de DataTypes
3. `Views/Partials/SynHost/` — Razor renderers
4. `Synergos.UI/platforms/angular/apps/elements/` — Angular projects
5. `C:\LOCAL_CDN\synergos\registry.json` — bundles publicados

---

## 0. Rutas base

```powershell
$repoRoot    = "C:\Users\HITMA\Desktop\synergos"
$uSyncCT     = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes"
$uSyncDT     = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\uSync\v9\DataTypes"
$synHostDir  = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Views\Partials\SynHost"
$bgridDir    = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Views\Partials\blockgrid\Components"
$angularDir  = "$repoRoot\Synergos.UI\platforms\angular\apps\elements"
$registryPath = "C:\LOCAL_CDN\synergos\registry.json"
```

---

## 1. Inventario de ElementTypes en uSync

```powershell
$elementTypes = Get-ChildItem $uSyncCT -Filter "elementSyn*.config" -Recurse |
    ForEach-Object {
        [xml]$xml = Get-Content $_.FullName -Encoding UTF8
        [PSCustomObject]@{
            File     = $_.Name
            Alias    = $xml.ContentType.Attributes["Alias"]?.Value ?? $xml.ContentType.alias
            Key      = $xml.ContentType.Attributes["Key"]?.Value
            IsElement= $xml.ContentType.Info.IsElement
            Name     = $xml.ContentType.Info.Name
        }
    } | Where-Object { $_.IsElement -eq "true" }

Write-Output "ElementTypes en uSync: $($elementTypes.Count)"
$elementTypes | ForEach-Object { Write-Output "  · $($_.Alias)  ($($_.Key))" }
```

---

## 2. Inventario de Compositions en uSync

```powershell
$compositions = Get-ChildItem $uSyncCT -Filter "comp*.config" -Recurse |
    ForEach-Object {
        [xml]$xml = Get-Content $_.FullName -Encoding UTF8
        $key  = $xml.ContentType.Attributes["Key"]?.Value
        $desc = $xml.ContentType.Info.Description

        # Determinar si es reserved
        $reserved = $desc -match '^\[Bloqueado externamente' -or $desc -match '^\[Disponible'

        # Contar consumers (ContentTypes que tienen este Key en CompositionKeys)
        $consumers = Get-ChildItem $uSyncCT -Recurse -Filter "*.config" |
            Select-String -Pattern $key -SimpleMatch |
            Where-Object { $_.Path -notmatch [regex]::Escape($_.Filename) } |
            Select-Object -ExpandProperty Path -Unique

        [PSCustomObject]@{
            Alias     = $xml.ContentType.Attributes["Alias"]?.Value
            Key       = $key
            Reserved  = $reserved
            Consumers = @($consumers).Count
            Desc      = ($desc -replace '\s+', ' ').Trim() | Select-Object -First 1
        }
    }

$orphanComps = $compositions | Where-Object { -not $_.Reserved -and $_.Consumers -eq 0 }
Write-Output "Compositions totales: $($compositions.Count)"
Write-Output "  Orphans reales (sin consumers ni marker): $($orphanComps.Count)"
if ($orphanComps) {
    $orphanComps | ForEach-Object { Write-Warning "  ORPHAN: $($_.Alias)  ($($_.Key))" }
}
```

---

## 3. Inventario de DataTypes en uSync

```powershell
$dataTypes = Get-ChildItem $uSyncDT -Filter "*.config" -Recurse |
    ForEach-Object {
        [xml]$xml = Get-Content $_.FullName -Encoding UTF8
        [PSCustomObject]@{
            Alias  = $xml.DataType.Attributes["Alias"]?.Value
            Key    = $xml.DataType.Attributes["Key"]?.Value
            Editor = $xml.DataType.Attributes["EditorAlias"]?.Value
        }
    }

# Encontrar DataTypes no usados en ningún ContentType
$usedDTKeys = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem $uSyncCT -Recurse -Filter "*.config" | ForEach-Object {
    [xml]$xml = Get-Content $_.FullName -Encoding UTF8
    $xml.ContentType.GenericProperties.GenericProperty | ForEach-Object {
        $usedDTKeys.Add($_.Type) | Out-Null
        $usedDTKeys.Add($_.Definition) | Out-Null
    }
}

$unusedDTs = $dataTypes | Where-Object { -not $usedDTKeys.Contains($_.Key) }
Write-Output "DataTypes totales: $($dataTypes.Count)"
Write-Output "DataTypes sin uso en ContentTypes: $($unusedDTs.Count)"
$unusedDTs | ForEach-Object { Write-Output "  · $($_.Alias) [$($_.Editor)]" }
```

---

## 4. Cruzar ElementTypes contra Razor views

```powershell
$razorIssues = [System.Collections.Generic.List[string]]::new()

foreach ($et in $elementTypes) {
    # Alias: elementSynHeroBanner → esperar HeroBanner.cshtml en SynHost/
    $pascal     = $et.Alias -replace '^elementSyn', ''
    $synHostFile = "$synHostDir\$pascal.cshtml"
    $bgridFile   = "$bgridDir\$($et.Alias).cshtml"  # o similar

    if (-not (Test-Path $synHostFile)) {
        $razorIssues.Add("MISSING Razor SynHost: $pascal.cshtml  (para $($et.Alias))")
    }
    if (-not (Test-Path $bgridFile)) {
        # Block grid wrapper puede tener nombre diferente — buscar por alias
        $found = Get-ChildItem $bgridDir -Filter "*.cshtml" |
            Select-String -Pattern $et.Alias -SimpleMatch -List
        if (-not $found) {
            $razorIssues.Add("MISSING BGrid wrapper para $($et.Alias)")
        }
    }
}

Write-Output "Razor issues: $($razorIssues.Count)"
$razorIssues | ForEach-Object { Write-Warning "  $_" }
```

---

## 5. Cruzar ElementTypes contra Angular projects

```powershell
$angularIssues = [System.Collections.Generic.List[string]]::new()

foreach ($et in $elementTypes) {
    $pascal   = $et.Alias -replace '^elementSyn', ''
    $kebab    = ($pascal -creplace '(?<=[a-z])(?=[A-Z])', '-').ToLower()
    # elementSynHeroBanner → hero-banner
    # Buscar en los tiers de Angular

    $found = $false
    foreach ($tier in @("primitive", "composition", "module", "experience")) {
        $projectPath = "$angularDir\$tier\$kebab"
        if (Test-Path $projectPath) { $found = $true; break }
        # Intentar sin guiones también
        $altPath = "$angularDir\$tier\$($kebab -replace '-','')"
        if (Test-Path $altPath) { $found = $true; break }
    }

    if (-not $found) {
        $angularIssues.Add("MISSING Angular project para $($et.Alias) (esperado: $kebab)")
    }
}

Write-Output "Angular project issues: $($angularIssues.Count)"
$angularIssues | ForEach-Object { Write-Warning "  $_" }
```

---

## 6. Cruzar ElementTypes contra registry.json

```powershell
$registryIssues = [System.Collections.Generic.List[string]]::new()

try {
    $reg      = Get-Content $registryPath -Raw | ConvertFrom-Json
    $regNames = @($reg.elements) | ForEach-Object { $_.name }

    foreach ($et in $elementTypes) {
        $pascal = $et.Alias -replace '^elementSyn', ''
        $kebab  = ($pascal -creplace '(?<=[a-z])(?=[A-Z])', '-').ToLower()

        if ($kebab -notin $regNames) {
            $registryIssues.Add("NOT IN REGISTRY: $($et.Alias) (esperado name: $kebab)")
        }
    }

    # Elementos en registry sin ElementType en uSync
    $etAliases = $elementTypes | ForEach-Object {
        ($($_.Alias -replace '^elementSyn', '') -creplace '(?<=[a-z])(?=[A-Z])', '-').ToLower()
    }
    foreach ($entry in @($reg.elements)) {
        if ($entry.name -notin $etAliases) {
            $registryIssues.Add("REGISTRY ORPHAN: $($entry.name) no tiene ElementType en uSync")
        }
    }
} catch {
    $registryIssues.Add("registry.json no legible: $_")
}

Write-Output "Registry issues: $($registryIssues.Count)"
$registryIssues | ForEach-Object { Write-Warning "  $_" }
```

---

## 7. Verificar GUIDs en BlockLists (collision check)

```powershell
$blockListCollisions = [System.Collections.Generic.List[string]]::new()

$blockListDTs = $dataTypes | Where-Object { $_.Editor -eq "Umbraco.BlockList" }
foreach ($dt in $blockListDTs) {
    [xml]$dtXml = Get-Content (Get-ChildItem $uSyncDT -Recurse | Where-Object {
        $content = Get-Content $_.FullName -Raw
        $content -match $dt.Key
    } | Select-Object -First 1).FullName -Encoding UTF8

    $blocksJson = $dtXml.DataType.PreValues.PreValue | Where-Object { $_.Alias -eq "blocks" }
    if ($blocksJson) {
        try {
            $blocks = $blocksJson.value | ConvertFrom-Json
            foreach ($block in $blocks) {
                $blockKey = $block.contentElementTypeKey
                if ($blockKey -and $blockKey -eq $dt.Key) {
                    $blockListCollisions.Add("GUID COLLISION en $($dt.Alias): DataType Key == Block contentElementTypeKey ($blockKey)")
                }
            }
        } catch { }
    }
}

Write-Output "BlockList GUID collisions: $($blockListCollisions.Count)"
$blockListCollisions | ForEach-Object { Write-Error "  $_" }
```

---

## 8. Verificar encoding de XMLs

```powershell
$encodingIssues = [System.Collections.Generic.List[string]]::new()

Get-ChildItem "$uSyncCT", "$uSyncDT" -Recurse -Filter "*.config" | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    if ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $encodingIssues.Add("UTF-16 LE BOM: $($_.Name)")
    } elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0x00 -and $bytes[1] -eq 0x3C) {
        $encodingIssues.Add("UTF-16 BE: $($_.Name)")
    }
}

Write-Output "Encoding issues: $($encodingIssues.Count)"
$encodingIssues | ForEach-Object { Write-Warning "  $_" }
```

---

## 9. Reporte final

```powershell
Write-Output ""
Write-Output "═══════════════════════════════════════════════════════════"
Write-Output "  SYNERGOS Schema Audit — $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Output "═══════════════════════════════════════════════════════════"
Write-Output "  ElementTypes      : $($elementTypes.Count)"
Write-Output "  Compositions      : $($compositions.Count)  ($($orphanComps.Count) orphans reales)"
Write-Output "  DataTypes         : $($dataTypes.Count)  ($($unusedDTs.Count) sin uso)"
Write-Output ""
Write-Output "  Razor issues      : $($razorIssues.Count)"
Write-Output "  Angular issues    : $($angularIssues.Count)"
Write-Output "  Registry issues   : $($registryIssues.Count)"
Write-Output "  BlockList GUID    : $($blockListCollisions.Count)"
Write-Output "  Encoding issues   : $($encodingIssues.Count)"
Write-Output "═══════════════════════════════════════════════════════════"

$total = $orphanComps.Count + $unusedDTs.Count + $razorIssues.Count +
         $angularIssues.Count + $registryIssues.Count +
         $blockListCollisions.Count + $encodingIssues.Count

if ($total -eq 0) {
    Write-Output "  Estado: LIMPIO — sin issues detectados"
} elseif ($blockListCollisions.Count -gt 0 -or $encodingIssues.Count -gt 0) {
    Write-Output "  Estado: CRITICO — resolver BlockList/encoding antes del próximo Import"
} else {
    Write-Output "  Estado: CON GAPS — elementos incompletos detectados"
}
Write-Output "═══════════════════════════════════════════════════════════"
```

---

## 10. Acciones por tipo de issue

| Issue | Acción |
|-------|--------|
| Razor MISSING | Crear `SynHost/{Pascal}.cshtml` con patrón ISynHostEmitter (ver synergos-cms-author §5) |
| Angular MISSING | Crear proyecto Angular en el tier correcto (ver synergos-cms-author §6) |
| NOT IN REGISTRY | Ejecutar synergos-cdn-build para ese elemento |
| REGISTRY ORPHAN | Verificar si el ElementType fue eliminado; si sí, eliminar de registry.json |
| DataType sin uso | Puede ser legacy — verificar si está referenciado en bloques de BlockGrid antes de eliminar |
| Composition orphan | Verificar si tiene marker `[Disponible...]` antes de proponer eliminación |
| BlockList GUID collision | Asignar Key nuevo al DataType (ver synergos-usync-author §1A) |
| Encoding UTF-16 | Reescribir con `[IO.File]::WriteAllText(path, content, [Text.Encoding]::UTF8)` |
