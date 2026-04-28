# Architecture Decision Records

An ADR is a short, immutable document that captures **one** architectural
decision, its context, and its consequences. We write one every time we
choose between options that a future reader would otherwise second-guess.

## Index

| # | Title | Status |
|---|-------|--------|
| [0001](0001-umbraco-13-lts-pin.md) | Umbraco 13 LTS pin | Accepted |
| [0002](0002-multi-project-solution.md) | Multi-project solution structure | Accepted |
| [0003](0003-sqlite-dev-database.md) | SQLite for development database | Accepted |
| [0004](0004-central-package-management.md) | Central Package Management (CPM) | Accepted |
| [0005](0005-composers-centralized.md) | Composers live only in the Web project | Accepted |
| [0006](0006-documentation-first-governance.md) | Documentation-first governance | Accepted |
| [0007](0007-xunit-test-framework.md) | xUnit as the test framework | Accepted |
| [0008](0008-usync-hybrid-source-of-truth.md) | uSync hybrid source-of-truth | Accepted |
| [0009](0009-extension-seams-mandatory.md) | Extension seams are mandatory | Accepted |
| [0010](0010-branding-via-provider.md) | Branding via provider, no conditional branching | Accepted |
| [0011](0011-feature-flags-typed-config.md) | Feature flags via typed config | Accepted |
| [0012](0012-cdn-contract-consumed.md) | CDN contract is consumed, not owned | Accepted |
| [0013](0013-no-automatic-seeders.md) | No automatic seeders; dev tooling behind flag | Accepted |
| [0014](0014-document-type-page-basic.md) | Document Type `PageBasic` (first product case, static pages) | Accepted |
| [0015](0015-synhost-framework-agnostic-integration.md) | SynHost framework-agnostic integration (CDN↔CMS) | Accepted |
| [0017](0017-layout-system-dropdown-compositions.md) | Layout system per-block compositions con dropdowns | Accepted |
| [0018](0018-forms-dual-path.md) | Forms dual-path (custom SSR + iframe bridge) | Accepted |
| [0019](0019-navigation-flat-groups.md) | Navigation flat groups, no recursion (SSR + a11y first) | Accepted |
| [0020](0020-platform-settings-split.md) | Platform/Settings tree separado + multi-brand via compBranding | Accepted |
| [0021](0021-datatype-semantics-by-intent.md) | DataType semantics: one type per editorial intent | Accepted |
| [0022](0022-page-composition-standard.md) | Page Composition Standard (Standard/Canvas/Bare/Landing + orchestration cascade) | Accepted |
| [0023](0023-componentization-layered-architecture.md) | Componentization Layered Architecture (5 capas + global components pattern) | Accepted |
| [0024](0024-page-minimal-and-editor-facing-descriptions.md) | Pages mínimas + descripciones editor-facing (refinamiento Ola 51) | Accepted |
| [0025](0025-global-components-extension-and-members-runtime.md) | Global components extension (cfgBanner/FooterNote/Modal) + Members runtime (Ola 52) | Accepted |
| [0026](0026-brand-runtime-completion-and-head-enrichment.md) | Brand runtime completion (HostBasedBrandingProvider) + `<head>` enrichment (Twitter Card + JSON-LD + hreflang) | Accepted |
| [0027](0027-blog-runtime-and-members-settings.md) | Blog runtime (IBlogQuery + PostPage/PostCategoryPage templates) + Members settings (LoginPath configurable) | Accepted |
| [0028](0028-shop-runtime-cart-and-query.md) | Shop runtime (ICartService cookie HMAC + IShopQuery + ProductPage/ProductCategoryPage templates + ShopController) | Accepted |
| [0029](0029-flow-templates-closure.md) | Flow templates closure (FlowDefinition + FlowStep templates + DefaultTemplate asignado) | Accepted |
| [0030](0030-forms-internal-submission-runtime.md) | Forms internal submission runtime (IFormSubmissionHandler + FileSystem default + FormSubmissionsController + honeypot + rate limit) | Accepted |
| [0031](0031-search-infrastructure-examine.md) | Search infrastructure on Examine ExternalIndex (ISearchQuery + ExamineSearchProvider + SearchController) | Accepted |
| [0032](0032-search-page-ux.md) | Search UX (searchPage DocType + SearchPage.cshtml + 5 dictionary keys + siteRoot Structure fix) | Accepted |
| [0033](0033-seo-infrastructure-sitemap-robots-rss.md) | SEO infrastructure (sitemap.xml + robots.txt + blog/rss.xml dynamic controllers) | Accepted |
| [0034](0034-member-self-service-runtime.md) | Member self-service runtime (IMemberAuthService + DefaultMemberAuthService + AccountController + Login/Register/Profile views) | Accepted |
| [0035](0035-email-transactional-runtime.md) | Email transactional runtime (IEmailService seam + DefaultEmailService adapter sobre Umbraco IEmailSender) | Accepted |
| [0036](0036-output-caching-sitemap-rss.md) | Output caching via IMemoryCache para Sitemap + Blog RSS (OutputCacheSettings POCO + multi-brand keys + bypass flag) | Accepted |
| [0037](0037-analytics-tracker-instrumentation.md) | Analytics tracker + instrumentación de 4 módulos (IAnalyticsTracker + LoggerAnalyticsTracker + 11 evento slugs en Search/Forms/Shop/Account) | Accepted |
| [0038](0038-comments-runtime-end-to-end.md) | Comments runtime end-to-end (ICommentRepository + FileSystemCommentRepository + CommentsController + elementCommentThread schema + renderer + reuso rate-limit) | Accepted |
| [0039](0039-site-chrome-editable-and-per-site-config.md) | Site Chrome editable + PlatformRoot landing + per-site Configuration folder (compSiteChrome 2 BlockGrid slots + PlatformRoot template + siteConfigFolder UX) | Accepted |
| [0040](0040-architectural-consolidation-theme-chrome-config-transversals-brand.md) | Gran Consolidación Arquitectónica: theme inheritance pura (siteRoot only) + chrome triádico (header/footer/aside) + siteConfiguration unificado + compTransversalSelectors drop-down + brand inheritance pura + ModelsBuilder SourceCodeAuto setup | Accepted |
| [0041](0041-lego-canonical-map-and-coupling-audit.md) | Mapa Lego canónico + auditoría de acoplamientos (30 compositions verified, DTSelect overlaps non-duplicate, pageBasic vs pageBare clarified, bug crítico compBranding fix, "regla del Lego ensamblable" 5 puntos) | Accepted |
| [0042](0042-error-pages-typed-views-dropdown-consts-misc-cleanup.md) | Error pages transversales (transversalErrorPage + ErrorController + UseStatusCodePagesWithReExecute) + DropdownOptions const class (13 DTSelect mirrors) + Typed views first batch (PlatformRoot/SearchPage) + SSR audit verbal + ICompositionReader audit | Accepted |
| [0043](0043-email-consumers-error-blocks-cart-abandonment-typed-views.md) | Email consumers wired (password reset + form notifications) + Error pages BlockGrid + Cart abandonment tracker + Typed views progress (PostPage) | Accepted |
| [0044](0044-email-templates-confirmation-typed-views-progress.md) | IEmailTemplateRenderer Razor-compiled + Email confirmation post-registro flow + Typed views progress (PostCategoryPage/ProductCategoryPage/FlowDefinition/FlowStep) | Accepted |
| [0045](0045-resend-confirmation-brand-emails-search-analytics-comments-moderation.md) | Resend email confirmation endpoint + brand-aware email SiteName via IBrandingProvider + Search analytics store + endpoint + Comments moderation (ICommentRepository extends approve/reject + CommentsModerationController role-gated) | Accepted |
| [0046](0046-search-analytics-gate-brand-email-subjects-comment-moderation-notifier.md) | Search analytics role gate (SearchSettings.AnalyticsAdminRolesCsv) + brand-aware email subjects (3 call sites) + ICommentModerationNotifier seam con email default + hook fire-and-forget en CommentsController | Accepted |
| [0047](0047-composite-notifier-pattern-comments-and-forms.md) | Composite + Channel notifier pattern para Comments y Forms (ICommentModerationNotifierChannel + WebhookCommentModerationNotifier + IFormSubmissionNotifier seam + IFormSubmissionNotifierChannel + Email/Webhook channels + composites + slim FormSubmissionsController) | Accepted |
| [0048](0048-css-design-system-aligned-with-synergos-ui.md) | CSS design system aligned with Synergos.UI — 13 archivos modulares (tokens/base/utilities/layout/chrome/primitives + per-area pages/shop/search/comments/globals/flow/member/blog/account/error) ~4400 líneas vanilla, 3 themes light/dark/silverGold, aliases legacy preserved, _AccountHead partial para 8 views Layout=null | Accepted |
| [0049](0049-cleanup-manrope-webhook-hmac-cart-notifier.md) | IDE0005 cleanup (26 archivos) + Error.cshtml @inject UmbracoHelper + Manrope font wire (3 entry points) + WebhookSigner HMAC-SHA256 helper + HMAC en 2 webhook channels existentes + Cart abandonment notifier (composite + email + webhook channels + email template + scanner hook) | Accepted |
| [0050](0050-slack-channels-and-webhook-replay-protection.md) | Slack-shaped notifier channels (Comments/Forms/Cart con SlackWebhookSender helper + Block Kit payloads + 3 SlackWebhookUrl settings) + Webhook replay protection canónica (X-Synergos-Timestamp header + HMAC sobre "{ts}.{body}" tipo Stripe/GitHub) | Accepted |
| [0051](0051-admin-moderation-dashboard-ssr.md) | Admin moderation dashboard SSR — AdminController member-gated (admin/moderator/editor) en /admin/moderation/comments con list + approve/reject inline forms PRG, alternativa simpler al backoffice section AngularJS deferido (Ola 78). Partial _AdminHead + syn-admin.css alineados con design system | Accepted |
| [0052](0052-admin-extensions-discord-teams-polly.md) | Admin extensions (search analytics dashboard + moderation pagination/filter/bulk) + Discord notifier channels (embeds) + Teams notifier channels (MessageCard) + Polly retry via Microsoft.Extensions.Http.Resilience 8.10.0 en los 12 named HttpClients (3 webhook + 3 Slack + 3 Discord + 3 Teams) | Accepted |
| [0053](0053-admin-landing-form-submissions-confirm-dialog.md) | Admin landing /admin con summary cards + Form submissions dashboard /admin/forms (IFormSubmissionReader seam read-only + FileSystem handler implementa ambas) + native HTML5 <dialog> confirm modal para bulk approve/reject + ghost button variant + topbar nav 4 entries con current state highlight | Accepted |
| [0054](0054-form-detail-topbar-partial-csv-export.md) | Form submission detail drill-down (IFormSubmissionReader.GetSubmission + FormSubmissionDetail record + StorageId URL-safe) + _AdminTopbar partial reusable + pending counter badge red→brand + CSV export con union de columnas + BOM UTF-8 + EscapeCsvField helper | Accepted |
| [0055](0055-date-range-cache-delete-spam.md) | Date range filter en CSV export + listing (IFormSubmissionReader.GetRecent extends fromUtc/toUtc) + pending counter IMemoryCache (TTL 30s + invalidation post-action) + IFormSubmissionReader.DeleteAsync + AdminController.DeleteFormSubmission con confirm dialog + AdminController.MarkCommentAsSpam (variant del reject con analytics tag distinto) + .syn-admin__action--spam + .syn-admin__panel--danger | Accepted |
| [0056](0056-admin-settings-streaming-undo.md) | AdminSettings POCO (PendingCountCacheTtl + DefaultPageSize + CsvExportHardCap + BulkUndoWindow) wireado en OptionsComposer + native HTML5 <dialog> en delete submission + CSV streaming via Response.Body.WriteAsync (memoria O(1)) + soft-delete undo bulk-reject (ICommentRepository.ReadByRefs/RestoreAsync + BulkUndoReject action con cache token + flash button "↶ Deshacer" 30s window) | Accepted |
| [0057](0057-adaptive-cards-polly-per-channel.md) | Adaptive Cards 1.4 schema reemplaza MessageCard en 3 TeamsXxxNotifier (TextBlock + FactSet + semantic colors via host theme) + AdminSettings.WebhookResilience POCO (MaxRetryAttempts/AttemptTimeoutSeconds/TotalRequestTimeoutSeconds/RetryBaseDelayMs) aplicado a los 12 named HttpClients via AddStandardResilienceHandler(ConfigureWebhookResilience) | Accepted |
| [0058](0058-health-receiver-docs-webhook-harness.md) | AdminController.Health endpoint /admin/health con HealthReport (uptime/memoria/runtime/seam counts/settings) + docs/webhooks/receiver-sdk.md con snippets C#/Node/Python para HMAC verification + replay window + AdminController.WebhookTestHarness /admin/webhooks/test con 3 POST actions (TestCommentWebhook/TestFormWebhook/TestCartWebhook) que disparan payloads test al composite + topbar 2 entries nuevas (Webhooks + Health) | Accepted |
| [0059](0059-post-135-runtime-hotfixes.md) | Post-135 runtime hotfixes — Error.cshtml @inherits UmbracoViewPage<T> reemplaza @model + @inject IUmbracoHelper roto (CS0234 runtime) + 3 build warnings cerrados (CS1573 FormSubmissionListItem param tags / CA1068 SlackWebhookSender CancellationToken last / CA1870 SearchValues cache en EscapeCsvField) + LocalizationComposer pre-popula RequestLocalizationOptions con Languages de DB para neutralizar race upstream de Umbraco (DynamicRequestCultureProviderBase.TryAddLocked, PR #14064) | Accepted |
| [0060](0060-seo-maturity-jsonld-news-sitemap.md) | SEO maturity — _SeoStructuredData.cshtml emite Article (postPage) + Product (productPage) JSON-LD via dispatch por ContentType.Alias + _Breadcrumbs.cshtml emite BreadcrumbList JSON-LD junto con `<nav>` visual + NewsSitemapController `/news-sitemap.xml` Google News Protocol con ventana 48h via OutputCacheSettings.NewsSitemapWindowHours + RobotsController anuncia 2 sitemaps | Accepted |
| [0061](0061-i18n-admin-baseline.md) | i18n admin baseline — uSync Dictionary tree parent "Admin" + 31 children (32 GUIDs verificados) cubriendo topbar nav + action buttons + landing strings con traducciones es-CO+en-US + refactor de 6 admin views (`_AdminTopbar`, `Index`, `ModerationComments`, `FormSubmissions`, `FormSubmissionDetail`, `WebhookTestHarness`) consumiendo `@Umbraco.GetDictionaryValue("admin.X", "fallback ES")` con zero regresión visual antes del Import | Accepted |
| [0062](0062-healthz-public-probes.md) | /healthz public probes — HealthzController con 2 endpoints sin auth para k8s/LB: `GET /healthz` liveness siempre HTTP 200 + uptime; `GET /healthz/ready` readiness HTTP 200 si UmbracoContext+Content listos o 503 status="warming" durante boot. JSON shape `{ status, uptimeSeconds }` minimal sin tocar DB ni IO | Accepted |
| [0063](0063-member-roster-admin.md) | Member roster admin — IMemberRosterReader seam read-only + UmbracoMemberRosterReader sobre IMemberService.GetAll + IMemberGroupService + AdminController.Members /admin/members member-gated con paginación + roleFilter querystring + Members.cshtml view con table (Email/Nombre/Roles/Último login/Creado/Estado) + topbar entry "Miembros" + Dictionary admin.nav.members | Accepted |
| [0064](0064-hot-reload-webhook-resilience.md) | Hot-reload WebhookResilience — WebhookResilienceSettings via AddOptions().Bind() registra IConfigurationChangeTokenSource automático + WebhookResilienceExtensions.AddWebhookResilience() helper usa AddOptions<HttpStandardResilienceOptions>(pipelineName).Configure<IOptionsMonitor<WebhookResilienceSettings>>(...) refactor de 12 named HttpClients. Latencia ~2min al next handler rotation, sin restart | Accepted |
| [0065](0065-receiver-sdk-go-java.md) | Receiver SDK Go + Java — extiende docs/webhooks/receiver-sdk.md con snippets idiomáticos: Go usando crypto/hmac+crypto/subtle.ConstantTimeCompare en http.Handler + Java usando javax.crypto.Mac+SecretKeySpec+HexFormat (Java 17+) en Spring @PostMapping. Coverage ahora 5 lenguajes (C#/Node/Python/Go/Java) | Accepted |
| [0066](0066-receiver-sdk-php-ruby-sitemap-index.md) | Receiver SDK PHP+Ruby + sitemap-index — PHP (hash_hmac+hash_equals) y Ruby (OpenSSL::HMAC+fixed_length_secure_compare) elevan coverage a 7 lenguajes; SitemapIndexController nuevo en /sitemap-index.xml con sitemapindex root listando sitemap.xml + news-sitemap.xml entries; cache per-host con TTL SitemapMinutes | Accepted |
| [0067](0067-audit-trail-seam.md) | IAuditTrailWriter seam + audit log file-based — append-only en App_Data/syn-audit/{yyyy-MM-dd}.jsonl con AuditEvent record (Id/OccurredAtUtc/ActorEmail/ActorName/Action/Resource/Outcome/Detail) + AdminController.EmitAuditAsync helper instrumentando 6 actions moderadoras + form-delete + GET /admin/audit view con filter actor/action + topbar entry "Auditoría" + Dictionary admin.nav.audit + IMemberAccessGate.CurrentMemberEmail | Accepted |
| [0068](0068-member-roster-writer-lock-unlock.md) | Member roster writer (lock/unlock) — IMemberRosterWriter seam split del Reader por ISP con LockAsync+UnlockAsync idempotent reversibles + UmbracoMemberRosterWriter sobre IMemberService.GetByKey+Save mutating IsLockedOut (unlock resetea FailedPasswordAttempts) + AdminController POST actions /admin/members/{key}/lock|/unlock con audit emit member.lock|member.unlock + Members.cshtml columna "Acciones" con botón condicional 🔒 Bloquear / 🔓 Desbloquear | Accepted |
| [0069](0069-per-channel-resilience-tuning.md) | Per-channel WebhookResilience tuning — Rule of Three triggered por observación de perfiles distintos (Teams lentos, Discord rate-limited, Slack estable). WebhookResilienceSettings.PerChannel Dictionary<string,WebhookResilienceChannelOverride> indexado por FactoryName con todos campos nullable; WebhookResilienceExtensions captura builder.Name + consulta override runtime via IOptionsMonitor manteniendo hot-reload de ADR 0064 | Accepted |
| [0070](0070-resilience-validation-audit-retention.md) | Resilience config validation + audit retention sweep — WebhookResilienceSettingsValidator implementa IValidateOptions con set estático de 12 FactoryNames + warning per typo en PerChannel; AdminSettings.AuditRetentionDays (default 90) + AuditRetentionHostedService BackgroundService sweep cada 24h purgando files JSONL más viejos que cutoff (0 = retention infinito) | Accepted |
| [0071](0071-audit-date-range-csv-export.md) | Audit date range query + CSV streaming export — IAuditTrailWriter.GetByDateRange con file-level pre-filter + event-level fine filter; AdminController.Audit cae a date range cuando query string tiene from/to; AdminController.AuditExportCsv /admin/audit/export streaming via Response.Body.WriteAsync 8 columns con BOM UTF-8 + recursive audit.export event self-instrumentation | Accepted |
| [0072](0072-webhook-telemetry-store.md) | Webhook telemetry per-channel observability — IWebhookTelemetryStore seam con ChannelTelemetrySnapshot record (Total/Success/Failure counts + P50/P95/P99 latency); InMemoryWebhookTelemetryStore ring buffer 1000 samples por canal thread-safe via per-channel lock; WebhookTelemetryHandler DelegatingHandler wrappa cada outgoing request capturando elapsed total incluyendo retries; Health view nueva sección con tabla coloreada por fail rate | Accepted |
| [0073](0073-i18n-admin-extension.md) | i18n admin extension — +22 Dictionary keys (2 action lock/unlock + 6 status ok/locked/unconfirmed/success/failure/partial + 12 column headers Email/Name/Roles/LastLogin/Created/State/Actions/When/Actor/Action/Resource/Outcome + 2 pagination Previous/Next) refactorizando Members.cshtml + Audit.cshtml. Total admin Dictionary keys ahora 55 | Accepted |
| [0074](0074-cdn-contract-proposal-2fa-seam.md) | CDN contract proposal + IMemberTwoFactorService seam — extiende docs/umbraco/cdn-contract.md con sección "Proposal — defaults the CMS would accept" con valores concretos para los 5 puntos + settings shape Synergos:Cdn:* + IMemberTwoFactorService interface (Start/Confirm/Verify/Disable/IsEnabled) + records POCO (TwoFactorEnrollmentChallenge) + enums tipados (EnrollmentResult, VerificationResult) | Accepted |
| [0075](0075-tests-gate-revisitado.md) | Tests gate revisitado — supersedes implicit gate de CLAUDE.md principle #9 + memoria feedback_tests_after_full_migration. Discovery: Tests project ya tenía 83 tests pasando. +28 tests sobre seams nuevos (InMemoryWebhookTelemetryStore 6 + WebhookResilienceSettingsValidator 15 con Theory + FileSystemAuditTrailWriter 8). Total 111 tests passing. Cleanup colateral 5 IDE0005 redundant usings | Accepted |
| [0076](0076-2fa-totp-phase1.md) | 2FA Phase 1 — TOTP service + admin reset action — Otp.NET 1.4.1 verificado nuget.org + FileSystemMemberTwoFactorStore JSON file persistence en App_Data/syn-2fa/ + UmbracoMemberTwoFactorService impl con TOTP RFC 6238 + drift window ±1 step + AdminController.ResetMemberTwoFactor /admin/members/{key}/2fa-reset + Members.cshtml view button. Phase 2 deferred (recovery codes + encryption-at-rest + Member self-service enrollment + login flow extension) | Accepted |
| [0077](0077-members-destructive-crud.md) | Members destructive CRUD — IMemberRosterWriter extends DeleteAsync (hard, irreversible) + SendPasswordResetAsync (delega flow self-service ADR 0034/0044) + SetRolesAsync (replace-set diff Assign/Dissociate). UmbracoMemberRosterWriter inyecta IMemberAuthService+IEmailService+RazorEmailTemplateRenderer+IBrandingProvider+IHttpContextAccessor para email composition. AdminController 3 POST actions con audit emit + self-delete guard. Members.cshtml dialog confirm + role checkboxes sub-row. Threat model documentado en ADR | Accepted |
| [0078](0078-hardening-docs-wcag-backup-gdpr.md) | Hardening docs (WCAG audit + Backup/DR + GDPR RTBF) — 3 docs en docs/hardening/: wcag-audit.md WCAG 2.1 AA self-audit con 30+ criteria + 3 gaps identificados (contrast 4.2:1, link purpose, form aria-describedby) + backup-and-recovery.md persistence inventory 11 surfaces + RPO/RTO + recipes + restore procedure 9-step + recovery testing + gdpr-rtbf.md personal data inventory + audit immutability special case + manual procedure 8-step + automated proposal | Accepted |
| [0079](0079-wcag-re-audit-fixes-and-audit-drilldown.md) | WCAG re-audit fixes + audit drill-down + resilience strict mode — re-audit verificado contra código real (Gap 1 spec error fixed con safety bump neutral-500→neutral-600 = 7:1 contrast; Gap 2 spec moot, fixed Members.cshtml action buttons aria-label; Gap 3 spec no-op, ya cubierto). IAuditTrailWriter.GetById + AdminController.AuditDetail /admin/audit/{id} + nearby events ±5min + AuditDetail.cshtml. WebhookResilienceSettings.StrictValidation + validator branch fail si typo + 2 tests | Accepted |
| [0080](0080-webhook-telemetry-alerts.md) | Webhook telemetry alerts — WebhookTelemetryAlertSettings POCO opt-in (Enabled/CheckIntervalMinutes/FailRateThreshold/MinimumSampleSize/CooldownMinutes/AlertEmail) + WebhookTelemetryAlertHostedService BackgroundService que cada N minutos polléa GetChannelStats y dispara email HTML al operador cuando canal cruza threshold; cooldown via ConcurrentDictionary | Accepted |
| [0081](0081-2fa-phase-2a-recovery-codes-enrollment.md) | 2FA Phase 2.A — recovery codes + Member self-service enrollment — 8 codes × 8 chars alphabet sin ambigüedad visual, hashed PBKDF2 100k+SHA256+16-byte salt per code + plaintext shown UNA vez via ConsumeLastEnrollmentRecoveryCodes + VerifyAsync extends con recovery branch FixedTimeEquals + remove single-use. IMemberAccessGate.CurrentMemberKey. AccountController.TwoFactorSetup/Confirm + 2 views (TwoFactorSetup + TwoFactorSetupConfirmed). Phase 2.B deferred: login flow extension + encryption-at-rest + QR rendering | Accepted |
| [0082](0082-cap-210-refinement-tests-coverage.md) | Cap-210 refinement — 2FA Phase 2.B login flow shipped (ValidateCredentialsAsync + SignInByEmailAsync seams + AccountController.LoginPost rewrite + /account/2fa-challenge GET+POST + view + IMemoryCache pending tokens TTL 5min) cierra el feature flagship que estaba como decoración. Tests coverage gaps cap-200 cubiertos: audit GetById (5) + 2FA store (6) + recovery codes helper extraído (12) + composite notifiers fan-out+isolation (6) + form submission DeleteAsync con path traversal protection (6). Total tests 113 → 148 (+35) | Accepted |

## Rules

1. ADRs are **numbered sequentially**. Never reuse a number, even if an ADR
   is rejected.
2. ADRs are **immutable once accepted**. To change a decision, write a
   new ADR with a later number that supersedes the previous one, and
   update the status of the superseded one to `Superseded by ADR-XXXX`.
3. **Status** is one of: `Proposed`, `Accepted`, `Rejected`, `Superseded`,
   `Deprecated`.
4. Keep them **short**. One page or less is the target. If the context
   needs pages of background, it probably needs its own long-form doc in
   `architecture/` and the ADR just links to it.

## Template

Copy this into `NNNN-short-slug.md`:

```markdown
# ADR NNNN — <Short Title>

- **Status:** Proposed | Accepted | Rejected | Superseded by ADR-XXXX | Deprecated
- **Date:** YYYY-MM-DD
- **Deciders:** <names or roles>

## Context

What is the problem? What forces are at play (technical, organizational,
external)? Keep it factual — no opinions yet.

## Decision

The choice that was made. One or two sentences. No hedging.

## Consequences

What becomes easier, harder, or impossible because of this decision?
List both positive and negative consequences honestly. A future reader
uses this section to decide if the decision still applies.

## Alternatives considered

Brief. What else was on the table, and why it lost.
```

## When to write a new ADR

- Choosing between two technologies that could both work
- Removing or adding an abstraction layer
- Changing naming, folder, or dependency rules at the architecture level
- Pinning a version with a specific rationale (e.g. "stay on LTS")
- Any decision where "why is it like this?" will be asked more than once

## When NOT to write an ADR

- Picking a variable name
- Refactoring a single file
- Fixing a bug
- Adding a NuGet package for a feature (note it in `CHANGELOG.md` instead)
