namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-seller&gt;</c>, la consola del vendedor: ventas, publicaciones, mensajes y devoluciones.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Seller</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El ElementType tiene también `subheading`: la vista lo mandaba y el elemento lo tiraba (D1, medido el 2026-10-02 en los 11 bloques). No se declara acá.</para>
/// </remarks>
[ElementoSynHost("seller", TipoDeColocable.Funcionalidad)]
public sealed record SellerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
