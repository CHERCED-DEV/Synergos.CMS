namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-ehr&gt;</c>, el portal clínico: paciente, médico y enfermería.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Ehr</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El ElementType ya no ofrece título ni subtítulo (#196): cada vista del portal trae su propio
/// encabezado (decisión anterior, anotada en la vista vieja), así que el editor los escribía en los 11
/// bloques de la base (medido el 2026-10-03) y no llegaban a ninguna parte.</para>
///
/// <para><b>Y ya no viaja el paciente</b> (#197). Salía de la vista —<c>pat-jorge-medina</c>, un
/// paciente de demo escrito a mano— y el portal lo mandaba de vuelta en cada petición, que es como
/// cualquiera leía la historia de otro. Hoy el servidor resuelve el paciente desde la sesión del
/// miembro y no lee el que mande el navegador, así que el elemento no tiene nada que recibir.</para>
///
/// <para><b>Y por dónde agenda</b> (<c>AppointmentPath</c>, 003-salud-cita): la puerta (<c>salud.cita</c>) o la
/// ruta directa <c>/api/ehr/appointment</c>, que se conservan las dos. Lo DECLARA el CMS con la regla de
/// <see cref="CaminoDeCompra"/> —el override de <c>Synergos:Features:Ehr:AppointmentPath</c>, o la puerta
/// cuando abre <c>salud.cita</c>, o la directa—, como el camino de compra de Eventos; el elemento no lo
/// adivina.</para>
///
/// <para><b>Y en qué zona se lee la hora</b> (<c>Zona</c>, la de <c>Synergos:Listados:ZonaHoraria</c>): por la
/// puerta la franja viaja como instante, y el «10:00» que elige el paciente es la hora del sitio, no la del
/// navegador. Sus textos de la cita son la sección <c>Health.Appointment</c> del diccionario (ADR 0136).</para>
///
/// <para><b>Y de dónde lee</b> (<c>LecturasBase</c>, 003-salud-cita, I1 de producción): la base de las lecturas
/// genéricas de Salud (<c>/api/lecturas/salud</c>), con forma neutra de vertical, como la tienda. Vacía, el portal lee
/// las rutas de <c>ApiBase</c>, como antes. Es del despliegue.</para>
///
/// <para><b>Y dónde vive la historia</b> (<c>HistoriaBase</c>, I2 T062): <c>/api/healthcare</c>, la historia clínica de
/// producción, de donde salen «Mi salud», el check-in y las vistas del personal. Vacía, el portal vuelve a lo de
/// <c>ApiBase</c>.</para>
///
/// <para><b>Y cuánto devuelve cancelar una cita confirmada</b> (<c>Devolucion*</c>, I6): la política del sitio, la misma que la
/// puerta manda a <c>salud.cancelacion</c>. El elemento la usa para decir lo que se devolverá antes de cancelar; lo que se
/// devuelve de verdad lo calcula el orquestador.</para>
///
/// <para><b>Y si la petición llegó sin sesión</b> (<c>SinSesion</c>, vuelta de valor de 003): lo sabe el servidor al pintar
/// la página, la misma que ya lleva dentro el panel «Inicia sesión» como reserva (D-012-V11). Con él la app arranca en ese
/// panel, sin pintar antes el inicio ni pedir lecturas que van a contestar 401 (el salto de 0,06 a 1280 px). Es una
/// pista para pintar, no un permiso: cada lectura la sigue decidiendo el servidor.</para>
/// </remarks>
[ElementoSynHost("ehr", TipoDeColocable.Funcionalidad, Diccionario = ["Health.Appointment", "Health.Consent", "Health.Record"])]
public sealed record EhrProps(
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string LecturasBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string AppointmentPath,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string Zona,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string HistoriaBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] int DevolucionTotalHoras = 24,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] int DevolucionParcialHoras = 2,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] int DevolucionParcialPorcentaje = 50,
    [property: CampoSynHost(OrigenDelCampo.Sesion)] bool SinSesion = false,
    // El nivel del título (D-016-Y4): 1 cuando el editor marcó «Es el título de la página»; sin él, la pieza usa el 2.
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? HeadingLevel = null);
