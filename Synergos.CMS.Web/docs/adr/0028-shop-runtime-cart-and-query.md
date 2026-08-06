# ADR 0028 — Shop runtime: cart state + product query (Ola 57)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 57
- **Cierra:** Módulo Shop runtime diferido desde Ola 33 + 36

## Context

Tras Ola 56 (Blog runtime + Members settings), el módulo Shop era el
último diferido editable sin depender del CDN team. Schema completo
desde Olas 33 + 36 (productPage + productCategoryPage + 8
elementShop*) pero:

- Cart state: ningún service centralizado para gestionar el cart del
  visitante. Renderers de `CartItem`/`CartSummary` consumían un
  storage inexistente.
- Product query: cada renderer (ProductCard, ProductGrid)
  reimplementaba la búsqueda directa en el árbol Umbraco —
  duplicación + sin reuso para listings de categoría.
- Templates faltantes: `ProductPage` y `ProductCategoryPage` sin
  `DefaultTemplate` asignado → URLs no navegables públicamente.
- Sin endpoints HTTP para mutar cart desde el frontend (add, remove,
  update, clear).

## Decision

### Parte A — Cart state cookie HMAC-firmada

**`CartSettings`** (POCO en `Synergos.CMS.Application/Configuration/`):
- `CookieName` (default `"syn_cart"`)
- `SecretKey` (HMAC, default `"synergos-dev-secret-change-me"` —
  warning si default en producción)
- `MaxItems` (50)
- `Currency` (`"COP"`)
- `CookieLifetimeDays` (30)

Bind via `OptionsComposer` desde sección `Synergos:Cart`.

**`ICartService`** (seam en `Synergos.CMS.Interfaces`):
```csharp
Cart GetCart();
Cart AddItem(string sku, int qty = 1, string? variantSku = null);
Cart UpdateQuantity(string sku, int qty, string? variantSku = null);
Cart RemoveItem(string sku, string? variantSku = null);
Cart Clear();
```

Records `Cart(Lines, Subtotal, Currency, ItemCount)` y
`CartLine(Sku, VariantSku, Quantity, ProductName, UnitPrice,
LineTotal, ImageUrl, ProductUrl)`.

**`DefaultCartService`** (impl en `Synergos.CMS.Web/Services/`):
- **Storage**: cookie `{base64(json)}.{base64(hmacSha256)}`. Sin DB,
  sin login required, persiste 30 días entre sesiones del visitante.
- **Validación**: HMAC con `CryptographicOperations.FixedTimeEquals`
  para prevenir tampering. Fail-open hacia "cart vacío" si firma no
  valida o JSON malformed (sin excepciones).
- **Hidratación**: cada `GetCart()` cruza SKUs almacenados con
  `productPage` publicados (`DescendantsOrSelfOfType`), proyecta
  nombre actual, precio (`productPriceBase` + variant `priceDelta`
  del JSON `productVariantsJson`), imagen (primera de
  `productImages`), URL.
- **Cookie attributes**: `HttpOnly`, `Secure` (si `IsHttps`),
  `SameSite=Lax`, `Expires=now+30d`, `Path=/`.

**`ShopController`** (Web/Controllers, `[Route("api/shop/cart")]`):
- `GET /` → `Cart`
- `POST /add { sku, quantity, variantSku? }` → `Cart`
- `POST /update { sku, quantity, variantSku? }` → `Cart`
- `POST /remove { sku, variantSku? }` → `Cart`
- `POST /clear` → `Cart`

Sin CSRF token (idempotentes + HMAC previene tampering del cliente).
Para sitios con requirements estrictos, agregar
`[ValidateAntiForgeryToken]` y configurar el design-system frontend.

### Parte B — Product query

**`IShopQuery`** (seam):
```csharp
IReadOnlyList<ProductSummary> GetProducts(ShopQueryRequest request);
ProductSummary? GetProductBySku(string sku);
```

`ShopQueryRequest(MaxItems = 12, Skip = 0, CategoryAliasOrName?,
SortBy?)` con sortBy: `"name"` (default), `"price-asc"`,
`"price-desc"`, `"newest"`.

`ProductSummary(Sku, Name, Price, Currency, ImageUrl, Url, InStock,
CategoryName)`.

**`DefaultShopQuery`** (impl): recorre `productPage` descendants del
siteRoot del request (fallback a todos los siteRoots si no hay
request), aplica filtros, sortea, proyecta. Currency viene de
`CartSettings.Currency` (single source of truth para la moneda del
sitio).

### Parte C — Templates Razor

**`Views/ProductPage.cshtml`** + `uSync/Templates/productpage.config`
(GUID fresh): detalle con header (categoría + nombre + SKU), galería
de imágenes, precio formateado en `es-CO`, stock badge,
`<section data-variants="...">` para hidratación JS del selector de
variantes, `<button data-action="add-to-cart" data-sku="...">` que
el design-system frontend captura para POST a `/api/shop/cart/add`,
body opcional via Layout Composer `sections`.

