---
name: synergos-cms-author
description: Full-stack authoring de Synergos CMS. Crea contenido Editorial vía Management API (todos los campos diligenciados, imágenes generadas y subidas), Y cuando no existe el ElementType/Composition/DataType necesario, lo crea completo (uSync XML + Razor view SynHost + componente Angular standalone). Conoce el 100% del schema vivo. Requiere CMS en http://synergos.local:5000.
model: claude-opus-4-8
---

# SYNERGOS CMS Author — full-stack schema + content + media

Skill integral que cubre tres capas en un solo flujo:

1. **Schema** — crea ElementTypes, Compositions y DataTypes (uSync XML) cuando no existe el adecuado.
2. **Razor views** — crea los partials SynHost + Block Grid wrappers para SSR.
3. **Angular components** — crea el stub del Web Component (standalone, zoneless, signal inputs).
4. **Content** — crea nodos editoriales en Umbraco vía Management API con todos los campos diligenciados.
5. **Media** — genera imágenes PNG y las sube automáticamente para campos MediaPicker3.

---

> ## ⚠️ AVISO (ADR 0093) — La autoría de contenido NO usa la Management API
> Umbraco 13 **no tiene Management API** (`/umbraco/management/api/*` → 404; el paquete `Umbraco.Cms.Api.Management` empieza en v14). **La §1 (token) y la §7 (POST /v1/document, /v1/media) de este skill NO funcionan en este stack.** Para crear/llenar contenido y media usa la vía server-side `IContentService`/`IMediaService` detrás del flag DevSeed: ver **`synergos-content-fill`** y **ADR 0093**. Las secciones de schema (uSync XML §2-§4), Razor (§5) y Angular (§6) de este skill siguen vigentes.

## 0. Reglas que no se rompen

- **Culture-variant por default** — todos los campos de texto van con `culture: "es-co"`. `Nothing` solo para flags/enums globales.
- **Pickers por intent** (ADR 0021): URLs → `DTUrlPickerSingle` (MultiUrlPicker MaxNumber=1); media → `MediaPicker3`; enums → `Dropdown.Flexible`; bool → `TrueFalse`.
- **GUID quad-check** antes de escribir cualquier XML uSync nuevo — nunca reusar un GUID existente.
- **uSync = source-of-truth** (ADR 0008) — schema se escribe en XML. Nunca code-first.
- **Composer no se toca** para schema nuevo — solo para wiring de seams.
- **Naming conventions**: `elementSyn{Pascal}` → `<synergos-{kebab}>` → Razor `SynHost/{Pascal}.cshtml`.
- **Angular standalone + zoneless** — `provideZonelessChangeDetection`, signal inputs, `createCustomElement`.
- **No seeders en boot** (ADR 0013) — content creation solo cuando el usuario invoca esta skill.
- **Alt text obligatorio** en toda imagen subida (WCAG 1.1.1).
- **Compositions reservadas** — skipear si `<Description>` arranca con `[Bloqueado externamente -` o `[Disponible — sin consumers`.

---

## 1. Pre-flight

```powershell
# Verificar CMS vivo
try {
    Invoke-WebRequest "http://synergos.local:5000/umbraco/api/keepalive/ping" -UseBasicParsing -TimeoutSec 5 | Out-Null
    Write-Output "CMS OK"
} catch { Write-Error "CMS no responde. Arrancar con 'dotnet run' en Synergos.CMS.Web/"; exit 1 }

# Autenticar
$baseUrl  = "http://synergos.local:5000"
$authBody = "grant_type=password&client_id=umbraco-back-office&username=admin%40synergos.local&password=Synergos2026%21"
$authResp = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/security/back-office/token" `
    -Method POST -ContentType "application/x-www-form-urlencoded" -Body $authBody
$token   = $authResp.access_token
$headers = @{ "Authorization" = "Bearer $token"; "Accept" = "application/json" }
```

---

## 2. Descubrimiento de schema — 100% awareness

### 2A. Encontrar ElementTypes por familia

```powershell
$schemaRoot = "Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes"

