namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Contenido textual estructurado: título, subtítulo, cuerpo enriquecido, resumen y caption.
/// Body se almacena como HTML pre-renderizado listo para @Html.Raw().
/// </summary>
public sealed record ContentTextModel(
    string? Title,
    string? Subtitle,
    string? Body,
    string? Summary,
    string? Caption);
