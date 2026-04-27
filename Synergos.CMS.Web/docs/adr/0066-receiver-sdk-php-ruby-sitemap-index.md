# ADR 0066 — Receiver SDK PHP+Ruby + sitemap-index endpoint (Olas 151-152)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Deciders:** Arquitecto + agente, scope-amplio batch.
- **Consolida:** 2 olas en un único ADR.

## Context

Tras el cap-150 cerrado (ADRs 0060-0065), el arquitecto extendió el cap
implícitamente con "continua". El primer batch del nuevo cap-160
prioriza items low-risk del diferido §11.13:

- **Receiver SDK PHP / Ruby** — diferido del cap-150 cuando solo C# /
  Node / Python / Go / Java estaban cubiertos.
- **Sitemap-index pattern** — diferido del Batch A original del cap-150
  (deferred por simplicidad cuando se priorizó News sitemap).

## Decision

### Ola 151 — Receiver SDK PHP + Ruby

Extiende `docs/webhooks/receiver-sdk.md` con 2 snippets idiomáticos:

**PHP** (`hash_hmac` + `hash_equals`):
- `strtotime` para parsear timestamp (acepta ISO 8601).
- `hash_hmac('sha256', ...)` retorna hex string.
- `hash_equals` para constant-time compare (PHP 5.6+).
- Uso típico en endpoint vanilla/Slim/Laravel via `php://input`.

**Ruby** (`OpenSSL::HMAC` + `OpenSSL.fixed_length_secure_compare`):
- `Time.iso8601` para parsing.
- `OpenSSL::HMAC.hexdigest('sha256', secret, input)`.
- `OpenSSL.fixed_length_secure_compare` (Ruby 3.1+) — alternativa
  `Rack::Utils.secure_compare` en stacks Rails.
- Uso típico en Rails controller con
  `skip_before_action :verify_authenticity_token`.

Coverage ahora **7 lenguajes** (C# / Node.js / Python / Go / Java /
PHP / Ruby).

### Ola 152 — Sitemap-index endpoint

`SitemapIndexController` nuevo en `/sitemap-index.xml` siguiendo el
[sitemap-index protocol](https://www.sitemaps.org/protocol.html#index)
con root `<sitemapindex>` listando los sitemaps del deploy:

```xml
<?xml version="1.0" encoding="utf-8"?>
<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
  <sitemap>
    <loc>https://example.com/sitemap.xml</loc>
    <lastmod>2026-04-27T15:00:00+00:00</lastmod>
  </sitemap>
  <sitemap>
    <loc>https://example.com/news-sitemap.xml</loc>
    <lastmod>2026-04-27T15:00:00+00:00</lastmod>
  </sitemap>
</sitemapindex>
```

Cache per-host con TTL `OutputCacheSettings.SitemapMinutes`.

Para deploys multi-siteRoot bajo el mismo `platformRoot`, el código
itera el árbol Umbraco para detectar siteRoots — actualmente solo
emite las 2 entries (sitemap + news-sitemap) pero la estructura está
lista para fanout futuro.

## Consequences

**Positivas:**

- **PHP/Ruby coverage**: integradores con stacks LAMP / Rails ahora
  copy-paste sin polyfills.
- **Sitemap discovery centralizada**: un solo `Sitemap:` line en
  `robots.txt` (al sitemap-index) puede reemplazar la lista actual de
  2 entries, simplificando configuración de search consoles que
  consumen sitemap-index.
- **Cero dependencia nueva**: los snippets usan stdlib en cada lenguaje
  + el sitemap-index controller solo `System.Xml`.

**Negativas:**

- **PHP 7.4+ requerido**: `declare(strict_types=1)` syntax. Para
  PHP 7.0-7.3 swap por `array` type hints sin `?`. Para PHP < 5.6
  swap `hash_equals` por implementación manual.
- **Ruby 3.1+ requerido** para `OpenSSL.fixed_length_secure_compare`.
  Para 3.0 swap a `Rack::Utils.secure_compare` (require gem).
- **Sitemap-index actual no fanout per-siteRoot**: el código está listo
  pero solo enumera 2 sitemaps fijos. Para deploys multi-siteRoot
  reales se debería emitir entries `/{siteSlug}/sitemap.xml` con
  routing complementario.

**Neutras:**

- 1 commit feat batch (Olas 151+152) + 1 docs ADR.
- 0 GUIDs nuevos.
- 0 NuGet packages nuevos.

## Implementation summary

| # | Foco |
|---|---|
| 151 | PHP + Ruby snippets en `receiver-sdk.md`. Coverage 7 lenguajes. |
| 152 | `SitemapIndexController` `/sitemap-index.xml` cache per-host TTL `SitemapMinutes`. |
| 0066 | (este) ADR consolidado |

## Próximas direcciones

- **Sitemap-index fanout per-siteRoot** — cuando llegue deploy
  multi-brand bajo mismo hostname, agregar lógica para enumerar
  siteRoots como entries `/{slug}/sitemap.xml` + ruta complementaria
  en `SitemapController`.
- **Robots.txt prefer sitemap-index** — opcional, swap de
  `Sitemap: {host}/sitemap.xml` + `Sitemap: {host}/news-sitemap.xml`
  por solo `Sitemap: {host}/sitemap-index.xml`.

## References

- ADR 0060 — SEO maturity (donde se introdujo NewsSitemapController +
  el Article/Product/BreadcrumbList JSON-LD).
- ADR 0033 — SEO infrastructure (origen de SitemapController +
  RobotsController).
- ADR 0058 — Receiver SDK docs initial (C# / Node / Python).
- ADR 0065 — Receiver SDK Go + Java additions.
- [sitemaps.org — sitemap-index protocol](https://www.sitemaps.org/protocol.html#index)
