using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Contracts;

/// <summary>
/// Lo que el CMS lee de <c>Bff.Alquiler</c> y lo que le manda cabe en el contrato que el orquestador publica
/// (<c>docs/contracts/openapi/Synergos.Bff.Alquiler.json</c>): el gate de consumidor CMS → orquestador del alquiler
/// (línea 005, T024).
/// </summary>
/// <remarks>
/// <para><b>Contra el documento COMITEADO</b>, que <c>ContratoOpenApiTests</c> mantiene fiel al código del orquestador:
/// consumidor → documento → productor, cerrado por transitividad, como el de Eventos.</para>
///
/// <para>Se juzga lo que SALE de cada cliente, grabado de la llamada real (la ruta y el método existen, la llave y el
/// sujeto que manda están declarados, cada campo del cuerpo está en el esquema), y lo que LEE, desde el tipo que
/// deserializa: un campo renombrado en un lado se quedaría en nulo en silencio — un contrato sin unidades, una
/// liquidación sin lo retenido. Cubre al artefacto de la puerta, a lo posterior al cierre de la puerta y a la ruta
/// directa; el publicador de la oferta entra con T029.</para>
/// </remarks>
public sealed class ConsumidorCmsDeBffAlquilerTests
{
    private static readonly Lazy<JsonDocument> Documento = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "docs", "contracts", "openapi", "Synergos.Bff.Alquiler.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    /// <summary>Un orquestador que graba lo que le llega y contesta lo justo para que el cliente siga.</summary>
    private sealed class Grabadora : HttpMessageHandler
    {
        public ConcurrentQueue<(string Metodo, string Ruta, IReadOnlyList<string> Cabeceras, string Cuerpo)> Llamadas { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var cuerpo = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct);
            Llamadas.Enqueue((req.Method.Method, req.RequestUri!.AbsolutePath,
                req.Headers.Select(h => h.Key).ToList(), cuerpo));

