using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La salida propia que le queda a la renta declarada (línea 005, T011): la garantía se cierra sin capturarse
/// (NN-4). La de las unidades (NN-3) se retiró con N2; sus llaves las vigila el oráculo declarado.
/// </summary>
public sealed class SalidasPropiasDeAlquilerTests
{
    private static readonly PasoDef Conservar = new(
        "conservar", "alquiler.conservar-garantia", ["garantiaId"], [], null, null, null, "retener", null);

    [Fact]
    public async Task Conservar_la_garantia_sigue_sin_tocar_ninguna_capacidad()
    {
        var r = await new PasoConservarGarantia().EjecutarAsync(new EntradaDePaso(
            "alq-1", Ref.Create("alquiler.renta", "alq-1"), Conservar, new FlowContext().Set("garantiaId", "pg-garantia"), null, null),
            CancellationToken.None);

        Assert.Equal(PasoResultado.Continuar, r.Control);
        Assert.Empty(r.Valores);
        Assert.Null(r.Reservado);
    }

    [Fact]
    public void La_salida_declara_lo_que_el_flujo_le_pide()
    {
        var conservar = Assert.Single(AlquilerSalidasPropias.Pasos());

        Assert.Equal("alquiler.conservar-garantia", conservar.Tipo);
        Assert.Equal(LlaveRequerida.Ninguna, conservar.Llave);
        Assert.Equal(1, conservar.Lecturas);
    }
}
