namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-blogs&gt;</c>, la red social editorial: feed, autores, historias y reacciones.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Blogs</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El subtítulo viaja y se pinta bajo el título del feed, como en academy: el editor lo escribió
/// en los 11 bloques de la base (medido el 2026-10-03) y no llegaba a ninguna parte.</para>
/// </remarks>
[ElementoSynHost("blogs", TipoDeColocable.Funcionalidad)]
public sealed record BlogsProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
