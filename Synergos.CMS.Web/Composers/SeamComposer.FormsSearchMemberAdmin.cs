using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    private void ComposeFormsSearchAndMemberAdmin(IUmbracoBuilder builder)
    {
        var services = builder.Services;

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

        // Definición del formulario (qué campos declaró el autor), para que el endpoint de
        // envío pueda EXIGIR los obligatorios. Singleton como el resto: no guarda estado, lee
        // el published cache en cada llamada vía IUmbracoContextAccessor.
        services.AddSingleton<IFormDefinitionReader, UmbracoFormDefinitionReader>();

        // Ola 61 — Search infrastructure (ADR 0031). ExamineSearchProvider
        // usa el ExternalIndex out-of-the-box de Umbraco (Examine 3.1.0)
        // y reproyecta los hits cruzando con el published cache para
        // resolver URL/cultura/siteRoot consistentes. Transient porque
        // depende de IUmbracoContextAccessor per-request.
        services.AddTransient<ISearchQuery, ExamineSearchProvider>();

        // Ola 86 — Search analytics store (ADR 0045 + ADR 0130). Dos orígenes, mismo
        // contrato, elegidos por Synergos:SearchAnalytics:Mode:
        //   - FileSystem (default): JSONL en App_Data. El CMS carga con su analítica.
        //   - Sessions: la delega al servicio Synergos.Api.Sessions por HTTP.
        //
        // El default es FileSystem a propósito: un clon recién bajado arranca sin
        // depender de otro proceso. Encender "Sessions" sin el servicio arriba degrada
        // —el dashboard sale vacío— pero no tumba el CMS.
        var searchAnalyticsMode = builder.Config["Synergos:SearchAnalytics:Mode"] ?? "FileSystem";
        if (string.Equals(searchAnalyticsMode, "Sessions", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = builder.Config["Synergos:SearchAnalytics:BaseUrl"];
            var apiKey = builder.Config["Synergos:SearchAnalytics:ApiKey"];
            services.AddHttpClient(HttpSearchAnalyticsStore.HttpClientName, http =>
            {
                http.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl)
                    ? "http://127.0.0.1:5200/"
                    : baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                // Timeout corto: este servicio es auxiliar. Si tarda, el dashboard prefiere
                // salir vacío antes que dejar la petición colgada.
                http.Timeout = TimeSpan.FromSeconds(5);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    http.DefaultRequestHeaders.Add(HttpSearchAnalyticsStore.ApiKeyHeader, apiKey);
                }
            })
            // El hilo de la correlación (HU #28). Es el consumidor MÁS VIEJO del árbol de
            // servicios y llevaba desde entonces sin rastro compartido.
            .AddHttpMessageHandler<CorrelationForwardingHandler>();
            // La MISMA instancia sirve la seam y el hosted service: el lazo de envío vive en
            // ella. Dos registros independientes darían dos colas y una sin drenar.
            services.AddSingleton<HttpSearchAnalyticsStore>();
            services.AddSingleton<ISearchAnalyticsStore>(sp => sp.GetRequiredService<HttpSearchAnalyticsStore>());
            services.AddHostedService(sp => sp.GetRequiredService<HttpSearchAnalyticsStore>());
        }
        else
        {
            services.AddSingleton<ISearchAnalyticsStore, FileSystemSearchAnalyticsStore>();
        }

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
        services.AddSingleton<FileSystemAuditTrailWriter>();

        // La sección se ENLAZA: sin esto el escritor recibe un AuditSettings recién construido y
        // lo que no viaja por el HttpClient —los Kind del actor y del recurso— se queda en su
        // default en silencio. Es el olvido que arrastraron Tienda (#24), Salud (#25),
        // Viajes (#36) y las notificaciones de Gobierno (#62).
        services.Configure<AuditSettings>(builder.Config.GetSection("Synergos:Audit"));

        // HU #15 — el asiento también sale de esta máquina, si el despliegue lo enciende.
        //
        // El JSONL NO se sustituye: envuelve. Las lecturas del seam son síncronas y la bitácora
        // del backoffice se pinta en cada carga, así que sigue siendo el modelo de lectura con la
        // capacidad encendida — con Api.Audit caída el administrador SIGUE viendo qué pasó, y lo
        // que se para es que el asiento salga de acá. Es la forma del timeline de pedidos (#46).
        if (string.Equals(builder.Config["Synergos:Audit:Mode"], "Api", StringComparison.OrdinalIgnoreCase))
        {
            var auditBase = builder.Config["Synergos:Audit:BaseUrl"];
            var auditKey = builder.Config["Synergos:Audit:ApiKey"];
            var auditTimeout = int.TryParse(
                builder.Config["Synergos:Audit:TimeoutSeconds"], out var at) && at > 0 ? at : 5;

            services.AddHttpClient(HttpAuditTrailWriter.ClientName, http =>
            {
                var url = string.IsNullOrWhiteSpace(auditBase) ? "http://127.0.0.1:5222/" : auditBase;
                http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                http.Timeout = TimeSpan.FromSeconds(auditTimeout);
                if (!string.IsNullOrWhiteSpace(auditKey))
                {
                    http.DefaultRequestHeaders.Add(HttpAuditTrailWriter.ApiKeyHeader, auditKey);
                }
            })
            .AddHttpMessageHandler<CorrelationForwardingHandler>();

            services.AddSingleton<IAuditTrailWriter>(sp => new HttpAuditTrailWriter(
                sp.GetRequiredService<FileSystemAuditTrailWriter>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IOptionsMonitor<AuditSettings>>(),
                sp.GetRequiredService<ILogger<HttpAuditTrailWriter>>()));
        }
        else
        {
            services.AddSingleton<IAuditTrailWriter>(sp => sp.GetRequiredService<FileSystemAuditTrailWriter>());
        }

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

    }
}
