using Synergos.Bff.Core.Flow;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// La salida que la renta declarada escribe mientras el núcleo no la tenga (línea 005): anotar el alquiler con su dueño
/// para que «mis alquileres» lo encuentre (NN-7, T024). Cuenta como pegamento atribuido a esa necesidad.
/// </summary>
/// <remarks>
/// Las otras dos se retiraron cuando el núcleo las dio: apartar N unidades idénticas con llaves distintas (NN-3) con N2
/// (<c>booking.apartar-por-indice</c>), y cerrar la garantía sin capturarla (NN-4) con el lote 6
/// (<c>payments.conservar</c>, P7 = A).
/// </remarks>
public static class AlquilerSalidasPropias
{
    /// <summary>Las salidas, para sumarlas a los pasos propios del orquestador.</summary>
    /// <param name="duenos">El índice de los alquileres de la puerta por dueño (NN-7, T024).</param>
    public static IPaso[] Pasos(AlquileresDeLaPuerta duenos) => [new PasoAnotarDueno(duenos)];
}
