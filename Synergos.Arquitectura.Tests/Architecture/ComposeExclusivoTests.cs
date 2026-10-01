using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Toda clase de esta suite que lea <c>compose.prod.yml</c> corre en la colección
/// <see cref="ComposeExclusivo"/>, en serie con la que lo escribe.
/// </summary>
/// <remarks>
/// <para><b>La regla estaba escrita y no la cumplía nadie.</b> El <c>remarks</c> de
/// <see cref="ComposeExclusivo"/> decía «si mañana aparece una quinta clase que lea
/// <c>compose.prod.yml</c>, va acá también». Aparecieron tres —<c>ClavesDeUmbracoTests</c>,
/// <c>CredencialesFueraDelArbolTests</c> y <c>ModosDelComposeTests</c>— y ninguna entró. El síntoma
/// fue el que ese mismo comentario anunciaba: un rojo intermitente al correr la solución entera, que
/// desaparece al correr la suite sola (#181). Una regla que vive sólo en un comentario no la ve quien
/// escribe la clase nueva; ésta la ve el build.</para>
///
/// <para><b>Se mira la FUENTE</b>, sin comentarios: el literal <c>compose.prod.yml</c> en código es
/// leer el fichero (directo o por una ruta armada con él). La colección se lee de la misma fuente.</para>
/// </remarks>
public sealed class ComposeExclusivoTests
{
    [Fact]
    public void Toda_clase_que_lee_compose_prod_yml_corre_en_serie_con_la_que_lo_escribe()
    {
        var carpeta = Path.Combine(RepoRoot(), "Synergos.Arquitectura.Tests", "Architecture");
        var lectoras = Directory.GetFiles(carpeta, "*.cs")
            .Where(f => Path.GetFileName(f) is not ("ComposeExclusivo.cs" or "ComposeExclusivoTests.cs"))
            .Select(f => (Fichero: Path.GetFileName(f), Codigo: SinComentarios(File.ReadAllText(f))))
            .Where(x => x.Codigo.Contains("compose.prod.yml", StringComparison.Ordinal))
            .ToList();

        // Suelo contra el vacío: la que escribe el fichero tiene que aparecer, o el barrido no mira nada.
        Assert.Contains(lectoras, x => x.Fichero == "ComposeStackTests.cs");

        var fuera = lectoras
            .Where(x => !x.Codigo.Contains("[Collection(ComposeExclusivo.Nombre)]", StringComparison.Ordinal))
            .Select(x => x.Fichero)
            .ToList();

        Assert.True(fuera.Count == 0,
            "leen compose.prod.yml y corren en paralelo con ComposeStackTests, que lo muta: "
            + string.Join(", ", fuera) + ". Ponéles [Collection(ComposeExclusivo.Nombre)].");
    }

    private static string SinComentarios(string fuente)
        => Regex.Replace(fuente, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
