using Synergos.Bff.Core.Flow;
using Synergos.Bff.Tienda.Contracts;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// <c>tienda.revisar-lineas</c> (004-tienda-compra), el único paso de dominio de la compra declarada: los bordes
/// que la compra contra capacidades reales no distingue (50 líneas pasan, 51 no).
/// </summary>
public sealed class PasoRevisarLineasTests
{
    private static Task<SalidaDePaso> Revisar(params (string Producto, int Cantidad)[] lineas)
    {
        var paso = FlujosDeclarados.Incrustado(typeof(PurchaseResponse).Assembly, "flujos/tienda.compra.json").Pasos["revisar"];
        IReadOnlyList<FlowContext> items = lineas
            .Select(l => new FlowContext().Set("producto", Ref.Create("tienda.producto", l.Producto)).Set("cantidad", l.Cantidad))
            .ToList();
        var ctx = new FlowContext().Set("lineas", items);
        return new PasoRevisarLineas().EjecutarAsync(
            new EntradaDePaso("saga-1", Ref.Create("tienda.compra", "saga-1"), paso, ctx, null, null), CancellationToken.None);
    }

    [Fact]
    public async Task Cincuenta_lineas_pasan_y_salen_como_se_cotizan()
    {
        var salida = await Revisar(Enumerable.Range(0, 50).Select(i => ($"p-{i}", i + 1)).ToArray());

        Assert.Equal(PasoResultado.Continuar, salida.Control);
        var aCotizar = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(salida.Valores.Single());
        Assert.Equal(50, aCotizar.Count);
        Assert.Equal((Ref.Create("tienda.producto", "p-49"), 50), aCotizar[49]);
    }

    [Fact]
    public async Task Cincuenta_y_una_lineas_se_rechazan()
    {
        var salida = await Revisar(Enumerable.Range(0, 51).Select(i => ($"p-{i}", 1)).ToArray());

        Assert.Equal(PasoResultado.Abortar, salida.Control);
        Assert.Equal("tienda.cart_too_large", salida.Rechazo!.Code);
    }

    [Fact]
    public async Task Una_cantidad_negativa_se_rechaza()
    {
        var salida = await Revisar(("p-1", -1));

        Assert.Equal("tienda.bad_line", salida.Rechazo!.Code);
    }
}
