using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Tooling dev-only que crea la <b>portada de arranque</b>: un <c>siteRoot</c> publicado
/// cuyo cuerpo es un Layout Composer real, para que un despliegue nuevo deje de servir el
/// cartel «No published content».
/// </summary>
/// <remarks>
/// <para><b>Qué problema cierra</b> (#119). El schema se importa con
/// <c>tools/importar-schema.sh</c> y el estado de las capacidades con
/// <c>tools/provisionar.sh</c>, pero <c>uSync/v9/Content/</c> está vacía y nada crea un nodo:
/// medido contra una base limpia con el árbol de uSync importado entero (896 ítems, 0
/// errores), <c>GET /</c> contesta <b>200 con 1926 bytes</b> y el título
/// «Umbraco: No published content». No falla — por eso hacía falta el corte que
/// <c>humo-publico.sh</c> ya hace.</para>
///
/// <para><b>Por qué una HERRAMIENTA y no un seeder de arranque ni XML escrito a mano.</b> Las
/// dos formas obvias están prohibidas y por razones distintas: sembrar en boot es ADR 0013
/// (y con <c>ContentHandler</c> encendido revertiría en cada arranque lo que un editor
/// publicó), y autorar <c>uSync/v9/Content/</c> desde el repo es ADR 0129 — ese XML lo
/// <b>exporta</b> uSync al guardar, no lo escribe un agente. La forma que respeta las dos es
/// ésta: se siembra en desarrollo, el arquitecto revisa y ajusta en el backoffice, uSync
/// exporta, y el XML entra al repo por su mano. Ver
/// <c>docs/despliegue/00-montar-el-entorno.md</c> §5.bis.2.</para>
///
/// <para><b>Un <c>siteRoot</c> en la raíz del árbol, no bajo un <c>platformRoot</c>.</b> Un
/// deploy es un origen (CLAUDE.md §0.A.8) y el propio <c>platformRoot</c> se describe como
/// «wrapper OPCIONAL cuando un deploy agrupa varios siteRoot». Con un solo raíz, <c>/</c>
/// resuelve al <c>siteRoot</c> y la portada es la portada del sitio — no un lanzador.</para>
///
/// <para><b>Idempotente, y de la forma que importa: no pisa.</b> Correrla dos veces no
/// duplica nada, y si el <c>siteRoot</c> ya tiene cuerpo la herramienta <b>no lo toca</b> y
/// lo dice. Sembrar encima sería peor que duplicar: se llevaría por delante lo que el
/// arquitecto acaba de autorar, en silencio, justo antes de exportarlo.</para>
///
/// <para><b>El cuerpo se compone con piezas del catálogo, no con markup.</b> Dos elementos
/// CDN (<c>elementSynHeroBanner</c>, <c>elementSynFeatureGrid</c>) y uno SSR
/// (<c>elementCorpMissionBlock</c>), todos dentro de <c>elementLayoutSection</c>. Los dos
/// primeros traen fallback SSR (ADR 0012), así que la portada se ve entera <b>sin CDN
/// configurado</b> — que es el estado de un servidor recién montado.</para>
///
/// <para><b>Lo que NO hace, a propósito: media.</b> Ni hero con imagen ni og:image. Un
/// binario sembrado se exporta a <c>uSync/v9/Media/</c> + <c>wwwroot/media/</c> y entra al
/// repo como parte de «la portada de arranque»; una imagen de relleno versionada para
/// siempre es peor que un hueco que el editor llena en treinta segundos.</para>
/// </remarks>
public sealed class StarterPortadaSeeder
{
    private const string Culture = "es-CO";
    private const string SiteRootAlias = "siteRoot";
    private const string SectionsAlias = "sections";

    /// <summary>Nombre del nodo. Es lo que el editor renombra primero, y por eso es genérico.</summary>
    private const string SiteName = "Inicio";

    private const string LayoutSectionAlias = "elementLayoutSection";
    private const string HeroAlias = "elementSynHeroBanner";
    private const string FeatureGridAlias = "elementSynFeatureGrid";
    private const string MissionAlias = "elementCorpMissionBlock";

    /// <summary>
    /// Los content types sin los cuales no hay portada. Público a propósito: un gate los cruza
    /// contra <c>uSync/v9/ContentTypes/</c> sin arrancar nada, y leer la lista de acá en vez de
    /// escribirla aparte es lo que impide que el gate se quede vigilando lo que ya no se usa.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredContentTypes = new[]
    {
        SiteRootAlias, LayoutSectionAlias, HeroAlias, FeatureGridAlias, MissionAlias,
    };

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly SchemaBlockDefaults _defaults;
    private readonly ILogger<StarterPortadaSeeder> _logger;

    public StarterPortadaSeeder(
        IContentService contentService,
        IContentTypeService contentTypeService,
        SchemaBlockDefaults defaults,
        ILogger<StarterPortadaSeeder> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _defaults = defaults;
        _logger = logger;
    }

