using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Todo ADR está en el índice, y el índice no apunta a nada que no exista (#64).
/// </summary>
/// <remarks>
/// <para><b>El defecto que evita ya ocurrió.</b> La ADR <b>0133</b> —la del despliegue entero:
/// imágenes por SHA a GHCR, el compose de producción, la vuelta atrás automática— existía,
/// estaba aceptada, y <b>no estaba en el índice</b>. <c>CLAUDE.md</c> §3 manda a
/// <c>docs/adr/README.md</c> para la pregunta «¿por qué se tomó esta decisión?», así que un ADR
/// fuera de esa tabla es un ADR perdido: el día que se cree el VPS (#21), quien vaya a buscarla
/// navegando el índice no la encuentra.</para>
///
/// <para><b>Se cruza en los DOS sentidos, y la segunda mitad no es simetría por gusto:</b> una
/// entrada que apunta a un fichero que no existe es <b>peor</b> que la ausencia, porque parece
/// que hay algo y no lo hay — es la misma lección que dejó §9 al nombrar tres artefactos
/// inexistentes (#53).</para>
///
/// <para><b>Qué NO comprueba.</b> Ni el orden de la tabla, ni el texto del resumen, ni el estado
/// (<c>Accepted</c> / <c>Superseded</c>). Eso es criterio de quien escribe el ADR, y un gate que
/// lo midiera obligaría a redactar para el gate. Lo que se vigila es que la lista esté completa,
/// que es lo único que se desactualiza solo.</para>
/// </remarks>
public sealed class AdrIndexTests
{
    private static string AdrDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var adr = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "docs", "adr");
        Assert.True(Directory.Exists(adr), $"No existe {adr}: revisar este gate.");
        return adr;
    }

    /// <summary>Los ADR del disco, por su número de cuatro cifras.</summary>
    private static IReadOnlyList<string> EnDisco()
        => Directory.EnumerateFiles(AdrDir(), "*.md")
            .Select(Path.GetFileName)
            .Where(n => Regex.IsMatch(n!, @"^\d{4}-"))
            .Select(n => n![..4])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Los que el índice enlaza, leídos del enlace y no del texto.</summary>
    /// <remarks>
    /// Del enlace a propósito: el número suelto aparece en la prosa del README —«ver ADR 0012»—
    /// y contarlo daría por indexado algo que sólo está mencionado de pasada.
    /// </remarks>
    private static IReadOnlyList<string> EnIndice()
    {
        var texto = File.ReadAllText(Path.Combine(AdrDir(), "README.md"));
        return Regex.Matches(texto, @"\((\d{4})-[^)]*\.md\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Todo_ADR_del_disco_esta_en_el_indice()
    {
        var disco = EnDisco();

        // Sin esto, un descubrimiento roto dejaría el assert comparando dos listas vacías.
        Assert.True(disco.Count > 100, $"Se encontraron {disco.Count} ADR: revisar este gate.");

        var faltan = disco.Except(EnIndice(), StringComparer.Ordinal).ToList();

        Assert.True(faltan.Count == 0,
            $"Estos ADR no están en docs/adr/README.md: {string.Join(", ", faltan)}. "
            + "CLAUDE.md §3 manda a ese índice para «¿por qué se tomó esta decisión?», así que "
            + "un ADR fuera de la tabla es un ADR perdido.");
    }

    [Fact]
    public void El_indice_no_enlaza_ADR_que_no_existen()
    {
        var sobran = EnIndice().Except(EnDisco(), StringComparer.Ordinal).ToList();

        Assert.True(sobran.Count == 0,
            $"El índice enlaza estos ADR y no están en disco: {string.Join(", ", sobran)}. "
            + "Un enlace roto es peor que la ausencia: parece que hay algo y no lo hay.");
    }
}
