# ADR 0027 — Blog runtime + Members settings configurable (Ola 56)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 56
- **Cierra:** TODO Ola 52.C (`/login` hardcoded) + módulo Blog runtime
  diferido desde Ola 32

## Context

Tras Ola 55 (cms-sync generator) quedaban cierre dos frentes
agent-able sin bloqueador externo:

1. **`/login` hardcoded en `MemberGatingHandler` (Ola 52.C)**.
   Documentado como TODO para extraer a `IOptions<MembersSettings>`.
   Cualquier sitio que usara `/iniciar-sesion`, `/account/login`,
   etc. requería tocar código en lugar de configurar.

2. **Módulo Blog runtime diferido desde Ola 32 + 36**. Schema
   completo: `postPage` + `postCategoryPage` + `compTagging` +
   `elementCompArticleList` + `elementCompBlogHighlight`. Pero:
   - Ningún template registrado para `postPage` ni `postCategoryPage`
     → categorías y artículos no se podían navegar públicamente
   - Los renderers de `ArticleList` y `BlogHighlight` hacían query
     inline en Razor (40+ líneas duplicadas entre ambos)
   - No había service centralizado para reusar la lógica de query

## Decision

### Parte A — `MembersSettings.LoginPath` configurable

Crear `MembersSettings` POCO en `Synergos.CMS.Application/Configuration/`
con un solo campo: `LoginPath` (default `"/login"`). Bind desde
section `Synergos:Members` en `appsettings.*.json` vía
`OptionsComposer`.

`MemberGatingHandler` ahora inyecta `IOptions<MembersSettings>` y
usa `_loginPath` (con fallback `"/login"` si la config viene vacía)
en lugar del const removido. Sin cambios funcionales para sitios
que no configuren — el default sigue siendo el mismo.

### Parte B — `IBlogQuery` + Blog templates

**Seam (`Synergos.CMS.Interfaces/IBlogQuery.cs`):**

```csharp
public interface IBlogQuery
{
    IReadOnlyList<PostSummary> GetPosts(BlogQueryRequest request);
}

public sealed record BlogQueryRequest(
    int MaxItems = 6,
    int Skip = 0,
    string? CategoryAliasOrName = null,
    string? TagsCsv = null);

public sealed record PostSummary(
    string Url, string Title, string? Excerpt, string? HeroImageUrl,
    DateTime? PublishDate, int? ReadTimeMinutes,
    string? CategoryName, IReadOnlyCollection<string> Tags);
```

**Implementación (`Synergos.CMS.Web/Services/DefaultBlogQuery.cs`):**

- Recorre `DescendantsOrSelfOfType("postPage")` bajo el siteRoot del
  request actual (fallback a todos los siteRoots si no hay request)
- Aplica filtros:
  - `CategoryAliasOrName`: compara contra `Parent.Name` y
    `Parent.Value<string>("categoryName")` si Parent es
    `postCategoryPage`
  - `TagsCsv`: split por coma, OR semántico contra
    `compTagging.tags` del post
- Ordena por `publishDate desc`, fallback al nombre del nodo
- Proyecta a `PostSummary` records con todos los campos relevantes

Sin caché — Umbraco mantiene published cache en memoria. Para
sitios con 10k+ posts evaluar Examine o store dedicado.

**Wire en `SeamComposer`:** `services.AddTransient<IBlogQuery,
DefaultBlogQuery>()` — Transient porque depende de
`IUmbracoContextAccessor` per-request.

**Razor templates nuevos:**

1. `Views/PostPage.cshtml` + `uSync/v9/Templates/postpage.config`
   (Key fresh, verificado sin colisiones). Renderiza header
   (categoría + título + excerpt + meta date/read-time), heroImage,
   body via `BlockGridModel("sections")`. `postpage.config`
   actualizado con `DefaultTemplate=PostPage`.

2. `Views/PostCategoryPage.cshtml` +
   `uSync/v9/Templates/postcategorypage.config`. Renderiza header
   (nombre + descripción de la categoría) + listado paginado vía
   `IBlogQuery` con `CategoryAliasOrName = category.Name`.
   - Page size: hardcoded 12 (futuro configurable)
   - Paginación: querystring `?page=N` (default 1)
   - Detección de "next page": pide `MaxItems = PageSize + 1`

