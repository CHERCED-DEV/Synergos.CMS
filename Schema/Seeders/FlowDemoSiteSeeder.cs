using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// Creates a dedicated demo site root for the Synergos Flow Engine walkthrough.
///
/// Idempotent: checks for existing nodes before creating.
/// Runs on every startup (not just schema changes).
///
/// Target tree:
///   PlatformRoot
///   └── Flow Engine Demo (siteRoot)
///       ├── SiteSettings
///       ├── ThemeSettings
///       ├── Intro al Flow Engine         (pageBase — overview + 3 features)
///       ├── Paso 1: Flujos en Umbraco    (pageBase — list flows in CMS)
///       ├── Paso 2: Registrar en Engine  (pageBase — publish flow to API)
///       ├── Paso 3: Ejecutar un Flujo    (pageBase — run a flow)
///       ├── Paso 4: Cambiar el Modo      (pageBase — change executionMode in backoffice)
///       └── Paso 5: Ver Resultados       (pageBase — execution history)
/// </summary>
internal sealed class FlowDemoSiteSeeder
{
    private readonly IContentService     _content;
    private readonly IContentTypeService _contentTypes;
    private readonly IFileService        _files;
    private readonly ILogger             _logger;

    private const string BgEditorAlias   = "Umbraco.BlockGrid";
    private const string PageSectionsKey = "pageSections";
    private const string SeedCulture     = "es-CO";

    // ── Layout Preset area GUIDs (must match DTBlockGridPageSections DataType config) ──
    private static readonly Guid A1Main  = new("fa010001-0000-0000-0000-000000000000");
    private static readonly Guid A2Left  = new("fa010002-0000-0000-0000-000000000000");
    private static readonly Guid A2Right = new("fa010003-0000-0000-0000-000000000000");
    private static readonly Guid A3Col1  = new("fa010004-0000-0000-0000-000000000000");
    private static readonly Guid A3Col2  = new("fa010005-0000-0000-0000-000000000000");
    private static readonly Guid A3Col3  = new("fa010006-0000-0000-0000-000000000000");

    // ── Content Type Keys (D-format) ─────────────────────────────────────────
    private const string T1Col      = "c3000010-0000-0000-0000-000000000000"; // LayoutPreset1Col
    private const string T2Col      = "c3000011-0000-0000-0000-000000000000"; // LayoutPreset2ColEqual
    private const string T3Col      = "c3000012-0000-0000-0000-000000000000"; // LayoutPreset3ColEqual
    private const string THeading   = "c4000001-0000-0000-0000-000000000000"; // ElementTextHeading
    private const string TParagraph = "c4000002-0000-0000-0000-000000000000"; // ElementTextParagraph
    private const string TFeature   = "c7000003-0000-0000-0000-000000000000"; // ElementInfoFeature
    private const string TKeyValue  = "c7000004-0000-0000-0000-000000000000"; // ElementInfoKeyValue
    private const string THero      = "c8000002-0000-0000-0000-000000000000"; // ElementCompHero
    private const string TCtaBanner = "c8000004-0000-0000-0000-000000000000"; // ElementCompCtaBanner

    public FlowDemoSiteSeeder(
        IContentService     content,
        IContentTypeService contentTypes,
        IFileService        files,
        ILogger             logger)
    {
        _content      = content;
        _contentTypes = contentTypes;
        _files        = files;
        _logger       = logger;
    }

    public void Seed()
    {
        var platformRoot = _content.GetRootContent()
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.PlatformRoot);

        if (platformRoot is null)
        {
            _logger.LogWarning(
                "FlowDemoSiteSeeder: 'platformRoot' not found — skipping demo site creation.");
            return;
        }

        var demoSite = GetOrCreateDemoSiteRoot(platformRoot.Id);
        if (demoSite is null) return;

        EnsureSettings(demoSite.Id);
        EnsureDemoPages(demoSite.Id);
        EnsureNavigation(demoSite.Id);

