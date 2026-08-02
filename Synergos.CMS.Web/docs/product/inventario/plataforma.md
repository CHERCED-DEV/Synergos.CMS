# Dominio: Plataforma transversal

## Resumen ejecutivo

La plataforma transversal de Synergos es, en su núcleo, **más real que "demo"**: identidad de members (registro/login/perfil/2FA TOTP real con Otp.NET), auditoría append-only, GDPR RTBF, PHI cifrado at-rest, y una consola `/admin` SSR de 8 secciones con acciones POST reales (no botones decorativos) están **VIVOS** — persisten en disco, se leen de vuelta, y tienen tests unitarios dedicados. La pieza más floja es el dato agregado de negocio (`IDashboardReadModel`): es real (deriva de checkouts/metrics capturados, no de un seed), pero su panel `IAgentInsights` declara explícitamente `Available:false` porque la capa de IA (ADR 0095) no existe.

El almacenamiento es honestamente lo que aparenta: **~20 superficies de ficheros bajo `App_Data/`**, todas detrás de un único patrón (`IJsonEntityStore` genérico + 5 stores dedicados con necesidades especiales: audit JSONL, comments, forms, 2FA cifrado, PHI cifrado, private files cifrados). Escritura atómica (`temp + File.Move`) en casi todos, así que sobreviven un reinicio del proceso — **excepto** `ISearchAnalyticsStore`, que es puramente `ConcurrentDictionary` en memoria pese a que existe una `SearchAnalyticsRetentionPolicy` que barre un directorio (`syn-search-analytics/`) que **ningún escritor real puebla jamás** — es retención muerta sobre un store que no persiste.

Retención está resuelta para 6 de las ~20 superficies (audit, comments-rejected, forms, search-analytics [muerta], dashboard-orders, healthcare-PHI). Las ~14 restantes — incluidos `syn-orders`, `syn-payments`, `syn-reservations`, `syn-travel-orders`, `syn-enrollments`, `syn-gov-cases`, `syn-event-orders`, `syn-files` (documentos privados cifrados) y los dos ledgers de idempotencia (`syn-payment-events`, `syn-notifications`) — **crecen indefinidamente sin política de purga**, un fichero por entidad/marcador para siempre.

La consola de administración (`/admin/*`) es SSR puro (sin Umbraco backoffice), gateada por rol `admin,moderator,editor` verificado **manualmente en cada action** (no hay filtro/atributo central — 27 repeticiones de `if (!_gate.HasAnyRole(...))` en `AdminController.cs`), y cubre moderación de comentarios, forms, miembros (lock/unlock/roles/2FA-reset/GDPR-erase/password-reset), analítica de búsqueda, test harness de webhooks, auditoría (listado+detalle+export CSV) y health. No hay tests de controller para `AdminController` (1144 líneas) pese a ser la superficie operativa más grande del proyecto.

## Capacidades

### Identidad de members + 2FA
- **Madurez**: VIVO
- **Seams**: `IMemberAuthService`, `IMemberAccessGate`, `IMemberRosterReader`/`Writer`, `IMemberTwoFactorService`, `IGdprRtbfCoordinator` (`Synergos.CMS.Interfaces`)
- **Implementación**: `DefaultMemberAuthService` envuelve `IMemberManager`/`IMemberSignInManager` reales de Umbraco (`Synergos.CMS.Web/Composers/SeamComposer.cs:517`). `UmbracoMemberTwoFactorService` usa Otp.NET real (RFC 6238 TOTP, ±1 step drift) + recovery codes PBKDF2 (`Synergos.CMS.Web/Services/UmbracoMemberTwoFactorService.cs`). `UmbracoMemberRosterReader`/`Writer` envuelven `IMemberService`/`IMemberGroupService` reales.
- **Persistencia**: Members en la DB nativa de Umbraco (SQLite/SQL Server). 2FA secrets en `App_Data/syn-2fa/{memberKey}.json`, cifrado con `IDataProtector` (purpose `Synergos.MemberTwoFactor.v1`), con fallback de lectura a legado plaintext (`FileSystemMemberTwoFactorStore.cs:60-78`).
- **Superficie HTTP**: `GET/POST /account/login`, `/account/2fa-challenge`, `/account/register`, `/account/2fa-setup`, `/account/profile`, `/account/forgot-password`, `/account/reset-password` (anónimo); `POST /admin/members/{key}/lock|unlock|2fa-reset|delete|password-reset|roles|gdpr-erase` (rol admin/moderator/editor, `AdminController.cs:942-1143`).
- **Schema CMS**: Member DocType (`uSync/v9/MemberTypes/`).
- **UI/CDN**: no verificado (fuera de este repo).
- **Flags**: ninguno — siempre activo. `HealthcareSettings.RequireTwoFactorStaff=true` por default para roles clínicos (ADR 0098).
- **Tests**: `FileSystemMemberTwoFactorStoreTests.cs`, `TwoFactorRecoveryCodesTests.cs`, `UmbracoMemberRosterWriterTests.cs`, `AccountControllerReturnUrlTests.cs`. **Hueco**: no hay test dedicado para `UmbracoMemberRosterReader` (solo Writer).
- **Huecos**: `AdminController.cs` repite `_gate.HasAnyRole(ModeratorRolesCsv)` en 27 actions distintas sin un filtro centralizado (`Synergos.CMS.Web/Controllers/AdminController.cs` — patrón en todo el fichero).

