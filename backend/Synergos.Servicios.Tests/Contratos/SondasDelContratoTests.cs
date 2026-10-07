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
///
/// <para><b>Salvo la llave OPCIONAL</b>, que sólo se lee en un caso del cuerpo: con <c>{}</c> el
/// ajuste de existencias contesta <c>inventory.adjust_required</c> antes de mirarla, y «declarada
/// opcional» y «sin declarar» daban lo mismo (medido: quitarle el metadato dejaba todo en verde).
/// Para esas, <see cref="CuerpoQueLeeLaLlave"/> dice con qué cuerpo se llega a leerla.</para>
/// </remarks>
public sealed class SondasDelContratoTests
{
    public static TheoryData<string> Piezas() => ContratoOpenApiTests.Piezas();

    /// <summary>
    /// El cuerpo mínimo con que una operación llega a leer <c>Idempotency-Key</c> cuando no la lee
    /// siempre: el caso que la exige. Sólo las que lo necesitan.
    /// </summary>
    /// <remarks>
    /// Es una tabla escrita a mano a propósito, y pequeña: el documento no puede decir QUÉ caso del
    /// cuerpo activa la llave (OpenAPI no tiene cómo), así que lo dice la sonda, y lo comprueba contra
    /// el host en los dos sentidos. Una fila cuyo cuerpo no la activa es rojo, y una operación que el
    /// documento declara opcional sin fila aquí también.
    /// </remarks>
    private static readonly Dictionary<string, string> CuerpoQueLeeLaLlave = new(StringComparer.Ordinal)
    {
        // El ajuste RELATIVO la exige y el absoluto no: repetir «hay 47» no cambia nada (#30).
        ["AdjustStock"] = """{"delta":1}""",
    };

    private sealed record Respuesta(OperacionPublicada Op, HttpStatusCode Status, string? Tipo, string Cuerpo);

    private static async Task<IReadOnlyList<Respuesta>> Sondear(string ensamblado, bool conLlaveCompartida)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        using var host = ContratoOpenApi.Pieza(ensamblado).Levantar(conContrato: false);
        var respuestas = new List<Respuesta>();

        foreach (var op in ContratoOpenApi.Operaciones(doc))
        {
            respuestas.Add(await Enviar(host, op, conLlaveCompartida, llave: null));
        }

