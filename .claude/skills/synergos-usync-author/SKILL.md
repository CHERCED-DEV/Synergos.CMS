---
name: synergos-usync-author
description: Protocolo seguro para escribir y editar XMLs de uSync (schema source-of-truth de Synergos). Cubre las reglas de inmutabilidad de Keys/GUIDs, GUID quad-check, encoding, anatomía exacta de XMLs para ContentType/DataType/Template/Dictionary, dependency ordering, y el flujo de export desde el backoffice. Invocar antes o durante la autoría de cualquier XML de schema — especialmente cuando synergos-cms-author delega la generación de XMLs complejos.
model: claude-opus-4-8
---

# SYNERGOS uSync Author — protocolo seguro de autoría de schema XMLs

Los XMLs de `uSync/v9/` son la **única fuente de verdad del schema** (ADR 0008). Un error aquí puede corromper el DB de Umbraco en el siguiente Import. Esta skill define las reglas exactas que todo XML de schema debe cumplir antes de ser importado.

---

## 0. Los dos flujos válidos de autoría

```
FLUJO A — Backoffice-first (arquitecto autora en la UI)
  Arquitecto crea DocType/DataType en backoffice
       ↓
  Umbraco persiste en DB
       ↓
  uSync Export → genera/actualiza los XMLs en uSync/v9/
       ↓
  Agente revisa los XMLs exportados (no los escribe)

FLUJO B — Agent-author (agente escribe XMLs directamente)
  Agente genera XML con GUID fresco verificado (quad-check)
       ↓
  Agente escribe el XML en uSync/v9/
       ↓
  Arquitecto ejecuta uSync Import desde backoffice
       ↓
  Umbraco aplica el schema al DB
```

**Cuándo usar Flujo B:** Cuando se crea schema nuevo que no existe aún en el backoffice — ElementTypes, DataTypes customizados, Compositions. El agente escribe el XML completo y correcto; el arquitecto solo importa.