### Consola de administración
- **Madurez**: VIVO
- Ver sección dedicada abajo. Nueve vistas Razor (`Views/Admin/*.cshtml`), cero dependencia del backoffice AngularJS de Umbraco.
- **Huecos**: sin tests de controller (`Synergos.CMS.Tests/Controllers/` no tiene `AdminControllerTests.cs`); antiforgery deliberadamente omitido en los POST (`AdminController.cs:25-27`, comentario explícito "sin antiforgery... risk de CSRF es bajo").

### Auditoría
- **Madurez**: VIVO
- **Seams**: `IAuditTrailWriter` (`Synergos.CMS.Interfaces`)
- **Implementación**: `FileSystemAuditTrailWriter` — JSONL append-only, un archivo por día, lock interno, dedupe por `Id` (`Synergos.CMS.Web/Services/FileSystemAuditTrailWriter.cs:33-63`).
- **Persistencia**: `App_Data/syn-audit/{yyyy-MM-dd}.jsonl`. Sobrevive reinicio. Retención 90 días (`AdminSettings.AuditRetentionDays`, `AdminSettings.cs:53-58`) vía `AuditRetentionPolicy`.
- **Superficie HTTP**: `GET /admin/audit` (listado+filtros), `GET /admin/audit/{id}` (detalle), `GET /admin/audit/export` (CSV streaming) — rol admin/moderator/editor.
- **Schema CMS**: n/a (no vive en uSync).
- **Tests**: `FileSystemAuditTrailWriterTests.cs`.
- **Huecos**: el comentario en `HealthcareRetentionPolicy.cs:11-12` afirma que la auditoría de acceso PHI tiene "retención indefinida por obligación legal (la gestiona otra policy)" — pero `AuditRetentionPolicy` no distingue eventos PHI de eventos genéricos: **todo** el audit trail (incluidas lecturas/escrituras PHI, ADR 0098) se purga a los 90 días por igual. Contradice la premisa legal citada. (`Synergos.CMS.Web/Services/Retention/HealthcareRetentionPolicy.cs:11-12` vs `AuditRetentionPolicy.cs:34`).

