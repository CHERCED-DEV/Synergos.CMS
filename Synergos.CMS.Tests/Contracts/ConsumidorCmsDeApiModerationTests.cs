using System.Net;
using System.Reflection;
using System.Text.Json;
using Synergos.CMS.Interfaces.Moderacion;
using Synergos.CMS.Tests.Services;
using Synergos.CMS.Web.Services.Moderacion;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que la moderación en Api le manda y le lee a <c>Api.Moderation</c> (<see cref="ModeracionEnApi"/>) cabe en el
/// contrato que la capacidad publica (<c>docs/contracts/openapi/Synergos.Api.Moderation.json</c>, 012-blogs-social, I5,
/// P6 A): el gate de consumidor CMS → capacidad, como el de <c>Api.Workflow</c>.
/// </summary>
/// <remarks>
/// Contra el documento COMITEADO, que <c>ContratoOpenApiTests</c> (en la otra suite) mantiene fiel al código de la
/// capacidad. Lo que se juzga es lo que SALE, grabado de las llamadas reales del cliente —denunciar, leer la cola y
/// retirar recorren las cuatro operaciones—, y lo que se LEE, de los tipos que deserializa.
/// </remarks>
public sealed class ConsumidorCmsDeApiModerationTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Api.Moderation.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    /// <summary>
    /// Denunciar, leer la cola y retirar llaman a lo publicado, con lo que publica: el cuerpo cabe, la consulta usa
    /// parámetros declarados y la llave cabe en su largo.
    /// </summary>
    [Fact]
    public async Task Denunciar_leer_la_cola_y_retirar_llaman_a_lo_publicado_y_lo_que_mandan_cabe()
    {
        var capacidad = new ModeracionEnApiTests.Capacidad
        {
            Respuesta = r => (r.Method.Method, r.RequestUri!.AbsolutePath) switch
            {
                ("POST", "/v1/cases") => (HttpStatusCode.Created, ModeracionEnApiTests.Caso("c1", "p-1")),
                ("GET", "/v1/cases/c1") => (HttpStatusCode.OK, ModeracionEnApiTests.Caso("c1", "p-1")),
                ("GET", "/v1/cases") => (HttpStatusCode.OK, ModeracionEnApiTests.Pagina(ModeracionEnApiTests.Caso("c1", "p-1"))),
                ("POST", _) => (HttpStatusCode.OK, ModeracionEnApiTests.Caso("c1", "p-1", "Decided", "Remove")),
                _ => null,
            },
        };
        var cliente = ModeracionEnApiTests.Cliente(capacidad);

        await cliente.DenunciarAsync(new DenunciaNueva(
            new ObjetivoModerable("blogs.publicacion", "p-1"), "Lo de p-1", null, "a1b2c3d4e5f60718", "Es spam"));
        await cliente.AbiertosAsync(50);
        await cliente.DecidirAsync("c1", DecisionDeModeracion.Retirar, new QuienModera("a1b2c3d4e5f60718", "Mónica"), "Spam");

        Assert.Equal(
            ["POST /v1/cases", "GET /v1/cases", "GET /v1/cases/c1", "GET /v1/cases", "POST /v1/cases/c1/decide"],
            capacidad.Llamadas.Select(l => $"{l.Metodo} {l.Ruta.Split('?')[0]}").ToArray());

        var malas = new List<string>();
        var denunciar = Operacion("/v1/cases", "post", "ReportCase");
        using (var enviado = JsonDocument.Parse(capacidad.Llamadas[0].Cuerpo))
        {
            Cabe(enviado.RootElement, CuerpoDe(denunciar), "ReportCase $", malas);
        }

        Assert.True(capacidad.Llamadas[0].Llave!.Length <= LargoDeLaLlave(denunciar));

        var listar = Operacion("/v1/cases", "get", "ListCases");
        var enLaConsulta = listar.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query").Select(p => p.GetProperty("name").GetString()!).ToHashSet();
        foreach (var llamada in capacidad.Llamadas.Where(l => l.Metodo == "GET" && l.Ruta.Contains('?')))
        {
            foreach (var nombre in llamada.Ruta.Split('?')[1].Split('&').Select(p => p.Split('=')[0]))
            {
                Assert.Contains(nombre, enLaConsulta);
            }
        }

        Operacion("/v1/cases/{id}", "get", "GetCase");
        var decidir = Operacion("/v1/cases/{id}/decide", "post", "DecideCase");
        using (var enviado = JsonDocument.Parse(capacidad.Llamadas[4].Cuerpo))
        {
            Cabe(enviado.RootElement, CuerpoDe(decidir), "DecideCase $", malas);
        }

        Assert.True(malas.Count == 0,
            "Lo que manda ModeracionEnApi no cabe en Api.Moderation:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    [Theory]
    [InlineData("/v1/cases", "post", "ReportCase", "201")]
    [InlineData("/v1/cases/{id}", "get", "GetCase", "200")]
    [InlineData("/v1/cases/{id}/decide", "post", "DecideCase", "200")]
    public void Lo_que_lee_de_un_caso_existe_en_la_respuesta(string ruta, string metodo, string operationId, string estado)
    {
        var respuesta = Operacion(ruta, metodo, operationId)
            .GetProperty("responses").GetProperty(estado).GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();

        Lee(Dto("CaseDto"), respuesta, "$", malas);

        Assert.True(malas.Count == 0, $"El CMS lee campos que {operationId} no publica:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Lo_que_lee_de_la_pagina_de_casos_existe_en_ListCases()
    {
        var respuesta = Operacion("/v1/cases", "get", "ListCases")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var pagina = (typeof(ModeracionEnApi).GetNestedType("PageDto`1", BindingFlags.NonPublic)
                      ?? throw new InvalidOperationException("ModeracionEnApi ya no tiene PageDto<T>: este gate dejó de ver lo que lee."))
            .MakeGenericType(Dto("CaseDto"));
        var malas = new List<string>();

        Lee(pagina, respuesta, "$", malas);

        Assert.True(malas.Count == 0, "El CMS lee de la página campos que no se publican:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    private static JsonElement Operacion(string ruta, string metodo, string operationId)
    {
        var op = default(JsonElement);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones) && operaciones.TryGetProperty(metodo, out op),
            $"Api.Moderation no publica {metodo.ToUpperInvariant()} {ruta}.");
        Assert.Equal(operationId, op.GetProperty("operationId").GetString());
        return op;
    }

    private static int LargoDeLaLlave(JsonElement op)
        => op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key")
            .GetProperty("schema").GetProperty("maxLength").GetInt32();

    private static JsonElement CuerpoDe(JsonElement op)
        => op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static Type Dto(string nombre)
        => typeof(ModeracionEnApi).GetNestedType(nombre, BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"ModeracionEnApi ya no tiene {nombre}: este gate dejó de ver lo que lee.");

    /// <summary>El tipo que lee el cliente, si es uno de sus DTO (directo o como elemento de una lista).</summary>
    private static Type? DtoDe(Type tipo)
    {
        if (tipo.DeclaringType == typeof(ModeracionEnApi)) return tipo;
        return tipo.IsGenericType && tipo.GetGenericArguments() is [var elemento] && elemento.DeclaringType == typeof(ModeracionEnApi)
            ? elemento
            : null;
    }

    private static void Lee(Type tipo, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(esquema);
        var propiedades = esquema.GetProperty("properties");
        var requeridas = esquema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
        var nulabilidad = new NullabilityInfoContext();
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(x => x.Name != "EqualityContract"))
        {
            var nombre = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!propiedades.TryGetProperty(nombre, out var sub))
            {
                malas.Add($"{donde}.{nombre}: el CMS lo lee y la respuesta no lo publica.");
                continue;
            }

            var anulable = Nullable.GetUnderlyingType(p.PropertyType) is not null
                           || nulabilidad.Create(p).ReadState == NullabilityState.Nullable;
            var admiteNulo = sub.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.Array
                             && t.EnumerateArray().Any(x => x.GetString() == "null");
            if (!anulable && !requeridas.Contains(nombre)) malas.Add($"{donde}.{nombre}: el CMS lo da por seguro y la respuesta no lo promete (no es required).");
            if (!anulable && admiteNulo) malas.Add($"{donde}.{nombre}: la respuesta puede mandar null y el CMS no lo admite.");
            if (DtoDe(p.PropertyType) is { } anidado)
            {
                var resuelto = Resolver(sub);
                Lee(anidado, resuelto.TryGetProperty("items", out var items) ? items : resuelto, $"{donde}.{nombre}", malas);
            }
        }
    }

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

        if (valor.ValueKind != JsonValueKind.Object) return;

        var propiedades = esquema.TryGetProperty("properties", out var ps) ? ps : default;
        foreach (var campo in valor.EnumerateObject())
        {
            if (propiedades.ValueKind != JsonValueKind.Object || !propiedades.TryGetProperty(campo.Name, out var sub))
            {
                malas.Add($"{donde}.{campo.Name}: el esquema no lo declara.");
                continue;
            }

            Cabe(campo.Value, sub, $"{donde}.{campo.Name}", malas);
        }
    }

    private static JsonElement Resolver(JsonElement esquema)
    {
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
