# ADR 0038 — Comments runtime end-to-end: ICommentRepository + elementCommentThread (Ola 68)

- **Status:** Accepted
- **Date:** 2026-04-25
- **Deciders:** Arquitecto + agente, durante Ola 68
- **Habilita:** Engagement editorial sobre blog posts y otros nodos
  publicados sin coordinación con servicio externo (Disqus, Hyvor)
- **Related:** ADR 0030 (reusa InMemoryFormRateLimiter), ADR 0034
  (consume IMemberAccessGate), ADR 0037 (instrumentado vía
  IAnalyticsTracker)

## Context

Tras Olas 60-67 (Forms internal + Search + SEO + Member self-service +
Email + Output cache + Analytics), faltaba el módulo Engagement
editorial: comentarios sobre nodos. Sin Comments:

- Blogs deployados quedaban como one-way broadcast — sin canal de
  feedback público del visitante.
- La opción típica (Disqus/Hyvor embed) acopla a un servicio externo
  con su propia política de privacidad, tracking JS y costos.
- No había forma de capturar "señal de engagement" del visitante para
  el equipo editorial.

## Decision

Construir un módulo Comments self-contained: seam + filesystem default
+ controller + element editorial + renderer. Cero servicios externos,
cero NuGet nuevo, cero DB nueva.

### Seam (Ola 68.1)

**`Synergos.CMS.Interfaces/ICommentRepository.cs`**:

```csharp
IReadOnlyList<Comment> GetApprovedForNode(int nodeId);
Task<Comment> AddAsync(NewComment comment, CancellationToken cancellationToken);
```

Records:
- `Comment(Id, NodeId, MemberKey?, AuthorName, Body, CreatedAtUtc, Approved)`
- `NewComment(NodeId, MemberKey?, AuthorName, Body)` — repository
  asigna Id/CreatedAtUtc/Approved.

**`Synergos.CMS.Application/Configuration/CommentsSettings.cs`** POCO:
- `StorageRoot` (default `App_Data/syn-comments/`)
- `MaxBodyLengthChars` (2000)
- `RequireModeration` (default `false` — comentarios visibles
  inmediato; activar para sitios con valor alto)
- `MaxCommentsPerHourPerIp` (5) — defensa contra spam
- `RequireAuthentication` (default `true` — gate por
  `IMemberAccessGate`)

### Default impl (Ola 68.1)

**`FileSystemCommentRepository`** en `Web/Services/`:

- Persiste un JSON por nodo:
  `{ContentRoot}/{StorageRoot}/{nodeId}.json` con array completo de
  Comments del nodo.
- `GetApprovedForNode`: load + filter `Approved=true` + orden
  insertion (CreatedAtUtc ASC implícito).
- `AddAsync`: load + append + write atomic vía `WriteAllBytesAsync`.
  Trim body + cap a `MaxBodyLengthChars` + `Approved=!RequireModeration`.
- Catchea `IO`/`Json` en load y devuelve lista vacía + log Warning si
  archivo malformed.

Wire: `services.AddSingleton<ICommentRepository, FileSystemCommentRepository>()`.

### Controller (Ola 68.2)

**`CommentsController`** (`[Route("api/comments")]`,
`[Consumes("application/x-www-form-urlencoded")]`):

```
POST /api/comments/{nodeId}
```

Pipeline:
1. Resolve `referrer` (fallback `/`).
2. Si `RequireAuthentication=true` y `!_gate.IsAuthenticated` →
   redirect referrer + `#comment-error-not-authenticated`.
3. Valida `body` no vacío.
4. Rate limit reusando `InMemoryFormRateLimiter` (Ola 60) con key
   `"comments:{nodeId}"` — comparte la defensa-en-capas del módulo
   Forms.
5. Construye `authorName`: si autenticado usa
   `_gate.CurrentMemberDisplayName`; si anónimo usa el `authorName`
   del form (fallback "Anonymous").
6. `_repository.AddAsync(...)`.
7. Track `comment.added` con properties `nodeId, approved, bodyLength`.
8. Redirect referrer + anchor `#comment-{id}` (si Approved) o
   `#comment-pending` (si moderación activa).

### Schema editorial (Ola 68.2)

**`elementCommentThread`** Element Type
(`Folder=Blocks/Engagement`, GUID
`cecbd643-72ea-4b82-b695-1ff61ad1e699`):

- Compone los 4 `compDom*` estándar.
- 2 props (Culture TextBox opcionales con fallback a dictionary):
  - `heading` — fallback `Comments.Heading`
  - `placeholder` — fallback `Comments.Placeholder`
- `IsElement=true`. Sin `DefaultTemplate`. Embebible vía Layout
  Composer en cualquier page.

### Renderer (Ola 68.2)

**`Views/Partials/Elements/Engagement/CommentThread.cshtml`**:

- Inyecta `ICommentRepository` + `IMemberAccessGate` +
  `IOptions<CommentsSettings>` + `IUmbracoContextAccessor`.
- Resuelve `nodeId` del `UmbracoContext.PublishedRequest.PublishedContent`
  (block "sabe" en qué page está embebido — sin requerir prop
  `nodeId` en el editor).
- Lista `<ol>` ordenada por `CreatedAtUtc` ASC con `id="comment-{guid}"`
  por item (anchor del redirect).
- Form POST nativo a `/api/comments/{nodeId}` con `maxlength` +
  `autocomplete` + `aria-required`.
- `canPost = !RequireAuthentication || IsAuthenticated`. Si
  `!canPost`, reemplaza form por link a `/account/login` con
  `returnUrl` preserved.
