namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-travel-shell&gt;</c>, el portal de viajes: estadías, vuelos y autos.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:TravelShell</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para><b>Y por dónde reserva</b> (<c>BookingPath</c>, ADR 0140; línea 006): la puerta
/// (<c>/api/flujos/viajes.reserva/…</c>) o la ruta directa (<c>/api/travel/…</c>), que se conservan las dos. Lo
/// DECLARA el CMS con la regla de <see cref="CaminoDeCompra"/> —«directa» por defecto en
/// <c>Synergos:Features:TravelShell:BookingPath</c>, la puerta sólo donde un sitio la declara—; el elemento no lo
/// adivina.</para>
///
/// <para><b>Sus textos de la reserva por la puerta</b> son la sección <c>Travel.Booking</c> del diccionario (ADR 0136):
/// la app los pide con <c>t()</c> y la página publica sólo las secciones que declaran sus elementos.</para>
/// </remarks>
[ElementoSynHost("travel-shell", TipoDeColocable.Funcionalidad, Diccionario = ["Travel.Booking"])]
public sealed record TravelShellProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string BookingPath);
