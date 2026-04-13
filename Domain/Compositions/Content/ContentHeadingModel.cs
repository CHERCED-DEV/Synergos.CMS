namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Jerarquía semántica de títulos de un componente.
/// Define QUÉ nivel es el encabezado — no cómo se ve visualmente.
/// </summary>
public sealed record ContentHeadingModel(
    string? HeadingText,
    string? HeadingLevel,
    string? HeadingTagOverride);
