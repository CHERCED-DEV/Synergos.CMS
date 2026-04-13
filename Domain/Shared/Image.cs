namespace Synergos.CMS.Domain.Shared;

/// <summary>
/// Representa una imagen editorial resuelta: URL pública, texto alternativo y dimensiones intrínsecas opcionales.
/// No contiene referencias a Umbraco ni a ningún framework de presentación.
/// </summary>
public sealed record Image(string Src, string Alt, int? Width = null, int? Height = null);
