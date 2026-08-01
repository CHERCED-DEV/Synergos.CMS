---
name: synergos-content-fill
description: Autoría server-side de contenido editorial en Synergos (Umbraco 13) con TODOS los campos diligenciados. Descubre los campos resolviendo la cadena de compositions, serializa cada valor por DataType para IContentService, aplica la regla de cultura por-propiedad, fuerza mandatory + alt text, y construye BlockGrid editor-safe. Umbraco 13 NO tiene Management API — la autoría es C# server-side detrás del flag DevSeed. Requiere CMS en http://synergos.local:5000.
model: claude-opus-4-8
---

# SYNERGOS Content Fill — completitud de campos server-side (IContentService)

Skill de **completitud**: poblar contenido editorial con cada campo correcto, por la vía nativa de Umbraco 13. Complementa a `synergos-cms-author` (que crea schema); aquí el foco es **llenar bien**.

> **CRÍTICO (ADR 0093):** Umbraco 13 **NO tiene Management API** (`/umbraco/management/api/*` → 404; el paquete `Umbraco.Cms.Api.Management` empieza en v14). La autoría programática es **C# server-side con `IContentService`/`IContentTypeService`/`IMediaService`**, detrás del flag `Synergos:DevSeed:Enabled` (ADR 0013), invocada por endpoints `/dev/*` del `DevController`. **Olvida el flujo token + POST /v1/document** — no existe aquí.

---

## 0. Reglas que no se rompen

- **No Management API.** Nada de `/umbraco/management/api`, `back-office/token`, `/v1/document`, `/v1/media`. Solo `IContentService` server-side. (ADR 0093)
- **Gated por flag.** Todo escribe detrás de `Synergos:DevSeed:Enabled=true`, por invocación explícita (`/dev/*`), nunca en boot. (ADR 0013)
- **Cultura por-propiedad.** `SetValue(alias, value, "es-CO")` si la prop es `Variations=Culture`; `SetValue(alias, value)` (culture null) si es `Nothing`. La variación de la PROPIEDAD manda, no la del ContentType.
- **Mandatory obligatorio.** Toda prop `Mandatory=true` debe tener valor antes de `SaveAndPublish`, o el publish falla (o el nodo queda inválido).
- **Alt text siempre.** `compContentMedia.mediaAlt` es mandatory (WCAG 1.1.1). Todo media lleva alt.
- **Props multi-value → `"[]"`.** En BlockGrid/BlockList, sembrar toda prop multi-value (DropDown.Flexible, MediaPicker3, MultiUrlPicker, Tags, CheckBoxList, MultiNodeTreePicker) con `"[]"` o el block editor revienta (`JsonReaderException`). Usar `SchemaBlockDefaults`. (ADR 0093, memoria [[feedback_serverside_blockgrid_authoring]])
- **GUIDs por alias en runtime.** `_contentTypeService.Get("alias").Key`, nunca hardcodear Keys. (ADR 0008)
- **No tocar ubicaciones no permitidas.** Respetar `AllowAtRoot` y `Structure` (allowed children) del DocType.
- **No seeders en boot, no schema desde content authoring** (ADR 0013 / 0008), **no `if (brand.Key == "X")`** (ADR 0010).

---

## 1. Pre-flight

```powershell
# CMS vivo (el sitio público responde; el keepalive ping da 404 en 13 — usar /)
try { Invoke-WebRequest "http://synergos.local:5000/" -UseBasicParsing -TimeoutSec 5 | Out-Null; "CMS OK" }
catch { Write-Error "CMS no responde. Arrancar con dotnet run en Synergos.CMS.Web/ (ver synergos-run-dev)"; exit 1 }

# DevSeed habilitado?
$ping = Invoke-RestMethod "http://synergos.local:5000/dev/ping"
if (-not $ping.devSeedEnabled) { Write-Error "Synergos:DevSeed:Enabled=false. Habilitar en appsettings.Development.json"; exit 1 }
"DevSeed ON"
```

La autoría se dispara invocando endpoints `/dev/*` (`[AllowAnonymous]`, no requieren token):
```powershell
Invoke-RestMethod "http://synergos.local:5000/dev/fill-synergos-pages" -Method POST
```

---

## 2. Arquitectura — dónde vive cada pieza

