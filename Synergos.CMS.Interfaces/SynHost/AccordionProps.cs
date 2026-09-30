namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-accordion&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>itemsJson</c> —el TEXTO del
/// TextArea, con <c>title</c>/<c>content</c> por sección— y el elemento lee <c>items</c>, una
/// LISTA de <c>title</c>/<c>body</c>: el acordeón colocado hidrataba vacío, sin una sola sección.
/// Ahora el resolver parsea ese texto en el servidor y viaja la lista tipada.</para>
///
/// <para><c>headingLevel</c> y, por sección, <c>id</c> y <c>open</c> los sabe pintar el elemento
/// pero el ElementType no los autora: no viajan y quedan como atributo o como el valor por defecto
/// del elemento (nivel 3, ids generados, todo cerrado).</para>
/// </remarks>
[ElementoSynHost("accordion", TipoDeColocable.Pieza)]
public sealed record AccordionProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<AccordionSection>? Items,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? AllowMultiple);

/// <summary>Una sección del acordeón: su encabezado y el texto que despliega.</summary>
public sealed record AccordionSection(string Title, string? Body = null);
