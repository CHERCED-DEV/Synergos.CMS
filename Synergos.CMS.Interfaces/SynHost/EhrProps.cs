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
/// </remarks>
[ElementoSynHost("ehr", TipoDeColocable.Funcionalidad)]
public sealed record EhrProps(
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
