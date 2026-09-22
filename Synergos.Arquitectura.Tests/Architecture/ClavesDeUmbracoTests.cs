using System.Text.Json;

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
/// </remarks>
public sealed class ClavesDeUmbracoTests
{
    private static string Esquema()
        => Proyectos.Dir("Synergos.CMS.Web", "appsettings-schema.Umbraco.Cms.json");

    /// <summary>
    /// Los <c>appsettings*.json</c> de configuración — <c>internal</c> porque los consume también
    /// <c>PerfilDeProduccionTests</c> (#150), y dos descubrimientos de la misma verdad se desvían.
    /// </summary>
    internal static IReadOnlyList<string> Appsettings()
        => Directory.EnumerateFiles(
                Proyectos.Dir("Synergos.CMS.Web"), "appsettings*.json", SearchOption.TopDirectoryOnly)
            // Los `appsettings-schema*.json` son EL schema, no configuración.
            .Where(p => !Path.GetFileName(p).StartsWith("appsettings-schema", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    internal static JsonDocument Abrir(string ruta)
        => JsonDocument.Parse(
            File.ReadAllText(ruta),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

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

            Descender(nuestro, nodoUmbraco, "Umbraco", Path.GetFileName(fichero));
        }

        void Descender(JsonElement nuestro, JsonElement esq, string ruta, string fichero)
        {
            // Si el schema no sabe describir este nivel, se PARA — no se declara que sobra.
            if (!Hijos(esq, raiz, out var props)) return;
            if (nuestro.ValueKind != JsonValueKind.Object) return;

            foreach (var p in nuestro.EnumerateObject())
            {
                var suya = $"{ruta}:{p.Name}";
                cruzadas++;

                if (!props.TryGetProperty(p.Name, out var hijoEsq))
                {
                    // El dato que hace útil el mensaje NO es «no existe», es DÓNDE sí existe:
                    // sin eso, quien lo lea tiene que buscar la sección buena a mano.
                    malas.Add($"{fichero} → {suya}{Donde(p.Name, nodoUmbraco, "Umbraco")}");
                    continue;
                }

                if (p.Value.ValueKind == JsonValueKind.Object) Descender(p.Value, hijoEsq, suya, fichero);
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
