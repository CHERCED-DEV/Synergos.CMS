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
    private void ComposePlatformServicesAndHealthcare(IUmbracoBuilder builder)
    {
        var services = builder.Services;

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

        // T6 — almacén de ficheros PRIVADOS (bytes subidos por usuarios): cifrado
        // at-rest y bajo App_Data/, donde NINGÚN middleware de estáticos llega. Es el
        // contrapeso de wwwroot/media, que es público por construcción: ahí van las
        // fotos de producto, aquí la cédula del ciudadano. Quien sirva estos bytes
        // comprueba permiso en cada descarga (el id opaco no es la autorización).
        services.AddSingleton<IPrivateFileStore, FileSystemPrivateFileStore>();

        // T9 — firma del QR de las entradas. La llave sale del secreto configurado o, si
        // no hay, se genera una vez y se guarda CIFRADA en el almacén de arriba: así el
        // QR sobrevive un reinicio (el bug que T9 corrige era justo lo contrario) sin
        // meter ningún secreto en el repo.
        // T7 — realtime por SSE (ADR 0111). UN hub transversal: los verticales publican
        // en canales y no saben del transporte. Fan-out EN PROCESO (un deploy = un origen).
        services.AddSingleton<SseRealtimeHub>();
        services.AddSingleton<IRealtimeNotifier>(sp => sp.GetRequiredService<SseRealtimeHub>());

        services.AddSingleton<TicketSigningKeyProvider>();
        services.AddSingleton<ITicketSigner, LazyTicketSigner>();

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
        // HU #25 — contra quién agenda la cita. Dos orígenes, mismo contrato, elegidos por
        // Synergos:Salud:Mode:
        //   - Stub (default): el motor en proceso. Reserva el cupo con IReservationService,
        //     cobra el copago y confirma — o sea, REIMPLEMENTA una saga del lado del CMS.
        //   - Bff: contra Synergos.Bff.Salud, que ya tiene ese orden y sus compensaciones.
        //
        // Api.Booking no sabe que el recurso es un médico y no puede saberlo (CLAUDE.md §12):
        // el sustantivo «doctor» vive acá y en el BFF, nunca en la capacidad. Lo vigila
        // SaludWiringTests.
        services.Configure<SaludSettings>(builder.Config.GetSection("Synergos:Salud"));

        if (string.Equals(builder.Config["Synergos:Salud:Mode"], "Bff", StringComparison.OrdinalIgnoreCase))
        {
            var saludBase = builder.Config["Synergos:Salud:BaseUrl"];
            var saludKey = builder.Config["Synergos:Salud:ApiKey"];
            var saludTimeout = int.TryParse(builder.Config["Synergos:Salud:TimeoutSeconds"], out var st) && st > 0 ? st : 30;

            services.AddHttpClient(HttpClinicalSchedulingService.BffClientName, http =>
            {
                var url = string.IsNullOrWhiteSpace(saludBase) ? "http://127.0.0.1:5301/" : saludBase;
                http.BaseAddress = new Uri(url.EndsWith('/') ? url : url + "/");
                http.Timeout = TimeSpan.FromSeconds(saludTimeout);
                if (!string.IsNullOrWhiteSpace(saludKey))
                {
                    http.DefaultRequestHeaders.Add(HttpClinicalSchedulingService.ApiKeyHeader, saludKey);
                }
            })
            // El hilo de la correlación cruza al árbol de servicios (HU #28). Va sobre los
            // clientes NOMBRADOS y no sobre uno global: los webhooks salen a terceros, y a un
            // tercero conviene mandarle lo mínimo.
            .AddHttpMessageHandler<CorrelationForwardingHandler>();
            services.AddSingleton<IClinicalSchedulingService, HttpClinicalSchedulingService>();
        }
        else
        {
            services.AddSingleton<IClinicalSchedulingService, StubClinicalSchedulingService>();
        }

        // OLA 7 Healthcare EHR-lite (doc 21 §2.5) — DOS portales de un mismo grafo
        // clínico. Seams NUEVOS, aditivos (no tocan el vertical Healthcare de
        // PRODUCCIÓN de ADR 0098 ni los seams EHR-lite de OLA 5 de arriba). Todos
        // reusan lo existente y auditan PHI vía IAuditTrailWriter (ADR 0037):
        //   - IClinicalResultsProvider: labs por paciente (valor/rango/flag). Semilla
        //     coherente con la historia (Jorge diabético→HbA1c alta; Valentina
        //     hipotiroidea→TSH alta). El order-entry lo ALIMENTA (bucle order→result).
        //   - IClinicalMedicationService: medicación activa DERIVADA de las recetas
        //     vivas (DIP, compone IClinicalPrescriptionService) + refill que enruta al
        //     In Basket del médico vía IMessagingService (contexto 'clinical', genérico
        //     Ola 1 reusado). El concreto expone GetPendingRefillsForProvider para que
        //     el In Basket lea las solicitudes por composición.
        //   - IClinicalOrderService: order-entry (lab/eRx/imaging/referral); una orden
        //     de lab libera un resultado coherente (compone IClinicalResultsProvider).
        //   - IClinicalBillingService: estado de cuenta + saldo + plan, sembrado.
        //   - IEhrInBasketService: cola tipada del proveedor (result|refill|message)
        //     que DERIVA de resultados + refills + mensajería — sin store paralelo
        //     (compone el StubClinicalMedicationService concreto, mismo patrón que
        //     StubContentStream→StubReactionService).
        // Singletons — el estado (resultados/refills/órdenes creados) vive en el
        // proceso, igual que el resto de stubs del motor.
        services.AddSingleton<IClinicalResultsProvider, StubClinicalResultsProvider>();
        services.AddSingleton<StubClinicalMedicationService>(sp =>
            new StubClinicalMedicationService(
                sp.GetRequiredService<IClinicalPrescriptionService>(),
                sp.GetRequiredService<IMessagingService>(),
                sp.GetRequiredService<IAuditTrailWriter>()));
        services.AddSingleton<IClinicalMedicationService>(sp => sp.GetRequiredService<StubClinicalMedicationService>());
        services.AddSingleton<IClinicalOrderService>(sp =>
            new StubClinicalOrderService(
                sp.GetRequiredService<IDoctorDirectory>(),
                sp.GetRequiredService<IClinicalResultsProvider>(),
                sp.GetRequiredService<IAuditTrailWriter>()));
        services.AddSingleton<IClinicalBillingService, StubClinicalBillingService>();
        services.AddSingleton<IEhrInBasketService>(sp =>
            new StubEhrInBasketService(
                sp.GetRequiredService<IClinicalResultsProvider>(),
                sp.GetRequiredService<StubClinicalMedicationService>(),
                sp.GetRequiredService<IMessagingService>(),
                sp.GetRequiredService<IPatientRegistry>()));

    }
}
