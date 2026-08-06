# ADR 0042 — Error pages transversales + DropdownOptions consts + Typed views first batch + audit verbal SSR/CompositionReader (Olas 73-77)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch de olas 73-77 — disparado
  por petición *"procede! super bien"* tras Ola 72 (audit Lego).
- **Scope:** 5 mejoras pequeñas-medianas ejecutadas en secuencia,
  consolidadas en un ADR único por proximidad temática.
- **Extiende:** ADR 0040 (Gran Consolidación), ADR 0041 (Mapa Lego)

## Context

La Ola 72 (audit Lego) detectó 5 mejoras concretas que no requieren
refactor estructural pero cierran gaps operacionales:

1. **Verificar coverage SSR de los 71 elementSyn*** — confirmar que
   no hay element types sin partial Razor.
2. **Errores transversales** — gap deferido desde Ola 70 (las
   transversales repository tenía alerts/modals/banners/footer notes
   pero NO error pages).
3. **`ICompositionReader` audit** — verificar si el seam soporta la
   resolución de composiciones anidadas (ej. siteConfiguration que
   compone los 3 settings legacy).
4. **DTSelect → C# consts** — eliminar magic strings en runtime
   (reemplazar `chromeMode == "bare"` por
   `DropdownOptions.ChromeMode.Bare`).
5. **Typed views via UmbracoViewPage<T>** — empezar refactor de las
   views a typed models post-ModelsBuilder SourceCodeAuto (Ola 71.4).

## Decision por ola

### Ola 73 — Audit SSR coverage (verbal, sin commits)

Verifié que los 71 `elementSyn*` configs en uSync tienen los 3
artefactos:
- 71 partial Razor en `Views/Partials/SynHost/*.cshtml`
- 71 wrapper en `Views/Partials/blockgrid/Components/elementSyn*.cshtml`
- 1 helper común `_Wrapper.cshtml`

Cada partial sigue patrón uniforme: inyecta `ISynHostEmitter`,
empaqueta props en `Dictionary<string, object?>`, llama
`EmitAsync(SynHostEmitRequest)`, renderiza vía `_Wrapper` (que aplica
compDom* del editor).

Cuando el CDN registry no responde (Stub), el emitter fallback emite
HTML comment + custom element tag — el SSR siempre produce algo
visible. **Coverage 100%, no hay gaps.**

### Ola 76 — Errores transversales (3 commits)

Schema (`uSync/v9/ContentTypes/`):
- **`transversalErrorPagesFolder`** (GUID `8b7aca1a`) — IsListView,
  Variations=Nothing, allow `transversalErrorPage` solo.
- **`transversalErrorPage`** (GUID `436bafd1`) — Document, Variations=Culture,
  compone `compCoreBase` + `compSeo`. 5 props: `statusCode` (TextBox
  regex `^[1-5]\d{2}$`), `errorTitle`, `errorBody` (TinyMCE),
  `showSearchBox`, `showHomeLink`.
- **`transversalsRepository.Structure`** += `transversalErrorPagesFolder`
  (5to child SortOrder=40 después de alerts/modals/banners/footerNotes).

Runtime:
- **`ErrorController`** (`[Route("error")]`, `[HttpGet("{statusCode:int}")]`):
  - `Response.StatusCode = statusCode` (preserva semantica HTTP — sin
    esto la re-execute devuelve 200, contrabajando el contrato).
  - Busca `transversalErrorPage` publicado con matching statusCode.
  - Si encuentra → `ErrorPageViewModel` con title/body/showSearchBox/
    showHomeLink del editor.
  - Si no → fallback inline (`FallbackTitleFor` map: 404/500/503
    semantic, generic para resto).
- **`Views/Error.cshtml`** Layout=null (cero chrome para evitar
  cascadas que también fallen), `meta robots=noindex`, dictionary
  keys `Error.SearchLabel` / `Error.BackToHome` con fallback inline.