# Por familia:
Get-ChildItem $schemaRoot "elementSyn*.config"     # CDN-hosted (elementSynHero, etc.)
Get-ChildItem $schemaRoot "elementLayout*.config"  # Layout presets
Get-ChildItem $schemaRoot "elementAction*.config"  # Botones, links
Get-ChildItem $schemaRoot "elementMedia*.config"   # Media, gallery, avatar
Get-ChildItem $schemaRoot "elementInfo*.config"    # Stat, FAQ, badge
Get-ChildItem $schemaRoot "elementCorp*.config"    # Corporate blocks
Get-ChildItem $schemaRoot "elementComp*.config"    # Composable blocks (Hero, Card, CtaBanner)
Get-ChildItem $schemaRoot "elementForm*.config"    # Form fields
Get-ChildItem $schemaRoot "elementShop*.config"    # E-commerce
Get-ChildItem $schemaRoot "elementMember*.config"  # Auth / member
Get-ChildItem $schemaRoot "elementNav*.config"     # Navigation
Get-ChildItem $schemaRoot "elementFlow*.config"    # Flows
Get-ChildItem $schemaRoot "elementInt*.config"     # Integraciones (iframe, script)
Get-ChildItem $schemaRoot "elementStruct*.config"  # Dividers, spacers
Get-ChildItem $schemaRoot "comp*.config"           # Compositions
Get-ChildItem $schemaRoot "page*.config"           # Page types
Get-ChildItem $schemaRoot "cfg*.config"            # Global settings
```

Para leer un schema específico:
```powershell
[xml]$schema = Get-Content "$schemaRoot\{alias}.config" -Encoding UTF8
$key           = $schema.ContentType.Key
$isElement     = $schema.ContentType.Info.IsElement
$variations    = $schema.ContentType.Info.Variations
$compositions  = $schema.ContentType.Info.Compositions.Composition | Select-Object -ExpandProperty "#text"
$props         = $schema.ContentType.GenericProperties.GenericProperty | ForEach-Object {
    [PSCustomObject]@{ Alias=$_.Alias; Type=$_.Type; Definition=$_.Definition
                       Mandatory=$_.Mandatory; Variations=$_.Variations; Tab=$_.Tab }
}
```

### 2B. GUIDs canónicos — Compositions (referencia fija)

| Alias | Key |
|-------|-----|
| compCoreBase | `5e2ec6b8-7f65-4c26-8c20-1b9a72bed2f8` |
| compSeo | `85e75635-b950-4583-b5ca-2a51c08892e3` |
| compDomClass | `46367d43-269b-418d-b54d-075fcf6d658b` |
| compDomVariant | `e11a9feb-fa1c-4740-9481-3dce56748473` |
| compDomVisibility | `7a383458-6ea6-4784-a4d1-17f8d4b01073` |
| compDomAttributes | `1d5bafbb-59af-4002-8709-ebade1be0392` |
| compDomSpacing | `0a2edb7e-8555-4044-bebc-bf0fe37662f2` |
| compDomDisplay | `f79a6dcc-aecd-4a2b-bd1e-0de3e08eb800` |
| compDomFlex | `610e39a0-bfd1-4786-9ddb-697884da5d4b` |
| compDomGrid | `b0bed49d-e17d-4da3-a0ec-453dcb9948f7` |
| compDomPresetChrome | `7ae5dcf3-5ed1-4ea1-a4e1-cc3a4a474138` |
| compContentHeading | `eb4bd93f-f9d1-44dd-b85e-d64b2084a3ad` |
| compContentText | `71d74897-c807-452c-83c3-04c94fd1414b` |
| compContentMedia | `e2a29901-8155-4d36-80ab-f42b2452a45b` |
| compContentCta | `92cb0070-0f4d-43a0-84a7-102f60dc41b4` |

### 2C. GUIDs canónicos — DataTypes base

| Alias | Key | EditorAlias |
|-------|-----|-------------|
| Textstring | `0cc0eba1-9960-42c9-bf9b-60e150b429ae` | Umbraco.TextBox |
| Textarea | `c6bac0dd-4ab9-45b1-8e30-e4b619ee5da3` | Umbraco.TextArea |
| Numeric | `2e6d3631-066e-44b8-aec4-96f09099b2b5` | Umbraco.Integer |
| True/false | `92897bc6-a5f3-4ffe-ae27-f2e7e33dda49` | Umbraco.TrueFalse |
| Richtext editor | `ca90c950-0aff-4e72-b976-a30b1ac57dad` | Umbraco.TinyMCE |
| Media Picker (single) | `4309a3ea-0d78-4329-a06c-c80b036af19a` | Umbraco.MediaPicker3 |
| Content Picker | `fd1e0da5-5606-4862-b679-5d0cf3a52a59` | Umbraco.ContentPicker |
| DTUrlPickerSingle | `49391109-580c-46d8-8408-f496b43b6409` | Umbraco.MultiUrlPicker (max=1) |
| Tags | `b6b73142-b9c1-4bf8-a16d-e1c23320b549` | Umbraco.Tags |
| DTBlockGridSections | `bdef3027-193b-4334-b3ee-738eded72215` | Umbraco.BlockGrid |
| DTBlockListCtaItems | `972ccc3d-ec95-4dd1-88f1-c9d8b8c1fc66` | Umbraco.BlockList |

Para DTSelect*, leer el DataType desde `uSync/v9/DataTypes/DTSelect{Name}.config` para obtener Key + values.

### 2D. GUIDs canónicos — Layout Presets (para BlockGrid)

| Alias | Key |
|-------|-----|
| elementLayoutSection | `1c68f4a9-24e9-49ac-9efa-05b3d4b1404a` |
| elementLayoutContainer | `f39c535a-879f-4bbf-8d94-8370c7f45f5a` |
| elementLayoutStack | `c3dd2aaa-7cdf-410a-873e-2a36d52ecc39` |
| elementLayoutGrid | `8247e825-1210-495a-a735-5ce8928fef07` |
| elementLayoutColumn | `4b075799-e7ee-4164-aef8-21911360cfc1` |
| elementLayout1Col | `57fc7792-c6d8-424b-be23-c7c217faedb3` |
| elementLayout2ColEven | `e8baf208-35d5-4c9f-9aa4-967fa5e070bf` |
| elementLayout2ColMainSidebar | `911a64ba-ccc4-4b21-a3f2-f9273c38c6b6` |
| elementLayout3Col | `1fc59d8b-7278-4a0a-9b4c-d596ae230372` |
| elementLayout4Col | `39e1538b-0ce7-40a3-9853-849354bb1c75` |
| elementLayoutHolyGrail | `c9356982-82a2-420f-a40d-84e509c0fa28` |
| elementLayoutSidebarMain | `e88e7c8b-0e74-447b-9cb7-51d8d642b9ec` |
| elementLayoutHero | `fef510f5-59c8-4ae8-b499-2188017df5c1` |

---

## 3. Decisión: reusar vs crear nuevo

**ANTES de proponer crear algo nuevo**, recorrer este árbol:

### Para una Composition nueva

Aplicar el filtro de 3 preguntas (ADR + `feedback_composition_design_solid`):
1. ¿Existe ya una composition que cubre esto? → Buscar en `comp*.config`. Si sí, reusar.
2. ¿Tiene 2+ consumers reales o un consumer + plan firme? → Si no, no crear.
3. ¿Captura capacidad transversal o es feature de un solo block? → Si es solo un block, ir inline.

Solo proceder si las 3 respuestas justifican la composition nueva.

### Para un ElementType nuevo

Crear cuando:
- La funcionalidad editorial no existe en ningún `element*.config` existente.
- No es un sub-caso de un elemento existente con prop diferente.
- Tiene al menos 1 consumer inmediato (una página o block grid que lo va a usar).

### Para un DataType nuevo

Crear cuando:
- Se necesita un enum/dropdown con valores nuevos que no están en ningún `DTSelect*.config`.
- No es una variación de una DTSelect existente que acepta esos valores.

---

## 4. Creación de schema nuevo

### 4A. Naming conventions (completo)

| Tipo | Patrón | Ejemplo |
|------|--------|---------|
| ElementType CDN | `elementSyn{Pascal}` | `elementSynPricingCard` |
| ElementType SSR action | `elementAction{Pascal}` | `elementActionDownload` |
| ElementType SSR media | `elementMedia{Pascal}` | `elementMediaVideo` |
| ElementType SSR layout | `elementLayout{Pascal}` | `elementLayoutMasonry` |
| ElementType SSR info | `elementInfo{Pascal}` | `elementInfoKeyFact` |
| ElementType SSR corp | `elementCorp{Pascal}` | `elementCorpTeamCard` |
| ElementType SSR comp | `elementComp{Pascal}` | `elementCompFeatureSplit` |
| ElementType SSR form | `elementForm{Pascal}` | `elementFormSelect` |
| ElementType SSR shop | `elementShop{Pascal}` | `elementShopWishlist` |
| ElementType SSR member | `elementMember{Pascal}` | `elementMemberBadge` |
| ElementType SSR nav | `elementNav{Pascal}` | `elementNavBreadcrumb` |
| ElementType SSR flow | `elementFlow{Pascal}` | `elementFlowConfirmation` |
| ElementType SSR int | `elementInt{Pascal}` | `elementIntChatWidget` |
| ElementType SSR struct | `elementStruct{Pascal}` | `elementStructRuler` |
| Composition | `comp{Pascal}` | `compContentRating` |
| DataType Dropdown | `DTSelect{Pascal}` | `DTSelectRatingScale` |
| DataType BlockList | `DTBlockList{Pascal}` | `DTBlockListRatingItems` |
| DOM tag (CDN) | `synergos-{kebab}` | `<synergos-pricing-card>` |
| Razor SynHost | `SynHost/{Pascal}.cshtml` | `SynHost/PricingCard.cshtml` |
| Block Grid wrapper | `blockgrid/Components/element{Family}{Pascal}.cshtml` | `blockgrid/Components/elementSynPricingCard.cshtml` |
| Angular component (selector) | `sg-{kebab}` | `sg-pricing-card` |
| Angular path | `platforms/angular/apps/elements/{tier}/{name}/` | `platforms/angular/apps/elements/compositions/pricing-card/` |

**Tiers Angular:** `primitive` (átomo), `composition` (molécula), `module` (organismo), `experience` (plantilla).

### 4B. GUID generation + quad-check

```powershell
# Generar GUID fresco
$g = [guid]::NewGuid().ToString()

