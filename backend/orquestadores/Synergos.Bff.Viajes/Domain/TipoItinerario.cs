using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>
/// <c>itinerario</c>: los ítems del viaje, <c>[{ producto, etiqueta, inicio, fin }]</c>, como los guarda la saga genérica de
/// <c>viajes.reserva</c> y los devuelve la puerta (línea 006, research R18).
/// </summary>
/// <remarks>
/// <para><b>Es el tipo de valor del vertical</b>, como <c>lineas-de-pedido</c> de Tienda: una lista no se guarda por sí
/// sola en la saga genérica, y la respuesta del viaje tiene que decir qué se reservó sin preguntárselo al navegador.</para>
///
/// <para><b>Y lo de la confirmación parcial</b> (historia 3), opcional: el <c>precio</c> que cotizó el servidor por cada ítem,
/// y, si no se cumplió, <c>noCumplido</c> y lo <c>devuelto</c>. Se escriben sólo cuando hay algo que decir, así que un viaje
/// de antes —o uno que se confirmó entero— se lee y se escribe igual que siempre.</para>
///
/// <para>No es un dato personal: producto, nombre en pantalla, periodo y dinero del viaje.</para>
/// </remarks>
public sealed class TipoItinerario : ITipoDeValor
{
    private static readonly string[] Obligatorios = ["producto", "etiqueta", "inicio", "fin"];
    private static readonly string[] Campos = [.. Obligatorios, "precio", "noCumplido", "devuelto"];

    public string Nombre => "itinerario";
    public Type Clr => typeof(IReadOnlyList<ParadaDelViaje>);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: tiene que ser una lista.");

        var instante = new Instante();
        var dinero = new Dinero();
        var paradas = new List<ParadaDelViaje>();
        var i = 0;
        foreach (var p in valor.EnumerateArray())
        {
            var aqui = $"{donde}[{i++}]";
            if (p.ValueKind != JsonValueKind.Object) throw new FormatException($"{aqui}: tiene que ser un objeto {{ producto, etiqueta, inicio, fin }}.");
            foreach (var campo in p.EnumerateObject().Where(c => !Campos.Contains(c.Name, StringComparer.Ordinal)))
            {
                throw new FormatException($"{aqui}: trae «{campo.Name}», y sólo admite {string.Join(", ", Campos)}.");
            }

            paradas.Add(new ParadaDelViaje(
                Texto(p, "producto", aqui),
                Texto(p, "etiqueta", aqui),
                (DateTimeOffset)instante.Leer(Propiedad(p, "inicio", aqui), $"{aqui}.inicio"),
                (DateTimeOffset)instante.Leer(Propiedad(p, "fin", aqui), $"{aqui}.fin"),
                Opcional(p, "precio") is { } precio ? (Money)dinero.Leer(precio, $"{aqui}.precio") : null,
                NoCumplido(p, aqui),
                Opcional(p, "devuelto") is { } devuelto ? (Money)dinero.Leer(devuelto, $"{aqui}.devuelto") : null));
        }
        return (IReadOnlyList<ParadaDelViaje>)paradas;
    }

    public JsonNode Escribir(object valor)
    {
        var instante = new Instante();
        var dinero = new Dinero();
        return new JsonArray(((IReadOnlyList<ParadaDelViaje>)valor)
            .Select(p =>
            {
                var parada = new JsonObject
                {
                    ["producto"] = p.Producto,
                    ["etiqueta"] = p.Etiqueta,
                    ["inicio"] = instante.Escribir(p.Inicio),
                    ["fin"] = instante.Escribir(p.Fin),
                };
                if (p.Precio is { } precio) parada["precio"] = dinero.Escribir(precio);
                if (p.NoCumplido) parada["noCumplido"] = true;
                if (p.Devuelto is { } devuelto) parada["devuelto"] = dinero.Escribir(devuelto);
                return (JsonNode)parada;
            })
            .ToArray());
    }

    public JsonObject Esquema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(Obligatorios.Select(c => (JsonNode?)c).ToArray()),
            ["properties"] = new JsonObject
            {
                ["producto"] = new JsonObject { ["type"] = "string" },
                ["etiqueta"] = new JsonObject { ["type"] = "string" },
                ["inicio"] = new Instante().Esquema(),
                ["fin"] = new Instante().Esquema(),
                ["precio"] = new Dinero().Esquema(),
                ["noCumplido"] = new JsonObject { ["type"] = "boolean" },
                ["devuelto"] = new Dinero().Esquema(),
            },
        },
    };

    private static JsonElement Propiedad(JsonElement objeto, string nombre, string donde)
        => objeto.TryGetProperty(nombre, out var p) ? p : throw new FormatException($"{donde}: falta «{nombre}».");

    /// <summary>Un campo opcional: ausente o nulo es «no está».</summary>
    private static JsonElement? Opcional(JsonElement objeto, string nombre)
        => objeto.TryGetProperty(nombre, out var p) && p.ValueKind != JsonValueKind.Null ? p : null;

    private static bool NoCumplido(JsonElement objeto, string donde) => Opcional(objeto, "noCumplido") switch
    {
        null => false,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        _ => throw new FormatException($"{donde}.noCumplido: tiene que ser sí o no."),
    };

    private static string Texto(JsonElement objeto, string nombre, string donde)
        => Propiedad(objeto, nombre, donde) is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}.{nombre}: tiene que ser un texto no vacío.");
}