**`Views/ProductCategoryPage.cshtml`** + `uSync/Templates/...`:
landing de categoría con header (nombre + descripción) + nav de sort
(query string `?sort=`) + grid de productos paginado (PageSize = 12,
detección de hasNext con `MaxItems = PageSize+1`) + paginación
prev/next via `?page=N`.

`productpage.config` y `productcategorypage.config` actualizados con
`DefaultTemplate` asignado.

## Consequences

**Positivas:**

- **Shop runtime end-to-end**: arquitecto crea siteRoot →
  productCategoryPage → N productPage → publica → URL pública del
  catálogo y del detalle funcionan. Visitante anónimo puede agregar
  al cart → cookie firmada persiste → ver/modificar via
  `/api/shop/cart/*`. Sin login required.
- **Cart hidratado en tiempo real**: cambios de precio en CMS se
  reflejan inmediatamente en cualquier render del cart (no hay
  precio cacheado en cookie — solo SKU + cantidad).
- **Variantes funcionan**: `productVariantsJson` con
  `[{sku, attributes, priceDelta}]` se respeta tanto en cart
  hidratación (suma `priceDelta`) como en el frontend (data-variants
  para hidratación JS).
- **DRY query logic**: `IShopQuery` consumido por ProductCategoryPage
  + (próximamente) `ProductGrid` block. Cambiar el orden, los
  filtros o agregar campos a `ProductSummary` impacta una sola
  pieza.
- **Cookie firmada**: cliente no puede inyectar items con precio
  fake o cantidades absurdas — el servidor reproyecta todo desde el
  CMS.

**Negativas:**

- **Sin checkout**: el cart provee estado pero no procesa pagos.
  Integración con gateway (Wompi, Mercado Pago, Stripe) vendría en
  futura ola. Por ahora el flujo termina en "ver cart".
- **`SecretKey` default inseguro**: `"synergos-dev-secret-change-me"`
  funciona en dev pero cualquiera con acceso al binario puede falsear
  cookies de cart en producción si no se sobreescribe. Documentado
  en CartSettings + ADR. **TODO próxima micro-ola**: agregar
  startup check que loguee `Critical` si `SecretKey == default` y
  `IWebHostEnvironment.EnvironmentName != "Development"`.
- **Refactor de blocks shop pendiente**: los renderers existentes
  (CartItem, CartSummary, ProductCard, ProductGrid, etc.) aún hacen
  query inline o no consumen el cart service. Quedan listos para
  refactor en futura ola — el patron está aplicado en
  ProductPage/ProductCategoryPage como referencia.
- **Sin paginación stateful en cart**: cart es plano (lista de
  líneas). Para sitios con cart largo, KISS dice no paginar — el
  visitante decide.

**Neutras:**

- 2 GUIDs nuevos (productpage Template + productcategorypage
  Template). Verificación cuádruple OK.
- `CartSettings.Currency` es la moneda canonical del sitio — usada
  por ICartService Y IShopQuery. Single source of truth.
- Sin `IBlogQuery`-style `OrderBy(publishDate)` porque productos no
  tienen `publishDate`; usa `UpdateDate` de Umbraco para `"newest"`
  sortBy (puede no coincidir con cuándo el SKU se "lanzó", pero es
  un proxy razonable hasta que un campo `productLaunchDate` se añada).

## Alternatives considered

- **Cart en `ISession` (server-side)**. Descartado. Requiere
  `services.AddDistributedMemoryCache()` o Redis; complica deploy
  multi-instancia. Cookie firmada es stateless y suficiente para el
  scope.
- **Cart en DB con guest session ID**. Descartado por scope. Cookie
  HMAC cubre el caso del 99% (visitante anónimo abandona/vuelve).
  Si el sitio adopta Members, una variante de `ICartService` puede
  mover el cart al `member.Properties` tree.
- **CSRF token obligatorio**. Diferido. HMAC de cookie + endpoints
  idempotentes es razonable para start. Si un sitio necesita
  hardening adicional, agregar `[ValidateAntiForgeryToken]` es
  cambio local.
- **`IShopQuery` async**. Descartado. Umbraco's published cache es
  síncrono in-memory; agregar async sin razón es ceremonia.
- **Precio en cookie**. Descartado. Inseguro (cliente puede falsear
  precio). Hidratar desde CMS en cada `GetCart()` es la decisión
  correcta.

## Implementation summary (Ola 57, 2 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-57.1)` | `251052e` | `CartSettings` + `ICartService` + `DefaultCartService` (cookie HMAC) + `ShopController` endpoints |
| `feat(ola-57.2)` | `e5a151b` | `IShopQuery` + `DefaultShopQuery` + `ProductPage.cshtml` + `ProductCategoryPage.cshtml` + 2 uSync Templates + DefaultTemplate asignado |

## References

- ADR 0009 — Extension seams (ICartService + IShopQuery siguen el
  patrón)
- ADR 0010 — Branding via provider (CartSettings sigue el POCO +
  IOptions pattern)
- ADR 0027 — Blog runtime (IShopQuery espeja el patrón de IBlogQuery)
- `refactor-docs/migration/05-legacy-refinement-inventory.md` —
  desbloqueo del módulo Shop (item #14 del backlog)
