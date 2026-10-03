using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Lo que una vista PÚBLICA le escribe a quien lee es la hora del sitio
/// (<c>Synergos:Listados:ZonaHoraria</c>), no la del instante UTC tal cual.
/// </summary>
/// <remarks>
/// <para>El hilo de comentarios escribía <c>CreatedAtUtc.ToString("dd MMM yyyy HH:mm")</c>: el
/// comentario de las 22:30 en Bogotá decía «03:30» del día siguiente. Una vista no tiene test de
/// comportamiento —el runtime la compila en caliente—, así que la guardia es de texto: ningún
/// <c>…Utc.ToString("…")</c> fuera del atributo <c>datetime</c> (<c>"O"</c>), que es para la
/// máquina.</para>
///
/// <para><b>Fuera de la barrida, y a propósito:</b> <c>Views/Admin</c> y <c>Views/Emails</c>. Son
/// del backoffice y de los avisos de operación, y la hora va rotulada «UTC» al lado.</para>
///
/// <para>Es una hipótesis con forma de grep: ve un instante que se llame <c>…Utc</c>. Uno con otro
/// nombre se le escapa.</para>
/// </remarks>
public sealed class VistasEnLaHoraDelSitioTests
{
    private static readonly Regex InstanteUtcFormateado = new(
        @"Utc(\.Value)?\.ToString\(""(?!O"")", RegexOptions.CultureInvariant);

    [Fact]
    public void Ninguna_vista_publica_escribe_un_instante_UTC_tal_cual()
    {
        var vistas = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views");
        var fuera = new[]
        {
            Path.Combine(vistas, "Admin") + Path.DirectorySeparatorChar,
            Path.Combine(vistas, "Emails") + Path.DirectorySeparatorChar,
        };

        var hallazgos = Directory.EnumerateFiles(vistas, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !fuera.Any(d => f.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(f => File.ReadLines(f)
                .Select((linea, i) => (f, i, linea))
                .Where(x => InstanteUtcFormateado.IsMatch(x.linea)))
            .Select(x => $"{Path.GetRelativePath(vistas, x.f)}:{x.i + 1}: {x.linea.Trim()}")
            .ToList();

        Assert.True(hallazgos.Count == 0, string.Join(Environment.NewLine, hallazgos));
    }

    [Fact] // control: la barrida SÍ ve la forma que se arregló, y deja pasar el atributo de máquina.
    public void La_barrida_ve_la_forma_del_defecto()
    {
        Assert.Matches(InstanteUtcFormateado, """@comment.CreatedAtUtc.ToString("dd MMM yyyy HH:mm", coCulture)""");
        Assert.Matches(InstanteUtcFormateado, """@m.LastLoginUtc.Value.ToString("yyyy-MM-dd HH:mm")""");
        Assert.DoesNotMatch(InstanteUtcFormateado, """datetime="@comment.CreatedAtUtc.ToString("O")" """);
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