        _logger.LogInformation("FlowDemoSiteSeeder: demo site seeded (Id={Id}).", demoSite.Id);
    }

    // ─── Site root ────────────────────────────────────────────────────────────

    private IContent? GetOrCreateDemoSiteRoot(int platformId)
    {
        var children = _content.GetPagedChildren(platformId, 0, 100, out _);
        var existing = children.FirstOrDefault(c =>
            c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot &&
            (c.GetValue<string>("siteName") ?? c.Name) == "Flow Engine Demo");

        if (existing is not null)
        {
            _logger.LogDebug("FlowDemoSiteSeeder: demo site root already exists (Id={Id}).", existing.Id);
            return existing;
        }

        var siteRootType = _contentTypes.Get(ContentTypeKeys.Aliases.SiteRoot);
        if (siteRootType is null)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: contentType 'siteRoot' not found.");
            return null;
        }

        var node = _content.Create("Flow Engine Demo", platformId, ContentTypeKeys.Aliases.SiteRoot);
        node.SetValue("siteName",    "Flow Engine Demo");
        node.SetValue("siteTagline", "Demostración paso a paso del Synergos Flow Engine");

        var template = _files.GetTemplate("SiteRoot");
        if (template is not null) node.TemplateId = template.Id;

        var result = _content.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success)
        {
            _logger.LogWarning(
                "FlowDemoSiteSeeder: failed to publish demo site root — {Status}.", result.Result);
            return null;
        }

        _logger.LogInformation("FlowDemoSiteSeeder: demo site root published (Id={Id}).", node.Id);
        return node;
    }

    // ─── Settings nodes ───────────────────────────────────────────────────────

    private void EnsureSettings(int siteId)
    {
        EnsureChild(siteId, ContentTypeKeys.Aliases.SiteSettingsAlias, "Site Settings",
            "SiteSettings", null);

        EnsureChild(siteId, ContentTypeKeys.Aliases.ThemeSettings, "Theme Settings",
            "ThemeSettings", null);
    }

    // ─── Demo pages ───────────────────────────────────────────────────────────

    private void EnsureDemoPages(int siteId)
    {
        var intro = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Intro al Flow Engine", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Intro al Flow Engine",                               culture: SeedCulture);
                node.SetValue("pageSubtitle",   "Qué es el Synergos Flow Engine y cómo funciona",    culture: SeedCulture);
                node.SetValue("seoTitle",       "Flow Engine Demo — Intro");
                node.SetValue("seoDescription", "El Flow Engine es la capa de orquestación de Synergos. Umbraco define los flujos; Synergos.API los ejecuta.");
                node.SortOrder = 0;
            });
        SeedGridIfEmpty(intro, BuildIntroGrid);

        var paso1 = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Paso 1: Flujos en Umbraco", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Paso 1: Flujos en Umbraco",                         culture: SeedCulture);
                node.SetValue("pageSubtitle",   "Revisa los FlowDefinition nodes en el backoffice",  culture: SeedCulture);
                node.SetValue("seoTitle",     "Flow Engine Demo — Paso 1");
                node.SetValue("seoDescription", "Navega a Content → Flow Settings Root. Verás approval-flow y notification-pipeline ya configurados.");
                node.SortOrder = 10;
            });
        SeedGridIfEmpty(paso1, BuildPaso1Grid);

        var paso2 = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Paso 2: Registrar en Engine", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Paso 2: Registrar en Engine",                       culture: SeedCulture);
                node.SetValue("pageSubtitle",   "Publica un flujo desde CMS hacia Synergos.API",    culture: SeedCulture);
                node.SetValue("seoTitle",     "Flow Engine Demo — Paso 2");
                node.SetValue("seoDescription", "POST /api/synergos/v1/orchestration/flows/approval-flow/publish firma el payload con HMAC-SHA256 y lo envía al engine.");
                node.SortOrder = 20;
            });
        SeedGridIfEmpty(paso2, BuildPaso2Grid);

        var paso3 = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Paso 3: Ejecutar un Flujo", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Paso 3: Ejecutar un Flujo",                         culture: SeedCulture);
                node.SetValue("pageSubtitle",   "Llama al engine con un payload de prueba",          culture: SeedCulture);
                node.SetValue("seoTitle",     "Flow Engine Demo — Paso 3");
                node.SetValue("seoDescription", "POST /api/engine/flows/approval-flow/execute con caseId y input. El engine corre los tracks y devuelve el outcome.");
                node.SortOrder = 30;
            });
        SeedGridIfEmpty(paso3, BuildPaso3Grid);

        var paso4 = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Paso 4: Cambiar el Modo", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Paso 4: Cambiar el Modo",                           culture: SeedCulture);
                node.SetValue("pageSubtitle",   "El momento demo: cambia executionMode en el backoffice", culture: SeedCulture);
                node.SetValue("seoTitle",     "Flow Engine Demo — Paso 4");
                node.SetValue("seoDescription", "Cambia approval-flow de sequential a parallel en el backoffice. Republica. El mismo execute ahora corre los tracks en simultáneo.");
                node.SortOrder = 40;
            });
        SeedGridIfEmpty(paso4, BuildPaso4Grid);

        var paso5 = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase,
            "Paso 5: Ver Resultados", "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      "Paso 5: Ver Resultados",                            culture: SeedCulture);
                node.SetValue("pageSubtitle",   "Consulta el historial de ejecuciones",              culture: SeedCulture);
                node.SetValue("seoTitle",     "Flow Engine Demo — Paso 5");
                node.SetValue("seoDescription", "GET /api/engine/executions muestra las últimas ejecuciones. GET /api/engine/health confirma el estado del engine.");
                node.SortOrder = 50;
            });
        SeedGridIfEmpty(paso5, BuildPaso5Grid);
    }

    // ─── Block Grid seeders ───────────────────────────────────────────────────

    private void SeedGridIfEmpty(IContent? page, Func<string> buildGrid)
    {
        if (page is null) return;

        // Skip ONLY if the PUBLISHED version already has block content.
        // Use published:true to read the published property value, not the draft.
        // This correctly handles the case where Save succeeded but Publish failed:
        //   draft has pageSections, published is empty → seed again.
        // After a successful SaveAndPublish both are identical, so the guard is safe.
        if (page.Published)
        {
            var publishedVal = page.GetValue<string>(PageSectionsKey, culture: SeedCulture, published: true);
            if (!string.IsNullOrWhiteSpace(publishedVal) && publishedVal.Contains("\"contentUdi\"")) return;
        }

        try
        {
            page.SetValue(PageSectionsKey, buildGrid(), culture: SeedCulture);
            var result = _content.SaveAndPublish(page, userId: UmbConstants.Security.SuperUserId);
            if (result.Success)
                _logger.LogInformation("FlowDemoSiteSeeder: seeded grid for '{Name}'.", page.Name);
            else
                _logger.LogWarning("FlowDemoSiteSeeder: failed to seed grid for '{Name}' — {Status}.", page.Name, result.Result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FlowDemoSiteSeeder: error seeding grid for '{Name}'.", page.Name);
        }
    }

    // ─── Page grid builders ───────────────────────────────────────────────────
    // GUID prefix: fd* (Flow Demo pages — distinct from fe* used by PageBlockGridSeeder)
    // Ranges: fd000100-199 Intro, fd000200-299 Paso1, fd000300-399 Paso2,
    //         fd000400-499 Paso3, fd000500-599 Paso4, fd000600-699 Paso5

    private static string BuildIntroGrid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Main heading (h1) ──────────────────────────────────────────
        // NOTE: ElementCompHero requires a mandatory media image — cannot seed
        // without a valid media UDI. Use Heading + Paragraph instead.
        Guid r1 = new("fd000100-0000-0000-0000-000000000000"),
             g1 = new("fd000101-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Synergos Flow Engine", "h1")));

        // ── Row 2: Intro paragraph ────────────────────────────────────────────
        Guid r2 = new("fd000110-0000-0000-0000-000000000000"),
             g2 = new("fd000111-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Define flujos en Umbraco. Ejecútalos desde cualquier sistema.",
            "<p>Configura tracks, pasos y reglas en el backoffice sin YAML, sin código. " +
            "Cambia el comportamiento del engine sin hacer ningún deploy.</p>")));

        // ── Row 3: h2 section heading ─────────────────────────────────────────
        Guid r3h = new("fd000112-0000-0000-0000-000000000000"),
             g3h = new("fd000113-0000-0000-0000-000000000000");
        layout.Add(Row(r3h, 12, (A1Main, [g3h])));
        contentData.Add(PresetEntry(T1Col, r3h));
        contentData.Add(ContentEntry(THeading, g3h, Heading("¿Cómo funciona?")));

        // ── Row 4: 3 KeyValue steps ───────────────────────────────────────────
        Guid r4k = new("fd000120-0000-0000-0000-000000000000"),
             f1  = new("fd000121-0000-0000-0000-000000000000"),
             f2  = new("fd000122-0000-0000-0000-000000000000"),
             f3  = new("fd000123-0000-0000-0000-000000000000");
        layout.Add(Row(r4k, 12, (A3Col1, [f1]), (A3Col2, [f2]), (A3Col3, [f3])));
        contentData.Add(PresetEntry(T3Col, r4k));
        contentData.Add(ContentEntry(TKeyValue, f1, KeyValue(
            "1. Configurar en backoffice",
            "<p>Define tracks, pasos y reglas desde la interfaz del CMS. Sin YAML, sin JSON a mano, sin deployar.</p>")));
        contentData.Add(ContentEntry(TKeyValue, f2, KeyValue(
            "2. Publicar al engine",
            "<p>Un POST firma el flujo con HMAC-SHA256 y lo registra en Synergos.API listo para ejecutar desde cualquier sistema.</p>")));
        contentData.Add(ContentEntry(TKeyValue, f3, KeyValue(
            "3. Cambiar sin deployar",
            "<p>Cambia el modo de ejecución de sequential a parallel en el backoffice. El engine lo toma en el siguiente publish.</p>")));

        // ── Row 5: CTA ────────────────────────────────────────────────────────
        Guid r5 = new("fd000130-0000-0000-0000-000000000000"),
             g5 = new("fd000131-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A1Main, [g5])));
        contentData.Add(PresetEntry(T1Col, r5));
        contentData.Add(ContentEntry(TCtaBanner, g5, CtaBanner(
            "Listo para verlo en acción",
            "<p>El demo completo toma menos de 10 minutos. Sigue los 5 pasos y verás cómo el backoffice controla el engine sin tocar código.</p>",
            "Paso 1: Ver los flujos →", "/flow-engine-demo/paso-1-flujos-en-umbraco")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildPaso1Grid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Heading ────────────────────────────────────────────────────
        Guid r1 = new("fd000200-0000-0000-0000-000000000000"),
             g1 = new("fd000201-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Paso 1: Flujos en Umbraco", "h1")));

        // ── Row 2: Intro paragraph ────────────────────────────────────────────
        Guid r2 = new("fd000210-0000-0000-0000-000000000000"),
             g2 = new("fd000211-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Dónde viven los flujos",
            "<p>Los <strong>FlowDefinition</strong> nodes viven en <code>Content → Flow Settings Root</code>. " +
            "Cada nodo define un flujo completo: nombre, alias, modo de ejecución y tracks. " +
            "Este proyecto ya tiene dos flujos sembrados para el demo.</p>")));

        // ── Row 3: 2 flujos disponibles ───────────────────────────────────────
        Guid r3 = new("fd000220-0000-0000-0000-000000000000"),
             k1 = new("fd000221-0000-0000-0000-000000000000"),
             k2 = new("fd000222-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A2Left, [k1]), (A2Right, [k2])));
        contentData.Add(PresetEntry(T2Col, r3));
        contentData.Add(ContentEntry(TKeyValue, k1, KeyValue(
            "approval-flow",
            "<p>Flujo de aprobación con dos tracks: <code>validate-budget</code> y <code>notify-approver</code>. " +
            "Modo <strong>sequential</strong> — los tracks corren uno tras otro.</p>")));
        contentData.Add(ContentEntry(TKeyValue, k2, KeyValue(
            "notification-pipeline",
            "<p>Flujo de notificaciones multicanal con tres tracks: <code>email</code>, <code>sms</code>, <code>push</code>. " +
            "Modo <strong>parallel</strong> — los tracks corren en simultáneo.</p>")));

        // ── Row 4: API endpoint ───────────────────────────────────────────────
        Guid r4 = new("fd000230-0000-0000-0000-000000000000"),
             g4 = new("fd000231-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(TKeyValue, g4, KeyValue(
            "Verificar desde la API",
            "<p><code>GET /api/synergos/v1/orchestration/flows</code> — lista todos los flujos publicados con su configuración actual. " +
            "Devuelve alias, executionMode y tracks de cada flujo registrado.</p>")));

        // ── Row 5: CTA ────────────────────────────────────────────────────────
        Guid r5 = new("fd000240-0000-0000-0000-000000000000"),
             g5 = new("fd000241-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A1Main, [g5])));
        contentData.Add(PresetEntry(T1Col, r5));
        contentData.Add(ContentEntry(TCtaBanner, g5, CtaBanner(
            "¿Ves los flujos en el backoffice?",
            "<p>Ve a <strong>Content → Flow Settings Root</strong> y comprueba que <code>approval-flow</code> y <code>notification-pipeline</code> están ahí. Cuando estés listo, sigue al Paso 2.</p>",
            "Paso 2: Publicar al engine →", "/flow-engine-demo/paso-2-registrar-en-engine",
            "primary")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildPaso2Grid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Heading ────────────────────────────────────────────────────
        Guid r1 = new("fd000300-0000-0000-0000-000000000000"),
             g1 = new("fd000301-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Paso 2: Registrar en Engine", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fd000310-0000-0000-0000-000000000000"),
             g2 = new("fd000311-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Qué significa publicar",
            "<p>Publicar un flujo significa enviarlo <strong>firmado</strong> desde Umbraco hacia Synergos.API, " +
            "donde queda disponible para ejecutar. El CMS es la fuente de verdad; el engine solo acepta flujos autenticados.</p>")));

        // ── Row 3: 3 steps ───────────────────────────────────────────────────
        Guid r3 = new("fd000320-0000-0000-0000-000000000000"),
             k1 = new("fd000321-0000-0000-0000-000000000000"),
             k2 = new("fd000322-0000-0000-0000-000000000000"),
             k3 = new("fd000323-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A3Col1, [k1]), (A3Col2, [k2]), (A3Col3, [k3])));
        contentData.Add(PresetEntry(T3Col, r3));
        contentData.Add(ContentEntry(TKeyValue, k1, KeyValue(
            "Endpoint de publicación",
            "<p><code>POST /api/synergos/v1/orchestration/flows/{alias}/publish</code></p>" +
            "<p>Ejemplo: <code>/flows/approval-flow/publish</code></p>")));
        contentData.Add(ContentEntry(TKeyValue, k2, KeyValue(
            "Seguridad HMAC",
            "<p>El CMS firma el payload con <strong>HMAC-SHA256</strong> usando el <code>WebhookSecret</code> configurado. " +
            "El engine verifica la firma antes de registrar — rechaza cualquier request sin firma válida.</p>")));
        contentData.Add(ContentEntry(TKeyValue, k3, KeyValue(
            "Verificar registro",
            "<p><code>GET /api/engine/flows</code> devuelve el listado de flujos registrados con su versión y estado actual. " +
            "Confirma que <code>approval-flow</code> aparece después del publish.</p>")));

        // ── Row 4: CTA ────────────────────────────────────────────────────────
        Guid r4 = new("fd000330-0000-0000-0000-000000000000"),
             g4 = new("fd000331-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(TCtaBanner, g4, CtaBanner(
            "Flujo registrado en el engine",
            "<p>Llama al endpoint de publish y verifica con <code>GET /api/engine/flows</code>. " +
            "Cuando veas <code>approval-flow</code> en el listado, el engine está listo para ejecutarlo.</p>",
            "Paso 3: Ejecutar →", "/flow-engine-demo/paso-3-ejecutar-un-flujo",
            "primary")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildPaso3Grid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Heading ────────────────────────────────────────────────────
        Guid r1 = new("fd000400-0000-0000-0000-000000000000"),
             g1 = new("fd000401-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Paso 3: Ejecutar un Flujo", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fd000410-0000-0000-0000-000000000000"),
             g2 = new("fd000411-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Ejecutar desde cualquier sistema",
            "<p>Con el flujo registrado, cualquier sistema puede ejecutarlo enviando un <code>POST</code> al engine. " +
            "No necesita saber cómo funciona el flujo internamente — solo el alias y el payload de negocio.</p>")));

        // ── Row 3: Endpoint + Body ────────────────────────────────────────────
        Guid r3 = new("fd000420-0000-0000-0000-000000000000"),
             k1 = new("fd000421-0000-0000-0000-000000000000"),
             k2 = new("fd000422-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A2Left, [k1]), (A2Right, [k2])));
        contentData.Add(PresetEntry(T2Col, r3));
        contentData.Add(ContentEntry(TKeyValue, k1, KeyValue(
            "Endpoint de ejecución",
            "<p><code>POST /api/engine/flows/approval-flow/execute</code></p>" +
            "<p>Body de ejemplo:</p>" +
            "<pre>{\n  \"caseId\": \"CASE-001\",\n  \"input\": {\n    \"amount\": 5000,\n    \"priority\": \"high\"\n  }\n}</pre>")));
        contentData.Add(ContentEntry(TKeyValue, k2, KeyValue(
            "Respuesta del engine",
            "<p>El engine devuelve:</p>" +
            "<ul><li><code>outcome</code> — resultado final del flujo</li>" +
            "<li><code>trackResults</code> — resultado de cada track</li>" +
            "<li><code>executionTimeMs</code> — tiempo total en ms</li></ul>" +
            "<p>En modo <strong>sequential</strong> los tracks corren uno tras otro. En el Paso 4 cambiaremos eso.</p>")));

        // ── Row 4: CTA ────────────────────────────────────────────────────────
        Guid r4 = new("fd000430-0000-0000-0000-000000000000"),
             g4 = new("fd000431-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(TCtaBanner, g4, CtaBanner(
            "¿Obtuviste el outcome?",
            "<p>Guarda el <code>executionTimeMs</code> de este primer run en modo sequential. " +
            "En el Paso 4 lo vas a comparar con el modo parallel.</p>",
            "Paso 4: Cambiar el modo →", "/flow-engine-demo/paso-4-cambiar-el-modo",
            "primary")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildPaso4Grid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Heading ────────────────────────────────────────────────────
        Guid r1 = new("fd000500-0000-0000-0000-000000000000"),
             g1 = new("fd000501-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Paso 4: Cambiar el Modo", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fd000510-0000-0000-0000-000000000000"),
             g2 = new("fd000511-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "El momento del demo",
            "<p>Aquí es donde el Flow Engine demuestra su valor. Vas a cambiar el comportamiento del flujo " +
            "desde el backoffice sin tocar ningún archivo, sin hacer deploy y sin reiniciar nada.</p>")));

        // ── Row 3: 3 pasos ────────────────────────────────────────────────────
        Guid r3 = new("fd000520-0000-0000-0000-000000000000"),
             f1 = new("fd000521-0000-0000-0000-000000000000"),
             f2 = new("fd000522-0000-0000-0000-000000000000"),
             f3 = new("fd000523-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A3Col1, [f1]), (A3Col2, [f2]), (A3Col3, [f3])));
        contentData.Add(PresetEntry(T3Col, r3));
        contentData.Add(ContentEntry(TKeyValue, f1, KeyValue(
            "1. Abrir el backoffice",
            "<p>Ve a <strong>Content → Flow Settings Root → approval-flow</strong>. Abre la pestaña Execution.</p>")));
        contentData.Add(ContentEntry(TKeyValue, f2, KeyValue(
            "2. Cambiar a parallel",
            "<p>Cambia el campo <em>Execution Mode</em> de <code>sequential</code> a <code>parallel</code>. Guarda y publica el nodo.</p>")));
        contentData.Add(ContentEntry(TKeyValue, f3, KeyValue(
            "3. Re-publicar y ejecutar",
            "<p>Llama al <code>/publish</code> endpoint. Luego ejecuta el mismo CASE-001. Los tracks ahora corren en simultáneo — el <code>executionTimeMs</code> baja.</p>")));

        // ── Row 4: CTA ────────────────────────────────────────────────────────
        Guid r4 = new("fd000530-0000-0000-0000-000000000000"),
             g4 = new("fd000531-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(TCtaBanner, g4, CtaBanner(
            "¿Notaste la diferencia?",
            "<p>Sequential vs Parallel — mismo flujo, mismo código, comportamiento diferente. " +
            "Solo cambiaste un campo en el backoffice. Eso es el Flow Engine.</p>",
            "Paso 5: Ver los resultados →", "/flow-engine-demo/paso-5-ver-resultados",
            "primary")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildPaso5Grid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Heading ────────────────────────────────────────────────────
        Guid r1 = new("fd000600-0000-0000-0000-000000000000"),
             g1 = new("fd000601-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Paso 5: Ver Resultados", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fd000610-0000-0000-0000-000000000000"),
             g2 = new("fd000611-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Historial de ejecuciones",
            "<p>El engine guarda el resultado de cada ejecución. Puedes consultar el historial completo, " +
            "el detalle de un caso específico, o el estado general del sistema.</p>")));

        // ── Row 3: 3 endpoints ────────────────────────────────────────────────
        Guid r3 = new("fd000620-0000-0000-0000-000000000000"),
             k1 = new("fd000621-0000-0000-0000-000000000000"),
             k2 = new("fd000622-0000-0000-0000-000000000000"),
             k3 = new("fd000623-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A3Col1, [k1]), (A3Col2, [k2]), (A3Col3, [k3])));
        contentData.Add(PresetEntry(T3Col, r3));
        contentData.Add(ContentEntry(TKeyValue, k1, KeyValue(
            "Últimas ejecuciones",
            "<p><code>GET /api/engine/executions?limit=10</code></p>" +
            "<p>Lista las ejecuciones más recientes con alias, caseId, outcome y tiempo.</p>")));
        contentData.Add(ContentEntry(TKeyValue, k2, KeyValue(
            "Detalle de un caso",
            "<p><code>GET /api/engine/executions/CASE-001</code></p>" +
            "<p>Muestra el resultado completo: trackResults, input recibido y executionTimeMs de cada track.</p>")));
        contentData.Add(ContentEntry(TKeyValue, k3, KeyValue(
            "Estado del engine",
            "<p><code>GET /api/engine/health</code></p>" +
            "<p>Confirma el estado del engine, la cantidad de flujos registrados y el estado de cada worker activo.</p>")));

        // ── Row 4: CTA ────────────────────────────────────────────────────────
        Guid r4 = new("fd000630-0000-0000-0000-000000000000"),
             g4 = new("fd000631-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(TCtaBanner, g4, CtaBanner(
            "Demo completado",
            "<p>En menos de 10 minutos viste cómo Synergos Flow Engine conecta el backoffice con la ejecución de negocio. " +
            "Configuración en Umbraco. Ejecución en la API. Sin tocar código.</p>",
            "← Volver al inicio", "/flow-engine-demo/intro-al-flow-engine",
            "dark")));

        return SerializeGrid(layout, contentData);
    }

    // ─── JSON helpers (same pattern as PageBlockGridSeeder) ───────────────────

    private static string Udi(Guid g) => $"umb://element/{g:N}";
    private static string AreaKey(Guid g) => g.ToString("D");

    private static JsonObject Row(Guid presetGuid, int colSpan, params (Guid areaKey, Guid[] items)[] areas)
    {
        var areaArray = new JsonArray();
        foreach (var (aKey, items) in areas)
        {
            var itemArray = new JsonArray();
            foreach (var item in items)
            {
                itemArray.Add(new JsonObject
                {
                    ["contentUdi"] = Udi(item),
                    ["columnSpan"] = 12,
                    ["rowSpan"]    = 1,
                    ["areas"]      = new JsonArray()
                });
            }
            areaArray.Add(new JsonObject { ["key"] = AreaKey(aKey), ["items"] = itemArray });
        }
        return new JsonObject
        {
            ["contentUdi"] = Udi(presetGuid),
            ["columnSpan"] = colSpan,
            ["rowSpan"]    = 1,
            ["areas"]      = areaArray
        };
    }

    private static JsonObject ContentEntry(string contentTypeKey, Guid contentGuid, Dictionary<string, object?> props)
    {
        var entry = new JsonObject
        {
            ["contentTypeKey"] = contentTypeKey,
            ["udi"]            = Udi(contentGuid)
        };
        foreach (var (alias, value) in props)
        {
            entry[alias] = value switch
            {
                null       => null,
                JsonNode n => n.DeepClone(),
                string s   => JsonValue.Create(s),
                int i      => JsonValue.Create(i),
                bool b     => JsonValue.Create(b),
                _          => JsonValue.Create(value.ToString())
            };
        }
        return entry;
    }

    private static JsonObject PresetEntry(string typeKey, Guid guid)
        => ContentEntry(typeKey, guid, new Dictionary<string, object?>());

    private static string SerializeGrid(JsonArray layoutItems, JsonArray contentData)
    {
        var root = new JsonObject
        {
            ["layout"]       = new JsonObject { [BgEditorAlias] = layoutItems },
            ["contentData"]  = contentData,
            ["settingsData"] = new JsonArray()
        };
        return root.ToJsonString();
    }

    // ─── Content factories ────────────────────────────────────────────────────

    private static string Dropdown(string value) => JsonSerializer.Serialize(new[] { value });

    private static Dictionary<string, object?> Heading(string text, string level = "h2") => new()
    {
        ["headingText"]  = text,
        ["headingLevel"] = Dropdown(level)
    };

    private static Dictionary<string, object?> Paragraph(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    private static Dictionary<string, object?> Hero(
        string headingText, string body,
        string ctaLabel, string ctaUrl,
        string variant = "dark") => new()
    {
        ["headingText"] = headingText,
        ["body"]        = body,
        ["ctaLabel"]    = ctaLabel,
        ["ctaLink"]     = Link(ctaUrl, ctaLabel),
        ["variant"]     = Dropdown(variant)
    };

    private static Dictionary<string, object?> Feature(string title, string subtitle, string summary) => new()
    {
        ["title"]    = title,
        ["subtitle"] = subtitle,
        ["summary"]  = summary
    };

    private static Dictionary<string, object?> KeyValue(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    private static Dictionary<string, object?> CtaBanner(
        string title, string body,
        string ctaLabel, string ctaUrl,
        string variant = "dark") => new()
    {
        ["title"]    = title,
        ["body"]     = body,
        ["ctaLabel"] = ctaLabel,
        ["ctaLink"]  = Link(ctaUrl, ctaLabel),
        ["variant"]  = Dropdown(variant)
    };

    private static JsonNode Link(string url, string name, string target = "_self")
        => JsonNode.Parse(
            $"[{{\"url\":\"{url}\",\"name\":\"{name}\",\"target\":\"{target}\",\"udi\":null}}]")!;

    // ─── Navigation + SiteSettings wiring ────────────────────────────────────

    private void EnsureNavigation(int siteId)
    {
        // 1. Config folder under demo site
        var config = EnsureChild(siteId, ContentTypeKeys.Aliases.SharedContentFolder, "Config", "", null);
        if (config is null)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: could not create 'Config' folder — navigation skipped.");
            return;
        }

        // 2. Look up demo pages before building the nav (needed for navLink UDIs)
        var demoPages = _content.GetPagedChildren(siteId, 0, 20, out _)
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.PageBase)
            .OrderBy(c => c.SortOrder)
            .ToList();

        // 3. Find or create navigationGroup.
        //    NOTE: groupName is mandatory → we must set it BEFORE the first SaveAndPublish.
        //    EnsureChild publishes with no properties, so we inline the find-or-create here.
        var navGroup = _content.GetPagedChildren(config.Id, 0, 20, out _)
            .FirstOrDefault(c =>
                c.ContentType.Alias == ContentTypeKeys.Aliases.NavigationGroup &&
                string.Equals(c.Name, "Flow Engine Demo Nav", StringComparison.OrdinalIgnoreCase));

        if (navGroup is null)
        {
            var navGroupType = _contentTypes.Get(ContentTypeKeys.Aliases.NavigationGroup);
            if (navGroupType is null)
            {
                _logger.LogWarning("FlowDemoSiteSeeder: contentType 'navigationGroup' not found — navigation skipped.");
                return;
            }
            navGroup = _content.Create("Flow Engine Demo Nav", config.Id, ContentTypeKeys.Aliases.NavigationGroup);
        }

        // Always rebuild navItems so content-node UDIs are current
        navGroup.SetValue("groupName",  "Flow Engine Demo Nav");
        navGroup.SetValue("groupAlias", "flow-engine-demo");
        navGroup.SetValue("navItems",   BuildNavItems(demoPages));

        var navResult = _content.SaveAndPublish(navGroup, userId: UmbConstants.Security.SuperUserId);
        if (!navResult.Success)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: failed to publish navigationGroup — {Status}.", navResult.Result);
            return;
        }
        _logger.LogInformation("FlowDemoSiteSeeder: navigationGroup published (Id={Id}).", navGroup.Id);

        // 4. Patch SiteSettings: nav pickers + SEO defaults
        var siteSettings = _content.GetPagedChildren(siteId, 0, 20, out _)
            .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.SiteSettingsAlias);

        if (siteSettings is null)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: SiteSettings not found — skipping nav wiring.");
            return;
        }

        var navUdi = $"umb://document/{navGroup.Key:N}";
        var dirty  = false;

        dirty |= SetIfEmpty(siteSettings, "headerNavigation",     navUdi);
        dirty |= SetIfEmpty(siteSettings, "footerNavigation",     navUdi);
        dirty |= SetIfEmpty(siteSettings, "seoTitleSuffix",       " | Flow Engine Demo");
        dirty |= SetIfEmpty(siteSettings, "seoDefaultDescription",
            "Demo paso a paso del Synergos Flow Engine. Del backoffice a la ejecución — sin tocar código.");

        if (dirty)
        {
            var r = _content.SaveAndPublish(siteSettings, userId: UmbConstants.Security.SuperUserId);
            if (r.Success)
                _logger.LogInformation("FlowDemoSiteSeeder: SiteSettings patched (Id={Id}).", siteSettings.Id);
            else
                _logger.LogWarning("FlowDemoSiteSeeder: failed to patch SiteSettings — {Status}.", r.Result);
        }
    }

    /// <summary>
    /// Builds a Block List JSON string for the navigationGroup.navItems property.
    /// Uses stable element GUIDs in the fd000701-706 range (block-element UDIs,
    /// not rows in umbracoNode — safe to use without GUID registry).
    /// </summary>
    private static string BuildNavItems(IReadOnlyList<IContent> pages)
    {
        const string NavItemTypeKey = "cb000001-0000-0000-0000-000000000000";
        var navElemGuids = new[]
        {
            new Guid("fd000701-0000-0000-0000-000000000000"),
            new Guid("fd000702-0000-0000-0000-000000000000"),
            new Guid("fd000703-0000-0000-0000-000000000000"),
            new Guid("fd000704-0000-0000-0000-000000000000"),
            new Guid("fd000705-0000-0000-0000-000000000000"),
            new Guid("fd000706-0000-0000-0000-000000000000"),
        };

        var layoutItems = new JsonArray();
        var contentData = new JsonArray();

        for (int i = 0; i < pages.Count && i < navElemGuids.Length; i++)
        {
            var page     = pages[i];
            var elemGuid = navElemGuids[i];
            var elemUdi  = $"umb://element/{elemGuid:N}";
            var docUdi   = $"umb://document/{page.Key:N}";

            layoutItems.Add(new JsonObject { ["contentUdi"] = elemUdi });
            contentData.Add(new JsonObject
            {
                ["contentTypeKey"] = NavItemTypeKey,
                ["udi"]            = elemUdi,
                ["navLabel"]       = page.Name ?? "",
                ["navLink"]        = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        name        = page.Name ?? "",
                        udi         = docUdi,
                        url         = "",
                        target      = "",
                        queryString = ""
                    }
                }),
                ["navHighlighted"] = "0"
            });
        }

        return new JsonObject
        {
            ["layout"]       = new JsonObject { ["Umbraco.BlockList"] = layoutItems },
            ["contentData"]  = contentData,
            ["settingsData"] = new JsonArray()
        }.ToJsonString();
    }

    private static bool SetIfEmpty(IContent node, string alias, string value)
    {
        if (!node.HasProperty(alias)) return false;
        if (!string.IsNullOrWhiteSpace(node.GetValue<string>(alias))) return false;
        node.SetValue(alias, value);
        return true;
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private IContent? EnsureChild(
        int parentId,
        string docTypeAlias,
        string name,
        string templateAlias,
        Action<IContent>? configure)
    {
        var children = _content.GetPagedChildren(parentId, 0, 200, out _);
        var existing = children.FirstOrDefault(c =>
            c.ContentType.Alias == docTypeAlias &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null) return existing;

        var docType = _contentTypes.Get(docTypeAlias);
        if (docType is null)
        {
            _logger.LogDebug("FlowDemoSiteSeeder: contentType '{Alias}' not found — skipping '{Name}'.",
                docTypeAlias, name);
            return null;
        }

        var node = _content.Create(name, parentId, docTypeAlias);
        configure?.Invoke(node);

        // Culture-variant content types require an explicit culture name before SaveAndPublish
        if (docType.Variations.HasFlag(ContentVariation.Culture))
            node.SetCultureName(name, SeedCulture);

        var template = _files.GetTemplate(templateAlias);
        if (template is not null) node.TemplateId = template.Id;

        var result = _content.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        if (result.Success)
            _logger.LogInformation("FlowDemoSiteSeeder: '{Name}' published (Id={Id}).", name, node.Id);
        else
            _logger.LogWarning("FlowDemoSiteSeeder: failed to publish '{Name}' — {Status}.", name, result.Result);

        return result.Success ? node : null;
    }
}
