---
name: synergos-db-ops
description: Operaciones seguras sobre la base de datos SQLite de Umbraco (Umbraco.sqlite.db). Protocolo obligatorio — stop CMS → checkpoint WAL → backup → operar → integrity check → restart. Cubre lectura segura, consultas de diagnóstico, corrección de datos, backup y restore. NO usar para cambios de schema — eso es territorio de uSync (synergos-usync-author + synergos-usync-import).
model: claude-opus-4-8
---

# SYNERGOS DB Ops — operaciones SQLite seguras

La base de datos SQLite de Umbraco NO se toca mientras el CMS está corriendo, salvo en lecturas de solo diagnóstico con las precauciones indicadas. Todo cambio de datos requiere el protocolo completo de esta skill.

**Regla de oro:** Para cambios de **schema** (DocTypes, DataTypes, Templates), usar uSync XMLs — nunca SQL directo. Esta skill es para **datos** (content nodes, property values, members, audit logs).

---

## 0. Rutas canónicas

```
DB principal   : Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db
WAL file       : Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db-wal
SHM file       : Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db-shm
Backups        : C:\Users\HITMA\Desktop\synergos-backups\
```

La DB NO se commitea al repo. Los backups van siempre en `synergos-backups\` — nunca dentro del árbol del repo.

---

## 1. Clasificación de operaciones

| Tipo | Riesgo | Requisito |
|------|--------|-----------|
| **Lectura vía Management API** | Ninguno | CMS corriendo |
| **Lectura SQL diagnóstico** | Bajo | CMS puede estar corriendo — usar WAL reader |
| **Lectura SQL directa** | Medio | Preferir CMS detenido; usar con cuidado |
| **Escritura SQL** | ALTO | **CMS detenido + backup obligatorio** |
| **Restore desde backup** | MUY ALTO | **CMS detenido + verificación post-restore** |

**Primera opción siempre:** usar la Management API (`/umbraco/management/api/v1/`) para leer datos. Solo bajar a SQL directo cuando la API no expone lo que se necesita.

---

## 2. Verificar estado del CMS

```powershell
function Get-CmsState {
    try {
        $r = Invoke-WebRequest "http://synergos.local:5000/umbraco/api/keepalive/ping" `
            -UseBasicParsing -TimeoutSec 3
        return @{ Running = $true; StatusCode = $r.StatusCode }
    } catch {
        return @{ Running = $false; Error = $_.Exception.Message }
    }
}

$state = Get-CmsState
if ($state.Running) {
    Write-Output "CMS está corriendo — status $($state.StatusCode)"
} else {
    Write-Output "CMS está detenido"
}
```

---

## 3. Detener el CMS de forma segura

Antes de cualquier escritura SQL o restore:

```powershell
function Stop-CmsSafe {
    param([int]$TimeoutSeconds = 30)

    $port5000 = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
    if (-not $port5000) {
        Write-Output "CMS ya estaba detenido."
        return
    }

    $pid5000 = $port5000.OwningProcess | Select-Object -First 1
    Write-Output "Deteniendo CMS (PID $pid5000)..."

    # Señal de cierre graceful primero
    Stop-Process -Id $pid5000 -ErrorAction SilentlyContinue

    # Esperar hasta $TimeoutSeconds
    $elapsed = 0
    while ($elapsed -lt $TimeoutSeconds) {
        $still = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
        if (-not $still) {
            Write-Output "CMS detenido correctamente en $elapsed s."
            return
        }
        Start-Sleep -Seconds 2
        $elapsed += 2
    }

    # Forzar si no cedió
    Write-Warning "CMS no se detuvo en $TimeoutSeconds s — forzando."
    Stop-Process -Id $pid5000 -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    $final = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue
    if ($final) {
        Write-Error "No se pudo detener el CMS. Abortar operación."
        exit 1
    }
    Write-Output "CMS forzado a detención."
}
```

---

## 4. Checkpoint WAL antes de backup

Umbraco usa SQLite en WAL (Write-Ahead Log) mode. Antes de copiar la DB, hacer checkpoint para que el WAL se consolide en el archivo principal:

```powershell
function Invoke-SqliteCheckpoint {
    param([string]$DbPath)

    # Buscar sqlite3.exe en PATH o en ubicaciones comunes
    $sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if (-not $sqlite3) {
        # Intentar ubicación de Chocolatey o winget
        $candidates = @(
            "C:\ProgramData\chocolatey\bin\sqlite3.exe",
            "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\SQLite.SQLite_Microsoft.Winget.Source_8wekyb3d8bbwe\sqlite3.exe"
        )
        $sqlite3 = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    } else {
        $sqlite3 = $sqlite3.Source
    }

    if ($sqlite3) {
        & $sqlite3 $DbPath "PRAGMA wal_checkpoint(FULL);" 2>&1 | Write-Output
        Write-Output "WAL checkpoint completado."
    } else {
        Write-Warning "sqlite3.exe no encontrado — checkpoint WAL omitido."
        Write-Warning "El backup puede estar incompleto si hay transacciones en WAL."
        Write-Warning "Instalar con: winget install SQLite.SQLite"
    }
}
```

---

## 5. Backup (siempre antes de escribir)

```powershell
function Backup-SynergosSqlite {
    param([string]$Reason = "pre-operation")

    $dbPath    = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
    $backupDir = "C:\Users\HITMA\Desktop\synergos-backups"
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $slug      = $Reason -replace '[^a-zA-Z0-9]', '-'
    $backupPath = "$backupDir\Umbraco-$slug-$timestamp.sqlite.db"

    if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory $backupDir | Out-Null }
    if (-not (Test-Path $dbPath))    { Write-Warning "DB no existe: $dbPath"; return $null }

    # Checkpoint WAL antes de copiar
    Invoke-SqliteCheckpoint $dbPath

    # Copiar archivo principal + WAL/SHM si existen
    Copy-Item $dbPath $backupPath -Force
    @("$dbPath-wal", "$dbPath-shm") | Where-Object { Test-Path $_ } | ForEach-Object {
        Copy-Item $_ "$backupPath$($_ -replace [regex]::Escape($dbPath))" -Force
    }

    $sizeMB = [Math]::Round((Get-Item $backupPath).Length / 1MB, 2)
    Write-Output "Backup creado: $backupPath ($sizeMB MB)"
    return $backupPath
}

# Uso:
# $backupPath = Backup-SynergosSqlite -Reason "pre-import-ola-310"
```

---

## 6. Herramienta de acceso SQL

### Opción A — sqlite3 CLI (recomendado si disponible)

```powershell
$dbPath = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"

# Consulta simple
sqlite3 $dbPath "SELECT nodeId, text, nodeObjectType FROM umbracoNode LIMIT 20;"

# Modo tabla
sqlite3 $dbPath -column -header "SELECT * FROM umbracoContent LIMIT 10;"

# Exportar a CSV
sqlite3 $dbPath -separator ',' "SELECT * FROM cmsPropertyData LIMIT 100;" > output.csv
```

### Opción B — Microsoft.Data.Sqlite vía .NET (sin sqlite3 CLI)

```powershell
# Los DLLs de SQLite ya están en el NuGet cache de Umbraco
$nugetPath = "$env:USERPROFILE\.nuget\packages\microsoft.data.sqlite.core"
$dllPath   = Get-ChildItem $nugetPath -Recurse "Microsoft.Data.Sqlite.dll" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $dllPath) {
    Write-Error "Microsoft.Data.Sqlite.dll no encontrado. Asegurarse de haber hecho dotnet restore."
    exit 1
}

# También necesita SQLitePCLRaw
$rawPath = Get-ChildItem "$env:USERPROFILE\.nuget\packages\sqlitepclraw.lib.e_sqlite3" `
    -Recurse "e_sqlite3.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like "*win-x64*" -or $_.DirectoryName -like "*runtimes*" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($rawPath) { [System.Runtime.InteropServices.NativeLibrary]::Load($rawPath.FullName) }

Add-Type -Path $dllPath.FullName

function Invoke-SqliteQuery {
    param(
        [string]$DbPath,
        [string]$Query,
        [switch]$Write
    )

    $connString = "Data Source=$DbPath;Mode=$(if ($Write) {'ReadWrite'} else {'ReadOnly'})"
    $conn = [Microsoft.Data.Sqlite.SqliteConnection]::new($connString)
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = $Query

        if ($Query.TrimStart().ToUpper() -match '^SELECT') {
            $reader  = $cmd.ExecuteReader()
            $results = @()
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $row[$reader.GetName($i)] = $reader.GetValue($i)
                }
                $results += [PSCustomObject]$row
            }
            $reader.Close()
            return $results
        } else {
            $affected = $cmd.ExecuteNonQuery()
            Write-Output "Filas afectadas: $affected"
        }
    } finally {
        $conn.Close()
        $conn.Dispose()
    }
}
```

---

## 7. Consultas de diagnóstico frecuentes

Estas son seguras con el CMS corriendo (solo lectura):

```powershell
$db = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"

