using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que la ruta directa del CMS le manda y le lee a <c>Bff.Salud</c> (<see cref="HttpClinicalSchedulingService"/>)
/// cabe en el contrato que el orquestador publica (<c>docs/contracts/openapi/Synergos.Bff.Salud.json</c>,
/// 003-salud-cita T007): el gate de consumidor CMS → orquestador, como el de Eventos.
/// </summary>
/// <remarks>
/// <para><b>Contra el documento COMITEADO</b>, que la deriva mantiene fiel al código del orquestador
/// (<c>ContratoOpenApiTests</c>, en la otra suite): consumidor → documento → productor, sin que esta suite
/// referencie el árbol de servicios.</para>
///
/// <para><b>Lo que se juzga es lo que SALE</b>, grabado de la llamada real del cliente, y lo que se LEE, del
/// tipo que deserializa. Las dos fases de P8 cuentan: un <c>confirm</c> que el orquestador dejara de publicar
/// devolvería a la cita a medias.</para>
/// </remarks>
public sealed class ConsumidorCmsDeBffSaludTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Salud.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    private const string Abierta = """
        {"id":"cita-1","patientKind":"salud.paciente","patientId":"pac-1","professionalKind":"salud.profesional",
         "professionalId":"doc-3","start":"2026-08-10T14:00:00+00:00","end":"2026-08-10T14:30:00+00:00",
         "status":"Running","total":{"amount":50000,"currency":"COP"},"reservationId":null,
         "pendingCompensations":2,"lastError":null}
        """;

    private sealed class Grabador : HttpMessageHandler
    {
        public List<(string Metodo, string Ruta, string Consulta, string? Llave, string Cuerpo)> Llamadas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            r.Headers.TryGetValues("Idempotency-Key", out var llave);
            Llamadas.Add((r.Method.Method, r.RequestUri!.AbsolutePath, r.RequestUri.Query, llave?.FirstOrDefault(),
                r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));

            var json = r.RequestUri.AbsolutePath switch
            {
                "/v1/appointments" => Abierta,
                "/v1/appointments/quote" => """{"amount":50000,"currency":"COP"}""",
                _ => Abierta.Replace("\"Running\"", "\"Completed\"", StringComparison.Ordinal),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("http://salud.local/") };
    }

    private sealed class Mon<T>(T v) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = v;
        public T Get(string? n) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private static HttpClinicalSchedulingService Cliente(Grabador cable)
    {
        var pacientes = Substitute.For<IPatientRegistry>();
        pacientes.GetAsync("pac-1", Arg.Any<CancellationToken>()).Returns(new EhrPatient(
            "pac-1", "Ana Ruiz", "CC-1", "F", new DateOnly(1990, 1, 1), 36, "300", "ana@ejemplo.co", "Bogotá", "O+",
            Array.Empty<string>(), Array.Empty<string>(), null, null));
        var medicos = Substitute.For<IDoctorDirectory>();
        medicos.GetAsync("doc-3", Arg.Any<CancellationToken>()).Returns(new MedicalDoctor(
            "doc-3", "Dra. Paula Gómez", "Endocrinología", "MP-1", 4.8, 12, null, new[] { DayOfWeek.Monday }, 8, 17, SlotMinutes: 30));
        return new HttpClinicalSchedulingService(new Fabrica(cable), new Mon<SaludSettings>(new SaludSettings { Mode = "Bff" }),
            pacientes, medicos, NullLogger<HttpClinicalSchedulingService>.Instance);
    }

    private static JsonElement Operacion(string ruta, string metodo, string operationId)
    {
        var op = default(JsonElement);
        Assert.True(Raiz.GetProperty("paths").TryGetProperty(ruta, out var operaciones) && operaciones.TryGetProperty(metodo, out op),
            $"Bff.Salud no publica {metodo.ToUpperInvariant()} {ruta}.");
        Assert.Equal(operationId, op.GetProperty("operationId").GetString());
        return op;
    }

    [Fact]
    public async Task Lo_que_manda_al_agendar_cabe_en_ScheduleAppointment_y_luego_confirma_con_ConfirmAppointment()
    {
        var cable = new Grabador();

        await Cliente(cable).BookAsync(new BookAppointmentRequest("pac-1", "doc-3", new DateTime(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(["/v1/appointments", "/v1/appointments/cita-1/confirm"], cable.Llamadas.Select(l => l.Ruta).ToArray());

        var abrir = Operacion("/v1/appointments", "post", "ScheduleAppointment");
        var cabecera = abrir.GetProperty("parameters").EnumerateArray()
            .Single(p => p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.True(cable.Llamadas[0].Llave!.Length <= cabecera.GetProperty("schema").GetProperty("maxLength").GetInt32());

        var malas = new List<string>();
        using var enviado = JsonDocument.Parse(cable.Llamadas[0].Cuerpo);
        Cabe(enviado.RootElement, abrir.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "$", malas);
        Assert.True(malas.Count == 0, "Lo que manda BookAsync no cabe en ScheduleAppointment:" + Environment.NewLine + string.Join(Environment.NewLine, malas));

        Operacion("/v1/appointments/{id}/confirm", "post", "ConfirmAppointment");
        Assert.Equal("", cable.Llamadas[1].Cuerpo);
    }

    [Theory]
    [InlineData("/v1/appointments", "post", "ScheduleAppointment", "201")]
    [InlineData("/v1/appointments/{id}/confirm", "post", "ConfirmAppointment", "200")]
    [InlineData("/v1/appointments/{id}", "get", "GetAppointment", "200")]
    public void Lo_que_lee_de_una_cita_existe_en_la_respuesta(string ruta, string metodo, string operationId, string estado)
    {
        var respuesta = Operacion(ruta, metodo, operationId)
            .GetProperty("responses").GetProperty(estado).GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var malas = new List<string>();

        Lee(Dto("AppointmentDto"), respuesta, "$", malas);

        Assert.True(malas.Count == 0, $"El CMS lee de una cita campos que {operationId} no publica:" + Environment.NewLine + string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public async Task La_cotizacion_que_pide_existe_en_QuoteAppointment()
    {
        var cable = new Grabador();

        await Cliente(cable).CopayAsync();

        var (_, ruta, consulta, _, _) = Assert.Single(cable.Llamadas);
        var op = Operacion(ruta, "get", "QuoteAppointment");
        var enLaConsulta = op.GetProperty("parameters").EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "query").Select(p => p.GetProperty("name").GetString()!).ToHashSet();
        foreach (var nombre in consulta.TrimStart('?').Split('&').Select(p => p.Split('=')[0]))
        {
            Assert.Contains(nombre, enLaConsulta);
        }

        var malas = new List<string>();
        Lee(Dto("MoneyDto"), op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), "$", malas);
        Assert.Empty(malas);
    }

    private static Type Dto(string nombre)
        => typeof(HttpClinicalSchedulingService).GetNestedType(nombre, BindingFlags.NonPublic)
           ?? throw new InvalidOperationException($"HttpClinicalSchedulingService ya no tiene {nombre}: este gate dejó de ver lo que lee.");

    private static void Lee(Type tipo, JsonElement esquema, string donde, List<string> malas)
    {
        esquema = Resolver(esquema);
        var propiedades = esquema.GetProperty("properties");
        foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var nombre = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
            if (!propiedades.TryGetProperty(nombre, out var sub))
            {
                malas.Add($"{donde}.{nombre}: el CMS lo lee y la respuesta no lo publica.");
                continue;
            }
            if (p.PropertyType.DeclaringType == typeof(HttpClinicalSchedulingService)) Lee(p.PropertyType, sub, $"{donde}.{nombre}", malas);
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
