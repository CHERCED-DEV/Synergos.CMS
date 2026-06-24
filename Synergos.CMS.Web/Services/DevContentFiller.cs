using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only: compone el cuerpo (Layout Composer / BlockGrid) de las
/// páginas del sitio Synergos con contenido editorial elegante y variado
/// (MediaTextSplit alternados + MissionBlock + CtaBanner), imágenes reales
/// (DevMediaFactory) y CTAs con link — todo server-side vía
/// <see cref="IContentService"/> (Umbraco 13 no tiene Management API).
/// </summary>
/// <remarks>
/// Gated por <c>Synergos:DevSeed:Enabled=true</c> (ADR 0013). No destructivo:
/// escribe <c>heading</c> y <c>sections</c> de páginas existentes; no crea/
/// borra nodos de contenido (sí crea Media idempotente). GUIDs por alias en
/// runtime (ADR 0008). Bloques SSR (elementComp*/Corp*) renderizan;
/// SchemaBlockDefaults evita el JsonReaderException. El Hero (elementCompHero)
/// se omite: su BlockList anidado ctaItems no es autorable server-side sin
/// items reales (limitación ADR 0093) — MediaTextSplit cumple el rol de hero.
/// Jerarquía: H1 = heading de página; H2 = títulos de bloque (distintos).
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

    private Guid _sectionKey, _splitKey, _missionKey, _ctaKey;

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
        var s = _contentTypeService.Get("elementLayoutSection")?.Key;
        var sp = _contentTypeService.Get("elementCompMediaTextSplit")?.Key;
        var m = _contentTypeService.Get("elementCorpMissionBlock")?.Key;
        var c = _contentTypeService.Get("elementCompCtaBanner")?.Key;
        var miss = new List<string>();
        if (s is null) miss.Add("elementLayoutSection");
        if (sp is null) miss.Add("elementCompMediaTextSplit");
        if (m is null) miss.Add("elementCorpMissionBlock");
        if (c is null) miss.Add("elementCompCtaBanner");
        missing = string.Join(",", miss);
        if (miss.Count > 0) return false;
        _sectionKey = s!.Value; _splitKey = sp!.Value; _missionKey = m!.Value; _ctaKey = c!.Value;
        return true;
    }

    private int Apply(string pageName, string heading, string sectionsJson, List<string> details)
    {
        var page = FindByName(pageName);
        if (page is null) { details.Add($"{pageName}:not-found"); return 0; }

        page.SetValue("heading", heading, Culture);   // H1 de página
        page.SetValue("showTitle", true);              // mostrar el H1; los bloques usan H2
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
        AddSplit(b, "Un código, infinitos productos",
            "El motor editorial polimórfico",
            "<p>Synergos está detrás de marcas profesionales, e-commerce, portales de membresía y experiencias corporativas. Compuesto server-side, sin reescribir código.</p>",
            "Synergos Home Hero", "Composición abstracta de capas Synergos", "#0A2540", "#0F58A7",
            mediaOnRight: false, ctaLabel: "Conoce la visión", ctaUrl: "/synergos/identidad");

        AddSplit(b, "Arquitectura por capas",
            "Settings · Compositions · Blocks · Pages · Wiring",
            "<p>Cinco capas estancas y un grafo de dependencias unidireccional mantienen el sistema extensible sin acoplarse. Cada decisión vive donde corresponde.</p>",
            "Synergos Capas", "Diagrama de las cinco capas de arquitectura", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        AddMission(b, "Componés, no programás",
            "El editor arrastra; la plataforma compone",
            "<p>122 bundles UI publicados y un Block Grid que el editor opera sin tocar código. El mismo schema sirve a healthcare, e-commerce, membresía o marca corporativa.</p>");

        AddCta(b, "¿Listo para construir el tuyo?",
            "Agenda una sesión técnica y te mostramos el grafo de capas en vivo.",
            "Agendar sesión", "/synergos/contacto");
        return b.Build();
    }

    private string BuildIdentidad()
    {
        var b = new BlockGridJsonBuilder();
        AddSplit(b, "Nuestra identidad",
            "Un CMS empresarial polimórfico",
            "<p>Un código, un schema, 122 bundles UI. Construido para escalar decisiones de arquitectura, no para repetirlas.</p>",
            "Synergos Identidad", "Identidad visual de la plataforma Synergos", "#1A1A2E", "#7A3FF2",
            mediaOnRight: false, ctaLabel: null, ctaUrl: null);

        AddSplit(b, "Un schema, polimórfico",
            "El mismo núcleo, mil formas",
            "<p>Profesional independiente, e-commerce, marca corporativa o membership portal: cambian las instancias de schema y los brand assets, nunca el código.</p>",
            "Synergos Polimorfico", "Representación del schema polimórfico", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        AddMission(b, "Nuestro propósito",
            "Hacia dónde vamos",
            "<p>Hacer que las decisiones de arquitectura escalen. Un futuro donde el schema editorial sea tan extensible como un lenguaje.</p>");

        AddCta(b, "¿Quieres ver Synergos por dentro?",
            "Agenda una sesión técnica con el equipo de plataforma.",
            "Agendar sesión técnica", "/synergos/contacto");
        return b.Build();
    }

    private string BuildContacto()
    {
        var b = new BlockGridJsonBuilder();
        AddSplit(b, "Sesiones técnicas, demos e integración",
            "Estamos disponibles",
            "<p>Una llamada de 30 minutos con el equipo de plataforma para resolver cualquier duda — desde schema hasta integración con el CDN.</p>",
            "Synergos Contacto", "Sección de contacto de Synergos", "#0A2540", "#1FA2A6",
            mediaOnRight: false, ctaLabel: null, ctaUrl: null);

        AddMission(b, "Cómo trabajamos",
            "Directo, técnico, sin vueltas",
            "<p>Cuéntanos tu caso y te mostramos cómo Synergos lo resuelve con el schema actual — o qué pieza nueva haría falta.</p>");

        AddCta(b, "Agenda una sesión",
            "30 minutos con el equipo de plataforma.",
            "Agendar", "/synergos/contacto");
        return b.Build();
    }

    // ───────────────────────── Helpers de bloque ─────────────────────────

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

    // MultiUrlPicker (Link) stored value: array JSON. url relativo/externo,
    // udi null. Model.Value<Link>("ctaLink").Url resuelve desde url.
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