**Prohibido:** Pipeline code-first (crear DocTypes en C# y que migren al DB automáticamente). Ver ADR 0008.

---

## 1. Reglas de inmutabilidad — las más críticas

### 1A. Key GUID — nunca reutilizar al cambiar storage type

Si se cambia el `<Type>` de un DataType (de TextBox a DropDown, UrlPicker, MediaPicker, Tags):

```
INCORRECTO — mismo Key, diferente Type:
  <DataType Key="abc123" Alias="DTMyField" DatabaseType="Nvarchar">
    <Type>Umbraco.TextBox</Type>   ← era TextBox
  </DataType>

  ↓ cambiar a DropDown con mismo Key ↓

  <DataType Key="abc123" Alias="DTMyField" DatabaseType="Nvarchar">
    <Type>Umbraco.DropDown.Flexible</Type>  ← CORRUPTO — el JSON de los valores legacy revienta
  </DataType>

CORRECTO — Key nuevo:
  <DataType Key="def456" Alias="DTMyField" DatabaseType="Nvarchar">
    <Type>Umbraco.DropDown.Flexible</Type>
  </DataType>
```

**Por qué:** Los property values en DB almacenan el valor serializado según el tipo original. Si el type cambia sin nuevo Key, el deserializador lee un string plano donde espera JSON → excepción en runtime.

### 1B. IsElement — inmutable post-creación

Una vez que un ContentType existe en el DB con `<IsElement>false</IsElement>`, cambiar a `true` vía uSync **no propaga** al DB. El XML se importa sin error pero la propiedad no cambia.

**Consecuencia aceptada:** ModelsBuilder se queja en boot ("FlagOutOfDateModels") pero el sistema funciona — los renderers acceden a propiedades untyped via `Model.Content.Value<T>("alias")`.

**Qué NO hacer:** Borrar y recrear el tipo para forzar el cambio. Se perderían todos los content nodes que usen ese tipo.

### 1C. Alias — estable una vez usado en Content

Si un alias de DocType ya tiene nodos de contenido en el DB, cambiar el alias rompe las referencias. Umbraco lo busca por Key (GUID), no por alias, pero el routing de templates sí usa el alias.

### 1D. Compositions referenciadas son inmutables en su Key

Si un ElementType A usa la Composition B (`cmsContentType2ContentType`), la Key de B no puede cambiar sin actualizar todos los XMLs que la referencian.

---

## 2. GUID quad-check — procedimiento obligatorio

**Antes de asignar cualquier GUID nuevo a un elemento de schema:**

```powershell
function Assert-GuidFresh {
    param([string]$Guid)

    $uSyncRoot = "Synergos.CMS\Synergos.CMS.Web\uSync\v9"
    $codeRoot  = "Synergos.CMS"

    # Check 1: XMLs de uSync
    $xmlHits = Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
        Select-String -Pattern $Guid -SimpleMatch
    if ($xmlHits) {
        Write-Error "GUID '$Guid' ya existe en XMLs de uSync:"
        $xmlHits | ForEach-Object { Write-Error "  $($_.Path):$($_.LineNumber)" }
        return $false
    }

    # Check 2: código C#
    $codeHits = Get-ChildItem $codeRoot -Recurse -Filter "*.cs" |
        Select-String -Pattern $Guid -SimpleMatch
    if ($codeHits) {
        Write-Error "GUID '$Guid' ya existe en código C#:"
        $codeHits | ForEach-Object { Write-Error "  $($_.Path):$($_.LineNumber)" }
        return $false
    }

    # Check 3: versión sin guiones (compacta)
    $compact = $Guid -replace '-', ''
    $compactHits = Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
        Select-String -Pattern $compact -SimpleMatch
    if ($compactHits) {
        Write-Error "GUID compacto '$compact' ya existe en XMLs."
        return $false
    }

    # Check 4: mayúsculas/minúsculas alternativas
    $upper = $Guid.ToUpper()
    $lower = $Guid.ToLower()
    $upperHits = Get-ChildItem $uSyncRoot -Recurse -Filter "*.config" |
        Select-String -Pattern $upper -SimpleMatch -CaseSensitive
    if ($upperHits) {
        Write-Error "GUID uppercase '$upper' ya existe en XMLs."
        return $false
    }

    Write-Output "GUID '$Guid' es fresco — 0 colisiones en 4 checks."
    return $true
}

# Generar y verificar GUID nuevo:
do {
    $g = [guid]::NewGuid().ToString()
} until (Assert-GuidFresh $g)
Write-Output "GUID seguro para usar: $g"
```

**Cuándo aplica:**
- Key de nuevo ContentType (DocType, ElementType, Composition)
- Key de nuevo DataType
- Key de nueva Template
- Key de nueva clave de Dictionary
- Key de nuevo MediaType

**NO aplica a:** GUIDs de compositions que ya existen (se reutilizan por referencia), GUIDs de contenido editorial (esos los asigna Umbraco).

---

## 3. Encoding y formato de XMLs

### Encoding requerido

**UTF-8 sin BOM** (Umbraco 13 acepta ambos, pero UTF-8 sin BOM es el estándar del proyecto).

```powershell
# Escritura correcta
$content = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
...
"@
[System.IO.File]::WriteAllText($filePath, $content, [System.Text.Encoding]::UTF8)
```

**Nunca:**
```powershell
# MAL — PowerShell 5.1 usa UTF-16 LE por defecto
Set-Content $filePath $content

# MAL — Out-File también UTF-16 en PS 5.1
Out-File $filePath -InputObject $content
```

### Declaración XML

Siempre la primera línea:
```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
```

### Indentación

2 espacios (no tabs). Consistente con los XMLs existentes en el repo.

---

## 4. Anatomía XML — ContentType (DocType / ElementType / Composition)

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<ContentType Key="{GUID-fresco-verificado}"
             Alias="{alias}"
             Level="{1 para top-level, 2+ para nested}"
             SortOrder="0">
  <Info>
    <Name>{Nombre legible en backoffice}</Name>
    <Icon>icon-{nombre-del-icono}</Icon>           <!-- ver referencia iconos Umbraco 13 -->
    <Thumbnail>folder.png</Thumbnail>
    <Description>{Descripción editor-facing, ~120 chars, sin jerga ADR}</Description>
    <AllowAtRoot>{true/false}</AllowAtRoot>
    <IsListView>false</IsListView>
    <Variations>Culture</Variations>              <!-- Culture por defecto; Nothing solo para flags/enums globales -->
    <IsElement>{true si ElementType, false si DocType}</IsElement>
    <HistoryCleanup>
      <PreventCleanup>false</PreventCleanup>
    </HistoryCleanup>
    <DefaultTemplate>{alias-template o vacío}</DefaultTemplate>
    <AllowedTemplates>                            <!-- solo para DocTypes que tienen templates -->
      <Template>{alias-template}</Template>
    </AllowedTemplates>
  </Info>
  <Structure>                                     <!-- DocTypes hijos permitidos (vacío para ElementTypes) -->
  </Structure>
  <GenericProperties>
    <GenericProperty>
      <Key>{GUID-fresh-para-esta-property}</Key>
      <Name>{Nombre del campo}</Name>
      <Alias>{camelCaseAlias}</Alias>
      <Type>{GUID-del-DataType}</Type>            <!-- GUID del DataType, no alias -->
      <Definition>{GUID-del-DataType}</Definition>
      <Tab>{alias-del-tab}</Tab>
      <SortOrder>{0, 10, 20, ...}</SortOrder>
      <Mandatory>{true/false}</Mandatory>
      <MandatoryMessage></MandatoryMessage>
      <Validation></Validation>
      <Description><![CDATA[{descripción del campo}]]></Description>
      <Variations>Culture</Variations>            <!-- heredar de contenedor o especificar -->
      <LabelOnTop>false</LabelOnTop>
    </GenericProperty>
    <!-- ... más propiedades ... -->
  </GenericProperties>
  <Tabs>
    <Tab>
      <Key>{GUID-del-tab}</Key>
      <Caption>{Nombre del tab visible en backoffice}</Caption>
      <Alias>{camelCaseAlias}</Alias>
      <Type>Tab</Type>
      <SortOrder>0</SortOrder>
    </Tab>
  </Tabs>
  <CompositionKeys>                               <!-- Compositions que este tipo hereda -->
    <Key>{GUID-de-la-composition}</Key>
    <Key>{GUID-de-otra-composition}</Key>
  </CompositionKeys>
</ContentType>
```

### Compositions canónicas del proyecto (GUIDs fijos — NO generar nuevos)

| Composition | Alias | GUID |
|-------------|-------|------|
| compDomId | compDomId | *(leer del XML existente en uSync/v9/ContentTypes/)* |
| compDomClass | compDomClass | *(leer del XML existente)* |
| compDomAttr | compDomAttr | *(leer del XML existente)* |
| compSeo | compSeo | *(leer del XML existente)* |
| compPageBasic | compPageBasic | *(leer del XML existente)* |

**Siempre leer los GUIDs de compositions desde los XMLs existentes — nunca inventarlos.**

```powershell
# Obtener GUID de una composition existente
$compXml = Get-Content "Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes\compDomId.config" -Raw
$compKey  = ([xml]$compXml).ContentType.Key
Write-Output "compDomId Key: $compKey"
```

---

## 5. Anatomía XML — DataType

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<DataType Key="{GUID-fresco-verificado}"
          Alias="{DTPrefix}{Pascal}"
          DatabaseType="{Nvarchar|Integer|Date|Decimal|Ntext}"
          EditorAlias="{Umbraco.EditorAlias}"
          EditorUiAlias="{Umb.PropertyEditorUi.Alias.si.aplica}"
          Level="1"
          SortOrder="0">
  <Info>
    <Name>{Nombre visible en backoffice}</Name>
  </Info>
  <PreValues>
    <!-- Configuración específica del editor (ver ejemplos por tipo abajo) -->
  </PreValues>
</DataType>
```

### PreValues por tipo de editor

**TextBox:**
```xml
<PreValues>
  <PreValue Alias="maxChars" Value="500"/>
</PreValues>
```

**TextArea:**
```xml
<PreValues>
  <PreValue Alias="maxChars" Value="1000"/>
  <PreValue Alias="rows" Value="5"/>
</PreValues>
```

**DropDown.Flexible (enum/select):**
```xml
<PreValues>
  <PreValue Alias="multiple" Value="0"/>         <!-- 0=single, 1=multiple -->
  <PreValue Alias="items">
    <value>
      <item id="{guid}" value="valor1">Etiqueta 1</item>
      <item id="{guid}" value="valor2">Etiqueta 2</item>
    </value>
  </PreValue>
</PreValues>
```

**MultiUrlPicker:**
```xml
<PreValues>
  <PreValue Alias="minNumber" Value="0"/>
  <PreValue Alias="maxNumber" Value="1"/>        <!-- 1 para single URL, 0=ilimitado -->
  <PreValue Alias="ignoreUserStartNodes" Value="0"/>
</PreValues>
```

**MediaPicker3 (imagen):**
```xml
<PreValues>
  <PreValue Alias="multiple" Value="0"/>
  <PreValue Alias="validationLimit">
    <value>{"min":0,"max":1}</value>
  </PreValue>
  <PreValue Alias="allowedMediaTypes" Value=""/>  <!-- vacío = todos; o GUID de MediaType -->
  <PreValue Alias="crops" Value="[]"/>
</PreValues>
```

**TrueFalse (boolean):**
```xml
<PreValues>
  <PreValue Alias="labelOn" Value="Sí"/>
  <PreValue Alias="labelOff" Value="No"/>
  <PreValue Alias="default" Value="0"/>
</PreValues>
```

**RichTextEditor (TinyMCE):**
```xml
<PreValues>
  <PreValue Alias="toolbar" Value='["bold","italic","underline","alignleft","aligncenter","bullist","numlist","link","umbmediapicker","umbembeddialog"]'/>
  <PreValue Alias="maxImageSize" Value="500"/>
  <PreValue Alias="ignoreUserStartNodes" Value="0"/>
</PreValues>
```

**BlockList:**
```xml
<PreValues>
  <PreValue Alias="blocks">
    <value>[
      {
        "contentElementTypeKey":"{GUID-del-ElementType}",
        "settingsElementTypeKey":null,
        "label":"{Nombre del bloque}",
        "editorSize":"medium",
        "forceHideContentEditorInOverlay":false,
        "iconColor":null,
        "backgroundColor":null,
        "thumbnail":null
      }
    ]</value>
  </PreValue>
  <PreValue Alias="validationLimit">
    <value>{"min":0,"max":0}</value>
  </PreValue>
  <PreValue Alias="useSingleBlockMode" Value="0"/>
  <PreValue Alias="useLiveEditing" Value="0"/>
  <PreValue Alias="useInlineEditingAsDefault" Value="0"/>
  <PreValue Alias="maxPropertyWidth" Value=""/>
</PreValues>
```

**ATENCIÓN en BlockList:** Antes de añadir un `contentElementTypeKey`, verificar que ese GUID no colisiona con el Key del DataType en sí. Ver memory `feedback_guid_block_element_collision.md`.

---

## 6. Anatomía XML — Template

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Template Key="{GUID-fresco-verificado}"
          Alias="{PascalCase}"
          Level="1">
  <Info>
    <Name>{Nombre visible}</Name>
    <Master>{alias-del-master-template-o-vacío}</Master>
    <Design><![CDATA[@{
    Layout = "MasterLayout";
}
<!-- contenido Razor -->]]></Design>
  </Info>
</Template>
```

---

## 7. Anatomía XML — Dictionary

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<DictionaryItem Key="{GUID-fresco-verificado}"
                ItemKey="{Syn.NombreClave}"
                ParentKey="{GUID-del-padre-o-vacío}">
  <Translations>
    <Translation Language="es-co">
      <![CDATA[Valor en español para Colombia]]>
    </Translation>
    <Translation Language="en-us">
      <![CDATA[Value in English]]>
    </Translation>
  </Translations>
</DictionaryItem>
```

---

## 8. Naming conventions

| Tipo | Patrón | Ejemplo |
|------|--------|---------|
| ElementType alias | `elementSyn{Pascal}` | `elementSynHeroBanner` |
| ElementType tag DOM | `synergos-{kebab}` | `synergos-hero-banner` |
| Composition alias | `comp{Pascal}` | `compSeo`, `compDomId` |
| DataType alias | `DT{Pascal}` o `DT{Context}{Pascal}` | `DTSelectRating`, `DTHeroBannerVariant` |
| Page DocType alias | `page{Pascal}` | `pageStandard`, `pageLanding` |
| Settings alias | `cfg{Pascal}` | `cfgAlert`, `cfgSiteConfigSettings` |
| BlockList DataType | `DTBlockList{Pascal}` | `DTBlockListMainContent` |
| Template alias | `{Pascal}` coincidiendo con el DocType | `PageStandard` |
| Dictionary key | `Syn.{Contexto}.{Campo}` | `Syn.Nav.SkipToContent` |
| XML filename | `{alias}.config` | `elementSynHeroBanner.config` |

---

## 9. Carpetas uSync — dónde va cada XML

```
uSync/v9/
├── ContentTypes/           ← DocTypes, ElementTypes, Compositions
│   ├── elementSyn*.config  ← ElementTypes
│   ├── comp*.config        ← Compositions
│   ├── page*.config        ← Page DocTypes
│   ├── cfg*.config         ← Settings/Config DocTypes
│   └── site*.config        ← Site-level DocTypes
├── DataTypes/
│   └── DT*.config          ← Todos los DataTypes
├── Templates/
│   └── *.config            ← Templates Razor (nombre = alias del DocType)
├── Dictionary/
│   └── Syn.*.config        ← Claves de diccionario
├── MediaTypes/
│   └── *.config            ← MediaTypes (sinImage, etc.)
└── Languages/
    └── *.config            ← Configuración de idiomas
```

---

## 10. Orden de importación (dependency resolution)

uSync 13 resuelve la mayoría de dependencias automáticamente, pero en casos complejos el orden manual es:

```
1. Languages           (si hay nuevos idiomas)
2. DataTypes           (DT*.config) — sin dependencias
3. MediaTypes          (si hay nuevos tipos de media)
4. Dictionary          (Syn.*.config) — sin dependencias
5. Compositions        (comp*.config) — solo dependen de DataTypes
6. ElementTypes simples (elementSyn*.config sin BlockLists)
7. ElementTypes con BlockList (que referencian otros ElementTypes)
8. Page/Config DocTypes (page*.config, cfg*.config)
9. Templates           (*.config en Templates/)
```

Si hay error por dependencia circular o no resuelta, importar en dos pasadas: primero sin las referencias conflictivas, luego con ellas.

---

## 11. Export desde backoffice (Flujo A)

Cuando el arquitecto crea o modifica schema en el backoffice, exportar para actualizar los XMLs:

```
1. Navegar a la sección uSync en el backoffice (Settings > uSync o árbol de Settings)
2. Seleccionar "Export" o "Report" para ver qué cambió
3. Ejecutar "Export" para que uSync genere/actualice los XMLs en uSync/v9/
4. Verificar con git diff que solo los archivos esperados cambiaron
5. Commitear los cambios
```

**Después del export**, el agente puede leer los XMLs generados para entender la estructura y usarla como referencia en Flujo B.

---

## 12. Checklist pre-import de XMLs

Antes de pedirle al arquitecto que haga Import, verificar:

```powershell
$uSyncRoot   = "Synergos.CMS\Synergos.CMS.Web\uSync\v9"
$newFiles    = @("elementSynPricingCard.config", "DTSelectRating.config")  # reemplazar con los nuevos
$errors      = @()

foreach ($fileName in $newFiles) {
    # Buscar el archivo (puede estar en subcarpeta)
    $file = Get-ChildItem $uSyncRoot -Recurse -Filter $fileName | Select-Object -First 1
    if (-not $file) { $errors += "$fileName: NO ENCONTRADO"; continue }

    # 1. XML bien formado
    try { [xml]$xml = Get-Content $file.FullName -Encoding UTF8 }
    catch { $errors += "$fileName: XML malformado — $($_.Exception.Message)"; continue }

    # 2. Key presente
    $key = $xml.DocumentElement.Attributes["Key"]?.Value
    if (-not $key) { $errors += "$fileName: Key attribute faltante" }

    # 3. Alias presente
    $alias = $xml.DocumentElement.Attributes["Alias"]?.Value
    if (-not $alias) { $errors += "$fileName: Alias attribute faltante" }

    # 4. Encoding (detectar UTF-16)
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $errors += "$fileName: ENCODING UTF-16 LE — debe ser UTF-8"
    }

    # 5. GUID no colisiona con otros archivos
    if ($key -and -not (Assert-GuidFresh $key)) {
        $errors += "$fileName: Key GUID colisiona con existente"
    }

    Write-Output "OK — $fileName (Key: $key, Alias: $alias)"
}

if ($errors) {
    Write-Error "Problemas encontrados — NO proceder con Import:"
    $errors | ForEach-Object { Write-Error "  - $_" }
} else {
    Write-Output "Checklist completo — todos los XMLs listos para Import."
}
```

---

## 13. Situaciones que requieren atención especial

### Cambio de tab structure

Si se mueven propiedades de un tab a otro en un ContentType existente, el Import puede fallar si el `Tab Key` cambia. Los tabs tienen su propio GUID — si se elimina un tab y se crea uno nuevo con el mismo nombre, el GUID cambia y las referencias internas se rompen.

**Solución:** Mantener el GUID del tab aunque se cambie el Caption. Nunca eliminar y recrear un tab.

### Propiedades mandatory en ContentType con contenido existente

Si se agrega `<Mandatory>true</Mandatory>` a una propiedad de un DocType que ya tiene contenido, el validador del backoffice pedirá rellenar el campo antes de publicar, pero no rompe nada automáticamente.

### DataType shared entre múltiples ContentTypes

Si un DataType se cambia (por ejemplo, se agregan opciones a un DropDown), el cambio aplica a todos los ContentTypes que lo usen. Verificar que el cambio es compatible con el contenido existente.

### Reserved compositions (no son orphans)

Compositions con Description que empieza con `[Bloqueado externamente - ...]` o `[Disponible — sin consumers actuales]` NO son código muerto. No proponer su eliminación.
