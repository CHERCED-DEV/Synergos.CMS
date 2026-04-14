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
/// Seeds the <c>pageSections</c> Block Grid for the four Synergos demo pages
/// on a fresh install. Idempotent — skips any page whose pageSections is
/// already populated (editor data is never overwritten).
///
/// Composition strategy per page:
///   Home      — Hero → Stats → Features → CtaBanner
///   Nosotros  — Heading → Paragraph → Mission/Vision → Values → Stats → CtaBanner
///   Servicios — Heading → Paragraph → Cards → Process → MediaText → CtaBanner
///   Contacto  — Heading → Paragraph → ContactInfo → FAQ
///
/// All hardcoded GUIDs were removed in favour of <see cref="ContentTypeKeys"/>,
/// <see cref="BlockGridAreaKeys"/>, and per-block <see cref="Guid.NewGuid"/>.
/// Magic strings ("h1", "dark", …) are typed via <see cref="HeadingLevel"/>
/// and <see cref="BlockVariant"/> enums.
/// </summary>
internal sealed class PageBlockGridSeeder
{
    private const string PageSectionsKey = "pageSections";
    private const string SeedCulture     = "es-CO";

    private readonly IContentService _contentService;
    private readonly ILogger         _logger;

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

    // ── Infrastructure ──────────────────────────────────────────────────────

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

    // ── Content factories — pure data, return prop dictionaries ─────────────

    private static Dictionary<string, object?> Heading(string text, HeadingLevel level = HeadingLevel.H2) => new()
    {
        ["headingText"]  = text,
        ["headingLevel"] = BlockGridJsonBuilder.DropdownValue(level.ToAlias())
    };

    private static Dictionary<string, object?> Paragraph(string title, string body) => new()
    {
        ["title"] = title,
        ["body"]  = body
    };

