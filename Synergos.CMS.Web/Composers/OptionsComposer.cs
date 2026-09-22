using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Binds typed POCOs from <c>appsettings.*.json</c> into the DI
/// container so that Application services can consume them via
/// <c>IOptions&lt;T&gt;</c> / <c>IOptionsMonitor&lt;T&gt;</c> at the Web
/// composition boundary.
/// </summary>
/// <remarks>
/// Per ADR 0005 all composers live in
/// <c>Synergos.CMS.Web/Composers/</c>. Per ADR 0002 the Application
/// project does not reference <c>Microsoft.Extensions.Options</c>;
/// bindings are resolved here, and Web-level wiring (done in a future
/// composer — see Ola 3) extracts <c>.Value</c> and injects the POCO
/// into defaults such as <c>DefaultBrandingProvider</c>.
///
/// Option sections:
/// <list type="bullet">
///   <item><c>Synergos:Branding</c> → <see cref="BrandingSettings"/> (ADR 0010)</item>
///   <item><c>Synergos:FeatureFlags</c> → <see cref="FeatureFlagsSettings"/> (ADR 0011)</item>
/// </list>
/// </remarks>
public sealed class OptionsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<BrandingSettings>(
            builder.Config.GetSection("Synergos:Branding"));

        builder.Services.Configure<FeatureFlagsSettings>(
            builder.Config.GetSection("Synergos:FeatureFlags"));

        builder.Services.Configure<LayoutComposerSettings>(
            builder.Config.GetSection("Synergos:LayoutComposer"));

        builder.Services.Configure<CdnSettings>(
            builder.Config.GetSection("Synergos:Cdn"));

        builder.Services.Configure<DevSeedSettings>(
            builder.Config.GetSection("Synergos:DevSeed"));

        builder.Services.Configure<MembersSettings>(
            builder.Config.GetSection("Synergos:Members"));

        builder.Services.Configure<CartSettings>(
            builder.Config.GetSection("Synergos:Cart"));

        builder.Services.Configure<CartAbandonmentSettings>(
            builder.Config.GetSection("Synergos:CartAbandonment"));

        builder.Services.Configure<FormsSettings>(
            builder.Config.GetSection("Synergos:Forms"));

        builder.Services.Configure<SearchSettings>(
            builder.Config.GetSection("Synergos:Search"));

        // T5 (ADR 0107) — motor de catálogo: topes de paginación, de qué fuente sale el
        // catálogo de cada vertical (demo|cms) y bajo qué siteRoot vive (por brandKey).
        builder.Services.Configure<CatalogSettings>(
            builder.Config.GetSection("Synergos:Catalog"));

        builder.Services.Configure<EmailSettings>(
            builder.Config.GetSection("Synergos:Email"));

        builder.Services.Configure<OutputCacheSettings>(
            builder.Config.GetSection("Synergos:OutputCache"));

        builder.Services.Configure<CommentsSettings>(
            builder.Config.GetSection("Synergos:Comments"));

        // Persistencia durable genérica (doc 25 — T1/T3/Booking): UN store para todas
        // las familias de entidades JSON (órdenes, pagos, reservas, viajes). Reemplaza a
        // los 4 POCOs dedicados que cada store tenía.
        builder.Services.Configure<JsonEntityStoreSettings>(
            builder.Config.GetSection("Synergos:JsonEntityStore"));

        // T6 (doc 25) — almacén de ficheros privados: raíz de almacenamiento + límites.
        // El default de StorageRoot ("App_Data/") es lo que mantiene los documentos
        // FUERA del alcance de los ficheros estáticos; apuntarlo a wwwroot los haría
        // públicos a todos.
        builder.Services.Configure<PrivateFileStoreSettings>(
            builder.Config.GetSection("Synergos:PrivateFileStore"));

        // T9 (doc 25) — secreto con el que se firma el QR de las entradas. Vacío NO es
        // "no firmar": el host genera y persiste una llave (ver TicketSigningKeyProvider).
        //
        // La sección va ANIDADA bajo la del vertical (#154). Antes era `Synergos:Events`,
        // hermana de `Synergos:Eventos` —la del eje 2— o sea a UNA letra de distancia; y un
        // dedazo entre las dos no falla, porque el binder descarta en silencio lo que no mapea.
        // El razonamiento entero está en TicketSettings.
        ExigirQueNadieUseLaSeccionRetirada(builder.Config);
        builder.Services.Configure<TicketSettings>(
            builder.Config.GetSection("Synergos:Eventos:Ticket"));

        // ADR 0124 — secreto con el que se deriva el id de los certificados de Educación.
        // Vacío NO es "no firmar": el host genera y persiste una llave cifrada (ver
        // CertificateSigningKeyProvider). Sección propia y NO la de Eventos: rotar el
        // secreto de las entradas no puede invalidar los diplomas.
        builder.Services.Configure<AcademySettings>(
            builder.Config.GetSection("Synergos:Academy"));

        // T3 (doc 25) — selección/gating del proveedor de pago + secreto del webhook.
        builder.Services.Configure<PaymentsSettings>(
            builder.Config.GetSection("Synergos:Payments"));

        // T4 (doc 25) — notificaciones de hechos transaccionales (recibo, viaje,
        // entradas, matrícula, radicado, decisión). Enabled=false por default: opt-in.
        builder.Services.Configure<NotificationsSettings>(
            builder.Config.GetSection("Synergos:Notifications"));

        builder.Services.Configure<AdminSettings>(
            builder.Config.GetSection("Synergos:Admin"));

        // Olas 195-196 — Webhook telemetry alerts (ADR 0080).
        builder.Services.Configure<WebhookTelemetryAlertSettings>(
            builder.Config.GetSection("Synergos:Admin:WebhookTelemetryAlerts"));

        // Ola 216 — Host bridge (ADR 0083). Tuning de window.synergos.
        builder.Services.Configure<HostBridgeSettings>(
            builder.Config.GetSection("Synergos:HostBridge"));

        // Olas 257-258 — Data Protection multi-instance keyring (ADR 0087).
        // Vacío default = preserva comportamiento per-instance (no breaking).
        builder.Services.Configure<DataProtectionSettings>(
            builder.Config.GetSection("Synergos:DataProtection"));

        // Olas 273-275 — Retention policies generalizadas (ADR 0088).
        // 0 = nunca purgar (operador gestiona manualmente).
        builder.Services.Configure<RetentionSettings>(
            builder.Config.GetSection("Synergos:Retention"));

        // Olas 278-279 — SQLite maintenance pragmas (ADR 0088 Batch D).
        // Activo cuando Umbraco usa SQLite — no-op silent en SQL Server.
        builder.Services.Configure<SqliteMaintenanceSettings>(
            builder.Config.GetSection("Synergos:SqliteMaintenance"));

        // Olas 281-282 — Local CDN static files endpoint (ADR 0089).
        // Default Enabled=false: solo se monta si el operador configura
        // explícitamente el LocalPath + flag.
        builder.Services.Configure<LocalCdnSettings>(
            builder.Config.GetSection("Synergos:LocalCdn"));

        // Olas 283-285 — Bundle registry client (ADR 0089 Batch B).
        // Mode={Stub|FileSystem|Http} controla qué adapter resuelve los
        // bundles UI. Settings detallados via Synergos:BundleRegistry.
        builder.Services.Configure<BundleRegistrySettings>(
            builder.Config.GetSection("Synergos:BundleRegistry"));

        // ADR 0097 — Dashboard de métricas. Flush + retención de la
        // proyección pre-agregada. Default Enabled=true; el panel/identidad
        // viven en cfgDashboardSettings (uSync) + la app Angular.
        builder.Services.Configure<DashboardSettings>(
            builder.Config.GetSection("Synergos:Dashboard"));

        // ADR 0098 — Healthcare (vertical clínico PHI): disclaimer, zona horaria,
        // retención, 2FA de staff.
        builder.Services.Configure<HealthcareSettings>(
            builder.Config.GetSection("Synergos:Healthcare"));

        // ADR 0027 — Blog: tamaño de página de categoría + posts relacionados.
        builder.Services.Configure<BlogSettings>(
            builder.Config.GetSection("Synergos:Blog"));
    }
    /// <summary>
    /// Falla AL CABLEAR si el despliegue todavía puebla la sección retirada del #154, o su
    /// vecina plana.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué hace falta, y por qué al arrancar.</b> Renombrar una sección de
    /// configuración deja huérfano lo que un servidor ya tiene puesto, y eso <b>no falla</b>: el
    /// binder descarta la clave vieja, el POCO se queda en su default —cadena vacía, que aquí
    /// significa «genera una llave y guárdala»— y el operador cree que rotó el secreto cuando no
    /// tocó nada. El síntoma llega meses después, el día que hay dos instancias con volúmenes
    /// distintos y un QR emitido por una no valida en la otra.</para>
    ///
    /// <para><b>Se mira la sección ENTERA y no sólo la clave que se movió.</b> Lo que quedó
    /// retirado es <c>Synergos:Events</c> completa, así que cualquier cosa debajo es una clave que
    /// nadie lee; enumerar sólo <c>TicketSigningSecret</c> dejaría pasar el typo que no se me
    /// ocurra hoy. Y se añade a mano <c>Synergos:Eventos:TicketSigningSecret</c> —la vecina
    /// PLANA— porque es lo que teclea quien acaba de leer un documento viejo y corrige la letra:
    /// vive bajo la sección correcta, así que el barrido de la retirada no la ve.</para>
    ///
    /// <para>Es la forma del #56 y la de la llave de firma de <c>Api.Identity</c>: arrancar
    /// verde, contestar <c>/health</c> y desmentirse delante de alguien es el peor de los tres
    /// modos de fallar.</para>
    /// </remarks>
    internal static void ExigirQueNadieUseLaSeccionRetirada(IConfiguration config)
    {
        const string Retirada = "Synergos:Events";
        const string VecinaPlana = "Synergos:Eventos:TicketSigningSecret";

        var puestas = config.GetSection(Retirada)
            .AsEnumerable(makePathsRelative: false)
            .Where(par => !string.IsNullOrWhiteSpace(par.Value))
            .Select(par => par.Key)
            .ToList();

        if (!string.IsNullOrWhiteSpace(config[VecinaPlana]))
        {
            puestas.Add(VecinaPlana);
        }

        if (puestas.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Configuración retirada en el #154: " + string.Join(", ", puestas.Order(StringComparer.Ordinal))
            + ". El secreto de firma de las entradas vive ahora en "
            + "`Synergos:Eventos:Ticket:SigningSecret` (variable de entorno "
            + "`Synergos__Eventos__Ticket__SigningSecret`). Se para al arrancar a propósito: dejar "
            + "la clave vieja puesta NO falla —el binder la descarta y el host genera otra llave—, "
            + "así que el QR de las entradas ya emitidas dejaría de validar en la instancia de al "
            + "lado sin que nada lo dijera.");
    }

}
