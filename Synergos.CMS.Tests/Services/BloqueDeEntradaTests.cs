using System.Reflection;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El bloque de entrada (<c>elementMemberLogin</c>) sólo acepta rutas locales y envía el campo que espera el
/// controlador de entrada (016, D-016-C21).
/// </summary>
public sealed class BloqueDeEntradaTests
{
    [Theory]
    [InlineData("/account/login", "/account/login")]
    [InlineData("/member/login?x=1", "/member/login?x=1")]
    [InlineData("https://otro.example/login", null)]
    [InlineData("//otro.example/login", null)]
    [InlineData("/\\otro.example", null)]
    [InlineData("account/login", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void El_destino_solo_es_una_ruta_del_sitio(string? configurado, string? esperado)
    {
        Assert.Equal(esperado, BloqueDeEntrada.Destino(configurado));
    }

    [Fact]
    public void El_campo_del_usuario_es_el_que_lee_el_controlador_de_entrada()
    {
        var parametros = typeof(AccountController)
            .GetMethod("LoginPost", BindingFlags.Public | BindingFlags.Instance)!
            .GetParameters()
            .Select(p => p.Name)
            .ToList();

        Assert.Contains(BloqueDeEntrada.CampoDelUsuario, parametros);
    }

    [Fact]
    public void La_vista_usa_el_destino_y_el_campo_del_bloque()
    {
        var vista = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views", "Partials", "Elements", "Member", "Login.cshtml"));

        Assert.Contains("BloqueDeEntrada.Destino(", vista, StringComparison.Ordinal);
        Assert.Contains("name=\"@Synergos.CMS.Web.Services.BloqueDeEntrada.CampoDelUsuario\"", vista, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"email\"", vista, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) { dir = dir.Parent; }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
