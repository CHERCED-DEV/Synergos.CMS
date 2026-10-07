using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>Una petición tal como salió de un consumidor, grabada antes de llegar al cable.</summary>
/// <param name="Cliente">El nombre del cliente HTTP: el de la capacidad.</param>
/// <param name="Metodo">GET, POST…</param>
/// <param name="Ruta">La ruta concreta, sin query.</param>
/// <param name="Query">Los nombres de los parámetros de query que mandó.</param>
/// <param name="Cabeceras">Los nombres de las cabeceras que mandó.</param>
/// <param name="Cuerpo">El JSON que mandó, o <c>null</c>.</param>
internal sealed record PeticionGrabada(
    string Cliente, string Metodo, string Ruta, IReadOnlyList<string> Query,
    IReadOnlyCollection<string> Cabeceras, string? Cuerpo);

/// <summary>
/// ¿Cabe lo que un consumidor manda y lee en el contrato publicado de quien lo atiende? Sin paquetes:
/// sólo el subconjunto de JSON Schema que emite ASP.NET (ADR 0140, F2).
/// </summary>
/// <remarks>
/// <para><b>Por qué subconjunto y no igualdad.</b> Los orquestadores transcriben a propósito sólo
/// lo que usan («un DTO que copie la respuesta entera obligaría a tocar el orquestador cada vez que
/// una capacidad agrega un campo que no le importa»). Lo que no puede pasar es que lo que mandan o
/// leen NO esté en el contrato: System.Text.Json descarta en silencio lo que no mapea, y lo que falta
/// llega como default. Esto convierte ese silencio en un rojo con nombre.</para>
///
/// <para><b>Nombres EXACTOS en camelCase</b>, más estricto que la deserialización web, que no
/// distingue mayúsculas: <c>forkind</c> funcionaría en ejecución y aquí es rojo. Es a propósito: el
/// contrato publica un nombre, y es ése.</para>
///
/// <para><b>Lo que no mira</b>, dicho para no mentir sobre su alcance: que el consumidor mande lo que
/// el NEGOCIO exige (el esquema publica la forma del cable; eso lo ven los tests contra host real) ni
/// el valor de lo que viaja. Y depende de que el documento comiteado sea el del código, que es lo
/// que garantiza la deriva en la misma suite. Lo reutilizan los recorridos de Tienda, Salud y Viajes
/// cuando lleguen.</para>
/// </remarks>
internal static class SubconjuntoOpenApi
{
    /// <summary>La operación del documento que atiende <paramref name="metodo"/> sobre la ruta concreta.</summary>
    public static OperacionPublicada? Operacion(JsonObject doc, string metodo, string ruta)
        => ContratoOpenApi.Operaciones(doc).FirstOrDefault(o =>
            string.Equals(o.Metodo, metodo, StringComparison.Ordinal)
            && Regex.IsMatch(ruta, "^" + Regex.Replace(Regex.Escape(o.Ruta), @"\\\{[^/]+?\}", "[^/]+") + "$"));

