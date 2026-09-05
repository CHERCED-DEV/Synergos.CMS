using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only seeder que crea la jerarquía completa Synergos:
/// platformRoot → siteRoot "Synergos" → 3 páginas pageBase
/// (Home, Identidad, Contacto) con Layout Composer config real.
/// </summary>
/// <remarks>
/// Invocado explícitamente vía <c>POST /dev/seed-synergos-identity</c>,
/// gated por <c>Synergos:DevSeed:Enabled=true</c> (ADR 0013).
/// Las páginas se llenan con bloques mixtos: SSR puros (siempre
/// renderizan) y elementSyn* (emiten placeholder offline mientras el
/// CDN team no publique el endpoint, ADR 0012).
/// </remarks>
public sealed class SynergosIdentitySeeder
{
    private const string DefaultCulture = "es-CO";
    private const string PlatformName = "Synergos Platform";
    private const string SiteName = "Synergos";

    // ContentType aliases
    private const string PlatformRootAlias = "platformRoot";
    private const string SiteRootAlias = "siteRoot";
    private const string PageBaseAlias = "pageBase";

    // DocType Keys (cosechados de uSync/v9/ContentTypes/)
    private static readonly Guid ElementLayoutSection = new("1c68f4a9-24e9-49ac-9efa-05b3d4b1404a");
    private static readonly Guid ElementCompHero = new("65a8b66a-be4f-4722-a5b2-42fc57a73ebb");
    private static readonly Guid ElementCorpMissionBlock = new("2717b9e7-debe-4354-8dfd-780f88f1842c");
    private static readonly Guid ElementCompFeatureGrid = new("178778fd-9193-4a43-a48d-5518c4a3f720");
    private static readonly Guid ElementInfoFeature = new("a50d36ba-030f-4036-959d-b82361e6dbee");
    private static readonly Guid ElementCompCtaBanner = new("5c21b68f-a4d6-4ef4-aba0-771517acb556");
    private static readonly Guid ElementCompCard = new("c646696c-8819-4630-bc7a-121f0839bb16");
    private static readonly Guid ElementSynHeroBanner = new("637eccc8-2c40-4b65-b2f6-8b7694e10c0d");
    private static readonly Guid ElementSynStatTicker = new("4051fd78-db88-43a8-98db-c96fa502d215");
    private static readonly Guid ElementSynTimeline = new("66976d3e-0d73-4634-8f69-4744efd30724");
    private static readonly Guid ElementInfoStat = new("b1157cc1-762d-4c6f-a8f1-dc42d5bb891a");
    private static readonly Guid ElementInfoTimelineItem = new("f845c403-0300-45e6-aa4f-1f1aa00cbdb4");

    // Area Keys del DataType DTBlockGridSections — sectionContent es el
    // único area que define elementLayoutSection.
    private static readonly Guid SectionContentAreaKey = new("3525d41c-ae84-47ac-9297-2148f6a4aae8");

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly ILogger<SynergosIdentitySeeder> _logger;

    public SynergosIdentitySeeder(
        IContentService contentService,
        IContentTypeService contentTypeService,
        ILogger<SynergosIdentitySeeder> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _logger = logger;
    }

    /// <summary>
    /// Borra TODO el content tree y vacía la papelera. Destructivo.
    /// </summary>
    public ClearResult ClearAll()
    {
        var roots = _contentService.GetRootContent().ToList();
        var count = 0;
        foreach (var root in roots)
        {
            count += _contentService.CountDescendants(root.Id) + 1;
            _contentService.MoveToRecycleBin(root);
        }
        _contentService.EmptyRecycleBin(-1);
        _logger.LogInformation("SynergosSeed: ClearAll eliminó {Count} nodos.", count);
        return new ClearResult(true, count, "ok");
    }

