using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Lo que el contrato publicado declara A MANO se cruza con el host real (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>Tres cosas del documento no salen del tipo de retorno</b>, y por eso pueden mentir sin
/// que la deriva ni el suelo lo vean: la cabecera <c>Idempotency-Key</c> (sale de un metadato que el
/// endpoint declara), el esquema <c>Rechazo</c> (lo escribe el transformer) y el 401 de la llave
/// compartida (también). Cada una tiene aquí su diente contra el host de verdad, que es lo único
/// que no puede mentir.</para>
///
/// <para><b>La sonda manda CADA operación del documento</b> —sin <c>Idempotency-Key</c>, con
/// <c>{}</c> de cuerpo si la operación lo lleva y con <c>sonda</c> en cada parámetro de ruta— y
/// juzga lo que vuelve. Funciona porque la llave se resuelve antes que cualquier regla (CLAUDE.md
/// §0.B.16): un endpoint que la exige contesta <c>*.idempotency_key_required</c> antes de mirar el
/// cuerpo o el id.</para>
/// </remarks>
public sealed class SondasDelContratoTests
{
    public static TheoryData<string> Piezas() => ContratoOpenApiTests.Piezas();

    private sealed record Respuesta(OperacionPublicada Op, HttpStatusCode Status, string? Tipo, string Cuerpo);

    private static async Task<IReadOnlyList<Respuesta>> Sondear(string ensamblado, bool conLlaveCompartida)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        using var host = ContratoOpenApi.Pieza(ensamblado).Levantar(conContrato: false);
        var respuestas = new List<Respuesta>();

        foreach (var op in ContratoOpenApi.Operaciones(doc))
        {
            var ruta = System.Text.RegularExpressions.Regex.Replace(op.Ruta, @"\{[^}]+\}", "sonda");
            using var req = new HttpRequestMessage(new HttpMethod(op.Metodo), new Uri(ruta, UriKind.Relative));
            if (conLlaveCompartida) req.Headers.Add(SharedKeyAuth.HeaderName, PiezaPublicada.Llave);
            if (op.Op["requestBody"] is not null) req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var r = await host.Cliente.SendAsync(req);
            respuestas.Add(new Respuesta(op, r.StatusCode, r.Content.Headers.ContentType?.MediaType, await r.Content.ReadAsStringAsync()));
        }

        Assert.True(respuestas.Count >= 4, $"{ensamblado}: se sondearon {respuestas.Count} operaciones.");
        return respuestas;
    }

    /// <summary>
    /// Lo que el documento dice de <c>Idempotency-Key</c> es lo que el endpoint exige, en los dos
    /// sentidos.
    /// </summary>
    /// <remarks>
    /// Declarada como requerida ⇔ sin ella contesta <c>400 *.idempotency_key_required</c>. Una
    /// declarada opcional (el ajuste de existencias: sólo el relativo la exige) no puede exigirla con
    /// un cuerpo vacío. Quitar el metadato de un endpoint que la lee, o ponérselo a uno que no la lee,
    /// sigue en rojo después de regenerar.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task La_llave_que_el_documento_declara_es_la_que_el_endpoint_exige(string ensamblado)
    {
        var malas = new List<string>();

        foreach (var r in await Sondear(ensamblado, conLlaveCompartida: true))
        {
            var declarada = (r.Op.Op["parameters"]?.AsArray() ?? new JsonArray())
                .Any(p => (string?)p?["in"] == "header" && (string?)p?["name"] == IdempotencyHeader.Name
                          && (bool?)p?["required"] == true);

            var exigida = r.Status == HttpStatusCode.BadRequest
                && Codigo(r.Cuerpo) is { } code
                && code.EndsWith(".idempotency_key_required", StringComparison.Ordinal);

            if (declarada != exigida)
            {
                malas.Add($"  {r.Op}: el documento dice que {(declarada ? "la EXIGE" : "no la exige")} y sin ella " +
                          $"contesta {(int)r.Status} {Codigo(r.Cuerpo) ?? "(sin code)"}.");
            }
        }

        Assert.True(malas.Count == 0,
            $"{ensamblado}: la cabecera {IdempotencyHeader.Name} que publica el contrato no es la que el " +
            "endpoint exige. Se declara con .ConLlaveDeIdempotencia() donde se lee con " +
            $"IdempotencyHeader.TryRead, y sólo ahí.{Environment.NewLine}{string.Join(Environment.NewLine, malas)}");
    }

    /// <summary>
    /// Todo rechazo REAL que contestan las sondas está publicado en su operación, y su cuerpo cumple
    /// el esquema <c>Rechazo</c> del documento.
    /// </summary>
    /// <remarks>
    /// El esquema está escrito a mano en el transformer, así que esto es su diente: renombrar
    /// <c>code</c> en <c>ToProblem</c> y regenerar sigue en rojo aquí. Los 400/415 del framework
    /// (JSON malformado, otro Content-Type) NO traen <c>Rechazo</c>, y las sondas no los provocan: es
    /// lo que el README de contratos dice que el documento no cubre.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task Un_rechazo_real_cumple_el_esquema_Rechazo(string ensamblado)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        var esquema = doc["components"]?["schemas"]?[ContratoOpenApi.EsquemaRechazo]?.AsObject();
        Assert.NotNull(esquema);

        var malas = new List<string>();
        var vistos = 0;

        foreach (var r in (await Sondear(ensamblado, conLlaveCompartida: true)).Where(r => (int)r.Status >= 400))
        {
            vistos++;
            var status = ((int)r.Status).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var publicada = r.Op.Op["responses"]?[status]?["content"]?["application/problem+json"]?["schema"]?["$ref"];
            if ((string?)publicada != $"#/components/schemas/{ContratoOpenApi.EsquemaRechazo}")
            {
                malas.Add($"  {r.Op}: contesta {status} y la operación no lo publica con {ContratoOpenApi.EsquemaRechazo}.");
                continue;
            }

            if (r.Tipo != "application/problem+json")
            {
                malas.Add($"  {r.Op}: el {status} sale como «{r.Tipo}» y no como application/problem+json.");
                continue;
            }

            malas.AddRange(Cumple(JsonNode.Parse(r.Cuerpo)?.AsObject(), esquema!, (int)r.Status)
                .Select(m => $"  {r.Op} ({status}): {m}"));
        }

        Assert.True(vistos >= 2, $"{ensamblado}: las sondas sólo produjeron {vistos} rechazos; no prueban nada.");
        Assert.True(malas.Count == 0,
            $"{ensamblado}: un rechazo real no es lo que el contrato publica.{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas));
    }

    /// <summary>
    /// Sin <c>X-Synergos-Key</c>, toda operación contesta 401 SIN cuerpo, que es lo que el documento
    /// publica.
    /// </summary>
    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task Sin_la_llave_compartida_toda_operacion_responde_401_sin_cuerpo(string ensamblado)
    {
        var malas = (await Sondear(ensamblado, conLlaveCompartida: false))
            .Where(r => r.Status != HttpStatusCode.Unauthorized || r.Cuerpo.Length > 0
                     || r.Op.Op["responses"]?["401"] is null || r.Op.Op["responses"]?["401"]?["content"] is not null)
            .Select(r => $"  {r.Op}: {(int)r.Status} con {r.Cuerpo.Length} caracteres de cuerpo; el documento " +
                         $"publica el 401 {(r.Op.Op["responses"]?["401"]?["content"] is null ? "sin" : "con")} cuerpo.")
            .ToList();

        Assert.True(malas.Count == 0,
            $"{ensamblado}: sin la llave compartida el contrato promete 401 sin cuerpo.{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas));
    }

    private static string? Codigo(string cuerpo)
    {
        try { return (string?)JsonNode.Parse(cuerpo)?["code"]; }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>
    /// El cuerpo cumple el esquema <c>Rechazo</c>: sus requeridos, el tipo de cada propiedad, el
    /// <c>enum</c> de <c>title</c>, y un <c>status</c> igual al de la respuesta.
    /// </summary>
    private static IEnumerable<string> Cumple(JsonObject? cuerpo, JsonObject esquema, int status)
    {
        if (cuerpo is null)
        {
            yield return "el cuerpo no es un objeto JSON.";
            yield break;
        }

        foreach (var req in esquema["required"]?.AsArray().Select(x => (string)x!) ?? [])
        {
            if (!cuerpo.ContainsKey(req)) yield return $"falta «{req}», que {ContratoOpenApi.EsquemaRechazo} exige.";
        }

        foreach (var (nombre, prop) in esquema["properties"]?.AsObject() ?? new JsonObject())
        {
            if (cuerpo[nombre] is not JsonValue valor) continue;
            var tipo = (string?)prop?["type"];
            var bien = tipo switch
            {
                "string" => valor.GetValueKind() == JsonValueKind.String,
                "integer" => valor.GetValueKind() == JsonValueKind.Number,
                "boolean" => valor.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
            if (!bien) yield return $"«{nombre}» es {valor.GetValueKind()} y el esquema dice {tipo}.";

            if (prop?["enum"] is JsonArray opciones && !opciones.Any(o => (string?)o == valor.ToString()))
            {
                yield return $"«{nombre}» vale «{valor}», fuera del enum del esquema.";
            }
        }

        if ((int?)cuerpo["status"] is { } s && s != status)
        {
            yield return $"«status» dice {s} y la respuesta es {status}.";
        }
    }
}
