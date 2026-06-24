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
    // Áreas de elementLayout3Col (de DTBlockGridSections) — col1/col2/col3.
    private static readonly Guid Col1AreaKey = new("b3141704-5e2d-4adf-9c83-654377a9717f");
    private static readonly Guid Col2AreaKey = new("316ace81-08bb-4688-a54b-930b8378d9e7");
    private static readonly Guid Col3AreaKey = new("ecd5685c-7b37-4311-bb0b-6453949cd898");
    // El preset 3Col no trae CSS de columnas propio; estas utilities (syn-utilities.css) sí.
    private const string ThreeColGridClass = "syn-display--grid syn-grid--tpl-3col syn-grid--gap-lg";

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly SchemaBlockDefaults _defaults;
    private readonly DevMediaFactory _media;
    private readonly ILogger<DevContentFiller> _logger;

    private Guid _sectionKey, _heroKey, _splitKey, _featureGridKey, _featureKey, _missionKey, _ctaKey, _buttonKey;
    private Guid _testimonialsKey, _testimonialItemKey, _faqListKey, _faqItemKey;
    private Guid _statKey, _threeColKey, _logoCloudKey, _logoItemKey;

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
        filled += Apply("Productos", "Un motor, muchos productos", BuildProductos(), details);
        filled += Apply("Contacto", "Hablemos", BuildContacto(), details);

        // Entry point: compone el launcher en platformRoot.introBody (Layout Composer),
        // estilado con clases .syn-launcher* vía cssClass. No bloquea el resultado.
        SeedPlatformLauncher(details);

        return new FillResult(filled == 4, filled, string.Join("; ", details));
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
            ("elementInfoTestimonialCarousel", k => _testimonialsKey = k),
            ("elementInfoTestimonialItem", k => _testimonialItemKey = k),
            ("elementInfoFaqList", k => _faqListKey = k),
            ("elementInfoFaqItem", k => _faqItemKey = k),
            ("elementInfoStat", k => _statKey = k),
            ("elementLayout3Col", k => _threeColKey = k),
            ("elementMediaLogoCloud", k => _logoCloudKey = k),
            ("elementMediaLogoItem", k => _logoItemKey = k),
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
        if (page is null)
        {
            // No existe → crear bajo el siteRoot "Synergos" (su padre es platformRoot, no root).
            var site = FindByName("Synergos");
            if (site is null) { details.Add($"{pageName}:siteroot-not-found"); return 0; }
            page = _contentService.Create(pageName, site.Id, "pageBase");
            page.SetCultureName(pageName, Culture);
        }

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

        AddStats(b,
            ("122", "bundles UI publicados"),
            ("1", "código, todos los verticales"),
            ("5", "capas estancas"));

        AddSplit(b, "Arquitectura por capas",
            "Settings · Compositions · Blocks · Pages · Wiring",
            "<p>Cinco capas estancas y un grafo de dependencias unidireccional mantienen el sistema extensible sin acoplarse. Cada decisión vive donde corresponde.</p>",
            "Synergos Capas", "Diagrama de las cinco capas de arquitectura", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        AddMission(b, "Componés, no programás",
            "Del schema a la página, sin fricción",
            "<p>El mismo schema sirve a healthcare, e-commerce, membresía o marca corporativa. Desplegar un vertical es cuestión de días, no de trimestres.</p>");

        AddTestimonials(b,
            ("Migramos cuatro verticales sobre el mismo core. Lo que antes era un trimestre ahora es una semana.", "Laura Méndez", "CTO, Grupo Andino"),
            ("El editor arma páginas completas sin tocarnos a desarrollo. La arquitectura por capas se nota.", "Diego Restrepo", "Líder de Producto"),
            ("Un solo schema para marca, e-commerce y membresía. La promesa polimórfica es real.", "Sofía Cardona", "Directora Digital"));

        AddLogoCloud(b, "Confían en la plataforma",
            ("Andina", "#0A2540", "#0F58A7"),
            ("Nimbus", "#143C8C", "#5B7CFA"),
            ("Vértice", "#0F58A7", "#1FA2A6"),
            ("Cobalto", "#1A1A2E", "#7A3FF2"),
            ("Solara", "#7A3FF2", "#C04CFC"));

        AddFaq(b, "Preguntas frecuentes",
            ("¿Sirve para más de un tipo de sitio?", "Sí. El mismo código y schema sirven a profesional independiente, e-commerce, marca corporativa o portal de membresía — cambian las instancias, no el código."),
            ("¿El editor necesita saber programar?", "No. El contenido se compone en el Layout Composer arrastrando bloques; el desarrollo solo entra para piezas nuevas."),
            ("¿Cómo se integran los componentes de UI?", "Vía un registry de bundles consumido por el CMS (ADR 0012); los bloques server-side renderizan siempre, los CDN hidratan cuando se publican."));

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

    private string BuildProductos()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Un motor, muchos productos",
            "Cinco verticales sobre el mismo core",
            "<p>El mismo schema y los mismos 122 bundles se adaptan a cada negocio. No reescribes código: instancias el vertical, cambias el branding y publicas.</p>",
            "Synergos Productos", "Verticales de Synergos", "#143C8C", "#1FA2A6",
            ("Agendar demo", "/synergos/contacto"));

        AddFeatureGrid(b, "Verticales disponibles", "Una receta por tipo de cliente", new[]
        {
            ("Profesional", "Médico · abogado · consultor", "Sitio institucional + servicios + agenda + contacto, listo en días."),
            ("E-commerce", "Catálogo + carrito + checkout", "Producto, variantes, carrito y query server-side sobre el mismo core."),
            ("Marca corporativa", "Empresa + casos + careers + blog", "Experiencias corporativas con chrome editable por marca."),
            ("Membresía", "Público + dashboard privado", "Contenido protegido, login y self-service de miembros."),
        });

        AddStats(b,
            ("5", "verticales base"),
            ("122", "bundles UI reutilizables"),
            ("1", "deploy, multi-dominio"));

        AddSplit(b, "Mismo schema, tu marca",
            "Branding por provider, no por código",
            "<p>Colores, tipografía, logo y tono se resuelven por settings y <code>IBrandingProvider</code>. El core nunca conoce tu marca — solo la sirve.</p>",
            "Synergos Branding", "Personalización de marca en Synergos", "#0F58A7", "#7A3FF2",
            mediaOnRight: true, ctaLabel: "Ver la identidad", ctaUrl: "/synergos/identidad");

        AddCta(b, "¿Cuál es tu vertical?",
            "Cuéntanos tu caso y te mostramos la receta que mejor encaja.",
            "Agendar sesión", "/synergos/contacto");
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

        AddFaq(b, "Antes de escribirnos",
            ("¿Qué necesito para la sesión?", "Una idea del vertical (marca, e-commerce, membresía…) y, si tienes, tus brand assets. Nosotros llevamos el resto."),
            ("¿Cuánto dura?", "30 minutos. Salimos con un plan concreto de qué piezas del schema cubren tu caso."),
            ("¿Trabajan sobre mi marca?", "Sí. El branding se resuelve por provider y settings, sin tocar el core — tu identidad, nuestro motor."));

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

    private void AddStats(BlockGridJsonBuilder b, params (string value, string label)[] stats)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var areas = new[] { Col1AreaKey, Col2AreaKey, Col3AreaKey };
        section.AddChild(SectionContentAreaKey, _threeColKey, col =>
        {
            col.Set("cssClass", ThreeColGridClass);   // las clases del preset no existen en CSS; estas utilities sí
            col.ApplyDefaults(_defaults.DefaultsFor(_threeColKey));
            for (var i = 0; i < stats.Length && i < 3; i++)
            {
                var (value, label) = stats[i];
                col.AddChild(areas[i], _statKey, s => s
                    .Set("statValue", value)
                    .Set("statLabel", label)
                    .ApplyDefaults(_defaults.DefaultsFor(_statKey)));
            }
        });
    }

    private void AddLogoCloud(BlockGridJsonBuilder b, string? cloudTitle,
        params (string name, string from, string to)[] logos)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var list = new BlockListJsonBuilder();
        foreach (var (name, from, to) in logos)
        {
            list.AddBlock(_logoItemKey)
                .Set("mediaReference", _media.GetOrCreatePickerValue(name, name, from, to, 320, 160))
                .Set("mediaAlt", name)
                .ApplyDefaults(_defaults.DefaultsFor(_logoItemKey));
        }
        section.AddChild(SectionContentAreaKey, _logoCloudKey, lc =>
        {
            lc.Set("cloudTitle", cloudTitle);
            if (list.HasItems) { lc.Set("logoItems", list.Build()); }
            lc.ApplyDefaults(_defaults.DefaultsFor(_logoCloudKey));
        });
    }

    private void AddTestimonials(BlockGridJsonBuilder b, params (string quote, string author, string role)[] items)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var list = new BlockListJsonBuilder();
        foreach (var (quote, author, role) in items)
        {
            list.AddBlock(_testimonialItemKey)
                .Set("textBody", $"<p>{quote}</p>")
                .Set("authorName", author)
                .Set("authorRole", role)
                .Set("mediaAlt", author)
                .ApplyDefaults(_defaults.DefaultsFor(_testimonialItemKey));
        }
        section.AddChild(SectionContentAreaKey, _testimonialsKey, tc =>
        {
            if (list.HasItems) { tc.Set("testimonialItems", list.Build()); }
            tc.ApplyDefaults(_defaults.DefaultsFor(_testimonialsKey));
        });
    }

    private void AddFaq(BlockGridJsonBuilder b, string faqTitle, params (string question, string answer)[] items)
    {
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var list = new BlockListJsonBuilder();
        foreach (var (question, answer) in items)
        {
            list.AddBlock(_faqItemKey)
                .Set("headingTitle", question)
                .Set("textBody", $"<p>{answer}</p>")
                .ApplyDefaults(_defaults.DefaultsFor(_faqItemKey));
        }
        section.AddChild(SectionContentAreaKey, _faqListKey, fl =>
        {
            fl.Set("faqTitle", faqTitle);
            if (list.HasItems) { fl.Set("faqItems", list.Build()); }
            fl.ApplyDefaults(_defaults.DefaultsFor(_faqListKey));
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

    // ──────────────── Entry point: launcher composable (introBody) ────────────────

    /// <summary>
    /// Compone el launcher del platformRoot en su <c>introBody</c> (Layout Composer):
    /// una Section (elementLayoutSection, cssClass=syn-launcher__grid) con 3 Cards
    /// (elementCompCard) — cada una con cssClass=syn-launcher__card + icono + estado
    /// y ctaLink a su Site Root. Cero schema nuevo; el look PS3 vive en clases CSS
    /// (.syn-launcher*) + platform-root.js. Publicado vía IContentService (editable
    /// en backoffice). Resuelve elementCompCard por separado → no-op si falta.
    /// </summary>
    private void SeedPlatformLauncher(List<string> details)
    {
        var cardKey = _contentTypeService.Get("elementCompCard")?.Key;
        if (cardKey is null || _sectionKey == Guid.Empty) { details.Add("Launcher:skipped-no-schema"); return; }

        var pr = _contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == "platformRoot");
        if (pr is null) { details.Add("Launcher:platformroot-not-found"); return; }

        var site = FindByName("Synergos");
        var entidadName = string.IsNullOrWhiteSpace(site?.Name) ? "Entidad" : site!.Name;

        var g = new BlockGridJsonBuilder();
        var section = g.AddTopLevelBlock(_sectionKey);
        section.Set("cssClass", "syn-launcher__grid").ApplyDefaults(_defaults.DefaultsFor(_sectionKey));

        void AddCard(string title, string body, string iconClass, string statusClass, string ctaLabel, string url) =>
            section.AddChild(SectionContentAreaKey, cardKey.Value, c => c
                .Set("headingTitle", title)
                .Set("textBody", body)
                .Set("mediaAlt", title)   // compContentMedia.mediaAlt es mandatory (sin imagen, pero requerido)
                .Set("ctaLabel", ctaLabel)
                .Set("ctaLink", LinkJson(title, url))
                .Set("cssClass", $"syn-launcher__card {iconClass} {statusClass}")
                .ApplyDefaults(_defaults.DefaultsFor(cardKey.Value)));

        AddCard(entidadName, "<p>Marca, identidad y páginas institucionales — el sitio editorial completo.</p>",
            "syn-launcher__card--grid", "syn-launcher__card--live", "Entrar al sitio →", "/synergos");
        AddCard("Blogs", "<p>Publicaciones, artículos y contenido editorial sobre el mismo core.</p>",
            "syn-launcher__card--document", "syn-launcher__card--soon", "Ver dominio →", "/blogs");
        AddCard("Ecommerce", "<p>Catálogo, productos y checkout sobre el mismo schema polimórfico.</p>",
            "syn-launcher__card--bag", "syn-launcher__card--soon", "Ver dominio →", "/ecommerce");

        pr.SetValue("introBody", g.Build(), Culture);
        var save = _contentService.SaveAndPublish(pr, new[] { Culture });
        if (!save.Success)
        {
            var invalid = save.InvalidProperties is null ? "(null)" : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogWarning("DevContentFiller: platformRoot launcher falló: {Result}; invalid=[{Invalid}]", save.Result, invalid);
            details.Add($"Launcher:save-failed:{save.Result}:[{invalid}]");
            return;
        }
        details.Add("Launcher:ok");
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
