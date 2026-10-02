namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-blogs&gt;</c>, la red social editorial: feed, autores, historias y reacciones.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Blogs</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El ElementType tiene también `subheading` y el elemento no lo lee (medido el 2026-10-02: el editor lo escribió en los 11 bloques y no viajaba): no se declara acá.</para>
/// </remarks>
[ElementoSynHost("blogs", TipoDeColocable.Funcionalidad)]
public sealed record BlogsProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
