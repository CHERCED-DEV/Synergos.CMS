using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only seeder del ANDAMIO de la vitrina SynergosLabs: platformRoot →
/// siteRoot "Synergos" → 3 páginas <c>pageBase</c> vacías, que
/// <c>DevContentFiller</c> (<c>POST /dev/fill-synergos-pages</c>) puebla después.
/// </summary>
/// <remarks>
/// <para>Invocado explícitamente vía <c>POST /dev/seed-synergos-identity</c>, gated
/// por <c>Synergos:DevSeed:Enabled=true</c> (ADR 0013).</para>
///
/// <para><b>No es la portada de arranque</b>, que vive en
/// <see cref="StarterPortadaSeeder"/> (<c>POST /dev/seed-portada</c>) y es la que
/// nombra <c>docs/despliegue/00-montar-el-entorno.md</c> §5.bis.2. Esto arma el
/// árbol de la vitrina de once verticales, que es otra cosa y mucho más grande.</para>
///
/// <para><b>Llevaba roto y contestando que sí</b> (#119). Dos defectos, y el segundo
/// tapaba al primero:</para>
/// <list type="number">
/// <item>No ponía <c>brandKey</c> ni <c>brandDisplayName</c> en el
/// <c>platformRoot</c> —las dos obligatorias de <c>compBranding</c>— así que el
/// publish se caía con <c>FailedPublishContentInvalid</c> y <b>no se creaba nada</b>.</item>
/// <item>El controller devolvía eso con un <b>200</b> y <c>success:false</c> dentro.
/// Medido contra una base limpia con el schema importado:
/// <c>{"success":false,"detail":"platform-save-failed:FailedPublishContentInvalid"}</c>,
/// HTTP 200. Y no estaba nombrado en ningún documento, así que nadie tenía por qué
/// correrlo y descubrirlo.</item>
/// </list>
///
/// <para><b>Y no era idempotente</b>: cada llamada creaba otro <c>platformRoot</c> y
/// otro <c>siteRoot</c>. Con dos raíces, <c>/</c> deja de resolver a lo que uno cree y
/// <c>DevContentFiller.FindSiteRoot()</c> —que toma el primero— empieza a llenar el
/// árbol equivocado. Hoy busca antes de crear, en los tres niveles.</para>
///
/// <para><b>Las páginas se crean VACÍAS a propósito</b>, y esto antes mentía: había
/// tres métodos <c>Build*Sections</c> componiendo un cuerpo que
/// <c>CreatePage</c> <b>descartaba</b> (<c>_ = sectionsJson;</c>). Un cuerpo escrito
/// que nadie aplica es peor que ninguno: la siguiente auditoría lo lee y da por hecho
/// que las páginas salen pobladas. Se borró la promesa; quien puebla es
/// <c>DevContentFiller</c>, que sí aplica <c>SchemaBlockDefaults</c>.</para>
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

        // 1. platformRoot — se busca antes de crear. Un segundo raíz no da un error:
        //    da dos árboles, y a partir de ahí `/` resuelve a uno y el filler llena el otro.
        var platform = FindRoot(PlatformRootAlias);
        if (platform is null)
        {
            platform = _contentService.Create(PlatformName, Constants.System.Root, PlatformRootAlias);
            platform.SetCultureName(PlatformName, DefaultCulture);
            platform.SetValue("welcomeMessage", "Plataforma editorial Synergos — un código, múltiples productos.", DefaultCulture);
            // Obligatorias de compBranding. Sin estas dos el publish se caía con
            // FailedPublishContentInvalid y el seeder no creaba absolutamente nada (#119).
            platform.SetValue("brandKey", "synergos");
            platform.SetValue("brandDisplayName", PlatformName, DefaultCulture);

            var savePlatform = _contentService.SaveAndPublish(platform, new[] { DefaultCulture });
            if (!savePlatform.Success)
            {
                _logger.LogError("SynergosSeed: fallo creando platformRoot. {Result}", savePlatform.Result);
                return new SeedResult(false, -1, -1, 0, $"platform-save-failed:{savePlatform.Result}");
            }
            _logger.LogInformation("SynergosSeed: platformRoot creado id={Id}", platform.Id);
        }

        // 2. siteRoot
        var site = FindChild(platform.Id, SiteRootAlias, SiteName);
        if (site is null)
        {
            site = _contentService.Create(SiteName, platform.Id, SiteRootAlias);
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
        }

        // 3. Páginas — el ANDAMIO. El cuerpo lo pone DevContentFiller.
        var pagesCreated = 0;
        foreach (var name in new[] { "Home", "Identidad", "Contacto" })
        {
            pagesCreated += CreatePage(site.Id, name) ? 1 : 0;
        }

        return new SeedResult(true, platform.Id, site.Id, pagesCreated, "ok");
    }

    /// <summary>Primer nodo de la raíz del árbol con ese alias.</summary>
    private IContent? FindRoot(string alias) => _contentService
        .GetRootContent()
        .FirstOrDefault(c => string.Equals(c.ContentType.Alias, alias, StringComparison.Ordinal));

    /// <summary>Hijo directo con ese alias y ese nombre.</summary>
    private IContent? FindChild(int parentId, string alias, string name) => _contentService
        .GetPagedChildren(parentId, 0, int.MaxValue, out _)
        .FirstOrDefault(c => string.Equals(c.ContentType.Alias, alias, StringComparison.Ordinal)
                             && string.Equals(c.Name, name, StringComparison.Ordinal));

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

    /// <summary>
    /// Crea la página si no existe. Sin cuerpo: el Block Grid lo pone
    /// <c>DevContentFiller</c>, que es quien aplica <see cref="SchemaBlockDefaults"/> —
    /// sin esos defaults, las props multi-value revientan el editor del backoffice
    /// (<c>JsonReaderException</c> en <c>MultipleValueEditor</c>). Ver ADR 0093.
    /// </summary>
    private bool CreatePage(int parentId, string name)
    {
        try
        {
            if (FindChild(parentId, PageBaseAlias, name) is not null)
            {
                _logger.LogInformation("SynergosSeed: pageBase '{Name}' ya existe. Idempotente, skip.", name);
                return false;
            }

            var page = _contentService.Create(name, parentId, PageBaseAlias);
            page.SetCultureName(name, DefaultCulture);
            page.SetValue("heading", ResolveHeading(name), DefaultCulture);
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

    public sealed record SeedResult(bool Success, int PlatformId, int SiteId, int PagesCreated, string Detail);
    public sealed record ClearResult(bool Success, int NodesDeleted, string Detail);
}