    /// <summary>Lo que la petición grabada incumple del contrato de su operación.</summary>
    public static IEnumerable<string> Peticion(JsonObject doc, OperacionPublicada op, PeticionGrabada p)
    {
        var parametros = (op.Op["parameters"]?.AsArray() ?? []).OfType<JsonObject>().ToList();

        // La query: lo que se manda está declarado, y lo requerido se manda.
        var query = parametros.Where(x => (string?)x["in"] == "query").ToList();
        foreach (var q in p.Query.Where(q => !query.Any(d => (string?)d["name"] == q)))
        {
            yield return $"manda ?{q}, que {op} no declara (la capacidad la ignoraría).";
        }

        foreach (var d in query.Where(d => (bool?)d["required"] == true && !p.Query.Contains((string)d["name"]!)))
        {
            yield return $"no manda ?{d["name"]}, que {op} exige.";
        }

        // Las cabeceras que la operación exige: Idempotency-Key, hoy.
        foreach (var h in parametros.Where(x => (string?)x["in"] == "header" && (bool?)x["required"] == true))
        {
            var nombre = (string)h["name"]!;
            if (!p.Cabeceras.Contains(nombre, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"no manda la cabecera {nombre}, que {op} exige.";
            }
        }

        // El cuerpo.
        var esquema = op.Op["requestBody"]?["content"]?["application/json"]?["schema"];
        if (p.Cuerpo is null)
        {
            if ((bool?)op.Op["requestBody"]?["required"] == true) yield return $"no manda cuerpo y {op} lo exige.";
            yield break;
        }

        if (esquema is null)
        {
            yield return $"manda cuerpo y {op} no declara ninguno.";
            yield break;
        }

        foreach (var e in Valor(doc, esquema, JsonNode.Parse(p.Cuerpo), "cuerpo")) yield return e;
    }

    /// <summary>Lo que el tipo que el consumidor LEE no encuentra en la respuesta 2xx de la operación.</summary>
    public static IEnumerable<string> Respuesta(JsonObject doc, OperacionPublicada op, Type leido)
    {
        var esquema = (op.Op["responses"]?.AsObject() ?? [])
            .Where(r => r.Key.StartsWith('2'))
            .Select(r => r.Value?["content"]?["application/json"]?["schema"])
            .FirstOrDefault(s => s is not null);

        return esquema is null
            ? [$"{op} no declara cuerpo de respuesta 2xx y el consumidor lee {leido.Name}."]
            : Tipo(doc, leido, esquema, $"respuesta {leido.Name}");
    }

    // ── El valor que se manda contra el esquema que se publica ────────────────────────────────

    private static IEnumerable<string> Valor(JsonObject doc, JsonNode esquema, JsonNode? valor, string donde)
    {
        var (s, admiteNulo) = SinNulo(doc, esquema);
        var tipos = Tipos(s);

        switch (valor)
        {
            case null:
                if (!admiteNulo) yield return $"{donde}: null donde el contrato no lo admite.";
                yield break;

            case JsonObject o:
                if (tipos.Count > 0 && !tipos.Contains("object"))
                {
                    yield return $"{donde}: un objeto donde el contrato dice {string.Join("|", tipos)}.";
                    yield break;
                }

                var props = s["properties"]?.AsObject();
                foreach (var (nombre, sub) in o)
                {
                    if (props?[nombre] is not { } declarada)
                    {
                        yield return $"{donde}.{nombre}: el contrato no la declara (la capacidad la tiraría en silencio).";
                        continue;
                    }

                    foreach (var e in Valor(doc, declarada, sub, $"{donde}.{nombre}")) yield return e;
                }

                foreach (var requerida in (s["required"]?.AsArray() ?? []).Select(x => (string)x!).Where(r => !o.ContainsKey(r)))
                {
                    yield return $"{donde}.{requerida}: el contrato la exige y no viaja.";
                }

                yield break;

            case JsonArray a:
                if (!tipos.Contains("array"))
                {
                    yield return $"{donde}: una lista donde el contrato dice {string.Join("|", tipos)}.";
                    yield break;
                }

                foreach (var e in a.SelectMany((x, i) => Valor(doc, s["items"]!, x, $"{donde}[{i}]"))) yield return e;
                yield break;

            case JsonValue v:
                var bien = v.GetValueKind() switch
                {
                    JsonValueKind.String => tipos.Contains("string"),
                    JsonValueKind.Number => tipos.Contains("number") || (tipos.Contains("integer") && v.TryGetValue<long>(out _)),
                    JsonValueKind.True or JsonValueKind.False => tipos.Contains("boolean"),
                    _ => false,
                };
                if (!bien) yield return $"{donde}: {v.GetValueKind()} donde el contrato dice {string.Join("|", tipos)}.";
                yield break;
        }
    }

    // ── El tipo que se lee contra el esquema que se publica ───────────────────────────────────

    private static IEnumerable<string> Tipo(JsonObject doc, Type tipo, JsonNode esquema, string donde)
    {
        var (s, _) = SinNulo(doc, esquema);
        var props = s["properties"]?.AsObject();
        var requeridas = (s["required"]?.AsArray() ?? []).Select(x => (string)x!).ToHashSet(StringComparer.Ordinal);
        var nulabilidad = new NullabilityInfoContext();

        foreach (var prop in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(x => x.Name != "EqualityContract"))
        {
            var nombre = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (props?[nombre] is not { } declarada)
            {
                yield return $"{donde}.{nombre}: el consumidor la lee y el contrato no la declara (llegaría el default).";
                continue;
            }

            var anulable = Nullable.GetUnderlyingType(prop.PropertyType) is not null
                           || nulabilidad.Create(prop).ReadState == NullabilityState.Nullable;
            var (sub, admiteNulo) = SinNulo(doc, declarada);
            if (!anulable && !requeridas.Contains(nombre))
            {
                yield return $"{donde}.{nombre}: el consumidor la da por segura y el contrato no la promete (no es required).";
            }

            if (!anulable && admiteNulo)
            {
                yield return $"{donde}.{nombre}: el contrato puede mandar null y el consumidor no lo admite.";
            }

            foreach (var e in Compatible(doc, prop.PropertyType, declarada, sub, $"{donde}.{nombre}")) yield return e;
        }
    }

    private static IEnumerable<string> Compatible(JsonObject doc, Type clr, JsonNode declarada, JsonObject sub, string donde)
    {
        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        var tipos = Tipos(sub);
        var esperado = Esperado(t);

        if (esperado == "array")
        {
            if (!tipos.Contains("array"))
            {
                yield return $"{donde}: el consumidor la lee como lista y el contrato dice {string.Join("|", tipos)}.";
                yield break;
            }

            var item = t.IsArray ? t.GetElementType()! : t.GetGenericArguments()[0];
            var itemEsquema = sub["items"]!;
            if (Esperado(item) == "object")
            {
                foreach (var e in Tipo(doc, item, itemEsquema, $"{donde}[]")) yield return e;
            }
            else
            {
                foreach (var e in Compatible(doc, item, itemEsquema, SinNulo(doc, itemEsquema).Esquema, $"{donde}[]")) yield return e;
            }

            yield break;
        }

        if (esperado == "object")
        {
            foreach (var e in Tipo(doc, t, declarada, donde)) yield return e;
            yield break;
        }

        // Un decimal acepta lo que el contrato publique como integer; un int no acepta un number.
        if (!tipos.Contains(esperado) && !(esperado == "number" && tipos.Contains("integer")))
        {
            yield return $"{donde}: el consumidor la lee como {esperado} y el contrato dice {string.Join("|", tipos)}.";
        }
    }

    /// <summary>El tipo JSON con que System.Text.Json (web) lee <paramref name="t"/>.</summary>
    /// <remarks>Un enum se rechaza en vez de adivinarse: depende de un convertidor que el gate no ve.</remarks>
    private static string Esperado(Type t)
    {
        if (t.IsEnum) throw new NotSupportedException($"{t.Name}: un enum no cabe en este gate sin saber su convertidor.");
        if (t == typeof(string) || t == typeof(DateTimeOffset) || t == typeof(DateTime) || t == typeof(Guid)) return "string";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "integer";
        if (t == typeof(decimal) || t == typeof(double) || t == typeof(float)) return "number";
        if (t == typeof(bool)) return "boolean";
        return t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t) ? "array" : "object";
    }

