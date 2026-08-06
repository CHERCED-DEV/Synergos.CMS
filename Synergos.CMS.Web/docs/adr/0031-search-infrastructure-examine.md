# ADR 0031 — Search infrastructure on Examine ExternalIndex (Ola 61)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 61
- **Habilita:** Ola 62 — `searchPage` DocType + `SearchPage.cshtml`
  template (UX layer)
- **Related:** ADR 0009 (extension seams), ADR 0008 (uSync hybrid SoT —
  no schema-first)

## Context

Tras Ola 60 (Forms internal runtime), el último módulo agente-able
sin runtime backend era Search. El bloque `elementSynSearchBox` ya
existía desde Ola 53 (sync UI/CMS) pero solo emitía un placeholder
SynHost — apuntaba a un endpoint externo `searchEndpoint` que el
editor debía configurar.

Sin runtime interno:

- El sitio de un cliente recién deployado no tiene búsqueda funcional
  hasta que algún equipo backend publique un endpoint.
- Cada deploy requiere coordinación con un servicio externo (Algolia,
  ElasticSearch hosted, etc.).
- El Synergos.UI `<synergos-search-box>` no puede mostrar resultados
  reales en demos.

Dependencias técnicas: Umbraco 13 trae **Examine 3.1.0** preinstalado
con dos índices auto-mantenidos:

- **`InternalIndex`**: incluye contenido sin publicar — backoffice search.
- **`ExternalIndex`**: solo contenido publicado — el correcto para
  búsqueda pública.

Ambos índices se actualizan automáticamente vía notification handlers
internos al publish/unpublish/delete. **No se necesita custom indexer**.

## Decision

Construir el módulo Search como una capa de seams sobre el
`ExternalIndex` de Examine, con un endpoint público REST consumible por
templates Razor (Ola 62), Synergos.UI Web Components y JS de
autocomplete.

### Schema (Ola 61) — sin schema nuevo

Cero cambios en uSync. El módulo opera al margen del schema editorial
(no introduce DocTypes nuevos). El `searchPage` DocType viene en
Ola 62 como UX opcional.

### Application + Interfaces (Ola 61.1)

**`Synergos.CMS.Interfaces/ISearchQuery.cs`**:

```csharp
SearchResponse Search(SearchRequest request);
```

Records:
- `SearchRequest(Query, MaxItems = 20, Skip = 0, DocTypeAliasFilter = null)`
- `SearchResponse(Query, Hits, TotalEstimated, ElapsedMilliseconds)`
- `SearchHit(Url, Title, Excerpt, DocTypeAlias, DocTypeName, Score)`

Sin async — Examine es síncrono in-process (Lucene local). Sin caché
explícita — Examine cachea internamente.

**`Synergos.CMS.Application/Configuration/SearchSettings.cs`** POCO:

- `SearchableFields` = `[nodeName, seoTitle, seoDescription, excerpt,
  summaryText]` — campos editoriales más comunes con OR semántico.
- `ExcludedDocTypeAliases` = `[siteConfigSettings, themeSettings,
  platformSettings, flowDefinition, flowStep, reusableBlock]` — schema
  interno que el visitante no debe ver.
- `MaxHitsHardCap` = 100 — defensa contra `MaxItems` abusivo.
- `ExcerptMaxLength` = 200 chars.

Bind via `OptionsComposer` desde sección `Synergos:Search`.

### Web — defaults (Ola 61.2 + 61.3)

**`ExamineSearchProvider`** (`ISearchQuery` impl):

Pipeline:
1. Trim + valida query (vacío → respuesta vacía inmediata).
2. Resuelve `ExternalIndex` via
   `IExamineManager.TryGetIndex(Constants.UmbracoIndexes.ExternalIndexName, out var index)`.
3. SplitTerms del query (whitespace).
4. Clamp `MaxItems` a `[1, MaxHitsHardCap]`; `fetchSize = MaxItems + Skip`.
5. `index.Searcher.CreateQuery("content").GroupedOr(SearchableFields, terms)
   .Execute(QueryOptions.SkipTake(0, fetchSize))`.
6. Itera resultados con `.Skip(skip)`: parsea `Id`, resuelve
   `IPublishedContent` via `ctx.Content.GetById(nodeId)` (mantiene
   cultura activa + hostname binding + siteRoot consistentes con el
   request actual), filtra `ExcludedDocTypeAliases` y
   `DocTypeAliasFilter`.
7. Proyecta a `SearchHit`: `Url=content.Url()`, `Title=seoTitle?Name`,
   `Excerpt=excerpt?seoDescription?summaryText` cap a `ExcerptMaxLength`,
   `DocTypeAlias`, `Score`.
8. Mide `ElapsedMilliseconds` con `Stopwatch` para telemetría.

Transient — depende de `IUmbracoContextAccessor` per-request.

**`SearchController`** (`[Route("api/search")]`, `[ApiController]`):

