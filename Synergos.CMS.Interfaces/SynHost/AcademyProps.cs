namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-academy&gt;</c>, la academia online: catálogo, lecciones, instructores e inscripciones.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Academy</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para><b>Y por dónde se matricula</b> (<c>EnrollmentPath</c>, 010-educacion-matricula): la puerta
/// (<c>educacion.matricula</c>) o la ruta directa de la academia, que se conservan las dos. Lo DECLARA el CMS —sin
/// valor, la directa— y el elemento lo sigue sin caer nunca de un camino al otro. Sus textos de la matrícula por la
/// puerta son la sección <c>Academy.Enrollment</c> del diccionario (ADR 0136).</para>
/// </remarks>
[ElementoSynHost("academy", TipoDeColocable.Funcionalidad, Diccionario = ["Academy.Enrollment"])]
public sealed record AcademyProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string EnrollmentPath);