### Notificaciones multi-canal
- **Madurez**: VIVO (Email) / VIVO-pero-opt-in (Slack/Discord/Teams/Webhook)
- **Seams**: `IEmailService`, `IEmailTemplateRenderer`, `ITransactionalNotifier`+`ITransactionalNotifierChannel`, `IAlertNotifier`+`IAlertNotifierChannel`, y 3 composites paralelos: `ICommentModerationNotifier`, `IFormSubmissionNotifier`, `ICartAbandonmentNotifier` (cada uno con 4 canales: Email/Webhook/Slack/Discord/Teams).
- **Implementación**: `DefaultEmailService` envuelve `Umbraco.Cms.Core.Mail.IEmailSender` real (SMTP vía config Umbraco) (`SeamComposer.cs:609-615`). Canales no-Email son no-op silenciosos si la URL/webhook no está configurada (`SeamComposer.cs:573` y comentarios paralelos). `ITransactionalNotifier` usa `IIdempotencyLedger` (scope `notifications`) para at-most-once (ADR 0106).
- **Persistencia**: sin persistencia propia de mensajes; el ledger de idempotencia sí persiste en `App_Data/syn-notifications/*.txt` (marcadores, sin expiración/retención — crece para siempre).
- **Superficie HTTP**: `POST /admin/webhooks/test/comment|form|cart` (dispara payload de prueba marcado como test, rol admin/moderator/editor).
- **Flags**: cada canal Slack/Discord/Teams/Webhook es opt-in por URL vacía = no-op (no hay flag explícito, es config-driven).
- **Tests**: `CompositeNotifiersTests.cs`, `WebhookResilienceSettingsValidatorTests.cs`.
- **Huecos**: `syn-notifications/` y `syn-payment-events/` (ledgers de idempotencia) no tienen retención — `FileSystemIdempotencyLedger.cs` no expone ni consume ningún `IRetentionPolicy`.

### Realtime (SSE)
- **Madurez**: VIVO pero de alcance mínimo
- **Seams**: `IRealtimeNotifier`, `INotificationFeed` (pull, no push)
- **Implementación**: `SseRealtimeHub` (singleton, fan-out en proceso) expuesto por `RealtimeController` (`GET /api/realtime/stream?channel=`), autorización **fail-closed por canal explícito** — hoy solo declara UN canal: `eventos:checkin:{eventId}` (rol `organizador,admin`) (`RealtimeController.cs:63-83`). Cualquier otro canal se rechaza con 403 y un warning de log.
- **Consumidores reales**: únicamente `EventosController` publica en el hub (verificado por grep — ningún otro controller inyecta `IRealtimeNotifier`).
- **Persistencia**: ninguna — mensajes en memoria, se pierden si nadie está suscrito.
- **Tests**: `SseRealtimeHubTests.cs`, `RealtimeControllerTests.cs`.
- **Huecos**: la infraestructura transversal (ADR 0111) está construida para "canales" en plural pero en la práctica sirve **un solo** caso de uso (check-in de Eventos); Blogs/DMs/Dashboard no empujan nada en vivo pese a tener los seams de dominio (`IContentStream`, `IMessagingService`) que podrían alimentarlo.

### Ficheros privados
- **Madurez**: VIVO pero de alcance mínimo
- **Seams**: `IPrivateFileStore`, `IDocumentUploadService`
- **Implementación**: `FileSystemPrivateFileStore` — cifrado con `IDataProtector` (purpose `Synergos.PrivateFiles.v1`), escritura atómica, id opaco generado por el store, content-type/nombre-original viajan dentro del sobre cifrado (`FileSystemPrivateFileStore.cs:11-28`).
- **Persistencia**: `App_Data/syn-files/{scope}/{fileId}.bin`. Sin retención — crece indefinidamente.
- **Consumidores reales**: solo Gobierno (`StubDocumentUploadService`, `GovController.cs:386-426` — descarga con chequeo de permiso en cada request: dueño del expediente o funcionario).
- **Superficie HTTP**: `GET /api/gov/document/{caseId}/{docId}` (member-gated, ownership check).
- **Tests**: `FileSystemPrivateFileStoreTests.cs`.
- **Huecos**: sin `IRetentionPolicy` registrada para `syn-files/`; ningún otro vertical (Healthcare, Eventos) lo usa pese a manejar documentos sensibles.

