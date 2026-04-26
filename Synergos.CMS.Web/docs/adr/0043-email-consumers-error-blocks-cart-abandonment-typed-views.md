# ADR 0043 — Email consumers wired (password reset + form notifications) + Error pages BlockGrid + Cart abandonment tracker + Typed views progress (Olas 79-81 + 75.2)

- **Status:** Accepted
- **Date:** 2026-04-26
- **Deciders:** Arquitecto + agente, durante batch tras Ola 72-77 — *"continua con todo"*.
- **Consolida:** 4 olas pequeñas-medianas en un único ADR por proximidad temática.

## Context

Tras el audit Lego (Ola 72) + el batch de mejoras 73-77 (errors + DropdownOptions
+ typed views first batch), quedaban deferred items concretos:

1. **Password reset por email** (deferred ADR 0034) — el seam y views
   estaban listos pero faltaba el wire al `IEmailService` (Ola 65).
2. **Form notifications** (deferred ADR 0030) — submissions persistidas
   pero el operador no recibía notificación.
3. **Error pages con BlockGrid** (deferred ADR 0042) — `transversalErrorPage`
   solo soportaba TinyMCE simple para body.
4. **Cart abandonment tracking** (deferred ADR 0037) — el analytics
   tracker prometía `cart.abandoned` events pero nadie los emitía.
5. **Typed views adoption** — continuar refactor post-Ola 71.4
   (ModelsBuilder SourceCodeAuto).

## Decision

Ejecutar las 4 olas en secuencia, cada una cierra un deferido específico.

### Ola 80 — Email consumers wired (3 commits)

**Password reset flow** (`479e8c6` `014f582`):
- `IMemberAuthService` extends:
  - `RequestPasswordResetAsync(email, ct) → PasswordResetRequestResult`
    (anti-enumeration: devuelve OK si email no existe sin error).
  - `ConfirmPasswordResetAsync(email, token, newPassword, ct) →
    MemberAuthResult` (mensaje genérico para errores de token).
- `DefaultMemberAuthService` usa `_memberManager.GeneratePasswordResetTokenAsync`
  + `ResetPasswordAsync` de Umbraco. Token TTL gobernado por security
  stamp policy de Umbraco (default 1h). `MapIdentityCode` mapea
  `InvalidToken`/`InvalidUserToken` → `"invalid-token"`.
- `AccountController` agrega 4 endpoints: GET/POST `/account/forgot-password`,
  GET/POST `/account/reset-password`. POSTs hacen PRG con feedback via
  querystring + analytics events `account.password-reset-requested/
  failed/completed`.
- 2 views nuevas: `ForgotPassword.cshtml` + `ResetPassword.cshtml`
  (Layout=null, meta robots noindex, dictionary keys con fallback inline).
- Email HTML simple via `IEmailService.SendAsync` con link absoluto
  `{scheme}://{host}/account/reset-password?email=X&token=Y`. KISS
  string concatenation con `HtmlEncode` — sin templating engine.

**Form notifications** (`668f8f7`):
- `FormsSettings.NotifyEmailAddress` (NEW, default vacío — opt-in).
- `FormSubmissionsController.Submit` tras success persistence + analytics
  llama `SendNotificationAsync` (si NotifyEmailAddress poblada) con tabla
  HTML de fields HtmlEncoded + metadata (formKey, referrer, clientIp,
  timestamp UTC).
- Try-catch defense-in-depth: SMTP failure NO rompe el pipeline; log
  Warning + continue.

### Ola 79 — Error pages con BlockGrid (1 commit `b9ffc65`)

- `transversalErrorPage` schema += `errorBlocks` (BlockGrid Sections,
  reusa `DTBlockGridSections`, Culture, opcional, GUID `92d1f072`).
- `errorBody` Description aclarada: "Si llenas Cuerpo (Layout Composer),
  ese tendrá prioridad sobre este texto."
