using System.Text.Json.Nodes;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// <see cref="DevContenidoDeVerticales"/> suma secciones al final de una portada (016-contenido-piezas): lo que la
/// página ya tenía queda igual y primero.
/// </summary>
public sealed class DevContenidoDeVerticalesTests
{
    private static string Cuerpo(params string[] udis) => new JsonObject
    {
        ["layout"] = new JsonObject
        {
            ["Umbraco.BlockGrid"] = new JsonArray(udis.Select(u => (JsonNode)new JsonObject { ["contentUdi"] = u }).ToArray()),
        },
        ["contentData"] = new JsonArray(udis.Select(u => (JsonNode)new JsonObject { ["udi"] = u }).ToArray()),
        ["settingsData"] = new JsonArray(),
    }.ToJsonString();

    private static string[] Orden(string json, string lista, string clave) =>
        JsonNode.Parse(json)![lista] is JsonObject o
            ? o["Umbraco.BlockGrid"]!.AsArray().Select(n => n![clave]!.GetValue<string>()).ToArray()
            : JsonNode.Parse(json)![lista]!.AsArray().Select(n => n![clave]!.GetValue<string>()).ToArray();

    [Fact]
    public void Sumar_deja_lo_que_habia_primero_y_lo_nuevo_debajo()
    {
        var (unido, nuevas) = DevContenidoDeVerticales.Sumar(Cuerpo("a", "b"), Cuerpo("c", "d", "e"));

        Assert.Equal(3, nuevas);
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Orden(unido, "layout", "contentUdi"));
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, Orden(unido, "contentData", "udi"));
    }

    [Fact]
    public void Sumar_sobre_una_portada_vacia_devuelve_solo_lo_nuevo()
    {
        var (unido, nuevas) = DevContenidoDeVerticales.Sumar("", Cuerpo("c"));

        Assert.Equal(1, nuevas);
        Assert.Equal(new[] { "c" }, Orden(unido, "layout", "contentUdi"));
    }

    [Fact]
    public void La_marca_de_idempotencia_es_por_vertical()
    {
        Assert.Equal("syn-contenido--viajes", DevContenidoDeVerticales.Marca("Viajes"));
    }
}
