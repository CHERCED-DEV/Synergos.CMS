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
/// del backoffice y de los avisos de operación, y la hora va rotulada «UTC» al lado. Que lo vaya
/// lo fija la segunda guardia: ahí la excepción sólo vale si lo dice.</para>
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

    /// <summary>Un valor para la máquina —el de un campo de fecha, el de la URL— no se rotula.</summary>
    private static readonly Regex ValorDeMaquina = new(
        @"value=""@|qs\.Add\(|InputValue\(\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// En el backoffice y en los avisos de operación, toda hora UTC que se le escribe a una persona
    /// dice «UTC»: en la misma línea, en la siguiente (una frase partida en dos) o en la cabecera de
    /// su tabla («Recibido (UTC)», «Cuándo (UTC)»).
    /// </summary>
    /// <remarks>
    /// <para>«Creado» en la lista de miembros y «Última vez» en la analítica de búsqueda salían sin
    /// rótulo, al lado de columnas que sí lo llevaban: quien lo leía en Bogotá tomaba por suya una
    /// fecha que podía ser la del día siguiente.</para>
    ///
    /// <para>La regla de la cabecera es gruesa a propósito: basta con que una cabecera de la tabla
    /// diga «UTC», no la de esa columna. Una tabla con dos fechas y un solo rótulo se le escapa.</para>
    /// </remarks>
    [Fact]
    public void El_backoffice_dice_UTC_junto_a_cada_hora_UTC()
    {
        var vistas = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Views");

        var hallazgos = new[] { "Admin", "Emails" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(vistas, d), "*.cshtml", SearchOption.AllDirectories))
            .SelectMany(f =>
            {
                var lineas = File.ReadAllLines(f);
                return lineas
                    .Select((linea, i) => (f, i, linea, siguiente: i + 1 < lineas.Length ? lineas[i + 1] : string.Empty))
                    .Where(x => InstanteUtcFormateado.IsMatch(x.linea) && !ValorDeMaquina.IsMatch(x.linea))
                    .Where(x => !x.linea.Contains("UTC", StringComparison.Ordinal)
                        && !x.siguiente.Contains("UTC", StringComparison.Ordinal)
                        && !LaCabeceraDeSuTablaDiceUtc(lineas, x.i));
            })
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
        Assert.Matches(ValorDeMaquina, """<input type="date" name="from" value="@fromUtc.ToString("yyyy-MM-dd")" />""");
        Assert.DoesNotMatch(ValorDeMaquina, """<td>@item.ReceivedAtUtc.ToString("yyyy-MM-dd HH:mm:ss")</td>""");
    }

    /// <summary>¿Alguna cabecera de la tabla que contiene la línea dice «UTC»?</summary>
    private static bool LaCabeceraDeSuTablaDiceUtc(string[] lineas, int indice)
    {
        for (var i = indice - 1; i >= 0; i--)
        {
            if (lineas[i].Contains("</table>", StringComparison.Ordinal))
            {
                return false;
            }

            if (lineas[i].Contains("<th", StringComparison.Ordinal) && lineas[i].Contains("UTC", StringComparison.Ordinal))
            {
                return true;
            }

            if (lineas[i].Contains("<table", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return false;
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
