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
    private const string BrandName = "SynergosLabs";   // marca de la Entidad + umbrella (decisión del arquitecto)
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

        var site = FindSiteRoot();
        if (site is null) { return new FillResult(false, 0, "siteroot-not-found"); }

        // Afiliación: el HOME vive en el siteRoot (sections), no en un nodo "Home" aparte.
        // De paso la Entidad se renombra a SynergosLabs (node + display + brand); la URL
        // /synergos se preserva vía umbracoUrlName para no romper enlaces internos.
        filled += ApplySiteRootHome(site, details);

        // Páginas navegables (hijas del siteRoot).
        filled += Apply("Identidad", "Construimos la plataforma donde tu producto se vuelve mil productos", BuildIdentidad(), details);
        filled += Apply("Productos", "Un motor, muchos productos", BuildProductos(), details);
        filled += Apply("Contacto", "Hablemos", BuildContacto(), details);

        // El nodo "Home" anterior queda redundante → fuera del menú (no se borra).
        HideRedundantHome(site, details);

        // SEO defaults (fallback de title/description del brand) alineados a SynergosLabs.
        UpdateSiteConfigSeo(details);

        // Entry point: launcher en platformRoot.introBody + rename del umbrella a SynergosLabs.
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
            // No existe → crear bajo el siteRoot (robusto al rename de la Entidad).
            var site = FindSiteRoot();
            if (site is null) { details.Add($"{pageName}:siteroot-not-found"); return 0; }
            page = _contentService.Create(pageName, site.Id, "pageBase");
            page.SetCultureName(pageName, Culture);
        }

        page.SetValue("heading", heading, Culture);
        page.SetValue("showTitle", false);   // el Hero es el H1 — sin header de página duplicado
        if (page.HasProperty("seoTitle")) { page.SetValue("seoTitle", $"{pageName} — {BrandName}", Culture); }
        if (page.HasProperty("seoDescription"))
        {
            var d = page.GetValue<string>("seoDescription", Culture);
            if (!string.IsNullOrEmpty(d)) { page.SetValue("seoDescription", RebrandText(d), Culture); }
        }
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

    /// <summary>
    /// El home de la Entidad vive en el <c>siteRoot.sections</c> (afiliación: el siteRoot
    /// ES el home; los hijos son las páginas). La marca visible pasa a SynergosLabs vía
    /// <c>siteDisplayName</c> + <c>brandDisplayName</c>. El nombre del NODO se conserva
    /// ("Synergos") a propósito: así la URL /synergos y los enlaces internos no cambian.
    /// </summary>
    private int ApplySiteRootHome(IContent site, List<string> details)
    {
        if (site.HasProperty("siteDisplayName")) { site.SetValue("siteDisplayName", BrandName, Culture); }
        if (site.HasProperty("brandDisplayName")) { site.SetValue("brandDisplayName", BrandName, Culture); }
        if (site.HasProperty("seoTitle")) { site.SetValue("seoTitle", $"{BrandName} — Composable Digital Solutions", Culture); }
        if (site.HasProperty("seoDescription"))
        {
            var sd = site.GetValue<string>("seoDescription", Culture);
            if (!string.IsNullOrEmpty(sd)) { site.SetValue("seoDescription", RebrandText(sd), Culture); }
        }
        // canonicalHostname (invariant) → el brand resuelve por HostBasedBrandingProvider en dev.
        // Solo si está vacío (no pisar un hostname de producción que haya puesto el arquitecto).
        if (site.HasProperty("canonicalHostname") && string.IsNullOrWhiteSpace(site.GetValue<string>("canonicalHostname")))
        {
            site.SetValue("canonicalHostname", "synergos.local");
        }
        site.SetValue(SectionsAlias, BuildHome(), Culture);

        var save = _contentService.SaveAndPublish(site, new[] { Culture });
        if (!save.Success)
        {
            var invalid = save.InvalidProperties is null ? "(null)" : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogWarning("DevContentFiller: siteRoot home+rename falló: {Result}; invalid=[{Invalid}]", save.Result, invalid);
            details.Add($"SiteRoot:save-failed:{save.Result}:[{invalid}]");
            return 0;
        }
        details.Add("SiteRoot:ok(home+SynergosLabs)");
        return 1;
    }

    /// <summary>
    /// El nodo "Home" anterior duplica el home (ahora en el siteRoot) → lo saca del menú
    /// principal y del footer vía <c>hideFromMainMenu</c>/<c>hideFromFooter</c> (compNavigation).
    /// No destructivo (el nodo sigue ahí; el arquitecto decide si lo borra).
    /// </summary>
    private void HideRedundantHome(IContent site, List<string> details)
    {
        var home = FindDescendant(site.Id, "Home");
        if (home is null) { details.Add("Home:none"); return; }
        if (!home.HasProperty("hideFromMainMenu")) { details.Add("Home:redundant(no-hide-prop)"); return; }
        home.SetValue("hideFromMainMenu", true);
        if (home.HasProperty("hideFromFooter")) { home.SetValue("hideFromFooter", true); }
        var save = _contentService.SaveAndPublish(home, new[] { Culture });
        details.Add(save.Success ? "Home:hidden(nav+footer)" : $"Home:hide-failed:{save.Result}");
    }

    // ───────────────────────── Composición por página ─────────────────────────

    private string BuildHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Una plataforma. Mil productos.",
            "Un código. Un schema. Infinitos productos.",
            "<p>SynergosLabs es el motor editorial detrás de marcas profesionales, e-commerce, portales de membresía y experiencias corporativas — compuesto server-side, sin reescribir código.</p>",
            "Synergos Home Hero", "Composición abstracta de capas SynergosLabs", "#0A2540", "#0F58A7",
            ("Agendar sesión", "/synergos/contacto"), ("Conoce la visión", "/synergos/identidad"));

        var features = new (string title, string subtitle, string body)[]
        {
            ("Polimórfico", "Un código, mil formas", "Profesional, e-commerce, marca o membresía: cambian las instancias de schema, nunca el código."),
            ("Componible", "El editor arrastra", "122 bundles UI y un Block Grid que el editor opera sin tocar una línea de código."),
            ("Server-side", "Render robusto", "Composición y publicación server-side: el contenido existe y rinde sin depender del cliente."),
        };
        if (_contentTypeService.Get("elementSynFeatureGrid")?.Key is not null)
        {
            AddSynFeatureGrid(b, "Por qué SynergosLabs", features);
        }
        else
        {
            AddFeatureGrid(b, "Por qué SynergosLabs", "Tres ideas, un mismo motor", features);
        }

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

        var testimonials = new (string quote, string author, string role)[]
        {
            ("Migramos cuatro verticales sobre el mismo core. Lo que antes era un trimestre ahora es una semana.", "Laura Méndez", "CTO, Grupo Andino"),
            ("El editor arma páginas completas sin tocarnos a desarrollo. La arquitectura por capas se nota.", "Diego Restrepo", "Líder de Producto"),
            ("Un solo schema para marca, e-commerce y membresía. La promesa polimórfica es real.", "Sofía Cardona", "Directora Digital"),
        };
        // Híbrido: el componente Angular (elementSynTestimonialSection) está wired end-to-end
        // (schema + SynHost + import map). Pero el runtime de Synergos.UI NO provee
        // @angular/core/rxjs-interop (sg-core.js lo importa, ningún bundle lo expone) → el
        // Web Component no hidrata. Hasta regenerar ese runtime, usamos el SSR. Flip a true
        // cuando el build de Synergos.UI esté arreglado → re-fill → testimonios Angular.
        var useAngularTestimonials = true;   // runtime Angular ya provee rxjs-interop (CDN local) → componente CDN activo
        if (useAngularTestimonials && _contentTypeService.Get("elementSynTestimonialSection")?.Key is not null)
        {
            AddSynTestimonials(b, "Lo que dicen de la plataforma", testimonials);
        }
        else
        {
            AddTestimonials(b, testimonials);
        }

        AddLogoCloud(b, "Confían en la plataforma",
            ("Andina", "#0A2540", "#0F58A7"),
            ("Nimbus", "#143C8C", "#5B7CFA"),
            ("Vértice", "#0F58A7", "#1FA2A6"),
            ("Cobalto", "#1A1A2E", "#7A3FF2"),
            ("Solara", "#7A3FF2", "#C04CFC"));

        var faqs = new (string question, string answer)[]
        {
            ("¿Sirve para más de un tipo de sitio?", "Sí. El mismo código y schema sirven a profesional independiente, e-commerce, marca corporativa o portal de membresía — cambian las instancias, no el código."),
            ("¿El editor necesita saber programar?", "No. El contenido se compone en el Layout Composer arrastrando bloques; el desarrollo solo entra para piezas nuevas."),
            ("¿Cómo se integran los componentes de UI?", "Vía un registry de bundles consumido por el CMS (ADR 0012); los bloques server-side renderizan siempre, los CDN hidratan cuando se publican."),
        };
        if (_contentTypeService.Get("elementSynFaqSection")?.Key is not null)
        {
            AddSynFaq(b, "Preguntas frecuentes", faqs);
        }
        else
        {
            AddFaq(b, "Preguntas frecuentes", faqs);
        }

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
            "Synergos Identidad", "Identidad visual de la plataforma SynergosLabs", "#1A1A2E", "#7A3FF2",
            ("Hablemos", "/synergos/contacto"));

        var synSplit = _contentTypeService.Get("elementSynMediaText")?.Key is not null;
        var bodyProposito = "<p>Hacer que las decisiones de arquitectura escalen. Un futuro donde el schema editorial sea tan extensible como un lenguaje.</p>";
        if (synSplit)
        {
            AddSynSplit(b, "Nuestro propósito", bodyProposito, "Synergos Proposito", "Ilustración del propósito de SynergosLabs", "#7A3FF2", "#C04CFC", mediaOnRight: false);
        }
        else
        {
            AddSplit(b, "Nuestro propósito", "Hacia dónde vamos", bodyProposito,
                "Synergos Proposito", "Ilustración del propósito de SynergosLabs", "#7A3FF2", "#C04CFC",
                mediaOnRight: false, ctaLabel: null, ctaUrl: null);
        }

        var bodyPolimorfico = "<p>Profesional independiente, e-commerce, marca corporativa o membership portal: cambian las instancias de schema y los brand assets, nunca el código.</p>";
        if (synSplit)
        {
            AddSynSplit(b, "Un schema, polimórfico", bodyPolimorfico, "Synergos Polimorfico", "Representación del schema polimórfico", "#0F58A7", "#1FA2A6", mediaOnRight: true);
        }
        else
        {
            AddSplit(b, "Un schema, polimórfico", "El mismo núcleo, mil formas", bodyPolimorfico,
                "Synergos Polimorfico", "Representación del schema polimórfico", "#0F58A7", "#1FA2A6",
                mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        }

        AddCta(b, "¿Quieres ver SynergosLabs por dentro?",
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
            "Synergos Productos", "Verticales de SynergosLabs", "#143C8C", "#1FA2A6",
            ("Agendar demo", "/synergos/contacto"));

        var verticals = new (string title, string subtitle, string body)[]
        {
            ("Profesional", "Médico · abogado · consultor", "Sitio institucional + servicios + agenda + contacto, listo en días."),
            ("E-commerce", "Catálogo + carrito + checkout", "Producto, variantes, carrito y query server-side sobre el mismo core."),
            ("Marca corporativa", "Empresa + casos + careers + blog", "Experiencias corporativas con chrome editable por marca."),
            ("Membresía", "Público + dashboard privado", "Contenido protegido, login y self-service de miembros."),
        };
        if (_contentTypeService.Get("elementSynFeatureGrid")?.Key is not null)
        {
            AddSynFeatureGrid(b, "Verticales disponibles", verticals, 2);
        }
        else
        {
            AddFeatureGrid(b, "Verticales disponibles", "Una receta por tipo de cliente", verticals);
        }

        AddStats(b,
            ("5", "verticales base"),
            ("122", "bundles UI reutilizables"),
            ("1", "deploy, multi-dominio"));

        AddSplit(b, "Mismo schema, tu marca",
            "Branding por provider, no por código",
            "<p>Colores, tipografía, logo y tono se resuelven por settings y <code>IBrandingProvider</code>. El core nunca conoce tu marca — solo la sirve.</p>",
            "Synergos Branding", "Personalización de marca en SynergosLabs", "#0F58A7", "#7A3FF2",
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
            "Synergos Contacto", "Sección de contacto de SynergosLabs", "#0A2540", "#1FA2A6",
            ("Agendar ahora", "/synergos/contacto"));

        AddMission(b, "Cómo trabajamos",
            "Directo, técnico, sin vueltas",
            "<p>Cuéntanos tu caso y te mostramos cómo SynergosLabs lo resuelve con el schema actual — o qué pieza nueva haría falta.</p>");

        var contactFaqs = new (string question, string answer)[]
        {
            ("¿Qué necesito para la sesión?", "Una idea del vertical (marca, e-commerce, membresía…) y, si tienes, tus brand assets. Nosotros llevamos el resto."),
            ("¿Cuánto dura?", "30 minutos. Salimos con un plan concreto de qué piezas del schema cubren tu caso."),
            ("¿Trabajan sobre mi marca?", "Sí. El branding se resuelve por provider y settings, sin tocar el core — tu identidad, nuestro motor."),
        };
        if (_contentTypeService.Get("elementSynFaqSection")?.Key is not null)
        {
            AddSynFaq(b, "Antes de escribirnos", contactFaqs);
        }
        else
        {
            AddFaq(b, "Antes de escribirnos", contactFaqs);
        }

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
        var ctaIndex = 0;
        foreach (var (label, url) in ctas)
        {
            var btn = ctaItems.AddBlock(_buttonKey)
                .Set("ctaLabel", label)
                .Set("ctaLink", LinkJson(label, url));
            if (ctaIndex > 0) { btn.Set("variantKey", "[\"secondary\"]"); } // jerarquía: 2º CTA = secundario (FlexibleDropdown = JSON array)
            btn.ApplyDefaults(_defaults.DefaultsFor(_buttonKey));
            ctaIndex++;
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
        // Si el stat-ticker Angular está importado, usa el count-up animado vía CDN;
        // si no, cae al elementInfoStat SSR. Mismos props (statValue/statLabel).
        var statKey = _contentTypeService.Get("elementSynStatTicker")?.Key ?? _statKey;
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
                col.AddChild(areas[i], statKey, s => s
                    .Set("statValue", value)
                    .Set("statLabel", label)
                    .ApplyDefaults(_defaults.DefaultsFor(statKey)));
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

    /// <summary>
    /// Testimonios vía componente Angular CDN (elementSynTestimonialSection): una Section
    /// con un bloque syn que el SynHost emite como &lt;synergos-testimonial-section config='...'&gt;.
    /// Config end-to-end desde el CMS (headingText + items[]); el Angular hidrata desde la CDN.
    /// </summary>
    private void AddSynTestimonials(BlockGridJsonBuilder b, string heading,
        (string quote, string author, string role)[] items)
    {
        var key = _contentTypeService.Get("elementSynTestimonialSection")?.Key;
        if (key is null) { return; }   // aún no importado → omitir (el caller hace fallback SSR)
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var itemsJson = "[" + string.Join(",", items.Select(t =>
            $"{{\"name\":\"{Esc(t.author)}\",\"quote\":\"{Esc(t.quote)}\",\"role\":\"{Esc(t.role)}\",\"avatarSrc\":\"\"}}")) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("headingText", heading)
            .Set("itemsJson", itemsJson)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>FAQ vía componente Angular CDN (elementSynFaqSection): acordeón interactivo configurado desde el CMS.</summary>
    private void AddSynFaq(BlockGridJsonBuilder b, string heading, (string question, string answer)[] items)
    {
        var key = _contentTypeService.Get("elementSynFaqSection")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var itemsJson = "[" + string.Join(",", items.Select(i =>
            $"{{\"question\":\"{Esc(i.question)}\",\"answer\":\"{Esc(i.answer)}\"}}")) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("headingText", heading)
            .Set("itemsJson", itemsJson)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Feature grid vía componente Angular CDN (elementSynFeatureGrid): grilla de features configurada desde el CMS.</summary>
    private void AddSynFeatureGrid(BlockGridJsonBuilder b, string heading, (string title, string subtitle, string body)[] features, int columns = 3)
    {
        var key = _contentTypeService.Get("elementSynFeatureGrid")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var itemsJson = "[" + string.Join(",", features.Select(f =>
            $"{{\"heading\":\"{Esc(f.title)}\",\"body\":\"{Esc(f.subtitle + " — " + f.body)}\"}}")) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("headingText", heading)
            .Set("itemsJson", itemsJson)
            .Set("columns", columns.ToString())
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
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

    /// <summary>Split media+texto vía componente Angular CDN (elementSynMediaText): imagen + heading + body.</summary>
    private void AddSynSplit(BlockGridJsonBuilder b, string title, string body,
        string imgName, string imgAlt, string from, string to, bool mediaOnRight)
    {
        var key = _contentTypeService.Get("elementSynMediaText")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var picker = _media.GetOrCreatePickerValue(imgName, imgAlt, from, to, 1200, 900);
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("mediaReference", picker)
            .Set("mediaAlt", imgAlt)
            .Set("headingText", title)
            .Set("body", body)
            .Set("mediaPosition", mediaOnRight ? "right" : "left")
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
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
            .Set("mediaAlt", BrandName)
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

        var site = FindSiteRoot();
        var entidadName = site?.GetValue<string>("siteDisplayName", Culture) is { Length: > 0 } dn ? dn : BrandName;

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

        pr.SetCultureName(BrandName, Culture);   // umbrella = SynergosLabs (el hero lee Model.Name)
        if (pr.HasProperty("welcomeMessage"))
        {
            pr.SetValue("welcomeMessage", "Plataforma editorial SynergosLabs — un código, múltiples productos.", Culture);
        }
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

    // Reemplaza "Synergos" suelto por "SynergosLabs" sin duplicar (no toca "SynergosLabs" existente).
    private static string RebrandText(string? s) =>
        string.IsNullOrEmpty(s) ? (s ?? "") : System.Text.RegularExpressions.Regex.Replace(s, "Synergos(?!Labs)", BrandName);

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

    /// <summary>Localiza el siteRoot por content type alias (robusto al rename del nodo).</summary>
    private IContent? FindSiteRoot() => FindFirstOfType("siteRoot");

    /// <summary>Primer nodo de un content type alias dado, en cualquier nivel del árbol.</summary>
    private IContent? FindFirstOfType(string alias)
    {
        foreach (var root in _contentService.GetRootContent())
        {
            if (root.ContentType.Alias == alias) { return root; }
            var found = FindByType(root.Id, alias);
            if (found is not null) { return found; }
        }
        return null;
    }

    /// <summary>Alinea los SEO defaults del siteConfigSettings (fallback de title/description) a SynergosLabs.</summary>
    private void UpdateSiteConfigSeo(List<string> details)
    {
        var cfg = FindFirstOfType("siteConfigSettings");
        if (cfg is null) { details.Add("SiteConfig:none"); return; }
        if (cfg.HasProperty("defaultSeoTitle")) { cfg.SetValue("defaultSeoTitle", $"{BrandName} — Composable Digital Solutions", Culture); }
        if (cfg.HasProperty("defaultSeoDescription")) { cfg.SetValue("defaultSeoDescription", "SynergosLabs es el motor editorial polimórfico: un código, un schema, múltiples productos sobre el mismo core.", Culture); }
        var save = _contentService.SaveAndPublish(cfg, new[] { Culture });
        details.Add(save.Success ? "SiteConfig:seo-ok" : $"SiteConfig:seo-failed:{save.Result}");
    }

    private IContent? FindByType(int parentId, string alias)
    {
        foreach (var child in _contentService.GetPagedChildren(parentId, 0, 200, out _))
        {
            if (child.ContentType.Alias == alias) { return child; }
            var deeper = FindByType(child.Id, alias);
            if (deeper is not null) { return deeper; }
        }
        return null;
    }

    private static bool Matches(IContent content, string name)
        => string.Equals(content.GetCultureName(Culture) ?? content.Name, name, StringComparison.Ordinal);

    public sealed record FillResult(bool Success, int PagesFilled, string Detail);
}
