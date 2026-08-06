# ADR 0093 — Autoría de contenido server-side vía IContentService (Umbraco 13 no tiene Management API)

- **Status:** Accepted
- **Date:** 2026-06-23
- **Deciders:** Arquitecto + agente.

## Context

El stack de skills de autoría (`synergos-cms-author` §7, `synergos-media-upload`, y el planeado `synergos-content-fill`) se diseñó sobre la **Management API REST** de Umbraco (`POST /umbraco/management/api/v1/security/back-office/token`, `/v1/document`, `/v1/media`). Un probe en vivo contra el CMS 13.13.1 corriendo confirmó que **toda `/umbraco/management/api/*` devuelve 404** (igual que `/umbraco/swagger`). Evidencia triple:

1. El paquete `Umbraco.Cms.Api.Management` en nuget.org **empieza en `14.0.0-rc1`** — no existe ninguna versión 13.x.
2. `Umbraco.Cms.Targets 13.13.1` (lo que arrastra el meta `Umbraco.Cms`) depende solo de `Api.Delivery` + `StaticAssets`; **nunca de `Api.Management`**.
3. El sitio público y el **backoffice clásico AngularJS** (`/umbraco`, `/umbraco/login`) responden 200; la Management API y Swagger, 404.

Conclusión: **la Management API es una característica de Umbraco 14+.** Como ADR 0001 pinea 13.13.1 sin subir a 14+, esa API no está disponible y no se va a habilitar. La autoría programática debe usar la vía nativa de Umbraco 13: **C# server-side con `IContentService` / `IContentTypeService` / `IMediaService`**.

Al ejercer esa vía aparecieron dos trampas que build + xUnit verdes NO atrapan (categoría `feedback_runtime_only_bugs_uncaught_by_tests`):

- **Editor backoffice**: construir BlockGrid JSON en código dejando props multi-value (DropDown.Flexible, MediaPicker3, MultiUrlPicker, Tags, CheckBoxList, MultiNodeTreePicker) ausentes/vacías rompe el block editor con `JsonReaderException` en `MultipleValueEditor.ToEditor`. Por eso `SynergosIdentitySeeder` construía el BlockGrid JSON y lo **descartaba** (`_ = sectionsJson`), dejando páginas con cuerpo vacío.
- **Render frontend**: los 150 component partials `Views/Partials/blockgrid/Components/*.cshtml` declaraban `@model BlockGridItem<IPublishedElement>` (genérico), pero Umbraco 13 pasa `BlockGridItem` **no-genérico** → `InvalidOperationException` capturada por `items.cshtml` → "Could not render component of type: X". **Ningún bloque renderizaba**, latente porque nunca hubo contenido que lo ejerciera.

## Decision

1. **La autoría programática de contenido en este proyecto usa `IContentService` server-side**, no la Management API. La Management API queda explícitamente fuera de alcance mientras rija ADR 0001.

2. **Gating (ADR 0013):** todo el tooling de autoría vive detrás de `Synergos:DevSeed:Enabled` y se dispara por invocación explícita (endpoints `/dev/*` en `DevController`, `[AllowAnonymous]`, 404 cuando el flag está off). No auto-ejecuta en boot.

3. **`SchemaBlockDefaults`** (`Services/SchemaBlockDefaults.cs`): recorre `IContentType.CompositionPropertyTypes` y siembra cada prop multi-value (por `PropertyEditorAlias`) con array JSON vacío `"[]"`, haciendo el BlockGrid JSON editor-safe. `BlockGridJsonBuilder.ApplyDefaults` lo aplica por bloque sin pisar valores reales.

4. **GUIDs por alias en runtime**: los Keys de ElementType se resuelven vía `IContentTypeService.Get(alias)` en lugar de hardcodearse (alinea con ADR 0008 / `feedback_no_preassigned_guids_usync`). El area key del DataType (`DTBlockGridSections`) se mantiene como constante (no es un ContentType).

5. **Ciclo de escritura**: `Create` → `SetValue(alias, value, "es-CO")` para props Variations=Culture (`culture:null` para Nothing) → `SaveAndPublish(content, new[] { "es-CO" })`. La prop del Layout Composer en `pageBase` es `sections` (Umbraco.BlockGrid, Variations=Culture). `mediaAlt` (compContentMedia) es mandatory por WCAG.

6. **Component partials de BlockGrid usan `@model Umbraco.Cms.Core.Models.Blocks.BlockGridItem` (no-genérico).** `Model.Content` (IPublishedElement) y `Model.Areas` siguen funcionando. Corregidos los 150 partials.

7. **Skills afectados se realineanan a `/dev/*` IContentService**: `synergos-content-fill` (nuevo) se escribe sobre esta vía; `synergos-cms-author` §7 y `synergos-media-upload` se anotan/corrigen para no usar la Management API.

## Consequences

**Positivas**

- Autoría de contenido programática **funciona** en Umbraco 13 (verificado en vivo: Home/Identidad/Contacto pobladas y renderizando SSR end-to-end).
- Se cierra el blind-spot de governance: la realidad Management-API queda documentada.
- Respeta ADR 0001 (no 14+) y ADR 0013 (DevSeed flag).
- El fix de los partials desbloquea el render de **todos** los bloques SSR, no solo el contenido seeded.

**Negativas**

- Los bloques **CDN-hosted** (`elementSyn*` y algunos `elementComp*`) siguen emitiendo placeholder hasta que el CDN publique sus bundles (ADR 0012) — separado, esperado.
- Sin path de media server-side todavía (`IMediaService` aún no usado); MediaPicker3 queda vacío hasta el incremento que añada el seam. `mediaReference` es opcional, así que las páginas funcionan sin imagen.
- Tooling de autoría acoplado a Umbraco runtime (no API externa); un cliente externo no puede autorar sin pasar por `/dev/*`.

**Neutras**

- Archivos nuevos: `Services/SchemaBlockDefaults.cs`, `Services/DevContentFiller.cs`. Modificados: `Services/BlockGridJsonBuilder.cs`, `Controllers/DevController.cs`, `Composers/SeamComposer.cs`, 150 `blockgrid/Components/*.cshtml`.
- 0 paquetes NuGet nuevos. 0 tests nuevos (diferidos por decisión del owner hasta estabilizar; pendiente reactivar ADR 0075 para este seam).

## Alternatives considered

- **Subir a Umbraco 14+ para recuperar la Management API** — rechazado: viola ADR 0001 (churn Bellissima, Block Grid Lit/TS, .NET 9+, riesgo solo-dev). Revisitar solo como decisión estratégica aparte antes del cutoff LTS de 13 (oct 2026).
- **Autoría 100% manual por backoffice clásico** — válido como fallback documentado (`feedback_neutral_backoffice_instructions`), pero no programable: no cumple el objetivo de `content-fill`.
- **API REST custom propia para autoría** — rechazado: abstracción prematura (CLAUDE.md §6); `IContentService` ya es la seam nativa.

## References

- ADR 0001 — Umbraco 13 LTS pin (no 14+).
- ADR 0008 — uSync hybrid source-of-truth (GUIDs por alias).
- ADR 0012 — CDN contract consumed (bloques CDN-hosted como placeholder).
- ADR 0013 — Cero seeders; tooling dev detrás de flag.
- `feedback_serverside_blockgrid_authoring` (memoria — las 2 trampas).
- `project_umbraco13_no_management_api` (memoria — el hecho verificado).
- Verificado en vivo 2026-06-23: `/dev/fill-synergos-pages` → 3 páginas pobladas; `/synergos/home|identidad|contacto` renderizan SSR sin errores.
