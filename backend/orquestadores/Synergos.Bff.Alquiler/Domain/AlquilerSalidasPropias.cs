using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// Las dos salidas que la renta declarada escribe mientras el núcleo no las tenga (línea 005, T011): apartar N
/// unidades idénticas sin que compartan llave (NN-3) y cerrar la garantía sin capturarla (NN-4). Cuentan como
/// pegamento atribuido a esas necesidades; cuando el núcleo las dé, se reemplazan por las comunes y quedan
/// declaradas.
/// </summary>
public static class AlquilerSalidasPropias
{
    /// <summary>Las salidas, para sumarlas a los pasos propios del orquestador.</summary>
    public static IPaso[] Pasos(IBookingPort agenda) => [new PasoApartarUnidad(agenda), new PasoConservarGarantia()];
}

/// <summary>
/// <c>alquiler.apartar-unidad</c>: aparta UNA unidad del equipo en la ventana, con la llave de SU número
/// (<c>{saga}|hold:{unidad}</c>). RESERVA.
/// </summary>
/// <remarks>
/// Es <c>booking.apartar-por-item</c> con otro discriminante (NN-3 de la línea 005, T047 del núcleo): aquél resume
/// el recurso y la ventana, y N unidades del mismo equipo en las mismas fechas son N ítems IDÉNTICOS que
/// compartirían llave —la segunda devolvería el apartado de la primera—.
/// </remarks>
public sealed class PasoApartarUnidad(IBookingPort agenda) : IPaso
{
    public string Tipo => "alquiler.apartar-unidad";
    public int Lecturas => 4;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.PorItem;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var unidad = entrada.Lee<int>(3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var apartado = await agenda.ApartarAsync(
            entrada.Lee<string>(0), entrada.Lee<TimeWindow>(1), entrada.Lee<Ref>(2), entrada.LlavePara(unidad), ct);
        return apartado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(apartado.Value), apartado.Value)
            : SalidaDePaso.Rechaza(apartado.Rejection!);
    }
}

/// <summary>
/// <c>alquiler.conservar-garantia</c>: CIERRA la reserva de la garantía SIN capturarla. La autorización queda viva
/// fuera de la saga, con su id en la salida, para que la resuelva la devolución días después.
/// </summary>
/// <remarks>
/// Es lo que <c>RentalFlow</c> hacía al vaciar las compensaciones al reservar: la saga se cierra y el barrido no la
/// toca, pero la garantía sigue retenida. Va al FINAL de <c>cerrar</c>, después de confirmar las unidades: si
/// confirmar falla, la garantía todavía se anula como compensación. Es <c>payments.conservar</c> (NN-4 de la línea
/// 005, la forma genérica de T051 del núcleo) mientras el núcleo no lo tenga.
/// </remarks>
public sealed class PasoConservarGarantia : IPaso
{
    public string Tipo => "alquiler.conservar-garantia";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct) => Task.FromResult(SalidaDePaso.Sigue());
}