| Pieza | Archivo | Rol |
|-------|---------|-----|
| `SchemaBlockDefaults` | `Services/SchemaBlockDefaults.cs` | Defaults editor-safe `"[]"` por ElementType (recorre `CompositionPropertyTypes`) |
| `BlockGridJsonBuilder` | `Services/BlockGridJsonBuilder.cs` | Construye JSON de Umbraco.BlockGrid; `.ApplyDefaults(...)` por bloque |
| `DevContentFiller` | `Services/DevContentFiller.cs` | Puebla la prop `sections` de páginas existentes (no destructivo) |
| `SynergosIdentitySeeder` | `Services/SynergosIdentitySeeder.cs` | Crea el árbol fresco (platformRoot→siteRoot→pages) |
| `DevController` | `Controllers/DevController.cs` | Endpoints `/dev/*` gated por DevSeed |

**Para autorar contenido nuevo:** agrega un método al filler/seeder + un endpoint `/dev/*`. Registra servicios nuevos en `Composers/SeamComposer.cs`.

---

## 3. Paso 1 — Descubrir TODOS los campos (cadena de compositions)

Un DocType/ElementType hereda N compositions, cada una con sus props. `IContentType.CompositionPropertyTypes` devuelve el set **completo** (propias + heredadas), deduplicado.

```csharp
var ct = _contentTypeService.Get("elementCorpMissionBlock"); // por alias
foreach (var pt in ct.CompositionPropertyTypes)
{
    // pt.Alias, pt.PropertyEditorAlias, pt.Mandatory, pt.VariesByCulture()
}
```

Para inspeccionar el schema en disco (uSync), leer `uSync/v9/ContentTypes/{alias}.config`:
- `<Compositions><Composition Key="...">compX</Composition>` — la cadena (recursiva).
- `<GenericProperty>`: `Alias`, `Type` (EditorAlias), `Definition` (DataType Key), `Mandatory`, `Variations`.

Compositions universales que casi todo `element*` lleva: `compDomClass`, `compDomVariant`, `compDomVisibility`, `compDomAttributes` (+ a veces `compDomSpacing/Display/Flex/Grid`). `compCoreBase` (en pages) es `Variations=Nothing`.

---

## 4. Paso 2 — Serialización por DataType (valor para `IContentService.SetValue`)

`SetValue` recibe el **valor de almacenamiento** del editor (NO el formato de la Management API). Tabla canónica:

| EditorAlias | Valor a pasar a SetValue | Notas |
|-------------|--------------------------|-------|
| `Umbraco.TextBox` / `Umbraco.TextArea` | `"string"` | plano |
| `Umbraco.TinyMCE` (RichText) | `"<p>HTML</p>"` | markup permitido |
| `Umbraco.Integer` | `0` (int) o `"0"` | inferir del alias |
| `Umbraco.TrueFalse` | `true`/`false` (o `"1"`/`"0"`) | flags |
| `Umbraco.DropDown.Flexible` | `"[\"value\"]"` (array JSON string) | multi-value → si vacío `"[]"` |
| `Umbraco.DateTime` | `"2026-06-23"` | ISO |
| `Umbraco.MediaPicker3` | `"[{\"key\":\"<g>\",\"mediaKey\":\"<g>\",\"crops\":[],\"focalPoint\":null}]"` | vacío → `"[]"` |
| `Umbraco.MultiUrlPicker` | array JSON string | vacío → `"[]"` |
| `Umbraco.Tags` | `"[\"tag\"]"` | vacío → `"[]"` |
| `Umbraco.ContentPicker` | `"umb://document/<guid>"` | |
| `Umbraco.BlockGrid` | JSON `{layout, contentData, settingsData}` | ver §6 |
| `Umbraco.BlockList` | JSON `{layout, contentData, settingsData}` | similar a BlockGrid |
| `Umbraco.Label.*` | **OMITIR** | read-only, lo calcula Umbraco |

**Formatos VERIFICADOS en vivo (2026-06-23 — publican + renderizan):**
- MediaPicker3: `[{"key":"<freshGuid>","mediaKey":"<mediaNodeKey>","crops":[],"focalPoint":null}]` (string).
- MultiUrlPicker (Link): `[{"name":"<label>","url":"<url>","target":"","udi":null,"icon":null,"queryString":null}]` (string).
- TrueFalse: bool JSON `true`/`false` directo (no string).

> **Regla de oro multi-value:** dentro de `contentData` de un BlockGrid/BlockList, toda prop de editor multi-value debe tener un valor parseable como JSON. Ausente o `""` → `JsonReaderException`/`FailedPublishContentInvalid`. `SchemaBlockDefaults` siembra `"[]"`; aplícalo a CADA bloque (incluido el wrapper `elementLayoutSection`).
>
> **`SchemaBlockDefaults` une `PropertyTypes` + `CompositionPropertyTypes`** — `CompositionPropertyTypes` solo trae heredadas, NO las props PROPIAS del tipo (ej. `Hero.ctaItems`).

