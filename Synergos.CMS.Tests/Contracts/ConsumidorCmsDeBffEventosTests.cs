using System.Text.Json;
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
