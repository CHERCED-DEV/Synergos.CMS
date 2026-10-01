namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-carousel&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>slidesJson</c> —el TEXTO del
/// TextArea, con <c>imageUrl</c>/<c>alt</c>/<c>caption</c> por diapositiva— y
/// <c>autoplayInterval</c>; el elemento lee <c>slides</c> (una LISTA de <c>src</c>/<c>alt</c>/
/// <c>label</c>), <c>autoplay</c> e <c>interval</c>. Sin diapositivas el elemento se oculta
/// (<c>display: none</c>): el carrusel colocado no se veía.</para>
///
/// <para><b>El intervalo del editor son DOS decisiones del elemento.</b> Un número mayor que cero
/// enciende el autoplay con ese intervalo; cero o vacío lo dejan apagado, que es el default del
/// elemento. La descripción del ElementType dice «Default 5000» y el elemento nunca arrancó solo:
/// no se enciende por defecto (movimiento que el visitante no pidió; WCAG 2.2.2).</para>
///
/// <para><c>linkUrl</c> lo promete la descripción del ElementType y el elemento no lo pinta: no
/// viaja.</para>
///
/// <para><b>Sección <c>Slider</c></b> (ADR 0136): los rótulos de los controles —que la pieza del
/// DS pintaba en inglés, «Previous»/«Next», en un sitio en español— los traduce el elemento con
/// <c>t()</c> y se los pasa a <c>syn-carousel</c> como texto: la hoja no sabe que hay
/// diccionario.</para>
/// </remarks>
[ElementoSynHost("carousel", TipoDeColocable.Pieza, Diccionario = ["Slider"])]
public sealed record CarouselProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<CarouselSlide>? Slides,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? Autoplay,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? Interval);

/// <summary>Una diapositiva: la imagen, su texto alternativo y su rótulo.</summary>
public sealed record CarouselSlide(string Src, string? Alt = null, string? Label = null);
