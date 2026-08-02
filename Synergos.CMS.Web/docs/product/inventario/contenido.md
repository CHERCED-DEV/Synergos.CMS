# Dominio: Contenido y captación

## Resumen ejecutivo

El núcleo editorial (blog, comentarios, SEO, formularios, búsqueda) está **VIVO**
de punta a punta: recorre el published cache de Umbraco en tiempo real, persiste
en filesystem JSON o vía Examine, y no depende del CDN externo. El **Layout
Composer** es, con evidencia, el feature más terminado del repo: 14 presets, 13
con defaults server-side verificados (uno, `SnippetRef`, deliberadamente
excluido), plugin backoffice vivo con preview + directiva JS de defaults, y 170
tipos de bloque instalables en `sections` sin tocar código — la afirmación de
"el más maduro" se sostiene. Hay, sin embargo, una capa paralela llamada
"Blogs — red social" (`IContentStream`/`IReactionService`/`BlogsController`,
988 líneas) que es pura **DEMO**: feed, reacciones y grafo social viven en
memoria de proceso sembrados por `SocialDemoSeed`/`BlogsDemoSeedHostedService`
y se pierden en cada reinicio — no debe confundirse con el blog editorial
(`postPage`/`IBlogQuery`), que sí es real. La búsqueda de contenido usa
**Examine** (`ExamineSearchProvider`), confirmando que el ADR 0107 (motor en
memoria) aplica solo a los catálogos de dominio (Tienda/Eventos/Propiedades/
Trámites/Educación) y no toca el buscador de sitio — ADR 0031 sigue vigente tal
como dice el propio ADR 0107. Los 4 bloques CDN-hosted del dominio
(`elementSynBlogs/CommentsWidget/SearchBox/FormStepper`) están bloqueados
externamente (`StubBundleRegistryClient` siempre null) y emiten placeholder.
El hueco de higiene que este inventario señalaba —`IDictionaryCache` registrado,
con invalidator wired y cero consumidores— **se cerró borrando el seam** (enmienda
al ADR 0009). Las 233 lecturas de diccionario en Views siguen usando
`@Umbraco.GetDictionaryValue` directo, que es el mecanismo real y el único.

## Capacidades

### Blog editorial (postPage)
- **Madurez**: VIVO
- **Seams**: `IBlogQuery` — `Synergos.CMS.Interfaces/IBlogQuery.cs`
- **Implementación**: `DefaultBlogQuery` — `Synergos.CMS.Web/Services/DefaultBlogQuery.cs`. Recorre `DescendantsOrSelfOfType("postPage")` del siteRoot activo, filtra por categoría/tags/autor, ordena por `publishDate` desc, proyecta a `PostSummary`. `GetRelated` pondera tags compartidos ×2 + misma categoría ×1.
- **Persistencia**: published cache de Umbraco (nodos reales `postPage`/`postCategoryPage`), sin store propio.
- **Superficie HTTP**: `GET /blog/tag/{tag}` (`BlogTagController.cs:35`, público) · `GET /blog/rss.xml` (`BlogRssController.cs:44`, público, cache `IMemoryCache`) · render directo `PostPage.cshtml` (RenderController implícito de Umbraco).
- **Schema CMS**: `postPage`, `postCategoryPage` — `uSync/v9/ContentTypes/postpage.config`, `postcategorypage.config`.
- **UI/CDN**: no usa bloque CDN — SSR puro Razor (`Views/PostPage.cshtml`).
- **Flags**: `BlogSettings.CategoryPageSize` (consumido en `BlogTagController.cs:39`), `OutputCacheSettings.BlogRssMinutes`/`Disabled`.
- **Tests**: 0 tests directos de `DefaultBlogQuery`, `BlogRssController` o `SitemapController` (dependen de `IUmbracoContextAccessor`, no hay harness de integración en el proyecto de tests). `BlogTagControllerTests.cs` tiene 4 tests (contra un fake `IBlogQuery`, no contra `DefaultBlogQuery`).
- **Huecos**: `Synergos.CMS.Web/Services/DefaultBlogQuery.cs` — sin caché, O(N posts); aceptado explícitamente en el XML-doc para sitios <10k posts. Sin test directo de la query real (`Synergos.CMS.Web/Services/DefaultBlogQuery.cs:1-190` completo sin cobertura).

