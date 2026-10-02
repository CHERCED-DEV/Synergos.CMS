namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-realty&gt;</c>, el portal inmobiliario: búsqueda en lista y mapa,
/// ficha, simulador de hipoteca, visitas y el escritorio del agente.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe su título y su subtítulo; la
/// configuración de NEGOCIO —dónde vive su API y la tasa con que arranca el simulador— sale de
/// <c>Synergos:Features:Realty</c>, por sitio (ADR 0137, escala #196). El JSON libre del editor
/// (<c>config</c>) no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para><b>Lo que no viaja, y por qué.</b> La moneda es un dato del precio y la manda el catálogo
/// con cada inmueble. La cara, la operación y el diseño de resultados (<c>role</c>,
/// <c>operation</c>, <c>layout</c>) sólo entraban por ese JSON: quedan como valores del componente
/// hasta que un editor necesite elegirlos, y entonces entran como selector.</para>
/// </remarks>
[ElementoSynHost("realty", TipoDeColocable.Funcionalidad)]
public sealed record RealtyProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] decimal DefaultRatePercent);
