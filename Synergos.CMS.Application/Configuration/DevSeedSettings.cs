namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:DevSeed</c>. Controla el dev tooling de content seeding
/// (ADR 0013 — prohibido seeding automático en boot; todo tras flag
/// con invocación explícita).
/// </summary>
public sealed class DevSeedSettings
{
    /// <summary>
    /// Cuando <c>true</c>, los endpoints <c>/dev/seed-test-site</c>
    /// y <c>/dev/clear-test-site</c> quedan disponibles para crear/
    /// borrar el siteRoot "Test Site" con content de smoke-test.
    /// Default <c>false</c>. Solo habilitar en dev/local.
    /// </summary>
    public bool Enabled { get; init; } = false;
}
