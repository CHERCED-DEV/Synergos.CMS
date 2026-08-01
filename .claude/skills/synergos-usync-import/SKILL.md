---
name: synergos-usync-import
description: Guía el flujo completo de uSync Import después de que el agente escribió nuevos XMLs de schema (ContentTypes, DataTypes, Templates, Dictionary). Pre-valida XMLs, hace backup SQLite, da instrucciones neutrales de backoffice para el Import, lista los logs que confirman éxito, verifica post-import vía Management API, y diagnostica errores comunes. Usar siempre después de synergos-cms-author cuando creó schema nuevo.
model: claude-opus-4-8
---

# SYNERGOS uSync Import — importar schema al CMS

Esta skill guía el proceso de aplicar al DB de Umbraco los XMLs de schema que el agente escribió en `uSync/v9/`. El Import es la operación que transforma los archivos de texto en DocTypes, DataTypes y Templates reales dentro del CMS.

**El agente NO ejecuta el Import directamente** — Umbraco lo opera desde su backoffice o CLI. Esta skill prepara todo, pre-valida, y verifica el resultado.

---

## 0. Cuándo invocar esta skill

Siempre que `synergos-cms-author` (u otro proceso) haya escrito archivos nuevos o modificado archivos existentes en:
- `Synergos.CMS.Web/uSync/v9/ContentTypes/`
- `Synergos.CMS.Web/uSync/v9/DataTypes/`
- `Synergos.CMS.Web/uSync/v9/Templates/`
- `Synergos.CMS.Web/uSync/v9/Dictionary/`
- `Synergos.CMS.Web/uSync/v9/MediaTypes/`

Si solo se creó **Content** (nodos editoriales), NO se necesita Import — el contenido ya está en el DB vía Management API.

---

## 1. Backup SQLite (siempre primero)

Antes de cualquier Import, hacer backup del DB. Un Import que falla a mitad puede dejar el schema inconsistente.

```powershell
$dbPath     = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
$backupDir  = "C:\Users\HITMA\Desktop\synergos-backups"
$timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = "$backupDir\Umbraco-pre-import-$timestamp.sqlite.db"

if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory $backupDir | Out-Null }

if (Test-Path $dbPath) {
    Copy-Item $dbPath $backupPath -Force
    $sizeMB = [Math]::Round((Get-Item $backupPath).Length / 1MB, 2)
    Write-Output "Backup OK: $backupPath ($sizeMB MB)"
} else {
    Write-Warning "DB no encontrada en $dbPath — es posible que el CMS no haya arrancado aún."
    Write-Warning "Arrancar el CMS primero (ver synergos-run-dev) para crear la DB inicial."
}
```

**Nota:** Los backups van en `C:\Users\HITMA\Desktop\synergos-backups\` — NUNCA dentro del repo. La DB no se commitea.

---

## 2. Pre-validar los XMLs nuevos

Antes del Import, verificar que los archivos escritos son válidos:

```powershell
$uSyncRoot = "Synergos.CMS\Synergos.CMS.Web\uSync\v9"

# 2A. Identificar archivos modificados recientemente (último commit o últimas 2 horas)
$recentFiles = Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
    Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-2) } |
    Sort-Object LastWriteTime -Descending

Write-Output "Archivos modificados recientemente ($($recentFiles.Count)):"
$recentFiles | ForEach-Object { Write-Output "  $($_.FullName)" }
```

```powershell
# 2B. Validar XML bien formado
$xmlErrors = @()
foreach ($file in $recentFiles) {
    try {
        [xml]$xml = Get-Content $file.FullName -Encoding UTF8
        # Verificar elemento raíz esperado
        $root = $xml.DocumentElement.LocalName
        $validRoots = @("ContentType", "DataType", "Template", "Language", "MediaType")
        if ($root -notin $validRoots) {
            $xmlErrors += "$($file.Name): raíz inesperada '$root'"
        }
    } catch {
        $xmlErrors += "$($file.Name): XML malformado — $($_.Exception.Message)"
    }
}

if ($xmlErrors) {
    Write-Error "XMLs con problemas:"
    $xmlErrors | ForEach-Object { Write-Error "  $_" }
    exit 1
}
Write-Output "Todos los XMLs son XML bien formado. OK"
```

```powershell
# 2C. Verificar GUIDs únicos (quad-check de los nuevos contra los existentes)
$allGuids = [System.Collections.Generic.HashSet[string]]::new()
$collisions = @()

Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $guidMatches = [regex]::Matches($content, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}')
    foreach ($m in $guidMatches) {
        $g = $m.Value.ToLower()
        if (-not $allGuids.Add($g)) {
            # Este GUID ya apareció — puede ser collision o referencia legítima
            # Solo reportar si aparece en el Key/Definition de los archivos NUEVOS
        }
    }
}

