namespace Synergos.CMS.Interfaces;

/// <summary>
/// Construye el shape de <c>window.synergos</c> que el CMS host
/// inyecta en cada page para que los Web Components UI consuman
/// i18n + theme + brand + member + page metadata sin acoplarse a
/// Razor. Ola 216, ADR 0083.
/// </summary>
/// <remarks>
/// El contrato canónico está documentado en
/// <c>Synergos.CMS.Web/docs/contracts/host-bridge.md</c>.
///
/// La impl default vive en
/// <c>Synergos.CMS.Web.Services.DefaultHostBridgeContextBuilder</c>
/// que consume IBrandingProvider + IBrandThemeProvider +
/// IMemberAccessGate + IUmbracoContextAccessor + ILocalizationService
/// internamente.
/// </remarks>
public interface IHostBridgeContextBuilder
{
    /// <summary>
    /// Construye el contexto sincróno desde los seams existentes.
    /// Llamado per-request por el partial _SynergosBridge.cshtml.
    /// </summary>
    HostBridgeContext Build();
}

/// <summary>
/// Snapshot inmutable que se serializa a JSON y se inyecta como
/// <c>window.synergos = {...}</c>. Schema canónico:
/// <c>docs/contracts/host-bridge.md</c>.
/// </summary>
/// <param name="Version">Bridge contract version (semver). UI verifica
///   compat antes de consumir features avanzadas.</param>
/// <param name="I18n">i18n keys + culture metadata.</param>
/// <param name="Theme">Theme variant activo.</param>
/// <param name="Brand">Brand info.</param>
/// <param name="Member">Member context si autenticado, null sino.</param>
/// <param name="Page">Page metadata.</param>
public sealed record HostBridgeContext(
    string Version,
    HostBridgeI18n I18n,
    HostBridgeTheme Theme,
    HostBridgeBrand Brand,
    HostBridgeMember? Member,
    HostBridgePage Page);

public sealed record HostBridgeI18n(
    string Culture,
    string DefaultCulture,
    IReadOnlyDictionary<string, string> Keys);

public sealed record HostBridgeTheme(
    string Variant,
    IReadOnlyList<string> Available);

public sealed record HostBridgeBrand(
    string Key,
    string DisplayName);

public sealed record HostBridgeMember(
    string Key,
    string DisplayName,
    string Email,
    IReadOnlyList<string> Roles);

public sealed record HostBridgePage(
    int Id,
    string DocType,
    string CanonicalUrl,
    IReadOnlyList<string> Cultures);