3. `Elements/Comp/ArticleList.cshtml` y `BlogHighlight.cshtml`:
   refactorizados para `@inject IBlogQuery` y consumir el service.
   Antes ~40 líneas de query inline duplicada; ahora 1 llamada +
   proyección de `PostSummary`. CMS inputs preservados.

## Consequences

**Positivas:**

- **Members runtime configurable**: editor/devops cambia LoginPath
  vía `appsettings.json` sin tocar código. Multi-deploy con paths
  diferentes (`/iniciar-sesion`, `/login-account`, etc.) ahora
  funciona out-of-the-box.
- **Blog runtime end-to-end**: arquitecto crea un siteRoot →
  postCategoryPage → N postPage → publish → navega URLs públicas y
  el listing/paginación funcionan. Cero código adicional necesario
  por sitio.
- **DRY query logic**: una sola fuente de verdad (DefaultBlogQuery)
  consumida por 3 superficies (ArticleList block, BlogHighlight
  block, PostCategoryPage template). Cambiar el orden, los filtros
  o agregar un campo a `PostSummary` impacta una sola pieza.
- **IBlogQuery extensible**: agregar `GetPostByCategory(slug)`,
  `GetRelatedPosts(currentPost)`, `GetPostsByAuthor(name)` etc. es
  un método más al seam sin tocar consumidores existentes.

**Negativas:**

- **Page size 12 hardcoded** en PostCategoryPage. Cuando un sitio
  necesite paginación distinta, extraer a
  `IOptions<BlogSettings>.CategoryPageSize`. No urgente.
- **No hay author runtime**. `compContentAuthor` existe como
  composition pero `postPage` no la compone (autor sigue siendo
  free text en Ola 32). Mantenido así por scope; hoy `PostSummary`
  no expone author. Cuando se decida adoptar
  `compContentAuthor`, agregar `AuthorName/Avatar/Role` al record.
- **No hay related posts ni search**. `IBlogQuery` solo lista por
  filtros simples. Búsqueda full-text vendría de Examine o un
  service externo — fuera del scope de Ola 56.

**Neutras:**

- 2 GUIDs nuevos (postpage.config Template + postcategorypage.config
  Template). Verificación cuádruple OK.
- `MembersSettings.LoginPath` mantiene default = `"/login"` —
  sitios existentes sin la nueva config bindeada siguen funcionando
  igual.

## Alternatives considered

- **`/login` config en `BrandingSettings`**. Descartado. El path
  de login es decisión técnica (routing), no editorial (brand).
  Settings separadas mantienen separation of concerns.
- **`IBlogQuery` async**. Descartado. Umbraco's published cache es
  síncrono in-memory; agregar async sin razón es ceremonia
  innecesaria.
- **Renderer ArticleList consume Umbraco directamente, sin service**.
  Descartado. Era el estado pre-Ola 56 — duplicación entre
  ArticleList y BlogHighlight, sin reuso para PostCategoryPage.
  Service centralizado es estrictamente mejor.
- **PostCategoryPage como variant de pageBase con un block listado**.
  Descartado. El listado paginado es decisión estructural, no
  editorial; templates dedicados con runtime específico son más
  limpios que soportar ?page=N en un page type general.

## Implementation summary (Ola 56, 2 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-56.1)` | `e341da4` | `MembersSettings.LoginPath` configurable + wire en handler |
| `feat(ola-56.2)` | `16b10ea` | `IBlogQuery` + `DefaultBlogQuery` + 2 templates Razor + 2 uSync Templates + refactor ArticleList/BlogHighlight |

## References

- ADR 0009 — Extension seams (IBlogQuery sigue el patrón)
- ADR 0010 — Branding via provider (MembersSettings sigue el patrón
  POCO + IOptions)
- ADR 0025 — Members runtime (cierra el TODO documentado en 0025)
- `refactor-docs/migration/05-legacy-refinement-inventory.md` —
  desbloqueo del módulo Blog (item #15 del backlog)
