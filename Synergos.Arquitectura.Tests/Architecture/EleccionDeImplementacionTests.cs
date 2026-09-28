using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Qué implementación se le sirve al visitante lo decide UN solo sitio, y nunca el orden del
/// diccionario (#131).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> Cuando el <c>DefaultFramework</c> no estaba entre las
/// implementaciones de un elemento, los clientes del registry caían a
/// <c>Implementations.Keys.FirstOrDefault()</c> — el orden de inserción del <c>Dictionary</c>,
/// que sale del orden en que <c>System.Text.Json</c> leyó el JSON, que sale del orden en que el
/// repo hermano escribió el registry al publicar. <b>Republicar cambiaba qué bundle recibe el
/// visitante</b>, sin que nada fallara y sin que nadie hubiera decidido nada.</para>
///
/// <para><b>Y estaba escrito DOS veces, con el ticket nombrando una.</b> Apareció con un
/// <c>grep</c>, no leyendo: <c>HttpBundleRegistryClient.ElegirFramework</c> y
/// <c>FileSystemBundleRegistryClient.ChooseFramework</c> — mismo sujeto y misma política, o sea
/// la misma cosa (<c>feedback_the_same_algorithm_is_not_the_same_thing</c> leído al derecho).
/// Por eso el primer diente no comprueba la regla: comprueba que la pieza esté <b>ENCHUFADA</b>,
/// que es la lección del addendum #14 de
/// <c>feedback_an_exemption_needs_a_signature_behind_it</c> — un fichero puede CONTENER la
/// llamada veinte líneas más abajo y seguir decidiendo por su cuenta.</para>
///
/// <para><b>El disparador ya llegó, y se mide contra el artefacto y no contra la guía</b>: el
/// registry CONSTRUIDO del hermano trae ya un elemento con DOS implementaciones —<c>badge</c>,
/// en <c>angular</c> y <c>preact</c>—. El daño necesita además que NINGUNA sea la de por
/// defecto, cosa que hoy no pasa; por eso esto se pudo arreglar sin prisa y por eso el gate va
/// ahora, que es cuando es gratis.</para>
/// </remarks>
public sealed class EleccionDeImplementacionTests
{
    /// <summary>
    /// Piso de descubrimiento. Si el recorrido dejara de ver los clientes, las dos listas
    /// saldrían vacías y los dos dientes pasarían en verde sin mirar nada — el 12/12 sobre la
    /// lista vacía del #136. Eran dos al escribirlo.
    /// </summary>
    private const int MinimoDeClientes = 2;

    /// <summary>El orden del diccionario, en cualquiera de sus formas.</summary>
    private static readonly Regex OrdenDelDiccionario = new(
        @"\.Keys\s*\.\s*(?:FirstOrDefault|First|Single|SingleOrDefault|ElementAt)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// La fuente sin comentarios.
    /// </summary>
    /// <remarks>
    /// <b>Acá el barrido SÍ sostiene el gate, y está medido</b> (el addendum de
    /// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>: la dirección se mide,
    /// no se supone). El <c>&lt;remarks&gt;</c> de <c>EleccionDeImplementacion</c> cita
    /// <c>Keys.FirstOrDefault()</c> con todas las letras —tiene que hacerlo: es lo que explica
    /// el defecto— así que sin recortar comentarios el segundo diente acusaría al fichero que
    /// documenta el arreglo. Un gate que se pone rojo por su propia explicación enseña a
    /// ignorarlo.
    /// </remarks>
    private static string Desnuda(string fuente)
    {
        var sinBloques = Regex.Replace(fuente, @"/\*[\s\S]*?\*/", string.Empty);
        return string.Join('\n', sinBloques
            .Split('\n')
            .Select(l =>
            {
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i < 0 ? l : l[..i];
            }));
    }

    private static IReadOnlyList<string> Servicios()
        => Directory
            .EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "Services"), "*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Quien_lee_las_implementaciones_del_registry_LLAMA_al_helper_y_no_elige_solo()
    {
        var lectores = Servicios()
            .Select(f => (Nombre: Path.GetFileName(f)!, Fuente: Desnuda(File.ReadAllText(f))))
            .Where(x => x.Fuente.Contains("Implementations", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            lectores.Count >= MinimoDeClientes,
            $"Sólo se descubrieron {lectores.Count} lectores del registry y eran {MinimoDeClientes} "
            + "al escribir esto. Si el recorrido dejó de verlos, este gate estaría pasando en "
            + "verde sin mirar nada (#136).");

        var porSuCuenta = lectores
            .Where(x => !x.Fuente.Contains("EleccionDeImplementacion.Elegir(", StringComparison.Ordinal))
            .Select(x => x.Nombre)
            .ToList();

        Assert.True(
            porSuCuenta.Count == 0,
            "Estos ficheros leen `Implementations` del registry y NO llaman a "
            + "`EleccionDeImplementacion.Elegir`: " + string.Join(", ", porSuCuenta)
            + ". Qué bundle recibe el visitante se decide en UN sitio — con dos copias, la "
            + "política se corrige en una y se olvida en la otra, que es literalmente lo que "
            + "pasó: el #131 nombraba `ElegirFramework` y `ChooseFramework` tenía el mismo "
            + "defecto con otro nombre (#131).");
    }

    [Fact]
    public void Nadie_desempata_por_el_orden_del_diccionario()
    {
        var culpables = Servicios()
            .Where(f => OrdenDelDiccionario.IsMatch(Desnuda(File.ReadAllText(f))))
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        Assert.True(
            culpables.Count == 0,
            "Estos ficheros eligen una implementación por el orden del diccionario: "
            + string.Join(", ", culpables)
            + ". El orden de inserción sale del orden en que el repo hermano escribió el "
            + "registry, así que republicar cambiaría qué bundle recibe el visitante sin que "
            + "nadie lo decida. Y el comparador del diccionario NO lo arregla: decide cómo se "
            + "BUSCA, no cómo se ENUMERA — el comentario que lo afirmaba es el aviso de que "
            + "nadie lo comprobó (#131).");
    }
}
