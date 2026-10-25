namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-gov&gt;</c>, el portal de trámites: radicar, pagar la tasa y seguir el expediente.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Gov</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para><b>Sus textos nuevos</b> son la sección <c>Gov.Tramite</c> del diccionario (ADR 0136; 009): las
/// acciones del proceso, tomar y subsanar, la aceptación de la notificación electrónica con la versión
/// del texto, «no disponible», «inicie sesión» y un texto por cada rechazo de la puerta. La app los pide
/// con <c>t()</c> y la página publica sólo las secciones que declaran sus elementos.</para>
/// </remarks>
[ElementoSynHost("gov", TipoDeColocable.Funcionalidad, Diccionario = ["Gov.Tramite"])]
public sealed record GovProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
