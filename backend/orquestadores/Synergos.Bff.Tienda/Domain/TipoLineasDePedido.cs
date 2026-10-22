using System.Text.Json;
using System.Text.Json.Nodes;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// <c>lineas-de-pedido</c>: las líneas con el precio que cotizó <c>Api.Pricing</c>, como las guarda la saga
/// genérica y las devuelve la compra.
/// </summary>
/// <remarks>
/// <para><b>Interino, de la necesidad N-12 al núcleo.</b> El paso común <c>pricing.precios-por-linea</c>
/// escribe <see cref="LineaDePedido"/>, pero ningún tipo de valor registrado las sabe guardar: sin él, la
/// compra no podría decir qué se compró a qué precio, y «Mis compras» y la devolución las necesitan del
/// lado que cobró, no del navegador. Cuando el núcleo lo registre, este fichero y su línea en
/// <c>Program.cs</c> se van.</para>
///
/// <para>No es un dato personal: sujeto, cantidad y precio.</para>
/// </remarks>
public sealed class TipoLineasDePedido : ITipoDeValor
{
    public string Nombre => "lineas-de-pedido";
    public Type Clr => typeof(IReadOnlyList<LineaDePedido>);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: tiene que ser una lista.");

        var lineas = new List<LineaDePedido>();
        var i = 0;
        foreach (var item in valor.EnumerateArray())
        {
            var aqui = $"{donde}[{i++}]";
            var sujeto = Propiedad(item, "sujeto", aqui);
            var precio = Propiedad(item, "precioUnitario", aqui);
            var cantidad = Propiedad(item, "cantidad", aqui);
            if (!cantidad.TryGetInt32(out var n)) throw new FormatException($"{aqui}.cantidad: tiene que ser un entero.");
            if (!Propiedad(precio, "amount", $"{aqui}.precioUnitario").TryGetDecimal(out var monto))
            {
                throw new FormatException($"{aqui}.precioUnitario.amount: tiene que ser un número.");
            }

            lineas.Add(new LineaDePedido(
                Ref.Create(Texto(sujeto, "kind", $"{aqui}.sujeto"), Texto(sujeto, "id", $"{aqui}.sujeto")),
                n,
                Money.Of(monto, Texto(precio, "currency", $"{aqui}.precioUnitario"))));
        }
        return (IReadOnlyList<LineaDePedido>)lineas;
    }

    public JsonNode Escribir(object valor)
        => new JsonArray(((IReadOnlyList<LineaDePedido>)valor).Select(l => (JsonNode)new JsonObject
        {
            ["sujeto"] = new JsonObject { ["kind"] = l.Sujeto.Kind, ["id"] = l.Sujeto.Id },
            ["cantidad"] = l.Cantidad,
            ["precioUnitario"] = new JsonObject
            {
                ["amount"] = JsonValue.Create(l.PrecioUnitario.Amount),
                ["currency"] = l.PrecioUnitario.Currency,
            },
        }).ToArray());

    public JsonObject Esquema() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("sujeto", "cantidad", "precioUnitario"),
            ["properties"] = new JsonObject
            {
                ["sujeto"] = Objeto(("kind", "string"), ("id", "string")),
                ["cantidad"] = new JsonObject { ["type"] = "integer", ["format"] = "int32" },
                ["precioUnitario"] = Objeto(("amount", "number"), ("currency", "string")),
            },
        },
    };

    private static JsonObject Objeto(params (string Nombre, string Tipo)[] campos) => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray(campos.Select(c => (JsonNode)c.Nombre).ToArray()),
        ["properties"] = new JsonObject(campos.Select(c =>
            KeyValuePair.Create(c.Nombre, (JsonNode?)new JsonObject { ["type"] = c.Tipo }))),
    };

    private static JsonElement Propiedad(JsonElement objeto, string nombre, string donde)
        => objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(nombre, out var p)
            ? p
            : throw new FormatException($"{donde}: falta «{nombre}».");

    private static string Texto(JsonElement objeto, string nombre, string donde)
        => Propiedad(objeto, nombre, donde) is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}.{nombre}: tiene que ser un texto no vacío.");
}