# Quad-check contra TODOS los XMLs uSync (ContentTypes + DataTypes + Templates + Dictionary + MediaTypes)
$hits = Get-ChildItem "Synergos.CMS\Synergos.CMS.Web\uSync\" -Recurse -Filter "*.config" |
    Select-String -Pattern $g -SimpleMatch
if ($hits) { Write-Error "GUID collision: $g. Generar nuevo."; exit 1 }

# También check en código C# por si acaso
$codeHits = Get-ChildItem "Synergos.CMS\" -Recurse -Filter "*.cs" |
    Select-String -Pattern $g -SimpleMatch
if ($codeHits) { Write-Error "GUID en código: $g. Generar nuevo."; exit 1 }

Write-Output "GUID verificado: $g — 0 colisiones"
```

### 4C. uSync XML — Nuevo ElementType

**Template completo para un `elementSyn{Name}` con un campo de texto + media:**

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentType Key="{KEY-ELEMENT}" Alias="elementSyn{Name}" Level="1">
  <Info>
    <Name>Element — Syn — {DisplayName}</Name>
    <Icon>icon-{umbraco-icon-name} color-blue</Icon>
    <Thumbnail>folder</Thumbnail>
    <Description><![CDATA[{Descripción ≤120 chars, editor-facing, sin jargon ADR}]]></Description>
    <AllowAtRoot>False</AllowAtRoot>
    <IsListView>False</IsListView>
    <Variations>Culture</Variations>
    <IsElement>true</IsElement>
    <Folder>Blocks/Syn</Folder>
    <Compositions>
      <Composition Key="46367d43-269b-418d-b54d-075fcf6d658b">compDomClass</Composition>
      <Composition Key="e11a9feb-fa1c-4740-9481-3dce56748473">compDomVariant</Composition>
      <Composition Key="7a383458-6ea6-4784-a4d1-17f8d4b01073">compDomVisibility</Composition>
      <Composition Key="1d5bafbb-59af-4002-8709-ebade1be0392">compDomAttributes</Composition>
      <!-- Agregar según necesidad: -->
      <!-- <Composition Key="0a2edb7e-8555-4044-bebc-bf0fe37662f2">compDomSpacing</Composition> -->
      <!-- <Composition Key="eb4bd93f-f9d1-44dd-b85e-d64b2084a3ad">compContentHeading</Composition> -->
      <!-- <Composition Key="e2a29901-8155-4d36-80ab-f42b2452a45b">compContentMedia</Composition> -->
      <!-- <Composition Key="92cb0070-0f4d-43a0-84a7-102f60dc41b4">compContentCta</Composition> -->
    </Compositions>
  </Info>
  <GenericProperties>
    <!-- Prop de texto -->
    <GenericProperty>
      <Key>{KEY-PROP-1}</Key>
      <Name>{Label visible al editor}</Name>
      <Alias>{propAlias}</Alias>
      <Definition>0cc0eba1-9960-42c9-bf9b-60e150b429ae</Definition>
      <Type>Umbraco.TextBox</Type>
      <Mandatory>true</Mandatory>
      <Validation></Validation>
      <Description><![CDATA[{Help text ≤120 chars}]]></Description>
      <SortOrder>10</SortOrder>
      <Tab Alias="content">Contenido</Tab>
      <MandatoryMessage></MandatoryMessage>
      <ValidationRegExpMessage></ValidationRegExpMessage>
      <LabelOnTop>false</LabelOnTop>
      <Variations>Culture</Variations>
    </GenericProperty>
    <!-- Prop de imagen -->
    <GenericProperty>
      <Key>{KEY-PROP-2}</Key>
      <Name>Imagen</Name>
      <Alias>media</Alias>
      <Definition>4309a3ea-0d78-4329-a06c-c80b036af19a</Definition>
      <Type>Umbraco.MediaPicker3</Type>
      <Mandatory>false</Mandatory>
      <Validation></Validation>
      <Description><![CDATA[Imagen asociada al bloque.]]></Description>
      <SortOrder>20</SortOrder>
      <Tab Alias="content">Contenido</Tab>
      <MandatoryMessage></MandatoryMessage>
      <ValidationRegExpMessage></ValidationRegExpMessage>
      <LabelOnTop>false</LabelOnTop>
      <Variations>Culture</Variations>
    </GenericProperty>
    <!-- Override de config CDN (siempre presente en elementSyn*) -->
    <GenericProperty>
      <Key>{KEY-PROP-3}</Key>
      <Name>Config Override (JSON)</Name>
      <Alias>configOverride</Alias>
      <Definition>c6bac0dd-4ab9-45b1-8e30-e4b619ee5da3</Definition>
      <Type>Umbraco.TextArea</Type>
      <Mandatory>false</Mandatory>
      <Validation></Validation>
      <Description><![CDATA[JSON override de configuración CDN. Avanzado — dejar vacío salvo necesidad técnica.]]></Description>
      <SortOrder>90</SortOrder>
      <Tab Alias="advanced">Avanzado</Tab>
      <MandatoryMessage></MandatoryMessage>
      <ValidationRegExpMessage></ValidationRegExpMessage>
      <LabelOnTop>false</LabelOnTop>
      <Variations>Nothing</Variations>
    </GenericProperty>
  </GenericProperties>
  <Structure />
  <Tabs>
    <Tab>
      <Key>{KEY-TAB-1}</Key>
      <Caption>Contenido</Caption>
      <Alias>content</Alias>
      <Type>Group</Type>
      <SortOrder>10</SortOrder>
    </Tab>
    <Tab>
      <Key>{KEY-TAB-2}</Key>
      <Caption>Avanzado</Caption>
      <Alias>advanced</Alias>
      <Type>Group</Type>
      <SortOrder>90</SortOrder>
    </Tab>
  </Tabs>
</ContentType>
```

