using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Si un rechazo del árbol de servicios se puede reintentar se LEE de la bandera que la
/// capacidad emite, y no se deduce del código de estado (#129).
/// </summary>
/// <remarks>
/// <para><b>El hallazgo, y su premisa corregida.</b> El ticket decía «14 de 15 clientes deciden
/// si reintentar mirando el CÓDIGO». Medido rama por rama sobre los quince
/// <c>Synergos.CMS.Web/Services/Http*.cs</c>: <b>cero</b> tienen bucle de reintento, <b>uno</b>
/// lee <c>transient</c>, y <b>cero</b> nombran un código de transitoriedad. O sea que los
/// catorce no deciden «si reintentar»: deciden <b>cómo PRESENTAR el fallo</b>, y casi siempre
/// bien — un 404 es «no existe», un 401 nombra la llave compartida, un 4xx con motivo sube como
/// rechazo de negocio. Eso es de cada cliente y se queda donde está. Creerle al verbo del
/// hallazgo habría metido una política de reintentos que nadie decidió en catorce ficheros.</para>
///
/// <para><b>Lo que sí era cierto: la regla estaba escrita DENTRO del único que la cumplía.</b>
/// El <c>&lt;remarks&gt;</c> de <c>HttpPaymentProvider</c> decía, con todas las letras, «se mira
/// esa bandera y no el código de estado: repetir aquí la tabla de códigos sería una segunda
/// verdad que se desincroniza». Una regla escrita en el único sitio que la obedece no se
/// difunde: se entierra, y encima <b>parece</b> difundida, porque quien la lee la está leyendo
/// ya cumplida. Es <c>feedback_a_fabrication_can_be_a_derivation</c> addendum #123 con el sujeto
/// cambiado —allá lo blindado era un defecto, acá una regla buena— y con la misma salida: lo que
/// reemplaza a la nota no es otra nota, es un TIPO (<c>RechazoDelArbolDeServicios</c>) más este
/// gate.</para>
///
/// <para><b>Trinquete ABSOLUTO y no línea base</b>, porque el árbol YA lo cumple: cero
/// infractores al escribirlo, así que exigirlo cuesta un censo vacío en vez del muro de
/// excepciones que deja de leerse. Es el criterio del #134 («un umbral absoluto sólo vale cuando
/// el árbol ya lo cumple») y el del #140 al revés — con un solo infractor habría ido línea
/// base.</para>
///
/// <para><b>Lo que este gate NO dice, para no mentir sobre su alcance:</b> no comprueba que
/// alguien REINTENTE. Hoy nadie lo hace, y quién debería —el cliente o quien lo llama— el CMS no
/// lo tiene decidido; en el árbol de servicios sí está escrito («la capacidad sabe QUÉ está
/// colgado y CÓMO se reintenta; el orquestador, CUÁNDO y CUÁNTAS VECES», #29) y traerlo a este
/// lado es otro trabajo. Lo que esto cierra es que el día que se tome la decisión se tome sobre
/// UN solo dato.</para>
/// </remarks>
public sealed class TransitoriedadTests
{
    /// <summary>
    /// Los clientes que SÍ pueden deducir transitoriedad de un código de estado, con su razón.
    /// </summary>
    /// <remarks>
    /// <para><b>Está vacío, y eso es lo correcto hoy</b>: medido, ninguno de los quince lo hace.
    /// Una entrada acá tendría que contestar «por qué esto NO se lee de la bandera» y no «por qué
    /// todavía no se lee» — lo segundo es un ticket sin abrir disfrazado de exención
    /// (<c>feedback_a_census_entry_is_how_a_defect_survives_its_own_gate</c>).</para>
    ///
    /// <para>Se vigila en los DOS sentidos: una fila que ya no corresponda rompe el build igual
    /// que un infractor sin declarar. Un censo vigilado en un solo sentido se queda afirmando que
    /// el defecto sigue ahí después de que alguien lo arregló.</para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Censo =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Piso de descubrimiento. Si el patrón de los clientes dejara de casar, la lista saldría
    /// vacía y el primer diente pasaría en verde sin mirar nada — el fallo que el #136 midió
    /// (12/12 sobre una lista vacía). Eran quince al escribirlo.
    /// </summary>
    private const int MinimoDeClientes = 10;

    /// <summary>
    /// Las formas de deducir «esto se puede reintentar» de la respuesta HTTP en vez de leerlo.
    /// </summary>
    /// <remarks>
    /// <para>No entran <c>400</c> ni <c>500</c>: son los extremos del corte 4xx que
    /// <c>HttpPaymentProvider</c> sí hace a propósito —un rechazo firme que llegue con un 5xx
    /// sigue siendo un fallo del servicio— y que no es deducir transitoriedad, sino decidir si
    /// la respuesta viene del medio de pago o de la infraestructura.</para>
    ///
    /// <para>Tampoco entra <c>Unavailable</c> a secas: ése es el vocabulario de
    /// <c>Rejection.Unavailable</c> del otro árbol, no un código de estado, y meterlo haría que
    /// el gate acusara a quien nombra correctamente la razón de la bandera.</para>
    /// </remarks>
    private static readonly Regex CodigoDeTransitoriedad = new(
        @"\bServiceUnavailable\b|\bRequestTimeout\b|\bTooManyRequests\b|\bGatewayTimeout\b"
        + @"|\bBadGateway\b|(?<![\w.])(?:502|503|504|408|429)(?![\w.])",
        RegexOptions.Compiled);