    public SeedResult Seed()
    {
        if (!ValidateContentTypes(out var missing))
        {
            _logger.LogError("SynergosSeed: ContentTypes faltantes: {Missing}", string.Join(", ", missing));
            return new SeedResult(false, -1, -1, 0, $"missing-content-types:{string.Join(',', missing)}");
        }

        // 1. platformRoot
        var platform = _contentService.Create(PlatformName, Constants.System.Root, PlatformRootAlias);
        platform.SetCultureName(PlatformName, DefaultCulture);
        platform.SetValue("welcomeMessage", "Plataforma editorial Synergos — un código, múltiples productos.", DefaultCulture);
        var savePlatform = _contentService.SaveAndPublish(platform, new[] { DefaultCulture });
        if (!savePlatform.Success)
        {
            _logger.LogError("SynergosSeed: fallo creando platformRoot. {Result}", savePlatform.Result);
            return new SeedResult(false, -1, -1, 0, $"platform-save-failed:{savePlatform.Result}");
        }
        _logger.LogInformation("SynergosSeed: platformRoot creado id={Id}", platform.Id);

        // 2. siteRoot
        var site = _contentService.Create(SiteName, platform.Id, SiteRootAlias);
        site.SetCultureName(SiteName, DefaultCulture);
        site.SetValue("siteDisplayName", "Synergos", DefaultCulture);
        site.SetValue("canonicalHostname", "synergos.local");
        site.SetValue("brandKey", "synergos");
        site.SetValue("brandDisplayName", "Synergos", DefaultCulture);
        var saveSite = _contentService.SaveAndPublish(site, new[] { DefaultCulture });
        if (!saveSite.Success)
        {
            _logger.LogError("SynergosSeed: fallo creando siteRoot. {Result}", saveSite.Result);
            return new SeedResult(false, platform.Id, -1, 0, $"site-save-failed:{saveSite.Result}");
        }
        _logger.LogInformation("SynergosSeed: siteRoot creado id={Id}", site.Id);

        // 3. Páginas
        var pagesCreated = 0;
        pagesCreated += CreatePage(site.Id, "Home", BuildHomeSections()) ? 1 : 0;
        pagesCreated += CreatePage(site.Id, "Identidad", BuildIdentitySections()) ? 1 : 0;
        pagesCreated += CreatePage(site.Id, "Contacto", BuildContactSections()) ? 1 : 0;

        return new SeedResult(true, platform.Id, site.Id, pagesCreated, "ok");
    }

    private bool ValidateContentTypes(out List<string> missing)
    {
        missing = new List<string>();
        foreach (var alias in new[] { PlatformRootAlias, SiteRootAlias, PageBaseAlias })
        {
            if (_contentTypeService.Get(alias) is null)
            {
                missing.Add(alias);
            }
        }
        return missing.Count == 0;
    }

    private bool CreatePage(int parentId, string name, string sectionsJson)
    {
        try
        {
            var page = _contentService.Create(name, parentId, PageBaseAlias);
            page.SetCultureName(name, DefaultCulture);
            page.SetValue("heading", ResolveHeading(name), DefaultCulture);

            // Block Grid: el cuerpo se puebla por separado vía
            // DevContentFiller (POST /dev/fill-synergos-pages), que aplica
            // SchemaBlockDefaults para sembrar las props multi-value con
            // "[]" y evitar el JsonReaderException en MultipleValueEditor.
            // Aquí el seeder crea solo la ESTRUCTURA publicable; el relleno
            // de secciones es responsabilidad del filler. Ver ADR 0093 +
            // synergos-content-fill. (Para auto-poblar al crear, inyectar
            // SchemaBlockDefaults y aplicar ApplyDefaults por bloque como en
            // DevContentFiller, luego SetValue("sections", json, culture).)
            _ = sectionsJson; // construido por las Build* como referencia de shape

            // SEO basics
            page.SetValue("seoTitle", $"{name} — Synergos", DefaultCulture);
            page.SetValue("seoDescription", ResolveSeoDescription(name), DefaultCulture);

            var save = _contentService.SaveAndPublish(page, new[] { DefaultCulture });
            if (save.Success)
            {
                _logger.LogInformation("SynergosSeed: pageBase '{Name}' creado id={Id}", name, page.Id);
                return true;
            }
            _logger.LogWarning("SynergosSeed: pageBase '{Name}' fallo guardar. {Result}", name, save.Result);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SynergosSeed: excepción creando pageBase '{Name}'.", name);
            return false;
        }
    }

    private static string ResolveHeading(string pageName) => pageName switch
    {
        "Home" => "Una plataforma. Mil productos.",
        "Identidad" => "Construimos la plataforma donde tu producto se vuelve mil productos",
        "Contacto" => "Hablemos",
        _ => pageName,
    };