**Guardar en:** `Synergos.CMS\Synergos.CMS.Web\uSync\v9\ContentTypes\elementSyn{name}.config`

### 4D. uSync XML — Nueva Composition

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentType Key="{KEY}" Alias="comp{Name}" Level="1">
  <Info>
    <Name>Comp — {Name}</Name>
    <Icon>icon-molecular color-orange</Icon>
    <Thumbnail>folder</Thumbnail>
    <Description><![CDATA[{Descripción ≤120 chars}]]></Description>
    <AllowAtRoot>False</AllowAtRoot>
    <IsListView>False</IsListView>
    <Variations>Culture</Variations>
    <IsElement>false</IsElement>
    <Folder>Compositions</Folder>
    <Compositions />
  </Info>
  <GenericProperties>
    <GenericProperty>
      <Key>{KEY-PROP}</Key>
      <Name>{Label}</Name>
      <Alias>{alias}</Alias>
      <Definition>{DataType-GUID}</Definition>
      <Type>{EditorAlias}</Type>
      <Mandatory>false</Mandatory>
      <Validation></Validation>
      <Description><![CDATA[{Help text}]]></Description>
      <SortOrder>10</SortOrder>
      <Tab Alias="{tab}">{Tab Caption}</Tab>
      <MandatoryMessage></MandatoryMessage>
      <ValidationRegExpMessage></ValidationRegExpMessage>
      <LabelOnTop>false</LabelOnTop>
      <Variations>Culture</Variations>
    </GenericProperty>
  </GenericProperties>
  <Structure />
  <Tabs>
    <Tab>
      <Key>{KEY-TAB}</Key>
      <Caption>{Tab Caption}</Caption>
      <Alias>{tab}</Alias>
      <Type>Group</Type>
      <SortOrder>10</SortOrder>
    </Tab>
  </Tabs>
</ContentType>
```

### 4E. uSync XML — Nuevo DataType DTSelect

```xml
<?xml version="1.0" encoding="utf-8"?>
<DataType Key="{KEY}" Alias="DTSelect{Name}" DatabaseType="Nvarchar"
          EditorAlias="Umbraco.DropDown.Flexible" Level="1">
  <Info>
    <Name>DT.Select.{Name}</Name>
    <Folder>DTSelect</Folder>
    <Thumbnail>list</Thumbnail>
  </Info>
  <Config><![CDATA[{"items":[{"id":1,"value":"option1"},{"id":2,"value":"option2"},{"id":3,"value":"option3"}],"multiple":false}]]></Config>
</DataType>
```

### 4F. uSync XML — Nuevo DataType DTBlockList

```xml
<?xml version="1.0" encoding="utf-8"?>
<DataType Key="{KEY}" Alias="DT.BlockList.{Name}" DatabaseType="Ntext"
          EditorAlias="Umbraco.BlockList" Level="1">
  <Info>
    <Name>DT.BlockList.{Name}</Name>
    <Folder>DTBlockList</Folder>
    <Thumbnail>list</Thumbnail>
  </Info>
  <Config><![CDATA[{"blocks":[{"contentElementTypeKey":"{ELEMENT-TYPE-KEY}","label":"{{ propAlias }}","editorSize":"small","forceHideContentEditorInOverlay":false,"stylesheet":null,"view":null,"settingsElementTypeKey":null}],"validationLimit":{"min":0,"max":null},"useSingleBlockMode":false,"useLiveEditing":false,"useInlineEditingAsDefault":false,"maxPropertyWidth":null}]]></Config>
</DataType>
```

### 4G. Trigger uSync Import

**Después de escribir cualquier XML nuevo**, el arquitecto debe:
1. Abrir el backoffice en `http://synergos.local:5000/umbraco/`
2. Ir a Settings → uSync → Import
3. Seleccionar "Import All" (no-destructive first pass)
4. Verificar que el nuevo tipo aparece en Content Types (Content Types section)

**El agente NO ejecuta el import** — solo escribe los XMLs y avisa al arquitecto.

---

## 5. Creación de Razor views

### 5A. Block Grid Wrapper (convención)

**Ruta:** `Synergos.CMS.Web/Views/Partials/blockgrid/Components/elementSyn{Name}.cshtml`

```razor
@*
    Block Grid convention wrapper. Unwraps the BlockGridItem and
    delegates to the SynHost renderer (Views/Partials/SynHost/{Name}.cshtml)
    so the same partial is reusable from LayoutComposer. ADR 0015.
*@
@model Umbraco.Cms.Core.Models.Blocks.BlockGridItem<Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement>
@await Html.PartialAsync("SynHost/{Name}", Model.Content)
```

