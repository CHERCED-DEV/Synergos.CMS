namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-breadcrumb&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>itemsJson</c> —el TEXTO del
/// TextArea, con <c>label</c>/<c>url</c> por paso— y el elemento lee <c>items</c>, una LISTA de
/// <c>label</c>/<c>href</c>: las migas colocadas hidrataban vacías, y sin pasos tampoco sale el
/// JSON-LD <c>BreadcrumbList</c> que el editor encendió.</para>
///
/// <para>El último paso es la página actual: el elemento le quita el enlace aunque lo traiga. El
/// nombre accesible de la navegación (<c>label</c>) y el <c>separator</c> no los autora el
/// ElementType: quedan como atributo.</para>
/// </remarks>
[ElementoSynHost("breadcrumb", TipoDeColocable.Pieza)]
public sealed record BreadcrumbProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<BreadcrumbStep>? Items,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? IncludeStructuredData);

/// <summary>Un paso de las migas: su texto y, si no es la página actual, su enlace.</summary>
public sealed record BreadcrumbStep(string Label, string? Href = null);
