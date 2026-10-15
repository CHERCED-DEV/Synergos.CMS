namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de el portal clínico ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="Servicio">Qué se agenda en el sitio (003-salud-cita): la puerta lo manda a la cita declarada
/// en <c>X-Synergos-Negocio</c>, así que el navegador no lo elige ni lo cambia. Su precio publicado es el
/// copago.</param>
public sealed record NegocioDeEhr(string ApiBase, ServicioAgendable Servicio);

/// <summary>
/// El servicio que se agenda, con la forma de un <c>Ref</c> del backend (<c>{ kind, id }</c>): es como lo lee
/// el tipo <c>ref</c> de la definición de <c>salud.cita</c>.
/// </summary>
public sealed record ServicioAgendable(string Kind, string Id);