    private static string ResolveSeoDescription(string pageName) => pageName switch
    {
        "Home" => "Synergos es el motor editorial detrás de marcas profesionales, e-commerce, portales de membresía y experiencias corporativas.",
        // Sin cifra a propósito: esto es copy que ve un visitante, y decía 122 cuando el CDN
        // publica 130 (#86). Un número en una descripción SEO no se actualiza nunca —nadie lo
        // cruza con nada— así que la frase se escribe para que no pueda envejecer.
        "Identidad" => "Synergos es un CMS empresarial polimórfico: un código, un schema, un catálogo de elementos UI. Conoce el equipo y la visión.",
        "Contacto" => "Agenda una sesión técnica con el equipo de plataforma.",
        _ => string.Empty,
    };

    // Aliases reales del schema (cosechados de uSync/v9/ContentTypes/):
    //   compContentHeading: headingTitle, headingSubtitle
    //   compContentText:    textBody
    //   compContentMedia:   mediaReference, mediaAlt (MANDATORY), mediaCaption
    //   compContentCta:     ctaLabel, ctaLink, ctaOpenInNewTab
    //   elementSynHeroBanner: title (MANDATORY propio)
    // FeatureGrid / StatTicker / Timeline requieren BlockList anidado o
    // mandatory fields complejos — el arquitecto los agrega en backoffice.
    private const string MediaAltPlaceholder = "Imagen pendiente";

    private static string BuildHomeSections()
    {
        var b = new BlockGridJsonBuilder();

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementSynHeroBanner,
            hero => hero
                .Set("title", "Una plataforma. Mil productos."));

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementCompCtaBanner,
            cta => cta
                .Set("headingTitle", "¿Listo para construir el tuyo?")
                .Set("headingSubtitle", "Agenda una sesión técnica y te mostramos el grafo de capas.")
                .Set("ctaLabel", "Agendar sesión"));

        return b.Build();
    }

    private static string BuildIdentitySections()
    {
        var b = new BlockGridJsonBuilder();

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementSynHeroBanner,
            hero => hero
                .Set("title", "Una plataforma. Mil productos."));

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementCorpMissionBlock,
            mission => mission
                .Set("headingTitle", "Nuestro propósito")
                .Set("headingSubtitle", "Hacia dónde vamos")
                .Set("textBody", "Hacer que las decisiones de arquitectura escalen. Cada vertical profesional, cada marca, cada portal de membresía debería poder desplegarse en días — no en trimestres — sin reescribir código. Un futuro donde el schema editorial sea tan extensible como un lenguaje.")
                .Set("mediaAlt", MediaAltPlaceholder));

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementCompCtaBanner,
            cta => cta
                .Set("headingTitle", "¿Quieres ver Synergos por dentro?")
                .Set("headingSubtitle", "Agenda una sesión técnica con el equipo de plataforma.")
                .Set("ctaLabel", "Agendar sesión técnica"));

        return b.Build();
    }

    private static string BuildContactSections()
    {
        var b = new BlockGridJsonBuilder();

        // Mission Block (no requiere ctaItems mín. 1 como Hero) para
        // intro de la página.
        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementCorpMissionBlock,
            intro => intro
                .Set("headingTitle", "Hablemos")
                .Set("headingSubtitle", "Estamos disponibles para sesiones técnicas, demos y consultas de integración.")
                .Set("textBody", "Una llamada de 30 minutos con el equipo de plataforma para resolver cualquier duda — desde dudas de schema hasta integración con el CDN.")
                .Set("mediaAlt", MediaAltPlaceholder));

        b.AddTopLevelBlock(ElementLayoutSection).AddChild(
            SectionContentAreaKey, ElementCompCtaBanner,
            cta => cta
                .Set("headingTitle", "Agenda una sesión")
                .Set("headingSubtitle", "30 minutos con el equipo de plataforma.")
                .Set("ctaLabel", "Agendar"));

        return b.Build();
    }

    public sealed record SeedResult(bool Success, int PlatformId, int SiteId, int PagesCreated, string Detail);
    public sealed record ClearResult(bool Success, int NodesDeleted, string Detail);
}
