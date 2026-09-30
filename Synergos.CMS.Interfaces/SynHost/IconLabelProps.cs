namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-icon-label&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>iconKey</c> y el elemento lee
/// <c>iconName</c> (el icono con nombre del set; <c>iconSymbol</c> es un glifo suelto, que el
/// ElementType no autora): el texto llegaba y el icono que eligió el editor se perdía al
/// hidratar.</para>
///
/// <para>El enlace, el destino, el tamaño, el tono, el espaciado, el icono al final y el modo
/// botón los sabe pintar el elemento pero el ElementType no los autora: quedan como atributo. El
/// <c>ariaLabel</c> de <c>compDomAttributes</c> la vista no lo mandaba y ya lo aplica el envoltorio
/// SynHost: no viaja.</para>
/// </remarks>
[ElementoSynHost("icon-label", TipoDeColocable.Pieza)]
public sealed record IconLabelProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? IconName,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? LabelText);