# 7A. Ver todos los DocTypes en el DB
sqlite3 $db -column -header @"
SELECT n.uniqueId, n.text AS alias, ct.alias, ct.isElement
FROM umbracoNode n
JOIN cmsContentType ct ON ct.nodeId = n.id
WHERE n.nodeObjectType = 'A2CB7800-F571-4787-9638-BC48539A0EFB'
ORDER BY n.text;
"@

# 7B. Ver propiedades de un DocType específico
sqlite3 $db -column -header @"
SELECT pt.alias, pt.Name, dt.propertyEditorAlias, pt.mandatory
FROM cmsPropertyType pt
JOIN umbracoDataType dt ON dt.nodeId = pt.dataTypeId
JOIN cmsContentType ct ON ct.nodeId = pt.contentTypeId
WHERE ct.alias = 'elementSynHeroBanner'
ORDER BY pt.sortOrder;
"@

# 7C. Ver content nodes publicados
sqlite3 $db -column -header @"
SELECT n.id, n.text, ct.alias AS docType, cv.published
FROM umbracoNode n
JOIN umbracoContent c  ON c.nodeId = n.id
JOIN cmsContentType ct ON ct.nodeId = c.contentTypeId
JOIN umbracoDocumentVersion cv ON cv.id = (
    SELECT MAX(id) FROM umbracoDocumentVersion WHERE nodeId = n.id
)
WHERE n.nodeObjectType = 'C66BA18E-EAF3-4CFF-8A22-41B16D66A972'
ORDER BY n.sortOrder
LIMIT 50;
"@