    /// <summary>
    /// La bandera del contrato: la propiedad que la enlaza o la clave serializada que la nombra.
    /// </summary>
    /// <remarks>
    /// <c>\bTransient\b</c> NO casa dentro de <c>AddTransient</c> —la <c>d</c> anterior es un
    /// carácter de palabra, así que no hay frontera— que es lo que deja fuera a los treinta y
    /// tantos registros de DI del composer sin necesidad de una excepción.
    /// </remarks>
    private static readonly Regex BanderaDelContrato = new(
        @"\bTransient\b|""transient""", RegexOptions.Compiled);

    /// <summary>
    /// La fuente sin comentarios.
    /// </summary>
    /// <remarks>
    /// <para><b>Es lo que evita un FALSO POSITIVO, y está medido en vez de supuesto</b> (el
    /// addendum de <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>: «quitar los
    /// comentarios» tiene una DIRECCIÓN y hay que medir cuál). Con el barrido apagado, el segundo
    /// diente acusa a <b>cinco</b> composers cuya prosa dice «Transient porque un
    /// <c>DelegatingHandler</c> lo es por contrato» — un tiempo de vida de DI que no tiene nada
    /// que ver con esta bandera. Y el primer diente acusaría a
    /// <c>HttpPaymentProvider.EsRechazoFirme</c>, cuyo <c>&lt;remarks&gt;</c> nombra
    /// <c>ServiceUnavailable</c> justamente para explicar que NO se mira. Un gate que se pone
    /// rojo por su propia documentación enseña a ignorarlo.</para>
    ///
    /// <para><b>Los literales NO se recortan, a propósito.</b> Un cliente que escribiera
    /// <c>"503"</c> en un log lo estaría nombrando igual, y lo que este gate quiere es que
    /// nadie tenga ese número a mano en su camino de rechazo. Recortarlos abriría justo la vía
    /// por la que se vuelve a escribir la tabla.</para>
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

    private static IReadOnlyList<string> Clientes()
        => Directory
            .EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "Services"), "Http*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> FuentesDelWeb()
        => Directory
            .EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(
                s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Ningun_cliente_deduce_la_transitoriedad_de_un_codigo_de_estado()
    {
        var clientes = Clientes();

        Assert.True(
            clientes.Count >= MinimoDeClientes,
            $"Sólo se descubrieron {clientes.Count} clientes «Http*.cs» en Services/, y eran 15 "
            + "al escribir esto. Si el patrón dejó de casar, este gate estaría pasando en verde "
            + "sin mirar nada (#136).");

        var infractores = clientes
            .Where(f => CodigoDeTransitoriedad.IsMatch(Desnuda(File.ReadAllText(f))))
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList();

        var sinDeclarar = infractores.Where(n => !Censo.ContainsKey(n)).ToList();
        Assert.True(
            sinDeclarar.Count == 0,
            "Estos clientes deducen del CÓDIGO DE ESTADO si un rechazo se puede reintentar: "
            + string.Join(", ", sinDeclarar)
            + ". Esa tabla es de la capacidad, no del CMS — se lee "
            + "`RechazoDelArbolDeServicios.EsTransitorio`, que sale de la bandera `transient` "
            + "del ProblemDetails. Repetirla acá es una segunda verdad que se desincroniza el "
            + "día que una capacidad cambie con qué código sale un rechazo transitorio, y lo "
            + "haría sin que nada se pusiera rojo (#129).");

        var sobran = Censo.Keys.Where(n => !infractores.Contains(n, StringComparer.Ordinal)).ToList();
        Assert.True(
            sobran.Count == 0,
            "Estas entradas del censo ya no corresponden: " + string.Join(", ", sobran)
            + ". Quitalas en el mismo commit — un censo que sigue afirmando un defecto arreglado "
            + "es como un defecto sobrevive a su propio gate.");
    }

    [Fact]
    public void La_bandera_transient_la_declara_UN_solo_fichero()
    {
        var declarantes = FuentesDelWeb()
            .Where(f => BanderaDelContrato.IsMatch(Desnuda(File.ReadAllText(f))))
            .Select(f => Path.GetFileName(f)!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            declarantes.Count == 1 && declarantes[0] == "RechazoDelArbolDeServicios.cs",
            "La bandera `transient` tiene que estar declarada en EXACTAMENTE un fichero del CMS "
            + "—`Services/RechazoDelArbolDeServicios.cs`— y hoy la declaran: "
            + (declarantes.Count == 0 ? "ninguno" : string.Join(", ", declarantes))
            + ". Con dos declaraciones hay dos formas de leer el mismo dato y ninguna las cruza; "
            + "con cero, el patrón de este gate se pudrió y no está vigilando nada (#129).");
    }
}
