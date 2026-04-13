using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Schema.Constants;
using UmbConstants = Umbraco.Cms.Core.Constants;

namespace Synergos.CMS.Schema.Seeders;

/// <summary>
/// Seeds Block Grid content (pageSections) for the four main pages.
/// Idempotent: skips pages that already have block grid content.
///
/// Content strategy:
///   Home     — Hero → Stats → Features → CtaBanner
///   Nosotros — Heading → Paragraph → Mission/Vision → Values → Stats → CtaBanner
///   Servicios— Heading → Paragraph → Cards → Process → MediaText → CtaBanner
///   Contacto — Heading → Paragraph → ContactInfo → FAQ
/// </summary>
internal sealed class PageBlockGridSeeder
{
    private readonly IContentService _contentService;
    private readonly ILogger         _logger;

    private const string BgEditorAlias   = "Umbraco.BlockGrid";
    private const string PageSectionsKey = "pageSections";
    private const string SeedCulture     = "es-CO"; // pageSections is culture-variant

    // ── Layout Preset area GUIDs (D-format keys from DocumentTypeInitializer) ──
    private static readonly Guid A1Main  = new("fa010001-0000-0000-0000-000000000000");
    private static readonly Guid A2Left  = new("fa010002-0000-0000-0000-000000000000");
    private static readonly Guid A2Right = new("fa010003-0000-0000-0000-000000000000");
    private static readonly Guid A3Col1  = new("fa010004-0000-0000-0000-000000000000");
    private static readonly Guid A3Col2  = new("fa010005-0000-0000-0000-000000000000");
    private static readonly Guid A3Col3  = new("fa010006-0000-0000-0000-000000000000");
    private static readonly Guid A4Col1  = new("fa010007-0000-0000-0000-000000000000");
    private static readonly Guid A4Col2  = new("fa010008-0000-0000-0000-000000000000");
    private static readonly Guid A4Col3  = new("fa010009-0000-0000-0000-000000000000");
    private static readonly Guid A4Col4  = new("fa010010-0000-0000-0000-000000000000");

    // ── Content Type Keys (D-format) ─────────────────────────────────────────
    private const string T1Col      = "c3000010-0000-0000-0000-000000000000"; // LayoutPreset1Col
    private const string T2Col      = "c3000011-0000-0000-0000-000000000000"; // LayoutPreset2ColEqual
    private const string T3Col      = "c3000012-0000-0000-0000-000000000000"; // LayoutPreset3ColEqual
    private const string T4Col      = "c3000013-0000-0000-0000-000000000000"; // LayoutPreset4ColEqual
    private const string THeading   = "c4000001-0000-0000-0000-000000000000"; // ElementTextHeading
    private const string TParagraph = "c4000002-0000-0000-0000-000000000000"; // ElementTextParagraph
    private const string TStat      = "c7000002-0000-0000-0000-000000000000"; // ElementInfoStat
    private const string TFeature   = "c7000003-0000-0000-0000-000000000000"; // ElementInfoFeature
    private const string TKeyValue  = "c7000004-0000-0000-0000-000000000000"; // ElementInfoKeyValue
    private const string TCard      = "c8000001-0000-0000-0000-000000000000"; // ElementCompCard
    private const string THero      = "c8000002-0000-0000-0000-000000000000"; // ElementCompHero
    private const string TCtaBanner = "c8000004-0000-0000-0000-000000000000"; // ElementCompCtaBanner
    private const string TMediaText = "c8000008-0000-0000-0000-000000000000"; // ElementCompMediaTextSplit
    private const string TContact   = "ca000007-0000-0000-0000-000000000000"; // ElementCorpContactInfo
    private const string TMission   = "ca000009-0000-0000-0000-000000000000"; // ElementCorpMissionBlock

    public PageBlockGridSeeder(IContentService contentService, ILogger logger)
    {
        _contentService = contentService;
        _logger         = logger;
    }

