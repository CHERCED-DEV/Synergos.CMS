using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Services.Gobierno;
using Synergos.CMS.Web.Services.Gobierno;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS le pide a <c>Bff.Gobierno</c> y lo que lee de su respuesta cabe en el contrato que el orquestador
/// publica (<c>docs/contracts/openapi/Synergos.Bff.Gobierno.json</c>, 009-gobierno-tramite T028): el gate de consumidor
/// CMS → orquestador (principio III), contra el documento COMITEADO que la deriva mantiene fiel al código.
/// </summary>
/// <remarks>
/// Lo que lee el artefacto se saca de los tipos con que lee (<see cref="ArtefactoDelExpediente.RadicacionLeida"/> y los
/// suyos), con el nombre con que viajan: un campo renombrado de un lado y no del otro es rojo aquí, y no un nulo en el
/// expediente. El publicador de la oferta (T034) se suma a este mismo censo.
/// </remarks>
public sealed class ConsumidorCmsDeBffGobiernoTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Gobierno.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    [Fact]
    public async Task Guardar_las_respuestas_lee_la_radicacion_con_consultar_y_con_el_sujeto()
    {
        var orquestador = new ArtefactoDelExpedienteTests.OrquestadorDeGobiernoFalso();
        var (artefacto, _) = ArtefactoDelExpedienteTests.Nuevo(orquestador);

        await artefacto.GuardarRespuestasAsync("rad-1", new GovCitizen("Ana", "ana@ejemplo.co", MemberKey: Guid.NewGuid()),
            new Dictionary<string, string>(), default);

        Assert.Equal("GET /v1/flujos/gobierno.radicacion/rad-1", Assert.Single(orquestador.Rutas));
        var op = Raiz.GetProperty("paths").GetProperty("/v1/flujos/gobierno.radicacion/{id}").GetProperty("get");
        Assert.Equal("consultar", op.GetProperty("x-synergos-flujo").GetProperty("operacion").GetString());
        Assert.Contains(op.GetProperty("parameters").EnumerateArray(),
            p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "X-Synergos-Sujeto");
        Assert.EndsWith("/GobiernoRadicacionRespuesta",
            op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json")
                .GetProperty("schema").GetProperty("$ref").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cada_campo_que_lee_el_artefacto_lo_publica_la_respuesta_de_la_radicacion()
        => LoQueLeeEstaEn(typeof(ArtefactoDelExpediente.RadicacionLeida), Esquema("GobiernoRadicacionRespuesta"), "GobiernoRadicacionRespuesta");

    /// <summary>
    /// Cada propiedad del tipo con que se lee, por el nombre con que viaja (su <see cref="JsonPropertyNameAttribute"/> o
    /// su nombre en camelCase), está en el esquema; y lo anidado, en lo anidado.
    /// </summary>
    private static void LoQueLeeEstaEn(Type leido, JsonElement esquema, string donde)
    {
        var propiedades = Propiedades(esquema);
        foreach (var p in leido.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var nombre = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            Assert.True(propiedades.TryGetProperty(nombre, out var hijo),
                $"{donde} no publica «{nombre}», y el artefacto lo lee ({leido.Name}.{p.Name}).");
            var tipo = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (tipo.DeclaringType == typeof(ArtefactoDelExpediente))
            {
                LoQueLeeEstaEn(tipo, hijo, $"{donde}.{nombre}");
            }
        }
    }

    /// <summary>Las propiedades de un esquema: por su referencia, o la rama objeto de un <c>oneOf</c> con nulo.</summary>
    private static JsonElement Propiedades(JsonElement esquema)
    {
        if (esquema.TryGetProperty("$ref", out var referencia))
        {
            return Propiedades(Esquema(referencia.GetString()!.Split('/')[^1]));
        }
        if (esquema.TryGetProperty("oneOf", out var ramas))
        {
            return Propiedades(ramas.EnumerateArray().First(r => r.TryGetProperty("properties", out _) || r.TryGetProperty("$ref", out _)));
        }
        return esquema.GetProperty("properties");
    }

    private static JsonElement Esquema(string nombre) => Raiz.GetProperty("components").GetProperty("schemas").GetProperty(nombre);

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