### Dashboard / métricas
- **Madurez**: VIVO (dato real, no seed) con un panel explícitamente apagado
- **Seams**: `IDashboardReadModel`, `IMetricsExporter`, `IMetricsProjectionStore`, `ICheckoutRecorder`, `IAnalyticsTracker`
- **Implementación**: `ProjectingAnalyticsTracker` decora `LoggerAnalyticsTracker` y proyecta cada evento a `InMemoryMetricsProjectionStore` (buckets por hora, O(1), sin IO en el hot path) (`SeamComposer.cs:629-653`). `DefaultDashboardReadModel` compone checkouts + proyección + roster + search + webhooks — sin datos sembrados (`DefaultDashboardReadModel.cs`).
- **Persistencia**: `App_Data/syn-dashboard/orders/{yyyy-MM-dd}.jsonl` (checkouts, append-only, dedupe por OrderId) + `App_Data/syn-dashboard/projections/projection.jsonl` (flush periódico cada `FlushIntervalSeconds`=300s, rehidrata al boot). Ambos sobreviven reinicio; hasta 5 min de datos en riesgo si el proceso muere entre flushes.
- **Retención**: `ProjectionRetentionDays`=730 días (2 años) — aplica tanto a `orders/` (`DashboardOrdersRetentionPolicy`) como a la proyección in-place al flushear.
- **Superficie HTTP**: `GET /api/dashboard/sales|members|behavior|search|webhooks|insights|export` (`DashboardApiController.cs`, rol admin, `[ApiController]`, consumido por Angular `<synergos-dashboard>` — no hay vista SSR equivalente en `Views/Admin/`).
- **Huecos**: `GetAgentInsights()` devuelve siempre `Available:false, Note:"La capa de IA (ADR 0095) aún no está construida."` (`DefaultDashboardReadModel.cs:95-96`) — **SÓLO SEAM** para ese panel específico.
- **Tests**: `DefaultDashboardReadModelTests.cs`, `DefaultMetricsExporterTests.cs`, `InMemoryMetricsProjectionStoreTests.cs`, `DashboardApiControllerTests.cs`, `DashboardOrdersRetentionPolicyTests.cs`.

### Retención + GDPR
- **Madurez**: VIVO
- **Seams**: `IRetentionPolicy` (6 implementaciones registradas), `IGdprRtbfCoordinator`, `IConsentLedger`, `IHealthcareDataAnonymizer`
- **Implementación**: `RetentionSweepHostedService` itera todas las `IRetentionPolicy` cada 24h con try/catch per-policy (`SeamComposer.cs:541-556`). `FileSystemGdprRtbfCoordinator` hace hard-delete del Member + anonimiza comments/forms + emite audit terminal `gdpr.rtbf-processed` (`SeamComposer.cs:528-533`).
- **Persistencia**: ver inventario App_Data abajo.
- **Superficie HTTP**: `POST /admin/members/{key}/gdpr-erase` (rol admin/moderator/editor, self-erase bloqueado, `AdminController.cs:1110-1143`).
- **Huecos**: de las ~20 superficies de estado, solo 6 tienen `IRetentionPolicy` real (audit, comments-rejected, forms, search-analytics [muerta], dashboard-orders, healthcare). Las 14 restantes (todo lo que pasa por `IJsonEntityStore` genérico: orders/payments/reservations/travel-orders/enrollments/course-progress/gov-cases/event-orders/reviews, más `syn-files`, `syn-2fa`, `syn-payment-events`, `syn-notifications`) no purgan nunca — comentario explícito en `SeamComposer.cs:160-164` confirma que `IJsonEntityStore` es "la ÚNICA impl de persistencia" pero no menciona retención.
- **Tests**: `FileSystemGdprRtbfCoordinatorTests.cs`, `FileSystemHealthcareDataAnonymizerTests.cs`, `HealthcareRetentionPolicyTests.cs`, `RetentionPolicyTests.cs` (cubre las 4 policies genéricas + dashboard).

### Feature flags
- **Madurez**: VIVO pero minimalista
- **Seams**: `IFeatureGate`
- **Implementación**: `AppsettingsFeatureGate` — diccionario `Gates` en `FeatureFlagsSettings`, nombre desconocido resuelve a `false` (`Synergos.CMS.Application/Services/Impl/AppsettingsFeatureGate.cs`). Sin flag service externo (ADR 0011, decisión explícita).
- **Persistencia**: `appsettings.json` estático — no runtime-toggle, requiere redeploy/restart para cambiar.
- **Uso real verificado**: `Synergos:DevSeed:Enabled` gatea el seeder dev (`DevController`/`DevTestContentSeeder`); `Synergos:LayoutComposer:EnableStarterScaffold` gatea el scaffold del Layout Composer.
- **Huecos**: no hay UI de toggling — cualquier cambio de flag es un cambio de `appsettings` + restart, no un self-service de producto.

