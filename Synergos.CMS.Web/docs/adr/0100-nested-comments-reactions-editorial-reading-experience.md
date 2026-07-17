# ADR 0100 — Comentarios anidados (2 niveles) + reacciones + experiencia editorial del post (TOC, progreso, share, tag page)

- **Status:** Accepted
- **Date:** 2026-06-26
- **Deciders:** Arquitecto + agente, fase SynergosLabs (OLA-4 "experiencia editorial del blog al estándar 2025"). Verificado contra código vivo (`FileSystemCommentRepository`, `CommentsController`, `PostPage.cshtml`, `DefaultBlogQuery`).
- **Relacionados:** ADR 0038 (comments runtime end-to-end), ADR 0027 (blog: postPage/authorPage/postCategoryPage + IBlogQuery), ADR 0075 (tests por seam), ADR 0021 (DataType ↔ intent — share/tags), ADR 0094 (design tokens SOT).

---

## Context

El módulo de comentarios (ADR 0038) persistía un modelo `Comment` **plano**:
una lista por nodo, sin jerarquía ni reacciones. El blog (ADR 0027) ya tenía
post/categoría/autor + `IBlogQuery`, pero la página de post era una columna
sola: sin tabla de contenidos, sin progreso de lectura, sin share fijo, y los
tags eran pills no-clicables (callejón sin salida). No existía una tag page.

La OLA-4 pidió llevar la experiencia editorial al estándar 2025 sin romper
nada de lo persistido. Tres restricciones gobiernan el cambio:

1. **El store de comentarios es un JSON por nodo** (`App_Data/syn-comments/{nodeId}.json`)
   con comentarios reales en producción/demo. Cualquier cambio de forma del
   record `Comment` debe **deserializar tolerante** el JSON legacy.
2. **Grafo de dependencias** (ADR 0002): el modelo y el seam viven en
   `Interfaces`; la impl filesystem en `Web`. `Application`/`Interfaces` no
   conocen Umbraco ni ASP.NET.
3. **Sin schema nuevo donde se pueda evitar** (ADR 0008): la tag page no
   justifica un DocType — se resuelve con RenderController + ruta virtual.

Además, un bug previo del POST de comentarios (parámetro sin `[FromForm]`)
obligaba a ser explícito al agregar `parentId` al binding del form.

## Decision

### 1. Modelo `Comment` persistido: `ParentId` + `Likes` (backward-compat por defaults)

El record `Comment` (en `Synergos.CMS.Interfaces/ICommentRepository.cs`) gana dos
campos **opcionales con default**, lo que hace la deserialización tolerante sin
custom converter (System.Text.Json deja el default cuando la propiedad falta):

```csharp
public sealed record Comment(
    string Id, int NodeId, string? MemberKey, string AuthorName,
    string Body, DateTime CreatedAtUtc, bool Approved,
    Guid? ParentId = null,   // null = top-level. Ausente en JSON legacy ⇒ null.
    int Likes = 0);          // Ausente en JSON legacy ⇒ 0.

public sealed record NewComment(
    int NodeId, string? MemberKey, string AuthorName, string Body,
    Guid? ParentId = null);
```

**Migración:** ninguna migración escrita. Los JSON viejos (sin los campos) se
leen como top-level con 0 likes. La forma nueva se escribe sólo cuando se crea
o reacciona a un comentario — los archivos legacy se reescriben con la forma
completa la primera vez que su nodo muta. No hay back-fill batch ni versión de
schema en el archivo: el contrato es "campo ausente = default".

### 2. Anidación de **2 niveles** (no árbol arbitrario)

Hilo de 2 niveles: top-level + replies. El repositorio **normaliza** al persistir
(`ResolveTopLevelParent`): responder a una respuesta re-ancla al abuelo
top-level; un `ParentId` fantasma o no-aprobado degrada el comentario a
top-level. El invariante "máximo 2 niveles" se garantiza en el write, no en el
render. El render agrupa replies por `ParentId` bajo cada top-level en
`.syn-comment__replies`.

### 3. Reacción "like" simple

`ICommentRepository.LikeAsync(nodeId, commentId)` incrementa `Likes` en uno y
devuelve el comentario actualizado, o `null` si no existe / no está aprobado.
**No idempotente por diseño**: cada POST suma uno, sin tracking de identidad de
quien reacciona (KISS — reacción ligera anónima, coherente con el modelo de auth
actual). Endpoint `POST /api/comments/{nodeId}/{commentId}/like`, rate-limited
por IP igual que la creación.