    // ── El esquema ────────────────────────────────────────────────────────────────────────────

    private static JsonObject Resolver(JsonObject doc, JsonNode esquema)
    {
        var s = esquema.AsObject();
        while ((string?)s["$ref"] is { } referencia)
        {
            s = doc["components"]!["schemas"]![referencia[(referencia.LastIndexOf('/') + 1)..]]!.AsObject();
        }

        return s;
    }

    /// <summary>El esquema sin su «o null», y si lo admitía: <c>oneOf [null, X]</c> o <c>type [null, …]</c>.</summary>
    private static (JsonObject Esquema, bool AdmiteNulo) SinNulo(JsonObject doc, JsonNode esquema)
    {
        var s = Resolver(doc, esquema);
        if (s["oneOf"] is JsonArray alternativas)
        {
            var resto = alternativas.Where(a => (string?)a?["type"] != "null").ToList();
            if (resto.Count == 1) return (Resolver(doc, resto[0]!), resto.Count < alternativas.Count);
        }

        return (s, Tipos(s).Contains("null"));
    }

    private static HashSet<string> Tipos(JsonObject s) => s["type"] switch
    {
        JsonArray a => a.Select(x => (string)x!).ToHashSet(StringComparer.Ordinal),
        JsonValue v => [(string)v!],
        _ => [],
    };
}