### Branding / temas
- **Madurez**: VIVO
- **Seams**: `IBrandingProvider`, `IBrandThemeProvider`
- **Implementación**: `HostBasedBrandingProvider` resuelve el brand activo iterando `siteRoot`s publicados del **contenido real** de Umbraco y matcheando `canonicalHostname` contra el host del request; fallback a `BrandingSettings` estático si no hay match (`HostBasedBrandingProvider.cs:60-115`). Cero `if (brand.Key == "X")` — cumple ADR 0010.
- **Persistencia**: contenido Umbraco (`siteRoot.compBranding`), no App_Data.
- **Schema CMS**: `compBranding`, `siteConfigSettings`, `DropdownOptions.PageThemeVariant` (uSync).
- **Tests**: no verificado un test dedicado a `HostBasedBrandingProvider` en el listado de `Synergos.CMS.Tests/Services/` (no se encontró `HostBasedBrandingProviderTests.cs`); puede haber cobertura de arquitectura no localizada.
- **Huecos**: ninguno detectado en el código leído.

### Salud del sistema
- **Madurez**: VIVO
- **Seams**: `ISchemaHealthProbe`, `IWebhookTelemetryStore`
- **Implementación**: `HealthController` (`GET /_health`) agrega todos los `ISchemaHealthProbe` registrados (SchemaVersionProbe, UsyncFolderProbe, BundleRegistryProbe) — 200 si todos healthy, 503 si alguno falla (`HealthController.cs:28-58`). `HealthzController` (`GET /healthz`, `/healthz/ready`) para k8s/LB liveness/readiness sin auth (`HealthzController.cs`). `AdminController.Health` (`GET /admin/health`, rol admin/moderator/editor) da diagnostics ricos: uptime, memoria, pending comments, resilience config, `IWebhookTelemetryStore.GetChannelStats()`.
- **Persistencia**: `IWebhookTelemetryStore` es `InMemoryWebhookTelemetryStore` — ring buffer de 1000 outcomes por canal, **se resetea en cada reinicio** (comentario explícito, `SeamComposer.cs:563-566`).
- **Tests**: `HealthControllerTests.cs`, `InMemoryWebhookTelemetryStoreTests.cs`, `WebhookTelemetryAlertScannerTests.cs`.
- **Huecos**: ninguno grave — el reset de telemetría en reinicio es una limitación documentada, no un hueco oculto.

## La consola de administración

Ruta base `/admin`, gate uniforme `IMemberAccessGate.HasAnyRole("admin,moderator,editor")` verificado inline en cada action (sin filtro central, sin distinción de permisos entre los 3 roles — cualquiera de los tres puede hacer cualquier acción, incluido GDPR-erase o member-delete).

| Pantalla | Ruta | Rol | Acciones |
|---|---|---|---|
| Inicio | `GET /admin` | admin,moderator,editor | resumen: pending comments, form keys, top 5 búsquedas 7d |
| Moderación de comentarios | `GET /admin/moderation/comments` | admin,moderator,editor | approve / reject / mark-as-spam / bulk-approve / bulk-reject / bulk-undo-reject (ventana 30s) por comentario |
| Forms | `GET /admin/forms`, `GET /admin/forms/{formKey}/{storageId}` | admin,moderator,editor | listar+filtrar por formKey/fecha, ver detalle, borrar submission, exportar CSV streaming (`/admin/forms/export`, hard cap 5000) |
| Miembros | `GET /admin/members` | admin,moderator,editor | lock / unlock / reset 2FA / delete (self-delete bloqueado) / password-reset / set-roles / GDPR-erase (self-erase bloqueado) — ve estado 2FA por miembro |
| Búsqueda (analytics) | `GET /admin/analytics/search` | admin,moderator,editor | top queries y top no-result queries por rango de fechas (solo lectura) |
| Webhooks (test harness) | `GET /admin/webhooks/test` | admin,moderator,editor | disparar payload de prueba marcado "[TEST]" a comment/form/cart notifier composites (no crea datos reales) |
| Auditoría | `GET /admin/audit`, `GET /admin/audit/{id}` | admin,moderator,editor | listar+filtrar por actor/acción/fecha, drill-down con eventos cercanos (±5 min), exportar CSV streaming (solo lectura, append-only) |
| Health | `GET /admin/health` | admin,moderator,editor | ver uptime/memoria/.NET version/pending counts/resilience config/telemetría de webhooks (solo lectura) |

