namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-ehr&gt;</c>, el portal clínico: paciente, médico y enfermería.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Ehr</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El título y el subtítulo del ElementType no se mandan: cada vista del portal trae su propio encabezado (decisión anterior, anotada en la vista vieja).</para>
/// </remarks>
[ElementoSynHost("ehr", TipoDeColocable.Funcionalidad)]
public sealed record EhrProps(
    [property: CampoSynHost(OrigenDelCampo.Sesion)] string? Patient,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
