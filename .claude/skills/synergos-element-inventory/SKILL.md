---
name: synergos-element-inventory
description: Genera un mapa cruzado completo de todos los elementos de Synergos — cruza uSync XMLs (ElementTypes), Razor views (SynHost + Block Grid wrappers), Angular projects (NX workspace), y bundles publicados (registry.json + LOCAL_CDN). Detecta elementos incompletos ("a medias") y los clasifica por nivel de completitud. Útil antes de una Ola para saber el estado real.
model: claude-opus-4-8
---

# SYNERGOS Element Inventory — mapa cruzado de todos los elementos

Un elemento "completo" en Synergos tiene exactamente 5 capas presentes:
1. **uSync XML** — `elementSyn*.config` (schema)
2. **Block Grid wrapper** — `blockgrid/Components/elementSyn*.cshtml` (punto de entrada Razor)
3. **SynHost renderer** — `SynHost/{Pascal}.cshtml` (delegación a ISynHostEmitter)
4. **Angular project** — `Synergos.UI/platforms/angular/apps/elements/{tier}/{kebab}/`
5. **Bundle publicado** — entrada en `registry.json` + archivo en `LOCAL_CDN/`

---

## 0. Rutas base

```powershell
$repoRoot    = "C:\Users\HITMA\Desktop\synergos"
$uSyncCT     = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes"
$synHostDir  = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Views\Partials\SynHost"
$bgridDir    = "$repoRoot\Synergos.CMS\Synergos.CMS.Web\Views\Partials\blockgrid\Components"
$angularRoot = "$repoRoot\Synergos.UI\platforms\angular\apps\elements"
$regPath     = "C:\LOCAL_CDN\synergos\registry.json"
```

---

## 1. Paso 1 — Recolectar todos los ElementTypes de uSync

```powershell
$elements = [System.Collections.Generic.List[PSCustomObject]]::new()

Get-ChildItem $uSyncCT -Filter "elementSyn*.config" -Recurse | ForEach-Object {
    try {
        [xml]$xml   = Get-Content $_.FullName -Encoding UTF8
        $alias      = $xml.ContentType.Attributes["Alias"]?.Value
        $key        = $xml.ContentType.Attributes["Key"]?.Value
        $isElement  = $xml.ContentType.Info.IsElement
        $name       = $xml.ContentType.Info.Name

        if ($alias -and $isElement -eq "true") {
            # Derivar pascal y kebab del alias
            $pascal = $alias -replace '^elementSyn', ''
            $kebab  = ($pascal -creplace '(?<=[a-z])(?=[A-Z])', '-').ToLower()

            $elements.Add([PSCustomObject]@{
                Alias   = $alias
                Pascal  = $pascal
                Kebab   = $kebab
                Key     = $key
                Name    = $name
                File    = $_.Name
                # Layers — se llenarán en pasos siguientes
                HasXml      = $true
                HasBgrid    = $false
                HasSynHost  = $false
                HasAngular  = $false
                HasBundle   = $false
                AngularTier = ""
                BundleVer   = ""
            })
        }
    } catch {
        Write-Warning "Error leyendo $($_.Name): $_"
    }
}

Write-Output "ElementTypes en uSync: $($elements.Count)"
```

---

## 2. Paso 2 — Verificar Block Grid wrappers

```powershell
foreach ($el in $elements) {
    # El wrapper puede tener el nombre del alias directamente o en subcarpeta
    $candidates = @(
        "$bgridDir\$($el.Alias).cshtml",
        "$bgridDir\$($el.Pascal).cshtml"
    )
    $bgridFiles = Get-ChildItem $bgridDir -Filter "*.cshtml" -Recurse |
        Where-Object { $_.Name -match $el.Pascal -or $_.Name -match $el.Alias }

    $el.HasBgrid = $bgridFiles.Count -gt 0
}
```

---

## 3. Paso 3 — Verificar SynHost renderers

```powershell
foreach ($el in $elements) {
    $synHostFile = "$synHostDir\$($el.Pascal).cshtml"
    $el.HasSynHost = Test-Path $synHostFile
}
```

---

## 4. Paso 4 — Verificar Angular projects