Todo lo demás (contenido, schema, DocTypes, media) sigue requiriendo el backoffice nativo de Umbraco — la consola `/admin` es exclusivamente operación runtime (moderación, soporte a members, auditoría, diagnóstico), no autoría de contenido.

## Inventario de estado en App_Data

| Directorio | Qué guarda | Seam que escribe | Retención | Sobrevive reinicio |
|---|---|---|---|---|
| `syn-audit/{yyyy-MM-dd}.jsonl` | Eventos de auditoría append-only (admin actions, PHI access, gov transitions) | `IAuditTrailWriter` → `FileSystemAuditTrailWriter` | 90 días (`AdminSettings.AuditRetentionDays`) | Sí |
| `syn-comments/{nodeId}.json` | Comentarios por nodo (approved + pending), anidación 1 nivel | `ICommentRepository` → `FileSystemCommentRepository` | 30 días solo rejected (`CommentsRetentionPolicy`); approved nunca se purga | Sí |
| `syn-form-submissions/{formKey}/{ts}_{guid}.json` | Submissions de formularios con metadata (IP, UA, referrer) | `IFormSubmissionHandler`/`Reader` → `FileSystemFormSubmissionHandler` | 365 días | Sí |
| `syn-2fa/{memberKey}.json` | Secret TOTP (Base32), recovery codes hasheados, estado enrolled | `FileSystemMemberTwoFactorStore` (cifrado `IDataProtector`) | Ninguna (se borra vía 2FA-reset o GDPR-erase, no por tiempo) | Sí |
| `syn-files/{scope}/{fileId}.bin` | Documentos privados subidos por usuarios (ej. adjuntos de trámites), cifrados, content-type+nombre dentro del sobre | `IPrivateFileStore` → `FileSystemPrivateFileStore` (cifrado `IDataProtector`) | **Ninguna** | Sí |
| `syn-healthcare/patients/*.phi`, `patient-history/`, `prescriptions/`, `consents/`, `appointments/` | PHI cifrado: pacientes (con versionado), recetas, consentimientos, citas | `IPhiStore` → `FileSystemEncryptedPhiStore` (cifrado `IDataProtector`) | 2190 días (6 años) recursivo sobre `*.phi` (`HealthcareRetentionPolicy`) | Sí |
| `syn-dashboard/orders/{yyyy-MM-dd}.jsonl` | Checkouts completados (revenue, append-only, dedupe por OrderId) | `ICheckoutRecorder` → `FileSystemCheckoutRecorder` | 730 días (`DashboardSettings.ProjectionRetentionDays`) | Sí |
| `syn-dashboard/projections/projection.jsonl` | Buckets de métricas por slug/hora (cart-added, logins, registers, etc.) | `IMetricsProjectionStore` → `InMemoryMetricsProjectionStore` (flush cada 300s) | 730 días, aplicada al rehidratar/flushear | Sí, salvo hasta 5 min entre flushes |
| `syn-search-analytics/{yyyy-MM-dd}.jsonl` | Una línea por búsqueda (`query`, `resultCount`, `elapsedMs`, `atUtc`) escrita por `FileSystemSearchAnalyticsStore` | `SearchController.Search` (registro) + `AdminController.Index` / `SearchController.Analytics` (lectura agregada sobre la ventana pedida) | 90 días — `SearchAnalyticsRetentionPolicy`, y aquí **sí** purga | **Sí** — sobrevive al reinicio |
| `syn-orders/{orderRef}.json` | Órdenes de Tienda (líneas, sesión de pago) | `IJsonEntityStore` genérico ← `StubShopOrderService` | Ninguna | Sí |
| `syn-payments/{sessionId}.json` | Sesiones de pago (stub PSP) | `IJsonEntityStore` genérico ← `StubPaymentProvider` | Ninguna | Sí |
| `syn-reservations/{holdId}.json` | Holds de habitación/asiento/cita (motor polimórfico compartido por Hoteles/Eventos/Healthcare/Propiedades) | `IJsonEntityStore` genérico ← `StubReservationService` | Ninguna | Sí |
| `syn-travel-orders/{tripRef}.json` | Carritos de viaje multi-producto confirmados | `IJsonEntityStore` genérico ← `TravelCartService` | Ninguna | Sí |
| `syn-enrollments/{enrollmentId}.json` | Matrículas de Educación | `IJsonEntityStore` genérico ← `StubEnrollmentService` | Ninguna | Sí |
| `syn-course-progress/{key}.json` | Progreso por (alumno,curso) | `IJsonEntityStore` genérico ← `StubEnrollmentService` | Ninguna | Sí |
| `syn-gov-cases/{radicado}.json` | Expedientes de Gobierno (máquina de estados auditada) | `IJsonEntityStore` genérico ← `StubApplicationService` | Ninguna | Sí |
| `syn-event-orders/{orderRef}.json` | Órdenes de tickets de Eventos | `IJsonEntityStore` genérico ← `StubEventTicketingService` | Ninguna | Sí |
| `syn-reviews/{key}.json` | Reseñas UGC de catálogo (T10) — **registrado pero sin consumer** (catálogo aún no lo lee, rating siempre 0d) | `IJsonEntityStore` genérico ← `FileSystemCatalogSocialProof` | Ninguna | Sí |
| `syn-payment-events/{provider-eventId}.txt` | Marcadores de idempotencia de webhooks de pago (at-most-once) | `IIdempotencyLedger` → `FileSystemIdempotencyLedger` (scope `payment-events`) | Ninguna | Sí |
| `syn-notifications/{dedupeKey}.txt` | Marcadores de idempotencia de notificaciones transaccionales | `IIdempotencyLedger` → `FileSystemIdempotencyLedger` (scope `notifications`) | Ninguna | Sí |

