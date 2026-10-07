using System.Text.Json.Nodes;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// El <c>format</c> en el gate de compatibilidad consumidor → capacidad (ADR 0140, F2), sobre
/// esquemas escritos aquí: lo que el documento real no publica hoy (un <c>int64</c>, una cadena sin
/// <c>date-time</c>) también tiene que salir rojo.
/// </summary>
/// <remarks>
/// El caso que lo motivó: <c>StockHoldDto.ExpiresAtUtc</c> es un <c>DateTimeOffset</c>; si
/// <c>Api.Inventory</c> lo pasa a texto libre y regenera, el documento sólo pierde
/// <c>"format": "date-time"</c>, el <c>type</c> sigue siendo <c>string</c>, y mirando sólo el tipo el
/// gate quedaba en verde mientras <c>ReadFromJsonAsync</c> lanzaba en ejecución.
/// </remarks>
public sealed class SubconjuntoOpenApiTests
{
    private sealed record ConFecha(DateTimeOffset Vence);

    private sealed record ConEntero(int Cuantos);

    private sealed record ConLargo(long Cuantos);

    private sealed record ConTexto(string Vence);

    private static JsonObject Documento(string propiedad, string esquema) => JsonNode.Parse($$"""
        {
          "paths": {
            "/v1/x": {
              "post": {
                "requestBody": {
                  "content": { "application/json": { "schema": { "$ref": "#/components/schemas/X" } } },
                  "required": true
                },
                "responses": {
                  "200": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/X" } } } }
                }
              }
            }
          },
          "components": {
            "schemas": {
              "X": { "required": ["{{propiedad}}"], "type": "object", "properties": { "{{propiedad}}": {{esquema}} } }
            }
          }
        }
        """)!.AsObject();

    private static List<string> Lee(Type tipo, string propiedad, string esquema)
    {
        var doc = Documento(propiedad, esquema);
        return SubconjuntoOpenApi.Respuesta(doc, SubconjuntoOpenApi.Operacion(doc, "POST", "/v1/x")!, tipo).ToList();
    }

    private static List<string> Manda(string propiedad, string esquema, string cuerpo)
    {
        var doc = Documento(propiedad, esquema);
        var p = new PeticionGrabada("x", "POST", "v1/x", [], [], cuerpo);
        return SubconjuntoOpenApi.Peticion(doc, SubconjuntoOpenApi.Operacion(doc, "POST", "/v1/x")!, p).ToList();
    }

    [Fact]
    public void Una_fecha_que_se_lee_pide_date_time_y_no_le_basta_una_cadena()
    {
        Assert.Empty(Lee(typeof(ConFecha), "vence", """{ "type": "string", "format": "date-time" }"""));

        var malas = Lee(typeof(ConFecha), "vence", """{ "type": "string" }""");
        Assert.Contains(malas, m => m.Contains("vence", StringComparison.Ordinal) && m.Contains("sin format", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_cadena_que_se_lee_acepta_cualquier_format()
        => Assert.Empty(Lee(typeof(ConTexto), "vence", """{ "type": "string", "format": "date-time" }"""));

    [Fact]
    public void Un_int_no_lee_un_int64_y_un_long_si_lee_un_int32()
    {
        Assert.Empty(Lee(typeof(ConEntero), "cuantos", """{ "type": "integer", "format": "int32" }"""));
        Assert.Empty(Lee(typeof(ConLargo), "cuantos", """{ "type": "integer", "format": "int32" }"""));
        Assert.Contains(Lee(typeof(ConEntero), "cuantos", """{ "type": "integer", "format": "int64" }"""),
            m => m.Contains("int64", StringComparison.Ordinal));
    }

    [Fact]
    public void Lo_que_se_manda_se_lee_con_el_format_que_publica_el_contrato()
    {
        const string fecha = """{ "type": "string", "format": "date-time" }""";
        Assert.Empty(Manda("vence", fecha, """{ "vence": "2026-10-07T12:00:00+00:00" }"""));
        Assert.Contains(Manda("vence", fecha, """{ "vence": "07/10/2026 12:00" }"""),
            m => m.Contains("date-time", StringComparison.Ordinal));

        const string entero = """{ "type": "integer", "format": "int32" }""";
        Assert.Empty(Manda("cuantos", entero, """{ "cuantos": 3 }"""));
        Assert.Contains(Manda("cuantos", entero, """{ "cuantos": 3000000000 }"""),
            m => m.Contains("int32", StringComparison.Ordinal));
    }
}
