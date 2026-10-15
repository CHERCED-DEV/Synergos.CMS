using Synergos.Bff.Core.Flow;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// La salida que la renta declarada escribe mientras el núcleo no la tenga (línea 005, T011): cerrar la garantía
/// sin capturarla (NN-4). Cuenta como pegamento atribuido a esa necesidad; cuando el núcleo la dé (la forma
/// genérica de T051), se reemplaza por la común y queda declarada.
/// </summary>
/// <remarks>
/// La otra salida de T011 —apartar N unidades idénticas con llaves distintas (NN-3)— se retiró con N2: el flujo
/// usa <c>booking.apartar-por-indice</c> y <c>booking.confirmar-por-indice</c>, con las llaves de
/// <c>RentalFlow</c> (<c>hold:{i}</c>, <c>confirm:{i}</c>).
/// </remarks>
public static class AlquilerSalidasPropias
{
    /// <summary>Las salidas, para sumarlas a los pasos propios del orquestador.</summary>
    public static IPaso[] Pasos() => [new PasoConservarGarantia()];
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
