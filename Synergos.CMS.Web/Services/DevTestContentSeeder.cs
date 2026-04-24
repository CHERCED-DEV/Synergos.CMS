using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Dev-only seeder que crea un siteRoot "Test Site" con páginas de
/// prueba cubriendo cada familia de bloques, para smoke-testing visual
/// y funcional de los renderers sin tocar el árbol de contenido real.
/// </summary>
/// <remarks>
/// Invocado explícitamente via <c>POST /dev/seed-test-site</c> (solo
/// cuando <c>Synergos:DevSeed:Enabled=true</c>, ADR 0013).
/// Idempotente: si el Test Site ya existe, no duplica.
/// <c>Clear()</c> elimina únicamente el Test Site + descendientes,
/// nunca toca otros siteRoots ni content.
/// </remarks>
public sealed class DevTestContentSeeder
{
    private const string TestSiteName = "Test Site";
    private const string TestSiteAlias = "testSite";

    private readonly IContentService _contentService;
    private readonly IContentTypeService _contentTypeService;
    private readonly ILogger<DevTestContentSeeder> _logger;

    public DevTestContentSeeder(
        IContentService contentService,
        IContentTypeService contentTypeService,
        ILogger<DevTestContentSeeder> logger)
    {
        _contentService = contentService;
        _contentTypeService = contentTypeService;
        _logger = logger;
    }

    public SeedResult Seed()
    {
        var existing = FindTestSite();
        if (existing is not null)
        {
            _logger.LogInformation("DevSeed: Test Site ya existe (id={Id}). Idempotente, skip.", existing.Id);
            return new SeedResult(false, existing.Id, CountChildren(existing.Id), "already-exists");
        }

        var siteRootType = _contentTypeService.Get("siteRoot");
        if (siteRootType is null)
        {
            _logger.LogError("DevSeed: siteRoot content type no encontrado. Corre uSync Import primero.");
            return new SeedResult(false, -1, 0, "siteroot-type-missing");
        }

        var pageBaseType = _contentTypeService.Get("pageBase");
        if (pageBaseType is null)
        {
            _logger.LogError("DevSeed: pageBase content type no encontrado.");
            return new SeedResult(false, -1, 0, "pagebase-type-missing");
        }

        // Crear siteRoot
        var site = _contentService.Create(TestSiteName, Constants.System.Root, "siteRoot");
        site.SetValue("siteDisplayName", "Sitio de Pruebas", "es-CO");
        site.SetCultureName("Test Site", "es-CO");
        site.SetValue("canonicalHostname", "localhost:44391");
        var saveSite = _contentService.SaveAndPublish(site, new[] { "es-CO" });
        if (!saveSite.Success)
        {
            _logger.LogError("DevSeed: fallo al guardar siteRoot. {Result}", saveSite.Result);
            return new SeedResult(false, -1, 0, $"siteroot-save-failed:{saveSite.Result}");
        }
        _logger.LogInformation("DevSeed: siteRoot creado id={Id}", site.Id);

        int pagesCreated = 0;
        foreach (var page in GetTestPages())
        {
            var p = _contentService.Create(page.Name, site.Id, "pageBase");
            p.SetCultureName(page.Name, "es-CO");
            if (!string.IsNullOrWhiteSpace(page.Body))
            {
                p.SetValue("body", page.Body, "es-CO");
            }
            var saveP = _contentService.SaveAndPublish(p, new[] { "es-CO" });
            if (saveP.Success)
            {
                pagesCreated++;
                _logger.LogInformation("DevSeed: pageBase '{Name}' creado id={Id}", page.Name, p.Id);
            }
            else
            {
                _logger.LogWarning("DevSeed: pageBase '{Name}' fallo guardar. {Result}", page.Name, saveP.Result);
            }
        }

        return new SeedResult(true, site.Id, pagesCreated, "ok");
    }

    public ClearResult Clear()
    {
        var existing = FindTestSite();
        if (existing is null)
        {
            return new ClearResult(false, 0, "not-found");
        }

        var countBefore = CountChildren(existing.Id) + 1;
        _contentService.MoveToRecycleBin(existing);
        _contentService.EmptyRecycleBin(-1);
        _logger.LogInformation("DevSeed: Test Site + descendientes eliminados ({Count} nodos).", countBefore);
        return new ClearResult(true, countBefore, "ok");
    }

    private int CountChildren(int parentId) => _contentService.CountChildren(parentId);

    private IContent? FindTestSite()
    {
        var roots = _contentService.GetRootContent();
        return roots.FirstOrDefault(c =>
            c.ContentType.Alias == "siteRoot" &&
            string.Equals(c.Name, TestSiteName, StringComparison.Ordinal));
    }

    private static IEnumerable<TestPage> GetTestPages() => new[]
    {
        new TestPage("Home Test",
            "<p>Página home del Test Site. Renderiza chrome + body richtext + dev-stub CSS.</p><h2>Heading H2 de prueba</h2><p>Párrafo con <a href=\"#\">enlace</a>, <strong>bold</strong> e <em>itálica</em>.</p>"),
        new TestPage("Typography Test",
            "<h1>Heading H1</h1><h2>Heading H2</h2><h3>Heading H3</h3><h4>Heading H4</h4><p>Párrafo body. Lorem ipsum dolor sit amet, consectetur adipiscing elit.</p><ul><li>Item 1</li><li>Item 2</li><li>Item 3</li></ul>"),
        new TestPage("Empty Blocks Test",
            "<p>Esta página tiene body simple. El Layout Composer Sections está vacío — debería renderizar solo el body sin errores.</p>"),
    };

    public sealed record SeedResult(bool Success, int SiteRootId, int ChildCount, string Detail);
    public sealed record ClearResult(bool Success, int NodesDeleted, string Detail);

    private sealed record TestPage(string Name, string Body);
}