### Comentarios (sobre nodos publicados, incl. posts)
- **Madurez**: VIVO
- **Seams**: `ICommentRepository`, `ICommentModerationNotifier` (+ `ICommentModerationNotifierChannel`) — `Synergos.CMS.Interfaces/ICommentRepository.cs`, `ICommentModerationNotifier.cs`.
- **Implementación**: `FileSystemCommentRepository` — `Synergos.CMS.Web/Services/FileSystemCommentRepository.cs`. Un JSON por nodo, hilos anidados a 2 niveles (`ParentId`), likes por comentario (`LikeAsync`), paginación de cola de moderación. Notificadores compuestos: `CompositeCommentModerationNotifier` + 5 canales (`Email/Webhook/Slack/Discord/Teams`) registrados en `SeamComposer.cs:964-973`.
- **Persistencia**: `{ContentRoot}/App_Data/syn-comments/{nodeId}.json` (default `CommentsSettings.StorageRoot`).
- **Superficie HTTP**: `POST /api/comments/{nodeId}` (público, rate-limited, gate por `RequireAuthentication`) · `POST /api/comments/{nodeId}/{commentId}/like` (público) — `CommentsController.cs:56,142`. Moderación: `GET /api/comments/moderation/pending`, `GET .../{nodeId}/pending`, `POST .../{nodeId}/{commentId}/approve|reject` — `CommentsModerationController.cs`, gated por rol vía `IMemberAccessGate.HasAnyRole("admin,moderator,editor")` (nota: decorados `[AllowAnonymous]` a nivel ASP.NET; el gate real es manual dentro de cada acción — `CommentsModerationController.cs:50-55` etc.).
- **Schema CMS**: `elementCommentThread` — `uSync/v9/ContentTypes/elementcommentthread.config`.
- **UI/CDN**: SSR directo `Views/Partials/Elements/Engagement/CommentThread.cshtml` (sin CDN). Existe además `elementSynCommentsWidget` (CDN) que es un bloque *distinto*, ver "SÓLO SEAM/bloqueado" abajo.
- **Flags**: `CommentsSettings` completo — `RequireAuthentication` (default `true`), `RequireModeration` (default `false`), `MaxCommentsPerHourPerIp` (5), `MaxBodyLengthChars` (2000), `NotifyEmailAddress`/`WebhookUrl`/`SlackWebhookUrl`/`DiscordWebhookUrl`/`TeamsWebhookUrl` (todos opt-in vacíos).
- **Tests**: `FileSystemCommentRepositoryTests.cs` (9 tests) + `CompositeNotifiersTests.cs` (10, comparte con Forms). 0 tests de `CommentsController`/`CommentsModerationController` directamente.
- **Huecos**: Gate de moderación es `[AllowAnonymous]` + chequeo manual de rol — funciona pero es un patrón frágil (fácil de olvidar en un endpoint nuevo) — `Synergos.CMS.Web/Controllers/CommentsModerationController.cs:49-56`.

### Reacciones — dos sistemas distintos, no confundir
- **Madurez**: VIVO (likes de comentarios) / **DEMO** (reacciones del feed social)
- **Seams**: `ICommentRepository.LikeAsync` (VIVO, ver capacidad Comentarios) vs. `IReactionService` — `Synergos.CMS.Interfaces/IReactionService.cs`.
- **Implementación DEMO**: `StubReactionService` — `Synergos.CMS.Application/Services/Impl/StubReactionService.cs`. Estado en memoria del proceso (`ConcurrentDictionary`), toggle idempotente por (actor,objeto,tipo). Registrado como singleton en `SeamComposer.cs:382-383`.
- **Persistencia**: proceso en memoria — se pierde en cada reinicio del sitio (no filesystem, no DB).
- **Superficie HTTP**: expuesto vía `BlogsController` (`/api/blogs/...`, ver capacidad "Blogs — red social" abajo), no vía un endpoint propio.
- **Schema CMS**: ninguno — no hay DocType detrás; `objectKey` es un id string opaco del feed sembrado (`SocialDemoSeed.Posts`).
- **UI/CDN**: consumido por `elementSynBlogs` (bloqueado externamente, ver abajo).
- **Flags**: ninguno.
- **Tests**: `StubReactionServiceTests.cs` (9 tests) — cubre el seam en aislamiento, no la integración end-to-end con contenido real.
- **Huecos**: `Synergos.CMS.Application/Services/Impl/StubReactionService.cs` es el ÚNICO adapter registrado; no hay ningún adapter real (DB/event-sourced) en el repo — la doc del seam lo anticipa ("el adapter real... se enchufa sin tocar el módulo Angular") pero no existe.

