using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Imagen o recurso multimedia editorial.
/// Reutiliza el value object Image del dominio compartido.
/// AltText sobreescribe el alt de la imagen si se provee.
/// </summary>
public sealed record ContentMediaModel(
    Image? Media,
    string? AltText,
    string? MediaTitle,
    string? MediaDescription);
