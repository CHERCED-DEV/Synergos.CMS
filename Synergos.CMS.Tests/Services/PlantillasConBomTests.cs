using System.Xml.Linq;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cada plantilla que declara uSync existe en <c>Views/</c> y empieza con BOM (#193, punto 9).
/// </summary>
/// <remarks>
/// <para>Umbraco escribe las plantillas en UTF-8 con BOM. Un arranque que importa uSync guarda cada
/// plantilla, y la que estaba en el repo sin BOM queda reescrita: <c>git status</c> muestra un cambio
/// que nadie hizo, en los humos y en <c>usync-rebuild-check</c>. <c>PostPage.cshtml</c> estuvo así
/// desde agosto.</para>
/// </remarks>
public sealed class PlantillasConBomTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void Cada_plantilla_de_uSync_existe_y_empieza_con_BOM()
    {
        var web = Path.Combine(RepoRoot(), "Synergos.CMS.Web");
        var plantillas = Directory.GetFiles(Path.Combine(web, "uSync", "v9", "Templates"), "*.config")
            .Select(c => XDocument.Load(c).Root!.Attribute("Alias")!.Value)
            .ToArray();
        Assert.NotEmpty(plantillas);

        var fuera = plantillas
            .Select(alias => Path.Combine(web, "Views", $"{alias}.cshtml"))
            .Where(vista => !File.Exists(vista) || !File.ReadAllBytes(vista).AsSpan().StartsWith(Bom))
            .Select(vista => Path.GetRelativePath(web, vista))
            .ToArray();

        Assert.True(fuera.Length == 0,
            "Plantillas sin vista o sin BOM; el próximo import de uSync las reescribe y ensucia el árbol:"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", fuera));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