Write-Output "GUID scan completado — $($allGuids.Count) GUIDs únicos en todo uSync"

# Verificar específicamente los Keys de los nuevos archivos
foreach ($file in $recentFiles) {
    [xml]$xml  = Get-Content $file.FullName -Encoding UTF8
    $keyAttr   = $xml.DocumentElement.Attributes["Key"]?.Value
    if ($keyAttr) {
        $dup = Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
            Where-Object { $_.FullName -ne $file.FullName } |
            Select-String -Pattern $keyAttr -SimpleMatch
        if ($dup) {
            $collisions += "COLLISION: Key $keyAttr de $($file.Name) también aparece en: $($dup.Path)"
        }
    }
}

if ($collisions) {
    Write-Error "Colisiones de GUID encontradas:"
    $collisions | ForEach-Object { Write-Error "  $_" }
    Write-Error "Detener. Reasignar GUIDs frescos en los archivos afectados."
    exit 1
}
Write-Output "Sin colisiones de GUID en archivos nuevos. OK"
```

```powershell
# 2D. Verificar encodings (deben ser UTF-8)
foreach ($file in $recentFiles) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    # UTF-8 BOM: EF BB BF
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    # UTF-16 LE BOM: FF FE
    $isUtf16 = ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE)
    if ($isUtf16) {
        Write-Warning "$($file.Name): encoding UTF-16 detectado — puede causar mojibake en Umbraco."
        Write-Warning "Reescribir con: [IO.File]::WriteAllText(path, content, [Text.Encoding]::UTF8)"
    }
    # UTF-8 sin BOM también es aceptado
}
Write-Output "Encodings verificados. OK"
```

---

## 3. Listar qué va a cambiar

Antes del Import, describir al arquitecto exactamente qué tipos se van a crear/modificar:

```powershell
Write-Output ""
Write-Output "════════════════════════════════════════════"
Write-Output "  CAMBIOS A IMPORTAR"
Write-Output "════════════════════════════════════════════"

$recentFiles | ForEach-Object {
    [xml]$xml  = Get-Content $_.FullName -Encoding UTF8
    $root      = $xml.DocumentElement.LocalName
    $key       = $xml.DocumentElement.Attributes["Key"]?.Value
    $alias     = $xml.DocumentElement.Attributes["Alias"]?.Value
    $name      = $xml.DocumentElement.Info?.Name

    switch ($root) {
        "ContentType" {
            $isElem = $xml.ContentType.Info.IsElement -eq "true"
            $type   = if ($isElem) { "ElementType" } else { "DocumentType" }
            Write-Output "  + $type: $alias  (Key: $key)"
        }
        "DataType" {
            $editor = $xml.DocumentElement.Attributes["EditorAlias"]?.Value
            Write-Output "  + DataType: $alias [$editor]  (Key: $key)"
        }
        "Template" {
            Write-Output "  + Template: $alias  (Key: $key)"
        }
        default {
            Write-Output "  + $root: $alias  (Key: $key)"
        }
    }
}
Write-Output "════════════════════════════════════════════"
```

---

## 4. Instrucciones de Import para el arquitecto

El arquitecto ejecuta el Import desde el backoffice o desde CLI. Las instrucciones son **neutrales** — sin path UI exacto (cambia entre versiones de Umbraco 13.x):

### Vía backoffice (recomendado)

```
1. Abrir http://synergos.local:5000/umbraco/
   Usuario: admin@synergos.local  |  Password: Synergos2026!

2. Navegar a la sección de configuración de uSync.
   En Umbraco 13, está en el árbol de Settings bajo "uSync" o accesible
   desde el ícono de configuración en la barra lateral.

3. Verificar que muestra la lista de tipos con cambios pendientes
   (icono de diferencia o indicador de "out of sync").

4. Ejecutar un Import de tipo "no-destructivo" primero:
   - Seleccionar "Report" o "Check" si está disponible para ver el diff
   - Luego "Import" (sin borrar los tipos que no están en uSync)

5. Confirmar el Import cuando se solicite.

6. Esperar a que el progreso complete — puede tardar 10-30 segundos
   dependiendo de cuántos tipos cambien.
