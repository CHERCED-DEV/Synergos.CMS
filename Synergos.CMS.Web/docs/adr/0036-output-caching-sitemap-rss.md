# ADR 0036 — Output caching strategy: IMemoryCache para Sitemap + Blog RSS (Ola 66)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 66
- **Extiende:** ADR 0033 (SEO infrastructure — donde
  "sin caché" estaba documentado como negativa)

## Context

ADR 0033 cerró la SEO infrastructure (sitemap.xml + robots.txt + blog
RSS) sin caché. Para sitios con hundreds de páginas o feed readers
agresivos (algunos consultan RSS cada 5-10 min), regenerar el XML por
cada request gasta CPU del web tier.

Análisis de costo:

- **`/sitemap.xml`**: itera todo el published cache (`GetAtRoot()` +
  `Descendants()` por root) y emite XML por cada nodo. Para un sitio
  con 5k nodos, ~50ms de CPU + ~500KB output. Crawlers consumen 1-2
  veces/día — pero load balancers ven N réplicas pidiendo cada uno.
- **`/blog/rss.xml`**: query a `IBlogQuery` (filter + sort + project)
  + serialización XML. Para un blog con 30 posts, ~10ms de CPU +
  ~30KB output. Feed readers consumen 4-12 veces/día.

Sin caché, un crawler que escanea un sitio cada 5 min con 4 instancias
detrás de LB → 1152 regeneraciones/día por endpoint.

## Decision

Cache in-memory simple (`IMemoryCache`) con TTL configurable per-endpoint
y key que respeta multi-brand routing. Bypass total via flag de
config para debugging.

### `OutputCacheSettings` POCO (Ola 66.1)

`Synergos.CMS.Application/Configuration/OutputCacheSettings.cs`:

- `SitemapMinutes` (default 60) — TTL del sitemap.xml.
- `BlogRssMinutes` (default 30) — TTL del blog RSS.
- `Disabled` (default false) — bypass total para debugging o sitios
  con requirements de freshness inmediata.

Bind via `OptionsComposer` desde sección `Synergos:OutputCache`.

### Wire (Ola 66.1)

`SeamComposer`: `services.AddMemoryCache()` (idempotente — si Umbraco
ya lo cableo, llamar dos veces es no-op).

### `SitemapController` cache (Ola 66.1)

- Cache key: `"syn:sitemap:" + hostBase` (multi-brand: cada hostname
  mantiene cache separado).
- Pipeline:
  1. Si `Disabled=true` → call `Build(hostBase)` directo.
  2. Si no → `_cache.Get<byte[]>(cacheKey)`. Hit → return inmediato.
  3. Miss → `Build` + `_cache.Set` con TTL `SitemapMinutes`.
- `Build` extraído como helper privado (separa concerns: cache logic
  vs render logic).

### `BlogRssController` cache (Ola 66.2)

Mismo patrón, key incluye filtros para no colisionar combinaciones:
- `"syn:blogrss:" + hostBase + "|" + maxItems + "|" + normalizedCategory`
- `normalizedCategory = categoryFilter.Trim().ToLowerInvariant()` —
  case + whitespace canonicalization para que `?category=Tech` y
  `?category=tech ` compartan cache.

## Consequences

**Positivas:**

- **Costo de regeneración amortizado**: 1152 → 24 regeneraciones/día
  por endpoint (1 cada 60 min para sitemap, 1 cada 30 min para RSS),
  con la primera request post-TTL pagando el costo de regenerar.
- **Multi-brand correcto**: cada hostname mantiene su cache key. Un
  crawler que pega 3 brands del mismo deploy hace 3 regeneraciones
  iniciales, luego cache hit.
- **Bypass via config**: `Disabled=true` permite debugging o
  freshness inmediata sin cambio de código.
- **Cero NuGet nuevo**: `IMemoryCache` ya está en
  `Microsoft.Extensions.Caching.Memory` (transitive de ASP.NET Core).
  `AddMemoryCache()` es idempotente.
