---
name: synergos-ola-open
description: Abre una nueva Ola de desarrollo de Synergos — determina el número siguiente, define el alcance y entregables, identifica los ADRs que se crearán, ejecuta un health check inicial, hace backup del DB, y prepara el contexto completo para que synergos-cms-author y synergos-usync-author trabajen con información completa. Invocar al inicio de cada ciclo de trabajo nuevo.
model: claude-opus-4-8
---

# SYNERGOS Ola Open — abrir una nueva Ola de desarrollo

Una Ola en Synergos es el ciclo de trabajo atómico: abre con un contexto definido, termina con todo committeado y documentado. Esta skill prepara ese contexto.

---

## 1. Determinar el número de Ola siguiente

```powershell
# Buscar el número de Ola más alto en los commits recientes
$lastOlaCommit = git log --oneline --all | Select-String -Pattern 'ola-(\d+)' |
    ForEach-Object {
        if ($_.Matches[0].Groups[1].Value -match '^\d+$') {
            [int]$_.Matches[0].Groups[1].Value
        }
    } | Sort-Object -Descending | Select-Object -First 1

# También buscar en los docs
$lastOlaDoc = Get-ChildItem "refactor-docs\architecture" -Filter "*.md" -Recurse |
    Select-String -Pattern 'OLA-(\d+)' |
    ForEach-Object { [int]$_.Matches[0].Groups[1].Value } |
    Sort-Object -Descending | Select-Object -First 1

$lastOla  = [Math]::Max($lastOlaCommit, $lastOlaDoc)
$nextOla  = $lastOla + 1
$olaId    = "OLA-$($nextOla.ToString('D3'))"

Write-Output "Última Ola detectada: OLA-$lastOla"
Write-Output "Próxima Ola: $olaId"
```

---

## 2. Definir el alcance de la Ola

El alcance debe responder:
- ¿Qué schema nuevo se crea? (ElementTypes, DataTypes, Compositions)
- ¿Qué Razor views se necesitan?
- ¿Qué componentes Angular se crean o modifican?
- ¿Qué contenido editorial se crea?
- ¿Qué ADRs se abrirán?
- ¿Hay cambios a seams existentes que requieren tests?

**Template de contexto de Ola:**

```markdown
# {OLA-NNN} — {Título}

## Objetivo
{Qué se quiere lograr al final de esta Ola — en 1-2 frases.}

## Entregables

### Schema (uSync XMLs)
- [ ] ElementType: elementSyn{Name} — {descripción}
- [ ] DataType: DT{Name} — {descripción}
- [ ] Composition: comp{Name} — {descripción}

### Razor
- [ ] SynHost/{Name}.cshtml
- [ ] blockgrid/Components/elementSyn{Name}.cshtml

### Angular
- [ ] {tier}/{name} — {descripción del componente}

### Bundles CDN
- [ ] registry.json actualizado
- [ ] LOCAL_CDN/{name}/angular/0.1.0/main.js publicado

### Contenido editorial
- [ ] {Tipo de contenido}: {descripción de los nodos a crear}

### ADRs
- [ ] ADR-{NNNN}: {Título} — {razón}

### Tests
- [ ] Tests para {seam}: empty, happy, filter, idempotent

## Restricciones / dependencias
{Cualquier restricción conocida: ADRs que deben respetarse, composiciones que existen,
GUIDs que NO deben reutilizarse, etc.}
```

---

## 3. Health check inicial

Verificar el estado del stack antes de empezar:

```powershell
# Ping CMS
$cmsOk = $false
try {
    Invoke-WebRequest "http://synergos.local:5000/umbraco/api/keepalive/ping" `
        -UseBasicParsing -TimeoutSec 5 | Out-Null
    $cmsOk = $true
    Write-Output "CMS: OK"
} catch {
    Write-Warning "CMS: NO responde — ejecutar synergos-run-dev antes de empezar"
}

# Registry
try {
    $reg   = Get-Content "C:\LOCAL_CDN\synergos\registry.json" | ConvertFrom-Json
    $count = @($reg.elements).Count
    Write-Output "Bundle registry: OK — $count elementos"
} catch {
    Write-Warning "Bundle registry: NO legible"
}

# DB
$dbPath = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
if (Test-Path $dbPath) {
    $sizeMB = [Math]::Round((Get-Item $dbPath).Length / 1MB, 2)
    Write-Output "DB: OK — $sizeMB MB"
} else {
    Write-Warning "DB: NO encontrada — primer arranque o DB no creada aún"
}
```

---

## 4. Backup de apertura

```powershell
$backupDir  = "C:\Users\HITMA\Desktop\synergos-backups"
$timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = "$backupDir\Umbraco-ola-open-$olaId-$timestamp.sqlite.db"

