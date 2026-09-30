namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-fab&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>actionUrl</c> y <c>ariaLabel</c>;
/// el elemento lee <c>actionLink</c> y <c>label</c>. El botón flotante colocado no llevaba a
/// ningún sitio —se pintaba como <c>&lt;button&gt;</c> en vez de enlace— y su nombre accesible
/// era el genérico «Acción», justo en un botón que sólo tiene un icono.</para>
///
/// <para><b><c>actionLink</c> es el destino del enlace</b> y <b><c>target</c></b> dónde abre, si el
/// editor marcó «ventana nueva» (sin él, el elemento abre fuera las URLs externas).
/// <b><c>label</c></b> es el <c>ariaLabel</c> del bloque y, si lo deja vacío, el texto del enlace.
/// <c>iconKey</c> y <c>position</c> son decisiones del editor y viajan como las escribió: el
/// vocabulario de iconos y de esquinas es del elemento, que cae a su valor por defecto con uno
/// que no conoce (el DataType de posición ofrece <c>top-center</c> y <c>bottom-center</c>, que el
/// elemento no pinta).</para>
///
/// <para><c>tooltip</c> lo acepta el elemento y no lo autora el ElementType: queda como
/// atributo.</para>
/// </remarks>
[ElementoSynHost("fab", TipoDeColocable.Pieza)]
public sealed record FabProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? IconKey,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? ActionLink,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Target,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Position,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);