> ✅ **BlockList anidado (resuelto).** Bloques con un BlockList PROPIO (`elementCompHero.ctaItems`, `elementCompFeatureGrid.features`) NO pasan la validación con el BlockList vacío/ausente (`FailedPublishContentInvalid` en `sections`). Solución: poblarlo con **≥1 item real** vía `Services/BlockListJsonBuilder.cs` (`AddBlock(elementKey).Set(props).ApplyDefaults(_defaults.DefaultsFor(elementKey))`). Cada item cumple sus mandatory (ej. elementInfoFeature: headingTitle + mediaAlt; FeatureGrid.features es Mandatory min 1). Verificado: Hero (ctaItems → elementActionButton) y FeatureGrid (features → elementInfoFeature) autoran y renderizan. Ver `DevContentFiller.AddHero/AddFeatureGrid`.

**Media server-side (seam `DevMediaFactory`, ADR 0093):** `IMediaService.CreateMedia(name, -1, "synImage")` + `media.SetValue(MediaFileManager, MediaUrlGeneratorCollection, IShortStringHelper, IContentTypeBaseServiceProvider, "umbracoFile", fileName, stream)` + `SetValue("altDefault", alt)` + `Save`. Imágenes generadas con SixLabors.ImageSharp (ya viene con Umbraco). Devuelve el valor MediaPicker3.

Si dudas del formato exacto de un editor, **verifícalo empíricamente** (§8 round-trip + logear `PublishResult.InvalidProperties`), no asumas.

---

## 5. Paso 3 — Regla de cultura por-propiedad

```csharp
// Variations=Culture  → culture explícita
node.SetValue("heading", "Título", "es-CO");
// Variations=Nothing  → culture null (datos compartidos: flags, enums, configOverride, internalNotes)
node.SetValue("isActive", true);
// Nombre del nodo (culture-variant):
node.SetCultureName("Home", "es-CO");
// Publicar:
_contentService.SaveAndPublish(node, new[] { "es-CO" });
```

Leer `pt.VariesByCulture()` (o `<Variations>` en el XML) **por propiedad**. Una página `Variations=Culture` puede tener props `Nothing` (ej. `compCoreBase.internalNotes`) y viceversa — gana la de la propiedad.

---

## 6. Paso 4 — Construir BlockGrid (Layout Composer) editor-safe

La prop del cuerpo en `pageBase` es **`sections`** (`Umbraco.BlockGrid`, `Variations=Culture`). El cierre es `sectionsAfterBody`.

```csharp
var b = new BlockGridJsonBuilder();
var sectionKey = _contentTypeService.Get("elementLayoutSection").Key;
var missionKey = _contentTypeService.Get("elementCorpMissionBlock").Key;
var areaKey = new Guid("3525d41c-ae84-47ac-9297-2148f6a4aae8"); // sectionContent (de DTBlockGridSections)

var section = b.AddTopLevelBlock(sectionKey);
section.ApplyDefaults(_defaults.DefaultsFor(sectionKey));            // ← "[]" multi-value del wrapper
section.AddChild(areaKey, missionKey, m => m
    .Set("headingTitle", "Una plataforma. Mil productos.")
    .Set("headingSubtitle", "Un código, un schema, infinitos productos.")
    .Set("textBody", "...")
    .Set("mediaAlt", "Synergos")                                    // ← mandatory
    .ApplyDefaults(_defaults.DefaultsFor(missionKey)));             // ← "[]" multi-value del hijo

page.SetValue("sections", b.Build(), "es-CO");
_contentService.SaveAndPublish(page, new[] { "es-CO" });
```

**Bloques SSR vs CDN:** familias `elementCorp*`, `elementInfo*`, `elementText*`, `elementMedia*`, `elementLayout*` renderizan server-side (visibles). Familias `elementSyn*` y varios `elementComp*` son CDN-hosted → emiten placeholder hasta que el CDN publique (ADR 0012). Para contenido visible hoy, preferir SSR.

---

## 7. Paso 5 — Crear/llenar y publicar

