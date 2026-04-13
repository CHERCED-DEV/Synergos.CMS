namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Metadata editorial: etiquetas, categoría y palabras clave para búsqueda y filtrado.
/// Tags es una lista vacía cuando no hay etiquetas — nunca null.
/// </summary>
public sealed record ContentMetadataModel(
    IReadOnlyList<string> Tags,
    string?               Category,
    string?               Keywords);