```powershell
$angularTiers = @("primitive", "composition", "module", "experience")

foreach ($el in $elements) {
    $found = $false
    foreach ($tier in $angularTiers) {
        # Buscar por kebab exacto o variantes
        $candidates = @(
            "$angularRoot\$tier\$($el.Kebab)",
            "$angularRoot\$tier\$($el.Kebab -replace '-','')",
            "$angularRoot\$tier\$($el.Pascal.ToLower())"
        )
        $match = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($match) {
            $el.HasAngular   = $true
            $el.AngularTier  = $tier
            $found = $true
            break
        }
    }
    if (-not $found) {
        # Búsqueda más amplia en todos los tiers
        $allProjects = Get-ChildItem $angularRoot -Recurse -Filter "project.json" |
            Where-Object { $_.DirectoryName -match $el.Kebab -or $_.DirectoryName -match $el.Pascal }
        if ($allProjects) {
            $el.HasAngular = $true
            # Inferir el tier de la ruta
            $tierMatch = $allProjects[0].DirectoryName | Select-String -Pattern "($($angularTiers -join '|'))"
            $el.AngularTier = if ($tierMatch) { $tierMatch.Matches[0].Value } else { "?" }
        }
    }
}
```

---

## 5. Paso 5 — Verificar bundles en registry.json y LOCAL_CDN

```powershell
$reg = $null
try {
    $reg = Get-Content $regPath -Raw | ConvertFrom-Json
} catch {
    Write-Warning "registry.json no legible — paso 5 saltado"
}

if ($reg) {
    foreach ($el in $elements) {
        $entry = @($reg.elements) | Where-Object { $_.name -eq $el.Kebab } | Select-Object -First 1

        if ($entry) {
            $ver    = $entry.implementations.angular.latest
            $mainJs = "C:\LOCAL_CDN\synergos\$($el.Kebab)\angular\$ver\main.js"
            $el.HasBundle  = Test-Path $mainJs
            $el.BundleVer  = $ver
        }
    }
}
```

---

## 6. Clasificar por nivel de completitud

```powershell
function Get-CompletionLevel {
    param($el)
    $layers = @($el.HasXml, $el.HasBgrid, $el.HasSynHost, $el.HasAngular, $el.HasBundle)
    $count  = ($layers | Where-Object { $_ }).Count
    switch ($count) {
        5 { return "COMPLETO" }
        4 { return "CASI (1 capa falta)" }
        3 { return "A MEDIAS" }
        { $_ -le 2 } { return "INCOMPLETO" }
    }
}

foreach ($el in $elements) {
    Add-Member -InputObject $el -NotePropertyName "Level" -NotePropertyValue (Get-CompletionLevel $el) -Force
    Add-Member -InputObject $el -NotePropertyName "Score" -NotePropertyValue (
        @($el.HasXml, $el.HasBgrid, $el.HasSynHost, $el.HasAngular, $el.HasBundle) |
        Where-Object { $_ } | Measure-Object | Select-Object -ExpandProperty Count
    ) -Force
}
```

---

## 7. Agregar elementos en LOCAL_CDN que NO están en uSync

```powershell
# Bundles huérfanos — en registry pero sin ElementType en uSync
if ($reg) {
    $uSyncAliases = $elements | ForEach-Object { $_.Kebab }
    $orphanBundles = @($reg.elements) | Where-Object { $_.name -notin $uSyncAliases }
    if ($orphanBundles.Count -gt 0) {
        Write-Warning "Bundles en registry SIN ElementType en uSync:"
        $orphanBundles | ForEach-Object { Write-Warning "  ORPHAN BUNDLE: $($_.name)" }
    }
}
```

---

## 8. Reporte de inventario

