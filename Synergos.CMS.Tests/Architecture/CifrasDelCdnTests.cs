using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cuántos elementos publica el CDN se escribe en UN solo sitio (hallazgo #86).
/// </summary>
/// <remarks>
/// <para><b>Su valor no se puede comprobar desde este repo</b>, y por eso este gate no lo intenta:
/// la cifra depende del CDN (red) y del repo hermano, y CI no tiene ninguno de los dos. Los otros
/// gates de cifras —#52, #66, #80— cuentan todos contra algo que está en el disco de acá; ésta era
/// la única escrita a mano sin nada que la cruzara, y se desvió en seis sitios a la vez.</para>
///
/// <para><b>Así que lo que se vigila es la FORMA en que se rompió: copiarla.</b> Un número que vive
/// en un sitio se corrige una vez; uno copiado en seis se corrige en cinco y medio. Llegó a tener el
/// valor correcto y uno equivocado <i>en el mismo fichero</i>, a cien líneas de distancia.</para>
///
/// <para><b>Y la sede lleva el comando al lado</b>, que es la otra mitad. Antes decía «139
/// elementos, cabeceras verificadas 2026-08-04»: una fecha hace que un número recordado se lea como
/// medido, y entonces nadie lo cruza porque parece que ya alguien lo hizo.</para>
/// </remarks>
public sealed class CifrasDelCdnTests
{
    /// <summary>Ficheros de texto donde podría aparecer la cifra. Los binarios y lo generado, no.</summary>
    private static readonly string[] Extensiones = [".md", ".cs", ".mjs", ".yml", ".yaml"];

    /// <summary>
    /// Lo que se busca: una línea que hable del CDN y lleve una cantidad de elementos.
    /// </summary>
    /// <remarks>
    /// Pide las DOS cosas —contexto de CDN y número con sustantivo— porque cualquiera de las dos
    /// sola da falsos positivos a montones: este repo cuenta aliases, claves de diccionario,
    /// endpoints y códigos de rechazo, y ninguna de esas cifras es ésta.
    /// </remarks>
    private static readonly Regex Cantidad = new(
        @"\b\d{2,4}\s+(elementos|bundles|entradas)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HablaDelCdn = new(
        @"cdn|registry\.json|workers\.dev|bundle",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Lo que este gate NO mira, y por qué cada cosa.
    /// </summary>
    /// <remarks>
    /// <para><b>Los ADRs y los inventarios son registros FECHADOS.</b> Cuando la ADR 0089 dice «49
    /// elementos» está contando lo que había el día que se decidió aquello, y el inventario de
    /// <c>docs/product/</c> igual. Corregirlos no los pondría al día: los <b>falsificaría</b>, que
    /// es justo lo que un registro con fecha existe para impedir. Un ADR no es documentación viva
    /// que se mantiene, es lo que se creía y decidió en un momento.</para>
    ///
    /// <para><b>El catálogo de la skill se AUTO-GENERA</b> desde el repo hermano —lleva escrito
    /// <c>AUTO-GENERATED</c> en su primera línea—, así que corregirlo a mano lo desharía la
    /// siguiente regeneración. Que estuviera stale era cierto y se regeneró; lo que no puede es
    /// entrar en este gate, o obligaría a editar a mano justo lo que no se edita a mano.</para>
    ///
    /// <para>Lo que queda dentro es lo que sí es una afirmación <b>viva</b>: la guía, el manual de
    /// despliegue, los comentarios de código y el copy del sitio. Ahí es donde una cifra copiada
    /// hace daño, porque se lee como el estado de hoy.</para>
    /// </remarks>
    private static readonly string[] Exentos =
    [
        Path.Combine("docs", "adr"),
        Path.Combine("docs", "product", "inventario"),
        Path.Combine(".claude", "skills"),
        Path.Combine("bin", ""),
        Path.Combine("obj", ""),
        "node_modules",
    ];

    [Fact]
    public void La_cuenta_de_elementos_del_CDN_aparece_en_un_solo_sitio()
    {
        var raiz = RepoRoot();

        var hallazgos = Directory
            .EnumerateFiles(raiz, "*", SearchOption.AllDirectories)
            .Where(f => Extensiones.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Exentos.Any(e => f.Contains(e, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(f => File.ReadLines(f)
                .Select((linea, i) => (Fichero: Path.GetRelativePath(raiz, f), Numero: i + 1, Linea: linea))
                .Where(l => Cantidad.IsMatch(l.Linea) && HablaDelCdn.IsMatch(l.Linea)))
            .ToList();

        // EXACTAMENTE uno, no «como mucho uno». Con `<=` el gate también pasaría con CERO, o sea
        // si alguien borra la sede — y entonces estaría vigilando que no exista lo que existe para
        // que se pueda leer. Un gate que pasa cuando desaparece lo que cuida no cuida nada.
        Assert.True(hallazgos.Count == 1,
            $"La cuenta de elementos del CDN aparece {hallazgos.Count} veces y tiene que aparecer " +
            "UNA. Copiada es cómo llegó a decir 139, 130 y 122 a la vez; borrada deja a quien lea " +
            "la guía sin saber de qué tamaño es el catálogo (#86). Su valor no se puede comprobar " +
            "desde este repo —depende de la red y del repo hermano— así que la defensa es que viva " +
            "en un sitio, con el comando para volver a medirla al lado. Encontrada en:\n  " +
            string.Join("\n  ", hallazgos.Select(h => $"{h.Fichero}:{h.Numero} → {h.Linea.Trim()}")));
    }

    /// <summary>
    /// Y ese sitio lleva el comando para volver a medirla.
    /// </summary>
    /// <remarks>
    /// Sin esto el gate de arriba se cumple dejando un número solo, sin forma de comprobarlo — que
    /// es como estaba antes, sólo que en un sitio en vez de seis. Lo que hace que una cifra no
    /// verificable sea honesta es que quien la lea pueda volver a medirla sin investigar cómo.
    /// </remarks>
    [Fact]
    public void Y_ese_sitio_dice_como_volver_a_medirla()
    {
        var claude = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        var i = claude.IndexOf("synergos-ui.synergos-labs.workers.dev/synergos/registry.json", StringComparison.Ordinal);
        Assert.True(i > 0,
            "CLAUDE.md ya no dice cómo medir la cuenta de elementos del CDN. Un número sin la forma " +
            "de comprobarlo vuelve a ser un número recordado (#86).");

        var alrededor = claude[Math.Max(0, i - 600)..Math.Min(claude.Length, i + 600)];
        Assert.Contains("curl", alrededor, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
