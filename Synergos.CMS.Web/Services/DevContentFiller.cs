using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only: compone el cuerpo (Layout Composer / BlockGrid) de las páginas
/// del sitio Synergos con contenido editorial elegante y variado —
/// Hero (con CTAs) + FeatureGrid + MediaTextSplit + MissionBlock + CtaBanner —
/// imágenes reales (DevMediaFactory) y BlockList anidados (CTAs/features) —
/// todo server-side vía <see cref="IContentService"/> (Umbraco 13 sin Management API).
/// </summary>
/// <remarks>
/// Gated por <c>Synergos:DevSeed:Enabled=true</c> (ADR 0013). No destructivo:
/// escribe <c>heading</c>, <c>showTitle</c> y <c>sections</c> de páginas
/// existentes (crea Media idempotente). GUIDs por alias en runtime (ADR 0008).
/// Hero/FeatureGrid usan <see cref="BlockListJsonBuilder"/> para sus BlockList
/// anidados (ctaItems/features) con ≥1 item real. showTitle=false: el Hero
/// es el H1; los demás bloques usan H2.
/// </remarks>
public sealed class DevContentFiller
{
    private const string Culture = "es-CO";
    private const string SectionsAlias = "sections";
    private static readonly Guid SectionContentAreaKey = new("3525d41c-ae84-47ac-9297-2148f6a4aae8");

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly SchemaBlockDefaults _defaults;
    private readonly DevMediaFactory _media;
    private readonly ILogger<DevContentFiller> _logger;

    private Guid _sectionKey, _heroKey, _splitKey, _featureGridKey, _featureKey, _missionKey, _ctaKey, _buttonKey;

    public DevContentFiller(
        IContentService contentService,
        IContentTypeService contentTypeService,
        SchemaBlockDefaults defaults,
        DevMediaFactory media,
        ILogger<DevContentFiller> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _defaults = defaults;
        _media = media;
        _logger = logger;
    }

    public FillResult FillSynergosPages()
    {
        if (!ResolveKeys(out var missing))
        {
            return new FillResult(false, 0, $"element-types-missing:{missing}");
        }

        var details = new List<string>();
        var filled = 0;
        filled += Apply("Home", "Una plataforma. Mil productos.", BuildHome(), details);
        filled += Apply("Identidad", "Construimos la plataforma donde tu producto se vuelve mil productos", BuildIdentidad(), details);
        filled += Apply("Contacto", "Hablemos", BuildContacto(), details);

        return new FillResult(filled == 3, filled, string.Join("; ", details));
    }

    private bool ResolveKeys(out string missing)
    {
        var map = new (string alias, Action<Guid> set)[]
        {
            ("elementLayoutSection", k => _sectionKey = k),
            ("elementCompHero", k => _heroKey = k),
            ("elementCompMediaTextSplit", k => _splitKey = k),
            ("elementCompFeatureGrid", k => _featureGridKey = k),
            ("elementInfoFeature", k => _featureKey = k),
            ("elementCorpMissionBlock", k => _missionKey = k),
            ("elementCompCtaBanner", k => _ctaKey = k),
            ("elementActionButton", k => _buttonKey = k),
        };
        var miss = new List<string>();
        foreach (var (alias, set) in map)
        {
            var key = _contentTypeService.Get(alias)?.Key;
            if (key is null) { miss.Add(alias); } else { set(key.Value); }
        }
        missing = string.Join(",", miss);
        return miss.Count == 0;
    }

    private int Apply(string pageName, string heading, string sectionsJson, List<string> details)
    {
        var page = FindByName(pageName);
        if (page is null) { details.Add($"{pageName}:not-found"); return 0; }

        page.SetValue("heading", heading, Culture);
        page.SetValue("showTitle", false);   // el Hero es el H1 — sin header de página duplicado
        page.SetValue(SectionsAlias, sectionsJson, Culture);

        var save = _contentService.SaveAndPublish(page, new[] { Culture });
        if (!save.Success)
        {
            var invalid = save.InvalidProperties is null ? "(null)" : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogWarning("DevContentFiller: '{Page}' falló: {Result}; invalid=[{Invalid}]", pageName, save.Result, invalid);
            details.Add($"{pageName}:save-failed:{save.Result}:[{invalid}]");
            return 0;
        }
        details.Add($"{pageName}:ok");
        return 1;
    }

    // ───────────────────────── Composición por página ─────────────────────────