```

### Vía `dotnet umbraco-usync` CLI (alternativa)

Si el `USync.Community.AutoImport` o similar está instalado, puede haber un comando CLI. Verificar con:

```bash
dotnet run -- usync import --all
```

Pero en la configuración actual del proyecto, el Import vía backoffice es el método canónico.

---

## 5. Logs que confirman éxito

Monitorear la salida del proceso `dotnet run` durante y después del Import:

### Señales de éxito ✓
```
info: uSync.Core[0]
    ContentType: Imported elementSynPricingCard [CREATE]

info: uSync.Core[0]
    DataType: Imported DTSelectRatingScale [CREATE]

info: uSync.Core[0]
    uSync Import Complete: X items imported, 0 errors
```

### Señales de advertencia (no detienen el Import, pero hay que revisar)
```
warn: uSync.Core[0]
    ContentType: elementSynPricingCard — IsElement flag cannot be changed after creation
    # → Esperado si se cambió IsElement de false→true en un tipo existente. No rompe nada.

warn: uSync.Core[0]
    Template: Skipping [no changes detected]
    # → Normal para templates que no cambiaron.
```

### Señales de error ✗ (detienen o fallan parcialmente)
```
error: uSync.Core[0]
    ContentType: UNIQUE constraint failed on Key xxx
    # → GUID collision. Detener, reasignar GUID en el XML afectado.

error: uSync.Core[0]
    DataType: Failed to import — EditorAlias 'Umbraco.DropDown.Flexible' not found
    # → Raro. El editor alias está mal escrito en el XML.

error: uSync.Core[0]
    ContentType: Composition Key {guid} not found
    # → Una composition referenciada en el nuevo tipo no existe aún.
    # → Importar la composition primero.
```

---

## 6. Verificación post-import vía Management API

Después de que el arquitecto confirme que el Import completó:

```powershell
$baseUrl = "http://synergos.local:5000"

# Auth
$auth    = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/security/back-office/token" `
    -Method POST -ContentType "application/x-www-form-urlencoded" `
    -Body "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
$headers = @{ "Authorization" = "Bearer $($auth.access_token)" }

# Verificar cada tipo importado
$importedAliases = $recentFiles | ForEach-Object {
    [xml]$xml = Get-Content $_.FullName -Encoding UTF8
    $xml.DocumentElement.Attributes["Alias"]?.Value
} | Where-Object { $_ }

Write-Output "Verificando tipos importados vía API..."

foreach ($alias in $importedAliases) {
    try {
        # GET document-type por alias
        $types = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/document-type?skip=0&take=200" `
            -Headers $headers
        $found = $types.items | Where-Object { $_.alias -eq $alias } | Select-Object -First 1

        if ($found) {
            Write-Output "  ✓ $alias — Key: $($found.id)"
        } else {
            # Intentar como data-type
            $dtypes = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/data-type?skip=0&take=200" `
                -Headers $headers
            $dtFound = $dtypes.items | Where-Object { $_.alias -eq $alias } | Select-Object -First 1

            if ($dtFound) {
                Write-Output "  ✓ $alias (DataType) — Key: $($dtFound.id)"
            } else {
                Write-Warning "  ✗ $alias — NO encontrado en la API. El Import puede no haber aplicado."
            }
        }
    } catch {
        Write-Warning "  ? $alias — Error al verificar: $($_.Exception.Message)"
    }
}
```

---

## 7. Casos especiales y errores comunes

### Error: UNIQUE constraint failed (GUID collision)

**Causa:** Dos archivos uSync tienen el mismo Key GUID o el GUID ya existe en el DB.

**Solución:**
```powershell
# Encontrar el archivo afectado
$collidingGuid = "{guid-del-error}"
Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
    Select-String -Pattern $collidingGuid -SimpleMatch |
    Select-Object Path, LineNumber, Line

