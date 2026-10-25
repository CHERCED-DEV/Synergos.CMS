using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core.Flow;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>
/// <c>itinerario</c>: los ítems del viaje, <c>[{ producto, etiqueta, inicio, fin }]</c>, como los guarda la saga genérica de
/// <c>viajes.reserva</c> y los devuelve la puerta (línea 006, research R18).
/// </summary>
/// <remarks>
/// <para><b>Es el tipo de valor del vertical</b>, como <c>lineas-de-pedido</c> de Tienda: una lista no se guarda por sí
/// sola en la saga genérica, y la respuesta del viaje tiene que decir qué se reservó sin preguntárselo al navegador.</para>
///
/// <para>No es un dato personal: producto, nombre en pantalla y periodo.</para>
/// </remarks>
public sealed class TipoItinerario : ITipoDeValor
{
    private static readonly string[] Campos = ["producto", "etiqueta", "inicio", "fin"];

    public string Nombre => "itinerario";
    public Type Clr => typeof(IReadOnlyList<ParadaDelViaje>);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: tiene que ser una lista.");

        var instante = new Instante();
        var paradas = new List<ParadaDelViaje>();
        var i = 0;
        foreach (var p in valor.EnumerateArray())
        {
            var aqui = $"{donde}[{i++}]";
            if (p.ValueKind != JsonValueKind.Object) throw new FormatException($"{aqui}: tiene que ser un objeto {{ producto, etiqueta, inicio, fin }}.");
            foreach (var campo in p.EnumerateObject().Where(c => !Campos.Contains(c.Name, StringComparer.Ordinal)))
            {
                throw new FormatException($"{aqui}: trae «{campo.Name}», y sólo admite producto, etiqueta, inicio y fin.");
            }

            paradas.Add(new ParadaDelViaje(
                Texto(p, "producto", aqui),
                Texto(p, "etiqueta", aqui),
                (DateTimeOffset)instante.Leer(Propiedad(p, "inicio", aqui), $"{aqui}.inicio"),
                (DateTimeOffset)instante.Leer(Propiedad(p, "fin", aqui), $"{aqui}.fin")));
        }
        return (IReadOnlyList<ParadaDelViaje>)paradas;
    }

    public JsonNode Escribir(object valor)
    {
        var instante = new Instante();
        return new JsonArray(((IReadOnlyList<ParadaDelViaje>)valor)
            .Select(p => (JsonNode)new JsonObject
            {
                ["producto"] = p.Producto,
                ["etiqueta"] = p.Etiqueta,
                ["inicio"] = instante.Escribir(p.Inicio),
                ["fin"] = instante.Escribir(p.Fin),
            })
            .ToArray());
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(Campos.Select(c => (JsonNode?)c).ToArray()),
            ["properties"] = new JsonObject
            {
                ["producto"] = new JsonObject { ["type"] = "string" },
                ["etiqueta"] = new JsonObject { ["type"] = "string" },
                ["inicio"] = new Instante().Esquema(),
                ["fin"] = new Instante().Esquema(),
            },
        },
    };

    private static JsonElement Propiedad(JsonElement objeto, string nombre, string donde)
        => objeto.TryGetProperty(nombre, out var p) ? p : throw new FormatException($"{donde}: falta «{nombre}».");

    private static string Texto(JsonElement objeto, string nombre, string donde)
        => Propiedad(objeto, nombre, donde) is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}.{nombre}: tiene que ser un texto no vacío.");
}