            // La ruta directa lee el alquiler antes de cerrarlo: la moneda sale de ahí.
            return req.Method == HttpMethod.Get && req.RequestUri.AbsolutePath == "/v1/rentals/alq-1"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"rentalId":"alq-1","equipmentId":"andamio-multidireccional-2m","quantity":1,"start":"2026-11-02",
                         "end":"2026-11-09","renterId":"u-1","state":"reserved","rentalTotal":{"amount":210000,"currency":"COP"},
                         "deposit":{"amount":250000,"currency":"COP"},"depositHeld":250000,"damageCharged":0}
                        """, Encoding.UTF8, "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"title":"Not Found","status":404}""", Encoding.UTF8, "application/json"),
                };
        }
    }

    private sealed class Fabrica(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(h, disposeHandler: false) { BaseAddress = new Uri("http://bff-alquiler.local/") };
    }

    private static IOptionsMonitor<AlquilerSettings> Alquiler()
    {
        var m = Substitute.For<IOptionsMonitor<AlquilerSettings>>();
        m.CurrentValue.Returns(new AlquilerSettings());
        return m;
    }

    private static RentalAgreement Contrato() => new(
        "alq-1", "u-1", "andamio-multidireccional-2m", "Andamio", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9),
        210_000m, 250_000m, "COP", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), "sello",
        CaminoDeCompra.Puerta);

    [Fact]
    public void Lo_que_lee_el_artefacto_de_una_renta_existe_en_AlquilerRentaConsultar()
    {
        var op = Raiz.GetProperty("paths").GetProperty("/v1/flujos/alquiler.renta/{id}").GetProperty("get");
        Assert.Equal("AlquilerRentaConsultar", op.GetProperty("operationId").GetString());
        Assert.True(DeclaraCabecera(op, "X-Synergos-Sujeto"), "Consultar no declara el sujeto que el artefacto manda.");

        var malas = new List<string>();
        Lee(typeof(ArtefactoDeAlquiler.RentaDto), Respuesta(op), "$", malas);
        Assert.True(malas.Count == 0,
            $"El artefacto lee de una renta campos que AlquilerRentaConsultar no publica:{Environment.NewLine}"
            + string.Join(Environment.NewLine, malas.Select(m => "  " + m)));
    }

    [Fact]
    public void Lo_que_lee_la_reconciliacion_existe_en_ListDoorRentals()
    {
        var op = Raiz.GetProperty("paths").GetProperty("/v1/alquileres").GetProperty("get");
        Assert.Equal("ListDoorRentals", op.GetProperty("operationId").GetString());
        Assert.True(DeclaraCabecera(op, "X-Synergos-Sujeto"), "La lista no declara el sujeto que la reconciliación manda.");

        var malas = new List<string>();
        Lee(typeof(ArtefactoDeAlquiler.ListaDto), Respuesta(op), "$", malas);
        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public async Task Lo_que_manda_y_lee_lo_posterior_al_cierre_de_la_puerta_cabe_en_sus_operaciones()
    {
        var grabadora = new Grabadora();
        var puerta = new HttpAlquilerDeLaPuerta(
            new Fabrica(grabadora), Alquiler(), NullLogger<HttpAlquilerDeLaPuerta>.Instance, hayOrquestador: true);

        await puerta.DeclareReturnAsync(Contrato(), "declarar-1");
        await puerta.ConfirmReturnAsync(Contrato(), 40_000m, "staff-1", "confirmar-1");
        await puerta.CancelAsync(Contrato(), RentalCanceller.Renter, 0m, null, "cancelar-1");
        await puerta.CancelAsync(Contrato(), RentalCanceller.Staff, 50_000m, "staff-1", "cancelar-2");

        Assert.Equal(4, grabadora.Llamadas.Count);
        var malas = Juzgar(grabadora);
        var liquidacion = typeof(HttpAlquilerDeLaPuerta).GetNestedType("LiquidacionWire", BindingFlags.NonPublic)!;
        foreach (var ruta in new[] { "declarar-devolucion", "confirmar-devolucion", "cancelar-confirmado" })
        {
            var op = Raiz.GetProperty("paths").GetProperty($"/v1/alquileres/{{id}}/{ruta}").GetProperty("post");
            Lee(liquidacion, Respuesta(op), ruta, malas);
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public async Task Lo_que_manda_y_lee_la_ruta_directa_cabe_en_sus_operaciones()
    {
        var grabadora = new Grabadora();
        var directa = new HttpEquipmentRentalService(
            new Fabrica(grabadora), new StubEquipmentCatalogProvider(), Alquiler(), NullLogger<HttpEquipmentRentalService>.Instance);

        await directa.ReserveAsync(new RentalRequest(
            "andamio-multidireccional-2m", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), "u-1"), "reservar-1");
        await directa.DeclareReturnAsync("alq-1", "declarar-1");
        await directa.CancelAsync("alq-1", 0m, RentalCanceller.Renter, "cancelar-1");
        await directa.ReturnAsync("alq-1", 40_000m, "devolver-1");

        Assert.Contains(grabadora.Llamadas, l => l is { Metodo: "POST", Ruta: "/v1/rentals" });
        Assert.Contains(grabadora.Llamadas, l => l is { Metodo: "POST", Ruta: "/v1/rentals/alq-1/cancel" });
        Assert.Contains(grabadora.Llamadas, l => l is { Metodo: "POST", Ruta: "/v1/rentals/alq-1/return" });
        var toleradas = new HashSet<string>(StringComparer.Ordinal);
        var malas = Juzgar(grabadora, toleradas);
        // Trinquete: si una deja de mandarse o el orquestador la declara, la tabla se achica.
        Assert.Equal(LlaveQueElOrquestadorNoLee.Order(), toleradas.Order());
        Lee(typeof(HttpEquipmentRentalService).GetNestedType("RentalWire", BindingFlags.NonPublic)!,
            Respuesta(Raiz.GetProperty("paths").GetProperty("/v1/rentals/{id}").GetProperty("get")), "GetRental", malas);

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    // ── Cómo se juzga ───────────────────────────────────────────────────────

    /// <summary>
    /// Las operaciones de la ruta directa a las que el CMS manda <c>Idempotency-Key</c> sin que el orquestador la lea
    /// (NP-4 de la línea 005): son idempotentes por la saga —sus llaves salen de ella— y la llave sólo hace que la
    /// pieza del árbol pueda reintentarlas. El contrato no puede declararla (la sonda prohíbe declarar una llave que
    /// el endpoint no lee), así que queda aquí con nombre y no escondida: cualquier OTRA cabecera sin declarar es roja.
    /// </summary>
    private static readonly string[] LlaveQueElOrquestadorNoLee =
        ["/v1/rentals/{id}/cancel", "/v1/rentals/{id}/declare-return", "/v1/rentals/{id}/return"];

    /// <summary>Cada llamada grabada: su operación existe, sus cabeceras están declaradas y su cuerpo cabe.</summary>
    private static List<string> Juzgar(Grabadora grabadora, HashSet<string>? toleradas = null)
    {
        var malas = new List<string>();
        foreach (var (metodo, ruta, cabeceras, cuerpo) in grabadora.Llamadas)
        {
            if (Operacion(metodo, ruta) is not { } hallada)
            {
                malas.Add($"{metodo} {ruta}: el orquestador no publica esa operación.");
                continue;
            }

            var (plantilla, op) = hallada;

            foreach (var cabecera in cabeceras.Where(c => c is "Idempotency-Key" or "X-Synergos-Sujeto"))
            {
                if (DeclaraCabecera(op, cabecera)) continue;
                if (cabecera == "Idempotency-Key" && toleradas is not null && LlaveQueElOrquestadorNoLee.Contains(plantilla))
                {
                    toleradas.Add(plantilla);
                    continue;
                }
                malas.Add($"{metodo} {ruta}: manda {cabecera} y la operación no la declara.");
            }

            if (cuerpo.Length == 0)
            {
                continue;
            }

            if (!op.TryGetProperty("requestBody", out var body))
            {
                malas.Add($"{metodo} {ruta}: manda cuerpo y la operación no lo lleva.");
                continue;
            }

            using var enviado = JsonDocument.Parse(cuerpo);
            Cabe(enviado.RootElement, body.GetProperty("content").GetProperty("application/json").GetProperty("schema"),
                $"{metodo} {ruta}", malas);
        }

        return malas;
    }

    /// <summary>La operación cuya plantilla de ruta (<c>/v1/rentals/{id}</c>) casa con la ruta grabada.</summary>
    private static (string Plantilla, JsonElement Op)? Operacion(string metodo, string ruta)
    {
        foreach (var plantilla in Raiz.GetProperty("paths").EnumerateObject())
        {
            var patron = "^" + Regex.Replace(Regex.Escape(plantilla.Name), @"\\\{[^}]+}", "[^/]+") + "$";
            if (Regex.IsMatch(ruta, patron) && plantilla.Value.TryGetProperty(metodo.ToLowerInvariant(), out var op))
            {
                return (plantilla.Name, op);
            }
        }

        return null;
    }

    private static bool DeclaraCabecera(JsonElement op, string nombre)
        => op.TryGetProperty("parameters", out var ps) && ps.EnumerateArray().Any(p =>
            p.GetProperty("in").GetString() == "header" && p.GetProperty("name").GetString() == nombre);

    private static JsonElement Respuesta(JsonElement op)
        => op.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");

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
                : (Tipo: Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType, Esquema: sub);
            if (dentro.Tipo.DeclaringType == tipo.DeclaringType && dentro.Tipo.IsClass && dentro.Tipo != typeof(string))
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
        // Un anulable sale como oneOf [$ref, null] (o al revés): se mira la rama que no es nula.
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
