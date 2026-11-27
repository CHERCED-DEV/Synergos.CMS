using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las estadías de muestra de Viajes (D-016-C27): cada una trae su portada y dos fotos más, y cada foto existe en
/// <c>wwwroot/media/booking/</c>. Una URL a un fichero que no está sería una imagen rota en la ficha.
/// </summary>
public sealed class GaleriaDeEstadiasTests
{
    private static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("ctg-getsemani")]
    [InlineData("ctg-bocagrande")]
    [InlineData("med-provenza")]
    [InlineData("bog-zonat")]
    [InlineData("eje-salento")]
    [InlineData("eje-filandia")]
    public async Task Cada_estadia_trae_su_portada_y_dos_fotos_que_existen(string estadia)
    {
        var detalle = await new StubStayContentProvider().GetStayAsync(estadia);

        Assert.NotNull(detalle);
        Assert.Equal(3, detalle!.Gallery.Count);
        Assert.Equal($"/media/booking/{estadia}.jpg", detalle.Gallery[0]);
        var web = Path.Combine(Raiz(), "Synergos.CMS.Web", "wwwroot");
        Assert.All(detalle.Gallery, url =>
            Assert.True(File.Exists(Path.Combine(web, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))), url));
    }
}