    private string BuildHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Una plataforma. Mil productos.",
            "Un código. Un schema. Infinitos productos.",
            "<p>Synergos es el motor editorial detrás de marcas profesionales, e-commerce, portales de membresía y experiencias corporativas — compuesto server-side, sin reescribir código.</p>",
            "Synergos Home Hero", "Composición abstracta de capas Synergos", "#0A2540", "#0F58A7",
            ("Agendar sesión", "/synergos/contacto"), ("Conoce la visión", "/synergos/identidad"));

        AddFeatureGrid(b, "Por qué Synergos", "Tres ideas, un mismo motor", new[]
        {
            ("Polimórfico", "Un código, mil formas", "Profesional, e-commerce, marca o membresía: cambian las instancias de schema, nunca el código."),
            ("Componible", "El editor arrastra", "122 bundles UI y un Block Grid que el editor opera sin tocar una línea de código."),
            ("Server-side", "Render robusto", "Composición y publicación server-side: el contenido existe y rinde sin depender del cliente."),
        });

        AddSplit(b, "Arquitectura por capas",
            "Settings · Compositions · Blocks · Pages · Wiring",
            "<p>Cinco capas estancas y un grafo de dependencias unidireccional mantienen el sistema extensible sin acoplarse. Cada decisión vive donde corresponde.</p>",
            "Synergos Capas", "Diagrama de las cinco capas de arquitectura", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        AddMission(b, "Componés, no programás",
            "Del schema a la página, sin fricción",
            "<p>El mismo schema sirve a healthcare, e-commerce, membresía o marca corporativa. Desplegar un vertical es cuestión de días, no de trimestres.</p>");

        AddCta(b, "¿Listo para construir el tuyo?",
            "Agenda una sesión técnica y te mostramos el grafo de capas en vivo.",
            "Agendar sesión", "/synergos/contacto");
        return b.Build();
    }

    private string BuildIdentidad()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Construimos la plataforma donde tu producto se vuelve mil productos",
            "Nuestra identidad",
            "<p>Un CMS empresarial polimórfico: un código, un schema, 122 bundles UI. Construido para escalar decisiones de arquitectura, no para repetirlas.</p>",
            "Synergos Identidad", "Identidad visual de la plataforma Synergos", "#1A1A2E", "#7A3FF2",
            ("Hablemos", "/synergos/contacto"));

        AddSplit(b, "Nuestro propósito",
            "Hacia dónde vamos",
            "<p>Hacer que las decisiones de arquitectura escalen. Un futuro donde el schema editorial sea tan extensible como un lenguaje.</p>",
            "Synergos Proposito", "Ilustración del propósito de Synergos", "#7A3FF2", "#C04CFC",
            mediaOnRight: false, ctaLabel: null, ctaUrl: null);

        AddSplit(b, "Un schema, polimórfico",
            "El mismo núcleo, mil formas",
            "<p>Profesional independiente, e-commerce, marca corporativa o membership portal: cambian las instancias de schema y los brand assets, nunca el código.</p>",
            "Synergos Polimorfico", "Representación del schema polimórfico", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        AddCta(b, "¿Quieres ver Synergos por dentro?",
            "Agenda una sesión técnica con el equipo de plataforma.",
            "Agendar sesión técnica", "/synergos/contacto");
        return b.Build();
    }

    private string BuildContacto()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Hablemos",
            "Sesiones técnicas, demos e integración",
            "<p>Una llamada de 30 minutos con el equipo de plataforma para resolver cualquier duda — desde schema hasta integración con el CDN.</p>",
            "Synergos Contacto", "Sección de contacto de Synergos", "#0A2540", "#1FA2A6",
            ("Agendar ahora", "/synergos/contacto"));

        AddMission(b, "Cómo trabajamos",
            "Directo, técnico, sin vueltas",
            "<p>Cuéntanos tu caso y te mostramos cómo Synergos lo resuelve con el schema actual — o qué pieza nueva haría falta.</p>");

        AddCta(b, "Agenda una sesión",
            "30 minutos con el equipo de plataforma.",
            "Agendar", "/synergos/contacto");
        return b.Build();
    }

    // ───────────────────────── Helpers de bloque ─────────────────────────

    private void AddHero(BlockGridJsonBuilder b, string title, string subtitle, string body,
        string imgName, string imgAlt, string from, string to, params (string label, string url)[] ctas)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var pickerValue = _media.GetOrCreatePickerValue(imgName, imgAlt, from, to, 1600, 720);

        var ctaItems = new BlockListJsonBuilder();
        foreach (var (label, url) in ctas)
        {
            ctaItems.AddBlock(_buttonKey)
                .Set("ctaLabel", label)
                .Set("ctaLink", LinkJson(label, url))
                .ApplyDefaults(_defaults.DefaultsFor(_buttonKey));
        }

        section.AddChild(SectionContentAreaKey, _heroKey, hero =>
        {
            hero.Set("headingTitle", title)
                .Set("headingSubtitle", subtitle)
                .Set("textBody", body)
                .Set("mediaReference", pickerValue)
                .Set("mediaAlt", imgAlt);
            if (ctaItems.HasItems) { hero.Set("ctaItems", ctaItems.Build()); }
            hero.ApplyDefaults(_defaults.DefaultsFor(_heroKey));
        });
    }

    private void AddFeatureGrid(BlockGridJsonBuilder b, string title, string subtitle,
        (string title, string subtitle, string body)[] features)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));

        var items = new BlockListJsonBuilder();
        foreach (var (ft, fsub, fbody) in features)
        {
            items.AddBlock(_featureKey)
                .Set("headingTitle", ft)
                .Set("headingSubtitle", fsub)
                .Set("textBody", $"<p>{fbody}</p>")
                .Set("mediaAlt", ft)   // compContentMedia.mediaAlt es mandatory
                .ApplyDefaults(_defaults.DefaultsFor(_featureKey));
        }

        section.AddChild(SectionContentAreaKey, _featureGridKey, fg =>
        {
            fg.Set("headingTitle", title).Set("headingSubtitle", subtitle);
            if (items.HasItems) { fg.Set("features", items.Build()); }
            fg.ApplyDefaults(_defaults.DefaultsFor(_featureGridKey));
        });
    }

    private void AddSplit(BlockGridJsonBuilder b, string title, string subtitle, string body,
        string imgName, string imgAlt, string from, string to, bool mediaOnRight,
        string? ctaLabel, string? ctaUrl)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var pickerValue = _media.GetOrCreatePickerValue(imgName, imgAlt, from, to, 1200, 900);
        section.AddChild(SectionContentAreaKey, _splitKey, split =>
        {
            split.Set("headingTitle", title)
                 .Set("headingSubtitle", subtitle)
                 .Set("textBody", body)
                 .Set("mediaReference", pickerValue)
                 .Set("mediaAlt", imgAlt)
                 .Set("mediaOnRight", mediaOnRight);
            if (!string.IsNullOrWhiteSpace(ctaLabel) && !string.IsNullOrWhiteSpace(ctaUrl))
            {
                split.Set("ctaLabel", ctaLabel).Set("ctaLink", LinkJson(ctaLabel!, ctaUrl!));
            }
            split.ApplyDefaults(_defaults.DefaultsFor(_splitKey));
        });
    }

    private void AddMission(BlockGridJsonBuilder b, string title, string subtitle, string body)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, _missionKey, m => m
            .Set("headingTitle", title)
            .Set("headingSubtitle", subtitle)
            .Set("textBody", body)
            .Set("mediaAlt", "Synergos")
            .ApplyDefaults(_defaults.DefaultsFor(_missionKey)));
    }

    private void AddCta(BlockGridJsonBuilder b, string title, string subtitle, string ctaLabel, string url)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, _ctaKey, c => c
            .Set("headingTitle", title)
            .Set("headingSubtitle", subtitle)
            .Set("ctaLabel", ctaLabel)
            .Set("ctaLink", LinkJson(ctaLabel, url))
            .ApplyDefaults(_defaults.DefaultsFor(_ctaKey)));
    }

    private static string LinkJson(string name, string url)
        => $"[{{\"name\":\"{Esc(name)}\",\"url\":\"{Esc(url)}\",\"target\":\"\",\"udi\":null,\"icon\":null,\"queryString\":null}}]";

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ───────────────────────── Búsqueda de nodos ─────────────────────────

    private IContent? FindByName(string name)
    {
        foreach (var root in _contentService.GetRootContent())
        {
            if (Matches(root, name)) return root;
            var found = FindDescendant(root.Id, name);
            if (found is not null) return found;
        }
        return null;
    }

    private IContent? FindDescendant(int parentId, string name)
    {
        var children = _contentService.GetPagedChildren(parentId, 0, 200, out _);
        foreach (var child in children)
        {
            if (Matches(child, name)) return child;
            var deeper = FindDescendant(child.Id, name);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private static bool Matches(IContent content, string name)
        => string.Equals(content.GetCultureName(Culture) ?? content.Name, name, StringComparison.Ordinal);

    public sealed record FillResult(bool Success, int PagesFilled, string Detail);
}
