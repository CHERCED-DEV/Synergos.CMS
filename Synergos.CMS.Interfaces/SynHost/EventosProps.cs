namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-eventos&gt;</c>, la app de eventos: cartelera, compra con
/// e-ticket y la consola del organizador.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134), la del piloto de la ADR 0137 (#194): del editor
/// recibe su título, su subtítulo y con qué cara abre; la configuración de NEGOCIO —dónde vive su
/// API y las dos comisiones— sale de <c>Synergos:Features:Eventos</c>, por sitio, y el editor no la
/// ve. Antes la comisión estaba compilada en el bundle y sólo la cambiaba un editor tecleando JSON
/// en el campo <c>config</c>, que tampoco viaja ya (una funcionalidad no recibe JSON libre, ADR 0135
/// §6).</para>
///
/// <para><b>Lo que NO viaja, y por qué.</b> La moneda es un dato del precio: la manda el catálogo
/// con cada importe. <c>scope</c> es el prefijo de las rutas por hash del elemento, de runtime. Las
/// dos estaban en la lista heredada del informe 16 §7.3 y el piloto las midió fuera.</para>
///
/// <para><b>Su microcopia sale del diccionario, sección <c>Events.Sale</c></b> (ADR 0136, #195): lo
/// que dice una localidad fuera de su ventana de venta —«Aún no está a la venta», «Venta cerrada»—,
/// que el CMS decide con <c>saleOpensAt</c>/<c>saleClosesAt</c>/<c>onSale</c>.</para>
/// </remarks>
[ElementoSynHost("eventos", TipoDeColocable.Funcionalidad, Diccionario = ["Events.Sale"])]
public sealed record EventosProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Role,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] decimal FeePercent,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] decimal PlatformFeePercent);
