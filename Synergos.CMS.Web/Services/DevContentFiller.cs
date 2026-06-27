using Synergos.CMS.Interfaces;
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
    private readonly ICommentRepository _comments;
    private readonly ILogger<DevContentFiller> _logger;

    private Guid _sectionKey, _heroKey, _splitKey, _featureGridKey, _featureKey, _missionKey, _ctaKey, _buttonKey;
    private string? _blogAuthorUdi;   // UDI del autor sembrado; los posts lo referencian en authorRef
    private int _blogSeedPostId;      // node id del post de arquitectura (para sembrar comentarios)
    private Guid _testimonialsKey, _testimonialItemKey, _faqListKey, _faqItemKey;
    private Guid _statKey, _threeColKey, _logoCloudKey, _logoItemKey;

    public DevContentFiller(
        IContentService contentService,
        IContentTypeService contentTypeService,
        SchemaBlockDefaults defaults,
        DevMediaFactory media,
        ICommentRepository comments,
        ILogger<DevContentFiller> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _defaults = defaults;
        _media = media;
        _comments = comments;
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
        SeedShop(details);
        SeedHealthcarePages(details);

        // OLA 4.6 (ADR 0102): verticales #7 Educación (scholar) + #8 Booking (meridian).
        // Núcleos de negocio nuevos, 100% composables (siteRoot + páginas como nodos de
        // contenido cuyo body es un Layout Composer). Booking = registro enterprise de
        // reservas/citas: precios vía IPriceFormatter (Servicios reusa la infra de Shop)
        // → precios sin hardcode.
        SeedEducacion(details);
        SeedBooking(details);

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
        // El home real vive en siteRoot.sections → este nodo "Home" es redundante.
        // Antes lo re-publicábamos oculto del menú, pero seguía vivo + indexado.
        // Además sus sections viejas tenían mediaOnRight="True", que reventaba el
        // reindex Examine (incluso al despublicar y en un rebuild del índice interno,
        // que sí indexa drafts). Limpiamos las sections ("[]") y despublicamos. No se
        // borra: queda draft recuperable, ya sin contenido tóxico.
        home.SetValue(SectionsAlias, string.Empty, Culture);   // BlockGrid vacío = "" (NO "[]", que rompe LayoutPresetDefaults)
        _contentService.Save(home);
        var unpub = _contentService.Unpublish(home, "*");
        details.Add(unpub.Success ? "Home:cleared+unpublished" : $"Home:unpublish-failed:{unpub.Result}");
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

        AddStats(b,
            (ComponentCount, "componentes listos para usar"),
            (VerticalCount, "verticales de negocio"),
            ("1", "plataforma, multi-dominio"));

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

        AddSplit(b, "El mismo motor, tu marca",
            "Tu identidad, de punta a punta",
            "<p>Colores, tipografía, logo y tono se aplican a todo el sitio desde un solo lugar. Cada negocio luce 100% propio — el motor es el mismo, la marca es tuya.</p>",
            "Synergos Branding", "Personalización de marca en SynergosLabs", "#0F58A7", "#7A3FF2",
            mediaOnRight: true, ctaLabel: "Qué es SynergosLabs", ctaUrl: "/synergos/identidad");

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

        AddStats(b,
            (VerticalCount, "verticales en producción"),
            (ComponentCount, "componentes reutilizados"),
            ("1", "plataforma para todo el grupo"));

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
        AddCard("Healthcare", "<p>Historia clínica, agenda y recetas — un vertical clínico completo sobre el mismo motor.</p>",
            "syn-launcher__card--grid", "syn-launcher__card--live", "Entrar a Healthcare →", "/healthcare");
        // OLA 4.6 — cards composables de los verticales #7/#8 (mismo patrón, sin hardcode).
        AddCard("Educación", "<p>Cursos, catálogo y inscripciones — una academia completa sobre el mismo motor.</p>",
            "syn-launcher__card--document", "syn-launcher__card--live", "Entrar a Educación →", "/educacion");
        AddCard("Booking", "<p>Reservas y citas: catálogo de servicios, calendario y registro multipaso — una plataforma de reservas completa sobre el mismo motor.</p>",
            "syn-launcher__card--grid", "syn-launcher__card--live", "Entrar a Booking →", "/booking");

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

        // El platformRoot TAMBIÉN compone compBranding (mandatory). Si quedó vacío no
        // puede re-publicar y CADA uSync import falla con
        // "Invalid Properties: brandKey, brandDisplayName". Lo sembramos defensivo.
        SeedPlatformBranding(pr, details);

        SeedVertical(pr.Id, "Blogs", "blogs", "SynergosLabs Blogs", "silverGold", "blogs.synergos.local",
            BuildBlogsHome(), details);

        SeedVertical(pr.Id, "Tienda", "ecommerce", "SynergosLabs Tienda", "dark", "tienda.synergos.local",
            BuildTiendaHome(), details);

        // ADR 0098 H0.5 — Healthcare es un VERTICAL completo (siteRoot propio,
        // identidad clínica clara). Landing pública + intake; la app clínica
        // (patients/agenda/recetas) montará <synergos-healthcare> en páginas gated (H4).
        SeedVertical(pr.Id, "Healthcare", "healthcare", "SynergosLabs Healthcare", "light", "healthcare.synergos.local",
            BuildHealthcareHome(), details);

        // OLA 4.6 — vertical #7 Educación: identidad académica "Scholar" (light, marfil/teal/gold).
        SeedVertical(pr.Id, "Educacion", "educacion", "SynergosLabs Educación", "scholar", "educacion.synergos.local",
            BuildEducacionHome(), details);

        // OLA 4.6 — vertical #8 Booking: registro enterprise de reservas/citas con
        // identidad propia "Meridian" (el tema lo define otro agente; acá solo se referencia).
        SeedVertical(pr.Id, "Booking", "meridian", "SynergosLabs Booking", "meridian", "booking.synergos.local",
            BuildBookingHome(), details);
    }

    private void SeedVertical(int parentId, string name, string brandKey, string brandDisplayName,
        string themeVariant, string hostname, string sectionsJson, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(parentId, 0, 100, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot" && Matches(c, name));
        var site = existing ?? _contentService.Create(name, parentId, "siteRoot");
        site.SetCultureName(name, Culture);
        SetIdentityField(site, "siteDisplayName", name);              // mandatory (Culture)
        SetIdentityField(site, "brandKey", brandKey);                // mandatory (Nothing), ^[a-z][a-z0-9-]*$
        SetIdentityField(site, "brandDisplayName", brandDisplayName); // mandatory (Culture)
        site.SetValue("pageThemeVariant", $"[\"{themeVariant}\"]");   // FlexibleDropdown → JSON array (trampa conocida)
        if (site.HasProperty("canonicalHostname")) { site.SetValue("canonicalHostname", hostname); }
        site.SetValue(SectionsAlias, sectionsJson, Culture);
        var save = _contentService.SaveAndPublish(site, new[] { Culture });
        details.Add(save.Success ? $"Vertical:{name}:ok({themeVariant})" : $"Vertical:{name}:failed:{save.Result}");
    }

    // Siembra el branding del platformRoot SOLO si está vacío (no pisa lo que el
    // arquitecto haya puesto). Sin esto, el platformRoot no re-publica.
    private void SeedPlatformBranding(IContent pr, List<string> details)
    {
        if (!string.IsNullOrWhiteSpace(pr.GetValue<string>("brandKey"))) { return; }
        SetIdentityField(pr, "brandKey", "default");
        SetIdentityField(pr, "brandDisplayName", "SynergosLabs");
        var save = _contentService.SaveAndPublish(pr, new[] { Culture });
        details.Add(save.Success ? "PlatformRoot:branding-seeded" : $"PlatformRoot:branding-failed:{save.Result}");
    }

    // Setea un campo de identidad respetando la variación REAL de la propiedad
    // (Culture vs Nothing) — blinda contra desajustes XML↔DB que producen
    // "Invalid Properties" al publicar (brandKey=Nothing, brandDisplayName=Culture).
    private static void SetIdentityField(IContent node, string alias, string value)
    {
        if (!node.HasProperty(alias)) { return; }
        var variesByCulture = node.Properties[alias]?.PropertyType
            .Variations.HasFlag(Umbraco.Cms.Core.Models.ContentVariation.Culture) ?? false;
        if (variesByCulture) { node.SetValue(alias, value, Culture); }
        else { node.SetValue(alias, value); }
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
            _blogSeedPostId = SeedPost(ingenieria, "Arquitectura por capas: el grafo de dependencias",
                "Interfaces ← Application ← Web ← Tests. Cómo un grafo unidireccional mantiene el motor mantenible a escala.",
                "2026-06-20", "7", new[] { "ingeniería", "arquitectura" }, gFrom, gTo,
                BuildRichArticle(
                    "Dependencias en una sola dirección",
                    "<p>Interfaces ← Application ← Web ← Tests. La capa de aplicación no conoce Umbraco ni ASP.NET; eso la hace probable y portable.</p>"
                    + "<blockquote class=\"syn-pull-quote\">El secreto no es agregar capas, sino que las flechas apunten siempre en la misma dirección.<cite class=\"syn-pull-quote__cite\">Principio de diseño SynergosLabs</cite></blockquote>"
                    + "<aside class=\"syn-callout syn-callout--info\"><span class=\"syn-callout__icon\" aria-hidden=\"true\">ℹ️</span><div class=\"syn-callout__body\"><p class=\"syn-callout__title\">Regla de oro</p><p>Si una capa necesita conocer a la de arriba, la abstracción está mal puesta: invertí la dependencia con una interfaz (seam).</p></div></aside>"
                    + "<p>En la práctica, el contrato vive en <code>Interfaces</code> y la implementación concreta en <code>Web</code>:</p>"
                    + "<pre class=\"syn-code-block\"><span class=\"syn-code-block__lang\">csharp</span><code>public interface IBlogQuery\n{\n    IReadOnlyList&lt;PostSummary&gt; GetByTag(string tag, int maxItems);\n}</code></pre>",
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

        // Hilo de comentarios de demo (anidado + likes) sobre el post de arquitectura.
        SeedComments(_blogSeedPostId, details);

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

    private int SeedPost(int categoryId, string title, string excerpt, string publishDate,
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
        return save.Success ? post.Id : 0;
    }

    // Siembra un hilo de comentarios de demo (anidado a 2 niveles + likes,
    // ADR 0100) sobre un post. Idempotente: no-op si el nodo ya tiene
    // comentarios. El repository persiste un JSON por nodo bajo App_Data.
    private void SeedComments(int nodeId, List<string> details)
    {
        if (nodeId <= 0) { return; }
        if (_comments.GetApprovedForNode(nodeId).Count > 0)
        {
            details.Add($"Blog:comments:{nodeId}:exists");
            return;
        }
        try
        {
            var parent = _comments.AddAsync(
                new NewComment(nodeId, MemberKey: null, AuthorName: "Mariana Ríos",
                    Body: "Excelente explicación del grafo de dependencias. ¿Cómo manejan los DTOs entre Application y Web?"),
                CancellationToken.None).GetAwaiter().GetResult();

            var parentGuid = Guid.ParseExact(parent.Id, "N");
            _comments.AddAsync(
                new NewComment(nodeId, null, "Equipo SynergosLabs",
                    Body: "Buena pregunta. Los DTOs viven en Application y Web sólo los proyecta — nunca al revés. Así la capa de aplicación sigue sin conocer ASP.NET.",
                    ParentId: parentGuid),
                CancellationToken.None).GetAwaiter().GetResult();

            _comments.AddAsync(
                new NewComment(nodeId, null, "Diego Torres",
                    Body: "Me sirvió muchísimo para refactorizar un proyecto legacy. Gracias."),
                CancellationToken.None).GetAwaiter().GetResult();

            // Un par de likes en el comentario top-level para mostrar la reacción.
            _comments.LikeAsync(nodeId, parent.Id, CancellationToken.None).GetAwaiter().GetResult();
            _comments.LikeAsync(nodeId, parent.Id, CancellationToken.None).GetAwaiter().GetResult();

            details.Add($"Blog:comments:{nodeId}:seeded(3+2likes)");
        }
        catch (Exception ex)
        {
            details.Add($"Blog:comments:{nodeId}:failed:{ex.GetType().Name}");
        }
    }

    // Cuerpo de artículo componible: sección de intro + split con imagen + CTA.
    // NO incluye hero: el template PostPage ya renderiza heroImage + título + meta.
    // NO incluye el hilo de comentarios: PostPage lo renderiza fijo al pie
    // (ADR 0100) — dropearlo aquí lo duplicaría.
    private string BuildArticle(string h1, string b1, string h2, string b2,
        string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddMission(b, h1, "", b1);
        AddSplit(b, h2, "", b2, mediaName, h2, hexFrom, hexTo, mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        AddCta(b, "¿Querés ver SynergosLabs en acción?",
            "Una demo de 30 minutos, sin compromiso.", "Hablar con ventas", "/synergos/contacto");
        return b.Build();
    }

    // Cuerpo de artículo RICO: intro con pull-quote + callout + code block
    // embebidos en el RTE (clases .syn-pull-quote / .syn-callout / .syn-code-block,
    // estilizadas en syn-blog.css), + split + CTA. Para que la demo no sea plana.
    // Los h2 del cuerpo alimentan el TOC auto-construido (syn-reading.js).
    private string BuildRichArticle(string h1, string introHtml, string h2, string b2,
        string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddMission(b, h1, "", introHtml);
        AddSplit(b, h2, "", b2, mediaName, h2, hexFrom, hexTo, mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        AddCta(b, "¿Querés ver SynergosLabs en acción?",
            "Una demo de 30 minutos, sin compromiso.", "Hablar con ventas", "/synergos/contacto");
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
        // Testimonios vía componente Angular CDN (hidrata desde la CDN).
        AddSynTestimonials(b, "Lo que dicen nuestros lectores", new (string quote, string author, string role)[]
        {
            ("Los artículos van al grano y se nota la experiencia real.", "Andrés Gómez", "Lector"),
            ("Mi fuente para entender arquitectura de plataformas.", "Valentina Díaz", "Suscriptora"),
            ("Cada post me ahorra horas de investigación.", "Felipe Castro", "Lector"),
        });
        AddCta(b, "¿Listo para publicar tu historia?",
            "Lanzá tu blog sobre el mismo motor y empezá a crecer tu audiencia.",
            "Hablar con ventas", "/synergos/contacto");
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

    // ───────────────── Tienda (catálogo componible, ADR e-commerce) ─────────────────
    // La infra de shop ya existe (productCategoryPage/productPage + IShopQuery +
    // bloques elementShop*). Acá sembramos CONTENIDO: categorías + productos bajo
    // el siteRoot Tienda, y el grid featureado en su home. Idempotente por nombre.
    private void SeedShop(List<string> details)
    {
        if (_contentTypeService.Get("productCategoryPage")?.Key is null
            || _contentTypeService.Get("productPage")?.Key is null)
        {
            details.Add("Shop:schema-not-imported");
            return;
        }
        var tienda = FindVertical("ecommerce");
        if (tienda is null) { details.Add("Shop:tienda-not-found"); return; }

        const string dFrom = "#020817", dTo = "#4f6ef7";   // gradiente oscuro (identidad Tienda)

        var ropa = SeedProductCategory(tienda.Id, "Ropa", "Prendas con la identidad de tu marca.", details);
        var accesorios = SeedProductCategory(tienda.Id, "Accesorios", "Complementos para tu colección.", details);

        if (ropa > 0)
        {
            SeedProduct(ropa, "TSHIRT-NEGRA-001", "Camiseta esencial negra", "89000", new[] { "ropa", "camiseta" },
                dFrom, dTo, BuildProductBody("<p>Algodón premium de 180g, corte regular. El básico que combina con todo y dura lavada tras lavada.</p>", "Producto Camiseta", dFrom, dTo), details);
            SeedProduct(ropa, "HOODIE-GRIS-001", "Hoodie gris premium", "189000", new[] { "ropa", "hoodie" },
                dFrom, dTo, BuildProductBody("<p>Felpa pesada con interior perchado y capucha forrada. Abriga sin sacrificar estilo.</p>", "Producto Hoodie", dFrom, dTo), details);
            SeedProduct(ropa, "GORRA-001", "Gorra clásica", "59000", new[] { "ropa", "gorra" },
                dFrom, dTo, BuildProductBody("<p>Ajustable, bordado de marca al frente. Para todos los días.</p>", "Producto Gorra", dFrom, dTo), details);
        }
        if (accesorios > 0)
        {
            SeedProduct(accesorios, "TOTE-001", "Tote bag de lona", "49000", new[] { "accesorios", "bolso" },
                dFrom, dTo, BuildProductBody("<p>Lona resistente con asas reforzadas. Lleva todo con estilo y de forma sostenible.</p>", "Producto Tote", dFrom, dTo), details);
            SeedProduct(accesorios, "MUG-001", "Taza cerámica", "39000", new[] { "accesorios", "taza" },
                dFrom, dTo, BuildProductBody("<p>Cerámica de 350ml, apta para microondas y lavavajillas. Tu café, tu marca.</p>", "Producto Taza", dFrom, dTo), details);
            SeedProduct(accesorios, "STICKER-PACK-001", "Pack de stickers", "19000", new[] { "accesorios", "stickers" },
                dFrom, dTo, BuildProductBody("<p>10 stickers vinílicos resistentes al agua. Personalizá tu laptop, agenda o botella.</p>", "Producto Stickers", dFrom, dTo), details);
        }

        details.Add($"Shop:seeded(ropa={ropa > 0},acc={accesorios > 0})");
    }

    private int SeedProductCategory(int parentId, string name, string description, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(parentId, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "productCategoryPage" && Matches(c, name));
        var cat = existing ?? _contentService.Create(name, parentId, "productCategoryPage");
        cat.SetCultureName(name, Culture);
        cat.SetValue("categoryName", name, Culture);                  // mandatory (Culture)
        if (cat.HasProperty("categoryDescription")) { cat.SetValue("categoryDescription", description, Culture); }
        if (cat.HasProperty("seoTitle")) { cat.SetValue("seoTitle", $"{name} — Tienda {BrandName}", Culture); }
        if (cat.HasProperty("seoDescription")) { cat.SetValue("seoDescription", description, Culture); }
        var save = _contentService.SaveAndPublish(cat, new[] { Culture });
        details.Add(save.Success ? $"Shop:cat:{name}:ok" : $"Shop:cat:{name}:failed:{save.Result}");
        return save.Success ? cat.Id : 0;
    }

    private void SeedProduct(int categoryId, string sku, string name, string priceBase, string[] tags,
        string hexFrom, string hexTo, string sectionsJson, List<string> details)
    {
        var existing = _contentService.GetPagedChildren(categoryId, 0, 500, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "productPage" && Matches(c, name));
        var p = existing ?? _contentService.Create(name, categoryId, "productPage");
        p.SetCultureName(name, Culture);
        p.SetValue("productSku", sku);                               // Nothing, mandatory
        p.SetValue("productName", name, Culture);                    // Culture, mandatory
        p.SetValue("productPriceBase", priceBase);                   // Nothing, mandatory (numérico)
        if (p.HasProperty("productInStock")) { p.SetValue("productInStock", true); }
        if (p.HasProperty("productImages"))
        {
            p.SetValue("productImages", _media.GetOrCreatePickerValue($"Producto {name}", name, hexFrom, hexTo, 800, 800), Culture);
        }
        if (p.HasProperty("tags")) { p.SetValue("tags", TagsJson(tags), Culture); }
        p.SetValue(SectionsAlias, sectionsJson, Culture);
        if (p.HasProperty("seoTitle")) { p.SetValue("seoTitle", $"{name} — {BrandName}", Culture); }
        var save = _contentService.SaveAndPublish(p, new[] { Culture });
        var label = name.Length > 24 ? name[..24] : name;
        details.Add(save.Success ? $"Shop:product:{label}:ok" : $"Shop:product:{label}:failed:{save.Result}");
    }

    // Cuerpo componible de la página de producto (la ficha precio/imágenes la
    // renderiza ProductPage.cshtml; esto es la descripción + detalles + CTA).
    private string BuildProductBody(string descHtml, string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddMission(b, "Descripción", "", descHtml);
        AddSplit(b, "Detalles", "", "<p>Materiales de calidad, pensados para durar. Envío a todo el país y devolución sin vueltas.</p>",
            mediaName, "Detalle del producto", hexFrom, hexTo, mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        AddCta(b, "¿Listo para comprar?", "Agregá al carrito y completá tu compra en minutos.", "Ver la tienda", "/tienda");
        return b.Build();
    }

    // Home del vertical Tienda: hero + grid de productos (consulta IShopQuery en runtime).
    private string BuildTiendaHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Tu tienda, sobre el mismo motor", "Catálogo, carrito y checkout",
            "<p>Vende online con producto, variantes, carrito y consultas server-side sobre el mismo núcleo — sin re-plataformar cuando crezcas.</p>",
            "Synergos Tienda Hero", "Hero del vertical Tienda", "#020817", "#4f6ef7",
            ("Ver el catálogo", "/tienda/ropa"), ("Hablar con ventas", "/synergos/contacto"));
        AddProductGrid(b);
        // Componentes CDN-Angular (hidratan <synergos-*> desde la CDN): testimonios + FAQ interactivo.
        AddSynTestimonials(b, "Lo que dicen nuestros clientes", new (string quote, string author, string role)[]
        {
            ("Calidad impecable y envío rápido. Repito sin dudar.", "Mariana López", "Cliente verificada"),
            ("El proceso de compra fue de dos clics. Facilísimo.", "Julián Torres", "Cliente verificado"),
            ("Los productos son tal cual las fotos. Recomendado.", "Carolina Ruiz", "Cliente verificada"),
        });
        AddSynFaq(b, "Preguntas frecuentes", new (string question, string answer)[]
        {
            ("¿Hacen envíos a todo el país?", "Sí, enviamos a toda Colombia con seguimiento incluido."),
            ("¿Puedo devolver un producto?", "Tenés 30 días para devoluciones, sin preguntas."),
            ("¿Qué medios de pago aceptan?", "Tarjetas, PSE y pago contra entrega en ciudades principales."),
        });
        AddCta(b, "¿Listo para vender online?",
            "Lanzá tu tienda esta semana.",
            "Ver el catálogo", "/tienda/ropa");
        return b.Build();
    }

    // Siembra un bloque CDN-Angular (elementSyn* → <synergos-*> hidratado desde la CDN).
    // Para demos: preferir estos sobre los Razor nativos donde aporten (ver memoria
    // feedback_prefer_cdn_angular_components). Sale no-op si el schema no está importado.
    private void AddSynBlock(BlockGridJsonBuilder b, string alias, Action<BlockGridJsonBuilder.BlockBuilder> configure)
    {
        var key = _contentTypeService.Get(alias)?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c =>
        {
            configure(c);
            c.ApplyDefaults(_defaults.DefaultsFor(key.Value));
        });
    }

    private void AddProductGrid(BlockGridJsonBuilder b)
    {
        var key = _contentTypeService.Get("elementShopProductGrid")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c   // defaults: todas las categorías, 12 items, grid
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    private IContent? FindVertical(string brandKey)
    {
        var pr = _contentService.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == "platformRoot");
        if (pr is null) { return null; }
        return _contentService.GetPagedChildren(pr.Id, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "siteRoot"
                && string.Equals(c.GetValue<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));
    }

    // ─────────── Componentes CDN-Angular reutilizables (OLA 4.6) ───────────
    // Mismo patrón que AddSynTestimonials/AddSynFaq: cada uno emite un bloque syn que
    // el SynHost hidrata como <synergos-*> desde la CDN. Config end-to-end desde el CMS.
    // Sale no-op si el ElementType aún no está importado (el llamador degrada con grace).

    /// <summary>Acordeón interactivo (elementSynAccordion): items {title, content}. Reusable para temario/lecciones, horarios, FAQ.</summary>
    private void AddSynAccordion(BlockGridJsonBuilder b, (string title, string content)[] items, bool allowMultiple = false)
    {
        var key = _contentTypeService.Get("elementSynAccordion")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var itemsJson = "[" + string.Join(",", items.Select(i =>
            $"{{\"title\":\"{Esc(i.title)}\",\"content\":\"{Esc(i.content)}\"}}")) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("itemsJson", itemsJson)
            .Set("allowMultiple", allowMultiple)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Carrusel (elementSynCarousel): slides {imageUrl, alt, caption}. Reusable para platos/cursos destacados.</summary>
    private void AddSynCarousel(BlockGridJsonBuilder b, (string name, string caption, string from, string to)[] slides)
    {
        var key = _contentTypeService.Get("elementSynCarousel")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var slidesJson = "[" + string.Join(",", slides.Select(s =>
        {
            var url = _media.GetOrCreateMediaUrl(s.name, s.caption, s.from, s.to, 1200, 720);
            return $"{{\"imageUrl\":\"{Esc(url)}\",\"alt\":\"{Esc(s.caption)}\",\"caption\":\"{Esc(s.caption)}\",\"linkUrl\":\"\"}}";
        })) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("slidesJson", slidesJson)
            .Set("autoplayInterval", "5000")
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Galería con lightbox (elementSynLightboxGallery): images {thumbUrl, fullUrl, alt, caption}. Reusable para mostrar espacios/servicios de Booking.</summary>
    private void AddSynGallery(BlockGridJsonBuilder b, int columns, (string name, string caption, string from, string to)[] images)
    {
        var key = _contentTypeService.Get("elementSynLightboxGallery")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var imagesJson = "[" + string.Join(",", images.Select(im =>
        {
            var url = _media.GetOrCreateMediaUrl(im.name, im.caption, im.from, im.to, 1000, 800);
            return $"{{\"thumbUrl\":\"{Esc(url)}\",\"fullUrl\":\"{Esc(url)}\",\"alt\":\"{Esc(im.caption)}\",\"caption\":\"{Esc(im.caption)}\"}}";
        })) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("imagesJson", imagesJson)
            .Set("columns", columns.ToString())
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Buscador en vivo (elementSynSearchBox): apunta a un endpoint GET. Reusable para filtrar el catálogo de cursos.</summary>
    private void AddSynSearchBox(BlockGridJsonBuilder b, string placeholder, string endpoint, string paramName)
    {
        var key = _contentTypeService.Get("elementSynSearchBox")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("searchPlaceholder", placeholder)
            .Set("searchEndpoint", endpoint)
            .Set("searchParamName", paramName)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Grilla de datos filtrable/paginada (elementSynDataGrid): columns {field,label,sortable,filterable} + dataSource GET. Catálogo de cursos.</summary>
    private void AddSynDataGrid(BlockGridJsonBuilder b, string dataSource, (string field, string label, bool sortable, bool filterable)[] columns, int pageSize = 9)
    {
        var key = _contentTypeService.Get("elementSynDataGrid")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var columnsJson = "[" + string.Join(",", columns.Select(col =>
            $"{{\"field\":\"{Esc(col.field)}\",\"label\":\"{Esc(col.label)}\",\"sortable\":{(col.sortable ? "true" : "false")},\"filterable\":{(col.filterable ? "true" : "false")}}}")) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("dataSource", dataSource)
            .Set("columnsJson", columnsJson)
            .Set("pageSize", pageSize.ToString())
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>
    /// Formulario multi-paso (elementSynFormStepper): steps {title, fields[{name,label,type,required,placeholder}]}.
    /// Reusable para inscripción a curso y reservas. POST al endpoint de Forms (honeypot + rate-limit + email).
    /// </summary>
    private void AddSynFormStepper(BlockGridJsonBuilder b, string submitEndpoint,
        (string title, (string label, string name, string type, bool required, string placeholder, string[] options)[] fields)[] steps)
    {
        var key = _contentTypeService.Get("elementSynFormStepper")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        var stepsJson = "[" + string.Join(",", steps.Select(s =>
        {
            var fields = "[" + string.Join(",", s.fields.Select(f =>
            {
                var optionsJson = f.options is { Length: > 0 }
                    ? ",\"options\":[" + string.Join(",", f.options.Select(o => $"\"{Esc(o)}\"")) + "]"
                    : "";
                return $"{{\"name\":\"{Esc(f.name)}\",\"label\":\"{Esc(f.label)}\",\"type\":\"{Esc(f.type)}\",\"required\":{(f.required ? "true" : "false")},\"placeholder\":\"{Esc(f.placeholder)}\"{optionsJson}}}";
            })) + "]";
            return $"{{\"title\":\"{Esc(s.title)}\",\"fields\":{fields}}}";
        })) + "]";
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("stepsJson", stepsJson)
            .Set("submitEndpoint", submitEndpoint)
            .Set("allowSkip", false)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Cuenta regresiva (elementSynCountdownDigital): ISO datetime. Reusable para "próxima apertura de inscripciones".</summary>
    private void AddSynCountdown(BlockGridJsonBuilder b, string endDateTimeIso)
    {
        var key = _contentTypeService.Get("elementSynCountdownDigital")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("endDateTime", endDateTimeIso)
            .Set("showLabels", true)
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>
    /// Calendario month-view (elementSynCalendar): eventsEndpoint (GET JSON con slots/eventos) +
    /// initialMonth (YYYY-MM, opcional → mes actual). Reusable para elegir fecha/slot de la reserva.
    /// </summary>
    private void AddSynCalendar(BlockGridJsonBuilder b, string eventsEndpoint, string? initialMonth = null)
    {
        var key = _contentTypeService.Get("elementSynCalendar")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c =>
        {
            c.Set("eventsEndpoint", eventsEndpoint);           // mandatory (URL ^https?://|^/)
            if (!string.IsNullOrWhiteSpace(initialMonth)) { c.Set("initialMonth", initialMonth); }
            c.ApplyDefaults(_defaults.DefaultsFor(key.Value));
        });
    }

    // ─────────── Componentes SSR-only (no hidratan; render Razor nativo) ───────────

    /// <summary>Login de miembro (elementMemberLogin, SSR). CTA "inscribirse" para curso gated. redirectLink opcional.</summary>
    private void AddMemberLogin(BlockGridJsonBuilder b, string redirectUrl)
    {
        var key = _contentTypeService.Get("elementMemberLogin")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c =>
        {
            c.Set("loginEndpoint", "/account/login");   // AccountController (ADR 0034)
            if (!string.IsNullOrWhiteSpace(redirectUrl)) { c.Set("redirectLink", LinkJson("Mi cuenta", redirectUrl)); }
            c.ApplyDefaults(_defaults.DefaultsFor(key.Value));
        });
    }

    /// <summary>Mapa embebido (elementCorpMapEmbed, SSR): iframe sandbox. Reusable para la ubicación (ej. página Reservar de Booking).</summary>
    private void AddMapEmbed(BlockGridJsonBuilder b, string mapUrl, string mapTitle, int height = 420)
    {
        var key = _contentTypeService.Get("elementCorpMapEmbed")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("mapUrl", mapUrl)
            .Set("mapTitle", mapTitle)             // mandatory
            .Set("mapHeight", height.ToString())
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    /// <summary>Grid de productos del Shop filtrado por categoría (precios vía IPriceFormatter, scoped al siteRoot). Reusable para el catálogo de servicios de Booking.</summary>
    private void AddProductGridFiltered(BlockGridJsonBuilder b, string categoryName, string sortBy = "name", int maxItems = 24)
    {
        var key = _contentTypeService.Get("elementShopProductGrid")?.Key;
        if (key is null) { return; }
        var section = b.AddTopLevelBlock(_sectionKey);
        section.ApplyDefaults(_defaults.DefaultsFor(_sectionKey));
        section.AddChild(SectionContentAreaKey, key.Value, c => c
            .Set("categoryFilter", categoryName)
            .Set("sortBy", sortBy)
            .Set("maxItems", maxItems.ToString())
            .ApplyDefaults(_defaults.DefaultsFor(key.Value)));
    }

    // Páginas públicas internas del vertical Healthcare (Servicios, Equipo) bajo su
    // siteRoot, componibles. Heredan la identidad clínica del siteRoot.
    private void SeedHealthcarePages(List<string> details)
    {
        var hc = FindVertical("healthcare");
        if (hc is null) { details.Add("HealthcarePages:healthcare-not-found"); return; }
        SeedSiteRootPage(hc.Id, "Servicios", "Servicios clínicos", BuildHealthcareServicios(), details);
        SeedSiteRootPage(hc.Id, "Equipo", "Nuestro equipo", BuildHealthcareEquipo(), details);
    }

    // Crea/actualiza una pageBase bajo un siteRoot ESPECÍFICO (vs Apply, que usa la
    // Entidad). Mismo patrón: heading + showTitle=false + seo + sections.
    private int SeedSiteRootPage(int parentId, string name, string heading, string sectionsJson, List<string> details)
    {
        var page = _contentService.GetPagedChildren(parentId, 0, 200, out _)
            .FirstOrDefault(c => c.ContentType.Alias == "pageBase" && Matches(c, name));
        page ??= _contentService.Create(name, parentId, "pageBase");
        page.SetCultureName(name, Culture);
        if (page.HasProperty("heading")) { page.SetValue("heading", heading, Culture); }
        if (page.HasProperty("showTitle")) { page.SetValue("showTitle", false); }
        if (page.HasProperty("seoTitle")) { page.SetValue("seoTitle", $"{name} — Healthcare {BrandName}", Culture); }
        page.SetValue(SectionsAlias, sectionsJson, Culture);
        var save = _contentService.SaveAndPublish(page, new[] { Culture });
        details.Add(save.Success ? $"HealthcarePage:{name}:ok" : $"HealthcarePage:{name}:failed:{save.Result}");
        return save.Success ? page.Id : 0;
    }

    private string BuildHealthcareServicios()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Servicios clínicos", "Atención integral, registro impecable",
            "<p>Desde la consulta hasta el seguimiento, todo queda registrado y accesible para tu equipo — con la privacidad del paciente protegida.</p>",
            "Healthcare Servicios Hero", "Servicios de Healthcare", "#0B3B3C", "#1FA2A6",
            ("Pedir una cita", "/healthcare"), ("Hablar con ventas", "/synergos/contacto"));
        FeatureGridAuto(b, "Lo que ofrecemos", "Cobertura completa de la práctica",
            new (string title, string subtitle, string body)[]
            {
                ("Consulta médica", "Presencial o virtual", "Agenda, atiende y registra la consulta con historia clínica versionada."),
                ("Controles y seguimiento", "Continuidad del cuidado", "Citas de control con acceso al historial completo del paciente."),
                ("Recetas", "Registro formal", "Emisión y consulta de recetas, vinculadas al paciente."),
                ("Consentimiento informado", "Transparencia", "El paciente controla quién accede a su información clínica."),
            }, 2);
        AddMission(b, "Cada servicio, registrado de punta a punta", "",
            "<p>Consulta, control, receta o consentimiento: todo queda en la historia clínica versionada del paciente, con acceso auditado. El sistema registra; el profesional decide y firma cada acto clínico.</p>");
        AddCta(b, "¿Listo para empezar?", "Pedí una cita o agendá una demo para tu equipo.", "Pedir una cita", "/healthcare");
        return b.Build();
    }

    private string BuildHealthcareEquipo()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Nuestro equipo", "Profesionales a cargo de tu salud",
            "<p>Un equipo comprometido con el cuidado y la privacidad. Conocé a los profesionales detrás de la práctica.</p>",
            "Healthcare Equipo Hero", "Equipo de Healthcare", "#0B3B3C", "#1FA2A6",
            ("Pedir una cita", "/healthcare"), ("Hablar con ventas", "/synergos/contacto"));
        FeatureGridAuto(b, "El equipo", "Profesionales licenciados",
            new (string title, string subtitle, string body)[]
            {
                ("Dra. Laura Méndez", "Medicina general", "Más de 10 años en atención primaria y prevención."),
                ("Dr. Andrés Villa", "Cardiología", "Especialista en salud cardiovascular y controles de seguimiento."),
                ("Dra. Sofía Cardona", "Pediatría", "Cuidado integral de niños y adolescentes."),
            }, 3);
        AddMission(b, "Compromiso con tu privacidad", "",
            "<p>Tu información clínica está cifrada y solo accesible por el personal autorizado con tu consentimiento. Este sistema registra; el profesional decide.</p>");
        AddCta(b, "Agendá tu cita", "Reservá con el especialista que necesitás.", "Pedir una cita", "/healthcare");
        return b.Build();
    }

    // ADR 0098 H0.5 — home pública del vertical Healthcare: landing + servicios +
    // disclaimer (RECORD-KEEPER) + intake de cita. Todo componible.
    private string BuildHealthcareHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Tu consultorio, en orden",
            "Historia clínica, agenda y recetas en un solo lugar",
            "<p>SynergosLabs Healthcare reúne la historia de tus pacientes, la agenda de citas y las recetas — cifrado y con acceso auditado. Pedí una cita abajo o conocé la plataforma.</p>",
            "Synergos Healthcare Hero", "Hero del vertical Healthcare", "#0B3B3C", "#1FA2A6",
            ("Hablar con ventas", "/synergos/contacto"), ("Ver planes", "/synergos/precios"));

        AddMission(b, "Aviso importante", "",
            "<p>Este sistema registra información médica pero NO brinda consejo clínico. Un profesional de la salud licenciado es responsable de todo diagnóstico y decisión.</p>");

        FeatureGridAuto(b, "Lo que incluye", "Todo para la práctica clínica",
            new (string title, string subtitle, string body)[]
            {
                ("Historia clínica", "Versionada y cifrada", "Registro de pacientes con historial inmutable y acceso auditado."),
                ("Agenda de citas", "Sin sobrecupos", "Reserva con control anti-overbooking por doctor."),
                ("Recetas", "Registro formal", "Emisión y consulta de recetas — el sistema registra, el profesional decide."),
                ("Consentimiento", "Paciente → doctor", "Libro de consentimientos que gobierna el acceso a la información clínica."),
            }, 2);

        // Componentes CDN-Angular (hidratan <synergos-*> desde la CDN): testimonios + FAQ clínico.
        AddSynTestimonials(b, "Lo que dicen las clínicas", new (string quote, string author, string role)[]
        {
            ("Pasamos de carpetas a historia clínica versionada en una semana.", "Dra. Patricia Niño", "Directora médica"),
            ("La agenda sin sobrecupos nos ordenó el día a día.", "Dr. Camilo Rojas", "Medicina general"),
            ("El acceso auditado nos dio tranquilidad con los datos.", "Lucía Fernández", "Administradora"),
        });
        AddSynFaq(b, "Preguntas frecuentes", new (string question, string answer)[]
        {
            ("¿Cómo agendo una cita?", "Llená el formulario de abajo o llamanos; te confirmamos en minutos."),
            ("¿Mis datos están seguros?", "Sí: la información clínica va cifrada y con acceso auditado por consentimiento."),
            ("¿Atienden urgencias?", "Para urgencias acudí al servicio de emergencias; acá agendamos consultas y controles."),
        });

        AddMission(b, "Cuidado y privacidad, de la mano", "",
            "<p>Acompañamos a tu equipo en la transición: migración de historias, capacitación y soporte. La información clínica viaja cifrada y con acceso auditado por consentimiento — desde el primer día.</p>");

        AddContactForm(b, "Pedir una cita", "Solicitar cita",
            ("Nombre", "nombre", "text", true, "Tu nombre"),
            ("Email", "email", "email", true, "tu@correo.com"),
            ("Teléfono", "telefono", "tel", false, "Tu teléfono (opcional)"),
            ("Motivo de consulta", "motivo", "textarea", true, "Contanos brevemente el motivo…"));

        AddCta(b, "¿Tu equipo quiere conocer la plataforma?",
            "Agendá una demo y te mostramos la práctica completa.", "Hablar con ventas", "/synergos/contacto");
        return b.Build();
    }

    // ═══════════════════════ Vertical #7 — Educación (scholar) ═══════════════════════
    // Núcleo de negocio nuevo, 100% composable. El siteRoot "Educacion" (theme scholar)
    // se crea en SeedVerticalSiteRoots con BuildEducacionHome() como body. Acá sembramos
    // las dos páginas clave (Cursos + un Curso detalle) como nodos pageBase bajo el siteRoot.
    // Cero contenido baked en .cshtml: todo es Layout Composer (bloques nativos + elementSyn* CDN).
    private const string ScholarFrom = "#0E7C7B", ScholarTo = "#D9A441";   // teal→gold (identidad scholar)

    private void SeedEducacion(List<string> details)
    {
        var edu = FindVertical("educacion");
        if (edu is null) { details.Add("Educacion:siteroot-not-found"); return; }
        SeedSiteRootPage(edu.Id, "Cursos", "Catálogo de cursos", BuildEducacionCursos(), details);
        SeedSiteRootPage(edu.Id, "Curso", "Fundamentos de plataformas componibles", BuildEducacionCursoDetalle(), details);
        details.Add("Educacion:pages-ok");
    }

    // Home Educación: hero + cursos destacados (feature-grid/cards) + value props + planes (pricing) + CTA.
    private string BuildEducacionHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Aprende lo que el mercado pide",
            "Cursos prácticos con certificación, a tu ritmo",
            "<p>Una academia online sobre el mismo motor: catálogo, lecciones, instructores e inscripciones. Aprende habilidades reales con proyectos guiados y obtén tu certificado.</p>",
            "Educacion Home Hero", "Hero de la academia Educación", ScholarFrom, ScholarTo,
            ("Ver cursos", "/educacion/cursos"), ("Ver planes", "/educacion#planes"));

        // Cursos destacados — feature-grid (CDN si está, SSR si no).
        FeatureGridAuto(b, "Cursos destacados", "Los más elegidos por nuestra comunidad", new (string title, string subtitle, string body)[]
        {
            ("Fundamentos de plataformas componibles", "12 lecciones · 8 h", "Aprende a pensar en componentes, temas y verticales sobre un mismo núcleo."),
            ("Diseño de sistemas con tokens", "10 lecciones · 6 h", "Construye una línea de diseño coherente con grilla de 8, tipografía y paleta por marca."),
            ("Arquitectura limpia en la práctica", "14 lecciones · 9 h", "Grafo de dependencias, seams y pruebas: la base que tu equipo va a respetar."),
        }, 3);

        // Value props — por qué estudiar acá.
        FeatureGridAuto(b, "Por qué aprender con nosotros", "Aprendizaje que se nota", new (string title, string subtitle, string body)[]
        {
            ("Proyectos reales", "Aprendes haciendo", "Cada curso termina en un proyecto que puedes mostrar en tu portafolio."),
            ("Instructores expertos", "De la industria", "Profesionales que construyen producto, no solo teoría de aula."),
            ("Certificación", "Avala tu progreso", "Obtén un certificado verificable al completar cada ruta de aprendizaje."),
        }, 3);

        AddSynTestimonials(b, "Lo que dicen nuestros estudiantes", new (string quote, string author, string role)[]
        {
            ("Pasé de teoría suelta a construir un proyecto completo en seis semanas.", "Camila Restrepo", "Estudiante · Frontend"),
            ("Los instructores responden dudas reales, no genéricas. Se nota la experiencia.", "Sebastián Mora", "Estudiante · Backend"),
            ("El certificado me sirvió para subir de rol en mi empresa.", "Daniela Quintero", "Egresada"),
        });

        // Planes — pricing composable (precio = DataType, editable; SSR si la tabla no está).
        AddEducacionPlanes(b);

        FaqAuto(b, "Preguntas frecuentes", new (string question, string answer)[]
        {
            ("¿Necesito conocimientos previos?", "Cada curso indica su nivel. Hay rutas desde cero y rutas avanzadas para perfiles con experiencia."),
            ("¿Los cursos tienen certificado?", "Sí. Al completar un curso obtienes un certificado verificable que puedes compartir."),
            ("¿Puedo estudiar a mi ritmo?", "Sí. El contenido queda disponible y avanzas cuando puedas; algunas rutas tienen cohortes en vivo."),
        });

        AddCta(b, "Empieza a aprender hoy",
            "Explora el catálogo y arranca tu primer curso esta semana.",
            "Ver cursos", "/educacion/cursos");
        return b.Build();
    }

    // Planes de la academia — pricing table composable (placeholders editables, precio = DataType).
    private void AddEducacionPlanes(BlockGridJsonBuilder b)
    {
        if (_contentTypeService.Get("elementPricingTable")?.Key is not null)
        {
            AddPricingTable(b, "Planes de acceso", "Elige cómo quieres aprender",
                ("Curso suelto", "$89.000", "/curso", "",
                 "Acceso de por vida a 1 curso\nProyecto guiado\nCertificado del curso\nSoporte de la comunidad",
                 false, "Comprar curso", "/educacion/cursos"),
                ("Membresía", "$49.900", "/mes", "El más elegido",
                 "Acceso a TODO el catálogo\nRutas de aprendizaje\nCertificados ilimitados\nCohortes en vivo\nSoporte prioritario",
                 true, "Suscribirme", "/educacion/curso"),
                ("Equipos", "A tu medida", "", "",
                 "Todo lo de Membresía\nPaneles de progreso\nFacturación por equipo\nRutas a medida\nAcompañamiento dedicado",
                 false, "Hablar con ventas", "/synergos/contacto"));
        }
        else
        {
            FeatureGridAuto(b, "Planes de acceso", "Elige cómo quieres aprender", new (string title, string subtitle, string body)[]
            {
                ("Curso suelto", "$89.000", "Acceso de por vida a un curso, con proyecto guiado y certificado."),
                ("Membresía", "$49.900/mes", "El más elegido: todo el catálogo, rutas, certificados y cohortes en vivo."),
                ("Equipos", "A tu medida", "Para empresas: paneles de progreso, facturación por equipo y rutas a medida."),
            }, 3);
        }
    }

    // Página Cursos: catálogo grid filtrable. Usa data-grid CDN (filtra/pagina contra un
    // endpoint) + search-box; si no están importados, cae a feature-grid de cursos.
    private string BuildEducacionCursos()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Catálogo de cursos",
            "Filtra por tema y encuentra tu próxima habilidad",
            "<p>Explora todos los cursos disponibles. Busca por tema, nivel o duración y empieza cuando quieras.</p>",
            "Educacion Cursos Hero", "Hero del catálogo de cursos", ScholarFrom, ScholarTo,
            ("Ver planes", "/educacion#planes"), ("Hablar con nosotros", "/synergos/contacto"));

        // Buscador en vivo (CDN). Apunta al endpoint de búsqueda del CMS (Examine, ADR de search).
        AddSynSearchBox(b, "Buscar cursos por tema o nivel…", "/api/search", "q");

        // Catálogo filtrable/paginable (CDN data-grid). dataSource = endpoint GET JSON.
        // Si el data-grid no está importado, fallback a un feature-grid del catálogo.
        if (_contentTypeService.Get("elementSynDataGrid")?.Key is not null)
        {
            AddSynDataGrid(b, "/api/search?type=curso", new (string field, string label, bool sortable, bool filterable)[]
            {
                ("title", "Curso", true, true),
                ("level", "Nivel", true, true),
                ("duration", "Duración", true, false),
                ("category", "Categoría", true, true),
            }, pageSize: 9);
        }
        else
        {
            FeatureGridAuto(b, "Todos los cursos", "Explora el catálogo completo", new (string title, string subtitle, string body)[]
            {
                ("Fundamentos de plataformas componibles", "Principiante · 8 h", "Piensa en componentes, temas y verticales sobre un mismo núcleo."),
                ("Diseño de sistemas con tokens", "Intermedio · 6 h", "Línea de diseño coherente: grilla de 8, tipografía y paleta por marca."),
                ("Arquitectura limpia en la práctica", "Avanzado · 9 h", "Grafo de dependencias, seams y pruebas aplicadas."),
                ("Render server-side y SEO técnico", "Intermedio · 5 h", "Velocidad, sitemap y datos estructurados desde el primer día."),
                ("Componentes Angular sobre la CDN", "Avanzado · 7 h", "Islas de interactividad publicadas a una CDN, framework-agnóstico."),
                ("Identidad de marca por sitio", "Intermedio · 4 h", "Una marca, mil caras: temas y tokens por propiedad."),
            }, 3);
        }

        AddCta(b, "¿No sabes por dónde empezar?",
            "Cuéntanos tu objetivo y te armamos una ruta de aprendizaje a tu medida.",
            "Hablar con nosotros", "/synergos/contacto");
        return b.Build();
    }

    // Página Curso detalle: temario/lecciones (accordion) + instructor + CTA inscribirse
    // (member-login para el contenido gated + form-stepper de inscripción).
    private string BuildEducacionCursoDetalle()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Fundamentos de plataformas componibles",
            "12 lecciones · 8 horas · Nivel principiante · Certificado",
            "<p>Aprende a construir y operar un producto digital sobre un mismo núcleo componible: componentes, temas, verticales e identidad por marca. Termina con un proyecto real para tu portafolio.</p>",
            "Educacion Curso Detalle Hero", "Hero del curso Fundamentos", ScholarFrom, ScholarTo,
            ("Inscribirme", "/educacion/curso#inscripcion"), ("Ver el temario", "/educacion/curso#temario"));

        AddMission(b, "Lo que vas a lograr", "",
            "<p>Al terminar serás capaz de componer páginas completas sin tocar código, aplicar una línea de diseño coherente con tokens y entender cómo un solo motor sirve marca, tienda y membresía.</p>");

        // Temario / lecciones — acordeón CDN interactivo (title + content).
        AddSynAccordion(b, new (string title, string content)[]
        {
            ("Módulo 1 · El núcleo componible", "Qué es un motor componible, cómo se piensa en componentes y por qué un solo core puede ser muchos productos."),
            ("Módulo 2 · Componer, no programar", "El editor visual, los bloques nativos y los componentes de la CDN. Armas una página completa de cero."),
            ("Módulo 3 · Línea de diseño con tokens", "Grilla de 8, tipografía, paleta por marca y ritmo vertical. Tu sitio se ve premium por defecto."),
            ("Módulo 4 · Identidad por marca", "Temas por propiedad, data-theme y branding. Una marca, mil caras sobre el mismo deploy."),
            ("Módulo 5 · Proyecto final", "Construyes y publicas un vertical completo con su identidad. Lo agregas a tu portafolio."),
        }, allowMultiple: false);

        // Instructor — media-text split (CDN si está, SSR si no).
        SplitAuto(b, "Tu instructor",
            "Quien te acompaña",
            "<p><strong>Andrés Gómez</strong> — Ingeniero de plataforma con 12 años construyendo productos digitales. Ha liderado equipos que migraron decenas de sitios a un solo motor componible y enseña con proyectos reales, no diapositivas.</p>",
            "Educacion Instructor", "Foto del instructor del curso", ScholarFrom, ScholarTo,
            mediaOnRight: true, ctaLabel: null, ctaUrl: null);

        // CTA inscribirse — para contenido gated: member-login (SSR) + form-stepper (CDN) de inscripción.
        AddMission(b, "Inscríbete al curso", "",
            "<p>Crea tu cuenta o inicia sesión para acceder a las lecciones y al proyecto guiado. Si tu empresa cubre tu formación, completa la inscripción y te enviamos la factura.</p>");
        AddMemberLogin(b, "/educacion/curso");
        AddSynFormStepper(b, "/api/forms/inscripcion-curso/submit", new (string title, (string label, string name, string type, bool required, string placeholder, string[] options)[] fields)[]
        {
            ("Tus datos", new (string, string, string, bool, string, string[])[]
            {
                ("Nombre completo", "nombre", "text", true, "Tu nombre", Array.Empty<string>()),
                ("Email", "email", "email", true, "tu@correo.com", Array.Empty<string>()),
            }),
            ("Tu objetivo", new (string, string, string, bool, string, string[])[]
            {
                ("¿Qué quieres lograr con el curso?", "objetivo", "textarea", true, "Cuéntanos brevemente…", Array.Empty<string>()),
                ("¿Tu empresa cubre la formación?", "empresa", "select", false, "", new[] { "Sí", "No", "No estoy seguro" }),
            }),
        });

        FaqAuto(b, "Antes de inscribirte", new (string question, string answer)[]
        {
            ("¿Cuánto dura el acceso?", "El acceso al curso es de por vida; puedes repasarlo cuando quieras."),
            ("¿Recibo certificado?", "Sí, al completar el proyecto final obtienes un certificado verificable."),
            ("¿Hay devolución?", "Tienes 14 días de garantía: si el curso no es para ti, te devolvemos tu dinero."),
        });

        AddCta(b, "¿Listo para empezar?",
            "Inscríbete hoy y construye tu primer proyecto esta semana.",
            "Ver más cursos", "/educacion/cursos");
        return b.Build();
    }

    // ═══════════════════════ Vertical #8 — Booking (meridian) ═══════════════════════
    // Núcleo de negocio nuevo, 100% composable: plataforma de reservas/citas, registro
    // ENTERPRISE. El siteRoot "Booking" (theme meridian, definido por otro agente — acá solo
    // se referencia el LITERAL) se crea en SeedVerticalSiteRoots con BuildBookingHome() como
    // body. El catálogo de SERVICIOS reservables reusa la infra de Shop (productCategoryPage +
    // productPage) → los precios se renderizan vía IPriceFormatter (es-CO), CERO precio
    // hardcodeado. DefaultShopQuery scopea por siteRoot, así que los servicios no se cruzan
    // con la Tienda. Páginas: Servicios + Reservar. CERO contenido baked en .cshtml.
    private const string MeridianFrom = "#0A2540", MeridianTo = "#1FA2A6";   // gradiente neutro on-brand (NO define el tema; el tema lo pinta data-theme="meridian")

    private void SeedBooking(List<string> details)
    {
        var booking = FindVertical("meridian");
        if (booking is null) { details.Add("Booking:siteroot-not-found"); return; }

        // El catálogo de servicios reservables son productCategoryPage + productPage bajo el
        // siteRoot de Booking: precio = productPriceBase (numérico), formateado por
        // IPriceFormatter. Sin hardcode. Categorías = tipos de recurso/servicio.
        SeedServiceCategoriesAndItems(booking.Id, details);

        SeedSiteRootPage(booking.Id, "Servicios", "Catálogo de servicios", BuildBookingServicios(), details);
        SeedSiteRootPage(booking.Id, "Reservar", "Reserva tu cita", BuildBookingReservar(), details);
        details.Add("Booking:pages-ok");
    }

    // Siembra el catálogo de servicios reservables como Shop scoped al siteRoot de Booking.
    // Cada servicio es un productPage con productPriceBase NUMÉRICO (lo formatea IPriceFormatter
    // en el render — es-CO). Categorías = áreas de servicio. CERO precio hardcodeado.
    private void SeedServiceCategoriesAndItems(int bookingId, List<string> details)
    {
        if (_contentTypeService.Get("productCategoryPage")?.Key is null
            || _contentTypeService.Get("productPage")?.Key is null)
        {
            details.Add("Booking:shop-schema-not-imported(services-fallback-cards)");
            return;
        }

        var consultoria = SeedProductCategory(bookingId, "Consultoría", "Sesiones con especialistas.", details);
        var espacios    = SeedProductCategory(bookingId, "Espacios", "Salas y recursos reservables.", details);
        var bienestar   = SeedProductCategory(bookingId, "Bienestar", "Servicios de bienestar y cuidado.", details);

        if (consultoria > 0)
        {
            SeedProduct(consultoria, "SVC-ASESORIA-30", "Asesoría express · 30 min", "90000", new[] { "consultoría", "30min" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Sesión 1:1 de 30 minutos con un especialista para resolver una duda puntual. Confirmación inmediata y enlace de videollamada.</p>", "Booking Asesoria Express", MeridianFrom, MeridianTo), details);
            SeedProduct(consultoria, "SVC-ASESORIA-60", "Consultoría estratégica · 60 min", "160000", new[] { "consultoría", "60min" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Una hora de consultoría a profundidad con diagnóstico y plan de acción. Ideal para decisiones de negocio.</p>", "Booking Consultoria Estrategica", MeridianFrom, MeridianTo), details);
        }
        if (espacios > 0)
        {
            SeedProduct(espacios, "SPC-SALA-REUNION", "Sala de reuniones · 2 h", "120000", new[] { "espacio", "sala" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Sala equipada para hasta 8 personas, con pantalla y conexión. Reserva por bloques de dos horas.</p>", "Booking Sala Reuniones", MeridianFrom, MeridianTo), details);
            SeedProduct(espacios, "SPC-AUDITORIO", "Auditorio · jornada", "480000", new[] { "espacio", "evento" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Auditorio para hasta 60 asistentes con sonido y proyección. Disponible por media jornada o jornada completa.</p>", "Booking Auditorio", MeridianFrom, MeridianTo), details);
        }
        if (bienestar > 0)
        {
            SeedProduct(bienestar, "WEL-MASAJE-60", "Masaje terapéutico · 60 min", "140000", new[] { "bienestar", "60min" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Sesión de masaje terapéutico de una hora con profesional certificado. Agenda tu horario preferido.</p>", "Booking Masaje", MeridianFrom, MeridianTo), details);
            SeedProduct(bienestar, "WEL-NUTRICION", "Plan nutricional · valoración", "110000", new[] { "bienestar", "nutrición" },
                MeridianFrom, MeridianTo, BuildServiceBody("<p>Valoración nutricional inicial con plan personalizado y seguimiento. Cupos limitados por día.</p>", "Booking Nutricion", MeridianFrom, MeridianTo), details);
        }

        details.Add($"Booking:services-seeded(con={consultoria > 0},esp={espacios > 0},bie={bienestar > 0})");
    }

    // Cuerpo composable de la ficha de servicio (la ficha precio/imagen la renderiza ProductPage;
    // esto es descripción + detalles + CTA reservar, sin precio hardcodeado).
    private string BuildServiceBody(string descHtml, string mediaName, string hexFrom, string hexTo)
    {
        var b = new BlockGridJsonBuilder();
        AddMission(b, "El servicio", "", descHtml);
        AddSplit(b, "Cómo funciona", "", "<p>Elige tu servicio, selecciona fecha y hora disponibles en el calendario y completa tus datos. Recibes la confirmación por correo al instante.</p>",
            mediaName, "Detalle del servicio", hexFrom, hexTo, mediaOnRight: true, ctaLabel: null, ctaUrl: null);
        AddCta(b, "¿Listo para reservar?", "Elige fecha y hora y asegura tu cupo en minutos.", "Reservar ahora", "/booking/reservar");
        return b.Build();
    }

    // Home Booking: hero (propuesta + CTA "Reservar") + servicios/cómo-funciona (feature-grid)
    // + value props + planes (pricing) + testimonios + FAQ + CTA.
    private string BuildBookingHome()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Reservas y citas, sin fricción",
            "Tu agenda en línea, disponible 24/7",
            "<p>Una plataforma de reservas enterprise sobre el mismo motor: catálogo de servicios, calendario en tiempo real y confirmación automática. Tus clientes reservan en minutos; tu equipo gestiona en un solo lugar.</p>",
            "Booking Home Hero", "Hero de la plataforma de reservas Booking", MeridianFrom, MeridianTo,
            ("Reservar", "/booking/reservar"), ("Ver servicios", "/booking/servicios"));

        // Cómo funciona — feature-grid (CDN si está, SSR si no).
        FeatureGridAuto(b, "Cómo funciona", "Reservar es cuestión de minutos", new (string title, string subtitle, string body)[]
        {
            ("1 · Elige tu servicio", "Catálogo claro", "Explora servicios y recursos reservables con duración y precio por adelantado."),
            ("2 · Selecciona fecha y hora", "Disponibilidad real", "El calendario muestra los cupos libres en tiempo real; eliges el que te sirve."),
            ("3 · Confirma y listo", "Sin llamadas", "Completas tus datos y recibes la confirmación por correo al instante."),
        }, 3);

        // Value props — por qué reservar acá.
        FeatureGridAuto(b, "Por qué reservar con nosotros", "Una experiencia que se nota", new (string title, string subtitle, string body)[]
        {
            ("Disponibilidad 24/7", "Reserva cuando quieras", "Tu agenda abierta a toda hora, sin depender de horarios de atención."),
            ("Confirmación al instante", "Cero esperas", "Recibes tu confirmación y recordatorios por correo automáticamente."),
            ("Gestión centralizada", "Todo en un lugar", "Servicios, calendario y reservas en una sola plataforma para tu equipo."),
        }, 3);

        // Planes — pricing composable (precio = DataType, editable; SSR si la tabla no está).
        AddBookingPlanes(b);

        AddSynTestimonials(b, "Lo que dicen nuestros clientes", new (string quote, string author, string role)[]
        {
            ("Pasamos de agendar por WhatsApp a un calendario que se gestiona solo.", "Valentina Ríos", "Gerente de operaciones"),
            ("La confirmación automática nos eliminó los no-shows casi por completo.", "Andrés Patiño", "Director de servicio"),
            ("Montamos el catálogo de salas en una tarde. Reservar es de dos clics.", "Lucía Gómez", "Coordinadora de espacios"),
        });

        FaqAuto(b, "Preguntas frecuentes", new (string question, string answer)[]
        {
            ("¿Necesito crear una cuenta para reservar?", "No es obligatorio: puedes reservar como invitado. Crear cuenta te deja gestionar tus reservas."),
            ("¿Puedo cancelar o reprogramar?", "Sí. Desde el enlace de confirmación puedes cambiar o cancelar tu cita según las reglas del servicio."),
            ("¿Cómo se cobran los servicios?", "Cada servicio muestra su precio en pesos colombianos (es-CO). El pago se coordina según la configuración del negocio."),
        });

        AddCta(b, "Empieza a recibir reservas hoy",
            "Explora el catálogo de servicios y haz tu primera reserva esta semana.",
            "Ver servicios", "/booking/servicios");
        return b.Build();
    }

    // Planes de la plataforma de reservas — pricing table composable (placeholders editables,
    // precio = DataType). SSR fallback si la tabla no está importada.
    private void AddBookingPlanes(BlockGridJsonBuilder b)
    {
        if (_contentTypeService.Get("elementPricingTable")?.Key is not null)
        {
            AddPricingTable(b, "Planes para tu negocio", "Del primer servicio a toda una agenda",
                ("Inicial", "Gratis", "", "",
                 "1 servicio reservable\nCalendario básico\nConfirmación por correo\nSoporte por comunidad",
                 false, "Empezar gratis", "/synergos/contacto"),
                ("Profesional", "$149.900", "/mes", "El más elegido",
                 "Servicios ilimitados\nMúltiples recursos\nRecordatorios automáticos\nGestión de cancelaciones\nReportes de ocupación\nSoporte prioritario",
                 true, "Elegir Profesional", "/synergos/contacto"),
                ("Enterprise", "A tu medida", "", "",
                 "Todo lo de Profesional\nMulti-sede\nIntegraciones a medida\nSLA garantizado\nAcompañamiento dedicado",
                 false, "Hablar con ventas", "/synergos/contacto"));
        }
        else
        {
            FeatureGridAuto(b, "Planes para tu negocio", "Del primer servicio a toda una agenda", new (string title, string subtitle, string body)[]
            {
                ("Inicial", "Gratis", "Para empezar: un servicio reservable, calendario básico y confirmación por correo."),
                ("Profesional", "$149.900/mes", "El más elegido: servicios y recursos ilimitados, recordatorios y reportes de ocupación."),
                ("Enterprise", "A tu medida", "Para escalar: multi-sede, integraciones a medida, SLA y acompañamiento dedicado."),
            }, 3);
        }
    }

    // Página Servicios: catálogo de servicios/recursos reservables. search-box + data-grid
    // filtrable (categorías/duración/precio vía IPriceFormatter es-CO) → fallback feature-grid
    // si el data-grid no resuelve. Si Shop está importado, también muestra el precio real
    // por categoría (elementShopProductGrid scoped, IPriceFormatter) — CERO precio hardcodeado.
    private string BuildBookingServicios()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Catálogo de servicios",
            "Encuentra el servicio o recurso que necesitas reservar",
            "<p>Explora todos los servicios y recursos disponibles. Filtra por categoría, duración o precio y reserva el que se ajuste a ti.</p>",
            "Booking Servicios Hero", "Hero del catálogo de servicios", MeridianFrom, MeridianTo,
            ("Reservar", "/booking/reservar"), ("Hablar con nosotros", "/synergos/contacto"));

        // Buscador en vivo (CDN). Apunta al endpoint de búsqueda del CMS (Examine).
        AddSynSearchBox(b, "Buscar servicios por nombre o categoría…", "/api/search", "q");

        // Catálogo filtrable/paginable (CDN data-grid). dataSource = endpoint GET JSON.
        // Columnas incluyen precio (lo formatea el render es-CO). Fallback a feature-grid.
        if (_contentTypeService.Get("elementSynDataGrid")?.Key is not null)
        {
            AddSynDataGrid(b, "/api/search?type=servicio", new (string field, string label, bool sortable, bool filterable)[]
            {
                ("title", "Servicio", true, true),
                ("category", "Categoría", true, true),
                ("duration", "Duración", true, false),
                ("price", "Precio", true, false),
            }, pageSize: 9);
        }
        else
        {
            FeatureGridAuto(b, "Todos los servicios", "Explora el catálogo completo", new (string title, string subtitle, string body)[]
            {
                ("Asesoría express", "Consultoría · 30 min", "Sesión 1:1 puntual con un especialista, confirmación inmediata."),
                ("Consultoría estratégica", "Consultoría · 60 min", "Diagnóstico a profundidad con plan de acción."),
                ("Sala de reuniones", "Espacios · 2 h", "Sala equipada para hasta 8 personas, por bloques de dos horas."),
                ("Auditorio", "Espacios · jornada", "Hasta 60 asistentes, media jornada o jornada completa."),
                ("Masaje terapéutico", "Bienestar · 60 min", "Sesión de una hora con profesional certificado."),
                ("Plan nutricional", "Bienestar · valoración", "Valoración inicial con plan personalizado y seguimiento."),
            }, 3);
        }

        // Si Shop está importado, además mostramos el precio REAL por categoría (es-CO).
        if (_contentTypeService.Get("elementShopProductGrid")?.Key is not null)
        {
            AddMission(b, "Consultoría", "", "<p>Sesiones con especialistas.</p>");
            AddProductGridFiltered(b, "Consultoría", sortBy: "price-asc");
            AddMission(b, "Espacios", "", "<p>Salas y recursos reservables.</p>");
            AddProductGridFiltered(b, "Espacios", sortBy: "price-asc");
            AddMission(b, "Bienestar", "", "<p>Servicios de bienestar y cuidado.</p>");
            AddProductGridFiltered(b, "Bienestar", sortBy: "price-asc");
        }

        AddCta(b, "¿No encuentras lo que buscas?",
            "Cuéntanos qué necesitas reservar y configuramos el servicio a tu medida.",
            "Hablar con nosotros", "/synergos/contacto");
        return b.Build();
    }

    // Página Reservar: calendar (elegir fecha/slot) + form-stepper multipaso (datos de la
    // reserva) + confirmación + (opcional) map-embed/horarios en accordion. Todo CDN con
    // fallback con grace. CERO contenido baked en .cshtml.
    private string BuildBookingReservar()
    {
        var b = new BlockGridJsonBuilder();
        AddHero(b, "Reserva tu cita",
            "Elige fecha y hora, sin llamadas",
            "<p>Selecciona el día y el horario disponibles en el calendario, completa tus datos y recibe la confirmación por correo en minutos.</p>",
            "Booking Reservar Hero", "Hero de la página de reserva", MeridianFrom, MeridianTo,
            ("Ver servicios", "/booking/servicios"), ("Volver al inicio", "/booking"));

        // Paso 1 — calendario: elegir fecha/slot. eventsEndpoint = GET JSON de disponibilidad.
        AddMission(b, "1 · Elige fecha y hora", "",
            "<p>Toca un día disponible en el calendario para ver los horarios libres. La disponibilidad se actualiza en tiempo real.</p>");
        AddSynCalendar(b, "/api/booking/availability");

        // Paso 2 — datos de la reserva: form-stepper multipaso → POST a Forms (honeypot + rate-limit + email).
        AddMission(b, "2 · Completa tus datos", "",
            "<p>Confirma el servicio, la fecha y la hora elegidas y déjanos tus datos de contacto. Te enviamos la confirmación al correo.</p>");
        AddSynFormStepper(b, "/api/forms/reserva-cita/submit", new (string title, (string label, string name, string type, bool required, string placeholder, string[] options)[] fields)[]
        {
            ("Tu reserva", new (string, string, string, bool, string, string[])[]
            {
                ("Servicio", "servicio", "select", true, "", new[] { "Asesoría express", "Consultoría estratégica", "Sala de reuniones", "Auditorio", "Masaje terapéutico", "Plan nutricional" }),
                ("Fecha", "fecha", "text", true, "DD/MM/AAAA", Array.Empty<string>()),
                ("Hora", "hora", "text", true, "Ej. 10:00 AM", Array.Empty<string>()),
            }),
            ("Tus datos", new (string, string, string, bool, string, string[])[]
            {
                ("Nombre", "nombre", "text", true, "Tu nombre", Array.Empty<string>()),
                ("Email", "email", "email", true, "tu@correo.com", Array.Empty<string>()),
                ("Teléfono", "telefono", "tel", true, "Tu teléfono", Array.Empty<string>()),
                ("Nota (opcional)", "nota", "textarea", false, "Indícanos cualquier detalle de tu reserva…", Array.Empty<string>()),
            }),
        });

        // Confirmación — mensaje de qué sigue tras reservar.
        AddMission(b, "3 · Confirmación", "",
            "<p>Al enviar tu reserva recibirás un correo con los detalles y un enlace para reprogramar o cancelar. Te enviaremos un recordatorio antes de tu cita.</p>");

        // Opcional — ubicación (map-embed SSR) + horarios de atención (accordion CDN).
        AddMapEmbed(b, "https://www.openstreetmap.org/export/embed.html?bbox=-74.08%2C4.60%2C-74.05%2C4.63&layer=mapnik",
            "Ubicación", height: 420);

        AddSynAccordion(b, new (string title, string content)[]
        {
            ("Lunes a viernes", "Atención de 8:00 a. m. a 6:00 p. m. · Reservas en línea 24/7."),
            ("Sábados", "Atención de 9:00 a. m. a 1:00 p. m. · Reservas en línea 24/7."),
            ("Domingos y festivos", "Sin atención presencial · Reservas en línea para días hábiles."),
        }, allowMultiple: true);

        FaqAuto(b, "Sobre tu reserva", new (string question, string answer)[]
        {
            ("¿Puedo cambiar o cancelar mi cita?", "Sí, desde el enlace que te enviamos por correo, según las reglas del servicio."),
            ("¿Recibo recordatorios?", "Sí. Te enviamos la confirmación al reservar y un recordatorio antes de tu cita."),
            ("¿Qué pasa si llego tarde?", "Cada servicio define su tolerancia; revisa los detalles en el correo de confirmación."),
        });

        AddCta(b, "¿Aún no eliges servicio?",
            "Revisa el catálogo completo antes de reservar.",
            "Ver servicios", "/booking/servicios");
        return b.Build();
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