### Blogs — red social (feed/follow/DM, distinto del blog editorial)
- **Madurez**: DEMO
- **Seams**: `IContentStream`, `ISocialGraphService`, `ISocialProfileProjection`, `INotificationFeed` (fuera del scope estricto del barrido pero acoplados) — `Synergos.CMS.Interfaces/IContentStream.cs`.
- **Implementación**: `StubContentStream` — `Synergos.CMS.Application/Services/Impl/StubContentStream.cs`. Feed paginado por cursor sobre `SocialDemoSeed.Posts` (hardcodeado) + items creados en runtime (`ConcurrentDictionary`, se pierden al reiniciar).
- **Persistencia**: memoria de proceso; sembrado al boot por `BlogsDemoSeedHostedService` — `Synergos.CMS.Web/Services/BlogsDemoSeedHostedService.cs:36` (DMs + guardados, vía seams genéricos `IMessagingService`/`IUserCollection`).
- **Superficie HTTP**: `GET /api/blogs/feed`, y ~15 endpoints más (perfil, DMs, notificaciones, estudio) — `Synergos.CMS.Web/Controllers/BlogsController.cs` (988 líneas), sin auth-gate en lectura, escritura gateada por `IMemberAccessGate` desde ADR 0103 (`RequireActor()` línea 79).
- **Schema CMS**: ninguno (no es contenido Umbraco, es una app social hardcodeada).
- **UI/CDN**: `elementSynBlogs` → `<synergos-blogs>` (bloqueado por CDN, ver abajo).
- **Flags**: ninguno.
- **Tests**: `StubContentStreamTests.cs` (13), `BlogsControllerOla6Tests.cs` (10), `StubSocialGraphServiceTests.cs` (9).
- **Huecos**: Esto NO es el "blog" que un editor de contenido usa — es una app social independiente con datos sembrados que un stakeholder podría confundir con el CMS editorial por compartir el nombre "Blogs". `Synergos.CMS.Application/Services/Impl/StubContentStream.cs:156` (`SocialDemoSeed.Posts.Select(...)`) es la evidencia del hardcode.

