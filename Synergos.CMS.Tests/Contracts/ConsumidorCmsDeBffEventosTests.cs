using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS le manda a <c>Bff.Eventos</c> cabe en el contrato que el orquestador publica
/// (<c>docs/contracts/openapi/Synergos.Bff.Eventos.json</c>, ADR 0140): el gate de consumidor CMS →
/// orquestador.
/// </summary>
/// <remarks>
/// <para><b>Contra el documento COMITEADO</b>, que la deriva mantiene fiel al código del orquestador
/// (<c>ContratoOpenApiTests</c>, en la otra suite): consumidor → documento → productor, cerrado por
/// transitividad, sin que esta suite referencie el árbol de servicios.</para>
///
/// <para><b>Lo que se juzga es lo que SALE</b>, grabado de la llamada real del cliente: la ruta y el
/// método existen, la cabecera de idempotencia que manda está declarada, y cada campo del cuerpo está
/// en el esquema con un tipo que el esquema admite. Un campo renombrado en un lado —<c>seats</c> por
/// <c>butacas</c>— lo descartaría el orquestador en silencio y la oferta saldría sin butacas.</para>
/// </remarks>
public sealed class ConsumidorCmsDeBffEventosTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Eventos.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    [Fact]
    public async Task Lo_que_manda_el_publicador_de_la_oferta_cabe_en_PublishEventOffer()
    {
        var orquestador = new HttpEventOfferPublisherTests.OrquestadorFalso();
        var publicador = new HttpEventOfferPublisher(
            new HttpEventOfferPublisherTests.Fabrica(orquestador),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpEventOfferPublisher>.Instance, hayDestino: true);

        await publicador.PublishAsync(HttpEventOfferPublisherTests.Evento());

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        var op = default(JsonElement);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones)
                    && operaciones.TryGetProperty("post", out op),
            $"El orquestador no publica POST {ruta}.");
        Assert.Equal("PublishEventOffer", op.GetProperty("operationId").GetString());

        // La llave que manda está declarada, y su largo cabe en el que el orquestador acepta.
        var cabecera = op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(llave!.Length <= cabecera.GetProperty("schema").GetProperty("maxLength").GetInt32());

        var esquema = op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();
        using var enviado = JsonDocument.Parse(cuerpo);
        Cabe(enviado.RootElement, esquema, "$", malas);

        Assert.True(malas.Count == 0,
            $"Lo que manda HttpEventOfferPublisher no cabe en PublishEventOffer:{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
        // Y el recorrido no fue en vano: el cuerpo trae las localidades con butacas.
        Assert.Contains("seats", cuerpo, StringComparison.Ordinal);
    }

    /// <summary>
    /// El retiro de la oferta (al despublicar o borrar un evento) existe en el orquestador con la ruta y la
    /// llave que manda el CMS: si no existiera, un evento despublicado se seguiría vendiendo por la puerta.
    /// </summary>
    [Fact]
    public async Task Lo_que_manda_el_retiro_de_la_oferta_existe_en_RetireEventOffer()
    {
        var orquestador = new HttpEventOfferPublisherTests.OrquestadorFalso();
        var publicador = new HttpEventOfferPublisher(
            new HttpEventOfferPublisherTests.Fabrica(orquestador),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpEventOfferPublisher>.Instance, hayDestino: true);

        await publicador.RetireAsync("evt-oferta");

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas/evt-oferta/retirar", ruta);
        var op = Raiz.GetProperty("paths").GetProperty("/v1/ofertas/{eventId}/retirar").GetProperty("post");
        Assert.Equal("RetireEventOffer", op.GetProperty("operationId").GetString());
        var cabecera = op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(llave!.Length <= cabecera.GetProperty("schema").GetProperty("maxLength").GetInt32());
        Assert.False(op.TryGetProperty("requestBody", out _));
        Assert.Equal("", cuerpo);
    }

    /// <summary>
    /// Lo que LEEN el artefacto de la puerta y la ruta vieja de una compra (ADR 0140 F3) existe en la
    /// respuesta de <c>GetTicketPurchase</c>, y la cabecera del sujeto que mandan está declarada.
    /// </summary>
    /// <remarks>
    /// Se lee del tipo que deserializa (<c>PurchaseDto</c> y los suyos), con el nombre que su JSON usa:
    /// un campo que el orquestador renombra —o que el CMS lee con otro nombre— se quedaría en nulo en
    /// silencio, y una compra sin <c>held</c> emite cero entradas.
    /// </remarks>
    [Fact]
    public void Lo_que_lee_el_artefacto_de_una_compra_existe_en_GetTicketPurchase()
    {
        var op = Raiz.GetProperty("paths").GetProperty("/v1/ticket-purchases/{id}").GetProperty("get");
        Assert.Equal("GetTicketPurchase", op.GetProperty("operationId").GetString());
        Assert.Contains(op.GetProperty("parameters").EnumerateArray(), p =>
            p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "X-Synergos-Sujeto");

        var respuesta = op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();
        Lee(typeof(CompraDeEventosEnElOrquestador.PurchaseDto), respuesta, "$", malas);

        Assert.True(malas.Count == 0,
            $"El CMS lee de una compra campos que GetTicketPurchase no publica:{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    /// <summary>Cada propiedad de <paramref name="tipo"/>, con su nombre en el JSON, está en <paramref name="esquema"/>.</summary>
    private static void Lee(Type tipo, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(esquema);
        var propiedades = esquema.GetProperty("properties");
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var nombre = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!propiedades.TryGetProperty(nombre, out var sub))
            {
                malas.Add($"{donde}.{nombre}: el CMS lo lee y la respuesta no lo publica.");
                continue;
            }

            var dentro = p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? (Tipo: p.PropertyType.GetGenericArguments()[0], Esquema: Resolver(sub).GetProperty("items"))
                : (Tipo: p.PropertyType, Esquema: sub);
            if (dentro.Tipo.DeclaringType == typeof(CompraDeEventosEnElOrquestador))
            {
                Lee(dentro.Tipo, dentro.Esquema, $"{donde}.{nombre}", malas);
            }
        }
    }

    /// <summary>Si <paramref name="valor"/> cabe en <paramref name="esquema"/>; lo que no, a <paramref name="malas"/>.</summary>
    private static void Cabe(JsonElement valor, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(esquema);

        if (esquema.TryGetProperty("type", out var tipo))
        {
            var admitidos = tipo.ValueKind == JsonValueKind.Array
                ? tipo.EnumerateArray().Select(t => t.GetString()!).ToHashSet(StringComparer.Ordinal)
                : [tipo.GetString()!];
            if (!admitidos.Contains(TipoDe(valor)) && !(TipoDe(valor) == "integer" && admitidos.Contains("number")))
            {
                malas.Add($"{donde}: manda {TipoDe(valor)} y el esquema admite {string.Join("|", admitidos)}.");
                return;
            }
        }

        switch (valor.ValueKind)
        {
            case JsonValueKind.Object:
                var propiedades = esquema.TryGetProperty("properties", out var p) ? p : default;
                foreach (var campo in valor.EnumerateObject())
                {
                    if (propiedades.ValueKind != JsonValueKind.Object || !propiedades.TryGetProperty(campo.Name, out var sub))
                    {
                        malas.Add($"{donde}.{campo.Name}: el esquema no lo declara.");
                        continue;
                    }
                    Cabe(campo.Value, sub, $"{donde}.{campo.Name}", malas);
                }
                if (esquema.TryGetProperty("required", out var requeridos))
                {
                    foreach (var r in requeridos.EnumerateArray().Select(r => r.GetString()!))
                    {
                        if (!valor.TryGetProperty(r, out _)) malas.Add($"{donde}.{r}: el esquema lo exige y no se manda.");
                    }
                }
                break;
            case JsonValueKind.Array when esquema.TryGetProperty("items", out var items):
                var i = 0;
                foreach (var elemento in valor.EnumerateArray()) Cabe(elemento, items, $"{donde}[{i++}]", malas);
                break;
        }
    }

    private static JsonElement Resolver(JsonElement esquema)
    {
        // Un anulable sale como oneOf [null, $ref]: se mira la rama que no es nula.
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            esquema = ramas.EnumerateArray().First(r => !(r.TryGetProperty("type", out var t) && t.GetString() == "null"));
        }
        while (esquema.TryGetProperty("$ref", out var referencia))
        {
            var nombre = referencia.GetString()!.Split('/').Last();
            esquema = Raiz.GetProperty("components").GetProperty("schemas").GetProperty(nombre);
        }
        return esquema;
    }

    private static string TipoDe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "string",
        JsonValueKind.Number => v.TryGetInt64(out _) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        _ => "object",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
