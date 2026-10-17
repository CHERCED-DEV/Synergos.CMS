using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La salida propia que le queda a la renta declarada (línea 005): el alquiler se anota con su dueño para que «mis
/// alquileres» lo encuentre (NN-7, T024). La de las unidades (NN-3) se retiró con N2 y la de la garantía (NN-4) con el
/// lote 6 (<c>payments.conservar</c>, que vigila <c>ArranqueDeLaRentaTests</c>).
/// </summary>
public sealed class SalidasPropiasDeAlquilerTests : IDisposable
{
    private static readonly PasoDef Anotar = new("anotar", "alquiler.anotar", ["inquilino"], [], null, null, null, null, null);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "l005-duenos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true);
    }

    private AlquileresDeLaPuerta Indice()
        => new(Options.Create(new SagaStorageOptions { Root = _raiz }), TimeProvider.System);

    [Fact]
    public void Las_salidas_declaran_lo_que_el_flujo_les_pide()
    {
        var pasos = AlquilerSalidasPropias.Pasos(Indice());

        Assert.Equal(["alquiler.anotar"], pasos.Select(p => p.Tipo));
        Assert.All(pasos, p => Assert.Equal(LlaveRequerida.Ninguna, p.Llave));
        Assert.Equal([1], pasos.Select(p => p.Lecturas));
    }

    [Fact]
    public async Task Anotar_deja_el_alquiler_con_su_dueno_una_vez_y_solo_el_lo_ve()
    {
        var indice = Indice();
        var paso = new PasoAnotarDueno(indice);
        var mio = Ref.Create("alquiler.arrendatario", "m-1");
        var otro = Ref.Create("alquiler.arrendatario", "m-2");

        foreach (var (saga, dueno) in new[] { ("alq-1", mio), ("alq-1", mio), ("alq-2", otro) })
        {
            var r = await paso.EjecutarAsync(
                new EntradaDePaso(saga, Ref.Create("alquiler.renta", saga), Anotar, new FlowContext().Set("inquilino", dueno), null, null),
                CancellationToken.None);
            Assert.Equal(PasoResultado.Continuar, r.Control);
            Assert.Empty(r.Valores);
        }

        // Releído de disco por otra instancia: el índice sobrevive al reinicio.
        var releido = Indice();
        Assert.Equal(["alq-1"], releido.De(mio).Select(a => a.SagaId));
        Assert.Equal(["alq-2"], releido.De(otro).Select(a => a.SagaId));
        Assert.Empty(releido.De(Ref.Create("eventos.comprador", "m-1")));
    }
}