Nota: `IJsonEntityStore` es genérico por `resourceType` (ADR 0105) — cualquier `resourceType` nuevo crea automáticamente `syn-{resourceType}/` sin cambio de código. El inventario de arriba refleja los `resourceType` verificados por grep en el código actual; un vertical nuevo puede añadir más sin tocar el store.

## Lo que un negocio obtiene "gratis" al montarse sobre Synergos

- Identidad de members con 2FA TOTP real (no placeholder) + roster admin + GDPR RTBF, sin escribir una línea de código de autenticación.
- Auditoría append-only automática de cada acción admin sensible (lock/unlock/delete/roles/gdpr-erase/form-delete/comment-moderation), consultable y exportable.
- Notificaciones transaccionales multi-canal (Email real vía SMTP de Umbraco; Slack/Discord/Teams/Webhook con solo configurar una URL) con idempotencia at-most-once ya resuelta.
- Panel de métricas de negocio (ventas, miembros, comportamiento, búsqueda, salud de webhooks) alimentado por datos reales de uso, sin seed ni mock.
- Consola operativa `/admin` sin tocar el backoffice de Umbraco, para moderación/soporte/diagnóstico del día a día.
- Almacén de ficheros privados cifrado at-rest fuera de `wwwroot` para cualquier vertical que necesite guardar adjuntos sensibles (ya probado con Gobierno).
- Health checks listos para Kubernetes/LB (`/healthz`, `/healthz/ready`) más un `/_health` extensible por schema.
- Infra de retención/purga automatizada (aunque parcial — ver huecos) sin necesidad de un cron externo.

## Huecos transversales

