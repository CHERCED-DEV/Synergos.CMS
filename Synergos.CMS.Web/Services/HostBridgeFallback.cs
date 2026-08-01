using System.Text.Json;
using Synergos.CMS.Application.Constants;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El payload mínimo de <c>window.synergos</c> que se sirve cuando
/// <see cref="IHostBridgeContextBuilder.Build"/> lanza — la degradación
/// del host bridge (ADR 0083).
/// </summary>
/// <remarks>
/// <para>Existe para que haya UNA sola definición del fallback. Antes vivía
/// escrito a mano como literal JSON en dos sitios —
/// <c>SynergosBridgeController</c> y <c>_SynergosBridge.cshtml</c>— y los dos
/// habían quedado rancios de la misma forma: publicaban
/// <c>["light","dark","silvergold"]</c> cuando las variantes vigentes son
/// ocho y el casing canónico de la tercera es <c>silverGold</c> (ADR 0101 §2).
/// Un literal duplicado no se actualiza: se olvida dos veces.</para>
/// <para>Al derivarlo de <see cref="DropdownOptions.PageThemeVariant.All"/>,
/// agregar un tema lo propaga también al camino degradado.</para>
/// <para>El <c>displayName</c> del brand queda genérico a propósito: si el
/// builder falló, no sabemos qué marca resolvía el host, y adivinarla pintaría
/// una identidad ajena en el sitio de otro.</para>
/// </remarks>
// public, no internal: las vistas Razor se compilan en RUNTIME en un
// assembly aparte (RazorCompileOnBuild=false, ADR 0001 / Umbraco 13), así
// que _SynergosBridge.cshtml no vería un tipo internal de este assembly.
public static class HostBridgeFallback
{
    /// <summary>
    /// Snapshot degradado: sin member, sin page, sin keys de diccionario.
    /// La UI cae a sus fallbacks inline (contrato <c>i18n-bridge.md</c>).
    /// </summary>
    public static readonly HostBridgeContext Context = new(
        Version: "1.0.0",
        I18n: new HostBridgeI18n(
            Culture: "es-CO",
            DefaultCulture: "es-CO",
            Keys: new Dictionary<string, string>()),
        Theme: new HostBridgeTheme(
            Variant: DropdownOptions.PageThemeVariant.Light,
            Available: DropdownOptions.PageThemeVariant.All),
        Brand: new HostBridgeBrand(
            Key: "default",
            DisplayName: "Synergos"),
        Member: null,
        Page: new HostBridgePage(
            Id: 0,
            DocType: string.Empty,
            CanonicalUrl: string.Empty,
            Cultures: Array.Empty<string>()));

    /// <summary>
    /// El mismo snapshot ya serializado en camelCase. Se computa una vez —
    /// es constante — y lo consumen el partial Razor y el endpoint CSP-strict.
    /// </summary>
    public static readonly string Json = JsonSerializer.Serialize(
        Context,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
