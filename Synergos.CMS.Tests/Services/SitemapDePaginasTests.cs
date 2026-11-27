using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El sitemap lista sólo páginas (D-016-Q7b): un nodo con plantilla entra; uno sin plantilla (configuración, o un dato
/// que pinta una app) no, porque su URL da 404.
/// </summary>
public sealed class SitemapDePaginasTests
{
    [Theory]
    [InlineData(1052, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(null, false)]
    public void Con_plantilla_entra_sin_plantilla_no(int? plantilla, bool entra)
    {
        Assert.Equal(entra, SitemapController.EsPagina(plantilla));
    }
}
