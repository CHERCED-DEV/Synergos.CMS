namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-timeline-horizontal&gt;</c>, la agenda por columnas (horas × carriles).
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista armaba un diccionario a mano y mandaba
/// <c>eventsJson</c> —el TEXTO del TextArea, con <c>date</c> por ítem— y el elemento lee
/// <c>items</c> y exige <c>time</c> y <c>title</c> en cada uno: en <c>/eventos/</c> la agenda colocada
/// pintaba vacía, con el elemento definido y todos los ítems descartados.</para>
///
/// <para>Cada ítem lleva la hora (<c>time</c>, texto libre como «09:00»), el título y, si los tiene,
/// el carril (<c>track</c>; sin él el elemento lo pone en «General») y el texto. El resolver lee
/// <c>time</c> o, para lo ya autorado, <c>date</c>. <c>snapEnabled</c> viaja como lo dejó el
/// editor, igual que antes.</para>
/// </remarks>
[ElementoSynHost("timeline-horizontal", TipoDeColocable.Pieza)]
public sealed record TimelineHorizontalProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<TimelineHorizontalItem>? Items,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool SnapEnabled);

/// <summary>Un ítem de la agenda: su hora, su título y, si los tiene, su carril y su texto.</summary>
public sealed record TimelineHorizontalItem(string Time, string Title, string? Track = null, string? Description = null);