    private static Dictionary<string, object?> Hero(
        string headingText, string body,
        string ctaLabel, string ctaUrl,
        BlockVariant variant = BlockVariant.Dark) => new()
    {
        ["headingText"] = headingText,
        ["body"]        = body,
        ["ctaLabel"]    = ctaLabel,
        ["ctaLink"]     = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel),
        ["variant"]     = BlockGridJsonBuilder.DropdownValue(variant.ToAlias())
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
        ["ctaLink"]  = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel)
    };

    private static Dictionary<string, object?> CtaBanner(
        string title, string body,
        string ctaLabel, string ctaUrl,
        BlockVariant variant = BlockVariant.Dark) => new()
    {
        ["title"]    = title,
        ["body"]     = body,
        ["ctaLabel"] = ctaLabel,
        ["ctaLink"]  = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel),
        ["variant"]  = BlockGridJsonBuilder.DropdownValue(variant.ToAlias())
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
        ["ctaLink"]  = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel)
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
        ["ctaLink"]  = BlockGridJsonBuilder.SingleLink(ctaUrl, ctaLabel)
    };

    // ── Page grid builders ──────────────────────────────────────────────────
    // Each builder is a pure declaration of "what blocks live on this page".
    // Per-block UDIs are auto-generated; no GUID literals here.

    private static string BuildHomeGrid()
    {
        var b = new BlockGridJsonBuilder();

        // Row 1: Hero full-width
        Guid heroPreset = Guid.NewGuid(), heroBlock = Guid.NewGuid();
        b.AddRow(heroPreset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { heroBlock }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, heroPreset);
        b.AddContent(ContentTypeKeys.ElementCompHero, heroBlock, Hero(
            "Transformación digital para tu empresa",
            "<p>Synergos es la plataforma composable que conecta tu contenido, integraciones y equipo en un solo lugar.</p>",
            "Descubrir nuestros servicios", "/servicios"));

        // Row 2: Stats heading
        AddSingleHeading(b, "Synergos en números");

        // Row 3: 4 Stats
        AddFourStats(b,
            ("150+",  "Clientes satisfechos"),
            ("98%",   "Tasa de satisfacción"),
            ("50+",   "Integraciones disponibles"),
            ("24/7",  "Soporte técnico"));

        // Row 4: Why heading
        AddSingleHeading(b, "¿Por qué Synergos?");

        // Row 5: 3 Features
        AddThreeFeatures(b,
            ("Composable", "Construye a tu medida",
             "Elige los bloques que necesitas y combínalos sin restricciones. Sin dependencias rígidas ni bloqueos de proveedor."),
            ("Integrado",  "Conecta con tu ecosistema",
             "APIs abiertas, webhooks y conectores nativos para tus herramientas favoritas. Todo en un solo flujo."),
            ("Escalable",  "Crece sin límites técnicos",
             "Arquitectura cloud-native diseñada para escalar desde 10 hasta 10 millones de usuarios sin cambiar de plataforma."));

        // Row 6: CTA Banner
        AddCtaBanner(b,
            "¿Listo para transformar tu empresa?",
            "<p>Habla con nuestro equipo y descubre cómo Synergos se adapta a tu negocio desde el primer día.</p>",
            "Solicitar una demo", "/contacto");

        return b.ToJson();
    }

    private static string BuildNosotrosGrid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Nuestra Historia", HeadingLevel.H1);

        AddSingleParagraph(b, "Quiénes somos",
            "<p>Synergos nació de la convicción de que la tecnología empresarial puede ser a la vez poderosa y accesible. " +
            "Desde 2018 ayudamos a organizaciones a modernizar su presencia digital con una plataforma que crece junto a ellas.</p>");

        // Mission + Vision side by side
        Guid mvPreset = Guid.NewGuid(), missionBlock = Guid.NewGuid(), visionBlock = Guid.NewGuid();
        b.AddRow(mvPreset, 12,
            (BlockGridAreaKeys.Preset2ColLeft,  new[] { missionBlock }),
            (BlockGridAreaKeys.Preset2ColRight, new[] { visionBlock  }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset2ColEqual, mvPreset);
        b.AddContent(ContentTypeKeys.ElementCorpMissionBlock, missionBlock, Mission(
            "Misión",
            "<p>Democratizar el acceso a tecnología CMS de clase empresarial, permitiendo que cualquier organización " +
            "pueda construir, publicar y optimizar experiencias digitales sin depender de equipos técnicos especializados.</p>"));
        b.AddContent(ContentTypeKeys.ElementCorpMissionBlock, visionBlock, Mission(
            "Visión",
            "<p>Ser la plataforma composable de referencia en Latinoamérica, reconocida por transformar la manera en que " +
            "las empresas crean y gestionan su contenido digital con agilidad, autonomía y excelencia.</p>"));

        AddSingleHeading(b, "Nuestros Valores");

        AddThreeFeatures(b,
            ("Innovación", "Siempre un paso adelante",
             "Invertimos continuamente en investigación y desarrollo para ofrecer capacidades que anticipan las necesidades del mercado."),
            ("Confianza",  "Relaciones a largo plazo",
             "Construimos relaciones duraderas basadas en transparencia, cumplimiento y resultados medibles que hablan por sí solos."),
            ("Excelencia", "Calidad sin concesiones",
             "Cada línea de código, cada interfaz y cada integración se diseña con el estándar más alto de calidad y usabilidad."));

        AddFourStats(b,
            ("8+",   "Años de experiencia"),
            ("45",   "Profesionales en equipo"),
            ("200+", "Proyectos entregados"),
            ("12",   "Países con presencia"));

        AddCtaBanner(b,
            "Únete a nuestra historia",
            "<p>¿Quieres ser parte de la transformación digital de la región? Hablemos sobre cómo podemos colaborar.</p>",
            "Contáctanos", "/contacto");

        return b.ToJson();
    }

    private static string BuildServiciosGrid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Nuestros Servicios", HeadingLevel.H1);

        AddSingleParagraph(b, "Soluciones a la medida de tu empresa",
            "<p>Ofrecemos un ecosistema completo de servicios digitales que se adaptan a las necesidades y el ritmo de cada organización, " +
            "desde startups hasta grandes corporaciones con requisitos enterprise.</p>");

        // 3 Service cards
        Guid cardsPreset = Guid.NewGuid(),
             card1 = Guid.NewGuid(), card2 = Guid.NewGuid(), card3 = Guid.NewGuid();
        b.AddRow(cardsPreset, 12,
            (BlockGridAreaKeys.Preset3Col1, new[] { card1 }),
            (BlockGridAreaKeys.Preset3Col2, new[] { card2 }),
            (BlockGridAreaKeys.Preset3Col3, new[] { card3 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset3ColEqual, cardsPreset);
        b.AddContent(ContentTypeKeys.ElementCompCard, card1, Card(
            "Consultoría Digital",
            "Diagnóstico, estrategia y hoja de ruta para tu transformación digital. Acompañamos a tu equipo desde la visión hasta la ejecución.",
            "Saber más", "/contacto"));
        b.AddContent(ContentTypeKeys.ElementCompCard, card2, Card(
            "CMS Empresarial",
            "Implementación y personalización de Synergos CMS adaptado a los flujos editoriales y de gobierno de tu organización.",
            "Saber más", "/contacto"));
        b.AddContent(ContentTypeKeys.ElementCompCard, card3, Card(
            "Integraciones",
            "Conectamos tu CMS con ERP, CRM, plataformas de marketing, analytics y cualquier API de tu ecosistema tecnológico.",
            "Saber más", "/contacto"));

        AddSingleHeading(b, "Nuestro Proceso");

        AddFourStats(b,
            ("01", "Diagnóstico y estrategia"),
            ("02", "Diseño y arquitectura"),
            ("03", "Desarrollo e integración"),
            ("04", "Lanzamiento y soporte"));

        // Media + Text split
        Guid mtPreset = Guid.NewGuid(), mtBlock = Guid.NewGuid();
        b.AddRow(mtPreset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { mtBlock }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, mtPreset);
        b.AddContent(ContentTypeKeys.ElementCompMediaTextSplit, mtBlock, MediaText(
            "Tecnología que trabaja para ti",
            "<p>Nuestro equipo de especialistas combina expertise técnico con comprensión del negocio para entregar " +
            "soluciones que realmente funcionan en el mundo real — con plazos cumplidos y resultados medibles.</p>",
            "Hablar con un especialista", "/contacto"));

        AddCtaBanner(b,
            "¿Tienes un proyecto en mente?",
            "<p>Cuéntanos qué necesitas y te mostraremos cómo Synergos puede ayudarte a lograrlo más rápido y con menos riesgo.</p>",
            "Solicitar propuesta", "/contacto");

        return b.ToJson();
    }

    private static string BuildContactoGrid()
    {
        var b = new BlockGridJsonBuilder();

        AddSingleHeading(b, "Ponte en Contacto", HeadingLevel.H1);

        AddSingleParagraph(b, "Estamos aquí para ayudarte",
            "<p>¿Tienes preguntas, quieres una demo o estás listo para iniciar tu proyecto? Nuestro equipo responde en menos de 24 horas.</p>");

        // 2 Contact blocks side by side
        Guid contactPreset = Guid.NewGuid(), contact1 = Guid.NewGuid(), contact2 = Guid.NewGuid();
        b.AddRow(contactPreset, 12,
            (BlockGridAreaKeys.Preset2ColLeft,  new[] { contact1 }),
            (BlockGridAreaKeys.Preset2ColRight, new[] { contact2 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset2ColEqual, contactPreset);
        b.AddContent(ContentTypeKeys.ElementCorpContactInfo, contact1, ContactInfo(
            "Escríbenos",
            "<p>Envíanos un correo y te respondemos en menos de un día hábil.</p>" +
            "<p><strong>Email:</strong> hola@synergos.com<br/><strong>Soporte:</strong> soporte@synergos.com</p>",
            "Enviar correo", "mailto:hola@synergos.com"));
        b.AddContent(ContentTypeKeys.ElementCorpContactInfo, contact2, ContactInfo(
            "Nuestra Oficina",
            "<p>Visítanos o llámanos directamente.</p>" +
            "<p><strong>Dirección:</strong> Calle 72 # 10-07, Bogotá<br/><strong>Teléfono:</strong> +57 1 234 5678</p>",
            "Ver en mapa", "https://maps.google.com"));

        AddSingleHeading(b, "Preguntas Frecuentes");

        // 3 FAQ items
        Guid faqPreset = Guid.NewGuid(), q1 = Guid.NewGuid(), q2 = Guid.NewGuid(), q3 = Guid.NewGuid();
        b.AddRow(faqPreset, 12,
            (BlockGridAreaKeys.Preset3Col1, new[] { q1 }),
            (BlockGridAreaKeys.Preset3Col2, new[] { q2 }),
            (BlockGridAreaKeys.Preset3Col3, new[] { q3 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset3ColEqual, faqPreset);
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, q1, KeyValue(
            "¿Cuánto tiempo toma implementar Synergos?",
            "<p>Un sitio estándar tarda entre 4 y 8 semanas. Proyectos con integraciones complejas pueden requerir 3 meses. Siempre iniciamos con una fase de diagnóstico.</p>"));
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, q2, KeyValue(
            "¿Ofrecen soporte después del lanzamiento?",
            "<p>Sí. Todos nuestros proyectos incluyen 3 meses de soporte post-lanzamiento y planes de mantenimiento continuo según las necesidades de cada cliente.</p>"));
        b.AddContent(ContentTypeKeys.ElementInfoKeyValue, q3, KeyValue(
            "¿Puedo migrar mi contenido existente?",
            "<p>Absolutamente. Contamos con herramientas de migración para los CMS más populares (WordPress, Drupal, Contentful) y desarrollamos migraciones personalizadas cuando es necesario.</p>"));

        return b.ToJson();
    }

    // ── Layout shorthands — encapsulate repeated row patterns ───────────────

    private static void AddSingleHeading(
        BlockGridJsonBuilder b, string text, HeadingLevel level = HeadingLevel.H2)
    {
        Guid preset = Guid.NewGuid(), heading = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { heading }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementTextHeading, heading, Heading(text, level));
    }

    private static void AddSingleParagraph(BlockGridJsonBuilder b, string title, string body)
    {
        Guid preset = Guid.NewGuid(), paragraph = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { paragraph }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementTextParagraph, paragraph, Paragraph(title, body));
    }

    private static void AddCtaBanner(
        BlockGridJsonBuilder b, string title, string body, string ctaLabel, string ctaUrl)
    {
        Guid preset = Guid.NewGuid(), banner = Guid.NewGuid();
        b.AddRow(preset, 12, (BlockGridAreaKeys.Preset1ColMain, new[] { banner }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset1Col, preset);
        b.AddContent(ContentTypeKeys.ElementCompCtaBanner, banner, CtaBanner(title, body, ctaLabel, ctaUrl));
    }

    private static void AddFourStats(BlockGridJsonBuilder b,
        (string Value, string Label) s1,
        (string Value, string Label) s2,
        (string Value, string Label) s3,
        (string Value, string Label) s4)
    {
        Guid preset = Guid.NewGuid();
        Guid b1 = Guid.NewGuid(), b2 = Guid.NewGuid(), b3 = Guid.NewGuid(), b4 = Guid.NewGuid();
        b.AddRow(preset, 12,
            (BlockGridAreaKeys.Preset4Col1, new[] { b1 }),
            (BlockGridAreaKeys.Preset4Col2, new[] { b2 }),
            (BlockGridAreaKeys.Preset4Col3, new[] { b3 }),
            (BlockGridAreaKeys.Preset4Col4, new[] { b4 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset4ColEqual, preset);
        b.AddContent(ContentTypeKeys.ElementInfoStat, b1, Stat(s1.Value, s1.Label));
        b.AddContent(ContentTypeKeys.ElementInfoStat, b2, Stat(s2.Value, s2.Label));
        b.AddContent(ContentTypeKeys.ElementInfoStat, b3, Stat(s3.Value, s3.Label));
        b.AddContent(ContentTypeKeys.ElementInfoStat, b4, Stat(s4.Value, s4.Label));
    }

    private static void AddThreeFeatures(BlockGridJsonBuilder b,
        (string Title, string Subtitle, string Summary) f1,
        (string Title, string Subtitle, string Summary) f2,
        (string Title, string Subtitle, string Summary) f3)
    {
        Guid preset = Guid.NewGuid();
        Guid b1 = Guid.NewGuid(), b2 = Guid.NewGuid(), b3 = Guid.NewGuid();
        b.AddRow(preset, 12,
            (BlockGridAreaKeys.Preset3Col1, new[] { b1 }),
            (BlockGridAreaKeys.Preset3Col2, new[] { b2 }),
            (BlockGridAreaKeys.Preset3Col3, new[] { b3 }));
        b.AddLayoutPreset(ContentTypeKeys.LayoutPreset3ColEqual, preset);
        b.AddContent(ContentTypeKeys.ElementInfoFeature, b1, Feature(f1.Title, f1.Subtitle, f1.Summary));
        b.AddContent(ContentTypeKeys.ElementInfoFeature, b2, Feature(f2.Title, f2.Subtitle, f2.Summary));
        b.AddContent(ContentTypeKeys.ElementInfoFeature, b3, Feature(f3.Title, f3.Subtitle, f3.Summary));
    }
}