- Wrapper en `blockgrid/Components/elementCommentThread.cshtml`
  delega al partial — convención estándar de blocks.

## Consequences

**Positivas:**

- **Engagement self-contained**: blog deploys reciben comentarios
  funcionales sin dependencia de Disqus/Hyvor. Sin tracking JS de
  terceros, sin costos recurrentes, sin política de privacidad
  externa.
- **Auth opcional pero por default seguro**:
  `RequireAuthentication=true` por default — los comentarios son
  trazables vía MemberKey + DisplayName. El operador puede flippear
  a false para sitios públicos abiertos.
- **Moderación opt-in**: `RequireModeration=true` activa workflow
  de aprobación. Comments quedan persistidos pero invisibles hasta
  que un moderator edita el JSON manualmente o un adapter custom
  implementa workflow.
- **Reusa rate-limit infrastructure**: la `InMemoryFormRateLimiter`
  de Ola 60 funciona idéntico aquí — 5 comments/hora/IP por default.
  Cero código duplicado.
- **Analytics integrado**: `comment.added` y `comment.rate-limited`
  fluyen al `IAnalyticsTracker` (Ola 67) — operador ve volumen +
  picos sin instrumentar nada extra.
- **PRG + anchor**: redirect post-submit a `#comment-{id}` lleva al
  visitante directo a su comentario recién publicado en el thread.
  UX cuidada sin JS.
- **Embebible en cualquier page**: el block es composable vía Layout
  Composer en postPage / pageBasic / cualquier nodo. No exclusivo a
  blog.

**Negativas:**

- **Sin nested replies**: el thread es flat (lista plana). Para
  comentarios anidados, agregar `ParentCommentId` al record + render
  recursivo. Diferido — la mayoría de blogs editoriales no necesitan
  threading.
- **Sin edit/delete por el autor**: una vez creado, el comment es
  inmutable. Para casos editables (corregir typo), agregar
  `UpdateAsync` al seam + endpoint controlador. Diferido.
- **Sin email notification al autor del post**: cuando alguien
  comenta, el autor del post no recibe email. Trivial agregar con
  `IEmailService` (Ola 65) + lookup de email del autor desde
  IPublishedContent. Diferido.
- **FileSystem no escala a > 1000 comments/nodo**: load + write
  full-array por op. Para hilos virales, swap por adapter sobre DB
  (SQLite, Postgres). El seam no cambia.
- **Concurrent writes pueden race**: dos comments al mismo nodo en
  el mismo instante pueden tener uno sobreescribir al otro. Para
  volumen real, lock per-node o adapter sobre DB. Aceptable para
  blog editorial (1-10 comments/post típico).
- **3 dictionary keys hardcoded inline (fallbacks en es-CO)**: las
  traducciones funcionan vía fallback string del Razor. Crear los
  XML uSync para internacionalizar es trivial pero diferido —
  consistente con decisión de Ola 64 (Account views).
- **Sin honeypot anti-bot**: el form no incluye campo hidden
  honeypot (a diferencia de Forms Ola 60). Trivial agregar si los
  sitios reales detectan bot abuse. Hoy `RequireAuthentication=true`
  por default es la primera línea de defensa.

**Neutras:**

- 3 GUIDs nuevos (1 ContentType + 2 props). Verificación cuádruple
  OK.
- 1 seam + 1 default impl + 1 controller + 1 schema + 1 wrapper +
  1 partial = 6 archivos código nuevos.
- Cero nuevos paquetes NuGet.
- Reusa `InMemoryFormRateLimiter` ya registrado en Singleton — el
  segundo consumidor del rate limiter (después de FormSubmissionsController).

## Alternatives considered

- **Disqus/Hyvor embed**. Descartado por dependencia externa,
  privacy + costo. Si el cliente lo justifica, el block
  `elementCommentThread` puede ser swappeado por `elementCommentEmbed`
  (iframe) — patrón análogo a `elementFormEmbed` de ADR 0018.
- **DB nueva (SQLite local + EF Core)**. Premature. Filesystem cubre
  el 95% de blogs editoriales. Adapter swap es trivial cuando
  justifique.
- **Threading nested desde el primer pase**. Complejidad UI alta sin
  demanda clara — la mayoría de comentarios editoriales no hilan.
  Diferido.
- **WYSIWYG / Markdown en el body**. Descartado en primer pase —
  plain text es más seguro (no XSS) y editorial-friendly. Si
  justifica, agregar opt-in via prop `allowFormatting` con sanitizer.
- **Notifications email automáticas al autor del post**. Diferido.
  Habilitable trivialmente con `IEmailService` (Ola 65) cuando se
  prioritice.

## Implementation summary (Ola 68, 3 commits)

| Commit | Hash | Foco |
|---|---|---|
| `feat(ola-68.1)` | `454e170` | `ICommentRepository` seam + Comment/NewComment records + `FileSystemCommentRepository` default + `CommentsSettings` POCO + wire |
| `feat(ola-68.2)` | `c4ed8ce` | `CommentsController` POST `/api/comments/{nodeId}` + `elementCommentThread` schema + wrapper + renderer (analytics + rate-limit reuso) |
| `docs(ola-68.3)` | (este) | ADR 0038 + index README |

## References

- ADR 0009 — Extension seams (`ICommentRepository` sigue el patrón)
- ADR 0027 — Blog runtime (postPage es el host primario del block)
- ADR 0030 — Forms internal (rate-limiter reuso)
- ADR 0034 — Member self-service (gate por `IMemberAccessGate` +
  redirect a `/account/login`)
- ADR 0037 — Analytics tracker (`comment.added`,
  `comment.rate-limited` events)
