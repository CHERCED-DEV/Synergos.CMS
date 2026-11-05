namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de el portal clínico ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="Servicio">Qué se agenda en el sitio (003-salud-cita): la puerta lo manda a la cita declarada
/// en <c>X-Synergos-Negocio</c>, así que el navegador no lo elige ni lo cambia. Su precio publicado es el
/// copago.</param>
/// <param name="AppointmentPath">Por dónde agenda el elemento si el despliegue lo DECLARA (<c>puerta</c> o
/// <c>directa</c>, ya normalizado); nulo si decide la regla de <see cref="CaminoDeCompra"/> (003-salud-cita).</param>
/// <param name="LecturasBase">La base de las lecturas genéricas de Salud (003-salud-cita, I1 de producción); vacía, el
/// portal vuelve a leer las rutas de <see cref="ApiBase"/>.</param>
/// <param name="VersionDelPermiso">La versión del texto del permiso de datos de salud que se muestra en el sitio (T048): la
/// puerta la manda a <c>salud.consentimiento</c> como negocio y llega a <c>Api.Consent</c> con el permiso. El navegador
/// no la elige.</param>
public sealed record NegocioDeEhr(
    string ApiBase,
    ServicioAgendable Servicio,
    string? AppointmentPath = null,
    string LecturasBase = "",
    string VersionDelPermiso = "");

/// <summary>
/// El servicio que se agenda, con la forma de un <c>Ref</c> del backend (<c>{ kind, id }</c>): es como lo lee
/// el tipo <c>ref</c> de la definición de <c>salud.cita</c>.
/// </summary>
public sealed record ServicioAgendable(string Kind, string Id);