### 5B. SynHost Renderer — Elemento CDN (con ISynHostEmitter)

**Ruta:** `Synergos.CMS.Web/Views/Partials/SynHost/{Name}.cshtml`

```razor
@*
    SynHost renderer — <synergos-{kebab}>. {Descripción del elemento}.
    Ola {N}, ADR 0015.
*@
@using Umbraco.Cms.Core.Models
@using Umbraco.Cms.Core.Models.PublishedContent
@model IPublishedElement
@inject Synergos.CMS.Interfaces.ISynHostEmitter Emitter
@{
    // — Extraer props del ElementType —
    var heading = Model.Value<string>("heading") ?? "";
    var body    = Model.Value<string>("body") ?? "";
    var media   = Model.Value<IPublishedContent>("media");
    var ctaLink = Model.Value<Link>("ctaLink");

    var props = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["heading"]  = heading,
        ["body"]     = body,
        ["imageSrc"] = media?.Url(mode: UrlMode.Absolute),
        ["ctaLabel"] = Model.Value<string>("ctaLabel"),
        ["ctaUrl"]   = ctaLink?.Url,
    };

    var request = new Synergos.CMS.Interfaces.SynHostEmitRequest(
        BlockAlias: "{kebab-name}",    // alias kebab del custom element (sin prefijo synergos-)
        Props: props,
        ConfigOverrideJson: Model.Value<string>("configOverride"),
        Culture: System.Globalization.CultureInfo.CurrentUICulture);

    var result = await Emitter.EmitAsync(request);
}
@await Html.PartialAsync("SynHost/_Wrapper", (Model, result.ScriptHtml, result.ElementHtml))
```

**Notas de implementación:**
- `BlockAlias` debe coincidir con el `name` registrado en `registry.json` (kebab, sin prefijo `synergos-`).
- Si el CDN bundle no está publicado, `StubBundleRegistryClient` retorna null → `EmitAsync` emite un placeholder HTML comment. No hay error, solo silencio en UI.
- `configOverride` permite al editor forzar props en JSON — propagado by value al Web Component.

### 5C. Layout Renderer (Block Grid con Areas)

**Ruta:** `Synergos.CMS.Web/Views/Partials/blockgrid/Components/elementLayout{Name}.cshtml`

```razor
@*
    elementLayout{Name} renderer (Ola {N}). {Descripción de las areas}.
*@
@model Umbraco.Cms.Core.Models.Blocks.BlockGridItem<Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement>
@using Synergos.CMS.Web.Services
@{
    var element  = Model.Content;
    var variant  = element.Value<string>("variantKey");
    var cssClass = element.Value<string>("cssClass");
    var classes  = string.Join(" ", new[]
    {
        "syn-layout",
        "syn-layout--{kebab-name}",
        string.IsNullOrWhiteSpace(variant)  ? null : $"syn-layout--v-{variant}",
        string.IsNullOrWhiteSpace(cssClass) ? null : cssClass,
    }.Concat(LayoutCssBuilder.Build(element)).Where(s => !string.IsNullOrWhiteSpace(s)));
}
<div class="@classes">
    @foreach (var area in Model.Areas)
    {
        <div class="syn-layout__area syn-layout__area--@area.Alias">
            @await Html.GetBlockGridItemsHtmlAsync(area)
        </div>
    }
</div>
```

### 5D. Dónde va cada archivo

| Caso | Ruta del archivo |
|------|-----------------|
| `elementSyn*` wrapper | `Views/Partials/blockgrid/Components/elementSyn{Name}.cshtml` |
| `elementSyn*` renderer | `Views/Partials/SynHost/{Name}.cshtml` |
| `elementLayout*` renderer | `Views/Partials/blockgrid/Components/elementLayout{Name}.cshtml` |
| `elementAction*`, `elementInfo*`, etc. | `Views/Partials/blockgrid/Components/element{Family}{Name}.cshtml` |
| Partials globales | `Views/Shared/_{Name}.cshtml` |

---

## 6. Creación de Componente Angular

### 6A. Estructura de archivos

```
platforms/angular/apps/elements/{tier}/{name}/
├── project.json
├── tsconfig.app.json
└── src/
    ├── main.ts
    ├── app.config.ts
    ├── index.html
    └── {name}/
        ├── {name}.ts
        ├── {name}.html
        └── {name}.scss
```

Donde `{tier}` = `primitive` / `composition` / `module` / `experience`.

### 6B. project.json

```json
{
  "name": "elements-{tier}-{name}",
  "$schema": "../../../../node_modules/nx/schemas/project-schema.json",
  "projectType": "application",
  "prefix": "sg",
  "sourceRoot": "apps/elements/{tier}/{name}/src",
  "tags": [
    "scope:elements",
    "tier:{tier}",
    "type:app",
    "element:{name}",
    "framework:angular"
  ],
  "targets": {
    "build": {
      "executor": "@angular/build:application",
      "outputs": ["{options.outputPath}"],
      "options": {
        "outputPath": "dist/{name}",
        "browser": "apps/elements/{tier}/{name}/src/main.ts",
        "index": "apps/elements/{tier}/{name}/src/index.html",
        "tsConfig": "apps/elements/{tier}/{name}/tsconfig.app.json"
      },
      "configurations": {
        "production": {
          "budgets": [
            { "type": "initial", "maximumWarning": "80kb", "maximumError": "200kb" }
          ]
        }
      }
    },
    "serve": {
      "executor": "@angular/build:dev-server",
      "options": { "port": 43XX }
    },
    "lint": {
      "executor": "@nx/eslint:lint",
      "options": {
        "lintFilePatterns": [
          "apps/elements/{tier}/{name}/src/**/*.ts",
          "apps/elements/{tier}/{name}/src/**/*.html"
        ]
      }
    }
  }
}
```

### 6C. {name}.ts — Componente principal (standalone, zoneless, signal inputs)