### Búsqueda de contenido (sitio)
- **Madurez**: VIVO
- **Seams**: `ISearchQuery`, `ISearchAnalyticsStore` — `Synergos.CMS.Interfaces/ISearchQuery.cs`, `ISearchAnalyticsStore.cs`.
- **Implementación**: `ExamineSearchProvider` — `Synergos.CMS.Web/Services/ExamineSearchProvider.cs`. Usa el `ExternalIndex` de Examine (auto-mantenido por Umbraco al publish/unpublish), `GroupedOr` sobre `SearchSettings.SearchableFields`, hidrata contra el published cache real. **Confirmado activo** — es el único `ISearchQuery` en el repo y está registrado en `SeamComposer.cs:505`.
- **ADR 0107 — aclaración verificada**: el motor "en memoria" (`ICatalogIndex<T>`) que introdujo ese ADR aplica **solo** a los 5 catálogos de dominio (Tienda/Eventos/Propiedades/Trámites/Educación), NO al buscador de contenido. El propio ADR 0107 lo dice explícitamente (línea 16): *"Para el contenido del sitio, Examine ya está vivo y hecho desde la Ola 86 (ExamineSearchProvider/ISearchQuery, ADR 0031). No hay hueco."* Verificado en código: cero referencias a `ICatalogIndex` en `ExamineSearchProvider.cs` ni en `SearchController.cs`.
- **Persistencia**: índice Lucene interno de Examine (`ExternalIndex`), sin store propio; analytics en memoria (`InMemorySearchAnalyticsStore`, `ConcurrentDictionary`).
- **Superficie HTTP**: `GET /api/search?q=&maxItems=&skip=&docType=` (público) · `GET /api/search/analytics?from=&to=&limit=` (gateado por rol si `SearchSettings.AnalyticsAdminRolesCsv` no está vacío, default `"admin,editor"`) — `SearchController.cs:53,101`.
- **Schema CMS**: `searchPage` — `uSync/v9/ContentTypes/searchpage.config`; sin DocType propio de índice (Examine indexa todo el published cache).
- **UI/CDN**: `elementSynSearchBox` → `<synergos-search-box>` (bloqueado por CDN) + `Views/SearchPage.cshtml` (SSR nativo, funcional sin CDN).
- **Flags**: `SearchSettings.SearchableFields` (default 5 campos), `ExcludedDocTypeAliases` (6 alias de settings), `MaxHitsHardCap` (100), `ExcerptMaxLength` (200), `AnalyticsAdminRolesCsv` (`"admin,editor"`).
- **Tests**: 0 tests directos de `ExamineSearchProvider` o `SearchController` (requieren Examine/Umbraco runtime, sin harness). Analytics store no tiene test file localizado en el barrido.
- **Huecos**: `Synergos.CMS.Web/Services/ExamineSearchProvider.cs` sin tests — el propio ADR 0107 documenta que precisamente ESTE tipo de frontera (query string → seam) fue donde vivían los bugs reales en los catálogos de dominio; el buscador de contenido no tiene ese tipo de test de frontera tampoco.

### SEO
- **Madurez**: VIVO
- **Seams**: ninguno propio — consume `compSeo`/`compTagging` directo vía `IPublishedContent.Value<T>()` + `IBrandingProvider` (fuera de este dominio estrictamente, pero inyectado).
- **Implementación**: `_SeoHead.cshtml` — `Synergos.CMS.Web/Views/Shared/_SeoHead.cshtml`. Cascada seoTitle/Description → `siteConfigSettings` del brand activo → `page.Name`. Canonical, hreflang por cultura publicada, OpenGraph, Twitter Card, JSON-LD (Organization + WebSite). `_SeoStructuredData.cshtml` y `_Breadcrumbs.cshtml` complementan (no leídos en detalle en este barrido, pero confirmada su existencia).
- **Persistencia**: propiedades de contenido (`compSeo` composition) + `siteConfigSettings` (árbol Settings).
- **Superficie HTTP**: no aplica (partial incluida en `_Layout.cshtml`), más `sitemap.xml`/`news-sitemap.xml`/`sitemap_index.xml`/`robots.txt` como endpoints dedicados.
- **Schema CMS**: composition `compSeo` (no leída línea por línea, pero referenciada extensamente en `uSync/v9/ContentTypes/comp*.config`).
- **Superficie HTTP adicional**: `GET /sitemap.xml` (`SitemapController.cs:53`, cacheado `IMemoryCache`, excluye `SearchSettings.ExcludedDocTypeAliases`), `SitemapIndexController`, `NewsSitemapController`, `RobotsController` — confirmados presentes, no auditados línea por línea (fuera de foco del Layout Composer/blog, pero mismo patrón: `IUmbracoContextAccessor` + recorrido real del published cache, cacheado con `IMemoryCache`).
- **Flags**: `OutputCacheSettings.SitemapMinutes`/`Disabled`.
- **Tests**: no localizados tests directos de `_SeoHead.cshtml` ni `SitemapController` (vistas Razor no son unit-testeables sin harness; controllers dependen de Umbraco context).
- **Huecos**: sin test de frontera para la cascada de fallback SEO (page → brand config → nombre) — cambios ahí solo se detectan visualmente.

