using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Qué implementación de un elemento se sirve lo decide UN sitio, y no el orden de un diccionario
/// (#131).
/// </summary>
/// <remarks>
/// <para><b>Estaba escrito dos veces y el ticket nombraba una.</b>
/// <c>HttpBundleRegistryClient.ElegirFramework</c> y
/// <c>FileSystemBundleRegistryClient.ChooseFramework</c>: mismo sujeto —qué bundle recibe el
/// visitante— y misma política, o sea la misma cosa
/// (<c>feedback_the_same_algorithm_is_not_the_same_thing</c>). La segunda apareció buscando, no
/// leyendo el ticket.</para>
///
/// <para><b>El defecto era el desempate</b>: <c>Keys.FirstOrDefault()</c> sobre un
/// <c>Dictionary</c> es el orden de inserción, que sale del orden en que <c>System.Text.Json</c>
/// leyó el registry, que sale del orden en que el repo hermano lo escribió al publicar. Y una de
/// las dos copias afirmaba lo contrario en un comentario —«determinista por <c>StringComparer</c>
/// del dict construido»—, que decide cómo se BUSCA y no cómo se enumera.</para>
///
/// <para><b>Dos dientes, y el primero es el barato.</b> Que nadie más elija —para que no haya una
/// tercera copia— y que quien elige NO lo haga por el orden del diccionario. El segundo es el que
/// vigila la propiedad; el primero, que la propiedad valga para todos.</para>
///
/// <para><b>Lo que este gate NO decide:</b> cuál framework es mejor. Eso es política del repo
/// hermano y ya la tomó — un elemento con varias implementaciones es un <b>escaparate declarado</b>
/// (<c>SHOWCASE_MULTIPLATAFORMA</c>), no una forma de producto. Lo que el CMS debe pase lo que pase
/// es que la elección sea <b>estable</b>, y eso es lo que se vigila.</para>
/// </remarks>
public sealed class EleccionDeImplementacionUnicaTests
{
    private const string Helper = "EleccionDeImplementacion.cs";

    private static IReadOnlyList<(string Nombre, string Fuente)> Servicios()
        => Directory.EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "Services"), "*.cs")
            .Select(f => (Path.GetFileName(f)!, SinComentarios(f)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// El fichero sin comentarios. Acá SÍ es load-bearing, y está medido: el <c>&lt;remarks&gt;</c>
    /// del helper y el de las dos llamadas citan <c>Keys.FirstOrDefault()</c> para explicar el
    /// defecto, así que un gate que leyera el texto crudo se dispararía con su propia
    /// documentación (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    [Fact]
    public void Un_solo_sitio_elige_la_implementacion_de_un_elemento()
    {
        var servicios = Servicios();
        Assert.True(servicios.Count >= 20,
            $"Sólo se vieron {servicios.Count} ficheros en Web/Services: si se movieron, este gate "
            + "pasa en verde sin mirar nada.");

        // Elegir es reducir las llaves A UNA. Ordenarlas no es elegir —los dos clientes las
        // ordenan para NOMBRARLAS en su aviso— y enumerarlas tampoco: `FrameworksDeclarados` las
        // usa enteras para componer el import map. El corte va sobre `.First`/`.Single`/… dentro
        // de la misma sentencia, que es el gesto de quedarse con una.
        var eligen = servicios
            .Where(s => Regex.IsMatch(
                s.Fuente,
                @"\.Keys[^;]*\.(First|FirstOrDefault|Single|SingleOrDefault|ElementAt|Last)\s*\("))
            .Select(s => s.Nombre)
            .ToList();

        Assert.True(eligen.Count == 1 && eligen[0] == Helper,
            "La elección de implementación tiene que vivir en un solo sitio y hoy eligen: "
            + string.Join(", ", eligen)
            + $". Estaba escrita dos veces y el #131 nombraba una sola; con una tercera, el día "
            + $"que el desempate cambie, cambiará en una y no en las otras. Va en `{Helper}`.");
    }

    [Fact]
    public void Quien_elige_NO_desempata_por_el_orden_del_diccionario()
    {
        var helper = Path.Combine(Proyectos.Dir("Synergos.CMS.Web", "Services"), Helper);
        Assert.True(File.Exists(helper), $"No existe {Helper}: revisar este gate.");

        var fuente = SinComentarios(helper);

        Assert.DoesNotContain("Keys.FirstOrDefault()", fuente, StringComparison.Ordinal);
        Assert.True(Regex.IsMatch(fuente, @"OrderBy\([^)]*StringComparer\.Ordinal\)"),
            $"{Helper} tiene que ordenar antes de desempatar. Sin eso, la elección la toma el orden "
            + "de inserción del `Dictionary` —el orden en que el hermano escribió el registry al "
            + "publicar—, así que republicar cambia qué bundle recibe el visitante sin que nada "
            + "falle y sin que nadie lo haya decidido (#131). Ordinal no es una preferencia: es que "
            + "la misma entrada dé la misma salida.");
    }
}