1. ~~**`ISearchAnalyticsStore` no persiste, pero su retención sí existe**~~ — **RESUELTO (2026-08-02)**. `FileSystemSearchAnalyticsStore` escribe en el mismo `App_Data/syn-search-analytics/` que la política barre, así que "top queries 7d" de `AdminController.Index` ya significa lo que promete. De paso se cerró un riesgo que no se había anotado aquí: el `ConcurrentDictionary` anterior no tenía tope y se indexaba por el texto que escribe el visitante.
2. **Retención cubre 6 de ~20 superficies de estado** — todo lo que pasa por `IJsonEntityStore` genérico (órdenes, pagos, reservas, viajes, matrículas, expedientes de gobierno, tickets, reviews) más `syn-files` (documentos cifrados) y los dos ledgers de idempotencia (`syn-payment-events`, `syn-notifications`) no tiene ningún `IRetentionPolicy`: crecen para siempre, un fichero por entidad/marcador. `Synergos.CMS.Web/Services/FileSystemJsonEntityStore.cs` no expone hook de expiración.
3. **Contradicción documentada en retención de auditoría PHI** — `Synergos.CMS.Web/Services/Retention/HealthcareRetentionPolicy.cs:11-12` afirma que la auditoría de acceso PHI vive "indefinida por obligación legal (la gestiona otra policy)", pero `AuditRetentionPolicy` (`Synergos.CMS.Web/Services/Retention/AuditRetentionPolicy.cs`) purga **todo** `syn-audit/` (incluidos eventos PHI) a los 90 días por igual — no hay policy separada para eventos PHI.
4. **`AdminController` sin tests de controller** — 1144 líneas, 27+ actions con gate de rol repetido inline, cero antiforgery en POSTs (decisión documentada pero sin test de regresión), y ningún fichero en `Synergos.CMS.Tests/Controllers/` lo cubre.
5. **Realtime transversal infrautilizado** — la infraestructura SSE (ADR 0111, fail-closed por canal) está construida para servir múltiples dominios pero en producción solo un canal (`eventos:checkin:*`) está autorizado y solo `EventosController` publica en el hub.

## Tabla de artefactos

| Artefacto | Ruta |
|---|---|
| Wiring central de seams | `Synergos.CMS.Web/Composers/SeamComposer.cs` |
| Consola admin (controller) | `Synergos.CMS.Web/Controllers/AdminController.cs` |
| Vistas consola admin | `Synergos.CMS.Web/Views/Admin/*.cshtml` |
| Dashboard API (Angular) | `Synergos.CMS.Web/Controllers/DashboardApiController.cs` |
| Dashboard read-model | `Synergos.CMS.Web/Services/DefaultDashboardReadModel.cs` |
| Health (schema) | `Synergos.CMS.Web/Controllers/HealthController.cs` |
| Health (liveness/readiness) | `Synergos.CMS.Web/Controllers/HealthzController.cs` |
| Realtime SSE | `Synergos.CMS.Web/Controllers/RealtimeController.cs`, `Synergos.CMS.Web/Services/SseRealtimeHub.cs` (no leído completo — referenciado) |
| 2FA TOTP | `Synergos.CMS.Web/Services/UmbracoMemberTwoFactorService.cs`, `Synergos.CMS.Web/Services/FileSystemMemberTwoFactorStore.cs` |
| Audit trail | `Synergos.CMS.Web/Services/FileSystemAuditTrailWriter.cs` |
| GDPR RTBF | `Synergos.CMS.Web/Services/FileSystemGdprRtbfCoordinator.cs` |
| PHI store + consent | `Synergos.CMS.Web/Services/FileSystemEncryptedPhiStore.cs`, `FileSystemConsentLedger.cs` |
| Private files | `Synergos.CMS.Web/Services/FileSystemPrivateFileStore.cs` |
| JSON entity store genérico | `Synergos.CMS.Web/Services/FileSystemJsonEntityStore.cs` |
| Idempotency ledger | `Synergos.CMS.Web/Services/FileSystemIdempotencyLedger.cs` |
| Retention policies | `Synergos.CMS.Web/Services/Retention/*.cs` |
| Branding provider | `Synergos.CMS.Web/Services/HostBasedBrandingProvider.cs` |
| Feature gate | `Synergos.CMS.Application/Services/Impl/AppsettingsFeatureGate.cs` |
| Config POCOs relevantes | `Synergos.CMS.Application/Configuration/{AdminSettings,RetentionSettings,DashboardSettings,HealthcareSettings,CommentsSettings,FormsSettings,JsonEntityStoreSettings,PrivateFileStoreSettings}.cs` |
| Search analytics (JSONL por día, con retención) | `Synergos.CMS.Web/Services/FileSystemSearchAnalyticsStore.cs` |
| Tests relevantes | `Synergos.CMS.Tests/Services/*.cs`, `Synergos.CMS.Tests/Controllers/{DashboardApiControllerTests,HealthControllerTests,RealtimeControllerTests,AccountControllerReturnUrlTests}.cs` |