### Formularios (path interno)
- **Madurez**: VIVO
- **Seams**: `IFormSubmissionHandler`, `IFormSubmissionReader`, `IFormDefinitionReader`, `IFormSubmissionNotifier` (+ channels) — `Synergos.CMS.Interfaces/IFormSubmissionHandler.cs` etc.
- **Implementación**: `FileSystemFormSubmissionHandler` (implementa handler+reader) — `Synergos.CMS.Web/Services/FileSystemFormSubmissionHandler.cs`; `UmbracoFormDefinitionReader` — `Synergos.CMS.Web/Services/UmbracoFormDefinitionReader.cs`, recorre TODO el árbol (no solo siteRoot, a propósito porque el POST no tiene página en contexto) buscando bloques `elementFormContainer` dentro de cualquier BlockGrid/BlockList de cualquier propiedad, valida campos `Required` contra el contenido publicado (no contra lo que el cliente afirma) — cierra un hueco de validación real documentado en el propio XML-doc del seam.
- **Persistencia**: `{ContentRoot}/App_Data/syn-form-submissions/{formKey}/{timestamp}_{guid}.json`.
- **Superficie HTTP**: `POST /api/forms/{formKey}/submit` (público, honeypot + rate-limit + validación de campos obligatorios contra `IFormDefinitionReader`) — `FormSubmissionsController.cs:60`. PRG con `?submitted=1` / `?form-error={code}`.
- **Schema CMS**: `elementFormContainer`, `elementFormField`, `elementFormEmbed` — `uSync/v9/ContentTypes/elementformcontainer.config` etc.
- **UI/CDN**: `elementSynFormStepper` (CDN, bloqueado) — el path interno normal (`elementFormContainer`) es SSR plain HTML, no depende del CDN.
- **Flags**: `FormsSettings` completo — `StorageRoot`, `MaxFieldLengthChars` (5000), `MaxFieldsPerSubmission` (50), `HoneypotFieldName` (`"syn_hp"`), `MaxSubmissionsPerHourPerIp` (10), `NotifyEmailAddress`/`WebhookUrl`/`SlackWebhookUrl`/`DiscordWebhookUrl`/`TeamsWebhookUrl` (opt-in vacíos).
- **Tests**: `FileSystemFormSubmissionHandlerTests.cs` (6), `CompositeNotifiersTests.cs` (10, compartido con Comments). 0 tests de `FormSubmissionsController` ni `UmbracoFormDefinitionReader`.
- **Huecos**: sin `[ValidateAntiForgeryToken]` (documentado como decisión consciente en el XML-doc, no un descuido) — `FormSubmissionsController.cs:20-23`. `UmbracoFormDefinitionReader.GetByKey` es O(páginas) por cada submit (barato con ~95 páginas, pero sin caché ni invalidación si el sitio crece) — `Synergos.CMS.Web/Services/UmbracoFormDefinitionReader.cs:44`.

### i18n / Dictionary
- **Madurez**: VIVO — un solo mecanismo, el nativo de Umbraco.
- **Seams**: ninguno. `IDictionaryCache` **se eliminó** el 2026-08-02 (enmienda al ADR 0009) junto con `DictionaryCache`, `DictionaryCacheInvalidator`, sus dos suites de tests y los registros del composer. Nunca tuvo un lector, y por construcción no podía tenerlo: la interfaz no expone `Set`, así que el registro DI dejaba inalcanzable el `Set` de la clase concreta — era un cache que no podía guardar nada. **No re-crearlo sin un lector real.**
- **Implementación**: las 394 vistas Razor usan `@Umbraco.GetDictionaryValue("Key", "fallback")` directo (233 ocurrencias contadas), el helper nativo que ya lee del published-dictionary-cache interno de Umbraco. Los catalog sources que resuelven etiquetas server-side (`UmbracoPropertyCatalogSource`, `UmbracoStayContentSource`, `UmbracoTramiteCatalogSource`) usan `ICultureDictionaryFactory` con respaldo es-CO.
- **Persistencia**: 443 ficheros `.config` en `uSync/v9/Dictionary/` (es-CO + en-US) — esa es la fuente real que Umbraco lee directamente.
- **Superficie HTTP**: ninguna.
- **Schema CMS**: N/A (Dictionary no es DocType).
- **UI/CDN**: N/A.
- **Flags**: ninguno.
- **Tests**: ninguno propio. El comportamiento que importa —que una etiqueta ausente caiga al respaldo en vez de salir en blanco— lo cubren los tests de los catalog sources.
- **Huecos**: ninguno conocido.

