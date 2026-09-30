namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-share-bar&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>platformsCsv</c> (las redes unidas
/// con comas) y <c>shareUrl</c>; el elemento lee <c>platforms</c> y <c>shareLink</c>. La barra
/// colocada ignoraba las redes que eligió el editor —pintaba las cinco de fábrica— y compartía la
/// página actual en vez del destino que él puso.</para>
///
/// <para><b><c>platforms</c> es una LISTA</b> (el desplegable múltiple del editor), no un texto con
/// comas. <b>El nombre de una red lo pone el elemento</b> (ADR 0083): el DataType dice
/// <c>twitter</c> y el elemento la llama <c>x</c>, así que el resolver lo traduce. Las demás viajan
/// como las marcó el editor; el DataType ofrece además <c>copy</c>, <c>reddit</c> y
/// <c>pinterest</c>, que el elemento no pinta como red (copiar el enlace es una acción que ya
/// tiene siempre) y descarta.</para>
///
/// <para><b><c>shareLink</c> es el destino del enlace</b>; vacío, el elemento comparte la página
/// actual. <c>shareTitle</c> vacío usa el título del documento.</para>
/// </remarks>
[ElementoSynHost("share-bar", TipoDeColocable.Pieza)]
public sealed record ShareBarProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] IReadOnlyList<string>? Platforms,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? ShareLink,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? ShareTitle);