    public void SeedAll()
    {
        var siteRoot = FindSiteRoot();
        if (siteRoot is null)
        {
            _logger.LogWarning("PageBlockGridSeeder: SiteRoot not found — skipping.");
            return;
        }

        var pages = _contentService.GetPagedChildren(siteRoot.Id, 0, 200, out _).ToList();

        SeedPage(pages, "Home",      BuildHomeGrid);
        SeedPage(pages, "Nosotros",  BuildNosotrosGrid);
        SeedPage(pages, "Servicios", BuildServiciosGrid);
        SeedPage(pages, "Contacto",  BuildContactoGrid);
    }

    // ── Infrastructure ────────────────────────────────────────────────────────

    private IContent? FindSiteRoot()
    {
        foreach (var root in _contentService.GetRootContent())
        {
            if (root.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot) return root;

            if (root.ContentType.Alias == ContentTypeKeys.Aliases.PlatformRoot)
            {
                var sr = _contentService.GetPagedChildren(root.Id, 0, 100, out _)
                    .FirstOrDefault(c => c.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);
                if (sr is not null) return sr;
            }
        }
        return null;
    }

    private void SeedPage(List<IContent> siblings, string name, Func<string> buildGrid)
    {
        var page = siblings.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (page is null)
        {
            _logger.LogDebug("PageBlockGridSeeder: page '{Name}' not found.", name);
            return;
        }

        var existing = page.GetValue<string>(PageSectionsKey, culture: SeedCulture);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            _logger.LogDebug("PageBlockGridSeeder: '{Name}' already has pageSections — skipping.", name);
            return;
        }

        try
        {
            var json = buildGrid();
            page.SetValue(PageSectionsKey, json, SeedCulture, null);
            var result = _contentService.SaveAndPublish(page, userId: UmbConstants.Security.SuperUserId);

            if (result.Success)
                _logger.LogInformation("PageBlockGridSeeder: seeded '{Name}' block grid.", name);
            else
                _logger.LogError("PageBlockGridSeeder: failed to publish '{Name}' — {Status}", name, result.Result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PageBlockGridSeeder: error seeding '{Name}'.", name);
        }
    }

    // ── JSON helpers ──────────────────────────────────────────────────────────

    // UDI format: umb://element/{guid:N}  (no hyphens in path)
    private static string Udi(Guid g) => $"umb://element/{g:N}";

    // Area key format: {guid:D} (with hyphens, lowercase)
    private static string AreaKey(Guid g) => g.ToString("D");

