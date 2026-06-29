using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Wires the extension seams declared in
/// <c>Synergos.CMS.Interfaces</c> to their Ola 1 defaults and Ola 3
/// adapters, and registers the dictionary notification handler.
/// </summary>
/// <remarks>
/// Per ADR 0005 all <see cref="IComposer"/> implementations live in
/// <c>Synergos.CMS.Web/Composers/</c>. This composer extracts
/// <c>IOptions&lt;T&gt;.Value</c> and injects the POCO into defaults,
/// honouring the decision taken in Ola 1 not to add
/// <c>Microsoft.Extensions.Options</c> as a reference of
/// <c>Synergos.CMS.Application</c>.
/// </remarks>
[ComposeAfter(typeof(OptionsComposer))]
public sealed class SeamComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var services = builder.Services;

        // Olas 146-147 — Resilience tuning hot-reloadable. WebhookResilienceSettings
        // bind via AddOptions().Bind(...) registra el IConfigurationChangeTokenSource
        // automático — cuando appsettings.json cambia, IOptionsMonitor<...> fire,
        // y el siguiente handler rotation (HttpClientFactory default ~2min) re-
        // ejecuta Configure<TDep> con los valores frescos. Sin rebuild manual de
        // pipelines.
        services.AddOptions<WebhookResilienceSettings>()
            .Bind(builder.Config.GetSection("Synergos:Admin:WebhookResilience"));

        // Ola 54.1 — IBrandingProvider resuelve el brand activo según el
        // hostname del request, matchéandolo contra los siteConfigSettings
        // publicados (cada uno con su brandKey via compBranding). Si no
        // hay match, fallback al brand de BrandingSettings (config
        // estática). Singleton — depende de accessors per-request.
        services.AddSingleton<IBrandingProvider>(sp =>
            new HostBasedBrandingProvider(
                sp.GetRequiredService<IHttpContextAccessor>(),
                sp.GetRequiredService<IUmbracoContextAccessor>(),
                sp.GetRequiredService<IOptions<BrandingSettings>>().Value));

        services.AddSingleton<IFeatureGate>(sp =>
            new AppsettingsFeatureGate(
                sp.GetRequiredService<IOptions<FeatureFlagsSettings>>().Value));

        services.AddSingleton<IDictionaryCache, DictionaryCache>();

        // Ola 8.5 + Cap-280 Batch B — SynHost stack (ADR 0015 + 0089).
        // El IBundleRegistryClient se registra condicionalmente por
        // Synergos:BundleRegistry:Mode (default Stub):
        //   - Stub: siempre null. Default cuando no hay CDN.
        //   - FileSystem: lee registry.json + manifests del filesystem,
        //     hot-reload via FileSystemWatcher, SRI lazy compute.
        //     Útil con CDN local (e.g. C:\LOCAL_CDN).
        //   - Http: GET al registry endpoint remoto (deferred).
        var bundleRegistryMode = builder.Config["Synergos:BundleRegistry:Mode"] ?? "Stub";
        if (string.Equals(bundleRegistryMode, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBundleRegistryClient, FileSystemBundleRegistryClient>();
        }
        else
        {
            services.AddSingleton<IBundleRegistryClient, StubBundleRegistryClient>();
        }
        services.AddSingleton<ISynHostEmitter, DefaultSynHostEmitter>();

        // Warmup hosted service fuerza la construcción del IBundleRegistryClient
        // singleton al boot — sin esto el adapter queda lazy hasta el primer
        // SynHost render, ocultando logs de "registry loaded" + watcher status.
        services.AddHostedService<BundleRegistryWarmupHostedService>();

        // Ola 3 — Web-side adapters.
        services.AddSingleton<IContentContextAccessor, UmbracoContentContextAccessor>();

        // Ola 37 — Brand theme provider reads themeSettings nodes from
        // the Umbraco content tree (ADR 0020). Transient because it
        // depends on the per-request IUmbracoContextAccessor; the cost
        // is negligible (single projection).
        services.AddTransient<IBrandThemeProvider, DefaultBrandThemeProvider>();

        // Ola 49 — Page render context resolver. Cascada page →
        // siteRoot → defaults inline para chrome/theme (ADR 0022).
        // Transient por la misma razón que el theme provider.
        services.AddTransient<IPageRenderContextResolver, DefaultPageRenderContextResolver>();

        // Ola 50 — Global component resolver. Lee siteConfigSettings.
        // globalComponents (BlockList) y devuelve la pieza activa
        // aplicable. Ola 52.A extendió a 4 cfg* (Alert/Banner/
        // FooterNote/Modal). Cada cfg* nuevo añade un método hermano
        // en el resolver sin tocar la lógica existente.
        services.AddTransient<IGlobalComponentResolver, DefaultGlobalComponentResolver>();

        // Ola 52.C — Member access gate + handler que aplica
        // compMemberGating.requiresAuth + allowedRolesCsv en el
        // routing del request. Singleton porque el gate solo lee
        // del HttpContext via accessor.
        services.AddSingleton<IMemberAccessGate, DefaultMemberAccessGate>();

        // Ola 56.2 — Blog query service. Recorre el published cache
        // buscando postPage descendientes del siteRoot, aplica filtros
        // de categoría/tags y proyecta a PostSummary records.
        // Consumido por renderers ArticleList, BlogHighlight y
        // PostCategoryPage.cshtml. Transient — depende del
        // IUmbracoContextAccessor per-request.
        services.AddTransient<IBlogQuery, DefaultBlogQuery>();

        // Ola 57.1 — Cart service. Persiste cart en cookie HMAC-firmada
        // del visitante (sin DB, sin login required). Hidrata items
        // cruzando SKUs con productPage publicados. Transient porque
        // depende de IHttpContextAccessor + IUmbracoContextAccessor.
        services.AddTransient<ICartService, DefaultCartService>();

        // Ola 81 — Cart abandonment tracker (ADR 0044). Singleton por
        // diseño — el state in-memory persiste entre requests del
        // mismo proceso. Background scanner emite "cart.abandoned"
        // events via IAnalyticsTracker cada N minutos para los carts
        // que excedan el threshold.
        services.AddSingleton<ICartAbandonmentTracker, InMemoryCartAbandonmentTracker>();
        services.AddHostedService<CartAbandonmentScannerHostedService>();

        // Ola 57.2 — Shop query service. Recorre productPage descendientes,
        // aplica filtros de categoría/sort y proyecta a ProductSummary.
        // Consumido por ProductGrid block + ProductCategoryPage.cshtml.
        services.AddTransient<IShopQuery, DefaultShopQuery>();

        // Bucket B (B-1) — Price formatter es-CO centralizado. Unifica el
        // render del precio (miles con punto, sin decimales, + moneda) que
        // antes se duplicaba inline en 6+ renderers Razor de Shop. Lógica
        // pura en Application; el composer extrae IOptions<CartSettings>.Value
        // y la inyecta como POCO (mismo patrón que AppsettingsFeatureGate)
        // para no referenciar Microsoft.Extensions.Options desde Application.
        // Singleton — stateless, solo cierra sobre la moneda default.
        services.AddSingleton<IPriceFormatter>(sp =>
            new EsCoPriceFormatter(
                sp.GetRequiredService<IOptions<CartSettings>>().Value));

        // Motor de pago (PSP) — seam IPaymentProvider, stub-first (doc 16/17).
        // Hoy ICheckoutRecorder solo REGISTRA la orden; faltaba procesar el cobro.
        // StubPaymentProvider (Application, puro) auto-autoriza para que el checkout
        // corra end-to-end en demo; swap por adapter real (Stripe/Wompi/PayU CO)
        // sin tocar el motor. Singleton — el stub mantiene estado en memoria.
        services.AddSingleton<IPaymentProvider, StubPaymentProvider>();

        // Motor de reservas (vertical Hoteles) — 3 seams stub-first (doc 17),
        // calcando IPaymentProvider. Hoy sirven la demo end-to-end en memoria;
        // se cambian por adapters reales (PMS / channel-manager) sin tocar el
        // motor. ADR 0002 (Application pura, sin Umbraco).
        //   - IRoomAvailabilityProvider: search por fecha+ocupación → ofertas
        //     (Room Type × Rate Plan). Stub: catálogo sembrado en memoria.
        //   - IReservationService: Hold/Confirm/Cancel/Get. Stub: estado en
        //     ConcurrentDictionary; Confirm idempotente. Singleton para que el
        //     estado persista entre requests del mismo proceso.
        //   - ICancellationPolicyEvaluator: penalidad por fecha. Stub puro/
        //     determinista (non-refundable → total; refundable → 0 si a tiempo).
        services.AddSingleton<IRoomAvailabilityProvider, StubRoomAvailabilityProvider>();
        services.AddSingleton<IReservationService, StubReservationService>();
        services.AddSingleton<ICancellationPolicyEvaluator, StubCancellationPolicyEvaluator>();

        // Auto-cancel de holds vencidos (aprendizaje NS.Booking, doc 17): barre
        // cada ~2 min los Held cuyo ExpiresAt pasó y libera el cupo (→ Expired).
        services.AddHostedService<HoldExpirationScannerHostedService>();

        // Motor de vuelos (vertical Aerolíneas, doc 18) — seam stub-first
        // calcando IRoomAvailabilityProvider de Hoteles. StubFlightAvailability
        // Provider (Application, puro/determinista) sirve un catálogo sembrado
        // en memoria (rutas BOG-MDE/CTG/MIA × itinerarios × familias tarifarias)
        // para que la búsqueda corra end-to-end en demo; el adapter real
        // (GDS / NDC con cotización en vivo) se enchufa sin tocar el motor.
        // El flujo hold/pay/confirm reusará IReservationService/IPaymentProvider
        // (generalizar ReservationRequest hotel→genérico es follow-up).
        // Singleton — stateless, catálogo estático. ADR 0002 (Application pura).
        services.AddSingleton<IFlightAvailabilityProvider, StubFlightAvailabilityProvider>();

        // OLA 1 Booking — motor transaccional multi-producto (doc booking-app-spec).
        // Tercer producto del carrito de viaje: autos. Seam stub-first calcando
        // IFlightAvailabilityProvider. StubCarRentalProvider (Application, puro/
        // determinista) sirve un catálogo sembrado (categorías SIPP × rentadoras)
        // para que la búsqueda corra end-to-end; el adapter real (agregador de
        // rentadoras) se enchufa sin tocar el motor. Singleton — stateless.
        services.AddSingleton<ICarRentalProvider, StubCarRentalProvider>();

        // OLA 1 Booking — carrito de viaje multi-producto: toma N ítems
        // heterogéneos (hotel|vuelo|auto), aparta CADA uno como reserva
        // (IReservationService.HoldItemAsync, vía polimórfica aditiva que NO toca
        // el flujo hotel) y abre UNA sola sesión de pago (IPaymentProvider) por el
        // total. ConfirmAsync captura el pago y confirma todas las reservas.
        // Idempotente. Singleton — el estado del carrito (orderRef → reservas +
        // sesión) vive en memoria del proceso, igual que el StubReservationService.
        services.AddSingleton<ITravelCartService, TravelCartService>();

        // OLA 2 Tienda — motor del marketplace e-commerce (doc tienda-app-spec).
        // Dos seams stub-first, aditivos (no tocan Booking/Travel ni el carrito
        // cookie IShopQuery/ICartService de los bloques Razor del CMS). ADR 0002
        // (Application pura, sin Umbraco) + ADR 0075 (seam con tests canónicos).
        //   - IProductCatalogProvider: búsqueda facetada (texto + categoría +
        //     facetas → productos + facetas derivadas) + detalle del producto
        //     (PDP: variantes + reviews + Q&A). Stub: catálogo sembrado en memoria
        //     (3 categorías × 6 productos). Adapter real: Examine/Lucene o Algolia.
        //     Singleton — stateless, catálogo estático.
        //   - IShopOrderService: motor transaccional del checkout, calcando
        //     TravelCartService. Resuelve precio/stock real desde el catálogo
        //     (anti-tampering), aparta stock vía IReservationService.HoldItemAsync,
        //     abre UNA sesión de pago (IPaymentProvider) por el total; Confirm
        //     captura y confirma. Idempotente. Singleton — el estado de las órdenes
        //     (orderRef → líneas + sesión) vive en memoria del proceso.
        services.AddSingleton<IProductCatalogProvider, StubProductCatalogProvider>();
        services.AddSingleton<IShopOrderService, StubShopOrderService>();

        // OLA 3 Blogs — red social (doc blogs-app-spec). Seams stub-first, aditivos
        // (no tocan Booking/Travel/Shop). ADR 0002 (Application pura, sin Umbraco) +
        // ADR 0075 (seam con tests canónicos). Reusa ICommentRepository EXISTENTE
        // para los comentarios del post (BlogsController deriva un nodeId estable del
        // id string del post) — no se crea otro seam de comentarios.
        //   - IContentStream: ABSTRACCIÓN REUSABLE de feed/contenido (Actor-Verb-Object,
        //     ActivityStreams 2.0). Genérica por Kind (post|article|lesson…) para que
        //     EDUCACIÓN la reuse por polimorfismo (filtrando Kind=lesson) sin instanciar
        //     Blogs ni copiar su schema. El stub compone ISocialGraphService (feed
        //     "Siguiendo") + IReactionService (métricas) — DIP, no duplica estado.
        //   - ISocialGraphService: grafo dirigido asimétrico (follow/unfollow idempotente,
        //     followers/following + conteos). Estado en memoria del proceso.
        //   - IReactionService: reacciones/likes por item (toggle idempotente por
        //     (actor,objeto), conteos por tipo + estado-por-usuario). Estado en memoria.
        //   - ISocialProfileProjection: Member → perfil social (handle/bio/banner) para
        //     el header del perfil. Stub sobre el catálogo sembrado.
        // Singletons — el estado social (grafo/reacciones/posts creados) vive en el
        // proceso, igual que StubReactionService de healthcare/reservas. El stub de
        // ContentStream pide el StubReactionService concreto para leer conteos O(1):
        // registramos el concreto y lo exponemos bajo la interfaz (composición manual).
        services.AddSingleton<StubReactionService>();
        services.AddSingleton<IReactionService>(sp => sp.GetRequiredService<StubReactionService>());
        services.AddSingleton<ISocialGraphService, StubSocialGraphService>();
        services.AddSingleton<IContentStream>(sp =>
            new StubContentStream(
                sp.GetRequiredService<ISocialGraphService>(),
                sp.GetRequiredService<StubReactionService>()));
        services.AddSingleton<ISocialProfileProjection, StubSocialProfileProjection>();

        // OLA 4 Educación — LMS (doc educacion-app-spec). Dos seams stub-first,
        // aditivos (no tocan Booking/Travel/Shop/Blogs). ADR 0002 (Application pura,
        // sin Umbraco) + ADR 0075 (seam con tests canónicos).
        //   - ICourseCatalogProvider: catálogo buscable (texto/categoría/nivel →
        //     cursos) + detalle del curso (PDP: módulos → lecciones + instructor +
        //     planes de precio/cuotas). Stub: catálogo sembrado (3 categorías ×
        //     cursos × módulos × lecciones). Adapter real: Examine sobre coursePage.
        //     POLIMORFISMO Blogs: el contenido editorial de cada lección NO se
        //     duplica — se SIEMBRA en el IContentStream EXISTENTE con Kind=lesson y
        //     se referencia por id (DIP; depende de la abstracción de feed, no del
        //     módulo Blogs ni de su schema; no instancia Blogs). Por eso el catálogo
        //     recibe el IContentStream ya registrado arriba.
        //   - IEnrollmentService: motor transaccional de matrícula + progreso,
        //     calcando StubShopOrderService/StubReservationService. Resuelve el
        //     precio real desde el catálogo (anti-tampering), abre UNA sesión de
        //     pago (IPaymentProvider) si el curso es de pago o activa la matrícula
        //     de inmediato si es gratis; Confirm captura y activa. Añade lo propio
        //     del LMS: progreso por lección (idempotente) + certificado al 100%.
        // Singletons — el estado (matrículas + progreso + lecciones sembradas) vive
        // en el proceso, igual que el resto de stubs del motor.
        services.AddSingleton<ICourseCatalogProvider>(sp =>
            new StubCourseCatalogProvider(sp.GetRequiredService<IContentStream>()));
        services.AddSingleton<IEnrollmentService>(sp =>
            new StubEnrollmentService(
                sp.GetRequiredService<ICourseCatalogProvider>(),
                sp.GetRequiredService<IPaymentProvider>()));

        // Ola 59.1 — Boot-time guard: log Critical si CartSettings.SecretKey
        // sigue en su valor default bajo env != "Development". No detiene
        // el app; solo señala en el log para que el operador rote la clave
        // antes de exponer al público (ver ADR 0028 — TODO cerrado).
        services.AddHostedService<CartSecretKeyValidationHostedService>();

        // Ola 60 — Forms internal submission path (ADR 0030).
        // FileSystemFormSubmissionHandler escribe JSON por submission a
        // App_Data/syn-form-submissions/{formKey}/. Para producción de
        // volumen, swap por adapter sobre queue/email — la seam
        // IFormSubmissionHandler aisla al controller del storage.
        // InMemoryFormRateLimiter mantiene sliding window por (IP, formKey)
        // — singleton para que el estado persista entre requests del
        // mismo proceso.
        // Ola 115 — el FileSystem handler implementa AMBAS interfaces
        // (write + read). Composer registra una sola instancia bajo los
        // 2 contratos. Para adapters fire-and-forget (queue/webhook) el
        // arquitecto registra otro IFormSubmissionHandler y deja el
        // reader como NoOp o swap por DB-backed reader.
        services.AddSingleton<FileSystemFormSubmissionHandler>();
        services.AddSingleton<IFormSubmissionHandler>(sp => sp.GetRequiredService<FileSystemFormSubmissionHandler>());
        services.AddSingleton<IFormSubmissionReader>(sp => sp.GetRequiredService<FileSystemFormSubmissionHandler>());
        services.AddSingleton<InMemoryFormRateLimiter>();

        // Ola 61 — Search infrastructure (ADR 0031). ExamineSearchProvider
        // usa el ExternalIndex out-of-the-box de Umbraco (Examine 3.1.0)
        // y reproyecta los hits cruzando con el published cache para
        // resolver URL/cultura/siteRoot consistentes. Transient porque
        // depende de IUmbracoContextAccessor per-request.
        services.AddTransient<ISearchQuery, ExamineSearchProvider>();

        // Ola 86 — Search analytics store (ADR 0045). InMemory persiste
        // top queries + no-result queries en ConcurrentDictionary.
        // Singleton para compartir state entre requests del mismo proceso.
        services.AddSingleton<ISearchAnalyticsStore, InMemorySearchAnalyticsStore>();

        // Ola 64 — Member self-service (ADR 0034). DefaultMemberAuthService
        // wraps IMemberManager + IMemberSignInManager para Register/Login/
        // Logout/ChangePassword. AccountController + Razor templates
        // consumen este seam. Transient porque IMemberManager/SignInManager
        // son scoped (per-request).
        services.AddTransient<IMemberAuthService, DefaultMemberAuthService>();

        // Olas 144-145 — Member roster admin (ADR 0060). UmbracoMemberRosterReader
        // wraps IMemberService + IMemberGroupService para listing/filter de
        // Members en /admin/members. Transient porque depende de scoped
        // services Umbraco (IMemberService es scoped per-request).
        services.AddTransient<IMemberRosterReader, UmbracoMemberRosterReader>();

        // Olas 155-156 — Member roster writer split (lock/unlock) — ISP-clean.
        services.AddTransient<IMemberRosterWriter, UmbracoMemberRosterWriter>();

        // Olas 233-235 — GDPR RTBF coordinator (Cap-240 Batch B). Orquesta
        // hard-delete del Member + anonimización inline de comments y form
        // submissions (App_Data/syn-comments/, App_Data/syn-form-submissions/)
        // + audit terminal "gdpr.rtbf-processed". Transient — depende del
        // roster writer transient.
        services.AddTransient<IGdprRtbfCoordinator, FileSystemGdprRtbfCoordinator>();

        // Olas 153-154 — Audit trail (ADR 0066). FileSystemAuditTrailWriter
        // persiste eventos admin en App_Data/syn-audit/{yyyy-MM-dd}.jsonl.
        // Singleton — solo depende de IHostEnvironment + ILogger; concurrency
        // gestionada via lock interno.
        services.AddSingleton<IAuditTrailWriter, FileSystemAuditTrailWriter>();

        // Ola 162 + Cap-270 Batch B — Retention sweep generalizado
        // (ADRs 0070 + 0088). RetentionSweepHostedService itera todas
        // las IRetentionPolicy registradas cada 24h con try-catch
        // per-policy. Reemplaza al antiguo AuditRetentionHostedService
        // (deletado) extendiendo el pattern a 4 stores: audit + comments
        // (rejected) + form-submissions + search-analytics. Cada policy
        // opt-in via setting de retention days (0 = nunca purgar).
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.AuditRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.CommentsRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.FormSubmissionsRetentionPolicy>();
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.SearchAnalyticsRetentionPolicy>();
        // ADR 0097 D5 — retención de los archivos de checkouts del dashboard.
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.DashboardOrdersRetentionPolicy>();
        // ADR 0098 H3 — retención de registros clínicos PHI (6 años por defecto).
        services.AddSingleton<IRetentionPolicy, Synergos.CMS.Web.Services.Retention.HealthcareRetentionPolicy>();
        services.AddHostedService<Synergos.CMS.Web.Services.Retention.RetentionSweepHostedService>();

        // Olas 278-279 — SQLite maintenance pragmas (Cap-270 Batch D).
        // Cierra audit finding: WAL crece sin checkpoint reciente.
        // Auto-detect SQLite via ProviderName; NO-OP si SQL Server.
        services.AddHostedService<SqliteMaintenanceHostedService>();

        // Olas 165-166 — Webhook telemetry store (ADR 0071). Ring buffer
        // in-memory por canal con last 1000 outcomes. Singleton — thread-safe
        // via per-channel lock. Reset on restart (no persistencia).
        services.AddSingleton<IWebhookTelemetryStore, InMemoryWebhookTelemetryStore>();

        // Olas 195-196 + 236-237 + 254-256 — Telemetry alerts
        // (ADRs 0080 + 0085 + 0087). Scanner extraído del hosted service
        // (Cap-240) + Cap-260 Batch B refactor a Composite IAlertNotifier
        // paralelo a Comments/Forms/Cart. Channels: email (existente) +
        // slack + discord + teams + webhook (paralelo). Cada uno opt-in
        // por URL configurada — vacío = canal no-op silencioso.
        services.AddSingleton<IAlertNotifierChannel, EmailAlertNotifier>();
        services.AddHttpClient(WebhookAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, WebhookAlertNotifier>();
        services.AddHttpClient(SlackAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, SlackAlertNotifier>();
        services.AddHttpClient(DiscordAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, DiscordAlertNotifier>();
        services.AddHttpClient(TeamsAlertNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IAlertNotifierChannel, TeamsAlertNotifier>();
        services.AddSingleton<IAlertNotifier, CompositeAlertNotifier>();

        services.AddSingleton<WebhookTelemetryAlertScanner>();
        services.AddHostedService<WebhookTelemetryAlertHostedService>();

        // Ola 216 — Host bridge (ADR 0083). DefaultHostBridgeContextBuilder
        // arma el shape canónico de window.synergos consumed by UI components
        // via _SynergosBridge.cshtml partial. Transient — depende de scoped
        // services Umbraco.
        services.AddTransient<IHostBridgeContextBuilder, DefaultHostBridgeContextBuilder>();

        // Olas 178-180 + 221-224 — Member 2FA TOTP (ADRs 0074 + 0084).
        // FileSystemMemberTwoFactorStore persiste secrets en App_Data/syn-2fa/
        // {memberKey}.json encrypted via IDataProtectionProvider (Ola 221).
        // Service wraps Otp.NET para TOTP generation/verification.
        // QrCodeRenderer (singleton) renderiza el provisioning URI como SVG.
        services.AddSingleton<FileSystemMemberTwoFactorStore>();
        services.AddTransient<IMemberTwoFactorService, UmbracoMemberTwoFactorService>();
        services.AddSingleton<QrCodeRenderer>();

        // Ola 161 — Validator que warn al boot/reload si una key del
        // PerChannel dict no matchea ningún FactoryName conocido.
        services.AddSingleton<
            Microsoft.Extensions.Options.IValidateOptions<WebhookResilienceSettings>,
            WebhookResilienceSettingsValidator>();

        // Ola 65 — Email transaccional (ADR 0035). DefaultEmailService
        // wraps Umbraco.Cms.Core.Mail.IEmailSender — Umbraco gestiona
        // SMTP transport via Umbraco:CMS:Global:Smtp + pickup directory.
        // Singleton OK — solo depende de IEmailSender (singleton) +
        // IOptions + ILogger. Habilita password reset, email confirmation,
        // form notifications cuando se cableen los consumidores.
        services.AddSingleton<IEmailService, DefaultEmailService>();

        // Ola 82 — Email template rendering (ADR 0044). RazorEmailTemplateRenderer
        // permite a consumers (AccountController, FormSubmissionsController)
        // componer emails con branding consistente sin string concat.
        // Singleton — depende de IRazorViewEngine + ITempDataProvider +
        // IServiceProvider (todos singleton).
        services.AddSingleton<IEmailTemplateRenderer, RazorEmailTemplateRenderer>();

        // Ola 66 — Output cache para endpoints operacionales sitemap/RSS
        // (ADR 0036). IMemoryCache es estandar ASP.NET Core — registra
        // explicito por si Umbraco no lo cableo. Idempotente.
        services.AddMemoryCache();

        // Ola 67 — Analytics tracker (ADR 0037). LoggerAnalyticsTracker
        // emite eventos como log estructurado — el operador agrega via
        // su sink standard (Serilog/AI/Elastic). Singleton porque solo
        // depende de ILogger (singleton).
        //
        // ADR 0097 — Dashboard: el tracker se DECORA con ProjectingAnalyticsTracker,
        // que además proyecta cada evento (O(1), en memoria, sin IO) al
        // IMetricsProjectionStore que alimenta el panel. Umbraco 13 no trae
        // Scrutor → composición manual: registramos el inner concreto y lo
        // envolvemos. Los consumidores siguen inyectando IAnalyticsTracker.
        services.AddSingleton<LoggerAnalyticsTracker>();
        services.AddSingleton<InMemoryMetricsProjectionStore>();
        services.AddSingleton<IMetricsProjectionStore>(sp =>
            sp.GetRequiredService<InMemoryMetricsProjectionStore>());
        services.AddSingleton<IAnalyticsTracker>(sp =>
            new ProjectingAnalyticsTracker(
                sp.GetRequiredService<LoggerAnalyticsTracker>(),
                sp.GetRequiredService<IMetricsProjectionStore>(),
                sp.GetRequiredService<ILogger<ProjectingAnalyticsTracker>>()));

        // ADR 0097 — Captura explícita de checkouts (revenue, append-only)
        // + flush periódico de la proyección a JSONL (background, no bloquea
        // requests).
        services.AddSingleton<ICheckoutRecorder, FileSystemCheckoutRecorder>();
        services.AddHostedService<DashboardSnapshotFlushHostedService>();

        // ADR 0097 D2 — read-model compartido (/admin SSR + API Angular).
        // Scoped: IMemberRosterReader depende de servicios per-request de
        // Umbraco → no capturarlo en un singleton (captive dependency).
        services.AddScoped<IDashboardReadModel, DefaultDashboardReadModel>();

        // ADR 0097 D5 — export CSV de métricas (singleton; solo depende del store).
        services.AddSingleton<IMetricsExporter, DefaultMetricsExporter>();

        // ADR 0098 — Healthcare PHI (H1, núcleo de seguridad): store cifrado
        // atómico (IDataProtector) + libro de consentimientos + access-guard
        // fail-closed. El guard es Scoped porque IMemberAccessGate es per-request.
        services.AddSingleton<IPhiStore, FileSystemEncryptedPhiStore>();
        services.AddSingleton<IConsentLedger, FileSystemConsentLedger>();
        services.AddScoped<IPhiAccessGuard, DefaultPhiAccessGuard>();

        // ADR 0098 H2 — repositorio de historia clínica (versionado, sobre el PHI store).
        services.AddSingleton<IPatientRepository, FileSystemPatientRepository>();

        // ADR 0098 H2b — agenda de citas: lógica pura (Application) + scheduler Web
        // con lock async por-doctor (anti-overbooking) sobre el PHI store.
        services.AddSingleton<Synergos.CMS.Application.Services.AppointmentSchedulingService>();
        services.AddSingleton<IAppointmentScheduler, LockingAppointmentScheduler>();

        // ADR 0098 H2c — recetas (RECORD-KEEPER, sobre el PHI store).
        services.AddSingleton<IPrescriptionService, FileSystemPrescriptionService>();

        // ADR 0098 H3 — de-identificador PHI (lo invoca el coordinador RTBF
        // antes del hard-delete del Member).
        services.AddSingleton<IHealthcareDataAnonymizer, FileSystemHealthcareDataAnonymizer>();

        // OLA 5 Healthcare EHR-lite — dashboard clínico de DEMO (doc healthcare-app-spec).
        // Capa ADITIVA distinta del núcleo PHI de producción de arriba (ADR 0098):
        // sirve datos sembrados coherentes para la app Angular module-healthcare-ehr
        // (/api/ehr → EhrController). Seams stub-first, lógica pura (ADR 0002), tipos
        // prefijados por dominio (Ehr*/Clinical*/MedicalDoctor) para no colisionar con
        // los records de IPatientRepository/IAppointmentScheduler en el namespace.
        //   - IPatientRegistry / IDoctorDirectory: padrón + staff sembrados (memoria).
        //   - IClinicalRecordService: historia + encuentros (SOAP). REUSA IAuditTrailWriter
        //     (ADR 0037) — cada lectura/escritura de PHI emite un evento append-only.
        //   - IClinicalPrescriptionService: recetas por paciente (RECORD-KEEPER), idem audit.
        //   - IClinicalSchedulingService: agenda. REUSA el motor de reservas (cita = ítem
        //     reservable polimórfico): HoldItemAsync → copago opcional vía IPaymentProvider
        //     → ConfirmAsync (misma máquina Held→Confirmed de hoteles/aerolíneas).
        // Singletons — el estado (citas creadas, encuentros, recetas) vive en el proceso,
        // igual que el resto de stubs del motor.
        services.AddSingleton<IPatientRegistry, StubPatientRegistry>();
        services.AddSingleton<IDoctorDirectory, StubDoctorDirectory>();
        services.AddSingleton<IClinicalRecordService, StubClinicalRecordService>();
        services.AddSingleton<IClinicalPrescriptionService, StubClinicalPrescriptionService>();
        services.AddSingleton<IClinicalSchedulingService, StubClinicalSchedulingService>();

        // OLA 6 Eventos — plataforma de eventos enterprise (doc eventos-app-spec).
        // Tres seams stub-first, aditivos (no tocan Booking/Travel/Shop/Blogs/Educación/
        // Healthcare). ADR 0002 (Application pura, sin Umbraco) + ADR 0075 (tests
        // canónicos). REUSA el motor (no reinventa): cada asiento/cupo es un recurso
        // reservable polimórfico (Event×Tier×Seat), apartado vía IReservationService.
        // HoldItemAsync con UNA sesión IPaymentProvider — igual que TravelCartService/
        // StubShopOrderService. Tipos prefijados Event*/Ticket* para no colisionar.
        //   - IEventCatalogProvider: search (texto) + ficha (tiers + seat-map JSON
        //     compatible con synergos-seat-map). Stub: catálogo sembrado (4 eventos,
        //     mezcla general/reserved). Adapter real: Examine sobre eventPage / ticketing.
        //   - IEventTicketingService: checkout (resuelve precio/aforo real → HoldItemAsync
        //     por unidad → 1 PaymentSession por el total) + confirm (captura + e-tickets
        //     QR determinista). Idempotente por orderRef. Singleton — el estado (órdenes
        //     + tickets) vive en el proceso, igual que el resto de stubs del motor.
        //   - IEventManagementService: dashboard (asistentes/aforo/vendidos) + check-in
        //     idempotente. NO duplica estado: lee del StubEventTicketingService concreto
        //     por composición (DIP, mismo patrón que StubContentStream→StubReactionService),
        //     por eso registramos el concreto y lo exponemos bajo la interfaz.
        services.AddSingleton<IEventCatalogProvider, StubEventCatalogProvider>();
        services.AddSingleton<StubEventTicketingService>(sp =>
            new StubEventTicketingService(
                sp.GetRequiredService<IEventCatalogProvider>(),
                sp.GetRequiredService<IReservationService>(),
                sp.GetRequiredService<IPaymentProvider>()));
        services.AddSingleton<IEventTicketingService>(sp => sp.GetRequiredService<StubEventTicketingService>());
        services.AddSingleton<IEventManagementService>(sp =>
            new StubEventManagementService(
                sp.GetRequiredService<StubEventTicketingService>(),
                sp.GetRequiredService<IEventCatalogProvider>()));

        // OLA 7 Propiedades — portal inmobiliario (doc propiedades-app-spec).
        // Cuatro seams stub-first, aditivos (no tocan Booking/Travel/Shop/Blogs/
        // Educación/Healthcare/Eventos). ADR 0002 (Application pura, sin Umbraco) +
        // ADR 0075 (tests canónicos). Caso especial del marco: la transacción central
        // NO es un pago — es agendar visita + generar lead. REUSA el motor (no lo
        // reinventa) con el paso de pago DESACTIVADO, validando su generalidad.
        // Tipos prefijados Property*/Visit*/Mortgage*/Lead* para no colisionar.
        //   - IPropertyCatalogProvider: search facetado (texto/tipo/precio/hab/
        //     ubicación → listados + facetas) + ficha (specs + galería + geo). Stub:
        //     catálogo sembrado con geo (varias ciudades CO × tipos). Adapter real:
        //     Examine sobre propertyListing o API MLS. Singleton — stateless.
        //   - IVisitSchedulingService: agendar visita = recurso reservable
        //     POLIMÓRFICO (igual que habitación/asiento/médico). BookAsync llama
        //     IReservationService.HoldItemAsync → ConfirmAsync con PaymentSessionId
        //     neutro "visit-free" (la visita es gratis): demuestra el flujo
        //     seleccionar→[pagar]→confirmar con el paso de pago OFF. Idempotente por
        //     slot. Singleton — el estado de slots apartados vive en el proceso.
        //   - IMortgageCalculator: amortización francesa pura/determinista (sin
        //     estado). Singleton.
        //   - ILeadCaptureService: captura el lead (contactar agente). REUSA
        //     IAuditTrailWriter (ADR 0037) + IAnalyticsTracker (ADR 0067) — no crea
        //     seams nuevos. Singleton — el estado de leads vive en el proceso.
        services.AddSingleton<IPropertyCatalogProvider, StubPropertyCatalogProvider>();
        services.AddSingleton<IVisitSchedulingService>(sp =>
            new StubVisitSchedulingService(sp.GetRequiredService<IReservationService>()));
        services.AddSingleton<IMortgageCalculator, StubMortgageCalculator>();
        services.AddSingleton<ILeadCaptureService>(sp =>
            new StubLeadCaptureService(
                sp.GetRequiredService<IAuditTrailWriter>(),
                sp.GetRequiredService<IAnalyticsTracker>(),
                null));

        // Ola 68 — Comments runtime (ADR 0038). FileSystemCommentRepository
        // persiste un JSON por nodo (App_Data/syn-comments/{nodeId}.json).
        // Singleton — solo depende de IOptions + IHostEnvironment + ILogger.
        // Para concurrent-heavy o > 1000 comments por nodo, swap por adapter
        // sobre DB.
        services.AddSingleton<ICommentRepository, FileSystemCommentRepository>();

        // Olas 89 + 90 — Comment moderation notifier composite.
        // El consumer (CommentsController) inyecta ICommentModerationNotifier;
        // el composite default itera todos los ICommentModerationNotifierChannel
        // registrados (email + webhook). Cada canal es no-op si su settings
        // están vacíos, por lo que registrar ambos es seguro by default.
        services.AddSingleton<ICommentModerationNotifierChannel, EmailCommentModerationNotifier>();
        services.AddHttpClient(WebhookCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, WebhookCommentModerationNotifier>();
        services.AddHttpClient(SlackCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, SlackCommentModerationNotifier>();
        services.AddHttpClient(DiscordCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, DiscordCommentModerationNotifier>();
        services.AddHttpClient(TeamsCommentModerationNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICommentModerationNotifierChannel, TeamsCommentModerationNotifier>();
        services.AddSingleton<ICommentModerationNotifier, CompositeCommentModerationNotifier>();

        // Ola 91 — Form submission notifier composite (paralelo del de
        // comments). Reemplaza la lógica inline de email del controller
        // con un seam swappable; cada canal es no-op si su settings
        // están vacíos.
        services.AddSingleton<IFormSubmissionNotifierChannel, EmailFormSubmissionNotifier>();
        services.AddHttpClient(WebhookFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, WebhookFormSubmissionNotifier>();
        services.AddHttpClient(SlackFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, SlackFormSubmissionNotifier>();
        services.AddHttpClient(DiscordFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, DiscordFormSubmissionNotifier>();
        services.AddHttpClient(TeamsFormSubmissionNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<IFormSubmissionNotifierChannel, TeamsFormSubmissionNotifier>();
        services.AddSingleton<IFormSubmissionNotifier, CompositeFormSubmissionNotifier>();

        // Ola 102 — Cart abandonment notifier composite (paralelo de
        // comments + forms). Hook en CartAbandonmentScannerHostedService
        // por cart detectado. Email + webhook channels con HMAC opt-in.
        services.AddSingleton<ICartAbandonmentNotifierChannel, EmailCartAbandonmentNotifier>();
        services.AddHttpClient(WebhookCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, WebhookCartAbandonmentNotifier>();
        services.AddHttpClient(SlackCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, SlackCartAbandonmentNotifier>();
        services.AddHttpClient(DiscordCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, DiscordCartAbandonmentNotifier>();
        services.AddHttpClient(TeamsCartAbandonmentNotifier.FactoryName).AddWebhookResilience();
        services.AddSingleton<ICartAbandonmentNotifierChannel, TeamsCartAbandonmentNotifier>();
        services.AddSingleton<ICartAbandonmentNotifier, CompositeCartAbandonmentNotifier>();

        // Ola 41 — Flow engine runtime. FlowResolver queries the content
        // tree (Transient for the same per-request reason as theme). The
        // FlowController is picked up by AddControllers() above;
        // IHttpContextAccessor is registered by ASP.NET Core and is
        // consumed by the elementFlowProgress renderer to read the
        // syn-flow-{flowKey} cookie.
        services.AddTransient<FlowResolver>();
        services.AddHttpContextAccessor();

        // Ola 47 — Dev tooling: content seeder para smoke-test. Gated
        // por Synergos:DevSeed:Enabled=true en appsettings. El controller
        // /dev/seed-test-site invoca el seeder. Scope: solo crea/borra
        // el siteRoot "Test Site", nunca otros árboles.
        services.AddTransient<DevTestContentSeeder>();
        services.AddTransient<SynergosIdentitySeeder>();

        // Autoría server-side de contenido (Umbraco 13 no tiene Management
        // API). SchemaBlockDefaults siembra props multi-value editor-safe;
        // DevContentFiller puebla el BlockGrid de páginas existentes.
        services.AddTransient<SchemaBlockDefaults>();
        services.AddTransient<DevMediaFactory>();
        services.AddTransient<DevContentFiller>();

        // Health probes. Each registers as an ISchemaHealthProbe; the
        // HealthController resolves them as IEnumerable<ISchemaHealthProbe>.
        services.AddSingleton<ISchemaHealthProbe>(_ => new SchemaVersionProbe());
        services.AddSingleton<ISchemaHealthProbe>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var absolutePath = Path.Combine(env.ContentRootPath, "uSync", "v9");
            return new UsyncFolderProbe(absolutePath);
        });

        // Cap-280 Batch C — BundleRegistry probe (ADR 0089). Reporta el
        // estado del adapter activo: Stub healthy con mensaje informativo,
        // FileSystem resuelve un probe tag canónico contra el registry,
        // Unknown mode reporta unhealthy. Visible vía /_health.
        services.AddSingleton<ISchemaHealthProbe, BundleRegistryProbe>();

        // MVC controller discovery for attribute-routed endpoints
        // (HealthController, future controllers). AddControllers is
        // idempotent so calling it here is safe even if Umbraco already
        // registered MVC.
        services.AddControllers();

        // Notification handler: dictionary invalidation.
        builder.AddNotificationHandler<
            DictionaryItemSavedNotification,
            DictionaryCacheInvalidator>();
        builder.AddNotificationHandler<
            DictionaryItemDeletedNotification,
            DictionaryCacheInvalidator>();

        // Ola 42.5 — pre-fill Layout Preset blocks with sensible
        // defaults on first save so the editor doesn't face empty
        // dropdowns every time they drop a preset. See
        // LayoutPresetDefaults for the all-empty heuristic.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutPresetDefaults>();

        // Ola 42.7 — starter scaffold: opt-in via
        // Synergos:LayoutComposer:EnableStarterScaffold. Seeds a
        // minimal Hero + 2ColEven on first save of a blank pageBase.
        builder.AddNotificationHandler<
            ContentSavingNotification,
            LayoutComposerStarterScaffold>();

        // Ola 52.C — Member gating: cuando el routing resuelve a un
        // PublishedContent que compone compMemberGating con
        // requiresAuth=true, el handler verifica IMemberAccessGate y
        // redirige a /login con returnUrl si el miembro no califica.
        builder.AddNotificationHandler<
            RoutingRequestNotification,
            MemberGatingHandler>();
    }
}