- **`Program.cs`** `app.UseStatusCodePagesWithReExecute("/error/{0}")`
  wired ANTES de `UseUmbraco` — captura todos los error codes (404
  routing + 5xx runtime).

### Ola 77 — `ICompositionReader` audit (verbal, sin commits)

`ICompositionReader<TInput, TOutput>` es un mapper genérico (Read
single source → typed output). NO tiene rol en resolución recursiva
de composiciones — esa preocupación se resuelve transparentemente
por **Umbraco mismo**: cuando un Document type compone otro vía
`<Compositions>`, las property definitions del compuesto son
copiadas al published cache del compositor. `IPublishedContent.
Value<T>(alias)` accede directamente sin necesidad de hop.

Verificación: `siteConfiguration` (Ola 71.7) compone `themeSettings` +
`siteConfigSettings` + `featureFlagsSettings` — el editor que abre
un nodo `siteConfiguration` ve los tabs de los 3 combinados, y el
runtime puede leer `node.Value<string>("primaryColor")` sin hops
extras. **Sin refactor necesario.**

### Ola 74 — DropdownOptions consts (1 commit)

**`Synergos.CMS.Application/Constants/DropdownOptions.cs`**: 13 nested
static classes con `const string` mirroring de los DTSelect* más usados:

- `PageThemeVariant`, `PageSurface` (theme triplet)
- `ChromeMode`, `HeaderMode`, `FooterMode` (orchestration)
- `AlertVariant`, `AlertTone`, `BannerPlacement`, `ModalTrigger`,
  `ModalFrequency` (transversales)
- `ContainerType`, `SpacingScale` (layout)
- `ShopSort` (shop query)

**Source of truth**: los XML uSync. Este const class es **mirror manual**
(dual-write trade-off documentado). Solo se mirroran los DTSelect que
runtime C# branchea — editorial-only (AriaRole, AspectRatio) quedan
como raw strings (no hay branching).

Adopción inicial: `DefaultPageRenderContextResolver` ahora usa
`DropdownOptions.ChromeMode.None/Bare/Embedded` en `ChromeAllowsChrome`
y `DropdownOptions.HeaderMode.Hidden` / `FooterMode.Hidden` en
`showHeader/showFooter` calc. Los demás consumers (DefaultGlobalComponentResolver,
DefaultShopQuery) quedan TODO de adopción gradual — no gating.

### Ola 75.1 — Typed views first batch (1 commit)

Refactor de 2 views a `@inherits UmbracoViewPage<TypedModel>`:

- **`PlatformRoot.cshtml`**: `@inherits UmbracoViewPage<Synergos.CMS.Web.PublishedModels.PlatformRoot>`.
  `Model.WelcomeMessage`, `Model.IntroBody`, `Model.Children<SiteRoot>()`
  con prop access tipado (`site.SiteDisplayName`, `site.CanonicalHostname`).

- **`SearchPage.cshtml`**: `@inherits UmbracoViewPage<Synergos.CMS.Web.PublishedModels.SearchPage>`.
  `Model.PageHeading`, `Model.PageIntro`, `Model.ItemsPerPage`,
  `Model.Url()`.