    // Create a layout-level row (top-level block with areas)
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
            areaArray.Add(new JsonObject
            {
                ["key"]   = AreaKey(aKey),
                ["items"] = itemArray
            });
        }

        return new JsonObject
        {
            ["contentUdi"] = Udi(presetGuid),
            ["columnSpan"] = colSpan,
            ["rowSpan"]    = 1,
            ["areas"]      = areaArray
        };
    }

    // Build a content data entry from a dictionary of property values
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
                null           => null,
                JsonNode n     => n.DeepClone(),
                string s       => JsonValue.Create(s),
                int i          => JsonValue.Create(i),
                bool b         => JsonValue.Create(b),
                _              => JsonValue.Create(value.ToString())
            };
        }

        return entry;
    }

    // Layout preset entries (no user-editable props, just contentTypeKey + udi)
    private static JsonObject PresetEntry(string typeKey, Guid guid)
        => ContentEntry(typeKey, guid, new Dictionary<string, object?>());

    // Serialize the complete grid root
    private static string SerializeGrid(JsonArray layoutItems, JsonArray contentData)
    {
        var root = new JsonObject
        {
            ["layout"]      = new JsonObject { [BgEditorAlias] = layoutItems },
            ["contentData"] = contentData,
            ["settingsData"] = new JsonArray()
        };
        return root.ToJsonString();
    }

    // ── Content factories ─────────────────────────────────────────────────────

    // Helper: DropDown.Flexible raw value = JSON-serialized string e.g. `["h2"]`
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
        ["headingText"]  = headingText,
        ["body"]         = body,
        ["ctaLabel"]     = ctaLabel,
        ["ctaLink"]      = Link(ctaUrl, ctaLabel),
        ["variant"]      = Dropdown(variant)
    };

    private static Dictionary<string, object?> Stat(string value, string label) => new()
    {
        ["title"]    = value,
        ["subtitle"] = label
    };

    private static Dictionary<string, object?> Feature(string title, string subtitle, string summary) => new()
    {
        ["title"]    = title,
        ["subtitle"] = subtitle,
        ["summary"]  = summary
    };

    private static Dictionary<string, object?> Card(string title, string summary, string ctaLabel, string ctaUrl) => new()
    {
        ["title"]    = title,
        ["summary"]  = summary,
        ["ctaLabel"] = ctaLabel,
        ["ctaLink"]  = Link(ctaUrl, ctaLabel)
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

    private static Dictionary<string, object?> Mission(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    private static Dictionary<string, object?> ContactInfo(string title, string body, string ctaLabel, string ctaUrl) => new()
    {
        ["title"]    = title,
        ["body"]     = body,
        ["ctaLabel"] = ctaLabel,
        ["ctaLink"]  = Link(ctaUrl, ctaLabel)
    };

    private static Dictionary<string, object?> KeyValue(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    private static Dictionary<string, object?> MediaText(
        string title, string body,
        string ctaLabel, string ctaUrl) => new()
    {
        ["title"]    = title,
        ["body"]     = body,
        ["ctaLabel"] = ctaLabel,
        ["ctaLink"]  = Link(ctaUrl, ctaLabel)
    };

    // Umbraco multi-URL-picker stored value (JSON array with one link object)
    private static JsonNode Link(string url, string name, string target = "_self")
        => JsonNode.Parse(
            $"[{{\"url\":\"{url}\",\"name\":\"{name}\",\"target\":\"{target}\",\"udi\":null}}]")!;

    // ── Page grid builders ────────────────────────────────────────────────────

    private static string BuildHomeGrid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Hero full-width ────────────────────────────────────────────
        Guid r1 = new("fe000001-0000-0000-0000-000000000000"),
             g1 = new("fe000002-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THero, g1, Hero(
            "Transformación digital para tu empresa",
            "<p>Synergos es la plataforma composable que conecta tu contenido, integraciones y equipo en un solo lugar.</p>",
            "Descubrir nuestros servicios", "/servicios")));

        // ── Row 2: Section heading ────────────────────────────────────────────
        Guid r2 = new("fe000010-0000-0000-0000-000000000000"),
             g2 = new("fe000011-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(THeading, g2, Heading("Synergos en números")));

        // ── Row 3: 4 Stats ────────────────────────────────────────────────────
        Guid r3  = new("fe000020-0000-0000-0000-000000000000"),
             s1  = new("fe000021-0000-0000-0000-000000000000"),
             s2  = new("fe000022-0000-0000-0000-000000000000"),
             s3  = new("fe000023-0000-0000-0000-000000000000"),
             s4  = new("fe000024-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A4Col1, [s1]), (A4Col2, [s2]), (A4Col3, [s3]), (A4Col4, [s4])));
        contentData.Add(PresetEntry(T4Col, r3));
        contentData.Add(ContentEntry(TStat, s1, Stat("150+",  "Clientes satisfechos")));
        contentData.Add(ContentEntry(TStat, s2, Stat("98%",   "Tasa de satisfacción")));
        contentData.Add(ContentEntry(TStat, s3, Stat("50+",   "Integraciones disponibles")));
        contentData.Add(ContentEntry(TStat, s4, Stat("24/7",  "Soporte técnico")));

        // ── Row 4: Why heading ────────────────────────────────────────────────
        Guid r4 = new("fe000030-0000-0000-0000-000000000000"),
             g4 = new("fe000031-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(THeading, g4, Heading("¿Por qué Synergos?")));

        // ── Row 5: 3 Features ─────────────────────────────────────────────────
        Guid r5 = new("fe000040-0000-0000-0000-000000000000"),
             f1 = new("fe000041-0000-0000-0000-000000000000"),
             f2 = new("fe000042-0000-0000-0000-000000000000"),
             f3 = new("fe000043-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A3Col1, [f1]), (A3Col2, [f2]), (A3Col3, [f3])));
        contentData.Add(PresetEntry(T3Col, r5));
        contentData.Add(ContentEntry(TFeature, f1, Feature(
            "Composable",
            "Construye a tu medida",
            "Elige los bloques que necesitas y combínalos sin restricciones. Sin dependencias rígidas ni bloqueos de proveedor.")));
        contentData.Add(ContentEntry(TFeature, f2, Feature(
            "Integrado",
            "Conecta con tu ecosistema",
            "APIs abiertas, webhooks y conectores nativos para tus herramientas favoritas. Todo en un solo flujo.")));
        contentData.Add(ContentEntry(TFeature, f3, Feature(
            "Escalable",
            "Crece sin límites técnicos",
            "Arquitectura cloud-native diseñada para escalar desde 10 hasta 10 millones de usuarios sin cambiar de plataforma.")));

        // ── Row 6: CTA Banner ─────────────────────────────────────────────────
        Guid r6 = new("fe000050-0000-0000-0000-000000000000"),
             g6 = new("fe000051-0000-0000-0000-000000000000");
        layout.Add(Row(r6, 12, (A1Main, [g6])));
        contentData.Add(PresetEntry(T1Col, r6));
        contentData.Add(ContentEntry(TCtaBanner, g6, CtaBanner(
            "¿Listo para transformar tu empresa?",
            "<p>Habla con nuestro equipo y descubre cómo Synergos se adapta a tu negocio desde el primer día.</p>",
            "Solicitar una demo", "/contacto")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildNosotrosGrid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Main heading ───────────────────────────────────────────────
        Guid r1 = new("fe000101-0000-0000-0000-000000000000"),
             g1 = new("fe000102-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Nuestra Historia", "h1")));

        // ── Row 2: Intro paragraph ────────────────────────────────────────────
        Guid r2 = new("fe000110-0000-0000-0000-000000000000"),
             g2 = new("fe000111-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Quiénes somos",
            "<p>Synergos nació de la convicción de que la tecnología empresarial puede ser a la vez poderosa y accesible. " +
            "Desde 2018 ayudamos a organizaciones a modernizar su presencia digital con una plataforma que crece junto a ellas.</p>")));

        // ── Row 3: Mission + Vision ───────────────────────────────────────────
        Guid r3 = new("fe000120-0000-0000-0000-000000000000"),
             m1 = new("fe000121-0000-0000-0000-000000000000"),
             m2 = new("fe000122-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A2Left, [m1]), (A2Right, [m2])));
        contentData.Add(PresetEntry(T2Col, r3));
        contentData.Add(ContentEntry(TMission, m1, Mission(
            "Misión",
            "<p>Democratizar el acceso a tecnología CMS de clase empresarial, permitiendo que cualquier organización " +
            "pueda construir, publicar y optimizar experiencias digitales sin depender de equipos técnicos especializados.</p>")));
        contentData.Add(ContentEntry(TMission, m2, Mission(
            "Visión",
            "<p>Ser la plataforma composable de referencia en Latinoamérica, reconocida por transformar la manera en que " +
            "las empresas crean y gestionan su contenido digital con agilidad, autonomía y excelencia.</p>")));

        // ── Row 4: Values heading ─────────────────────────────────────────────
        Guid r4 = new("fe000130-0000-0000-0000-000000000000"),
             g4 = new("fe000131-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(THeading, g4, Heading("Nuestros Valores")));

        // ── Row 5: 3 Value Features ───────────────────────────────────────────
        Guid r5 = new("fe000140-0000-0000-0000-000000000000"),
             v1 = new("fe000141-0000-0000-0000-000000000000"),
             v2 = new("fe000142-0000-0000-0000-000000000000"),
             v3 = new("fe000143-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A3Col1, [v1]), (A3Col2, [v2]), (A3Col3, [v3])));
        contentData.Add(PresetEntry(T3Col, r5));
        contentData.Add(ContentEntry(TFeature, v1, Feature(
            "Innovación",
            "Siempre un paso adelante",
            "Invertimos continuamente en investigación y desarrollo para ofrecer capacidades que anticipan las necesidades del mercado.")));
        contentData.Add(ContentEntry(TFeature, v2, Feature(
            "Confianza",
            "Relaciones a largo plazo",
            "Construimos relaciones duraderas basadas en transparencia, cumplimiento y resultados medibles que hablan por sí solos.")));
        contentData.Add(ContentEntry(TFeature, v3, Feature(
            "Excelencia",
            "Calidad sin concesiones",
            "Cada línea de código, cada interfaz y cada integración se diseña con el estándar más alto de calidad y usabilidad.")));

        // ── Row 6: Team stats ─────────────────────────────────────────────────
        Guid r6 = new("fe000150-0000-0000-0000-000000000000"),
             t1 = new("fe000151-0000-0000-0000-000000000000"),
             t2 = new("fe000152-0000-0000-0000-000000000000"),
             t3 = new("fe000153-0000-0000-0000-000000000000"),
             t4 = new("fe000154-0000-0000-0000-000000000000");
        layout.Add(Row(r6, 12, (A4Col1, [t1]), (A4Col2, [t2]), (A4Col3, [t3]), (A4Col4, [t4])));
        contentData.Add(PresetEntry(T4Col, r6));
        contentData.Add(ContentEntry(TStat, t1, Stat("8+",  "Años de experiencia")));
        contentData.Add(ContentEntry(TStat, t2, Stat("45",  "Profesionales en equipo")));
        contentData.Add(ContentEntry(TStat, t3, Stat("200+","Proyectos entregados")));
        contentData.Add(ContentEntry(TStat, t4, Stat("12",  "Países con presencia")));

        // ── Row 7: CTA ────────────────────────────────────────────────────────
        Guid r7 = new("fe000160-0000-0000-0000-000000000000"),
             g7 = new("fe000161-0000-0000-0000-000000000000");
        layout.Add(Row(r7, 12, (A1Main, [g7])));
        contentData.Add(PresetEntry(T1Col, r7));
        contentData.Add(ContentEntry(TCtaBanner, g7, CtaBanner(
            "Únete a nuestra historia",
            "<p>¿Quieres ser parte de la transformación digital de la región? Hablemos sobre cómo podemos colaborar.</p>",
            "Contáctanos", "/contacto")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildServiciosGrid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Main heading ───────────────────────────────────────────────
        Guid r1 = new("fe000201-0000-0000-0000-000000000000"),
             g1 = new("fe000202-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Nuestros Servicios", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fe000210-0000-0000-0000-000000000000"),
             g2 = new("fe000211-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Soluciones a la medida de tu empresa",
            "<p>Ofrecemos un ecosistema completo de servicios digitales que se adaptan a las necesidades y el ritmo de cada organización, " +
            "desde startups hasta grandes corporaciones con requisitos enterprise.</p>")));

        // ── Row 3: 3 Service cards ────────────────────────────────────────────
        Guid r3 = new("fe000220-0000-0000-0000-000000000000"),
             c1 = new("fe000221-0000-0000-0000-000000000000"),
             c2 = new("fe000222-0000-0000-0000-000000000000"),
             c3 = new("fe000223-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A3Col1, [c1]), (A3Col2, [c2]), (A3Col3, [c3])));
        contentData.Add(PresetEntry(T3Col, r3));
        contentData.Add(ContentEntry(TCard, c1, Card(
            "Consultoría Digital",
            "Diagnóstico, estrategia y hoja de ruta para tu transformación digital. Acompañamos a tu equipo desde la visión hasta la ejecución.",
            "Saber más", "/contacto")));
        contentData.Add(ContentEntry(TCard, c2, Card(
            "CMS Empresarial",
            "Implementación y personalización de Synergos CMS adaptado a los flujos editoriales y de gobierno de tu organización.",
            "Saber más", "/contacto")));
        contentData.Add(ContentEntry(TCard, c3, Card(
            "Integraciones",
            "Conectamos tu CMS con ERP, CRM, plataformas de marketing, analytics y cualquier API de tu ecosistema tecnológico.",
            "Saber más", "/contacto")));

        // ── Row 4: Process heading ────────────────────────────────────────────
        Guid r4 = new("fe000230-0000-0000-0000-000000000000"),
             g4 = new("fe000231-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(THeading, g4, Heading("Nuestro Proceso")));

        // ── Row 5: 4 Process steps ────────────────────────────────────────────
        Guid r5 = new("fe000240-0000-0000-0000-000000000000"),
             p1 = new("fe000241-0000-0000-0000-000000000000"),
             p2 = new("fe000242-0000-0000-0000-000000000000"),
             p3 = new("fe000243-0000-0000-0000-000000000000"),
             p4 = new("fe000244-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A4Col1, [p1]), (A4Col2, [p2]), (A4Col3, [p3]), (A4Col4, [p4])));
        contentData.Add(PresetEntry(T4Col, r5));
        contentData.Add(ContentEntry(TStat, p1, Stat("01", "Diagnóstico y estrategia")));
        contentData.Add(ContentEntry(TStat, p2, Stat("02", "Diseño y arquitectura")));
        contentData.Add(ContentEntry(TStat, p3, Stat("03", "Desarrollo e integración")));
        contentData.Add(ContentEntry(TStat, p4, Stat("04", "Lanzamiento y soporte")));

        // ── Row 6: Media + Text split ─────────────────────────────────────────
        Guid r6 = new("fe000250-0000-0000-0000-000000000000"),
             g6 = new("fe000251-0000-0000-0000-000000000000");
        layout.Add(Row(r6, 12, (A1Main, [g6])));
        contentData.Add(PresetEntry(T1Col, r6));
        contentData.Add(ContentEntry(TMediaText, g6, MediaText(
            "Tecnología que trabaja para ti",
            "<p>Nuestro equipo de especialistas combina expertise técnico con comprensión del negocio para entregar " +
            "soluciones que realmente funcionan en el mundo real — con plazos cumplidos y resultados medibles.</p>",
            "Hablar con un especialista", "/contacto")));

        // ── Row 7: CTA ────────────────────────────────────────────────────────
        Guid r7 = new("fe000260-0000-0000-0000-000000000000"),
             g7 = new("fe000261-0000-0000-0000-000000000000");
        layout.Add(Row(r7, 12, (A1Main, [g7])));
        contentData.Add(PresetEntry(T1Col, r7));
        contentData.Add(ContentEntry(TCtaBanner, g7, CtaBanner(
            "¿Tienes un proyecto en mente?",
            "<p>Cuéntanos qué necesitas y te mostraremos cómo Synergos puede ayudarte a lograrlo más rápido y con menos riesgo.</p>",
            "Solicitar propuesta", "/contacto")));

        return SerializeGrid(layout, contentData);
    }

    private static string BuildContactoGrid()
    {
        var layout      = new JsonArray();
        var contentData = new JsonArray();

        // ── Row 1: Main heading ───────────────────────────────────────────────
        Guid r1 = new("fe000301-0000-0000-0000-000000000000"),
             g1 = new("fe000302-0000-0000-0000-000000000000");
        layout.Add(Row(r1, 12, (A1Main, [g1])));
        contentData.Add(PresetEntry(T1Col, r1));
        contentData.Add(ContentEntry(THeading, g1, Heading("Ponte en Contacto", "h1")));

        // ── Row 2: Intro ──────────────────────────────────────────────────────
        Guid r2 = new("fe000310-0000-0000-0000-000000000000"),
             g2 = new("fe000311-0000-0000-0000-000000000000");
        layout.Add(Row(r2, 12, (A1Main, [g2])));
        contentData.Add(PresetEntry(T1Col, r2));
        contentData.Add(ContentEntry(TParagraph, g2, Paragraph(
            "Estamos aquí para ayudarte",
            "<p>¿Tienes preguntas, quieres una demo o estás listo para iniciar tu proyecto? Nuestro equipo responde en menos de 24 horas.</p>")));

        // ── Row 3: 2 Contact blocks ───────────────────────────────────────────
        Guid r3 = new("fe000320-0000-0000-0000-000000000000"),
             ci1 = new("fe000321-0000-0000-0000-000000000000"),
             ci2 = new("fe000322-0000-0000-0000-000000000000");
        layout.Add(Row(r3, 12, (A2Left, [ci1]), (A2Right, [ci2])));
        contentData.Add(PresetEntry(T2Col, r3));
        contentData.Add(ContentEntry(TContact, ci1, ContactInfo(
            "Escríbenos",
            "<p>Envíanos un correo y te respondemos en menos de un día hábil.</p>" +
            "<p><strong>Email:</strong> hola@synergos.com<br/><strong>Soporte:</strong> soporte@synergos.com</p>",
            "Enviar correo", "mailto:hola@synergos.com")));
        contentData.Add(ContentEntry(TContact, ci2, ContactInfo(
            "Nuestra Oficina",
            "<p>Visítanos o llámanos directamente.</p>" +
            "<p><strong>Dirección:</strong> Calle 72 # 10-07, Bogotá<br/><strong>Teléfono:</strong> +57 1 234 5678</p>",
            "Ver en mapa", "https://maps.google.com")));

        // ── Row 4: FAQ heading ────────────────────────────────────────────────
        Guid r4 = new("fe000330-0000-0000-0000-000000000000"),
             g4 = new("fe000331-0000-0000-0000-000000000000");
        layout.Add(Row(r4, 12, (A1Main, [g4])));
        contentData.Add(PresetEntry(T1Col, r4));
        contentData.Add(ContentEntry(THeading, g4, Heading("Preguntas Frecuentes")));

        // ── Row 5: 3 FAQ items ────────────────────────────────────────────────
        Guid r5 = new("fe000340-0000-0000-0000-000000000000"),
             q1 = new("fe000341-0000-0000-0000-000000000000"),
             q2 = new("fe000342-0000-0000-0000-000000000000"),
             q3 = new("fe000343-0000-0000-0000-000000000000");
        layout.Add(Row(r5, 12, (A3Col1, [q1]), (A3Col2, [q2]), (A3Col3, [q3])));
        contentData.Add(PresetEntry(T3Col, r5));
        contentData.Add(ContentEntry(TKeyValue, q1, KeyValue(
            "¿Cuánto tiempo toma implementar Synergos?",
            "<p>Un sitio estándar tarda entre 4 y 8 semanas. Proyectos con integraciones complejas pueden requerir 3 meses. Siempre iniciamos con una fase de diagnóstico.</p>")));
        contentData.Add(ContentEntry(TKeyValue, q2, KeyValue(
            "¿Ofrecen soporte después del lanzamiento?",
            "<p>Sí. Todos nuestros proyectos incluyen 3 meses de soporte post-lanzamiento y planes de mantenimiento continuo según las necesidades de cada cliente.</p>")));
        contentData.Add(ContentEntry(TKeyValue, q3, KeyValue(
            "¿Puedo migrar mi contenido existente?",
            "<p>Absolutamente. Contamos con herramientas de migración para los CMS más populares (WordPress, Drupal, Contentful) y desarrollamos migraciones personalizadas cuando es necesario.</p>")));

        return SerializeGrid(layout, contentData);
    }
}
