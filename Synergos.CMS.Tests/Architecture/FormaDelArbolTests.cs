using System.Diagnostics;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La FORMA del árbol versionado, no su contenido (defecto #90).
/// </summary>
/// <remarks>
/// <para>Todos los demás gates miran qué DICE un fichero. Éste mira qué ES. Hizo falta porque
/// `master` llegó a traer un symlink <b>a sí mismo</b> —<c>Synergos.CMS → /home/user/Synergos.CMS</c>,
/// ruta absoluta de un contenedor— y eso tumbó los seis gates de segregación de golpe: .NET sigue
/// los enlaces al recorrer directorios, así que cada <c>.csproj</c> aparecía dos veces y todo
/// <c>SingleOrDefault()</c> sobre un nombre reventaba.</para>
///
/// <para><b>No lo escribió nadie a mano.</b> Lo dejan las herramientas cross-repo de
/// <c>Synergos.UI</c> cuando el hermano no está en la ruta que asumen: crean el enlace para que sus
/// rutas relativas resuelvan, y el <c>git add -A</c> siguiente se lo lleva. Por eso el gate va sobre
/// la clase entera y no sobre ese nombre: el enlace que entre la próxima vez se llamará de otra
/// forma.</para>
/// </remarks>
public sealed class FormaDelArbolTests
{
    /// <summary>
    /// Ningún fichero versionado es un enlace simbólico.
    /// </summary>
    /// <remarks>
    /// <para><b>Se pregunta al índice de git, no al disco</b>, y ésa es la diferencia que hace que
    /// esto sirva: el disco de quien corre los tests tiene enlaces legítimos —clones vecinos,
    /// atajos de herramientas— que no son asunto del repo. Lo que importa es qué quedó
    /// <i>versionado</i>, porque eso es lo que le llega a los demás.</para>
    ///
    /// <para><b>Y un symlink versionado no es «raro pero inofensivo».</b> Guarda una ruta, y una
    /// ruta sólo significa algo en la máquina donde se escribió: en cualquier otra queda colgada o
    /// —peor— apunta a otra cosa. Si el repo alguna vez necesita uno de verdad, esta lista se abre
    /// con el caso escrito al lado, que es más de lo que tuvo el que rompió los gates.</para>
    /// </remarks>
    [Fact]
    public void Ningun_fichero_versionado_es_un_symlink()
    {
        var enlaces = GitLsFiles()
            // 120000 es el modo de un symlink en el índice de git. 100644/100755 son ficheros.
            .Where(l => l.StartsWith("120000 ", StringComparison.Ordinal))
            .Select(l => l.Split('\t').Last())
            .ToList();

        Assert.True(enlaces.Count == 0,
            "Hay symlinks versionados, y uno solo tumba los seis gates de segregación (#90): .NET " +
            "los sigue al recorrer directorios, así que cada .csproj aparece dos veces y todo " +
            "SingleOrDefault() sobre un nombre revienta. Además guardan una ruta, que sólo " +
            "significa algo en la máquina donde se escribió — el que rompió master apuntaba a " +
            "/home/user/Synergos.CMS. Suelen entrar por un `git add -A` después de correr las " +
            "herramientas cross-repo de §7, que crean el enlace para resolver sus rutas. " +
            "Encontrados:\n  " + string.Join("\n  ", enlaces));
    }

    /// <summary>
    /// Y no hay una carpeta versionada que repita el nombre del repo.
    /// </summary>
    /// <remarks>
    /// <c>CLAUDE.md</c> §7 afirma que «no hay carpeta anidada con ese nombre», y de esa afirmación
    /// cuelgan todas las rutas que da la guía. El enlace de #90 la volvió mentira sin que nada
    /// fallara por ese lado — los tests reventaron por el bucle, no por la contradicción. Esto
    /// cubre también el caso de que entre como carpeta de verdad y no como enlace.
    /// </remarks>
    [Fact]
    public void No_hay_una_carpeta_versionada_que_repita_el_nombre_del_repo()
    {
        var repetidos = GitLsFiles()
            .Select(l => l.Split('\t').Last())
            .Where(ruta => ruta.StartsWith("Synergos.CMS/", StringComparison.Ordinal)
                        || ruta.Equals("Synergos.CMS", StringComparison.Ordinal))
            .ToList();

        Assert.True(repetidos.Count == 0,
            "Hay algo versionado bajo `Synergos.CMS/`, y CLAUDE.md §7 dice que no hay carpeta " +
            "anidada con ese nombre — de esa frase cuelgan todas las rutas que da la guía (#90). " +
            "Encontrados:\n  " + string.Join("\n  ", repetidos.Take(10)));
    }

    /// <summary>Los ficheros del índice, con su modo. Falla ruidosamente si git no contesta.</summary>
    private static IReadOnlyList<string> GitLsFiles()
    {
        var psi = new ProcessStartInfo("git", "ls-files -s")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);

        var salida = proc!.StandardOutput.ReadToEnd();
        proc.WaitForExit(30_000);

        // Sin esto, un git que no arranca dejaría el gate en verde con cero ficheros — vigilando
        // nada, que es exactamente lo que un gate no puede hacer en silencio.
        Assert.True(proc.ExitCode == 0, $"`git ls-files -s` falló: {proc.StandardError.ReadToEnd()}");

        var lineas = salida.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lineas.Length > 100, $"`git ls-files -s` devolvió {lineas.Length} líneas: no es este repo.");
        return lineas;
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
