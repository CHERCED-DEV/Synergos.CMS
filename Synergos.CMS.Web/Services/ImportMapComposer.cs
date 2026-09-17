using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Junta los import maps de varios frameworks en el único que el navegador va a leer.
/// </summary>
/// <remarks>
/// <para><b>Por qué hay que componer, y no elegir</b> (#127). El navegador lee <b>el primer</b>
/// <c>&lt;script type="importmap"&gt;</c> de la página y <b>ignora los siguientes</b>: no se
/// acumulan, no se fusionan, y uno presente no se corrige con otro después. Así que una página con
/// un elemento de Angular y uno de React tiene un solo mapa para los dos — y si ese mapa es el de
/// uno, el otro no resuelve sus bare specifiers y <b>no hidrata</b>, con el SSR entero en pantalla
/// y nada fallando.</para>
///
/// <para><b>Se componen TODOS los frameworks que el registry declara, no los de la página.</b>
/// Parece derrochador y no lo es: un import map sólo <i>declara</i> correspondencias — el
/// navegador no descarga nada hasta que un módulo importa. Las entradas de un framework que la
/// página no usa cuestan unos cientos de bytes de HTML y <b>cero peticiones</b>. Componer «lo que
/// esta página usa» exigiría saber qué elementos trae antes de renderizar el <c>&lt;head&gt;</c>,
/// que es justo lo que no se sabe.</para>
///
/// <para><b>Y la lista de frameworks se DERIVA del registry</b> —de las claves de
/// <c>implementations</c> de cada elemento— en vez de escribirse a mano. Una lista a mano es como
/// <c>compose-gen.mjs</c> acabó repartiendo la llave de identidad a dos capacidades cuando eran
/// cuatro: el despliegue bien configurado comportándose como uno roto.</para>
/// </remarks>
internal static class ImportMapComposer
{
    /// <summary>
    /// Los frameworks que el registry declara, en orden estable.
    /// </summary>
    /// <remarks>
    /// Orden estable a propósito: sin él, dos réplicas emiten el mismo mapa con las claves en
    /// distinto orden y cualquier caché intermedia guarda dos copias de lo mismo. Es la misma razón
    /// por la que el almacén de las capacidades dejó de fingir que un directorio tiene orden (#112).
    /// </remarks>
    public static IReadOnlyList<string> FrameworksDeclarados(IEnumerable<IEnumerable<string>> porElemento)
        => porElemento
            .SelectMany(x => x)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Funde los mapas de cada framework en uno. Devuelve el conflicto si dos declaran el mismo
    /// specifier apuntando a sitios distintos.
    /// </summary>
    /// <remarks>
    /// <para><b>El mismo specifier con la MISMA URL no es un conflicto</b>, es lo normal: <c>rxjs</c>
    /// puede salir del mismo sitio para dos frameworks. Se deduplica y ya.</para>
    ///
    /// <para><b>Con URLs distintas no hay salida buena y por eso se para.</b> Elegir uno en silencio
    /// deja al otro framework cargando el runtime equivocado, y eso no se lee como «el mapa está
    /// mal»: se lee como «ese elemento está roto», que manda a alguien a depurar el elemento. Es la
    /// misma decisión que <c>PaymentEngineCoexistenceTests</c> toma con dos motores de cobro vivos
    /// — cuando dos piezas se contradicen sobre un hecho, servir una de las dos es peor que parar.</para>
    /// </remarks>
    public static (ImportMap? Mapa, string? Conflicto) Componer(
        IReadOnlyList<(string Framework, IReadOnlyDictionary<string, string> Imports)> mapas)
    {
        var fundido = new Dictionary<string, string>(StringComparer.Ordinal);
        var quienLoPuso = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (framework, imports) in mapas)
        {
            foreach (var (specifier, url) in imports)
            {
                if (!fundido.TryGetValue(specifier, out var yaPuesta))
                {
                    fundido[specifier] = url;
                    quienLoPuso[specifier] = framework;
                    continue;
                }

                if (string.Equals(yaPuesta, url, StringComparison.Ordinal)) continue;

                return (null,
                    $"El specifier «{specifier}» sale de dos sitios distintos: «{framework}» lo "
                    + $"apunta a «{url}» y «{quienLoPuso[specifier]}» a «{yaPuesta}». El navegador "
                    + "lee UN solo import map, así que servir cualquiera de los dos deja al otro "
                    + "framework cargando el runtime equivocado — y eso se lee como «ese elemento "
                    + "está roto», no como un mapa mal compuesto. Se para hasta que los dos "
                    + "publiquen el mismo destino para ese specifier.");
            }
        }

        // Orden estable por la razón del <remarks> de FrameworksDeclarados.
        var ordenado = fundido
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        return (new ImportMap(ordenado), null);
    }
}