- **Claves namespaced**: `syn:sitemap:`, `syn:blogrss:` prefijos
  evitan colisiones con cache keys que Umbraco u otras libs puedan
  usar en el mismo `IMemoryCache`.

**Negativas:**

- **Sin invalidation on publish**: cuando el editor publica un post
  nuevo, el RSS no se actualiza hasta que TTL expira (max 30 min) o
  el proceso restartea. Para sitios con SLA de "post publicado debe
  aparecer en RSS en < 1 min", agregar
  `INotificationHandler<ContentPublishedNotification>` que llame
  `_cache.Remove("syn:blogrss:*")`. Diferido. Aceptable para 99% de
  blogs editoriales.
- **In-memory no se sincroniza entre instancias**: bajo load balancer
  con N instancias, cada una mantiene su copia. Eventual consistency
  — un crawler puede recibir versiones diferentes según la instancia.
  Para alta consistency cross-instance, swap por
  `IDistributedCache` sobre Redis. Diferido.
- **Cache no se warmea pre-emptivo**: la primera request post-TTL
  paga el costo de regenerar (~50ms para sitemap). Mejorable con
  background warmup vía `IHostedService`. Diferido.
- **Memoria no cap-eada**: `IMemoryCache.Set` con TTL pero sin size
  limit. Para sitios con muchos brands (50+) podría crecer
  significativamente. Mitigación: `MemoryCacheOptions.SizeLimit` +
  size por entry. Diferido — los entries son pocos y pequeños.
- **Cache poisoning si Umbraco devuelve datos incorrectos**: si por
  alguna razón el `IBlogQuery` devuelve resultado erróneo, el cache
  lo persiste por TTL min. Mitigación: validación pre-cache de que
  la lista de posts no esté vacía cuando el blog tiene contenido.
  KISS no agregamos esto — un cache miss durante un bug en
  IBlogQuery sería peor (cada request rompe).

**Neutras:**

- 1 POCO + 0 seams nuevos. Cache es un detalle de implementación de
  los controllers — no se expone via interface.
- `byte[]` como tipo cacheado (output ya serializado a XML). Trade-off:
  más memoria que cachear los DTOs upstream, pero evita re-serializar
  por cada hit.

## Alternatives considered

- **`Microsoft.AspNetCore.OutputCaching` middleware**. Disponible en
  .NET 8. Más limpio (decorador `[OutputCache(Duration = 3600)]`
  sobre la action), pero requiere `app.UseOutputCache()` en el
  pipeline, que necesita un composer custom para Umbraco. Diferido —
  IMemoryCache es funcionalmente equivalente y simpler de wirear.
- **`IDistributedCache` desde el primer pase**. Premature.
  IMemoryCache cubre el 95% de deploys (single instance o
  acceptable eventual consistency). Swap a IDistributedCache cuando
  el cliente justifique.
- **CDN edge caching (Cloudflare, etc.)**. Complementario, no
  reemplazo. Ambos son válidos: CDN sirve la versión cacheada al
  edge, IMemoryCache sirve la del origen. Trabajan juntos.
- **Cache invalidation on publish**. Diferido. La pérdida de
  freshness por max 60 min es aceptable para SEO/RSS.
- **Cachear los DTOs en lugar del XML serializado**. Más memoria-
  eficiente si el output es muy grande, pero re-serializa por hit.
  Trade-off favorable a cachear bytes para outputs ≤1MB.

## Implementation summary (Ola 66, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-66.1)` | `c08caf6` | `OutputCacheSettings` POCO + `SitemapController` IMemoryCache wrap + wire OptionsComposer + SeamComposer (AddMemoryCache) |
| `feat(ola-66.2)` | `9159263` | `BlogRssController` IMemoryCache wrap (key incluye host + maxItems + categoryFilter normalizado) |
| `docs(ola-66.3)` | (este) | ADR 0036 + index README |

## References

- ADR 0033 — SEO infrastructure (cierra la negativa "sin caché")
- ADR 0027 — Blog runtime (IBlogQuery — consumido por RSS)
- Microsoft.Extensions.Caching.Memory documentation
