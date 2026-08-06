# ADR 0060 — SEO maturity: Article/Product JSON-LD + BreadcrumbList + News sitemap (Olas 136-138)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 3 olas en un único ADR.

## Context

Tras el cap acotado a Ola 135 cerrado (ADR 0058 + post-135 hotfixes
0059), el arquitecto extendió el cap a Ola 150. El primer item del
diferido §11.12 fue **SEO maturity**: structured data per page type
+ multi-brand sitemap.

Contexto previo:
- ADR 0026 — `_SeoHead.cshtml` ya emitía Organization + WebSite JSON-LD
  + Twitter Card + hreflang. Faltaba per-page-type schema.
- ADR 0033 — `SitemapController` + `BlogRssController` + `RobotsController`
  ya cubrían el SEO baseline. Faltaba news-sitemap para Google News.
- `_Breadcrumbs.cshtml` ya renderizaba la `<ol>` visual sin JSON-LD.

## Decision

### Ola 136 — _SeoStructuredData.cshtml

Nuevo partial invocado desde `_Layout.cshtml` después de `_SeoHead`,
que dispatcha por `ContentType.Alias`:

- **postPage** → Article JSON-LD: `headline`, `description`, `image`,
  `datePublished`, `dateModified`, `mainEntityOfPage`, `author`+`publisher`
  como Organization con siteName.
- **productPage** → Product JSON-LD: `name`, `sku`, `description`,
  `image`, `offers` con `price`, `priceCurrency`, `availability`
  (`schema.org/InStock` o `OutOfStock`), `url`.
- **default** → no-op (los singletons Organization+WebSite siguen
  viniendo del `_SeoHead`).

Sin dependencia nueva — `JsonSerializer.Serialize` escapa values.

### Ola 137 — BreadcrumbList JSON-LD

`_Breadcrumbs.cshtml` ahora emite además del `<nav>` visual un
`<script type="application/ld+json">` con `BreadcrumbList`. Cada
`itemListElement` tiene `@type=ListItem`, `position`, `name`, `item`
(URL absoluta cuando hay `canonicalHostname` en siteRoot).

### Ola 138 — News sitemap

`NewsSitemapController` nuevo en `/news-sitemap.xml` siguiendo
[Google News Sitemap Protocol](https://developers.google.com/search/docs/crawling-indexing/sitemaps/news-sitemap)
con namespace `http://www.google.com/schemas/sitemap-news/0.9`.
Filtra `postPage` publicados dentro de la ventana
`OutputCacheSettings.NewsSitemapWindowHours` (default 48h)
consumido via `IBlogQuery.GetPosts(MaxItems=1000)`.

Cache per-host con TTL `NewsSitemapMinutes` (default 15m, más bajo
que sitemap.xml por la cadencia agresiva de Google News re-crawl).

`RobotsController` actualizado para anunciar ambos sitemaps:
```
Sitemap: {host}/sitemap.xml
Sitemap: {host}/news-sitemap.xml
```

`OutputCacheSettings` agrega 2 keys nuevas (`NewsSitemapMinutes` +
`NewsSitemapWindowHours`).

## Consequences

**Positivas:**

- **Rich Results en Google**: Article snippets con thumbnail + fecha,
  Product con price + availability, BreadcrumbList navigation. Trade
  visible en SERP.
- **Google News indexing**: blog posts entran al News index
  automáticamente cuando se publican (ventana 48h respeta el protocolo).
- **Cero dependencia nueva**: solo `System.Text.Json` + `System.Xml`
  ya en el SDK.
- **Cero schema rompedor**: ningún DocType / DataType / Composition
  nuevo. Solo runtime emit + nuevas configurations en POCO.

**Negativas:**

- **Dependencia de canonicalHostname**: si el siteRoot no tiene
  `canonicalHostname`, las URLs absolutas en BreadcrumbList y News
  sitemap se degradan a relativas. Aceptable — Google parsea
  relative URLs como contextual al sitemap location.
- **Article schema fija author=Organization**: no usamos `Person` schema
  porque `postPage` actual no tiene `authorName` típico. Si un futuro
  ola agrega compAuthorByline, refinar a `Person` como nested.
- **News sitemap MaxItems=1000**: si un blog produce > 1000 posts en
  48h, los más viejos se truncan. Ningún sitio actual se acerca a ese
  volumen.

**Neutras:**

- 1 commit feat batch (Olas 136+137+138 unificadas) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 136 | `_SeoStructuredData.cshtml` partial dispatching Article/Product/no-op por alias. Wired desde `_Layout.cshtml`. |
| 137 | `_Breadcrumbs.cshtml` emite BreadcrumbList JSON-LD junto con la `<nav>` visual. |
| 138 | `NewsSitemapController` `/news-sitemap.xml` Google News Protocol + `OutputCacheSettings` 2 keys + `RobotsController` anuncia 2 sitemaps. |
| 0060 | (este) ADR consolidado |

## Próximas direcciones

- **Refinement Article author**: cuando llegue compAuthorByline, swap
  `author=Organization` por `Person` nested.
- **FAQPage schema** para landing pages con FAQ blocks (Ola futura).
- **Event schema** si se agrega un eventPage DocType.
- **VideoObject schema** para SynHost VideoPlayer renders.

## References

- ADR 0026 — Brand runtime completion + `<head>` enrichment (donde
  vive `_SeoHead.cshtml` con Organization + WebSite JSON-LD).
- ADR 0033 — SEO infrastructure (sitemap.xml + robots.txt + RSS).
- [Schema.org Article](https://schema.org/Article)
- [Schema.org Product](https://schema.org/Product)
- [Schema.org BreadcrumbList](https://schema.org/BreadcrumbList)
- [Google News Sitemap Protocol](https://developers.google.com/search/docs/crawling-indexing/sitemaps/news-sitemap)
