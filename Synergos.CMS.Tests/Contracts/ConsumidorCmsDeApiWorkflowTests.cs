using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que la ruta directa del trámite le manda y le lee a <c>Api.Workflow</c> (<see cref="HttpCaseWorkflowService"/>)
/// cabe en el contrato que la capacidad publica (<c>docs/contracts/openapi/Synergos.Api.Workflow.json</c>, 009-P2,
/// T019): el gate de consumidor CMS → capacidad, como los de los orquestadores.
/// </summary>
/// <remarks>
/// <para><b>Contra el documento COMITEADO</b>, que la deriva mantiene fiel al código de la capacidad
/// (<c>ContratoOpenApiTests</c>, en la otra suite): consumidor → documento → productor, sin que esta suite referencie
/// el árbol de servicios.</para>
///
/// <para><b>Lo que se juzga es lo que SALE</b>, grabado de la llamada real del cliente —tomar un expediente recién
/// radicado recorre las cuatro operaciones: buscar su instancia, leer la definición, arrancarla y disparar—, y lo
/// que se LEE, de los tipos que deserializa. Un campo que el CMS mande y la capacidad no declare se tira al suelo en
/// silencio (System.Text.Json no avisa); uno que lea y no se publique queda en su valor por defecto.</para>
/// </remarks>
public sealed class ConsumidorCmsDeApiWorkflowTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Api.Workflow.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    private const string DefinicionV2 = """
        {"id":"def-1","key":"gov.tramite.v2","initialState":"submitted","finalStates":["approved","rejected"],
         "states":["approved","in-review","info-requested","rejected","submitted"],
         "transitions":[
           {"name":"take","from":"submitted","to":"in-review","requiredRoles":["funcionario"]},
           {"name":"approve","from":"in-review","to":"approved","requiredRoles":["funcionario"]},
           {"name":"reject","from":"in-review","to":"rejected","requiredRoles":["funcionario"]},
           {"name":"request-info","from":"in-review","to":"info-requested","requiredRoles":["funcionario"]},
           {"name":"resubmit","from":"info-requested","to":"in-review","requiredRoles":["ciudadano"]}]}
        """;

    private static string Instancia(string estado) => $$"""
        {"id":"wf-1","definitionKey":"gov.tramite.v2","subjectKind":"gov.expediente","subjectId":"case-1001",
         "state":"{{estado}}","history":[],"startedAtUtc":"2026-10-24T10:00:00+00:00"}
        """;

    private sealed class Grabador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, string Consulta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            r.Headers.TryGetValues("Idempotency-Key", out var llave);
            Llamadas.Add((r.Method.Method, r.RequestUri!.AbsolutePath, r.RequestUri.Query, llave?.FirstOrDefault(),
                r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));

            var (estado, json) = (r.Method.Method, r.RequestUri.AbsolutePath) switch
            {
                ("GET", "/v1/instances") => (HttpStatusCode.OK, """{"items":[],"total":0,"offset":0,"hasMore":false}"""),
                ("GET", "/v1/definitions/gov.tramite.v2") => (HttpStatusCode.OK, DefinicionV2),
                ("POST", "/v1/instances") => (HttpStatusCode.Created, Instancia("submitted")),
                _ => (HttpStatusCode.OK, Instancia("in-review")),
            };
            return new HttpResponseMessage(estado) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("http://workflow.local/") };
    }

    private static readonly CaseActor Funcionaria = new(
        CaseRoles.KindFuncionario, "f-ana", "Ana Funcionaria", "ana@entidad.gov.co");

    private static (HttpCaseWorkflowService Cliente, string CaseId) Armar(Grabador cable)
    {
        var casos = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());
        var radicado = casos.ListCases().First(c => c.Status == CaseStatus.Radicado);
        return (new HttpCaseWorkflowService(new Fabrica(cable), casos, Options.Create(new GobSettings { Mode = "Api" })),
            radicado.CaseId);
    }

    private static JsonElement Operacion(string ruta, string metodo, string operationId)
    {
        var op = default(JsonElement);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones) && operaciones.TryGetProperty(metodo, out op),
            $"Api.Workflow no publica {metodo.ToUpperInvariant()} {ruta}.");
        Assert.Equal(operationId, op.GetProperty("operationId").GetString());
        return op;
    }

    private static int LargoDeLaLlave(JsonElement op)
        => op.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key")
            .GetProperty("schema").GetProperty("maxLength").GetInt32();

    private static JsonElement CuerpoDe(JsonElement op)
        => op.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    /// <summary>
    /// Tomar un expediente nuevo recorre las cuatro operaciones publicadas, y lo que manda cabe en cada una.
    /// </summary>
    [Fact]
    public async Task Tomar_un_expediente_nuevo_llama_a_lo_publicado_y_lo_que_manda_cabe()
    {
        var cable = new Grabador();
        var (cliente, caseId) = Armar(cable);

        await cliente.TakeAsync(caseId, Funcionaria, "La reviso yo.");

        Assert.Equal(
            ["GET /v1/instances", "GET /v1/definitions/gov.tramite.v2", "POST /v1/instances", "POST /v1/instances/wf-1/fire"],
            cable.Llamadas.Select(l => $"{l.Metodo} {l.Ruta}").ToArray());

        // Buscar su instancia: los parámetros de la consulta son los que la operación publica.
        var buscar = Operacion("/v1/instances", "get", "ListInstancesForSubject");
        var enLaConsulta = buscar.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query").Select(p => p.GetProperty("name").GetString()!).ToHashSet();
        foreach (var nombre in cable.Llamadas[0].Consulta.TrimStart('?').Split('&').Select(p => p.Split('=')[0]))
        {
            Assert.Contains(nombre, enLaConsulta);
        }

        Operacion("/v1/definitions/{key}", "get", "GetDefinition");

        var malas = new List<string>();

        // Arrancar y disparar: el cuerpo cabe en el esquema y la llave en su largo.
        var arrancar = Operacion("/v1/instances", "post", "StartInstance");
        using (var enviado = JsonDocument.Parse(cable.Llamadas[2].Cuerpo))
        {
            Cabe(enviado.RootElement, CuerpoDe(arrancar), "StartInstance $", malas);
        }
        Assert.True(cable.Llamadas[2].Llave!.Length <= LargoDeLaLlave(arrancar));

        var disparar = Operacion("/v1/instances/{id}/fire", "post", "FireTransition");
        using (var enviado = JsonDocument.Parse(cable.Llamadas[3].Cuerpo))
        {
            Cabe(enviado.RootElement, CuerpoDe(disparar), "FireTransition $", malas);
        }
        Assert.True(cable.Llamadas[3].Llave!.Length <= LargoDeLaLlave(disparar));

        Assert.True(malas.Count == 0,
            "Lo que manda HttpCaseWorkflowService no cabe en Api.Workflow:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    /// <summary>
    /// La llave de disparar es la más larga que arma el CMS (lleva el id de la instancia, el estado y la transición):
    /// con el estado y la transición más largos del proceso sigue cabiendo.
    /// </summary>
    [Fact]
    public void La_llave_mas_larga_que_arma_el_CMS_cabe_en_la_que_publica_FireTransition()
    {
        var peor = $"gov-fire-{Guid.NewGuid():N}-info-requested-request-info";

        Assert.True(peor.Length <= LargoDeLaLlave(Operacion("/v1/instances/{id}/fire", "post", "FireTransition")),
            $"«{peor}» ({peor.Length}) no cabe en la llave de FireTransition.");
    }

    [Theory]
    [InlineData("/v1/definitions/{key}", "get", "GetDefinition", "200", "DefinitionDto")]
    [InlineData("/v1/instances", "post", "StartInstance", "201", "InstanceDto")]
    [InlineData("/v1/instances/{id}/fire", "post", "FireTransition", "200", "InstanceDto")]
    public void Lo_que_lee_existe_en_la_respuesta(string ruta, string metodo, string operationId, string estado, string dto)
    {
        var respuesta = Operacion(ruta, metodo, operationId)
            .GetProperty("responses").GetProperty(estado).GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();

        Lee(Dto(dto), respuesta, "$", malas);

        Assert.True(malas.Count == 0, $"El CMS lee campos que {operationId} no publica:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Lo_que_lee_de_la_pagina_de_instancias_existe_en_ListInstancesForSubject()
    {
        var respuesta = Operacion("/v1/instances", "get", "ListInstancesForSubject")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var pagina = (typeof(HttpCaseWorkflowService).GetNestedType("PageDto`1", BindingFlags.NonPublic)
                      ?? throw new InvalidOperationException("HttpCaseWorkflowService ya no tiene PageDto<T>: este gate dejó de ver lo que lee."))
            .MakeGenericType(Dto("InstanceDto"));
        var malas = new List<string>();

        Lee(pagina, respuesta, "$", malas);

        Assert.True(malas.Count == 0, "El CMS lee de la página campos que no se publican:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    private static Type Dto(string nombre)
        => typeof(HttpCaseWorkflowService).GetNestedType(nombre, BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"HttpCaseWorkflowService ya no tiene {nombre}: este gate dejó de ver lo que lee.");

    /// <summary>El tipo que lee el cliente, si es uno de sus DTO (directo o como elemento de una lista).</summary>
    private static Type? DtoDe(Type tipo)
    {
        if (tipo.DeclaringType == typeof(HttpCaseWorkflowService)) return tipo;
        return tipo.IsGenericType && tipo.GetGenericArguments() is [var elemento] && elemento.DeclaringType == typeof(HttpCaseWorkflowService)
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
        if (valor.ValueKind == JsonValueKind.Array && esquema.TryGetProperty("items", out var deCadaUno))
        {
            var i = 0;
            foreach (var elemento in valor.EnumerateArray()) Cabe(elemento, deCadaUno, $"{donde}[{i++}]", malas);
            return;
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