### Layout Composer
Ver sección dedicada abajo.

## El Layout Composer en detalle (¿qué puede armar un editor SIN programador?)

**Presets confirmados: 14** (no 13, no 10 — se verificó contando ficheros
`uSync/v9/ContentTypes/elementlayout*.config`): Section, Container, Stack,
Grid, Column, 1Col, 2ColEven, 2ColMainSidebar, 3Col, 4Col, HolyGrail,
SidebarMain, Hero, SnippetRef.

**Defaults server-side — verificados, funcionan de verdad:**
`LayoutPresetDefaults.cs` (`Synergos.CMS.Web/Notifications/LayoutPresetDefaults.cs`)
es un `INotificationHandler<ContentSavingNotification>` que parsea el JSON
crudo del BlockGrid en cada save, busca entradas cuyo `contentTypeKey` esté en
una whitelist hardcodeada de **13 GUIDs** (todos los presets MENOS
`SnippetRef`, que no tiene chrome props — correcto por diseño), y rellena
per-prop (`containerType=normal`, `theme=light`, `spacingTop/Bottom=lg`,
`spacingInline=md`) solo donde el editor dejó el campo vacío. Se verificaron
los 13 GUIDs contra los `Key=` reales de los `.config` — coinciden
exactamente. **Huecos**: 0 tests directos de `TryApplyDefaults` (la lógica de
parseo JSON no tiene test file localizado) pese a ser lógica no trivial
(GUID matching + JSON walking) — riesgo real si un preset nuevo se agrega y
se olvida añadir su GUID a la whitelist (fallaría en silencio, sin excepción).

**JS pre-drop defaults**: confirmado, `layout-composer.preview.js:289-319`
(`lcInitDefaults` directive), rellena las mismas 5 props en el DOM-bound data
ANTES de que el editor vea el overlay — complementa (no reemplaza) el handler
C#, documentado explícitamente como "per-prop fill" en ambos lados.

**Plugin backoffice**: vivo. `App_Plugins/LayoutComposer/package.manifest`
carga 1 JS (321 líneas: extracción de texto para preview cards + defaults +
filtros AngularJS) + 1 CSS. 14 thumbnails SVG + 14 vistas HTML de preview
(`views/block-*.html`), una por preset.

**Composición sin tocar código — hasta dónde llega:**
- Un editor puede dropear cualquiera de los 14 presets en el root de
  `sections` (BlockGrid `DTBlockGridSections`, `allowAtRoot: true` en la
  mayoría) o anidarlos (`allowInAreas: true`).
- Dentro de las áreas de cada preset puede dropear cualquiera de los **170
  tipos de bloque** declarados en `DTBlockGridSections.config` (conteo real
  vía `grep -c contentElementTypeKey`; el número "148" de CLAUDE.md parece
  desactualizado o contaba solo un subconjunto — no verificado el origen de
  esa cifra), organizados en 14 grupos (`Layout`, `Syn (CDN)`, `Comp`, `Text`,
  `Action`, `Media`, `Info`, `Corp`, `Structural`, `Form`, `Nav`, `Shop`,
  `Member`, `Flow`).
- Cada preset renderiza semantic HTML real (`<section>`, `<aside>`, `<main>`,
  `<nav>` según el area alias) vía SSR Razor puro — verificado en
  `elementLayoutSection.cshtml`, sin dependencia de CDN.
- El wrapper `_Wrapper.cshtml` (compDom*, ADR 44.1) aplica clases/atributos
  editoriales al HTML emitido por los partials SynHost — pero es un mecanismo
  DISTINTO del Layout Composer (aplica a cualquier bloque, no solo a
  presets).
- **Starter scaffold** (`LayoutComposerStarterScaffold.cs`): opt-in vía
  `Synergos:LayoutComposer:EnableStarterScaffold` (default `false`,
  verificado en `LayoutComposerSettings.cs`) — pre-llena una página
  `pageBase` en blanco con Hero + 2ColEven al primer save. Apagado por
  defecto ("preserva UX sin sorpresas").

