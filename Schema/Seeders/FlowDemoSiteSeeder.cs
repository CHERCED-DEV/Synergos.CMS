using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;
using Synergos.CMS.Schema.Constants.BlockGrid;
using Synergos.CMS.Schema.Seeders.BlockGrid;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// Creates the Synergos Flow Engine demo site under PlatformRoot. Idempotent —
/// every node is checked for existence before creation; pageSections are seeded
/// only when the published value is empty (so editor changes are never overwritten).
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
///       └── Config/Flow Engine Demo Nav  (navigationGroup — wired into SiteSettings)
///
/// Block Grid content is built via <see cref="BlockGridJsonBuilder"/>; layout area
/// keys come from <see cref="BlockGridAreaKeys"/>; content type GUIDs come from
/// <see cref="ContentTypeKeys"/>; magic strings are typed via <see cref="HeadingLevel"/>
/// and <see cref="BlockVariant"/> enums. No Block UDI literals — all are
/// <see cref="Guid.NewGuid"/> at build time.
/// </summary>
internal sealed class FlowDemoSiteSeeder
{
    private const string PageSectionsKey = "pageSections";
    private const string SeedCulture     = "es-CO";
    private const string DemoSiteName    = "Flow Engine Demo";
    private const string DemoNavName     = "Flow Engine Demo Nav";
    private const string DemoNavAlias    = "flow-engine-demo";