# Generar nuevo GUID y reemplazar en el archivo
$newGuid  = [guid]::NewGuid().ToString()
$filePath = "{ruta-del-archivo-conflictivo}"
$content  = Get-Content $filePath -Raw
$content  = $content.Replace($collidingGuid, $newGuid)
[System.IO.File]::WriteAllText($filePath, $content, [System.Text.Encoding]::UTF8)
Write-Output "GUID reemplazado: $collidingGuid → $newGuid"
# Repetir quad-check (§2C) antes de intentar el Import de nuevo
```

### Error: Composition no encontrada

**Causa:** El nuevo ElementType referencia una Composition por Key que no existe aún en el DB.

**Solución:** Importar las compositions primero. El Import debe hacerse en orden de dependencia:
1. DataTypes nuevos
2. Compositions nuevas (`comp*.config`)
3. ElementTypes que dependen de esas compositions
4. Page types que dependen de los ElementTypes

Si el backoffice no permite orden manual, revisar si uSync lo maneja automáticamente (en Umbraco 13 uSync resuelve dependencias). Si no: importar en dos pasadas.

### Error: IsElement inmutable

**Causa:** Se cambió `<IsElement>false</IsElement>` → `<IsElement>true</IsElement>` en un ContentType ya existente.

**Comportamiento esperado:** El log dice "cannot be changed" pero el Import continúa. El tipo FUNCIONA en modo untyped (Razor accede a propiedades via `Model.Content.Value<T>("alias")`). ModelsBuilder puede quejarse pero "Running without models" no rompe nada en producción.

**No intentar:** Borrar y recrear el tipo para forzar el cambio — se pierden los contenidos existentes.

### Error: Template no encontrada

**Causa:** Un ContentType tiene `<DefaultTemplate>` que referencia una Template que no existe aún.

**Solución:** Importar primero el Template XML (`Templates/` folder), luego el ContentType.

### Advertencia: "No changes detected"

Si uSync dice que todos los tipos están "in sync" y no aplica nada, verificar:
1. El archivo fue guardado correctamente (encoding UTF-8, no UTF-16)
2. El Key del XML coincide con el tipo existente en DB (si es actualización, deben coincidir)
3. El `<Level>` es correcto

---

## 8. Orden recomendado para imports complejos

Si se crearon muchos tipos en una sola ola:

```
1. DataTypes nuevos       (DTSelect*, DTBlockList*)
2. Compositions nuevas    (comp*.config) — sin dependencies externas
3. ElementTypes simples   (elementSyn* sin referencias a otros elements)
4. ElementTypes complejos (los que tienen BlockList que referencia otros elements)
5. Page types             (page*.config)
6. Templates              (si son nuevas)
7. Dictionary keys        (si se agregaron)
```

uSync 13 generalmente resuelve el orden solo, pero en casos de dependencias circulares puede necesitar dos pasadas.

---

## 9. Post-import: regenerar Models (si aplica)

Si `ModelsBuilder:ModelsMode = "SourceCodeAuto"`, Umbraco regenera los modelos automáticamente después del Import. Verificar en los logs:

```
info: Umbraco.Cms.Infrastructure.ModelsBuilder.Building.PureLiveModelFactory[0]
    Generating models
info: Umbraco.Cms.Infrastructure.ModelsBuilder.Building.PureLiveModelFactory[0]
    Models generated (X types)
```

Si no regenera automáticamente, hacer un touch a cualquier archivo de configuración para forzar reload, o reiniciar el CMS.

---

## 10. Reporte final post-import

```
════════════════════════════════════════════════════
  SYNERGOS uSync Import — Completado
════════════════════════════════════════════════════
  Backup:     C:\...\synergos-backups\Umbraco-pre-import-{timestamp}.sqlite.db
  XMLs validados: {N} archivos OK
  GUIDs verificados: 0 colisiones
  Import ejecutado: ✓ (por el arquitecto desde backoffice)
  Verificación API:
    ✓ elementSyn{Name} — encontrado, Key: {guid}
    ✓ DTSelect{Name}   — encontrado, Key: {guid}

  Siguiente paso:
    → Si es elementSyn*: crear Razor views (§5 de synergos-cms-author)
    → Si es elementSyn*: crear Angular component y publicar (synergos-cdn-build)
    → Si es solo DataType/Composition: listo para usar en otros tipos
════════════════════════════════════════════════════
```

---

## 11. Rollback (si algo salió muy mal)

Si el Import dejó el schema inconsistente y el CMS no arranca correctamente:

```powershell
# 1. Detener el CMS
# 2. Restaurar el backup
$latestBackup = Get-ChildItem "C:\Users\HITMA\Desktop\synergos-backups" "*.sqlite.db" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dbPath = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"

Copy-Item $latestBackup.FullName $dbPath -Force
Write-Output "DB restaurada desde: $($latestBackup.FullName)"

# 3. Revertir los XMLs con git
# git checkout -- Synergos.CMS/Synergos.CMS.Web/uSync/v9/

# 4. Arrancar el CMS de nuevo
```
