# ADR 0032 — Search UX: searchPage DocType + SearchPage.cshtml (Ola 62)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 62
- **Extiende:** ADR 0031 (Search infrastructure on Examine)
- **Cierra:** Search UX layer (módulo end-to-end navegable público)

## Context

ADR 0031 cerró la infraestructura backend de Search:
`ISearchQuery` + `ExamineSearchProvider` + `SearchController` JSON
endpoint. Pero el sitio recién deployado seguía sin una **página
pública navegable** donde el visitante pudiera escribir un query y ver
resultados sin que el frontend le construyera el wrapping con JS.

Necesitábamos:
- Un DocType editorial para que el arquitecto cree "/search" como
  child del siteRoot.
- Un template Razor que lea `?q=` directamente y renderice resultados
  SSR.
- Paginación nativa `?page=N`.
- Filtro opcional `?docType=alias` (compatible con futuros menús
  facetados).
- Accesibilidad: form `role="search"`, ARIA live, semantic HTML, sin
  dependencia de JS.

## Decision

### Schema (Ola 62.1)

**`searchPage`** DocType (`Folder=Search`, `Variations=Culture`,
`AllowAtRoot=False`, `IsListView=False`, GUID
`a391916d-0522-446d-95aa-5fbd4f0d807b`):

- Compone `compCoreBase` + `compSeo` + `compCoreLifecycle`.
- `DefaultTemplate=SearchPage` + `AllowedTemplates=[SearchPage]`.
- 3 props:
  - `pageHeading` (TextBox, Culture, opcional) — default dictionary
    `Search.Heading`.
  - `pageIntro` (TextArea, Culture, opcional) — texto breve antes del
    input.
  - `itemsPerPage` (TextBox, Nothing, regex `^\d+$`, opcional, default
    20).
- `Structure>` vacío (no hijos).

**`SearchPage` Template** (GUID
`135846f4-fa6d-4029-bbe8-7ee0971334ce`).

**`siteRoot.config` Structure** ampliado: agregado `searchPage` +
`postCategoryPage` + `productCategoryPage` que estaban faltando del
allow-list de hijos del siteRoot (gap latente desde Olas 56/57 — los
DocTypes existían y tenían renderer, pero el editor no podía crearlos
como child del siteRoot via UI sin agregar Allowed Child Types
manualmente).

### Template (Ola 62.2)

`Views/SearchPage.cshtml`:

- `Layout = "_Layout"`. Inyecta `ISearchQuery` + `IHttpContextAccessor`.
- Lee querystring `q`, `page` (1-based), `docType`.
- `itemsPerPage` del DocType property con fallback 20.
- Si `q` vacío: solo renderiza el form (no ejecuta query).
- Si `q` no vacío: `SearchQuery.Search(new SearchRequest(...))` con
  `Skip = (page - 1) * itemsPerPage`.
- Form `role="search"`, `method="get"`, `action="@basePath"` —
  navegación nativa preserva URL bookmarkable.
- Si hay `docType` filter, hidden input lo preserva al re-submit.
- Resultado:
  - `aria-live="polite"` en el `<div class="syn-search__meta">` para
    screen readers.
  - `Search.NoResults` con `{query}` placeholder (Replace).
  - `Search.ResultCount` con `{n}` placeholder + sufijo `(Xms)` para
    telemetría visible al usuario.
  - Lista `<ul>` de hits — cada `<li>` con `data-doc-type="{alias}"`
    para que CSS/JS pueda agruparlos.
  - Excerpt opcional. `Score` no se muestra (solo debugging).
- Paginación: prev/next con `rel="prev|next"` + `<span>` "X / Y".
  Helper `PaginationHref(targetPage)` preserva `q` + `docType`.

### Dictionary (Ola 62.2)

5 entries nuevas (es-CO + en-US, `Parent=Search`):
- `Search.Heading` — "Buscar" / "Search"
- `Search.Label` — "Buscar en el sitio" / "Search the site"
- `Search.Pagination` — "Paginación de resultados" / "Search results pagination"
- `Search.Previous` — "Anteriores" / "Previous"
- `Search.Next` — "Siguientes" / "Next"

