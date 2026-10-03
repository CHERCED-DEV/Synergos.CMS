namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-scroll-top&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>ariaLabel</c> y el elemento lee
/// <c>label</c>: el nombre accesible que escribió el editor («Subir al inicio») se tiraba al
/// hidratar y el botón decía siempre «Volver arriba». <c>scrollThreshold</c> y <c>position</c> ya
/// llegaban con su nombre; ahora viajan declarados, y el umbral como NÚMERO.</para>
///
/// <para><c>position</c> es el valor del selector <c>DTSelectScreenPosition</c>, que ofrece seis
/// posiciones; el elemento pinta sólo las tres de abajo y descarta las otras (cae a
/// <c>bottom-right</c>). La lista vive en el UI: copiarla acá sería otra copia sin cruzar.</para>
///
/// <para>El umbral sale de un TextBox y se lee como ENTERO («400»): lo que no lo es («400px»,
/// «1.000») no viaja y se anota, y el elemento aplica el suyo.</para>
///
/// <para><b>Sección <c>ScrollTop</c></b> (ADR 0136): el nombre por defecto. El pie tenía una copia
/// (<c>Footer.BackToTop</c>), sin lector, y se retiró (#193); declararla habría acoplado el botón al pie.</para>
/// </remarks>
[ElementoSynHost("scroll-top", TipoDeColocable.Pieza, Diccionario = ["ScrollTop"])]
public sealed record ScrollTopProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? ScrollThreshold,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Position,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);
