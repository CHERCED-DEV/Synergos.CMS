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
    private const string ComponentCount = "120";       // cifra canónica de componentes (P1-9 — sin contradicciones entre páginas)
    private const string VerticalCount = "4";          // cifra canónica de verticales (P1-9)
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
    private string? _blogAuthorUdi;   // UDI del autor sembrado; los posts lo referencian en authorRef
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
        filled += Apply("Identidad", "Qué es SynergosLabs", BuildIdentidad(), details, 1);
        filled += Apply("Soluciones", "Soluciones para cada tipo de negocio", BuildSoluciones(), details, 2);
        filled += Apply("Productos", "Un motor, muchos productos", BuildProductos(), details, 3);
        filled += Apply("Cómo funciona", "De la idea a producción en cuatro pasos", BuildComoFunciona(), details, 4);
        filled += Apply("Precios", "Planes que crecen con tu negocio", BuildPrecios(), details, 5);
        filled += Apply("Casos", "Negocios que ya corren sobre SynergosLabs", BuildCasos(), details, 6);
        filled += Apply("Contacto", "Hablemos", BuildContacto(), details, 7);

        // El nodo "Home" anterior queda redundante → fuera del menú (no se borra).
        HideRedundantHome(site, details);

        // SEO defaults (fallback de title/description del brand) alineados a SynergosLabs.
        UpdateSiteConfigSeo(details);

        // Entry point: launcher en platformRoot.introBody + rename del umbrella a SynergosLabs.
        SeedPlatformLauncher(details);

        // P1-1: verticales Blogs (silverGold) + Ecommerce (dark) con identidad propia.
        SeedVerticalSiteRoots(details);
        SeedBlog(details);

        return new FillResult(filled == 8, filled, string.Join("; ", details));   // siteRoot home + 7 páginas hijas
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

    private int Apply(string pageName, string heading, string sectionsJson, List<string> details, int sortOrder = 0)
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
        if (sortOrder > 0) { page.SortOrder = sortOrder; }   // orden del nav = orden lógico, no orden de creación

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
        if (site.HasProperty("seoTitle")) { site.SetValue("seoTitle", $"{BrandName} — Soluciones digitales componibles", Culture); }
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
        AddHero(b, "Un motor. Mil productos digitales.",
            "La plataforma componible para lanzar tu negocio online",
            "<p>Marca, tienda, portal de membresía o sitio profesional: créalo, lánzalo y hazlo crecer sobre un mismo núcleo. Con SynergosLabs cambias el negocio, no la plataforma — y sales a producción en días.</p>",
            "Synergos Home Hero", "Composición abstracta de capas SynergosLabs", "#0A2540", "#0F58A7",
            ("Ver planes", "/synergos/precios"), ("Cómo funciona", "/synergos/como-funciona"));

        var features = new (string title, string subtitle, string body)[]
        {
            ("Lanza en días, no meses", "Empiezas listo", "Arrancas con una receta probada para tu tipo de negocio y la haces tuya. De la idea a producción sin proyectos eternos."),
            ("Un motor, todos tus productos", "Menos herramientas, menos costo", "Marca, e-commerce y membresías sobre la misma base: un solo equipo, una sola curva de aprendizaje, una sola factura."),
            ("Crece sin re-plataformar", "Escala cuando quieras", "Sumas verticales, dominios y componentes sin reemplazar lo que ya funciona. La plataforma crece contigo."),
        };
        if (_contentTypeService.Get("elementSynFeatureGrid")?.Key is not null)
        {
            AddSynFeatureGrid(b, "Por qué elegir SynergosLabs", features);
        }
        else
        {
            AddFeatureGrid(b, "Por qué elegir SynergosLabs", "Una base, todos tus productos", features);
        }

        AddStats(b,
            (ComponentCount, "componentes listos para usar"),
            (VerticalCount, "verticales de negocio"),
            ("1", "plataforma, multi-dominio"));

        AddSplit(b, "Tú compones. El motor hace el resto.",
            "Sin tocar código en el día a día",
            "<p>Tu equipo arma páginas completas arrastrando bloques en un editor visual. El motor se encarga del rendimiento, el SEO y la consistencia de marca — para que te enfoques en el negocio, no en la plomería.</p>",
            "Synergos Capas", "Editor visual de SynergosLabs", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: "Cómo funciona", ctaUrl: "/synergos/como-funciona");

        AddMission(b, "Sólido por dentro, simple por fuera",
            "La base técnica que tu equipo va a respetar",
            "<p>Arquitectura por capas, render server-side y un sistema de diseño con tokens. Potencia de ingeniería sin la complejidad: extensible cuando lo necesitas, robusto desde el primer día.</p>");

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
            ("¿Sirve para mi tipo de negocio?", "Casi seguro que sí. Marca, e-commerce, membresía o sitio profesional corren sobre el mismo motor. Si tu caso es distinto, lo adaptamos con la misma base."),
            ("¿Necesito un equipo técnico para operarlo?", "Para el día a día no: el contenido se arma arrastrando bloques. Tu equipo técnico entra solo cuando quieres extender o integrar algo nuevo."),
            ("¿Puedo empezar pequeño y crecer?", "Sí. Empiezas con un plan inicial y subes cuando el negocio lo pide — sin migraciones ni re-plataformar."),
        };
        if (_contentTypeService.Get("elementSynFaqSection")?.Key is not null)
        {
            AddSynFaq(b, "Preguntas frecuentes", faqs);
        }
        else
        {
            AddFaq(b, "Preguntas frecuentes", faqs);
        }

        AddCta(b, "Encuentra el plan para tu negocio",
            "Compara los planes y lanza esta semana — o habla con nosotros si tienes dudas.",
            "Ver planes", "/synergos/precios");
        return b.Build();
    }

    private string BuildIdentidad()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Qué es SynergosLabs",
            "La plataforma que convierte un negocio en muchos",
            "<p>SynergosLabs es una plataforma digital componible: un mismo motor con el que creas y operas marcas, tiendas y portales. En vez de construir y mantener un sistema por cada producto, lo haces todo sobre una base — y la haces crecer cuando quieras.</p>",
            "Synergos Identidad", "Identidad visual de la plataforma SynergosLabs", "#1A1A2E", "#7A3FF2",
            ("Ver soluciones", "/synergos/soluciones"), ("Ver planes", "/synergos/precios"));

        var synSplit = _contentTypeService.Get("elementSynMediaText")?.Key is not null;
        var bodyProposito = "<p>Piensa en SynergosLabs como el motor de tu presencia digital. Hoy lanzas tu marca; mañana sumas una tienda; después un portal de miembros. Todo con la misma cuenta, el mismo equipo y la misma identidad — sin empezar de cero cada vez.</p>";
        if (synSplit)
        {
            AddSynSplit(b, "En palabras simples", bodyProposito, "Synergos Proposito", "Cómo funciona SynergosLabs para tu negocio", "#7A3FF2", "#C04CFC", mediaOnRight: false);
        }
        else
        {
            AddSplit(b, "En palabras simples", "Una base, muchos negocios", bodyProposito,
                "Synergos Proposito", "Cómo funciona SynergosLabs para tu negocio", "#7A3FF2", "#C04CFC",
                mediaOnRight: false, ctaLabel: null, ctaUrl: null);
        }

        var bodyPolimorfico = "<p>Desde un profesional que necesita presencia en línea, hasta un grupo con varias marcas y dominios. Si tu negocio cambia o se expande, la plataforma te sigue el ritmo — en lugar de frenarte con una migración.</p>";
        if (synSplit)
        {
            AddSynSplit(b, "Para quién es", bodyPolimorfico, "Synergos Polimorfico", "Para qué negocios sirve SynergosLabs", "#0F58A7", "#1FA2A6", mediaOnRight: true);
        }
        else
        {
            AddSplit(b, "Para quién es", "Negocios que quieren crecer", bodyPolimorfico,
                "Synergos Polimorfico", "Para qué negocios sirve SynergosLabs", "#0F58A7", "#1FA2A6",
                mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        }

        FeatureGridAuto(b, "Qué incluye", "Todo lo que necesitas para operar", new (string title, string subtitle, string body)[]
        {
            ("Tu marca, en todo", "Identidad propia", "Logo, colores y tipografía aplicados a cada página, sin tocar código."),
            ("+120 componentes", "Listos para usar", "Bloques de contenido, comercio e interacción que tu equipo combina a voluntad."),
            ("Multi-dominio", "Una cuenta, varios sitios", "Marcas y dominios distintos sobre la misma plataforma y el mismo equipo."),
        }, 3);

        AddCta(b, "Veámoslo con tu negocio",
            "Cuéntanos qué quieres lanzar y te mostramos cómo SynergosLabs lo hace realidad.",
            "Hablar con ventas", "/synergos/contacto");
        return b.Build();
    }

    private string BuildProductos()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Un motor, muchos productos",
            "Lo que puedes construir con SynergosLabs",
            "<p>El mismo motor te da todo lo que un sitio necesita —contenido, comercio, miembros, formularios, búsqueda y SEO— en más de 120 componentes listos para combinar. Tú compones; el motor hace el resto.</p>",
            "Synergos Productos", "Capacidades de SynergosLabs", "#143C8C", "#1FA2A6",
            ("Ver soluciones", "/synergos/soluciones"), ("Ver planes", "/synergos/precios"));

        // P1-10: Productos = CAPACIDADES (qué incluye el motor). Las industrias/casos
        // viven en Soluciones → las dos páginas dejan de solaparse.
        var capabilities = new (string title, string subtitle, string body)[]
        {
            ("Contenido", "Hero, features, testimonios, FAQ…", "Más de 120 bloques editoriales que tu equipo combina en un editor visual, sin código."),
            ("Comercio", "Catálogo · carrito · checkout", "Producto, variantes, carrito y consultas server-side listos para tu tienda."),
            ("Miembros y acceso", "Login · roles · 2FA · portal", "Contenido protegido, autoservicio de miembros y doble factor de fábrica."),
            ("Formularios y captación", "Forms · anti-spam · avisos", "Captura leads con honeypot, rate-limit y notificación por email — sin integraciones."),
            ("Búsqueda y SEO", "Examine · sitemap · JSON-LD", "Búsqueda full-text y SEO técnico (sitemap, robots, datos estructurados) incluidos."),
            ("Identidad por marca", "Tema · tokens · multi-dominio", "Una identidad por sitio: color, tipografía y logo se aplican a todo desde un solo lugar."),
        };
        if (_contentTypeService.Get("elementSynFeatureGrid")?.Key is not null)
        {
            AddSynFeatureGrid(b, "Lo que incluye el motor", capabilities, 3);
        }
        else
        {
            AddFeatureGrid(b, "Lo que incluye el motor", "Todo lo que un sitio necesita, listo para combinar", capabilities);
        }

        AddStats(b,
            (VerticalCount, "verticales base"),
            (ComponentCount, "componentes reutilizables"),
            ("1", "deploy, multi-dominio"));

        AddSplit(b, "El mismo motor, tu marca",
            "Tu identidad, de punta a punta",
            "<p>Colores, tipografía, logo y tono se aplican a todo el sitio desde un solo lugar. Cada negocio luce 100% propio — el motor es el mismo, la marca es tuya.</p>",
            "Synergos Branding", "Personalización de marca en SynergosLabs", "#0F58A7", "#7A3FF2",
            mediaOnRight: true, ctaLabel: "Qué es SynergosLabs", ctaUrl: "/synergos/identidad");

        AddCta(b, "¿Cuál es tu caso?",
            "Mira las soluciones por tipo de negocio o cuéntanos el tuyo y te mostramos la receta que encaja.",
            "Ver soluciones", "/synergos/soluciones");
        return b.Build();
    }

    private string BuildContacto()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Hablemos de tu proyecto",
            "Una demo de 30 minutos, sin compromiso",
            "<p>Cuéntanos qué quieres lanzar y te mostramos en vivo cómo SynergosLabs lo resuelve — y qué plan encaja con tu negocio. Sin tecnicismos si no los quieres.</p>",
            "Synergos Contacto", "Sección de contacto de SynergosLabs", "#0A2540", "#1FA2A6",
            ("Ver planes", "/synergos/precios"), ("Ver soluciones", "/synergos/soluciones"));

        AddContactForm(b, "Cuéntanos de tu proyecto", "Enviar mensaje",
            ("Nombre", "nombre", "text", true, "Tu nombre"),
            ("Email", "email", "email", true, "tu@correo.com"),
            ("Empresa", "empresa", "text", false, "Tu empresa (opcional)"),
            ("¿Qué quieres lanzar?", "mensaje", "textarea", true, "Cuéntanos brevemente tu proyecto…"));

        AddMission(b, "Cómo trabajamos",
            "Directo, claro, a tu ritmo",
            "<p>Empezamos por tu objetivo de negocio, no por la tecnología. Te mostramos qué puedes lanzar ya y cómo crecer después — y si tu equipo es técnico, entramos en el detalle que quieras.</p>");

        var contactFaqs = new (string question, string answer)[]
        {
            ("¿Qué necesito para la demo?", "Una idea de lo que quieres lanzar (marca, tienda, membresía…) y, si tienes, tu logo y colores. Del resto nos encargamos nosotros."),
            ("¿Cuánto dura?", "30 minutos. Sales con un plan concreto y una recomendación de plan para tu caso."),
            ("¿Trabajan sobre mi marca?", "Sí. Tu identidad se aplica a todo el sitio — tu marca, nuestro motor."),
        };
        if (_contentTypeService.Get("elementSynFaqSection")?.Key is not null)
        {
            AddSynFaq(b, "Antes de escribirnos", contactFaqs);
        }
        else
        {
            AddFaq(b, "Antes de escribirnos", contactFaqs);
        }

        AddCta(b, "¿Prefieres ver los planes primero?",
            "Compara los planes y elige el que encaja — o llena el formulario y te contactamos.",
            "Ver planes", "/synergos/precios");
        return b.Build();
    }

    // Auto-fallback: usa el componente Angular (CDN) si su ElementType está importado; si no, SSR.
    private void FeatureGridAuto(BlockGridJsonBuilder b, string heading, string subtitle, (string title, string subtitle, string body)[] features, int columns = 3)
    {
        if (_contentTypeService.Get("elementSynFeatureGrid")?.Key is not null) { AddSynFeatureGrid(b, heading, features, columns); }
        else { AddFeatureGrid(b, heading, subtitle, features); }
    }

    private void FaqAuto(BlockGridJsonBuilder b, string heading, (string question, string answer)[] items)
    {
        if (_contentTypeService.Get("elementSynFaqSection")?.Key is not null) { AddSynFaq(b, heading, items); }
        else { AddFaq(b, heading, items); }
    }

    private void SplitAuto(BlockGridJsonBuilder b, string title, string subtitle, string body,
        string imgName, string imgAlt, string from, string to, bool mediaOnRight, string? ctaLabel = null, string? ctaUrl = null)
    {
        if (_contentTypeService.Get("elementSynMediaText")?.Key is not null) { AddSynSplit(b, title, body, imgName, imgAlt, from, to, mediaOnRight); }
        else { AddSplit(b, title, subtitle, body, imgName, imgAlt, from, to, mediaOnRight, ctaLabel, ctaUrl); }
    }

    // ─────────── Soluciones — por tipo de negocio (orientada al comprador) ───────────
    private string BuildSoluciones()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Una solución para cada tipo de negocio",
            "Elige tu caso y arranca sobre un motor probado",
            "<p>No importa qué vendas o a quién: marca, tienda, membresía o servicios profesionales corren sobre el mismo núcleo de SynergosLabs. Eliges tu caso, pones tu marca y sales a producción en días.</p>",
            "Synergos Soluciones Hero", "Soluciones de SynergosLabs por tipo de negocio", "#0A2540", "#1FA2A6",
            ("Ver planes", "/synergos/precios"), ("Hablar con ventas", "/synergos/contacto"));

        var verticals = new (string title, string subtitle, string body)[]
        {
            ("Marca y empresa", "Posiciona tu marca", "Sitio institucional, casos de éxito, blog y vacantes — con la identidad de tu marca y listo para crecer."),
            ("Tienda online", "Vende sin fricción", "Catálogo, variantes, carrito y checkout sobre el mismo motor. Tu e-commerce, con tu marca y tu dominio."),
            ("Membresía y portal", "Fideliza a tu comunidad", "Contenido protegido, login y autoservicio de miembros. Ideal para una comunidad, una academia o un SaaS."),
            ("Profesional independiente", "Consigue más clientes", "Médico, abogado o consultor: presencia, servicios, agenda y contacto — en línea en cuestión de días."),
        };
        FeatureGridAuto(b, "Soluciones disponibles", "Una receta por tipo de cliente", verticals, 2);

        SplitAuto(b, "Cambias el negocio, no la plataforma",
            "El mismo motor, mil formas",
            "<p>El secreto: un solo núcleo componible. Cuando tu negocio cambia o suma una línea, no reemplazas la tecnología — agregas un vertical y publicas. Menos riesgo, menos costo, más velocidad.</p>",
            "Synergos Polimorfico", "El motor componible de SynergosLabs", "#0F58A7", "#7A3FF2",
            mediaOnRight: true, ctaLabel: "Cómo funciona", ctaUrl: "/synergos/como-funciona");

        AddStats(b,
            ("4", "tipos de negocio sobre un core"),
            (ComponentCount, "componentes reutilizables"),
            ("1", "plataforma, multi-dominio"));

        AddCta(b, "¿No ves tu caso?",
            "El mismo motor se adapta a negocios fuera de catálogo. Cuéntanos el tuyo y te mostramos cómo encaja.",
            "Hablar con ventas", "/synergos/contacto");
        return b.Build();
    }

    // ─────────── Cómo funciona — proceso en 4 pasos + reaseguro técnico ───────────
    private string BuildComoFunciona()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "De la idea a producción en cuatro pasos",
            "Simple para ti, sólido por dentro",
            "<p>Lanzar con SynergosLabs no es un proyecto de meses. Eliges una base lista, la haces tuya y publicas — mientras el motor se encarga del rendimiento, el SEO y la consistencia.</p>",
            "Synergos Como Funciona Hero", "Proceso de SynergosLabs en cuatro pasos", "#143C8C", "#5B7CFA",
            ("Ver planes", "/synergos/precios"), ("Ver soluciones", "/synergos/soluciones"));

        var steps = new (string title, string subtitle, string body)[]
        {
            ("1 · Elige tu base", "Empiezas listo", "Arrancas con una receta probada para tu tipo de negocio — no desde una página en blanco."),
            ("2 · Pon tu marca", "Tu identidad en minutos", "Logo, colores y tipografía se aplican a todo el sitio sin tocar una línea de código."),
            ("3 · Compón tu contenido", "Arrastra y suelta", "Tu equipo arma las páginas con el editor visual y más de 120 componentes listos para usar."),
            ("4 · Publica y crece", "En vivo en días", "Sales a producción y sumas dominios, verticales y componentes cuando el negocio lo pida."),
        };
        FeatureGridAuto(b, "El proceso", "Cuatro pasos, sin fricción", steps, 2);

        SplitAuto(b, "Sólido para tu equipo técnico",
            "Potencia de ingeniería, sin la complejidad",
            "<p>Bajo el capó: arquitectura por capas, render server-side y un sistema de diseño con tokens. Extensible cuando lo necesitas, robusto desde el primer día — la base que tu equipo va a respetar.</p>",
            "Synergos Capas", "Arquitectura por capas de SynergosLabs", "#0F58A7", "#1FA2A6",
            mediaOnRight: false, ctaLabel: "Qué es SynergosLabs", ctaUrl: "/synergos/identidad");

        AddCta(b, "Empieza hoy",
            "Elige un plan y publica tu primer producto esta semana — o agenda una demo y te lo mostramos en vivo.",
            "Ver planes", "/synergos/precios");
        return b.Build();
    }

    // ─────────── Precios — escalera estilo MercadoLibre (placeholders editables) ───────────
    private string BuildPrecios()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Planes que crecen con tu negocio",
            "Empieza gratis, escala cuando quieras",
            "<p>Todos los planes corren sobre el mismo motor: cambias capacidad y soporte, nunca la tecnología. Sin permanencia — subes, bajas o cancelas cuando lo necesites.</p>",
            "Synergos Precios Hero", "Planes de SynergosLabs", "#0A2540", "#0F58A7",
            ("Hablar con ventas", "/synergos/contacto"), ("Ver soluciones", "/synergos/soluciones"));

        // Cifras placeholder estilo MercadoLibre (gratis → profesional → premium). El arquitecto
        // ajusta los precios reales. Tabla composable (precio = DataType) si está importada; si no, SSR.
        if (_contentTypeService.Get("elementPricingTable")?.Key is not null)
        {
            AddPricingTable(b, "Elige tu plan", "Del primer sitio a todo un ecosistema",
                ("Inicial", "Gratis", "", "",
                 "1 sitio o vertical\nComponentes esenciales\nPublicación en subdominio\nSoporte por comunidad",
                 false, "Empezar gratis", "/synergos/contacto"),
                ("Profesional", "$129.900", "/mes", "El más elegido",
                 "Todo lo de Inicial\nMulti-vertical\nDominio propio\n+120 componentes\nBranding completo\nSoporte prioritario",
                 true, "Elegir Profesional", "/synergos/contacto"),
                ("Premium", "A tu medida", "", "",
                 "Todo lo de Profesional\nMulti-dominio\nSLA garantizado\nIntegraciones a medida\nAcompañamiento dedicado",
                 false, "Hablar con ventas", "/synergos/contacto"));
        }
        else
        {
            var plans = new (string title, string subtitle, string body)[]
            {
                ("Inicial", "Gratis", "Para validar tu idea: 1 sitio, los componentes esenciales, publicación en subdominio. Sin costo, para siempre."),
                ("Profesional", "Desde $129.900 COP/mes", "El más elegido: multi-vertical, dominio propio, más de 120 componentes, branding completo y soporte prioritario."),
                ("Premium", "A tu medida", "Para escalar sin límites: multi-dominio, SLA, integraciones a medida y un equipo de acompañamiento dedicado."),
            };
            FeatureGridAuto(b, "Elige tu plan", "Del primer sitio a todo un ecosistema", plans, 3);
        }

        AddMission(b, "¿Cuál me conviene?",
            "Mismo motor, distinta capacidad",
            "<p>Si estás validando, empieza en Inicial. Si ya vendes o necesitas tu dominio y soporte, Profesional. Si manejas varias marcas o dominios, Premium. Cambias de plan cuando quieras, sin migraciones.</p>");

        var faqs = new (string question, string answer)[]
        {
            ("¿Puedo cambiar de plan después?", "Sí. Subes o bajas cuando quieras y los cambios aplican de inmediato — sin migraciones ni re-plataformar."),
            ("¿Hay permanencia?", "No. Los planes pagos son mes a mes; cancelas cuando quieras sin penalidad."),
            ("¿Qué incluye el plan gratis?", "Lo necesario para lanzar y validar: un sitio, los componentes esenciales y publicación en un subdominio."),
        };
        FaqAuto(b, "Preguntas sobre los planes", faqs);

        AddCta(b, "¿Dudas sobre qué plan elegir?",
            "Hablemos 15 minutos y te recomendamos el que mejor encaja con tu negocio. Sin compromiso.",
            "Hablar con ventas", "/synergos/contacto");
        return b.Build();
    }

    // ─────────── Casos — prueba social orientada a resultados ───────────
    private string BuildCasos()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Negocios que ya corren sobre SynergosLabs",
            "Resultados, no promesas",
            "<p>Equipos que cambiaron meses de desarrollo por semanas, y varias herramientas por una sola plataforma. Estas son sus historias.</p>",
            "Synergos Casos Hero", "Casos de clientes de SynergosLabs", "#1A1A2E", "#7A3FF2",
            ("Ver planes", "/synergos/precios"), ("Hablar con ventas", "/synergos/contacto"));

        AddStats(b,
            (VerticalCount, "verticales en producción"),
            (ComponentCount, "componentes reutilizados"),
            ("1", "plataforma para todo el grupo"));

        // P1-11: testimonios DISTINTOS a los del Home (evita la prueba social duplicada).
        var testimonials = new (string quote, string author, string role)[]
        {
            ("Pasamos de cinco sitios inconexos a una sola plataforma. El mantenimiento cayó a la mitad.", "Andrés Villa", "COO, Retail Norte"),
            ("Abrimos una línea de negocio nueva en dos semanas, sin tocar lo que ya estaba en producción.", "Carolina Ruiz", "Gerente de E-commerce, Pacífico"),
            ("Un solo equipo opera marca, tienda y comunidad. Menos proveedores, menos fricción, más foco.", "Mateo Salas", "Director de Tecnología, Altavista"),
        };
        if (_contentTypeService.Get("elementSynTestimonialSection")?.Key is not null)
        {
            AddSynTestimonials(b, "Lo que dicen nuestros clientes", testimonials);
        }
        else
        {
            AddTestimonials(b, testimonials);
        }

        SplitAuto(b, "Cómo lo lograron",
            "Una base, muchas marcas",
            "<p>En vez de mantener un sitio por cada negocio, el Grupo Andino consolidó marca, tienda y membresía sobre un solo motor. Menos costo de mantenimiento, una identidad coherente y la libertad de sumar verticales sin re-plataformar.</p>",
            "Synergos Capas", "Resultado de consolidar en SynergosLabs", "#0F58A7", "#1FA2A6",
            mediaOnRight: true, ctaLabel: "Ver soluciones", ctaUrl: "/synergos/soluciones");

        AddCta(b, "¿Listo para ser el próximo caso?",
            "Empieza con el plan que encaja con tu negocio — o cuéntanos tu caso y te mostramos el camino.",
            "Ver planes", "/synergos/precios");
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

    // Formulario de contacto: reusa el sistema de Forms existente (elementFormContainer +
    // elementFormField, POST a /api/forms/{formInternalKey}/submit → FormSubmissionsController
    // → honeypot + rate-limit + persistencia + notificación email FormNotification). ADR 0018 + 0030.
    private void AddContactForm(BlockGridJsonBuilder b, string title, string submitLabel,
        params (string label, string name, string type, bool required, string placeholder)[] fields)
    {
        var formKey = _contentTypeService.Get("elementFormContainer")?.Key;
        var fieldKey = _contentTypeService.Get("elementFormField")?.Key;
        if (formKey is null || fieldKey is null) { return; }

        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));

        var list = new BlockListJsonBuilder();
        foreach (var (label, name, type, required, placeholder) in fields)
        {
            list.AddBlock(fieldKey.Value)
                .Set("fieldLabel", label)
                .Set("fieldName", name)
                .Set("fieldType", type)
                .Set("fieldRequired", required ? "1" : "0")
                .Set("fieldPlaceholder", placeholder)
                .ApplyDefaults(_defaults.DefaultsFor(fieldKey.Value));
        }

        section.AddChild(SectionContentAreaKey, formKey.Value, c =>
        {
            c.Set("formTitle", title)
             .Set("formInternalKey", "contacto")   // → /api/forms/contacto/submit (kebab, matchea FormKeyRegex)
             .Set("submitLabel", submitLabel);
            if (list.HasItems) { c.Set("fields", list.Build()); }
            c.ApplyDefaults(_defaults.DefaultsFor(formKey.Value));
        });
    }

    // Tabla de precios composable: elementPricingTable + elementPricingPlan (DataTypes propios:
    // precio, periodo, tagline, features, destacado, CTA). SSR. El editor configura todo en backoffice.
    private void AddPricingTable(BlockGridJsonBuilder b, string heading, string subheading,
        params (string name, string price, string period, string tagline, string features, bool highlighted, string ctaLabel, string ctaUrl)[] plans)
    {
        var tableKey = _contentTypeService.Get("elementPricingTable")?.Key;
        var planKey = _contentTypeService.Get("elementPricingPlan")?.Key;
        if (tableKey is null || planKey is null) { return; }

        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));

        var list = new BlockListJsonBuilder();
        foreach (var p in plans)
        {
            var block = list.AddBlock(planKey.Value)
                .Set("planName", p.name)
                .Set("planPrice", p.price)
                .Set("planPeriod", p.period)
                .Set("planTagline", p.tagline)
                .Set("planFeatures", p.features)
                .Set("planHighlighted", p.highlighted ? "1" : "0")
                .Set("planCtaLabel", p.ctaLabel);
            if (!string.IsNullOrEmpty(p.ctaUrl)) { block.Set("planCtaLink", LinkJson(p.ctaLabel, p.ctaUrl)); }
            block.ApplyDefaults(_defaults.DefaultsFor(planKey.Value));
        }

        section.AddChild(SectionContentAreaKey, tableKey.Value, c =>
        {
            c.Set("tableHeading", heading).Set("tableSubheading", subheading);
            if (list.HasItems) { c.Set("plans", list.Build()); }
            c.ApplyDefaults(_defaults.DefaultsFor(tableKey.Value));
        });
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
        // P2-8: los verticales Blogs/Tienda ya existen (P1-1) → cards "live" a su siteRoot.
        AddCard("Blogs", "<p>Publicaciones, artículos y contenido editorial sobre el mismo motor.</p>",
            "syn-launcher__card--document", "syn-launcher__card--live", "Entrar a Blogs →", "/blogs");
        AddCard("Tienda", "<p>Catálogo, productos y checkout sobre la misma plataforma.</p>",
            "syn-launcher__card--bag", "syn-launcher__card--live", "Entrar a la Tienda →", "/tienda");

        pr.SetCultureName(BrandName, Culture);   // umbrella = SynergosLabs (el hero lee Model.Name)
        // P2-3: identidad propia del launcher (compBranding + compPageTheme) — gestionable
        // en vez de hardcodeada. HasProperty guarda hasta que se importe el schema.
        if (pr.HasProperty("brandKey")) { pr.SetValue("brandKey", "synergoslabs"); }              // ^[a-z][a-z0-9-]*$
        if (pr.HasProperty("brandDisplayName")) { pr.SetValue("brandDisplayName", BrandName, Culture); }
        if (pr.HasProperty("pageThemeVariant")) { pr.SetValue("pageThemeVariant", "[\"dark\"]"); }  // launcher PS3 oscuro
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
        if (cfg.HasProperty("defaultSeoTitle")) { cfg.SetValue("defaultSeoTitle", $"{BrandName} — Soluciones digitales componibles", Culture); }
        if (cfg.HasProperty("defaultSeoDescription")) { cfg.SetValue("defaultSeoDescription", "SynergosLabs es el motor editorial polimórfico: un código, un schema, múltiples productos sobre el mismo core.", Culture); }
        // P0-3: OG image de marca. Sin socialOgImage, el _SeoHead omite og:image,
        // twitter:image y Organization.logo → links compartidos salen sin marca.
        if (cfg.HasProperty("socialOgImage"))
        {
            var og = _media.GetOrCreateOgImagePickerValue("SynergosLabs OG", "SynergosLabs — Soluciones digitales componibles");
            cfg.SetValue("socialOgImage", og, Culture);
        }
        var save = _contentService.SaveAndPublish(cfg, new[] { Culture });
        details.Add(save.Success ? "SiteConfig:seo-ok" : $"SiteConfig:seo-failed:{save.Result}");
    }

    /// <summary>
    /// P1-1: crea los siteRoots verticales (Blogs=silverGold, Ecommerce=dark) bajo el
    /// platformRoot, con identidad propia (siteDisplayName + brandKey + brandDisplayName
    /// + pageThemeVariant) y un home mínimo. Idempotente por nombre. "Un motor, mil
    /// productos": que más de un vertical pinte su tema. La resolución de marca por
    /// HOSTNAME requiere configurar Culture &amp; Hostnames (arquitecto); por path
    /// (/blogs, /tienda) ya pintan su pageThemeVariant.
    /// </summary>
    private void SeedVerticalSiteRoots(List<string> details)
    {
        var pr = _contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == "platformRoot");
        if (pr is null) { details.Add("Verticals:platformroot-not-found"); return; }

        SeedVertical(pr.Id, "Blogs", "blogs", "SynergosLabs Blogs", "silverGold", "blogs.synergos.local",
            BuildBlogsHome(), details);

        SeedVertical(pr.Id, "Tienda", "ecommerce", "SynergosLabs Tienda", "dark", "tienda.synergos.local",
            BuildVerticalHome("Tu tienda, sobre el mismo motor", "Catálogo, carrito y checkout",
                "<p>Vende online con producto, variantes, carrito y query server-side sobre el mismo núcleo — sin re-plataformar cuando crezcas.</p>",
                "Synergos Tienda Hero", "#020817", "#4f6ef7"), details);
    }

    private void SeedVertical(int parentId, string name, string brandKey, string brandDisplayName,
        string themeVariant, string hostname, string sectionsJson, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot" && Matches(c, name));
        var site = existing ?? _contentService.Create(name, parentId, "siteRoot");
        site.SetCultureName(name, Culture);
        site.SetValue("siteDisplayName", name, Culture);              // mandatory (Culture)
        site.SetValue("brandKey", brandKey);                          // mandatory (Nothing), ^[a-z][a-z0-9-]*$
        site.SetValue("brandDisplayName", brandDisplayName, Culture); // mandatory (Culture)
        site.SetValue("pageThemeVariant", $"[\"{themeVariant}\"]");   // FlexibleDropdown → JSON array (trampa conocida)
        if (site.HasProperty("canonicalHostname")) { site.SetValue("canonicalHostname", hostname); }
        site.SetValue(SectionsAlias, sectionsJson, Culture);
        var save = _contentService.SaveAndPublish(site, new[] { Culture });
        details.Add(save.Success ? $"Vertical:{name}:ok({themeVariant})" : $"Vertical:{name}:failed:{save.Result}");
    }

    private string BuildVerticalHome(string title, string subtitle, string bodyHtml, string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, title, subtitle, bodyHtml,
            mediaName, $"Hero del vertical {title}", hexFrom, hexTo,
            ("Hablemos", "/synergos/contacto"), ("Ver planes", "/synergos/precios"));
        return b.Build();
    }

    // ───────────────── Blog (entrega grande componible, ADR 0027) ─────────────────
    // La infra de blog ya existe (postCategoryPage/postPage + IBlogQuery + bloques
    // ArticleList/BlogHighlight). Acá sembramos CONTENIDO componible: categorías +
    // posts bajo el siteRoot Blogs, y el feed featureado en su home. Idempotente por
    // nombre; no-op si el schema de blog no está importado.
    private void SeedBlog(List<string> details)
    {
        if (_contentTypeService.Get("postCategoryPage")?.Key is null
            || _contentTypeService.Get("postPage")?.Key is null)
        {
            details.Add("Blog:schema-not-imported");
            return;
        }
        var blogs = FindBlogsSiteRoot();
        if (blogs is null) { details.Add("Blog:blogs-siteroot-not-found"); return; }

        const string gFrom = "#2A2412", gTo = "#B59659";   // gradiente dorado (identidad Blogs)

        _blogAuthorUdi = SeedAuthor(blogs.Id, "Equipo SynergosLabs", "Redacción",
            "<p>El equipo de producto e ingeniería de SynergosLabs. Escribimos sobre cómo construimos un motor componible y qué aprendemos en el camino.</p>",
            details);

        var producto = SeedCategory(blogs.Id, "Producto",
            "Novedades, lanzamientos y guías de SynergosLabs.", details);
        var ingenieria = SeedCategory(blogs.Id, "Ingeniería",
            "Cómo construimos el motor: arquitectura y decisiones.", details);

        if (producto > 0)
        {
            SeedPost(producto, "Un motor, mil productos: la idea detrás de SynergosLabs",
                "Por qué un solo núcleo componible puede ser marca, tienda, membresía o blog — sin reescribir nada.",
                "2026-06-22", "5", new[] { "producto", "plataforma" }, gFrom, gTo,
                BuildArticle(
                    "La idea: un núcleo componible",
                    "<p>La promesa de SynergosLabs es simple: un solo motor, mil productos. En vez de un CMS por cada tipo de sitio, hay un núcleo componible que se adapta poniéndole tu marca y eligiendo tu vertical.</p>",
                    "Componer, no programar",
                    "<p>Tu equipo arma páginas completas arrastrando bloques en un editor visual. El motor se encarga del rendimiento, el SEO y la consistencia de marca.</p>",
                    "Blog Motor Componible", gFrom, gTo), details);

            SeedPost(producto, "Lanzamos los verticales: Blogs y Tienda",
                "Dos verticales nuevos sobre el mismo motor, cada uno con su identidad: Blogs en dorado, Tienda en oscuro.",
                "2026-06-18", "4", new[] { "producto", "lanzamiento" }, gFrom, gTo,
                BuildArticle(
                    "Dos verticales, un motor",
                    "<p>Estrenamos dos verticales que comparten núcleo pero no identidad: <strong>Blogs</strong> con una línea editorial cálida en dorado, y <strong>Tienda</strong> con un tono comercial oscuro.</p>",
                    "Identidad por siteRoot",
                    "<p>Cada vertical pinta su propia paleta, tipografía y tono desde un solo lugar — sin tocar el código del motor.</p>",
                    "Blog Verticales", gFrom, gTo), details);

            SeedPost(producto, "Componer en vez de programar: el editor visual",
                "Cómo el Layout Composer deja que el equipo editorial arme páginas completas sin pasar por desarrollo.",
                "2026-06-12", "6", new[] { "producto", "editor" }, gFrom, gTo,
                BuildArticle(
                    "El contenido no debería ser código",
                    "<p>El look es código (CSS/JS); el contenido no. El Layout Composer separa ambos: el equipo editorial compone arrastrando bloques, no escribiendo plantillas.</p>",
                    "Bloques gobernados por diseño",
                    "<p>Más de 120 bloques usan tokens de diseño (grilla de 8, Manrope, paleta de marca) para que todo salga coherente por defecto.</p>",
                    "Blog Editor Visual", gFrom, gTo), details);
        }

        if (ingenieria > 0)
        {
            SeedPost(ingenieria, "Arquitectura por capas: el grafo de dependencias",
                "Interfaces ← Application ← Web ← Tests. Cómo un grafo unidireccional mantiene el motor mantenible a escala.",
                "2026-06-20", "7", new[] { "ingeniería", "arquitectura" }, gFrom, gTo,
                BuildArticle(
                    "Dependencias en una sola dirección",
                    "<p>Interfaces ← Application ← Web ← Tests. La capa de aplicación no conoce Umbraco ni ASP.NET; eso la hace probable y portable.</p>",
                    "Seams, no acoplamiento",
                    "<p>Cada integración externa entra por una interfaz (seam). Cambiar de proveedor no toca a los consumidores.</p>",
                    "Blog Arquitectura", gFrom, gTo), details);

            SeedPost(ingenieria, "Identidad por siteRoot: una marca, mil caras",
                "Cómo un solo deploy sirve múltiples marcas por hostname, cada una con su paleta — sin multi-tenant.",
                "2026-06-15", "5", new[] { "ingeniería", "identidad" }, gFrom, gTo,
                BuildArticle(
                    "Multi-marca no es multi-tenant",
                    "<p>Un mismo deploy resuelve la marca activa por hostname y aplica su identidad vía tokens — sin middleware de tenants.</p>",
                    "Tokens como fuente de verdad",
                    "<p>Color, tipografía y radios viven en tokens; cada marca mapea los suyos y el resto del sistema los consume.</p>",
                    "Blog Identidad", gFrom, gTo), details);

            SeedPost(ingenieria, "CDN híbrida: componentes Angular sobre SSR",
                "Bloques server-side por defecto, hidratados con componentes Angular desde la CDN cuando aportan valor.",
                "2026-06-10", "6", new[] { "ingeniería", "cdn" }, gFrom, gTo,
                BuildArticle(
                    "Lo mejor de dos mundos",
                    "<p>Render server-side para velocidad y SEO, más islas Angular publicadas a una CDN cuando una pieza necesita interactividad rica.</p>",
                    "Framework-agnóstico",
                    "<p>El schema no conoce el framework; un cliente de registry resuelve el bundle en runtime. Angular es el primer adapter, no un lock-in.</p>",
                    "Blog CDN", gFrom, gTo), details);
        }

        details.Add($"Blog:seeded(prod={producto > 0},ing={ingenieria > 0})");
    }

    private int SeedCategory(int parentId, string name, string description, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(parentId, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "postCategoryPage" && Matches(c, name));
        var cat = existing ?? _contentService.Create(name, parentId, "postCategoryPage");
        cat.SetCultureName(name, Culture);
        cat.SetValue("categoryName", name, Culture);                 // mandatory (Culture)
        if (cat.HasProperty("description")) { cat.SetValue("description", description, Culture); }
        if (cat.HasProperty("seoTitle")) { cat.SetValue("seoTitle", $"{name} — Blog {BrandName}", Culture); }
        if (cat.HasProperty("seoDescription")) { cat.SetValue("seoDescription", description, Culture); }
        var save = _contentService.SaveAndPublish(cat, new[] { Culture });
        details.Add(save.Success ? $"Blog:cat:{name}:ok" : $"Blog:cat:{name}:failed:{save.Result}");
        return save.Success ? cat.Id : 0;
    }

    private string? SeedAuthor(int parentId, string name, string role, string bioHtml, List<string> details)
    {
        if (_contentTypeService.Get("authorPage")?.Key is null)
        {
            details.Add("Blog:author-schema-not-imported");
            return null;
        }
        var existing = _contentService.GetPagedChildren(parentId, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "authorPage" && Matches(c, name));
        var author = existing ?? _contentService.Create(name, parentId, "authorPage");
        author.SetCultureName(name, Culture);
        if (author.HasProperty("authorName")) { author.SetValue("authorName", name, Culture); }
        if (author.HasProperty("authorRole")) { author.SetValue("authorRole", role, Culture); }
        if (author.HasProperty("authorAvatar"))
        {
            author.SetValue("authorAvatar", _media.GetOrCreatePickerValue($"Avatar {name}", name, "#2A2412", "#B59659", 320, 320), Culture);
        }
        var b = new BlockGridJsonBuilder();
        AddMission(b, $"Sobre {name}", "", bioHtml);
        author.SetValue(SectionsAlias, b.Build(), Culture);
        if (author.HasProperty("seoTitle")) { author.SetValue("seoTitle", $"{name} — Blog {BrandName}", Culture); }
        var save = _contentService.SaveAndPublish(author, new[] { Culture });
        details.Add(save.Success ? $"Blog:author:{name}:ok" : $"Blog:author:{name}:failed:{save.Result}");
        return save.Success ? $"umb://document/{author.Key:N}" : null;
    }

    private void SeedPost(int categoryId, string title, string excerpt, string publishDate,
        string readTimeMinutes, string[] tags, string hexFrom, string hexTo, string sectionsJson, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(categoryId, 0, 500, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "postPage" && Matches(c, title));
        var post = existing ?? _contentService.Create(title, categoryId, "postPage");
        post.SetCultureName(title, Culture);
        post.SetValue("publishDate", publishDate);                   // mandatory (Nothing), ISO YYYY-MM-DD
        if (post.HasProperty("readTimeMinutes")) { post.SetValue("readTimeMinutes", readTimeMinutes); }  // Nothing
        if (post.HasProperty("excerpt")) { post.SetValue("excerpt", excerpt, Culture); }
        if (post.HasProperty("heroImage"))
        {
            post.SetValue("heroImage", _media.GetOrCreatePickerValue($"Blog {title}", title, hexFrom, hexTo, 1600, 720), Culture);
        }
        if (post.HasProperty("tags")) { post.SetValue("tags", TagsJson(tags), Culture); }   // Umbraco.Tags (Culture)
        post.SetValue(SectionsAlias, sectionsJson, Culture);
        if (_blogAuthorUdi is not null && post.HasProperty("authorRef")) { post.SetValue("authorRef", _blogAuthorUdi, Culture); }
        if (post.HasProperty("seoTitle")) { post.SetValue("seoTitle", $"{title} — {BrandName}", Culture); }
        if (post.HasProperty("seoDescription")) { post.SetValue("seoDescription", excerpt, Culture); }
        var save = _contentService.SaveAndPublish(post, new[] { Culture });
        var label = title.Length > 24 ? title[..24] : title;
        details.Add(save.Success ? $"Blog:post:{label}:ok" : $"Blog:post:{label}:failed:{save.Result}");
    }

    // Cuerpo de artículo componible: sección de intro + split con imagen + CTA.
    // NO incluye hero: el template PostPage ya renderiza heroImage + título + meta.
    private string BuildArticle(string h1, string b1, string h2, string b2,
        string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddMission(b, h1, "", b1);
        AddSplit(b, h2, "", b2, mediaName, h2, hexFrom, hexTo, mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        AddCta(b, "¿Querés ver SynergosLabs en acción?",
            "Una demo de 30 minutos, sin compromiso.", "Hablar con ventas", "/synergos/contacto");
        AddCommentThread(b, "Comentarios");
        return b.Build();
    }

    // Home del vertical Blogs: hero + feed (destacados + listado). Los bloques de feed
    // consultan IBlogQuery en runtime, así que muestran los posts apenas existan.
    private string BuildBlogsHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Blogs que enganchan", "Publica, organiza y crece tu audiencia",
            "<p>Un blog editorial sobre el mismo motor: categorías, autores y SEO listos. Tu marca, tu voz — sin montar un CMS desde cero.</p>",
            "Synergos Blogs Hero", "Hero del vertical Blogs", "#2A2412", "#B59659",
            ("Hablemos", "/synergos/contacto"), ("Ver planes", "/synergos/precios"));
        AddBlogHighlight(b, "Lo más reciente");
        AddArticleList(b, "Todos los artículos");
        return b.Build();
    }

    private void AddBlogHighlight(BlockGridJsonBuilder b, string title)
    {
        var key = _contentTypeService.Get("elementCompBlogHighlight")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("highlightTitle", title)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    private void AddArticleList(BlockGridJsonBuilder b, string title)
    {
        var key = _contentTypeService.Get("elementCompArticleList")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("listTitle", title)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    // Hilo de comentarios (ADR 0038). Se auto-enlaza al nodo actual al renderizar,
    // así que dropearlo en el cuerpo del post muestra los comentarios de ese post.
    private void AddCommentThread(BlockGridJsonBuilder b, string heading)
    {
        var key = _contentTypeService.Get("elementCommentThread")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("heading", heading)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    private IContent? FindBlogsSiteRoot()
    {
        var pr = _contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == "platformRoot");
        if (pr is null) { return null; }
        return _contentService.GetPagedChildren(pr.Id, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot"
                && string.Equals(c.GetValue<string>("brandKey"), "blogs", StringComparison.OrdinalIgnoreCase));
    }

    private static string TagsJson(string[] tags)
        => "[" + string.Join(",", tags.Select(t => $"\"{Esc(t)}\"")) + "]";

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