- `ErrorController.ErrorPageViewModel` ahora incluye
  `BlockGridModel? BodyBlocks`.
- `Views/Error.cshtml` render precedence:
  1. Si `BodyBlocks` tiene contenido → `Html.GetBlockGridHtmlAsync` (Layout Composer).
  2. Else si `BodyHtml` → TinyMCE rendered.
  3. Else nada (solo título + status code).
- CSS modifiers `syn-error__body--blocks` vs `syn-error__body--text`
  para que design-system distinga.

UX editor: 404 simple llena `errorBody` (TinyMCE) y suficiente. 404
brand-rich llena `errorBlocks` con hero + ilustración + CTA prominente.

### Ola 81 — Cart abandonment tracking (1 commit `bd32003`)

- **`ICartAbandonmentTracker`** seam (Synergos.CMS.Interfaces): `MarkActivity` /
  `MarkCompleted` / `DetectAbandoned`. Records: `AbandonedCart` (CartId,
  ItemCount, Subtotal, Currency, LastActivityUtc).
- **`CartAbandonmentSettings`** POCO (Synergos.CMS.Application):
  `Enabled` (default true), `AbandonmentThreshold` (2h), `ScanInterval`
  (15min), `MinSubtotalToReport` (0).
- **`InMemoryCartAbandonmentTracker`** (Singleton): ConcurrentDictionary
  `cartId → ActivityRecord` con `Reported` flag para no duplicar eventos.
  Documentado upgrade-path a Redis/SQL para multi-instancia.
- **`CartAbandonmentScannerHostedService`** BackgroundService: cada
  `ScanInterval` invoca `DetectAbandoned`, emite `cart.abandoned`
  events via `IAnalyticsTracker` con `cartId/itemCount/subtotal/
  currency/minutesSinceActivity`. Filtra `subtotal < MinSubtotalToReport`.
  Delay inicial 30s post-boot.
- **`DefaultCartService`** hooks:
  - Constructor inyecta `ICartAbandonmentTracker`.
  - `ResolveCartId` helper: extrae primer 16 chars del HMAC base64 de
    la cookie firmada (anon-friendly identifier).
  - `TrackActivity` helper: notifica ItemCount/Subtotal/Currency
    post-mutación.
  - `AddItem`/`UpdateQuantity`/`RemoveItem` ahora invocan
    `TrackActivity(hydrated)` antes de return.
  - `Clear`: `MarkCompleted` (assume checkout) — el cart NO se
    reporta como abandoned aunque el threshold expire.
- Wire composers: `OptionsComposer` bind `Synergos:CartAbandonment` +
  `SeamComposer` AddSingleton + AddHostedService.

Operador downstream consume `cart.abandoned` events del log
estructurado y dispara recovery emails / retargeting / etc. — el
tracker no envía emails directamente (separation of concerns).

### Ola 75.2 — PostPage typed view (1 commit `e84a57e`)

- `@inherits UmbracoViewPage<Synergos.CMS.Web.PublishedModels.PostPage>`
- `Model.Excerpt`, `Model.HeroImage`, `Model.Sections`, `Model.Parent
  as PostCategoryPage` (typed cast) reemplazan `post.Value<T>("alias")`.
- `publishDate`/`readTimeMinutes` en el generated model son `string`
  (raw cookie value, no parsed) — `DateTime.TryParse` + `int.TryParse`
  recupera el typing del original.
- `heroImage` cambio de `IPublishedContent` a `MediaWithCrops` (typed) —
  `.Url()` funciona idéntico en ambos.

## Consequences

**Positivas:**

- **Password reset funcional end-to-end**: visitante click "olvidé mi
  contraseña" → email con link → click link → form de nueva contraseña →
  login con la nueva. Sin tocar código adicional. Anti-enumeration
  preservado.
- **Form notifications opt-in trivial**: editor pone su email en
  `Synergos:CartAbandonment:NotifyEmailAddress` y empieza a recibir
  cada submission con tabla HTML formateada. Persistencia FileSystem
  sigue siendo la fuente de verdad — el email es notificación, no
  storage primario.