Reusa los existentes (legacy placeholders desde Ola 0):
- `Search.Button`, `Search.Placeholder`, `Search.NoResults` (`{query}`),
  `Search.ResultCount` (`{n}`).

## Consequences

**Positivas:**

- **End-to-end navegable**: el arquitecto crea `/search` debajo del
  siteRoot, configura `pageHeading` opcional, publica → URL pública
  `/search` o `/search?q=foo` funcional sin escribir línea de código.
- **Bookmark-able**: el form GET preserva el query en la URL — el
  visitante puede compartir `/search?q=marketing`.
- **Accesible sin JS**: form nativo, `role="search"`, `aria-live`,
  semantic HTML. Validators de a11y modernos no flaggean.
- **Multi-cultura first**: `pageHeading` + `pageIntro` con
  `Variations=Culture`. Dictionary entries en es-CO + en-US.
- **DocType filter ready**: la UI puede agregar facets
  (`?docType=postPage`) y la paginación los preserva — base para
  futuras "Buscar solo en blog" / "Buscar solo en productos".
- **siteRoot Structure fix**: efecto colateral positivo — corrige
  el gap latente de Olas 56/57 donde `postCategoryPage` y
  `productCategoryPage` no estaban en el allow-list del siteRoot.

**Negativas:**

- **Sin highlighting de matches**: hereda la limitación del provider
  (ADR 0031). El excerpt es del campo `excerpt`/`seoDescription`/
  `summaryText`, no del texto matching. Diferido a versión con
  Lucene `SimpleHTMLFormatter`.
- **Sin facets agregados visibles**: la UI solo muestra resultados
  planos. Para "3 páginas, 2 posts, 1 producto" se necesita
  Aggregations o un segundo query con filter. Diferido.
- **Paginación 1-based en UI vs 0-based en API**: el template usa
  1-based (más natural para usuarios) y traduce a `Skip` 0-based al
  llamar `ISearchQuery`. Inconsistencia menor entre UI y API JSON.
- **Sin auto-suggest / autocomplete**: el form es submit-on-enter.
  Para autocomplete on-key, design-system frontend puede consumir
  `/api/search?q=` con fetch — el JSON endpoint existe desde Ola 61.

**Neutras:**

- 1 DocType + 1 Template + 5 Dictionary entries — 7 archivos uSync
  nuevos. GUIDs verificados cuádruple.
- `pageHeading` pierde valor cuando `Search.Heading` ya da default
  i18n, pero permite override editor-facing por sitio (ej. "Buscar
  en el blog corporativo"). Trade-off favorable.

## Alternatives considered

- **Resolver `/search` via controller route sin DocType**. Descartado.
  No permite per-site customization (heading, intro, itemsPerPage).
  El DocType es consistente con cómo Blog/Shop expose sus landings
  (postCategoryPage, productCategoryPage, searchPage).
- **Auto-submit on input con JS**. Descartado para el primer pase.
  Form nativo es accesible y suficiente. Si un sitio quiere instant
  search, design-system frontend puede interceptar y consumir
  `/api/search`.
- **Resaltar matches en el excerpt vía string.Replace**. Descartado.
  Naive Replace inyecta HTML y abre XSS si el query no está
  sanitizado. La forma correcta es vía Lucene formatter — diferido.
- **Mostrar el `Score` numérico en cada hit**. Descartado. Solo
  útil para debugging del provider; el visitante no lo entiende.

## Implementation summary (Ola 62, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-62.1)` | `4079edc` | `searchPage` DocType + Template + siteRoot Structure (3 archivos uSync; fix lateral postCategoryPage + productCategoryPage en allow-list) |
| `feat(ola-62.2)` | `c2c7204` | `SearchPage.cshtml` + 5 Dictionary entries nuevas (Search.Heading/Label/Pagination/Previous/Next) |
| `docs(ola-62.3)` | (este) | ADR 0032 + index README |

## References

- ADR 0031 — Search infrastructure (provider + controller)
- ADR 0027 — Blog runtime (referente de `postCategoryPage` template)
- ADR 0028 — Shop runtime (referente de `productCategoryPage` template)
- Próxima ola: 63 — SEO infrastructure (sitemap.xml + robots.txt + RSS)
