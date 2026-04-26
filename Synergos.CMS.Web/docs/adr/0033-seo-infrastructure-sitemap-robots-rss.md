# ADR 0033 — SEO infrastructure: sitemap.xml + robots.txt + blog RSS (Ola 63)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 63
- **Related:** ADR 0026 (`<head>` enrichment), ADR 0027 (Blog runtime
  + IBlogQuery), ADR 0031 (Search infrastructure — comparte
  `ExcludedDocTypeAliases`)

## Context

Tras las Olas 60-62 (Forms internal + Search infra/UX), el último
gap operativo de SEO eran los archivos canónicos que crawlers
esperan:

- **`/sitemap.xml`** — Google/Bing/Yandex usan para enumerar URLs
  públicas, frecuencia de update y prioridad. Sin sitemap, el crawler
  depende de descubrimiento por enlaces (más lento, incompleto).
- **`/robots.txt`** — control directo sobre qué rutas crawlear y
  referencia al sitemap. Sin robots, crawlers leen rutas internas
  (`/umbraco/`, `/api/`) y gastan crawl budget.
- **Blog RSS** — feed reader subscriptions, syndication automática
  a otras plataformas, integración con servicios tipo Mailchimp RSS
  campaigns.

ADR 0026 ya cerró el `<head>` (Twitter Card + JSON-LD + hreflang).
Falta la capa de infraestructura externa.

## Decision

Tres controllers atributo-routed, cada uno consume una seam o el
published cache directamente. Cero schema editorial nuevo (todo
operacional).

### `SitemapController` (Ola 63.1)

`[Route("sitemap.xml")]` `[AllowAnonymous]`. Pipeline:

1. `IUmbracoContextAccessor.TryGetUmbracoContext` — fail con 404
   si no hay context.
2. Itera `ctx.Content.GetAtRoot()` y `.Descendants()` de cada root.
3. Skip de `SearchSettings.ExcludedDocTypeAliases` (reusa la
   blacklist del módulo Search — `siteConfigSettings`,
   `themeSettings`, `platformSettings`, `flowDefinition`,
   `flowStep`, `reusableBlock`).
4. Skip de URLs vacías o `"#"` (ej. nodos sin segment publicado).
5. Por nodo: `<url><loc>{absolute}</loc><lastmod>{ISO 8601}</lastmod>
   <changefreq>weekly</changefreq></url>`.
6. `hostBase = Request.Scheme + "://" + Request.Host` — multi-brand
   routing emite sitemap brand-specific automáticamente cuando un
   deploy tiene N siteRoots con N hostnames.
7. `XmlWriter` UTF-8 sin BOM, indent true.
8. Devuelve `File(bytes, "application/xml; charset=utf-8")`.

### `RobotsController` (Ola 63.2)

`[Route("robots.txt")]` `[AllowAnonymous]`. Pipeline:

1. Detecta env via `IHostEnvironment.IsProduction()`.
2. **Production**:
   ```
   User-agent: *
   Allow: /
   Disallow: /umbraco/
   Disallow: /api/
   Disallow: /flow/

   Sitemap: {host}/sitemap.xml
   ```
3. **Non-Production** (Development, Staging, etc.):
   ```
   # Non-production environment ({Name}) — discourage indexing.
   User-agent: *
   Disallow: /

   Sitemap: {host}/sitemap.xml
   ```
4. Devuelve `Content(body, "text/plain; charset=utf-8")`.

Defensa-en-capas: si alguien expone un staging accidentalmente al
público, el robots de no-Production evita que Google lo indexe.

### `BlogRssController` (Ola 63.3)

`[Route("blog/rss.xml")]` `[AllowAnonymous]`. Pipeline:

1. Inyecta `IBlogQuery` (ADR 0027) — single source of truth con
   PostCategoryPage. Cero duplicación de lógica de query.
2. Acepta `?maxItems=30` (cap 100) y `?category=foo` opcional.
3. Llama `_blogQuery.GetPosts(new BlogQueryRequest(...))`.
4. `XmlWriter` UTF-8 sin BOM. Estructura RSS 2.0:
   - `<rss version="2.0"><channel>` con `title`, `link`,
     `description`, `language="es-CO"`, `generator`,
     `lastBuildDate` (RFC 1123).
   - Por post: `<item>` con `<title>`, `<link>`, `<guid
     isPermaLink="true">`, `<description>` (excerpt), `<pubDate>`
     (RFC 1123), `<category>` (categoryName).
5. Devuelve `File(bytes, "application/rss+xml; charset=utf-8")`.

Compatible con Feedly, Inoreader y feed readers estándar.

## Consequences

**Positivas:**

- **SEO infra completa out-of-the-box**: deploy fresh tiene
  `/sitemap.xml` + `/robots.txt` + `/blog/rss.xml` funcionales sin
  configuración adicional. Submission a Google Search Console + Bing
  Webmaster es un copy/paste.
