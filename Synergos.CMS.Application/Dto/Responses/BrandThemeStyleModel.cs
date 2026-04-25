using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Dto.Responses;

/// <summary>
/// Modelo de entrada al partial <c>_BrandThemeStyle.cshtml</c>. Encapsula
/// el <see cref="BrandTheme"/> resuelto por <c>IBrandThemeProvider</c> +
/// la variante de tema page-level resuelta por
/// <c>IPageRenderContextResolver.PageRenderContext.ThemeVariant</c>.
/// </summary>
/// <param name="Theme">Brand theme con colores, fonts y assets — null
///   cuando no hay <c>themeSettings</c> configurado para el brand activo.</param>
/// <param name="PageThemeVariant">Variante de tema decidida por la
///   página actual (light / dark / silverGold / brand). Null o vacío
///   se trata como "light" (no override).</param>
public sealed record BrandThemeStyleModel(BrandTheme? Theme, string? PageThemeVariant);
