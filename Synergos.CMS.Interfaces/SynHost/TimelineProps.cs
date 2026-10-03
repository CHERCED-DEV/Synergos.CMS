namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-timeline&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>eventsJson</c> —el TEXTO del
/// TextArea— y el elemento lee <c>events</c>, una LISTA: la línea de tiempo colocada salía con
/// «No hay hitos para mostrar».</para>
///
/// <para>Cada hito lleva lo que documenta el ElementType con el nombre que el elemento lee:
/// <c>date</c> (texto libre; si es <c>aaaa-mm-dd</c> el elemento la formatea), <c>title</c> y
/// <c>description</c>, que viaja como <c>body</c>. <c>iconKey</c> lo documenta el ElementType y el
/// elemento no lo pinta: no viaja.</para>
///
/// <para><b>El ElementType ya no ofrece <c>orientation</c></b> (#192, caso 1): ofrecía
/// vertical/horizontal y el elemento pinta siempre vertical; lo horizontal es su propio elemento,
/// <c>timeline-horizontal</c>. El título de la sección y el texto de «sin hitos» no los autora el editor:
/// son atributos.</para>
///
/// <para><b>Su microcopia sale del diccionario, sección <c>Timeline</c></b> (ADR 0136): el nombre de la
/// región cuando no tiene título y la línea sin hitos.</para>
/// </remarks>
[ElementoSynHost("timeline", TipoDeColocable.Pieza, Diccionario = ["Timeline"])]
public sealed record TimelineProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<TimelineEntry>? Events);

/// <summary>Un hito: su fecha (texto libre), su título y su texto.</summary>
public sealed record TimelineEntry(string? Date = null, string? Title = null, string? Body = null);
