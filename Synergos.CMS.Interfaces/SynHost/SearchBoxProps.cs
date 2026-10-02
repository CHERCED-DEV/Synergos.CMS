namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-search-box&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1, #196 tanda D).</b> La vista mandaba
/// <c>searchPlaceholder</c>, <c>searchEndpoint</c> y <c>searchParamName</c>; el elemento lee
/// <c>placeholder</c> y no llama a ninguna API. El buscador de cursos, servicios, agenda y del
/// listado de inmuebles salía sin texto, y lo que buscaba no le llegaba a nadie.</para>
///
/// <para><b>Buscar recarga la página con <c>?q</c></b> si el editor lo pide
/// (<see cref="SubmitToPage"/>): el listado de la página lo lee en el servidor (decisión del
/// arquitecto, #196). Sin eso, el elemento solo avisa por sus eventos, como antes.</para>
/// </remarks>
[ElementoSynHost("search-box", TipoDeColocable.Pieza)]
public sealed record SearchBoxProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Placeholder,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? SubmitToPage);