**15+ views adicionales** (PageBase/Basic/Landing/Bare, PostPage,
ProductPage, Account/*, FlowDefinition/Step, Error, `_Layout`) quedan
TODO de refactor gradual. No gating — los typed models están listos
en `umbraco/models/*.generated.cs` (240 archivos generados tras Ola
71.4 + reboot del runtime).

## Consequences

**Positivas:**

- **Errores con UX editorial completa**: editor crea `transversalErrorPage`
  con statusCode=404 + title custom + searchbox toggle → renderea cuando
  ASP.NET dispara 404. Sin código adicional. Para cada status code, el
  editor crea un nodo nuevo o usa el fallback inline.
- **Compile-time check en runtime crítico**: `DefaultPageRenderContextResolver`
  ya no rompe si alguien renombra `"bare"` a `"stripped"` en el XML
  sin actualizar el resolver — el `DropdownOptions.ChromeMode.Bare`
  apunta al string canónico, IDE find-references muestra todos los
  consumers en 1 click.
- **Typed views entry-point**: las 2 views refactoradas (PlatformRoot,
  SearchPage) son referencia para futuras refactores. El patrón es
  trivial de replicar: `@inherits UmbracoViewPage<PublishedModels.X>`
  + reemplazar `Model.Value<T>("alias")` por `Model.Alias`.
- **SSR coverage verified**: cero piezas elementSyn* sin renderer —
  fallback funciona aún sin CDN registry.
- **Composition resolution clarified**: el modelo Lego de Olas 70-71
  funciona transparente — Umbraco copia property definitions de
  composables al compositor. No hay overhead runtime en la composition
  chain.

**Negativas:**

- **DropdownOptions es dual-write**: si alguien agrega una option al
  XML sin mirror al .cs, los consumers que usan `DropdownOptions.X`
  no la conocerán. Mitigación: documentación explícita en la clase
  + adopción gradual (los consumers de magic strings siguen
  funcionando hasta ser refactoreados).
- **15+ views legacy sin typed**: quedan untyped por ahora. Sin gating;
  típica deuda técnica controlada — refactor cuando el dev toque la
  view por otra razón.
- **`Error.cshtml` Layout=null**: cero chrome del sitio. Trade-off
  deliberado (chrome cascading puede causar fallos secundarios en una
  page de error). Si el operador quiere chrome custom en errores,
  futura ola puede agregar `errorChromeBlocks` BlockGrid en
  `transversalErrorPage`.

**Neutras:**

- 7 commits en 5 olas + 1 fix lateral (76.1b wire de
  transversalErrorPagesFolder al Structure del repo).
- 6 GUIDs nuevos en Ola 76 (1 folder + 1 DocType + 4 properties +
  1 tab key). Verificación cuádruple OK.
- Cero impacto en seams o resolvers existentes.

## Implementation summary (Olas 73-77, 7 commits)

| # | Hash | Foco |
|---|---|---|
| 73 | (audit) | SSR coverage verified — 71/71 elementSyn* tienen partial Razor |
| 76.1 | `1592b1a` | Schema `transversalErrorPagesFolder` + `transversalErrorPage` + wire en repo |
| 76.1b | `5c60fd9` | Fix wire `transversalErrorPagesFolder` al Structure (Edit fallo en 76.1) |
| 76.2 | `b797642` | `ErrorController` + `Views/Error.cshtml` + `UseStatusCodePagesWithReExecute` en Program.cs |
| 77 | (audit) | `ICompositionReader` — sin refactor (Umbraco resuelve transparente) |
| 74 | `a79389a` | `DropdownOptions` const class (13 nested static classes) + adopt en `DefaultPageRenderContextResolver` |
| 75.1 | `607bd5d` | Typed views: PlatformRoot + SearchPage refactor a `UmbracoViewPage<T>` |
| 0042 | (este) | ADR consolidado |

## Próximas direcciones

- **Ola 75.2**: continuar typed views refactor — las 15+ views legacy
  cuando aplique necesidad.
- **Ola 78**: backoffice section custom para gestionar transversales
  (lista flat de todos los nodos transversal* con quick actions:
  publicar/despublicar, schedule edit). Hoy editor navega árbol —
  custom UI mejora la UX.
- **Ola 79**: error pages templates editorial con Block Grid (`errorBlocks`)
  para diseños complejos en lugar de solo TinyMCE body.

## References

- ADR 0040 — Gran Consolidación (siteConfiguration unifies 3 legacy)
- ADR 0041 — Mapa Lego canónico (audit base)
- ADR 0030 — Forms internal (referente del patrón Controller + ViewModel)
- ADR 0033 — SEO infrastructure (referente del Controller + status code)
- Memoria `feedback_no_preassigned_guids_usync` — 6 GUIDs Ola 76
  cuádruple-verificados.
