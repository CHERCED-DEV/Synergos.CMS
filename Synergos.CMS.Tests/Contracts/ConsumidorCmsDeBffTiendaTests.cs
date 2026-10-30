using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS le manda a <c>Bff.Tienda</c> para publicar y retirar la oferta cabe en el contrato que el
/// orquestador publica (<c>docs/contracts/openapi/Synergos.Bff.Tienda.json</c>, 004-tienda-compra R-9).
/// </summary>
/// <remarks>
/// El molde de <c>ConsumidorCmsDeBffEventosTests</c>: contra el documento COMITEADO, que la deriva mantiene fiel
/// al código del orquestador; lo que se juzga es lo que SALE, grabado de la llamada real del cliente. Un campo
/// renombrado en un lado —<c>stock</c> por <c>existencias</c>— lo descartaría el orquestador en silencio y la
/// oferta se rechazaría por existencias que faltan.
/// </remarks>
public sealed class ConsumidorCmsDeBffTiendaTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Tienda.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    private static (HttpTiendaOfferPublisher Publicador, HttpTiendaOfferPublisherTests.OrquestadorFalso Orquestador) Montar()
    {
        var orquestador = new HttpTiendaOfferPublisherTests.OrquestadorFalso();
        return (new HttpTiendaOfferPublisher(new HttpTiendaOfferPublisherTests.Fabrica(orquestador),
            NullLogger<HttpTiendaOfferPublisher>.Instance, hayDestino: true), orquestador);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lo_que_manda_el_publicador_de_la_oferta_cabe_en_PublishProductOffer(bool conVariantes)
    {
        var (publicador, orquestador) = Montar();

        await publicador.PublishAsync(conVariantes ? HttpTiendaOfferPublisherTests.Camiseta() : HttpTiendaOfferPublisherTests.Taza());

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        var op = default(JsonElement);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones) && operaciones.TryGetProperty("post", out op),
            $"El orquestador no publica POST {ruta}.");
        Assert.Equal("PublishProductOffer", op.GetProperty("operationId").GetString());
        Assert.True(llave!.Length <= Cabecera(op).GetProperty("schema").GetProperty("maxLength").GetInt32());

        var malas = new List<string>();
        using var enviado = JsonDocument.Parse(cuerpo);
        Cabe(enviado.RootElement, op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "$", malas);

        Assert.True(malas.Count == 0,
            $"Lo que manda HttpTiendaOfferPublisher no cabe en PublishProductOffer:{Environment.NewLine}" +
            string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
        Assert.Contains("stock", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lo_que_manda_el_retiro_de_la_oferta_existe_en_RetireProductOffer()
    {
        var (publicador, orquestador) = Montar();

        await publicador.RetireAsync("TAZA-001");

        var (ruta, llave, cuerpo) = Assert.Single(orquestador.Llamadas);
        Assert.Equal("/v1/ofertas/TAZA-001/retirar", ruta);
        var op = Raiz.GetProperty("paths").GetProperty("/v1/ofertas/{productId}/retirar").GetProperty("post");
        Assert.Equal("RetireProductOffer", op.GetProperty("operationId").GetString());
        Assert.True(llave!.Length <= Cabecera(op).GetProperty("schema").GetProperty("maxLength").GetInt32());
        Assert.False(op.TryGetProperty("requestBody", out _));
        Assert.Equal("", cuerpo);
    }

    /// <summary>
    /// Lo que el registro lee del envío de una compra (013-seller-operaciones, T010) existe en la respuesta de
    /// <c>LeerEnvioDeLaCompra</c>: un campo renombrado en el orquestador —<c>destino</c> por <c>destination</c>— se
    /// quedaría en nulo en silencio y el personal despacharía sin saber a dónde.
    /// </summary>
    [Fact]
    public void Lo_que_lee_el_registro_del_envio_existe_en_LeerEnvioDeLaCompra()
    {
        var op = Raiz.GetProperty("paths").GetProperty("/v1/envios/{compra}").GetProperty("get");
        Assert.Equal("LeerEnvioDeLaCompra", op.GetProperty("operationId").GetString());

        var malas = new List<string>();
        Lee(typeof(RegistroDeComprasDeLaTienda.EnvioLeido),
            op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "$", malas);

        Assert.True(malas.Count == 0, "El registro lee campos que el contrato no publica: " + string.Join(" | ", malas));
    }

    /// <summary>
    /// Cada propiedad de <paramref name="tipo"/> existe en <paramref name="esquema"/>, y por dentro de sus registros, con
    /// el nombre con que viaja: el de <c>JsonPropertyName</c> si lo tiene.
    /// </summary>
    private static void Lee(Type tipo, JsonElement esquema, string donde, List<string> malas)
    {
        var propiedades = Resolver(esquema).GetProperty("properties");
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var nombre = p.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name
                         ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!propiedades.TryGetProperty(nombre, out var sub))
            {
                malas.Add($"{donde}.{nombre}");
                continue;
            }
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var (dentro, esq) = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? (t.GetGenericArguments()[0], Resolver(sub).GetProperty("items"))
                : (t, sub);
            if (dentro.IsClass && dentro.Namespace == typeof(RegistroDeComprasDeLaTienda).Namespace) Lee(dentro, esq, $"{donde}.{nombre}", malas);
        }
    }

    private static JsonElement Cabecera(JsonElement op) => op.GetProperty("parameters").EnumerateArray()
        .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");

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
            esquema = Raiz.GetProperty("components").GetProperty("schemas").GetProperty(referencia.GetString()!.Split('/').Last());
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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