```typescript
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Web Component scaffold for <synergos-{kebab}>.
 * CMS element: elementSyn{Pascal}.
 * Each CMS property alias becomes a TypeScript input.
 */
@Component({
  selector: 'sg-{kebab}',
  standalone: true,
  templateUrl: './{name}.html',
  styleUrl: './{name}.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'sg-{name}' },
})
export class {Pascal}ElementComponent {
  // Mirror de cada prop del ElementType (alias = nombre del input)
  readonly heading       = input<string | undefined>(undefined);
  readonly body          = input<string | undefined>(undefined);
  readonly imageSrc      = input<string | undefined>(undefined);
  readonly ctaLabel      = input<string | undefined>(undefined);
  readonly ctaUrl        = input<string | undefined>(undefined);
  readonly configOverride = input<string | undefined>(undefined);
  // compDom* (siempre presentes en elementSyn*)
  readonly cssClass      = input<string | undefined>(undefined);
  readonly variantKey    = input<string | undefined>(undefined);
}
```

**Reglas:**
- `readonly propName = input<string | undefined>(undefined)` — siempre string o undefined (los Web Component attrs son strings).
- Un input por prop del ElementType (aliases idénticos al uSync).
- NO usar `@Input()` decorator ni `BehaviorSubject`. Solo signal inputs.
- Si la prop es booleana en CMS (TrueFalse), el attr llega como `"true"`/`"false"` string — parsear en template.

### 6D. {name}.html — Template placeholder

```html
<!--
  Placeholder de {Pascal}. El diseño visual va aquí o en una
  implementación real del componente.
-->
<div
  class="sg-{name}__placeholder"
  [class]="cssClass() ?? ''"
  [attr.data-variant]="variantKey() ?? null"
  role="region"
  [attr.aria-label]="heading() ?? 'synergos-{kebab}'"
>
  @if (imageSrc()) {
    <img [src]="imageSrc()" [alt]="heading() ?? ''" class="sg-{name}__image" />
  }
  <div class="sg-{name}__content">
    @if (heading()) {
      <h2 class="sg-{name}__heading">{{ heading() }}</h2>
    }
    @if (body()) {
      <p class="sg-{name}__body">{{ body() }}</p>
    }
    @if (ctaLabel() && ctaUrl()) {
      <a [href]="ctaUrl()" class="sg-{name}__cta">{{ ctaLabel() }}</a>
    }
  </div>
</div>
```

### 6E. {name}.scss — Estilos mínimos

```scss
:host {
  display: block;
  max-width: 100%;
  container-type: inline-size;
}

.sg-{name}__placeholder {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 2rem;
  border: 1px dashed #cbd5e1;
  border-radius: 0.5rem;
  background: #f8fafc;
}

.sg-{name}__heading {
  font-size: clamp(1.5rem, 4cqi, 3rem);
  font-weight: 700;
  color: #0f172a;
  margin: 0;
}

.sg-{name}__body {
  color: #475569;
  margin: 0;
}

.sg-{name}__image {
  width: 100%;
  height: auto;
  object-fit: cover;
  border-radius: 0.25rem;
}

.sg-{name}__cta {
  display: inline-block;
  padding: 0.625rem 1.25rem;
  background: #0f58a7;
  color: #fff;
  text-decoration: none;
  border-radius: 0.25rem;
  font-weight: 500;
}
```

### 6F. app.config.ts

```typescript
import { ApplicationConfig, provideZonelessChangeDetection } from '@angular/core';

export const appConfig: ApplicationConfig = {
  providers: [provideZonelessChangeDetection()],
};
```

### 6G. main.ts — Registro del Custom Element

```typescript
import { createApplication } from '@angular/platform-browser';
import { createCustomElement } from '@angular/elements';
import { appConfig } from './app.config';
import { {Pascal}ElementComponent } from './{name}/{name}';

createApplication(appConfig).then((appRef) => {
  if (!customElements.get('synergos-{kebab}')) {
    const Element = createCustomElement({Pascal}ElementComponent, {
      injector: appRef.injector,
    });
    customElements.define('synergos-{kebab}', Element);
  }
});
```

### 6H. Actualizar registry.json

Agregar entrada al final del array en `C:\LOCAL_CDN\synergos\registry.json`:

```json
{
  "name": "{name}",
  "alias": "elementSyn{Pascal}",
  "tag": "synergos-{kebab}",
  "tier": "{tier}",
  "implementations": {
    "angular": { "latest": "0.1.0", "v0": "0.1.0" }
  }
}
```

### 6I. Actualizar vitals/contracts/

Agregar al final de `vitals/contracts/src/elements-syn.contract.ts`:

```typescript
/** elementSyn{Pascal} — tier:{tier} → tag:<synergos-{kebab}> */
export interface Syn{Pascal}Schema {
  readonly heading?: string;
  readonly body?: string;
  readonly imageSrc?: string;
  readonly ctaLabel?: string;
  readonly ctaUrl?: string;
  readonly cssClass?: string;
  readonly variantKey?: string;
  readonly configOverride?: string;
}
```

Agregar al `element-registry.json`:
```json
{ "name": "{name}", "alias": "elementSyn{Pascal}", "tag": "synergos-{kebab}", "tier": "{tier}" }
```

Luego regenerar el catálogo:
```bash
node Synergos.UI/tools/refresh-skill-catalog.mjs
```

---

## 7. Creación de contenido ~~vía Management API~~ → DEPRECADO en Umbraco 13

> ⛔ **Esta sección asume la Management API que NO existe en Umbraco 13 (404).** No la uses.
> La autoría real es server-side con `IContentService` — ver **`synergos-content-fill`** + **ADR 0093**.
> Se conserva abajo solo como referencia histórica del formato de valores (algunos formatos JSON aplican igual al valor de almacenamiento que recibe `IContentService.SetValue`).

### [HISTÓRICO] Creación de contenido vía API

### 7A. Generación de valores por tipo de campo

