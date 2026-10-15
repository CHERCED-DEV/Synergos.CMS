namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Quién sabe deshacer una lista cerrada de tipos de compensación (núcleo N1): contra ella se cruzan el
/// <c>antes</c> y el <c>despues</c> de cada reserva de un flujo genérico, AL ARRANCAR.
/// </summary>
/// <remarks>
/// Lo implementa el ejecutor de la saga genérica (<c>DeshacerComun</c> de <c>Bff.Pasos</c>). Sin esto, una
/// compensación mal escrita en el JSON no fallaría hasta la primera vez que hubiera que deshacer: el día
/// en que más caro sale.
/// </remarks>
public interface IConoceSusTipos
{
    /// <summary>Los tipos de compensación que sabe deshacer.</summary>
    IReadOnlyCollection<string> Tipos { get; }
}