        Assert.True(respuestas.Count >= 4, $"{ensamblado}: se sondearon {respuestas.Count} operaciones.");
        return respuestas;
    }

    /// <summary>
    /// Manda UNA operación: <c>sonda</c> en cada parámetro de ruta, <paramref name="cuerpo"/> (o
    /// <c>{}</c>) si lleva cuerpo y, si se da, la <paramref name="llave"/> en <c>Idempotency-Key</c>.
    /// </summary>
    private static async Task<Respuesta> Enviar(
        HostDeLaPieza host, OperacionPublicada op, bool conLlaveCompartida, string? llave, string? cuerpo = null)
    {
        var ruta = System.Text.RegularExpressions.Regex.Replace(op.Ruta, @"\{[^}]+\}", "sonda");
        using var req = new HttpRequestMessage(new HttpMethod(op.Metodo), new Uri(ruta, UriKind.Relative));
        if (conLlaveCompartida) req.Headers.Add(SharedKeyAuth.HeaderName, PiezaPublicada.Llave);
        if (llave is not null) req.Headers.Add(IdempotencyHeader.Name, llave);
        if (op.Op["requestBody"] is not null || cuerpo is not null)
        {
            req.Content = new StringContent(cuerpo ?? "{}", Encoding.UTF8, "application/json");
        }

        using var r = await host.Cliente.SendAsync(req);
        return new Respuesta(op, r.StatusCode, r.Content.Headers.ContentType?.MediaType, await r.Content.ReadAsStringAsync());
    }

    /// <summary>La cabecera <c>Idempotency-Key</c> que declara la operación, si la declara.</summary>
    private static JsonObject? LlaveDeclarada(OperacionPublicada op)
        => (op.Op["parameters"]?.AsArray() ?? new JsonArray()).OfType<JsonObject>()
            .FirstOrDefault(p => (string?)p["in"] == "header" && (string?)p["name"] == IdempotencyHeader.Name);

    /// <summary>Si la respuesta es el rechazo de la llave: <c>400 *.idempotency_key_required</c>.</summary>
    private static bool PideLaLlave(Respuesta r)
        => r.Status == HttpStatusCode.BadRequest
           && Codigo(r.Cuerpo) is { } code
           && code.EndsWith(".idempotency_key_required", StringComparison.Ordinal);

    /// <summary>
    /// Lo que el documento dice de <c>Idempotency-Key</c> es lo que el endpoint exige, en los dos
    /// sentidos.
    /// </summary>
    /// <remarks>
    /// <para>Con <c>{}</c>: declarada como requerida ⇔ sin ella contesta
    /// <c>400 *.idempotency_key_required</c>.</para>
    ///
    /// <para>Con el cuerpo de <see cref="CuerpoQueLeeLaLlave"/>, la opcional: ese cuerpo sin la llave
    /// tiene que pedirla, y el documento tiene que declararla. Así quitar
    /// <c>.ConLlaveDeIdempotencia(siempre: false)</c> de un endpoint que la lee sigue en rojo después
    /// de regenerar, y ponérsela a uno que no la lee también: sin fila en la tabla no hay caso que la
    /// active, y con una fila inventada el endpoint no la pide.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task La_llave_que_el_documento_declara_es_la_que_el_endpoint_exige(string ensamblado)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        using var host = ContratoOpenApi.Pieza(ensamblado).Levantar(conContrato: false);
        var malas = new List<string>();

        foreach (var op in ContratoOpenApi.Operaciones(doc))
        {
            var llave = LlaveDeclarada(op);
            var requerida = (bool?)llave?["required"] == true;

            var vacia = await Enviar(host, op, conLlaveCompartida: true, llave: null);
            if (requerida != PideLaLlave(vacia))
            {
                malas.Add($"  {op}: el documento dice que {(requerida ? "la EXIGE" : "no la exige")} y sin ella " +
                          $"contesta {(int)vacia.Status} {Codigo(vacia.Cuerpo) ?? "(sin code)"}.");
            }

            if ((string?)op.Op["operationId"] is { } nombre && CuerpoQueLeeLaLlave.TryGetValue(nombre, out var cuerpo))
            {
                var activada = await Enviar(host, op, conLlaveCompartida: true, llave: null, cuerpo);
                if (!PideLaLlave(activada))
                {
                    malas.Add($"  {op}: con {cuerpo} y sin la llave contesta {(int)activada.Status} " +
                              $"{Codigo(activada.Cuerpo) ?? "(sin code)"}: ese cuerpo no la activa, o el endpoint no la lee.");
                }
                else if (llave is null)
                {
                    malas.Add($"  {op}: con {cuerpo} la exige y el documento no la declara " +
                              "(falta .ConLlaveDeIdempotencia(siempre: false)).");
                }
            }
            else if (llave is not null && !requerida)
            {
                malas.Add($"  {op}: el documento la declara opcional y no hay cuerpo que la active en " +
                          $"{nameof(CuerpoQueLeeLaLlave)}: o el endpoint no la lee (y sobra la declaración), o falta su fila.");
            }
        }

        Assert.True(malas.Count == 0,
            $"{ensamblado}: la cabecera {IdempotencyHeader.Name} que publica el contrato no es la que el " +
            "endpoint exige. Se declara con .ConLlaveDeIdempotencia() donde se lee con " +
            $"IdempotencyHeader.TryRead, y sólo ahí.{Environment.NewLine}{string.Join(Environment.NewLine, malas)}");
    }

    /// <summary>
    /// El largo que el documento publica para <c>Idempotency-Key</c> es el que el endpoint acepta:
    /// una llave de exactamente <c>maxLength</c> pasa, y una de un carácter más sale con
    /// <c>400 *.idempotency_key_required</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que cierra.</b> El contrato publicaba 128 en todas las operaciones, y
    /// <c>BuyTickets</c> aceptaba 128, pero de 91 en adelante la compra moría en un 500 al derivar la
    /// llave de apartar (<c>{saga}|hold:{item}</c> no cabía en <c>IdempotencyKey</c>). Hoy el
    /// orquestador acepta <c>LlaveDeSaga.MaxLength</c> y lo publica; esto comprueba los dos lados
    /// del número contra el host. Que con ese largo la saga entera derive sus llaves sin pasarse lo
    /// comprueba <c>LlaveDeSagaTests</c>, con identificadores del largo de los de verdad.</para>
    ///
    /// <para>La llave «justa» no tiene que dar éxito —el id es <c>sonda</c>—: tiene que no
    /// rechazarse por la llave y no romper (ningún 5xx). La opcional se manda con el cuerpo de
    /// <see cref="CuerpoQueLeeLaLlave"/>, que es el caso en que la lee; con <c>{}</c> no llegaría.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task El_largo_que_el_documento_publica_para_la_llave_es_el_que_el_endpoint_acepta(string ensamblado)
    {
        var doc = ContratoOpenApi.Comiteado(ensamblado);
        using var host = ContratoOpenApi.Pieza(ensamblado).Levantar(conContrato: false);
        var malas = new List<string>();
        var vistas = 0;

        foreach (var op in ContratoOpenApi.Operaciones(doc))
        {
            if (LlaveDeclarada(op) is not { } llave) continue;

            string? cuerpo = null;
            if ((bool?)llave["required"] != true
                && ((string?)op.Op["operationId"] is not { } nombre || !CuerpoQueLeeLaLlave.TryGetValue(nombre, out cuerpo)))
            {
                continue;   // sin el caso que la activa no se llega a leer; el rojo lo da la sonda de arriba
            }

            vistas++;
            if ((int?)llave["schema"]?["maxLength"] is not { } max)
            {
                malas.Add($"  {op}: declara {IdempotencyHeader.Name} sin maxLength.");
                continue;
            }

            var justa = await Enviar(host, op, conLlaveCompartida: true, new string('k', max), cuerpo);
            if (PideLaLlave(justa) || (int)justa.Status >= 500)
            {
                malas.Add($"  {op}: con una llave de {max} caracteres, lo que publica, contesta {(int)justa.Status} " +
                          $"{Codigo(justa.Cuerpo) ?? "(sin code)"}.");
            }

            var larga = await Enviar(host, op, conLlaveCompartida: true, new string('k', max + 1), cuerpo);
            if (!PideLaLlave(larga) || larga.Tipo != "application/problem+json")
            {
                malas.Add($"  {op}: con una llave de {max + 1} caracteres, uno más de lo que publica, contesta " +
                          $"{(int)larga.Status} {Codigo(larga.Cuerpo) ?? "(sin code)"} y no 400 *.idempotency_key_required.");
            }
        }

        Assert.True(vistas >= 1, $"{ensamblado}: ninguna operación exige {IdempotencyHeader.Name}; la sonda no prueba nada.");
        Assert.True(malas.Count == 0,
            $"{ensamblado}: el largo de {IdempotencyHeader.Name} que publica el contrato no es el que el endpoint " +
            "acepta. Los dos salen del metadato (.ConLlaveDeIdempotencia(maxLength: …), o .ConLlaveDeSaga() en un " +
            $"orquestador), que lee IdempotencyHeader.TryRead.{Environment.NewLine}{string.Join(Environment.NewLine, malas)}");
    }

    /// <summary>
    /// Cada fila de <see cref="CuerpoQueLeeLaLlave"/> nombra una operación publicada: una fila de una
    /// operación renombrada no la probaría nadie, y la sonda la daría por cubierta.
    /// </summary>
    [Fact]
    public void Cada_cuerpo_que_lee_la_llave_es_de_una_operacion_publicada()
    {
        var publicadas = ContratoOpenApi.Piezas
            .SelectMany(p => ContratoOpenApi.Operaciones(ContratoOpenApi.Comiteado(p.Ensamblado)))
            .Select(o => (string?)o.Op["operationId"])
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(CuerpoQueLeeLaLlave.Keys, k => Assert.True(publicadas.Contains(k),
            $"{nameof(CuerpoQueLeeLaLlave)} tiene «{k}» y ningún documento publica esa operación."));
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