# 7D. Ver property data de un nodo
sqlite3 $db -column -header @"
SELECT pt.alias, pd.textValue, pd.dateValue, pd.intValue
FROM cmsPropertyData pd
JOIN cmsPropertyType pt ON pt.id = pd.propertytypeId
JOIN umbracoContentVersion cv ON cv.id = pd.versionId
WHERE cv.nodeId = 1234  -- reemplazar con el nodeId real
  AND cv.current = 1
ORDER BY pt.alias;
"@

# 7E. Ver tamaño de tablas (útil para diagnóstico de performance)
sqlite3 $db @"
SELECT name, SUM(pgsize) AS size_bytes
FROM dbstat
GROUP BY name
ORDER BY size_bytes DESC
LIMIT 20;
"@

# 7F. Integridad de la DB
sqlite3 $db "PRAGMA integrity_check;"

# 7G. GUIDs de todos los DataTypes
sqlite3 $db -column -header @"
SELECT n.uniqueId, n.text, dt.propertyEditorAlias, dt.dbType
FROM umbracoNode n
JOIN umbracoDataType dt ON dt.nodeId = n.id
WHERE n.nodeObjectType = '30A2A501-1978-4DDB-A57B-F7EFED43BA3C'
ORDER BY n.text;
"@
```

---

## 8. Operaciones de escritura seguras

**Protocolo obligatorio para cualquier escritura:**

```powershell
# PASO 1: Verificar que el CMS está detenido
$state = Get-CmsState
if ($state.Running) {
    Write-Warning "CMS está corriendo. Detener antes de escribir."
    Stop-CmsSafe
}

# PASO 2: Backup
$backup = Backup-SynergosSqlite -Reason "pre-{descripcion-de-la-operacion}"

# PASO 3: Ejecutar la operación
# Ejemplo: corregir un valor de propiedad
$query = @"
UPDATE cmsPropertyData
SET textValue = 'valor correcto'
WHERE propertytypeId = (
    SELECT id FROM cmsPropertyType WHERE alias = 'heading'
) AND versionId = (
    SELECT id FROM umbracoContentVersion
    WHERE nodeId = 1234 AND current = 1
);
"@

Invoke-SqliteQuery -DbPath $db -Query $query -Write

# PASO 4: Verificar integridad
$integrity = Invoke-SqliteQuery -DbPath $db -Query "PRAGMA integrity_check;"
if ($integrity[0]."integrity_check" -ne "ok") {
    Write-Error "Integridad comprometida después de la escritura. Restaurar backup."
    # Ver §9 Restore
} else {
    Write-Output "Integridad OK post-escritura."
}