- **Multi-brand routing automático**: cada hostname del deploy emite
  su propio sitemap/robots/RSS — ningún brand "leakea" URLs de otro
  brand porque `hostBase` se calcula del request.
- **Defensa-en-capas en staging**: env != Production emite
  `Disallow: /` automáticamente. Salva al cliente de SEO accidental
  cuando una rama feature se expone temporalmente.
- **Reutilización de seams existentes**:
  - `SearchSettings.ExcludedDocTypeAliases` se comparte entre Search
    y Sitemap — un solo lugar para mantener la blacklist editorial.
  - `IBlogQuery` ya existía desde Ola 56 — el RSS controller no
    reimplementa filtros de categoría.
- **Cero schema nuevo**: 0 DocTypes, 0 Templates, 0 Dictionary entries.
  Pure operational infra.

**Negativas:**

- **Sin caché**: cada request a `/sitemap.xml` genera el XML on-demand.
  Para sitios con 10k+ páginas el render puede ir a 100-500ms. Crawlers
  consumen sitemap rara vez (1-2 veces al día) — el costo es bajo, pero
  para optimizar se puede agregar `OutputCache` con TTL 1h o cachear
  el resultado en `MemoryCache` con invalidation on publish/unpublish.
  Diferido.
- **Sin paginación a sitemap_index.xml**: el protocolo limita a 50k
  URLs por archivo + 50MB total. Para sitios mayores se necesita un
  índice + N children. El 99% de deploys queda muy debajo del límite,
  pero es un techo conocido. Diferido a una micro-ola.
- **`changefreq=weekly` hardcoded**: el protocolo permite per-URL
  `changefreq` y `priority`. Hoy emitimos weekly para todo. Refinable
  en futuro: per-DocType (homepage daily, post weekly, page monthly).
- **Sin auto-discovery del RSS**: el `<head>` no emite todavía
  `<link rel="alternate" type="application/rss+xml"
  href="/blog/rss.xml">`. Trivial agregar a `_SeoHead.cshtml` cuando
  el sitio adopta blog (condicional a "este siteRoot tiene postCategoryPage
  publicado"). Diferido.
- **Sin Atom feed**: solo RSS 2.0. Feed readers modernos consumen
  ambos, pero algunos casos (ActivityPub, Pleroma) prefieren Atom.
  Trivial agregar `BlogAtomController` espejado en futura ola.

**Neutras:**

- 3 controllers, 0 services nuevos. La inyección reutiliza
  `IUmbracoContextAccessor`, `IOptions<SearchSettings>`,
  `IBlogQuery`, `IHostEnvironment` — todos ya registrados desde
  olas previas.
- `ExcludedDocTypeAliases` ahora tiene dos consumidores
  (`ExamineSearchProvider` + `SitemapController`). Acoplamiento
  intencional — la lista canonical de "schema interno" debe
  excluirse de search y de sitemap por las mismas razones.

## Alternatives considered

- **Sitemap como archivo estático generado por hosted service**.
  Descartado por scope. La generación dinámica es trivial al volumen
  esperado y elimina sync con `wwwroot`. Si un cliente justifica,
  swap a hosted service que escriba `wwwroot/sitemap.xml` post-publish.
- **`<priority>` en sitemap basado en `productPage` vs `postPage`
  vs `pageBasic`**. Diferido. Google ignora `priority` en gran parte
  desde 2017. KISS: omitir.
- **Robots.txt vía archivo estático en `wwwroot/`**. Descartado. No
  responde al env (Development vs Production) y obliga a deploys
  separados de robots por env. Controller dinámico es más limpio.
- **RSS unificado del sitio entero (no solo blog)**. Diferido.
  Mezclar productos + páginas + posts en un único feed pierde
  semántica para subscribers. Si justifica, agregar
  `SiteUpdatesRssController` espejado.
- **Atom 1.0 como default en lugar de RSS 2.0**. RSS 2.0 tiene
  compatibility universal con feed readers; Atom es técnicamente
  superior pero menos universal. Pragmatic.

## Implementation summary (Ola 63, 4 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-63.1)` | `e6919f8` | `SitemapController` GET `/sitemap.xml` — itera published cache, skip ExcludedDocTypeAliases, XmlWriter |
| `feat(ola-63.2)` | `4d0b2d3` | `RobotsController` GET `/robots.txt` — Production allow + Non-Production disallow + Sitemap link |
| `feat(ola-63.3)` | `c90332d` | `BlogRssController` GET `/blog/rss.xml` — RSS 2.0 sobre IBlogQuery |
| `docs(ola-63.4)` | (este) | ADR 0033 + index README |

## References

- ADR 0026 — `<head>` enrichment (Twitter Card + JSON-LD + hreflang)
- ADR 0027 — Blog runtime (IBlogQuery — consumido por RSS controller)
- ADR 0031 — Search infrastructure (comparte ExcludedDocTypeAliases)
- sitemaps.org protocol: https://www.sitemaps.org/protocol.html
- robots.txt RFC 9309
- RSS 2.0 spec: https://cyber.harvard.edu/rss/rss.html
