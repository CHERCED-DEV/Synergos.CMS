namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Nadie afirma una identidad con más fuerza de la que alguien certificó (defecto #42).
/// </summary>
/// <remarks>
/// <para><b>Este gate vigila el USO; el de <see cref="BackendSegregationTests"/> vigila la
/// DECLARACIÓN.</b> Aquel comprueba que <c>IdentityAssertion</c> viva en un solo sitio, y estaba
/// bien — pero son cosas distintas, y por eso el defecto #42 pasó: había un acuse anotado como
/// <c>IdentityToken</c> con el vocabulario perfectamente definido en un único fichero.</para>
///
/// <para><b>La regla, en una frase: el valor no mide confianza, mide quién dio fe.</b> «No hay
/// duda de quién escribió» es un razonamiento sobre confianza y no autoriza a subir de escalón.
/// <c>IdentityToken</c> significa que <c>Api.Identity</c> emitió un token verificable, y hoy
/// <c>Api.Identity</c> no sabe emitir ninguno: solo tiene
/// <c>POST /v1/credentials/verify</c>, que es una comprobación de un solo tiro.
/// <c>GovFederation</c> significa que dio fe el Estado, y no hay federación conectada.</para>
///
/// <para><b>Por qué también <c>GovFederation</c>, que el ticket no pedía.</b> Porque es
/// exactamente el mismo defecto esperando turno: una afirmación cuyo certificador todavía no
/// existe. Vigilar una y dejar la otra libre habría dejado el gate a medio hacer por una
/// frontera que no significa nada.</para>
///
/// <para><b>Se cae solo, y ése es el punto.</b> El día que la HU #14 emita tokens de verdad este
/// gate se pone en rojo, y esa línea roja ES la revisión: obliga a mirar cada sitio que empiece a
/// afirmar más fuerte, en vez de que se cuele uno por descuido. Cuando pase, se acota a los
/// sitios legítimos — no se borra.</para>
///
/// <para><b>Lo que este gate NO hace: mirar el pasado.</b> Los acuses ya guardados con
/// <c>IdentityToken</c> se quedan como están. Reescribirlos convertiría un archivo append-only en
/// uno editable, que es justo lo que lo hace inútil como prueba. Lo que queda escrito —acá y en
/// la definición del enum— es que los acuses anteriores a esta corrección no significan lo que
/// dicen.</para>
/// </remarks>
public sealed class IdentityAssertionUsageTests
{
    /// <summary>Afirmaciones sin emisor: nadie puede certificarlas todavía.</summary>
    private static readonly string[] SinEmisor =
    {
        "IdentityAssertion.IdentityToken",
        "IdentityAssertion.GovFederation",
    };

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

    /// <summary>
    /// El árbol de producción: todo <c>.cs</c> que no sea de pruebas ni generado.
    /// </summary>
    /// <remarks>
    /// Las pruebas quedan fuera a propósito — un test que verifica el rechazo de una afirmación
    /// fuerte tiene que poder nombrarla. Lo que se vigila es lo que se ENVÍA, no lo que se prueba.
    /// </remarks>
    private static IEnumerable<string> FicherosDeProduccion()
    {
        var root = RepoRoot();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var s = Path.DirectorySeparatorChar;
            if (path.Contains($"{s}obj{s}", StringComparison.Ordinal) ||
                path.Contains($"{s}bin{s}", StringComparison.Ordinal) ||
                path.Contains($"{s}_archive{s}", StringComparison.Ordinal) ||
                path.Contains($"{s}Synergos.CMS.Tests{s}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
        }
    }

    /// <summary>La prosa explica el código, no lo es: un comentario que la nombra no la emite.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) ||
                t.StartsWith("*", StringComparison.Ordinal) ||
                t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    [Fact]
    public void Nadie_afirma_una_identidad_que_nadie_certifico()
    {
        var culpables = new List<string>();
        var mirados = 0;

        foreach (var ruta in FicherosDeProduccion())
        {
            mirados++;
            var codigo = SinComentarios(ruta);
            foreach (var afirmacion in SinEmisor)
            {
                if (codigo.Contains(afirmacion, StringComparison.Ordinal))
                {
                    culpables.Add($"{Path.GetRelativePath(RepoRoot(), ruta)} → {afirmacion}");
                }
            }
        }

        Assert.True(mirados > 100, $"Solo se miraron {mirados} ficheros: revisar este gate.");

        Assert.True(culpables.Count == 0,
            "Se está afirmando una identidad que nadie certificó todavía:\n  "
            + string.Join("\n  ", culpables)
            + "\n\nEl campo no mide cuánta confianza tenemos en quién es — mide QUIÉN DIO FE. "
            + "`IdentityToken` exige que Api.Identity haya emitido un token (HU #14, sin hacer) y "
            + "`GovFederation` que lo certifique el Estado. Mientras no existan, lo honesto es "
            + "`CmsSession`: el borde miró la sesión y nos fiamos de él.\n"
            + "Si estás cableando el emisor de verdad, este gate se acota a los sitios legítimos "
            + "— no se borra.");
    }

    /// <summary>
    /// La definición del enum deja dicho que el archivo viejo miente. Sin esa nota, el día que
    /// alguien audite verá acuses con <c>IdentityToken</c> y creerá que hubo un token.
    /// </summary>
    [Fact]
    public void La_definicion_ADVIERTE_sobre_los_acuses_viejos()
    {
        var enumeracion = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.Core", "IdentityAssertion.cs"));

        Assert.Contains("#42", enumeracion, StringComparison.Ordinal);
    }
}
