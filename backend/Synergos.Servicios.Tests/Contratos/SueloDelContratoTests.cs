using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// El suelo del contrato publicado: lo que un documento tiene que decir para SERVIR de contrato,
/// y que regenerarlo no arregla (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>Por qué no basta la deriva.</b> <c>ContratoOpenApiTests</c> compara el código con el
/// fichero, así que da por bueno un documento empobrecido si alguien lo regenera: un
/// <c>(IResult)</c> en <c>GetPrice</c> seguido de <c>SYNERGOS_ACTUALIZAR_CONTRATOS=1</c> vuelve a
/// verde con la respuesta sin esquema. Y el UI genera sus tipos de este documento, y el gate de
/// compatibilidad de los orquestadores lo usa de verdad: un documento pobre les deja a los dos
/// un verde que no dice nada.</para>
///
/// <para><b>Lee el COMITEADO</b>, que es el que consumen; la deriva garantiza que es el del
/// código. Cada regla nombra lo que la rompe, en una sola lista por pieza.</para>
/// </remarks>
public sealed class SueloDelContratoTests
{
    public static TheoryData<string> Piezas() => ContratoOpenApiTests.Piezas();

    private static readonly Regex SufijoNumerico = new(@"\d+$", RegexOptions.Compiled);

    [Theory]
    [MemberData(nameof(Piezas))]
    public void El_documento_cumple_el_suelo_del_contrato(string ensamblado)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        var ops = ContratoOpenApi.Operaciones(doc);
        var malas = new List<string>();

        Assert.True(ops.Count >= 4, $"{ensamblado}: se leyeron {ops.Count} operaciones; el documento o su lectura están rotos.");

        // 1. Toda operación tiene nombre, y no se repite: sin operationId el generador del UI deja
        //    la tabla de operaciones vacía, y la puerta (F3) llama por operación.
        foreach (var op in ops.Where(o => string.IsNullOrWhiteSpace((string?)o.Op["operationId"])))
        {
            malas.Add($"{op}: sin operationId (falta .WithName en el endpoint).");
        }

        foreach (var g in ops.GroupBy(o => (string?)o.Op["operationId"]).Where(g => g.Key is not null && g.Count() > 1))
        {
            malas.Add($"operationId «{g.Key}» repetido en {string.Join(", ", g)}.");
        }

        // 2. Toda operación tiene UN éxito con cuerpo JSON y esquema: es lo que el consumidor lee.
        //    Un handler sin tipo de retorno sale «200 OK» sin nada.
        foreach (var op in ops)
        {
            var exitos = (op.Op["responses"]?.AsObject() ?? new JsonObject())
                .Where(r => r.Key.StartsWith('2'))
                .ToList();
            var conEsquema = exitos.Count(r => r.Value?["content"]?["application/json"]?["schema"] is not null);
            if (conEsquema != 1)
            {
                malas.Add($"{op}: {conEsquema} respuestas 2xx con application/json y esquema, y tiene que ser una " +
                          "(¿el endpoint devuelve IResult en vez de su tipo?).");
            }
        }

        // 3. Ningún número que también sea cadena: daría `number | string` en el UI y dejaría
        //    ciego al gate de compatibilidad ante un cambio de tipo.
        foreach (var (ruta, nodo) in Nodos(doc, "#"))
        {
            if (nodo["type"] is JsonArray tipos)
            {
                var t = tipos.Select(x => (string?)x).ToHashSet(StringComparer.Ordinal);
                if ((t.Contains("number") || t.Contains("integer")) && t.Contains("string"))
                {
                    malas.Add($"{ruta}: un número que también es cadena ({string.Join("|", t)}).");
                }
            }
        }

        // 4. Ningún id de esquema con sufijo numérico. ASP.NET 10.0.12 no sufija dos tipos con el
        //    mismo nombre: colisiona en silencio, y eso lo vuelve rojo la generación misma
        //    (ContratoOpenApi, CreateSchemaReferenceId). Esto queda para la versión que sí sufije
        //    —X y X2 según el orden—, donde el nombre dejaría de ser estable.
        var esquemas = doc["components"]?["schemas"]?.AsObject() ?? new JsonObject();
        foreach (var id in esquemas.Select(e => e.Key).Where(k => SufijoNumerico.IsMatch(k)))
        {
            malas.Add($"components.schemas.{id}: id con sufijo numérico (dos tipos con el mismo nombre).");
        }

        // 5. El rechazo y la llave compartida, presentes: sin ellos el consumidor no sabe qué forma
        //    tiene un no, ni que hace falta la llave.
        if (esquemas[ContratoOpenApi.EsquemaRechazo] is not JsonObject rechazo
            || rechazo["properties"]?["code"] is null || rechazo["properties"]?["transient"] is null)
        {
            malas.Add($"components.schemas.{ContratoOpenApi.EsquemaRechazo}: falta, o no trae code y transient.");
        }

        foreach (var op in ops)
        {
            var r401 = op.Op["responses"]?["401"];
            if (r401 is null || r401["content"] is not null)
            {
                malas.Add($"{op}: sin 401, o con cuerpo (SharedKeyAuth contesta 401 sin cuerpo).");
            }
        }

        if (!ops.Any(o => (o.Op["responses"]?.AsObject() ?? new JsonObject())
                .Any(r => r.Value?["content"]?["application/problem+json"] is not null)))
        {
            malas.Add("Ninguna operación publica un rechazo: ¿se perdió el transformer?");
        }

        Assert.True(malas.Count == 0,
            $"{ensamblado}.json no cumple el suelo del contrato. Regenerarlo no lo arregla: lo arregla el " +
            $"código (tipo de retorno, WithName, los records).{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    /// <summary>Todos los objetos JSON del documento, con su ruta, para las reglas que miran esquemas.</summary>
    private static IEnumerable<(string Ruta, JsonObject Nodo)> Nodos(JsonNode? nodo, string ruta)
    {
        switch (nodo)
        {
            case JsonObject o:
                yield return (ruta, o);
                foreach (var (k, v) in o)
                {
                    foreach (var x in Nodos(v, $"{ruta}/{k}")) yield return x;
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    foreach (var x in Nodos(a[i], $"{ruta}/{i}")) yield return x;
                }
                break;
        }
    }
}
