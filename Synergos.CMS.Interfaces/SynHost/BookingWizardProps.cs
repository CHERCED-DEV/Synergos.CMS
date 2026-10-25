namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-booking-wizard&gt;</c>, el asistente de reservas de estadías.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:BookingWizard</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>La moneda NO viaja: era un campo del editor (`currency`, «COP» en los 35 bloques medidos) y es un dato del precio, que la API manda con cada tarifa (ADR 0137, cambio 4).</para>
///
/// <para><b>Y por dónde reserva</b> (<c>BookingPath</c>, ADR 0140; línea 006): la puerta (<c>viajes.reserva</c>, un
/// viaje de un ítem) o la ruta directa (<c>/api/booking/…</c>). Lo DECLARA el CMS con la regla de
/// <see cref="CaminoDeCompra"/>, «directa» por defecto; el elemento no lo adivina.</para>
/// </remarks>
[ElementoSynHost("booking-wizard", TipoDeColocable.Funcionalidad, Diccionario = ["Travel.Booking"])]
public sealed record BookingWizardProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? DestinationLabel,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string BookingPath);
