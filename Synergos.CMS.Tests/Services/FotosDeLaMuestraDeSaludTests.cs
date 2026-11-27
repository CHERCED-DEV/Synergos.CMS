using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Toda foto que emite la muestra de Salud existe en <c>wwwroot</c>. Una URL a un fichero que no está sería una imagen
/// rota en «Equipo» (<c>/healthcare/equipo/</c>), que compone la lista de profesionales; sin foto, se cae a las
/// iniciales.
/// </summary>
public sealed class FotosDeLaMuestraDeSaludTests
{
    private static bool Existe(string url)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        var web = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "wwwroot");
        return File.Exists(Path.Combine(web, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task Cada_foto_de_un_profesional_existe()
    {
        var profesionales = await new SaludDemoDirectorySource().GetAllAsync();

        Assert.NotEmpty(profesionales);
        Assert.All(profesionales.Where(p => !string.IsNullOrWhiteSpace(p.AvatarUrl)),
            p => Assert.True(Existe(p.AvatarUrl!), p.AvatarUrl));
    }

    [Fact]
    public async Task Cada_foto_de_un_paciente_existe()
    {
        var pacientes = await new StubPatientRegistry().SearchAsync(null);

        Assert.NotEmpty(pacientes);
        Assert.All(pacientes.Where(p => !string.IsNullOrWhiteSpace(p.AvatarUrl)),
            p => Assert.True(Existe(p.AvatarUrl!), p.AvatarUrl));
    }
}
