using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Toda clave bajo <c>Umbraco:</c> está donde el schema del vendedor la declara (#138).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>appsettings.Development.json</c> y <c>appsettings.Docker.json</c>
/// declaraban <c>Umbraco:CMS:Global:UmbracoApplicationUrl</c>, y esa clave vive en
/// <c>WebRouting</c>: <c>Global</c> tiene dieciséis propiedades y ninguna es ésa. El binder de
/// .NET descarta lo que no mapea <b>sin un aviso</b>, así que Umbraco se comportaba como si nadie
/// la hubiera configurado y el <c>KeepAliveJob</c> escribía «No umbracoApplicationUrl for service
/// (yet), skip» <b>cada minuto</b>. Lo mismo con <c>Umbraco:CMS:RuntimeMode</c>, que va en
/// <c>Runtime:Mode</c>.</para>
///
/// <para><b>Es la familia que el repo ya pagó con el compose de <c>Api.Identity</c></b> (§11):
/// «nombraba <c>Identity__Tokens__*</c> de cuando la sección era propia, así que la llave llegaba
/// a una sección que nadie lee y un servidor bien configurado se comportaba como uno sin llave».
/// La diferencia acá es que <b>el vendedor nos da la verdad</b>: el schema de Umbraco viene en el
/// repo con sus 245 propiedades y ya sabe dónde va cada clave, así que no hay lista que escribir
/// ni que mantener.</para>
///
/// <para><b>Y por qué no lo veía nadie:</b> el <c>$schema</c> que esos ficheros declaran lo usa el
/// IDE para autocompletar, y <b>un IDE no falla un build</b>. Es #133 otra vez — un artefacto que
/// mira una herramienta que no usamos para verificar, así que su deriva no aparece en ninguna
/// corrida.</para>
///
/// <para><b>Se cruza por RUTA y no por nombre, y ese corte costó su mutación.</b> El primer intento
/// recolectaba los 245 nombres de propiedad del schema y comprobaba pertenencia: <b>pasó en verde
/// con el defecto puesto</b>, porque <c>UmbracoApplicationUrl</c> SÍ es una propiedad que el schema
/// conoce — sólo que de otro padre. Hay que resolver los <c>$ref</c> y descender el árbol a la par
/// que el JSON de configuración. Es el mismo error que el regex de #120 (veía el caso bonito) y el
/// <c>Contains</c> del addendum #14 (medía que el fichero contuviera la pieza).</para>
///
/// <para><b>Sólo se juzga lo que el schema SABE describir.</b> Si una sección de nuestra
/// configuración no tiene <c>properties</c> resolubles —un diccionario abierto, un
/// <c>additionalProperties</c>— se deja de descender ahí en vez de inventar que sobra: un gate que
/// se cree más listo de lo que es acaba con un muro de excepciones y deja de leerse.</para>
///
/// <para><b>Addendum #159 — y el #138 se quedó corto justo donde su propio gate no llegaba.</b>
/// Esto miraba <c>appsettings*.json</c> y nada más, así que arregló las dos copias que podía ver y
/// dejó vivas las otras dos: <c>compose.prod.yml</c> y <c>docker-compose.yml</c> siguieron pasando
/// <c>Umbraco__CMS__Global__UmbracoApplicationUrl</c> durante meses. <b>Una variable
/// <c>Umbraco__A__B</c> es la clave <c>Umbraco:A:B</c> por otro transporte</b>, y el binder la
/// descarta igual de callado — así que en producción toda URL absoluta que Umbraco construía
/// apuntaba a <c>http://localhost:8080/</c>, el valor del perfil, con el <c>KeepAliveJob</c>
/// escribiendo «No umbracoApplicationUrl for service (yet), skip» cada minuto. El agravante:
/// <c>ComposeStackTests</c> y <c>DeployPipelineTests</c> <b>sí</b> leen esos ficheros, y ninguno
/// cruzaba sus claves contra el schema — cada gate miraba lo suyo y «qué sección lee cada clave»
/// no era de nadie.</para>
///
/// <para><b>Los dos sujetos comparten UN caminante</b> (<see cref="Ubicar"/>). Escribir un segundo
/// cruce para los compose sería <c>feedback_the_same_algorithm_is_not_the_same_thing</c>: dos
/// criterios para la misma verdad, y el día que uno se afine el otro miente.</para>
///
/// <para><b>Y las claves del compose NO se sacan con un matcher de bloques.</b> El
/// <c>environment:</c> es YAML con sangría significativa, y un regex que delimite ese bloque tiene
/// puntos ciegos que no dejan hueco: dejan al gate midiendo un subconjunto y diciendo que miró todo
/// — es el <c>\s*</c> que costó <c>ComposeStackTests</c> (32 de 58 claves) en el #152. Se busca el
/// TOKEN <c>Umbraco__…</c> donde sea: una variable de configuración se llama igual esté en el bloque
/// que esté, así que no hay bloque que delimitar.</para>
/// </remarks>
[Collection(ComposeExclusivo.Nombre)]
public sealed class ClavesDeUmbracoTests
{
    private static string Esquema()
        => Proyectos.Dir("Synergos.CMS.Web", "appsettings-schema.Umbraco.Cms.json");

    private static IReadOnlyList<string> Appsettings()
        => Directory.EnumerateFiles(
                Proyectos.Dir("Synergos.CMS.Web"), "appsettings*.json", SearchOption.TopDirectoryOnly)
            // Los `appsettings-schema*.json` son EL schema, no configuración.
            .Where(p => !Path.GetFileName(p).StartsWith("appsettings-schema", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static JsonDocument Abrir(string ruta)
        => JsonDocument.Parse(
            File.ReadAllText(ruta),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    /// <summary>
    /// Los ficheros que le pasan configuración de Umbraco al contenedor por variable de entorno
    /// (#159): los dos compose y el generador del de producción.
    /// </summary>
    /// <remarks>
    /// El generador entra <b>además</b> del fichero que produce, y no es redundante: arreglar sólo
    /// <c>compose.prod.yml</c> deja la clave mala en quien lo reescribe, así que el defecto vuelve en
    /// la siguiente regeneración y el rojo lo paga otro.
    /// </remarks>
    private static IReadOnlyList<string> Composes()
        => new[]
            {
                Proyectos.Ruta("compose.prod.yml"),
                Proyectos.Ruta("docker-compose.yml"),
                Proyectos.Ruta("tools", "compose-gen.mjs"),
            }
            .Where(File.Exists)
            .ToList();

    /// <summary>
    /// Quita las líneas de comentario —<c>#</c> de YAML y <c>//</c> de JS— antes de buscar claves.
    /// </summary>
    /// <remarks>
    /// <para><b>Sólo líneas ENTERAS</b>, nunca un marcador a media línea: un <c>#</c> suelto puede
    /// ser parte de un valor, y recortar por <c>//</c> en cualquier posición partiría cada
    /// <c>https://…</c> del fichero.</para>
    ///
    /// <para><b>Y acá sostiene el gate, medido en los dos sentidos.</b> Los tres ficheros explican en
    /// un comentario por qué la clave va en <c>WebRouting</c> y <b>nombran la mala</b>, que es lo que
    /// hace útil la nota para quien la lea. Sin este barrido el gate se pone rojo acusando justo a
    /// los ficheros que documentan el arreglo —un falso positivo— y un gate que se cae por su propia
    /// explicación enseña a ignorarlo. Comprobado apagándolo: 3 acusaciones, las tres de prosa.</para>
    /// </remarks>
    private static string SinComentarios(string fuente)
        => string.Join(
            '\n',
            fuente.Split('\n')
                .Where(l =>
                {
                    var t = l.TrimStart();
                    return !t.StartsWith('#') && !t.StartsWith("//", StringComparison.Ordinal);
                }));

    /// <summary>
    /// Sigue <c>$ref</c> y los <c>oneOf</c>/<c>allOf</c> de un solo miembro hasta el nodo real.
    /// </summary>
    /// <remarks>
    /// El schema de Umbraco envuelve casi todo: una sección es un <c>$ref</c> a
    /// <c>#/definitions/XSettings</c>, y una propiedad de enum es un <c>oneOf</c> de uno. Sin
    /// desenvolver, el descenso se para en el primer nivel y el gate <b>no mira nada</b>.
    /// </remarks>
    private static JsonElement Resolver(JsonElement nodo, JsonElement raiz)
    {
        for (var vuelta = 0; vuelta < 10; vuelta++)
        {
            if (nodo.ValueKind != JsonValueKind.Object) return nodo;

            if (nodo.TryGetProperty("$ref", out var r) && r.GetString() is { } puntero)
            {
                var actual = raiz;
                foreach (var tramo in puntero.TrimStart('#', '/').Split('/'))
                {
                    if (!actual.TryGetProperty(tramo, out actual)) return nodo;
                }

                nodo = actual;
                continue;
            }

            foreach (var envoltorio in new[] { "oneOf", "allOf" })
            {
                if (nodo.TryGetProperty(envoltorio, out var lista)
                    && lista.ValueKind == JsonValueKind.Array
                    && lista.GetArrayLength() == 1)
                {
                    nodo = lista[0];
                    goto siguiente;
                }
            }

            return nodo;

        siguiente: ;
        }

        return nodo;
    }

    private static bool Hijos(JsonElement nodo, JsonElement raiz, out JsonElement props)
    {
        props = default;
        var real = Resolver(nodo, raiz);
        return real.ValueKind == JsonValueKind.Object
            && real.TryGetProperty("properties", out props)
            && props.ValueKind == JsonValueKind.Object;
    }

    /// <summary>
    /// Camina una RUTA de configuración (<c>["CMS","Global","UmbracoApplicationUrl"]</c>) por el
    /// schema. Devuelve <c>null</c> si liga —o si el schema no sabe juzgar ese nivel— y el tramo que
    /// falla si no.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el ÚNICO criterio, y lo comparten los dos sujetos</b> —los <c>appsettings</c> y
    /// las variables de entorno de los compose (#159)—. Escribir un segundo cruce para el segundo
    /// transporte es cómo se acaba con dos verdades sobre la misma clave.</para>
    ///
    /// <para><b>Se camina por RUTA, no por nombre</b>, y ese corte es el que hace útil al gate: un
    /// cruce que recolecte los 245 nombres del schema y compruebe pertenencia <b>pierde</b>
    /// <c>UmbracoApplicationUrl</c>, porque es una propiedad que el schema sí conoce — sólo que de
    /// otro padre. Medido en el #138 con el defecto puesto: el cruce barato cazaba el inocuo
    /// (<c>RuntimeMode</c>, un nombre que no existe) y dejaba pasar el que tenía el síntoma vivo.</para>
    /// </remarks>
    private static string? Ubicar(IReadOnlyList<string> tramos, JsonElement nodoUmbraco, JsonElement raiz)
    {
        var esq = nodoUmbraco;
        var recorrido = "Umbraco";

        foreach (var tramo in tramos)
        {
            // Si el schema no sabe describir este nivel, se PARA — no se declara que sobra.
            if (!Hijos(esq, raiz, out var props)) return null;

            if (!props.TryGetProperty(tramo, out var hijo)) return $"{recorrido}:{tramo}";

            esq = hijo;
            recorrido = $"{recorrido}:{tramo}";
        }

        return null;
    }

    [Fact]
    public void Toda_clave_de_Umbraco_esta_donde_el_schema_la_declara()
    {
        using var esquema = Abrir(Esquema());
        var raiz = esquema.RootElement;

        Assert.True(Hijos(raiz, raiz, out var deLaRaiz),
            "El schema de Umbraco no declara `properties` en su raíz: revisar este gate.");
        Assert.True(deLaRaiz.TryGetProperty("Umbraco", out var nodoUmbraco),
            "El schema de Umbraco no declara la sección `Umbraco`: revisar este gate.");

        var ficheros = Appsettings();
        Assert.True(ficheros.Count >= 3,
            $"Sólo se encontraron {ficheros.Count} appsettings de configuración: revisar este gate.");

        var malas = new List<string>();
        var cruzadas = 0;

        foreach (var fichero in ficheros)
        {
            using var doc = Abrir(fichero);
            if (!doc.RootElement.TryGetProperty("Umbraco", out var nuestro)) continue;

            Descender(nuestro, [], Path.GetFileName(fichero));
        }

        // Recorre NUESTRO JSON juntando rutas; el veredicto lo da `Ubicar`, que es el mismo que
        // juzga las variables de entorno de los compose. Dos recorridos, un solo criterio.
        void Descender(JsonElement nuestro, List<string> tramos, string fichero)
        {
            if (nuestro.ValueKind != JsonValueKind.Object) return;

            foreach (var p in nuestro.EnumerateObject())
            {
                List<string> ruta = [.. tramos, p.Name];
                cruzadas++;

                if (Ubicar(ruta, nodoUmbraco, raiz) is { } fallo)
                {
                    // El dato que hace útil el mensaje NO es «no existe», es DÓNDE sí existe:
                    // sin eso, quien lo lea tiene que buscar la sección buena a mano.
                    malas.Add($"{fichero} → {fallo}{Donde(p.Name, nodoUmbraco, "Umbraco")}");
                    continue;
                }

                if (p.Value.ValueKind == JsonValueKind.Object) Descender(p.Value, ruta, fichero);
            }
        }

        // Red de seguridad: si el descenso deja de ver, el cruce pasaría en verde sin mirar nada —
        // el fallo silencioso que este repo ya tiene escrito cuatro veces (#118, #133, #136).
        Assert.True(cruzadas > 40,
            $"Sólo se cruzaron {cruzadas} claves de Umbraco. El schema envuelve casi todo en `$ref`, "
            + "así que un descenso que no los resuelva se para en el primer nivel y este gate no "
            + "mira nada. Revisarlo antes que nada.");

        Assert.True(malas.Count == 0,
            "Estas claves de configuración de Umbraco NO están donde el schema del vendedor las "
            + "declara:\n  " + string.Join("\n  ", malas)
            + "\n\nEl binder de .NET descarta en SILENCIO lo que no mapea, así que la clave llega a "
            + "una sección que nadie lee y el servidor se comporta como uno sin configurar — es lo "
            + "que dejó el aviso «No umbracoApplicationUrl for service (yet), skip» cada minuto "
            + "durante meses, y lo mismo que costó la llave de `Api.Identity` en el compose (#138, "
            + "§11). El schema vive en `Synergos.CMS.Web/appsettings-schema.Umbraco.Cms.json`.");
    }

    /// <summary>
    /// Y lo mismo para las variables <c>Umbraco__…</c> que los compose le pasan al contenedor (#159).
    /// </summary>
    /// <remarks>
    /// <para>Va en un <c>[Fact]</c> aparte del de los <c>appsettings</c> —mismo criterio, sujeto
    /// distinto— para que el rojo nombre el transporte: «lo que teclea el operador» y «lo que lleva
    /// el despliegue» se arreglan en ficheros distintos y a veces sólo uno de los dos está mal.</para>
    ///
    /// <para><b>La red de seguridad va sobre las CLAVES encontradas y no sobre los ficheros.</b> Los
    /// tres ficheros pueden existir y el extractor devolver cero —un <c>__</c> que cambie, un
    /// <c>SinComentarios</c> que se coma de más— y entonces «ninguna está mal» es cierto y vacío. Es
    /// el verde sobre la lista vacía que este repo ya pagó cuatro veces.</para>
    /// </remarks>
    [Fact]
    public void Toda_variable_de_Umbraco_de_los_compose_esta_donde_el_schema_la_declara()
    {
        using var esquema = Abrir(Esquema());
        var raiz = esquema.RootElement;

        Assert.True(Hijos(raiz, raiz, out var deLaRaiz) && deLaRaiz.TryGetProperty("Umbraco", out _),
            "El schema de Umbraco no declara la sección `Umbraco`: revisar este gate.");
        deLaRaiz.TryGetProperty("Umbraco", out var nodoUmbraco);

        var ficheros = Composes();
        Assert.True(ficheros.Count == 3,
            $"Se esperaban 3 ficheros que pasen configuración por entorno y se encontraron "
            + $"{ficheros.Count}. Si uno se renombró o se movió, este gate dejó de mirarlo: "
            + "actualizar `Composes()` en vez de bajar esta cuenta.");

        var malas = new List<string>();
        var vistas = 0;

        foreach (var fichero in ficheros)
        {
            var fuente = SinComentarios(File.ReadAllText(fichero));
            var nombre = Path.GetFileName(fichero);

            foreach (var variable in Regex
                         .Matches(fuente, @"\bUmbraco(?:__[A-Za-z0-9]+)+\b")
                         .Select(m => m.Value)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(v => v, StringComparer.Ordinal))
            {
                vistas++;

                // `Umbraco__A__B` ES `Umbraco:A:B` — el doble guión bajo es el escape de .NET para
                // el separador de secciones, así que la traducción es mecánica y el criterio el mismo.
                var tramos = variable.Split("__", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

                if (Ubicar(tramos, nodoUmbraco, raiz) is { } fallo)
                {
                    malas.Add($"{nombre} → {variable}   (= {fallo}){Donde(tramos[^1], nodoUmbraco, "Umbraco")}");
                }
            }
        }

        Assert.True(vistas >= 3,
            $"Sólo se encontraron {vistas} variables `Umbraco__*` en los compose. El extractor dejó "
            + "de ver, y un cruce sobre una lista vacía informa «ninguna está mal» sin haber mirado "
            + "nada. Revisarlo antes que nada.");

        Assert.True(malas.Count == 0,
            "Estas variables de entorno de los compose NO están donde el schema del vendedor declara "
            + "su clave:\n  " + string.Join("\n  ", malas)
            + "\n\nUna variable `Umbraco__A__B` es la clave `Umbraco:A:B`, y el binder de .NET la "
            + "descarta en SILENCIO igual que en un `appsettings`: el contenedor arranca sano y se "
            + "comporta como uno sin configurar. Así vivió meses `Global__UmbracoApplicationUrl` en "
            + "los dos compose —#138 arregló los `appsettings` y su gate no podía leer esto— con "
            + "toda URL absoluta de PRODUCCIÓN apuntando a `http://localhost:8080/` (#159).");
    }

    /// <summary>
    /// Busca en qué sección del schema SÍ vive una propiedad, para decirlo en el mensaje.
    /// </summary>
    /// <remarks>
    /// Es la mitad que vuelve accionable el rojo. «<c>Global:UmbracoApplicationUrl</c> no existe» y
    /// «va en <c>WebRouting</c>» cuestan lo mismo de calcular y una de las dos ahorra la búsqueda —
    /// la misma razón por la que el mensaje del #132 nombra las dos salidas en vez de sólo lanzar.
    /// </remarks>
    private static string Donde(string propiedad, JsonElement desde, string ruta)
    {
        // El schema viene una vez por proceso, pero esta búsqueda sólo corre al fallar.
        using var esquema = Abrir(Esquema());
        var raiz = esquema.RootElement;
        var encontradas = new List<string>();

        void Buscar(JsonElement nodo, string aqui, int hondo)
        {
            if (hondo > 4 || !Hijos(nodo, raiz, out var props)) return;
            foreach (var p in props.EnumerateObject())
            {
                if (p.Name == propiedad) encontradas.Add($"{aqui}:{p.Name}");
                else Buscar(p.Value, $"{aqui}:{p.Name}", hondo + 1);
            }
        }

        if (Hijos(raiz, raiz, out var deLaRaiz) && deLaRaiz.TryGetProperty("Umbraco", out var u))
        {
            Buscar(u, ruta, 0);
        }

        return encontradas.Count == 0
            ? "   (el schema no la declara en ninguna sección)"
            : $"   → va en {string.Join(" o ", encontradas)}";
    }
}
