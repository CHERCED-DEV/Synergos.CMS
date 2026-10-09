namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-alquiler&gt;</c>, el alquiler de equipos: catálogo, tarifas por
/// duración, reserva con la garantía retenida y el contrato sellado (#147).
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe su contenido y qué familia de
/// equipos acotar; dónde vive su API sale de <c>Synergos:Features:Alquiler</c>, por sitio (ADR
/// 0137). El piloto nació antes de esas ADR, con la API y un JSON libre como campos del editor; el
/// tipo nunca se importó a ninguna base, así que quitarlos no pierde nada que alguien escribiera.</para>
///
/// <para><b>La moneda no viaja</b>: llega con cada precio de la API, como en las otras ocho.</para>
///
/// <para><b>Sus textos</b> son la sección <c>Alquiler</c> del diccionario (ADR 0136): la app los
/// pide con <c>t()</c> y la página publica sólo las secciones que declaran sus elementos.</para>
/// </remarks>
[ElementoSynHost("alquiler", TipoDeColocable.Funcionalidad, Diccionario = ["Alquiler"])]
public sealed record AlquilerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Category,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase);