if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory $backupDir | Out-Null }

if (Test-Path $dbPath) {
    Copy-Item $dbPath $backupPath -Force
    $sizeMB = [Math]::Round((Get-Item $backupPath).Length / 1MB, 2)
    Write-Output "Backup de apertura: $backupPath ($sizeMB MB)"
} else {
    Write-Warning "Sin DB para respaldar — Ola sin backup de apertura"
}
```

---

## 5. Inventario inicial (snapshot del estado actual)

Antes de hacer cualquier cambio, registrar el estado inicial:

```powershell
$uSyncCT = "Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes"
$uSyncDT = "Synergos.CMS\Synergos.CMS.Web\uSync\v9\DataTypes"

# Contar elementos actuales
$elementCount   = (Get-ChildItem $uSyncCT -Filter "elementSyn*.config" -Recurse).Count
$compCount      = (Get-ChildItem $uSyncCT -Filter "comp*.config" -Recurse).Count
$dtCount        = (Get-ChildItem $uSyncDT -Filter "*.config" -Recurse).Count
$ctTotal        = (Get-ChildItem $uSyncCT -Filter "*.config" -Recurse).Count
$bundleCount    = try { @((Get-Content "C:\LOCAL_CDN\synergos\registry.json" | ConvertFrom-Json).elements).Count } catch { 0 }

Write-Output ""
Write-Output "══════════════════════════════════════════════"
Write-Output "  Estado inicial para $olaId"
Write-Output "══════════════════════════════════════════════"
Write-Output "  ContentTypes totales : $ctTotal"
Write-Output "  ElementTypes (elementSyn*): $elementCount"
Write-Output "  Compositions (comp*)  : $compCount"
Write-Output "  DataTypes             : $dtCount"
Write-Output "  Bundles en registry   : $bundleCount"
Write-Output "══════════════════════════════════════════════"
Write-Output "  Al cerrar la Ola, estos números cambiarán."
Write-Output "══════════════════════════════════════════════"
```

---

## 6. Verificar el último commit

```powershell
Write-Output "Último commit:"
git log --oneline -5

Write-Output ""
Write-Output "Estado del working tree:"
git status --short
```

Si hay archivos sin commitear del trabajo anterior: completar/commitear antes de abrir la nueva Ola.

---

## 7. Contexto para skills subsiguientes

Al abrir la Ola, proporcionar este contexto a `synergos-cms-author` y `synergos-usync-author`:

```
Ola activa: {OLA-NNN}
Objetivo: {una línea}
ADRs a respetar en esta Ola: ADR-0001 (Umbraco 13), ADR-0008 (uSync source of truth),
  ADR-0010 (IBrandingProvider), ADR-0012 (IBundleRegistryClient), ADR-0013 (no seeders),
  + cualquier ADR específico al dominio de esta Ola

Naming para esta Ola:
  ElementType alias: elementSyn{Name}
  Angular tier: {tier basado en la naturaleza del componente}
  GUID quad-check: obligatorio antes de asignar

Estado inicial:
  {N} ElementTypes, {N} Compositions, {N} DataTypes, {N} bundles
```

---

## 8. Reporte de apertura

```
══════════════════════════════════════════════════════
  SYNERGOS — Apertura de {OLA-NNN}
══════════════════════════════════════════════════════
  Título    : {título}
  Fecha     : {fecha}
  Backup    : {path del backup de apertura}

  Stack:
    CMS     : {OK / NO CORRIENDO}
    Registry: {N} elementos
    DB      : {N} MB

  Entregables planificados:
    Schema  : {N} tipos nuevos
    Razor   : {N} views
    Angular : {N} componentes
    ADRs    : {N} nuevos

  ADRs que rigen esta Ola:
    {lista de ADRs relevantes}

  Listo para comenzar.
  Próximos pasos: /synergos-cms-author o /synergos-usync-author
══════════════════════════════════════════════════════
```

---

## 9. Convenciones de Ola — recordatorio

| Regla | Detalle |
|-------|---------|
| GUIDs | Quad-check obligatorio antes de asignar cualquier GUID nuevo |
| Commits | Atómicos por tipo de archivo (XMLs separados de C#, separados de Razor) |
| DB | No se commitea (`Umbraco.sqlite.db` en .gitignore) |
| Backups | En `C:\Users\HITMA\Desktop\synergos-backups\` — fuera del repo |
| Encoding | UTF-8 sin BOM para todos los XMLs |
| IsElement | Inmutable post-creación — planificar antes de crear |
| Storage type | Key nueva si cambia el Type de un DataType existente |
| Seeders | Prohibidos en boot (ADR 0013) |
| Code-first | Prohibido para schema (ADR 0008) |
| Multi-culture | Variations=Culture por defecto en todos los tipos y propiedades |