```
GET /api/search?q={query}&maxItems={n}&skip={n}&docType={alias}
```

`AllowAnonymous` — solo indexa contenido publicado, ya filtrado por
`ExcludedDocTypeAliases`. Devuelve `SearchResponse` JSON.

## Consequences

**Positivas:**

- **Búsqueda funcional out-of-the-box**: cualquier deploy fresh tiene
  `/api/search?q=...` funcional sin configuración adicional. Examine
  hace re-index automático en background tras el primer publish.
- **URL consistente con cultura/siteRoot/hostname**: la hidratación vía
  `IPublishedContent.Url()` garantiza que el hit `/blog/post-1`
  respeta el siteRoot y la cultura del request, sin que el provider
  tenga que reproducir esa lógica.
- **Schema-agnostic**: el provider no asume DocTypes específicos —
  busca todos los publicados menos los excluidos. Editores pueden
  agregar nuevos DocTypes y aparecerán en search sin tocar el código.
- **Seam intercambiable**: si en el futuro el cliente quiere swap a
  Algolia/ElasticSearch hosted, solo se reemplaza el binding de
  `ISearchQuery` en `SeamComposer`. Controllers + templates no cambian.
- **Defensa en capas**: `MaxHitsHardCap`, `ExcludedDocTypeAliases`,
  trim + split del query, `Skip()` post-Examine para paginación.
- **Sin notification handler custom**: Umbraco mantiene el index
  automáticamente. Cero código de "rebuild on publish".

**Negativas:**

- **Sin highlighting de matches en el excerpt**: el excerpt se toma
  del campo `excerpt`/`seoDescription`/`summaryText`, no del texto
  donde matchea el término. Para highlighting real se necesita
  custom: query con `SimpleHTMLFormatter` de Lucene + parsing del
  result text. Diferido.
- **Sin facets agregados (groupBy DocType)**: el response es plano.
  La UI puede agrupar a posteriori, pero queries facetadas requieren
  Aggregations de Examine que en 3.x no son first-class. Diferido.
- **Sin spell-correct**: typos → 0 results. Lucene tiene
  `FuzzyQuery`, pero requiere wire específico. Diferido.
- **In-process Lucene**: el index vive en filesystem del proceso. En
  multi-instancia con load balancer, cada instancia mantiene su copia
  del index — bajo deploy/restart se reconstruye desde el published
  cache (puede demorar minutos en sitios grandes). Para clusters reales,
  swap por adapter remoto (Algolia / ES managed).
- **Sin search analytics**: el endpoint no logea queries para "trending
  searches" o "no-result queries". Los logs estándar capturan IPs +
  paths pero sin el `q=` parseado. Agregar futuro `ISearchAnalytics`
  seam si justifica.

**Neutras:**

- 0 GUIDs nuevos (cero schema). 0 templates uSync. 0 Razor views — la
  UX viene en Ola 62.
- `Examine.Search` namespace + `IExamineManager` agregados como
  dependency del Web project — ya estaban en transitive de Umbraco.
- `SearchHit.DocTypeName` por ahora devuelve el alias (sin lookup
  IContentTypeService). Futura mejora: enriquecer con friendly name si
  la UI lo necesita.

## Alternatives considered

- **Query inline al published cache** (sin Examine). Descartado para
  catálogos > ~1000 nodos: search en árbol completo es O(n*campos*terms)
  por request. Examine ya está ahí, gratis.
- **Custom indexer (`IConfigurableIndexValueSetBuilder`)** para tunear
  qué se indexa. Diferido — el ExternalIndex out-of-the-box cubre el
  90% de casos. Si se necesita boosting o stemming custom, agregar en
  ola dedicada.
- **Synergos.API search backend**. Diferido. Cuando Synergos.API exista
  con endpoint search, swap del binding + ajuste de DTOs.
- **Algolia / ElasticSearch managed**. Costo + complejidad de keys
  externas + sync sin valor adicional para el MVP.

## Implementation summary (Ola 61, 4 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-61.1)` | `addd057` | `ISearchQuery` seam + DTOs + `SearchSettings` POCO |
| `feat(ola-61.2)` | `5641a03` | `ExamineSearchProvider` — `ExternalIndex` query + published cache hidratación |
| `feat(ola-61.3)` | `cb0400d` | `SearchController` GET `/api/search` + wire OptionsComposer + SeamComposer |
| `docs(ola-61.4)` | (este) | ADR 0031 + index README |

## References

- ADR 0009 — Extension seams (`ISearchQuery` sigue el patrón)
- ADR 0008 — uSync hybrid SoT (sin schema-first; Examine no necesita
  schema en uSync)
- ADR 0028 — Shop runtime (referente de patrón Settings POCO + seam +
  default impl)
- Umbraco docs — Examine ExternalIndex
- Próxima ola: 62 — `searchPage` DocType + `SearchPage.cshtml` (UX)
