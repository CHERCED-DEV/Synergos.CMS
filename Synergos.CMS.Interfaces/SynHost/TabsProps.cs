namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-tabs&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>tabsJson</c> —el TEXTO del
/// TextArea— y el elemento lee <c>tabs</c>, una LISTA: sin pestañas el elemento no pinta nada, y
/// las pestañas colocadas desaparecían al hidratar. Sólo <c>initialTab</c> llegaba.</para>
///
/// <para>Cada pestaña lleva lo que documenta el ElementType: <c>id</c> (el ancla del
/// <c>#hash</c> y lo que nombra <c>initialTab</c>), <c>label</c> y <c>content</c>, que el
/// elemento pinta como TEXTO. El elemento sabe además deshabilitar una (<c>disabled</c>), pero el
/// ElementType no lo ofrece: no viaja. La orientación no la autora el editor: es atributo.</para>
/// </remarks>
[ElementoSynHost("tabs", TipoDeColocable.Pieza)]
public sealed record TabsProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<TabsItem>? Tabs,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? InitialTab);

/// <summary>Una pestaña: su rótulo, su contenido y el id con el que se enlaza.</summary>
public sealed record TabsItem(string Label, string? Id = null, string? Content = null);