# PASO 5: Reiniciar el CMS
Write-Output "Reiniciando CMS..."
# Ver synergos-run-dev §3 para arrancar
```

---

## 9. Restore desde backup

Solo en emergencia — cuando el CMS no arranca o el DB está corrupto:

```powershell
function Restore-SynergosSqlite {
    param([string]$BackupPath)

    if (-not (Test-Path $BackupPath)) {
        Write-Error "Backup no encontrado: $BackupPath"
        exit 1
    }

    # 1. Asegurar que el CMS está detenido
    Stop-CmsSafe

    $dbPath  = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"
    $walPath = "$dbPath-wal"
    $shmPath = "$dbPath-shm"

    # 2. Backup del estado actual (por si acaso)
    $emergencyBackup = Backup-SynergosSqlite -Reason "pre-restore-emergency"
    Write-Output "Estado actual preservado en: $emergencyBackup"

    # 3. Eliminar WAL/SHM actuales (pueden estar corruptos)
    @($walPath, $shmPath) | Where-Object { Test-Path $_ } | Remove-Item -Force
    Write-Output "WAL/SHM actuales eliminados."

    # 4. Restaurar backup
    Copy-Item $BackupPath $dbPath -Force

    # Restaurar WAL del backup si existe
    $backupWal = "$BackupPath-wal"
    if (Test-Path $backupWal) {
        Copy-Item $backupWal $walPath -Force
        Write-Output "WAL del backup restaurado."
    }

    # 5. Verificar integridad del backup restaurado
    $integrity = sqlite3 $dbPath "PRAGMA integrity_check;" 2>&1
    if ($integrity -ne "ok") {
        Write-Error "El backup restaurado también tiene problemas de integridad: $integrity"
        Write-Error "Buscar un backup más antiguo."
    } else {
        Write-Output "Restore exitoso. Integridad OK."
        Write-Output "Backups disponibles en: C:\Users\HITMA\Desktop\synergos-backups\"
        Write-Output "Reiniciar el CMS para verificar que arranca correctamente."
    }
}

# Uso — listar backups disponibles y elegir:
Get-ChildItem "C:\Users\HITMA\Desktop\synergos-backups" "*.sqlite.db" |
    Sort-Object LastWriteTime -Descending |
    Select-Object Name, LastWriteTime, @{N="MB"; E={[Math]::Round($_.Length/1MB, 2)}} |
    Format-Table -AutoSize

# Luego:
# Restore-SynergosSqlite "C:\Users\HITMA\Desktop\synergos-backups\Umbraco-pre-import-20260606-143022.sqlite.db"
```

---

## 10. Vacuum y mantenimiento

Ejecutar periódicamente para reducir tamaño y mejorar performance:

```powershell
# Solo con CMS detenido
Stop-CmsSafe
$backup = Backup-SynergosSqlite -Reason "pre-vacuum"

$db = "Synergos.CMS\Synergos.CMS.Web\umbraco\Data\Umbraco.sqlite.db"

# Checkpoint WAL completo primero
sqlite3 $db "PRAGMA wal_checkpoint(TRUNCATE);"

# Vacuum (puede tardar varios minutos en DBs grandes)
$before = (Get-Item $db).Length
sqlite3 $db "VACUUM;"
$after  = (Get-Item $db).Length
$saved  = [Math]::Round(($before - $after) / 1MB, 2)
Write-Output "Vacuum completado. Espacio liberado: $saved MB"

# Analyze (mejora query planner)
sqlite3 $db "ANALYZE;"
Write-Output "ANALYZE completado."
```

---

## 11. Tablas clave de Umbraco 13

| Tabla | Contenido |
|-------|-----------|
| `umbracoNode` | Todos los nodos del árbol (contenido, tipos, etc.) |
| `umbracoContent` | Enlace nodo↔DocType |
| `umbracoContentVersion` | Versiones (published/draft) |
| `cmsContentType` | DocTypes y ElementTypes |
| `cmsPropertyType` | Propiedades de cada DocType |
| `cmsPropertyData` | Valores de propiedades |
| `umbracoDataType` | DataTypes (editores) |
| `umbracoDocument` | Estado de publicación |
| `cmsMember` | Members |
| `umbracoUser` | Usuarios backoffice |
| `umbracoMediaVersion` | Archivos de media |
| `umbracoLanguage` | Idiomas del sitio |
| `umbracoDictionary` | Claves de diccionario |
| `umbracoLog` | Log de auditoría |
| `cmsContentType2ContentType` | Relaciones composition |

---

## 12. Lo que NUNCA hacer directamente en SQL

| Acción | Razón | Alternativa |
|--------|-------|-------------|
| Crear/modificar DocTypes/DataTypes | Rompe consistencia con uSync | Usar uSync XMLs + Import |
| Cambiar `uniqueId` de nodos | Los GUIDs son referencias externas | Nunca cambiar |
| Borrar filas de `umbracoNode` directamente | Cascadas inconsistentes | Usar Management API `DELETE /document/{key}` |
| Modificar `umbracoLanguage` sin reiniciar | El cache de idiomas no se invalida | Reiniciar CMS después |
| Tocar tablas de `cmsTemplate` | Rompe el routing de vistas | Usar uSync Templates |
| Editar `cmsMember` passwords | Los hashes tienen salt específico | Usar el API `/member/{key}/change-password` |
