namespace Synergos.CMS.Application.Output.Config;

/// <summary>
/// Atributos DOM de nivel página (id y clase CSS del contenedor raíz).
/// </summary>
public sealed record PageDomConfig(string? CssId, string? CssClass);