| EditorAlias | Formato valor API | Estrategia de generación |
|-------------|-------------------|-------------------------|
| `Umbraco.TextBox` | `"string"` | 1 frase editorial es-CO contextual al campo |
| `Umbraco.TextArea` | `"string\npárrafo2"` | 2-3 frases descriptivas sin HTML |
| `Umbraco.TinyMCE` | `"<p>HTML</p>"` | 2 párrafos con `<strong>` mínimo |
| `Umbraco.Integer` | `0` (int) | Inferir del alias: sortOrder→0, columns→3, max→10, height→400 |
| `Umbraco.TrueFalse` | `true`/`false` | hidden/disabled→false; active/enabled/featured→true |
| `Umbraco.DropDown.Flexible` | `"value"` | Leer DataType → tomar primer value; o el más contextual |
| `Umbraco.DateTime` | `"2026-06-06"` | publishDate/start→hoy; expiry/end→+1año |
| `Umbraco.MediaPicker3` | Ver §7C | Flujo completo de generación + upload |
| `Umbraco.ImageCropper` | Ver §7C | Flujo completo de generación + upload |
| `Umbraco.MultiUrlPicker` | `"[{\"name\":\"Ver más\",\"url\":\"/\",\"target\":null,\"queryString\":null,\"udi\":null}]"` | Link genérico coherente |
| `Umbraco.ContentPicker` | `"umb://document/{guid}"` | Buscar nodo root vía GET /document |
| `Umbraco.Tags` | `"tag1,tag2"` | 2-4 tags relevantes |
| `Umbraco.BlockList` | Ver §7D | Generar 1-3 ítems |
| `Umbraco.BlockGrid` | Ver §7E | Estructura mínima Section + Container |
| `Umbraco.Label` | **OMITIR** | Solo lectura — Umbraco lo calcula |

### 7B. Autenticación (reusar del pre-flight — token ~60min)

Guardar `$token` y `$headers` desde §1. Si expira, re-autenticar.

### 7C. MediaPicker3 / ImageCropper — Upload inline

```powershell
Add-Type -AssemblyName System.Drawing

function New-SynPlaceholder {
    param([string]$Title, [string]$Subtitle="", [int]$W=1200, [int]$H=630,
          [string]$Bg="#0F58A7", [string]$Out)
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml($Bg))
    $g.FillRectangle(
        (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(55,0,0,0))),
        0, [int]($H*0.65), $W, [int]($H*0.35))
    $fs   = [Math]::Max(32, [Math]::Min(62, $W/19))
    $font = New-Object System.Drawing.Font("Segoe UI", $fs, [System.Drawing.FontStyle]::Bold)
    $sf   = New-Object System.Drawing.StringFormat
    $sf.Alignment = $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($Title, $font, [System.Drawing.Brushes]::White,
        (New-Object System.Drawing.RectangleF(60, 50, ($W-120), ($H*0.58))), $sf)
    $font.Dispose()
    if ($Subtitle) {
        $fs2   = [Math]::Max(18, $fs*0.52)
        $font2 = New-Object System.Drawing.Font("Segoe UI", $fs2)
        $b2    = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210,255,255,255))
        $g.DrawString($Subtitle, $font2, $b2,
            (New-Object System.Drawing.RectangleF(60, [int]($H*0.63), ($W-120), [int]($H*0.28))), $sf)
        $font2.Dispose(); $b2.Dispose()
    }
    $g.FillRectangle([System.Drawing.Brushes]::White, 60, ($H-18), 72, 6)
    $g.Dispose()
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Upload-SynMedia {
    param([string]$Title, [string]$AltText, [string]$BgHex="#0F58A7",
          [int]$W=1200, [int]$H=630)
    # 1. Generar imagen
    $slug = ($Title -replace '[^a-zA-Z0-9]', '-').ToLower() -replace '-+', '-'
    $tmp  = [System.IO.Path]::Combine($env:TEMP, "syn-$slug-$([guid]::NewGuid().ToString('N').Substring(0,6)).png")
    New-SynPlaceholder -Title $Title -BgHex $BgHex -W $W -H $H -Out $tmp

    # 2. Crear nodo media
    $body = @{
        contentTypeKey = "bcc6d08c-509e-4ab6-8d8b-c00c6199253f"
        parentKey      = $null
        values         = @(@{ alias="altDefault"; value=$AltText; culture=$null; segment=$null })
    } | ConvertTo-Json -Depth 5 -Compress
    $node = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/media" `
        -Method POST -Headers $headers -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
    $mediaKey = $node.id

    # 3. Subir archivo
    Add-Type -AssemblyName System.Net.Http
    $client = [System.Net.Http.HttpClient]::new()
    $client.DefaultRequestHeaders.Add("Authorization", "Bearer $token")
    $mp = [System.Net.Http.MultipartFormDataContent]::new()
    $bc = [System.Net.Http.ByteArrayContent]::new([System.IO.File]::ReadAllBytes($tmp))
    $bc.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new("image/png")
    $mp.Add($bc, "file", [System.IO.Path]::GetFileName($tmp))
    $r = $client.PostAsync("$baseUrl/umbraco/management/api/v1/media/$mediaKey/file", $mp).GetAwaiter().GetResult()
    if (-not $r.IsSuccessStatusCode) {
        Write-Error "Upload fallido: $($r.Content.ReadAsStringAsync().GetAwaiter().GetResult())"; exit 1
    }
    $client.Dispose()
    Remove-Item $tmp -Force

    # 4. Retornar valor para el campo
    $pickerValue = "[{`"key`":`"$mediaKey`",`"mediaKey`":`"$mediaKey`",`"focalPoint`":null,`"crops`":[]}]"
    return @{ mediaKey = $mediaKey; pickerValue = $pickerValue }
}

# Uso:
$imgResult = Upload-SynMedia -Title "Hero de Home" -AltText "Imagen del hero principal" -BgHex "#0A2540"
$values += @{ alias="media"; value=$imgResult.pickerValue; culture="es-co"; segment=$null }
```

### 7D. BlockList — generar ítems

```powershell
function New-BlockListValue {
    param([string]$ElementTypeKey, [hashtable[]]$ItemProps, [int]$Count=2)

    $items = 1..$Count | ForEach-Object {
        $udi = "umb://element/$([guid]::NewGuid().ToString('N'))"
        @{ udi=$udi; contentTypeKey=$ElementTypeKey; props=$ItemProps }
    }

    $layout = @{
        "Umbraco.BlockList" = @($items | ForEach-Object { @{ contentUdi=$_.udi } })
    }
    $contentData = $items | ForEach-Object {
        $obj = @{ contentTypeKey=$_.contentTypeKey; udi=$_.udi }
        $_.props | ForEach-Object { $obj[$_.alias] = $_.value }
        $obj
    }

    return (@{ layout=$layout; contentData=@($contentData); settingsData=@() } | ConvertTo-Json -Depth 15 -Compress)
}