**Veredicto sobre la doc**: la afirmación "el feature más maduro" se sostiene
con evidencia — es el único subsistema del dominio con: (a) un plugin
backoffice propio y funcional, (b) doble capa de defaults (JS + C#)
verificada y consistente entre sí, (c) 170 tipos de bloque combinables sin
código, y (d) cero dependencia del bloqueo externo del CDN (a diferencia de
blog social, comentarios-widget, search-box y form-stepper, que si son
CDN-hosted quedan en placeholder). El único punto débil real es la ausencia
de tests directos sobre `LayoutPresetDefaults.TryApplyDefaults`.

## Flujo end-to-end que HOY funciona

1. Editor arma una página con Layout Composer (presets + bloques de
   contenido) — 100% SSR, sin CDN.
2. Editor publica un `postPage` con tags/categoría/autor → aparece en
   `IBlogQuery.GetPosts` (listados), `/blog/tag/{tag}`, `/blog/rss.xml`,
   `sitemap.xml`, y es indexado automáticamente por Examine para
   `/api/search`.
3. `_SeoHead.cshtml` resuelve título/descripción/canonical/hreflang/OG/
   JSON-LD con fallback a `siteConfigSettings` del brand activo — sin
   intervención del editor si no seteó nada.
4. Un visitante comenta en `elementCommentThread` (SSR) →
   `POST /api/comments/{nodeId}` → `FileSystemCommentRepository` persiste
   JSON → si no está en cola de auto-aprobación, dispara
   `ICommentModerationNotifier` (email/webhook/Slack/Discord/Teams según
   config) → un moderador con rol `admin/moderator/editor` aprueba desde
   `/api/comments/moderation/*`.
5. Un visitante envía un `elementFormContainer` → honeypot + rate-limit +
   validación de campos obligatorios contra `IFormDefinitionReader` (lee el
   contenido publicado real) → `FileSystemFormSubmissionHandler` persiste →
   `IFormSubmissionNotifier` composite dispara canales configurados.
6. Un visitante busca en `/api/search?q=` → `ExamineSearchProvider` contra
   el índice real → `InMemorySearchAnalyticsStore` registra la query para
   `/api/search/analytics`.

Todo esto es real, verificado abriendo el código de cada paso — no hay
seeding, no hay datos hardcodeados, no hay "vacío disfrazado de éxito".

## Flujo que NO cierra y por qué

1. **"Blogs" red social completo** (`/api/blogs/*`, 988 líneas de
   controller): feed, follow, DMs, reacciones — todo en memoria de proceso,
   sembrado por `SocialDemoSeed`. Un reinicio del sitio borra toda la
   actividad creada en runtime (`StubContentStream._created`,
   `ConcurrentDictionary`). No hay adapter real registrado en
   `SeamComposer.cs` para ninguno de `IContentStream`/`IReactionService`/
   `ISocialGraphService` — solo los stubs. Riesgo de confusión: el nombre
   "Blogs" es compartido con el sistema editorial real (`postPage`), pero
   son sistemas completamente independientes.
2. **4 bloques CDN-hosted del dominio** (`elementSynBlogs`,
   `elementSynCommentsWidget`, `elementSynSearchBox`,
   `elementSynFormStepper`): `StubBundleRegistryClient.TryResolveAsync`
   siempre devuelve `null` (`Synergos.CMS.Application/Proxies/Impl/
   StubBundleRegistryClient.cs:39-42`) → `DefaultSynHostEmitter` emite un
   `<!-- synHost: bundle registry did not resolve... -->` comment +
   `data-synergos-cdn-offline="true"` + fallback vacío. Bloqueado
   externamente hasta que el equipo CDN publique el contrato
   (`docs/umbraco/cdn-contract.md`) — no es un bug del CMS, es una
   dependencia externa documentada.
3. **`IDictionaryCache`** — **RESUELTO (2026-08-02)**: era infraestructura completa
   (seam + impl + composer registration + invalidator + tests) para una capacidad
   que el proyecto terminó resolviendo de otra forma (`Umbraco.GetDictionaryValue`
   nativo). Se borró entera. El riesgo que se cierra es el de claridad, que era el
   real: un agente nuevo podía intentar "usar" el seam pensando que hacía algo.
4. **`ICompositionReader<TInput,TOutput>` / `IElementViewModelMapper<TInput,TOutput>`**:
   ambos son "resolvers" (`CompositionResolver.cs`, `ElementViewModelResolver.cs`)
   documentados explícitamente en su propio XML-doc como "not registered in
   DI yet" / "future wiring ola". Cero registro en `SeamComposer.cs`
   (verificado por grep), cero consumers fuera de sus propios tests. Son
   scaffolding arquitectónico para reemplazar a los mappers ad-hoc del
   proyecto legado, pero hoy no hacen nada en producción — todo el mapeo
   real (compSeo, compDom*, compTagging) se hace con `.Value<T>()` directo
   en cada Razor partial.

## Tabla de artefactos

| DocType/Schema | Seam | Implementación | Endpoint | Elemento UI | Madurez |
|---|---|---|---|---|---|
| `postPage`/`postCategoryPage` | `IBlogQuery` | `DefaultBlogQuery` | `/blog/tag/{tag}`, `/blog/rss.xml` | SSR (`PostPage.cshtml`) | VIVO |
| `elementCommentThread` | `ICommentRepository` | `FileSystemCommentRepository` | `POST /api/comments/{nodeId}[/like]` | SSR (`CommentThread.cshtml`) | VIVO |
| — (comentarios) | `ICommentModerationNotifier` | `CompositeCommentModerationNotifier` + 5 canales | `POST /api/comments/moderation/*` | backoffice futuro | VIVO |
| `elementSynCommentsWidget` | `ISynHostEmitter` | `DefaultSynHostEmitter` | — | `<synergos-comments-widget>` | INERTE (CDN bloqueado) |
| — (feed social) | `IContentStream`, `IReactionService` | `StubContentStream`, `StubReactionService` | `/api/blogs/feed`, etc. | `elementSynBlogs` / `<synergos-blogs>` | DEMO |
| `searchPage` | `ISearchQuery` | `ExamineSearchProvider` | `GET /api/search` | SSR (`SearchPage.cshtml`) | VIVO |
| — (analytics) | `ISearchAnalyticsStore` | `InMemorySearchAnalyticsStore` | `GET /api/search/analytics` | — | VIVO (en memoria, no persiste tras reinicio) |
| `elementSynSearchBox` | `ISynHostEmitter` | `DefaultSynHostEmitter` | — | `<synergos-search-box>` | INERTE (CDN bloqueado) |
| `elementFormContainer`/`elementFormField` | `IFormSubmissionHandler`, `IFormDefinitionReader` | `FileSystemFormSubmissionHandler`, `UmbracoFormDefinitionReader` | `POST /api/forms/{formKey}/submit` | SSR plain HTML | VIVO |
| `elementSynFormStepper` | `ISynHostEmitter` | `DefaultSynHostEmitter` | — | `<synergos-form-stepper>` | INERTE (CDN bloqueado) |
| `compSeo` (composition) | — (sin seam) | `_SeoHead.cshtml` directo | — | `<head>` | VIVO |
| — | — | `SitemapController`/`NewsSitemapController`/`RobotsController` | `/sitemap.xml`, `/news-sitemap.xml`, `/robots.txt` | — | VIVO |
| Dictionary (.config × 443) | — (seam eliminado, ADR 0009 enmendado) | `Umbraco.GetDictionaryValue` nativo | — | — | VIVO (un solo mecanismo) |
| `elementLayout*` (× 14) | — (sin seam propio) | `LayoutPresetDefaults` (notification) + 14 partials Razor | render directo (BlockGrid) | App_Plugins/LayoutComposer (backoffice) | VIVO |
| — (mapeo genérico) | `ICompositionReader<,>` | `CompositionResolver` | — | — | SÓLO SEAM (no registrado en DI) |
| — (mapeo genérico) | `IElementViewModelMapper<,>` | `ElementViewModelResolver` | — | — | SÓLO SEAM (no registrado en DI) |
