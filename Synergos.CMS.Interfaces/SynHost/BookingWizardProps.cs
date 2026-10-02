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
/// </remarks>
[ElementoSynHost("booking-wizard", TipoDeColocable.Funcionalidad)]
public sealed record BookingWizardProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? DestinationLabel,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
