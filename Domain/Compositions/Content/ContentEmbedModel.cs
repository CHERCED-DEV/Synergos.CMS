namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Contenido embebido externo: video, mapa o iframe.
/// EmbedType mapea a tokens de render (video, map, iframe).
/// </summary>
public sealed record ContentEmbedModel(
    string? EmbedUrl,
    string? EmbedType,
    string? EmbedTitle);