### 4. POST con `parentId` ([FromForm] explícito)

`CommentsController.Submit` recibe `[FromForm] string? parentId` (parse tolerante
`Guid.TryParse`; vacío/inválido ⇒ top-level). El render emite un form de
"responder" por comentario top-level con `<input type="hidden" name="parentId">`.

### 5. Experiencia de lectura: TOC sticky + barra de progreso + share + tags clicables

- `wwwroot/js/syn-reading.js` (IIFE, post-scoped, cargado por `PostPage.cshtml`):
  (a) barra de progreso `.syn-reading-progress__bar` por % de scroll del artículo;
  (b) TOC auto-construido desde los `h2/h3` de `.syn-post__body` (slug `id` si
  falta), inyectado en `<nav.syn-post__toc>`, con scroll-spy vía
  IntersectionObserver; (c) toggle de los forms de reply. Respeta
  `prefers-reduced-motion`. No-op si sus contenedores no existen.
- `PostPage.cshtml` pasa a **layout 2-col** (`.syn-post-layout`: TOC aside sticky
  | artículo) en ≥1024px, 1-col apilado en móvil. Share (`Corp/SocialShare`) y el
  hilo de comentarios se renderizan como **partials FIJOS** (todo post los tiene
  sin que el editor los dropee — se elimina el `elementCommentThread` del cuerpo
  sembrado para no duplicar). Los tags pasan a `<a href="/blog/tag/{tag}">`.

### 6. Tag page: RenderController + seam, sin schema

`IBlogQuery.GetByTag(tag, maxItems)` (atajo sobre `GetPosts` con `TagsCsv` de un
solo tag). `BlogTagController` ([HttpGet `/blog/tag/{tag}`]) renderiza
`Views/Shared/BlogTag.cshtml` reutilizando el post-card canónico
(`.syn-post-category*`). Sin DocType (ADR 0008).

## Consequences

**Positivas:**
- Comentarios con conversación real (replies) + señal social (likes) sin DB:
  sigue siendo un JSON por nodo, swappable por adapter (ADR 0038).
- Backward-compat garantizada por defaults del record: cero migración, cero
  riesgo sobre los JSON existentes.
- Experiencia de post moderna (TOC navegable, progreso, share, tags vivos)
  como progressive enhancement: sin JS el artículo sigue legible y publicable.
- Tag page sin schema: una ruta virtual, single source of truth con
  PostCategoryPage/BlogRss vía `IBlogQuery`.
- Seams nuevos llegan con tests (ADR 0075): `FileSystemCommentRepositoryTests`
  (empty/happy/anidado/like/backward-compat/idempotente) + `BlogTagControllerTests`.

**Negativas o trade-offs:**
- Likes sin dedupe: un usuario puede sumar varios. Aceptado (reacción ligera);
  si se necesita 1-por-identidad, se agrega tracking en una ola futura.
- Anidación capada a 2 niveles: responder a una respuesta sube al top-level
  (no hay hilos profundos). Decisión de producto, no limitación técnica.
- `Comment.Id` es hex `"N"` (string) y `ParentId` es `Guid?`: el render y el
  repo parsean con `Guid.TryParseExact(id, "N")`. Consistente pero hay que
  recordar el formato al comparar.
- El reply-toggle requiere JS para revelar el form anidado; sin JS, sólo el
  form top-level del pie funciona (degradación aceptable).

**Notas de implementación:**
- **Recompila C#** (cambio de record + controller + repo + DevContentFiller que
  ahora inyecta `ICommentRepository`). El arquitecto recompila el server.
- **uSync Import** requerido para 5 Dictionary keys nuevas (`Blog.OnThisPage`,
  `Blog.ReadingProgress`, `Blog.Tag`, `Blog.Article`, `Blog.Articles`) — GUIDs
  frescos verificados. Sin DocType/DataType nuevos.
- **Re-seed opcional** (`POST /dev/fill-synergos-pages`): siembra un hilo de
  comentarios de demo (anidado + likes) y un cuerpo de artículo rico
  (pull-quote + callout + code block) en el post de arquitectura.
