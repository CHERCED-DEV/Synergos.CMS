using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// <c>tienda.revisar-lineas</c>: lo que una compra de la tienda tiene que cumplir antes de cotizar, y las
/// líneas como las cotiza <c>Api.Pricing</c>.
/// </summary>
/// <remarks>
/// <para><b>Es la regla del dominio que el flujo declarado no puede escribir en JSON</b> (sin parámetros,
/// decisión 001-P2): la misma que <see cref="PurchaseFlow"/> le aplica a una canasta, ahora sobre las
/// líneas que manda quien compra por la puerta. Los códigos son los de siempre, para que el storefront
/// diga lo mismo por los dos caminos.</para>
///
/// <para><b>Un producto repetido se rechaza, no se suma.</b> El apartado lleva la llave del ítem de
/// existencias: dos líneas del mismo producto compartirían llave, y la segunda devolvería el apartado de
/// la primera —se cobrarían las dos con mercancía para una—. Sumarlas en silencio cambiaría lo que el
/// comprador pidió sin decírselo.</para>
/// </remarks>
public sealed class PasoRevisarLineas : IPaso
{
    public string Tipo => "tienda.revisar-lineas";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var lineas = entrada.Lee<IReadOnlyList<FlowContext>>(0);

        if (lineas.Count == 0)
        {
            return Rechaza(Rejection.Invalid("tienda.cart_empty", "No se puede comprar una canasta vacía."));
        }
        if (lineas.Count > PurchaseFlow.MaxLines)
        {
            return Rechaza(Rejection.Invalid("tienda.cart_too_large",
                $"Una compra admite hasta {PurchaseFlow.MaxLines} líneas y ésta trae {lineas.Count}."));
        }

        var aCotizar = new List<(Ref Sujeto, int Cantidad)>(lineas.Count);
        foreach (var linea in lineas)
        {
            var producto = linea.Get<Ref>("producto");
            var cantidad = linea.Get<int>("cantidad");
            if (cantidad <= 0)
            {
                return Rechaza(Rejection.Invalid("tienda.bad_line", $"La cantidad de {producto.Id} tiene que ser mayor que cero."));
            }
            if (aCotizar.Any(l => l.Sujeto == producto))
            {
                return Rechaza(Rejection.Invalid("tienda.bad_line", $"{producto.Id} viene dos veces: va una línea por producto."));
            }
            aCotizar.Add((producto, cantidad));
        }

        return Task.FromResult(SalidaDePaso.Sigue((IReadOnlyList<(Ref Sujeto, int Cantidad)>)aCotizar));
    }

    private static Task<SalidaDePaso> Rechaza(Rejection motivo) => Task.FromResult(SalidaDePaso.Rechaza(motivo));
}
