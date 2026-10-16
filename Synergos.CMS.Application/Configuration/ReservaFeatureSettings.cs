using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la reserva de servicio (ADR 0137, 011-booking-reserva) — sección
/// <c>Synergos:Features:Reserva</c>.
/// </summary>
/// <remarks>
/// <para><b>Una sola clave, y es del despliegue</b>: si el sitio reserva en línea (<c>puerta</c>, por defecto) o no
/// (<c>apagada</c>). Permite salir a producción con la reserva apagada y encenderla tras la verificación en vivo
/// (011-P7). Una palabra que no es ninguna de las dos no deja arrancar.</para>
/// </remarks>
public sealed class ReservaFeatureSettings : SeccionDeNegocio<ReservaFeatureSitio, NegocioDeReserva>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Reserva";

    /// <summary>Por dónde reserva el elemento: <c>puerta</c> o <c>apagada</c>.</summary>
    public string Camino { get; init; } = CaminoDeReserva.Puerta;

    /// <inheritdoc />
    protected override NegocioDeReserva Fusionar(ReservaFeatureSitio? propio)
        => new(CaminoDeReserva.Normalizar(propio?.Camino ?? Camino));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeReserva negocio)
        => CaminoDeReserva.EsValido(negocio.Camino)
            ? []
            : [$"Camino: «{negocio.Camino}» tiene que ser «{CaminoDeReserva.Puerta}» o «{CaminoDeReserva.Apagada}»."];
}

/// <summary>Lo que un sitio cambia de <see cref="ReservaFeatureSettings"/>. Una clave sin valor la hereda.</summary>
public sealed class ReservaFeatureSitio
{
    /// <inheritdoc cref="ReservaFeatureSettings.Camino"/>
    public string? Camino { get; init; }
}