**Crear nodo nuevo:**
```csharp
var page = _contentService.Create("Home", parentId, "pageBase");
page.SetCultureName("Home", "es-CO");
page.SetValue("heading", "Título", "es-CO");
page.SetValue("seoTitle", "Home — Synergos", "es-CO");
page.SetValue("sections", blockGridJson, "es-CO");
var save = _contentService.SaveAndPublish(page, new[] { "es-CO" });
if (!save.Success) { /* log save.Result (e.g. mandatory faltante) */ }
```

**Actualizar nodo existente (no destructivo):** buscar por nombre culture-aware (`GetCultureName("es-CO") ?? Name`), `SetValue(...)` solo las props a cambiar, `SaveAndPublish`.

Padre: `Constants.System.Root` para raíz; o el `Id` del padre. Respetar `Structure` (allowed children) del DocType.

---

## 8. Paso 6 — Auto-verificación (round-trip)

Como el formato de algunos editores es sutil, **verifica empíricamente** antes de dar por bueno:
1. Tras `SaveAndPublish`, abre el nodo en el backoffice (`/umbraco` → Content → página → tab Contenido) y confirma en el **preview del Layout Composer** (plugin `App_Plugins/LayoutComposer/`, con sus views `block-section.html`/thumbnails) que cada bloque aparece como sección estilizada, que carga **sin** "Could not render" ni error, y que el overlay del bloque mapea todas las props. Este preview in-editor es la verificación visual primaria.
2. Abre la página pública (`/synergos/{slug}`) y confirma que los bloques SSR renderizan (busca el texto en el HTML).
3. Si el block editor revienta → falta `"[]"` en alguna prop multi-value (revisa `SchemaBlockDefaults`). Si el frontend dice "Could not render: X" → el partial `blockgrid/Components/X.cshtml` debe usar `@model BlockGridItem` no-genérico (ADR 0093).

---

## 9. Checklist antes de cerrar

- [ ] ContentType alias existe (`_contentTypeService.Get(alias)` ≠ null).
- [ ] Todos los campos `Mandatory=true` de la cadena de compositions, diligenciados.
- [ ] Cultura correcta por propiedad (`es-CO` vs null) según `VariesByCulture()`.
- [ ] Media con `mediaAlt`.
- [ ] Props multi-value de cada bloque sembradas con `"[]"` (`ApplyDefaults`).
- [ ] `SaveAndPublish` retornó `Success=true`.
- [ ] Verificado en backoffice (editor carga) **y** frontend (SSR renderiza).
- [ ] GUIDs resueltos por alias, no hardcodeados.

---

## 10. Troubleshooting

| Síntoma | Causa | Solución |
|---------|-------|----------|
| `JsonReaderException` al abrir bloque en backoffice | prop multi-value ausente/vacía | `ApplyDefaults` con `"[]"` en TODA prop multi-value del bloque |
| Frontend: "Could not render component of type: X" | partial `blockgrid/Components/X.cshtml` con `@model BlockGridItem<IPublishedElement>` | cambiar a `@model BlockGridItem` no-genérico (ADR 0093) |
| `SaveAndPublish` `Success=false` | campo mandatory faltante o valor mal serializado | log `save.Result`; revisar mandatory + formato del editor |
| Bloque CDN no aparece en frontend | bundle CDN no publicado (ADR 0012) | esperado — placeholder hasta que el CDN publique; usar bloque SSR si necesitas visible |
| Texto con acentos corrupto | encoding | el JSON lo serializa System.Text.Json bien; en edición masiva de archivos usar `[IO.File]::WriteAllText` UTF-8 ([[feedback_powershell_utf8_bulk_edits]]) |
| `/dev/*` da 404 | `DevSeed:Enabled=false` | habilitar en `appsettings.Development.json` |

---

## 11. Referencias

- **ADR 0093** — Autoría server-side IContentService (no Management API). La decisión que rige este skill.
- ADR 0013 — Cero seeders; tooling dev tras flag.
- ADR 0008 — uSync SSOT; GUIDs por alias.
- ADR 0012 — CDN consumido; bloques CDN como placeholder.
- Memorias: [[project_umbraco13_no_management_api]], [[feedback_serverside_blockgrid_authoring]], [[feedback_variations_culture_default]], [[feedback_picker_semantics]].
- Código de referencia: `Services/{SchemaBlockDefaults,BlockGridJsonBuilder,DevContentFiller,SynergosIdentitySeeder}.cs`, `Controllers/DevController.cs`.
- Hermanos: `synergos-cms-author` (crea schema), `synergos-media-upload` (media — pendiente realinear a IMediaService).