# Ejemplo — CTAs (usa elementActionButton Key: 2d8d712e-66ec-4052-a627-0ad8a68d0d38)
$ctaProps = @(
    @{ alias="label"; value="Ver más" }
    @{ alias="variant"; value="primary" }
)
$ctaValue = New-BlockListValue -ElementTypeKey "2d8d712e-66ec-4052-a627-0ad8a68d0d38" -ItemProps $ctaProps -Count 2
```

### 7E. BlockGrid — estructura mínima

```powershell
function New-MinimalSections {
    param([string]$SectionKey   = "1c68f4a9-24e9-49ac-9efa-05b3d4b1404a",   # elementLayoutSection
          [string]$ContainerKey = "f39c535a-879f-4bbf-8d94-8370c7f45f5a")   # elementLayoutContainer

    $sUdi = "umb://element/$([guid]::NewGuid().ToString('N'))"
    $cUdi = "umb://element/$([guid]::NewGuid().ToString('N'))"

    $layout = @{
        "Umbraco.BlockGrid" = @(@{
            contentUdi  = $sUdi
            areas       = @(@{
                key   = "sectionContent"
                items = @(@{ contentUdi=$cUdi; areas=@() })
            })
            columnSpan = 12
            rowSpan    = 1
        })
    }
    $contentData = @(
        @{ contentTypeKey=$SectionKey;   udi=$sUdi }
        @{ contentTypeKey=$ContainerKey; udi=$cUdi }
    )
    return (@{ layout=$layout; contentData=@($contentData); settingsData=@() } | ConvertTo-Json -Depth 15 -Compress)
}

$sectionsValue = New-MinimalSections
```

### 7F. Construir payload y crear documento

```powershell
$docPayload = @{
    documentTypeKey = $contentTypeKey  # GUID del ContentType
    parentKey       = $parentKey       # GUID del padre o $null
    values          = @($values)       # array de @{alias, value, culture, segment}
    variants        = @(@{
        culture     = "es-co"
        segment     = $null
        name        = $nodeName
        publishDate = $null
    })
} | ConvertTo-Json -Depth 15 -Compress

$doc    = Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/document" `
    -Method POST -Headers $headers -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($docPayload))
$docKey = $doc.id
```

### 7G. Publicar

```powershell
$pub = @{ publishSchedules=@(@{ culture="es-co"; schedule=$null }) } |
    ConvertTo-Json -Depth 5 -Compress
Invoke-RestMethod "$baseUrl/umbraco/management/api/v1/document/$docKey/publish" `
    -Method PUT -Headers $headers -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($pub))
```

---

## 8. Checklist completo antes de cerrar

### Si se creó schema nuevo:
- [ ] GUIDs generados con `[guid]::NewGuid()` y quad-checked
- [ ] XML uSync escrito en la carpeta correcta con encoding UTF-8
- [ ] Alias sigue naming convention (§4A)
- [ ] Icono verificado en `reference_umbraco13_icons.txt`
- [ ] Descripciones ≤120 chars, sin jargon ADR
- [ ] Se avisó al arquitecto para hacer uSync Import manual
- [ ] Si es `elementSyn*`: Razor wrapper + SynHost renderer creados (§5A, §5B)
- [ ] Si es `elementSyn*` CDN: Angular component creado (§6) + registry.json actualizado
- [ ] `vitals/contracts/elements-syn.contract.ts` actualizado
- [ ] `refresh-skill-catalog.mjs` ejecutado

### Si se creó contenido:
- [ ] ContentType alias existe en uSync (no inventado)
- [ ] Nodo padre verificado en el content tree
- [ ] Campos obligatorios (`Mandatory=true`) todos diligenciados
- [ ] Campos de imagen: alt text en `altDefault`
- [ ] Valores Dropdown extraídos del DataType real (no inventados)
- [ ] BlockGrid: GUIDs de layout presets verificados (§2D)
- [ ] Documento publicado (`state="Published"` verificado vía GET)

---

## 9. Troubleshooting

| Error | Causa | Solución |
|-------|-------|----------|
| `400` en POST /document | Formato de valor incorrecto | MediaPicker y BlockList son strings JSON, no objetos directos |
| `422` en POST /document | Campo mandatory faltante | Revisar `<Mandatory>true</Mandatory>` en el ContentType |
| `409 Conflict` | Nombre duplicado bajo mismo padre | Cambiar `name` en el payload |
| `401` en cualquier call | Token expirado | Re-autenticar desde §1 |
| uSync Import falla | GUID collision o XML malformado | Re-verificar quad-check; abrir XML en editor para validar |
| Razor `CS0234 IUmbracoHelper` | No usar `@inject IUmbracoHelper` en Razor | Usar `@inherits UmbracoViewPage<T>` + `ISynHostEmitter` (ADR 0059) |
| `System.Drawing` no carga | GDI+ no disponible | Usar fallback SVG (generar como texto plano) |
| Custom element no hidrata | bundle CDN no publicado | Normal — `StubBundleRegistryClient` emite placeholder. Bundle disponible solo cuando CDN team publique (ADR 0012) |
| Angular `NG0100` change detection | Zona activa (no zoneless) | Verificar `provideZonelessChangeDetection` en `app.config.ts` |
| `customElements.get` ya definido | Doble import del script | El guard `if (!customElements.get(...))` en `main.ts` previene esto |

---

## 10. Referencias del proyecto

- `Synergos.CMS/CLAUDE.md` — 10 principios + dónde está la verdad
- `refactor-docs/architecture/00-current-state-synergos-cms.md §11` — estado real
- `refactor-docs/architecture/06-composition-design-principles.md` — filtro 3 preguntas
- `Synergos.CMS.Web/docs/contracts/` — 5 contratos CMS↔UI (ADR 0083)
- `Synergos.CMS.Web/docs/adr/` — 92 ADRs ratificados
- `references/ui-elements-catalog.md` — 122 bundles publicados
- `references/cms-to-ui-mapping.md` — alias CMS ↔ tag DOM ↔ bundle URL
- `vitals/contracts/src/elements-syn.contract.ts` — schema mirrors TS
