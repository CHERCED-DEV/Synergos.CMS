namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Encabezado de una colección de elementos.
/// El contenido de la colección (items) se resuelve en el nivel de componente
/// via Block List — no vive en esta composición.
/// </summary>
public sealed record ContentCollectionModel(
    string? CollectionTitle,
    string? CollectionDescription);