    /// <summary>
    /// Crea (o completa) la portada de arranque. Idempotente.
    /// </summary>
    public PortadaResult Seed()
    {
        var missing = MissingTypes();
        if (missing.Count > 0)
        {
            // El schema no está importado. Se dice cuál falta en vez de devolver un OK
            // vacío: «no pasó nada» y «no se pudo» se ven igual desde fuera.
            _logger.LogError("Portada: faltan content types {Missing}. Corré el import de uSync primero.",
                string.Join(", ", missing));
            return new PortadaResult(false, 0, PortadaOutcome.MissingContentTypes,
                $"missing-content-types:{string.Join(',', missing)}");
        }

        var raices = _contentService.GetRootContent().ToList();
        var site = raices.FirstOrDefault(
            c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal));

        if (site is not null && HasBody(site))
        {
            _logger.LogInformation("Portada: '{Name}' (id={Id}) ya tiene cuerpo — no se toca.", site.Name, site.Id);
            return new PortadaResult(true, site.Id, PortadaOutcome.AlreadyAuthored, "already-authored");
        }

        if (site is null && raices.Count > 0)
        {
            // La raíz ya está ocupada por otra cosa —típicamente el platformRoot del andamio
            // de la vitrina—. Crear un siteRoot al lado NO pone la portada en `/`: Umbraco
            // resuelve `/` al primer raíz, así que quedaría en /inicio y la herramienta
            // habría contestado «creada» sin que nadie la vea. Es el modo de fallo que este
            // ticket vino a cerrar, así que no se hace: se dice qué hay y no se toca nada.
            var ocupa = raices[0];
            _logger.LogInformation("Portada: la raíz ya la ocupa '{Name}' ({Alias}) — no se siembra.",
                ocupa.Name, ocupa.ContentType.Alias);
            return new PortadaResult(true, ocupa.Id, PortadaOutcome.RootAlreadyTaken,
                $"root-already-taken:{ocupa.ContentType.Alias}:{ocupa.Name}");
        }

        var created = site is null;
        if (site is null)
        {
            site = _contentService.Create(SiteName, Constants.System.Root, SiteRootAlias);
            site.SetCultureName(SiteName, Culture);
        }

        // Obligatorias del schema (siteDisplayName, brandKey, brandDisplayName). Se rellenan
        // SOLO si están vacías: un siteRoot que ya existía puede traer la marca del
        // arquitecto y esto es una siembra, no una reconfiguración.
        FillIfEmpty(site, "siteDisplayName", SiteName, varies: true);
        FillIfEmpty(site, "brandDisplayName", SiteName, varies: true);
        FillIfEmpty(site, "brandKey", "default", varies: false);
        FillIfEmpty(site, "seoTitle", SiteName, varies: true);
        FillIfEmpty(site, "seoDescription",
            "Portada de arranque. Edítala en el backoffice y expórtala con uSync.", varies: true);

        site.SetValue(SectionsAlias, BuildPortada(), Culture);

        var save = _contentService.SaveAndPublish(site, new[] { Culture });
        if (!save.Success)
        {
            var invalid = save.InvalidProperties is null
                ? "(null)"
                : string.Join(",", save.InvalidProperties.Select(p => p.Alias));
            _logger.LogError("Portada: fallo al publicar. {Result}; invalid=[{Invalid}]", save.Result, invalid);
            return new PortadaResult(false, site.Id, PortadaOutcome.SaveFailed,
                $"save-failed:{save.Result}:[{invalid}]");
        }