```powershell
Write-Output ""
Write-Output "═══════════════════════════════════════════════════════════════════════════"
Write-Output "  SYNERGOS Element Inventory — $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Write-Output "═══════════════════════════════════════════════════════════════════════════"
Write-Output "  Total ElementTypes: $($elements.Count)"
Write-Output ""

# Por nivel
$completos    = @($elements | Where-Object { $_.Level -eq "COMPLETO" })
$casiCompleto = @($elements | Where-Object { $_.Level -like "CASI*" })
$aMedias      = @($elements | Where-Object { $_.Level -eq "A MEDIAS" })
$incompletos  = @($elements | Where-Object { $_.Level -eq "INCOMPLETO" })

Write-Output "  COMPLETOS ($($completos.Count)):"
$completos | Sort-Object Alias | ForEach-Object {
    Write-Output "    ✓ $($_.Alias)  [tier:$($_.AngularTier) v$($_.BundleVer)]"
}

if ($casiCompleto.Count -gt 0) {
    Write-Output ""
    Write-Output "  CASI COMPLETOS — falta 1 capa ($($casiCompleto.Count)):"
    $casiCompleto | Sort-Object Alias | ForEach-Object {
        $missing = @()
        if (-not $_.HasBgrid)   { $missing += "BlockGrid wrapper" }
        if (-not $_.HasSynHost) { $missing += "SynHost Razor" }
        if (-not $_.HasAngular) { $missing += "Angular project" }
        if (-not $_.HasBundle)  { $missing += "Bundle CDN" }
        Write-Output "    ⚠ $($_.Alias)  [FALTA: $($missing -join ', ')]"
    }
}

if ($aMedias.Count -gt 0) {
    Write-Output ""
    Write-Output "  A MEDIAS — faltan 2+ capas ($($aMedias.Count)):"
    $aMedias | Sort-Object Score -Descending | ForEach-Object {
        $missing = @()
        if (-not $_.HasBgrid)   { $missing += "BlockGrid" }
        if (-not $_.HasSynHost) { $missing += "SynHost" }
        if (-not $_.HasAngular) { $missing += "Angular" }
        if (-not $_.HasBundle)  { $missing += "Bundle" }
        Write-Output "    ✗ $($_.Alias)  [$($_.Score)/5] FALTA: $($missing -join ', ')"
    }
}

if ($incompletos.Count -gt 0) {
    Write-Output ""
    Write-Output "  INCOMPLETOS — solo XML ($($incompletos.Count)):"
    $incompletos | Sort-Object Alias | ForEach-Object {
        Write-Output "    ✗✗ $($_.Alias)  — solo uSync XML, sin nada más"
    }
}

Write-Output ""
Write-Output "  Resumen: $($completos.Count) completos | $($casiCompleto.Count) casi | $($aMedias.Count) a medias | $($incompletos.Count) incompletos"
Write-Output "═══════════════════════════════════════════════════════════════════════════"
```

---

## 9. Tabla detallada (para análisis profundo)

```powershell
Write-Output ""
Write-Output "Tabla detallada:"
Write-Output ("  {0,-35} {1,-5} {2,-5} {3,-6} {4,-7} {5,-6} {6}" -f "Alias","XML","BGrid","Razor","Angular","Bundle","Tier")
Write-Output ("  " + ("-" * 80))

$elements | Sort-Object Alias | ForEach-Object {
    $row = "  {0,-35} {1,-5} {2,-5} {3,-6} {4,-7} {5,-6} {6}" -f `
        $_.Alias,
        (if ($_.HasXml)     { "OK" } else { "NO" }),
        (if ($_.HasBgrid)   { "OK" } else { "NO" }),
        (if ($_.HasSynHost) { "OK" } else { "NO" }),
        (if ($_.HasAngular) { "OK" } else { "NO" }),
        (if ($_.HasBundle)  { "OK" } else { "NO" }),
        $_.AngularTier
    Write-Output $row
}
```

---

## 10. Plan de acción sugerido

Para cada elemento incompleto, el plan de remediación:

```powershell
Write-Output ""
Write-Output "Plan de acción para elementos incompletos:"

$needsWork = @($elements | Where-Object { $_.Level -ne "COMPLETO" }) | Sort-Object Score -Descending

foreach ($el in $needsWork) {
    Write-Output ""
    Write-Output "  $($el.Alias) [$($_.Level)]:"
    if (-not $el.HasBgrid)   { Write-Output "    1. Crear Views/Partials/blockgrid/Components/$($el.Alias).cshtml" }
    if (-not $el.HasSynHost) { Write-Output "    2. Crear Views/Partials/SynHost/$($el.Pascal).cshtml" }
    if (-not $el.HasAngular) { Write-Output "    3. Crear Angular project en platforms/angular/apps/elements/{tier}/$($el.Kebab)/" }
    if (-not $el.HasBundle)  { Write-Output "    4. Ejecutar /synergos-cdn-build para $($el.Kebab)" }
}
```