    private readonly IContentService     _content;
    private readonly IContentTypeService _contentTypes;
    private readonly IFileService        _files;
    private readonly ILogger             _logger;

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
            _logger.LogWarning("FlowDemoSiteSeeder: 'platformRoot' not found — skipping demo site creation.");
            return;
        }

        var demoSite = GetOrCreateDemoSiteRoot(platformRoot.Id);
        if (demoSite is null) return;

        EnsureSettings(demoSite.Id);
        EnsureDemoPages(demoSite.Id);
        EnsureNavigation(demoSite.Id);

        _logger.LogInformation("FlowDemoSiteSeeder: demo site seeded (Id={Id}).", demoSite.Id);
    }

    // ─── Site root ──────────────────────────────────────────────────────────

    private IContent? GetOrCreateDemoSiteRoot(int platformId)
    {
        var children = _content.GetPagedChildren(platformId, 0, 100, out _);
        var existing = children.FirstOrDefault(c =>
            c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot &&
            (c.GetValue<string>("siteName") ?? c.Name) == DemoSiteName);

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

        var node = _content.Create(DemoSiteName, platformId, ContentTypeKeys.Aliases.SiteRoot);
        node.SetValue("siteName",    DemoSiteName);
        node.SetValue("siteTagline", "Demostración paso a paso del Synergos Flow Engine");

        var template = _files.GetTemplate("SiteRoot");
        if (template is not null) node.TemplateId = template.Id;

        var result = _content.SaveAndPublish(node, userId: UmbConstants.Security.SuperUserId);
        if (!result.Success)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: failed to publish demo site root — {Status}.", result.Result);
            return null;
        }

        _logger.LogInformation("FlowDemoSiteSeeder: demo site root published (Id={Id}).", node.Id);
        return node;
    }

    // ─── Settings nodes ─────────────────────────────────────────────────────

    private void EnsureSettings(int siteId)
    {
        EnsureChild(siteId, ContentTypeKeys.Aliases.SiteSettingsAlias, "Site Settings",
            "SiteSettings", null);

        EnsureChild(siteId, ContentTypeKeys.Aliases.ThemeSettings, "Theme Settings",
            "ThemeSettings", null);
    }

    // ─── Demo pages ─────────────────────────────────────────────────────────

    private void EnsureDemoPages(int siteId)
    {
        EnsureDemoPage(siteId, "Intro al Flow Engine",       0,
            "Qué es el Synergos Flow Engine y cómo funciona",
            "Flow Engine Demo — Intro",
            "El Flow Engine es la capa de orquestación de Synergos. Umbraco define los flujos; Synergos.API los ejecuta.",
            BuildIntroGrid);

        EnsureDemoPage(siteId, "Paso 1: Flujos en Umbraco",  10,
            "Revisa los FlowDefinition nodes en el backoffice",
            "Flow Engine Demo — Paso 1",
            "Navega a Content → Flow Settings Root. Verás approval-flow y notification-pipeline ya configurados.",
            BuildPaso1Grid);

        EnsureDemoPage(siteId, "Paso 2: Registrar en Engine", 20,
            "Publica un flujo desde CMS hacia Synergos.API",
            "Flow Engine Demo — Paso 2",
            "POST /api/synergos/v1/orchestration/flows/approval-flow/publish firma el payload con HMAC-SHA256 y lo envía al engine.",
            BuildPaso2Grid);

        EnsureDemoPage(siteId, "Paso 3: Ejecutar un Flujo",  30,
            "Llama al engine con un payload de prueba",
            "Flow Engine Demo — Paso 3",
            "POST /api/engine/flows/approval-flow/execute con caseId y input. El engine corre los tracks y devuelve el outcome.",
            BuildPaso3Grid);

        EnsureDemoPage(siteId, "Paso 4: Cambiar el Modo",     40,
            "El momento demo: cambia executionMode en el backoffice",
            "Flow Engine Demo — Paso 4",
            "Cambia approval-flow de sequential a parallel en el backoffice. Republica. El mismo execute ahora corre los tracks en simultáneo.",
            BuildPaso4Grid);

        EnsureDemoPage(siteId, "Paso 5: Ver Resultados",     50,
            "Consulta el historial de ejecuciones",
            "Flow Engine Demo — Paso 5",
            "GET /api/engine/executions muestra las últimas ejecuciones. GET /api/engine/health confirma el estado del engine.",
            BuildPaso5Grid);
    }

    private void EnsureDemoPage(
        int siteId, string name, int sortOrder,
        string subtitle, string seoTitle, string seoDescription,
        Func<string> buildGrid)
    {
        var page = EnsureChild(siteId, ContentTypeKeys.Aliases.PageBase, name, "PageBase",
            node =>
            {
                node.SetValue("pageTitle",      name,            culture: SeedCulture);
                node.SetValue("pageSubtitle",   subtitle,        culture: SeedCulture);
                node.SetValue("seoTitle",       seoTitle);
                node.SetValue("seoDescription", seoDescription);
                node.SortOrder = sortOrder;
            });
        SeedGridIfEmpty(page, buildGrid);
    }

    // ─── Block Grid seeders ─────────────────────────────────────────────────

    private void SeedGridIfEmpty(IContent? page, Func<string> buildGrid)
    {
        if (page is null) return;

        // Skip ONLY if the PUBLISHED version already has block content.
        // Use published:true to read the published property value, not the draft.
        // This handles the case where Save succeeded but Publish failed:
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

    // ─── Page grid builders ─────────────────────────────────────────────────
    // Each builder is a sequence of declarative AddXxx calls. Per-block UDIs are
    // auto-generated; no GUID literals here.

    private static string BuildIntroGrid()
    {
        var b = new BlockGridJsonBuilder();

        // Hero would require a mandatory media image we don't seed → use Heading+Paragraph instead.
        AddSingleHeading(b, "Synergos Flow Engine", HeadingLevel.H1);

        AddSingleParagraph(b,
            "Define flujos en Umbraco. Ejecútalos desde cualquier sistema.",
            "<p>El Flow Engine es la capa de orquestación de Synergos. " +
            "Tú defines los flujos en el backoffice — tracks, pasos, reglas — y el engine los ejecuta. " +
            "Cualquier sistema (.NET, Node, Python, lo que sea) puede invocar un flujo enviando un POST al engine.</p>");

        AddSingleHeading(b,
            "Configura tracks, pasos y reglas en el backoffice sin YAML, sin código. " +
            "Cambia el comportamiento del engine sin hacer ningún deploy.");

        AddSingleHeading(b, "¿Cómo funciona?");

        AddThreeKeyValues(b,
            ("1. Configurar en backoffice",
             "<p>Define tracks, pasos y reglas desde la interfaz del CMS. Sin YAML, sin JSON a mano, sin deployar.</p>"),
            ("2. Publicar al engine",
             "<p>Un POST firma el flujo con HMAC-SHA256 y lo registra en Synergos.API listo para ejecutar desde cualquier sistema.</p>"),
            ("3. Cambiar sin deployar",
             "<p>Cambia el modo de ejecución de sequential a parallel en el backoffice. El engine lo toma en el siguiente publish.</p>"));

        AddCtaBanner(b,
            "Listo para verlo en acción",
            "<p>El demo completo toma menos de 10 minutos. Sigue los 5 pasos y verás cómo el backoffice controla el engine sin tocar código.</p>",
            "Paso 1: Ver los flujos →", "/flow-engine-demo/paso-1-flujos-en-umbraco");

        return b.ToJson();
    }

    private static string BuildPaso1Grid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Paso 1: Flujos en Umbraco", HeadingLevel.H1);

        AddSingleParagraph(b,
            "Dónde viven los flujos",
            "<p>Los <strong>FlowDefinition</strong> nodes viven en <code>Content → Flow Settings Root</code>. " +
            "Cada nodo define un flujo completo: nombre, alias, modo de ejecución y tracks. " +
            "Este proyecto ya tiene dos flujos sembrados para el demo.</p>");

        AddTwoKeyValues(b,
            ("approval-flow",
             "<p>Flujo de aprobación con dos tracks: <code>validate-budget</code> y <code>notify-approver</code>. " +
             "Modo <strong>sequential</strong> — los tracks corren uno tras otro.</p>"),
            ("notification-pipeline",
             "<p>Flujo de notificaciones multicanal con tres tracks: <code>email</code>, <code>sms</code>, <code>push</code>. " +
             "Modo <strong>parallel</strong> — los tracks corren en simultáneo.</p>"));

        AddSingleKeyValue(b, "Verificar desde la API",
            "<p><code>GET /api/synergos/v1/orchestration/flows</code> — lista todos los flujos publicados con su configuración actual. " +
            "Devuelve alias, executionMode y tracks de cada flujo registrado.</p>");

        AddCtaBanner(b,
            "¿Ves los flujos en el backoffice?",
            "<p>Ve a <strong>Content → Flow Settings Root</strong> y comprueba que <code>approval-flow</code> y <code>notification-pipeline</code> están ahí. Cuando estés listo, sigue al Paso 2.</p>",
            "Paso 2: Publicar al engine →", "/flow-engine-demo/paso-2-registrar-en-engine",
            BlockVariant.Primary);

        return b.ToJson();
    }

    private static string BuildPaso2Grid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Paso 2: Registrar en Engine", HeadingLevel.H1);

        AddSingleParagraph(b,
            "Qué significa publicar",
            "<p>Publicar un flujo significa enviarlo <strong>firmado</strong> desde Umbraco hacia Synergos.API, " +
            "donde queda disponible para ejecutar. El CMS es la fuente de verdad; el engine solo acepta flujos autenticados.</p>");

        AddThreeKeyValues(b,
            ("Endpoint de publicación",
             "<p><code>POST /api/synergos/v1/orchestration/flows/{alias}/publish</code></p>" +
             "<p>Ejemplo: <code>/flows/approval-flow/publish</code></p>"),
            ("Seguridad HMAC",
             "<p>El CMS firma el payload con <strong>HMAC-SHA256</strong> usando el <code>WebhookSecret</code> configurado. " +
             "El engine verifica la firma antes de registrar — rechaza cualquier request sin firma válida.</p>"),
            ("Verificar registro",
             "<p><code>GET /api/engine/flows</code> devuelve el listado de flujos registrados con su versión y estado actual. " +
             "Confirma que <code>approval-flow</code> aparece después del publish.</p>"));

        AddCtaBanner(b,
            "Flujo registrado en el engine",
            "<p>Llama al endpoint de publish y verifica con <code>GET /api/engine/flows</code>. " +
            "Cuando veas <code>approval-flow</code> en el listado, el engine está listo para ejecutarlo.</p>",
            "Paso 3: Ejecutar →", "/flow-engine-demo/paso-3-ejecutar-un-flujo",
            BlockVariant.Primary);

        return b.ToJson();
    }

    private static string BuildPaso3Grid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Paso 3: Ejecutar un Flujo", HeadingLevel.H1);

        AddSingleParagraph(b,
            "Ejecutar desde cualquier sistema",
            "<p>Con el flujo registrado, cualquier sistema puede ejecutarlo enviando un <code>POST</code> al engine. " +
            "No necesita saber cómo funciona el flujo internamente — solo el alias y el payload de negocio.</p>");

        AddTwoKeyValues(b,
            ("Endpoint de ejecución",
             "<p><code>POST /api/engine/flows/approval-flow/execute</code></p>" +
             "<p>Body de ejemplo:</p>" +
             "<pre>{\n  \"caseId\": \"CASE-001\",\n  \"input\": {\n    \"amount\": 5000,\n    \"priority\": \"high\"\n  }\n}</pre>"),
            ("Respuesta del engine",
             "<p>El engine devuelve:</p>" +
             "<ul><li><code>outcome</code> — resultado final del flujo</li>" +
             "<li><code>trackResults</code> — resultado de cada track</li>" +
             "<li><code>executionTimeMs</code> — tiempo total en ms</li></ul>" +
             "<p>En modo <strong>sequential</strong> los tracks corren uno tras otro. En el Paso 4 cambiaremos eso.</p>"));

        AddCtaBanner(b,
            "¿Obtuviste el outcome?",
            "<p>Guarda el <code>executionTimeMs</code> de este primer run en modo sequential. " +
            "En el Paso 4 lo vas a comparar con el modo parallel.</p>",
            "Paso 4: Cambiar el modo →", "/flow-engine-demo/paso-4-cambiar-el-modo",
            BlockVariant.Primary);

        return b.ToJson();
    }

    private static string BuildPaso4Grid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Paso 4: Cambiar el Modo", HeadingLevel.H1);

        AddSingleParagraph(b,
            "El momento del demo",
            "<p>Aquí es donde el Flow Engine demuestra su valor. Vas a cambiar el comportamiento del flujo " +
            "desde el backoffice sin tocar ningún archivo, sin hacer deploy y sin reiniciar nada.</p>");

        AddThreeKeyValues(b,
            ("1. Abrir el backoffice",
             "<p>Ve a <strong>Content → Flow Settings Root → approval-flow</strong>. Abre la pestaña Execution.</p>"),
            ("2. Cambiar a parallel",
             "<p>Cambia el campo <em>Execution Mode</em> de <code>sequential</code> a <code>parallel</code>. Guarda y publica el nodo.</p>"),
            ("3. Re-publicar y ejecutar",
             "<p>Llama al <code>/publish</code> endpoint. Luego ejecuta el mismo CASE-001. Los tracks ahora corren en simultáneo — el <code>executionTimeMs</code> baja.</p>"));

        AddCtaBanner(b,
            "¿Notaste la diferencia?",
            "<p>Sequential vs Parallel — mismo flujo, mismo código, comportamiento diferente. " +
            "Solo cambiaste un campo en el backoffice. Eso es el Flow Engine.</p>",
            "Paso 5: Ver los resultados →", "/flow-engine-demo/paso-5-ver-resultados",
            BlockVariant.Primary);

        return b.ToJson();
    }

    private static string BuildPaso5Grid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Paso 5: Ver Resultados", HeadingLevel.H1);

        AddSingleParagraph(b,
            "Historial de ejecuciones",
            "<p>El engine guarda el resultado de cada ejecución. Puedes consultar el historial completo, " +
            "el detalle de un caso específico, o el estado general del sistema.</p>");

        AddThreeKeyValues(b,
            ("Últimas ejecuciones",
             "<p><code>GET /api/engine/executions?limit=10</code></p>" +
             "<p>Lista las ejecuciones más recientes con alias, caseId, outcome y tiempo.</p>"),
            ("Detalle de un caso",
             "<p><code>GET /api/engine/executions/CASE-001</code></p>" +
             "<p>Muestra el resultado completo: trackResults, input recibido y executionTimeMs de cada track.</p>"),
            ("Estado del engine",
             "<p><code>GET /api/engine/health</code></p>" +
             "<p>Confirma el estado del engine, la cantidad de flujos registrados y el estado de cada worker activo.</p>"));

        AddCtaBanner(b,
            "Demo completado",
            "<p>En menos de 10 minutos viste cómo Synergos Flow Engine conecta el backoffice con la ejecución de negocio. " +
            "Configuración en Umbraco. Ejecución en la API. Sin tocar código.</p>",
            "← Volver al inicio", "/flow-engine-demo/intro-al-flow-engine");

        return b.ToJson();
    }

    // ─── Layout shorthands — encapsulate repeated row patterns ──────────────

    private static void AddSingleHeading(
        BlockGridJsonBuilder b, string text, HeadingLevel level = HeadingLevel.H2)
    {
        Guid preset = Guid.NewGuid(), heading = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { heading }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementTextHeading, heading, new Dictionary<string, object?>
        {
            ["headingText"]  = text,
            ["headingLevel"] = BlockGridJsonBuilder.DropdownValue(level.ToAlias())
        });
    }

    private static void AddSingleParagraph(BlockGridJsonBuilder b, string title, string body)
    {
        Guid preset = Guid.NewGuid(), paragraph = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { paragraph }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementTextParagraph, paragraph, new Dictionary<string, object?>
        {
            ["title"] = title,
            ["body"]  = body
        });
    }

    private static void AddSingleKeyValue(BlockGridJsonBuilder b, string title, string body)
    {
        Guid preset = Guid.NewGuid(), kv = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { kv }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, kv, KeyValueProps(title, body));
    }

    private static void AddTwoKeyValues(BlockGridJsonBuilder b,
        (string Title, string Body) k1,
        (string Title, string Body) k2)
    {
        Guid preset = Guid.NewGuid(), b1 = Guid.NewGuid(), b2 = Guid.NewGuid();
        b.AddRow(preset, 12,
            (BlockGridAreaKeys.Preset2ColLeft,  new[] { b1 }),
            (BlockGridAreaKeys.Preset2ColRight, new[] { b2 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset2ColEqual, preset);
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, b1, KeyValueProps(k1.Title, k1.Body));
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, b2, KeyValueProps(k2.Title, k2.Body));
    }

    private static void AddThreeKeyValues(BlockGridJsonBuilder b,
        (string Title, string Body) k1,
        (string Title, string Body) k2,
        (string Title, string Body) k3)
    {
        Guid preset = Guid.NewGuid(), b1 = Guid.NewGuid(), b2 = Guid.NewGuid(), b3 = Guid.NewGuid();
        b.AddRow(preset, 12,
            (BlockGridAreaKeys.Preset3Col1, new[] { b1 }),
            (BlockGridAreaKeys.Preset3Col2, new[] { b2 }),
            (BlockGridAreaKeys.Preset3Col3, new[] { b3 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset3ColEqual, preset);
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, b1, KeyValueProps(k1.Title, k1.Body));
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, b2, KeyValueProps(k2.Title, k2.Body));
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, b3, KeyValueProps(k3.Title, k3.Body));
    }

    private static void AddCtaBanner(
        BlockGridJsonBuilder b, string title, string body, string ctaLabel, string ctaUrl,
        BlockVariant variant = BlockVariant.Dark)
    {
        Guid preset = Guid.NewGuid(), banner = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { banner }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementCompCtaBanner, banner, new Dictionary<string, object?>
        {
            ["title"]    = title,
            ["body"]     = body,
            ["ctaLabel"] = ctaLabel,
            ["ctaLink"]  = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel),
            ["variant"]  = BlockGridJsonBuilder.DropdownValue(variant.ToAlias())
        });
    }

    private static Dictionary<string, object?> KeyValueProps(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    // ─── Navigation + SiteSettings wiring ───────────────────────────────────

    private void EnsureNavigation(int siteId)
    {
        var config = EnsureChild(siteId, ContentTypeKeys.Aliases.SharedContentFolder, "Config", "", null);
        if (config is null)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: could not create 'Config' folder — navigation skipped.");
            return;
        }

        var demoPages = _content.GetPagedChildren(siteId, 0, 20, out _)
            .Where(c => c.ContentType.Alias == ContentTypeKeys.Aliases.PageBase)
            .OrderBy(c => c.SortOrder)
            .ToList();

        // Find or create the navigation group inline (groupName is mandatory and must
        // be set BEFORE the first SaveAndPublish, so we don't reuse EnsureChild here).
        var navGroup = _content.GetPagedChildren(config.Id, 0, 20, out _)
            .FirstOrDefault(c =>
                c.ContentType.Alias == ContentTypeKeys.Aliases.NavigationGroup &&
                string.Equals(c.Name, DemoNavName, StringComparison.OrdinalIgnoreCase));

        if (navGroup is null)
        {
            var navGroupType = _contentTypes.Get(ContentTypeKeys.Aliases.NavigationGroup);
            if (navGroupType is null)
            {
                _logger.LogWarning("FlowDemoSiteSeeder: contentType 'navigationGroup' not found — navigation skipped.");
                return;
            }
            navGroup = _content.Create(DemoNavName, config.Id, ContentTypeKeys.Aliases.NavigationGroup);
        }

        // Always rebuild navItems so content-node UDIs are current
        navGroup.SetValue("groupName",  DemoNavName);
        navGroup.SetValue("groupAlias", DemoNavAlias);
        navGroup.SetValue("navItems",   BuildNavItems(demoPages));

        var navResult = _content.SaveAndPublish(navGroup, userId: UmbConstants.Security.SuperUserId);
        if (!navResult.Success)
        {
            _logger.LogWarning("FlowDemoSiteSeeder: failed to publish navigationGroup — {Status}.", navResult.Result);
            return;
        }
        _logger.LogInformation("FlowDemoSiteSeeder: navigationGroup published (Id={Id}).", navGroup.Id);

        // Wire the nav into SiteSettings + seed defaults
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
    /// Builds a Block List JSON for navigationGroup.navItems. Each item is an
    /// <see cref="ContentTypeKeys.ElementNavItem"/> that links to one of the demo pages.
    /// Per-item UDIs are <see cref="Guid.NewGuid"/> — block-element UDIs only need to
    /// be unique within this JSON document.
    /// </summary>
    private static string BuildNavItems(IReadOnlyList<IContent> pages)
    {
        var navItemTypeKey = ContentTypeKeys.ElementNavItem.ToString("D");
        var layoutItems    = new JsonArray();
        var contentData    = new JsonArray();

        foreach (var page in pages)
        {
            var elemUdi = $"umb://element/{Guid.NewGuid():N}";
            var docUdi  = $"umb://document/{page.Key:N}";

            layoutItems.Add(new JsonObject { ["contentUdi"] = elemUdi });
            contentData.Add(new JsonObject
            {
                ["contentTypeKey"] = navItemTypeKey,
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

    // ─── Helper ─────────────────────────────────────────────────────────────

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

        // Culture-variant content types require an explicit culture name before SaveAndPublish.
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