        _logger.LogInformation("Portada: {Verbo} siteRoot '{Name}' id={Id}.",
            created ? "creado" : "completado", site.Name, site.Id);
        return new PortadaResult(true, site.Id,
            created ? PortadaOutcome.Created : PortadaOutcome.Filled,
            created ? "created" : "filled");
    }

    /// <summary>Los content types que la portada necesita y el schema podría no tener.</summary>
    private List<string> MissingTypes() => RequiredContentTypes
        .Where(alias => _contentTypeService.Get(alias) is null)
        .ToList();

    private static bool HasBody(IContent site)
    {
        var body = site.GetValue<string>(SectionsAlias, Culture);
        // Un Block Grid vacío se guarda como "" o como un JSON sin bloques; las dos cosas
        // son «no hay portada todavía», no «hay una y está vacía».
        return !string.IsNullOrWhiteSpace(body) && body.Contains("\"contentTypeKey\"", StringComparison.Ordinal);
    }

    private static void FillIfEmpty(IContent node, string alias, string value, bool varies)
    {
        if (!node.HasProperty(alias)) { return; }
        var current = varies ? node.GetValue<string>(alias, Culture) : node.GetValue<string>(alias);
        if (!string.IsNullOrWhiteSpace(current)) { return; }
        if (varies) { node.SetValue(alias, value, Culture); } else { node.SetValue(alias, value); }
    }

    /// <summary>
    /// El cuerpo de la portada, en JSON de Block Grid.
    /// </summary>
    /// <remarks>
    /// Público a propósito: es lo único de esta clase que se puede probar sin Umbraco, y lo
    /// que hay que vigilar —que los tres bloques entren en el area correcta y que las
    /// obligatorias del schema vayan rellenas— no se ve desde el resultado del publish.
    /// </remarks>
    public string BuildPortada()
    {
        var b = new BlockGridJsonBuilder();

        var heroSection = b.AddTopLevelBlock(KeyOf(LayoutSectionAlias));
        // El hero es una banda full-bleed autocontenida: pega al header sin aire alrededor.
        // Se setea ANTES de ApplyDefaults, que no pisa lo ya puesto.
        heroSection.Set("spacingTop", "[\"none\"]");
        heroSection.Set("spacingBottom", "[\"none\"]");
        heroSection.Set("spacingInline", "[\"none\"]");
        heroSection.ApplyDefaults(_defaults.DefaultsFor(LayoutSectionAlias));
        heroSection.AddChild(LayoutComposerKeys.SectionContentArea, KeyOf(HeroAlias), hero => hero
            .Set("title", "Tu sitio ya está publicado")
            .Set("subtitle", "Ésta es la portada de arranque: cámbiala por la tuya desde el backoffice.")
            .ApplyDefaults(_defaults.DefaultsFor(HeroAlias)));

        // Sin ctaLabel ni ctaLink en el hero: un botón sin destino es peor que ninguno, y en
        // un árbol recién sembrado no hay todavía una página a la que apuntar.

        var gridSection = b.AddTopLevelBlock(KeyOf(LayoutSectionAlias));
        gridSection.ApplyDefaults(_defaults.DefaultsFor(LayoutSectionAlias));
        gridSection.AddChild(LayoutComposerKeys.SectionContentArea, KeyOf(FeatureGridAlias), grid => grid
            .Set("headingText", "Lo que ya tienes montado")
            .Set("itemsJson", FeaturesJson)
            .Set("columns", "3")
            .ApplyDefaults(_defaults.DefaultsFor(FeatureGridAlias)));

        var missionSection = b.AddTopLevelBlock(KeyOf(LayoutSectionAlias));
        missionSection.ApplyDefaults(_defaults.DefaultsFor(LayoutSectionAlias));
        missionSection.AddChild(LayoutComposerKeys.SectionContentArea, KeyOf(MissionAlias), mission => mission
            .Set("headingTitle", "El siguiente paso")
            .Set("headingSubtitle", "De esta portada a la tuya")
            .Set("textBody",
                "<p>Entra al backoffice y edita esta página: cambia los textos, arrastra bloques "
                + "del Layout Composer, agrega las páginas que necesites.</p>"
                + "<p>Cuando quede como quieres, expórtala con uSync y commitea "
                + "<code>uSync/v9/Content/</code>. El próximo despliegue arrancará con tu portada, "
                + "no con ésta.</p>")
            // mediaAlt es obligatoria en compContentMedia aunque no haya imagen. No se
            // inventa un texto alternativo de algo que no existe: se nombra la sección.
            .Set("mediaAlt", "El siguiente paso")
            .ApplyDefaults(_defaults.DefaultsFor(MissionAlias)));

        return b.Build();
    }

    /// <summary>
    /// Las tres tarjetas de la rejilla. Describen lo que el despliegue ya tiene, sin
    /// afirmar nada que dependa de configuración que quizá no esté puesta.
    /// </summary>
    private const string FeaturesJson =
        "[{\"heading\":\"Editor visual\",\"body\":\"Compón cada página arrastrando bloques del Layout Composer. "
        + "Sin tocar código y sin desplegar.\"},"
        + "{\"heading\":\"Sistema de diseño\",\"body\":\"Tipografía, color y espaciado salen de tokens compartidos: "
        + "lo que cambies una vez se aplica a todo el sitio.\"},"
        + "{\"heading\":\"Contenido versionado\",\"body\":\"Lo que publiques se exporta a uSync y viaja con el "
        + "repositorio al siguiente despliegue.\"}]";

    private Guid KeyOf(string alias) => _contentTypeService.Get(alias)?.Key
        ?? throw new InvalidOperationException($"ContentType '{alias}' no existe — corré el import de uSync.");

    /// <summary>Qué hizo la herramienta. El detalle es para un humano; esto es para un gate.</summary>
    public enum PortadaOutcome
    {
        /// <summary>El schema no está importado.</summary>
        MissingContentTypes,

        /// <summary>No había <c>siteRoot</c>; se creó con su portada.</summary>
        Created,

        /// <summary>Había <c>siteRoot</c> sin cuerpo; se le puso la portada.</summary>
        Filled,

        /// <summary>Ya había portada. No se tocó nada.</summary>
        AlreadyAuthored,

        /// <summary>
        /// La raíz del árbol ya la ocupa otro nodo. No se siembra: un <c>siteRoot</c> al lado
        /// no sale en <c>/</c>, así que «creada» sería mentira.
        /// </summary>
        RootAlreadyTaken,

        /// <summary>Umbraco rechazó el publish.</summary>
        SaveFailed,
    }

    public sealed record PortadaResult(bool Success, int SiteRootId, PortadaOutcome Outcome, string Detail);
}