- **Error pages branded**: 404/500 ya no son páginas planas — el
  editor diseña la experiencia con todo el power del Layout Composer
  (148 blocks + 14 layout presets disponibles).
- **Cart abandonment señal medible**: el operador conecta su sink de
  logs (Elastic/AppInsights) a triggers de marketing automation —
  recovery email o retargeting sin que el CMS los envíe directamente.
- **Typed views progressively adopted**: 3 views ya en typed
  (PlatformRoot, SearchPage, PostPage). Pattern probado para futuras
  refactores graduales.

**Negativas:**

- **Email plain HTML inline**: sin templating engine ni branding
  consistente. Mejora futura: `IEmailTemplateRenderer` (Razor templates
  compiled) — diferido KISS.
- **Cart abandonment in-memory**: estado se pierde con restart del
  proceso. No comparte multi-instancia. Mitigación documentada (Redis
  adapter) pero no implementada.
- **Typed views avanza gradualmente**: 12+ views legacy aún sin typed
  (PageBase usa `PageBaseResponse` DTO intermedio, _Layout usa `dynamic`,
  Account/* usa Layout=null sin Model). No gating — adopción cuando
  el dev toque la view por otra razón.
- **`MinSubtotalToReport=0` por default**: cualquier cart con 1+ items
  se reporta. Sites con muchos visitantes "exploradores" pueden
  necesitar subir el threshold para reducir ruido.

**Neutras:**

- 10 commits + ADR consolidado.
- 1 GUID nuevo (`errorBlocks` prop). Verificación cuádruple OK.
- 0 nuevos paquetes NuGet.
- 0 breaking changes en consumers existentes.

## Implementation summary

| # | Hash | Foco |
|---|---|---|
| 80.1 | `479e8c6` | `IMemberAuthService` extends RequestPasswordResetAsync + ConfirmPasswordResetAsync + DefaultMemberAuthService impl |
| 80.2 | `014f582` | AccountController forgot-password + reset-password endpoints + 2 views |
| 80.3 | `668f8f7` | FormSubmissionsController email notification + FormsSettings.NotifyEmailAddress |
| 79 | `b9ffc65` | transversalErrorPage += errorBlocks BlockGrid + Error.cshtml renderiza Layout Composer |
| 81 | `bd32003` | ICartAbandonmentTracker + scanner BackgroundService + DefaultCartService hooks (3 layers) |
| 75.2 | `e84a57e` | PostPage view a typed UmbracoViewPage<PostPage> |
| 0043 | (este) | ADR consolidado |

## Próximas direcciones

- **Ola 75.3+**: continuar typed views — ProductPage, PostCategoryPage,
  ProductCategoryPage, FlowDefinition/Step, Error (gradual).
- **Ola 78** (deferred): backoffice section custom AngularJS para gestionar
  transversales — listado flat con quick actions.
- **Ola 82**: `IEmailTemplateRenderer` Razor-compiled — emails con branding
  consistente, no string concat.
- **Ola 83**: email confirmation post-registro (`MembersSettings.RequireEmailConfirmation`)
  — el flujo es análogo al password reset pero al `RegisterAsync` y
  con `ConfirmEmailAsync` en lugar de `ResetPasswordAsync`.
- **Ola 84**: cart abandonment Redis adapter para multi-instancia.

## References

- ADR 0030 — Forms internal submission (form notifications deferido cerrado)
- ADR 0034 — Member self-service (password reset deferido cerrado)
- ADR 0035 — Email transactional runtime (consumer added)
- ADR 0037 — Analytics tracker (cart.abandoned event consumer added)
- ADR 0040 — Gran Consolidación (typed views ModelsBuilder setup)
- ADR 0042 — Error pages + DropdownOptions + Typed views (errorBlocks
  deferido cerrado)
