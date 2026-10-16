namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-reserva&gt;</c>, la reserva de un servicio por franja con cobro, por la puerta
/// (011-booking-reserva).
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe su título, su subtítulo, el servicio que se
/// reserva (una ficha reservable que elige) y el enlace alternativo para cuando la reserva en línea está apagada;
/// el camino sale de <c>Synergos:Features:Reserva</c> (ADR 0137) y la zona, de la del sitio.</para>
///
/// <para><b>Lo que NO viaja</b>: el precio. Lo que se cobra lo dice la cotización del servidor (<c>cotizar</c>), no
/// el precio de lista de la ficha (principio VI).</para>
///
/// <para><b>Sus textos</b> son la sección <c>Booking.Slot</c> del diccionario (ADR 0136).</para>
/// </remarks>
[ElementoSynHost("reserva", TipoDeColocable.Funcionalidad, Diccionario = ["Booking.Slot"])]
public sealed record ReservaProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] ServicioReservable? Servicio,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? EnlaceAlternativo,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string Camino,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string Zona);

/// <summary>
/// El servicio que se reserva, tal como lo declaró el editor en su ficha (la composición «reservable»).
/// </summary>
/// <param name="Id">La clave estable de la ficha: el identificador que el flujo recibe.</param>
/// <param name="Nombre">El nombre de la ficha.</param>
/// <param name="DuracionMinutos">Lo que dura una franja.</param>
/// <param name="Dias">Los días que se atiende, en inglés (<c>Monday</c>…), como los lee la agenda.</param>
/// <param name="Abre">Desde qué hora (<c>HH:mm</c>).</param>
/// <param name="Cierra">Hasta qué hora (<c>HH:mm</c>).</param>
public sealed record ServicioReservable(
    string Id,
    string Nombre,
    int DuracionMinutos,
    IReadOnlyList<string> Dias,
    string Abre,
    string Cierra);
