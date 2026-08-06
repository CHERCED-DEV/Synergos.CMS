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
    /// Cuando <c>true</c>, quedan disponibles las superficies HTTP que
    /// dependen de data sembrada: el tooling <c>/dev/*</c> (crear/borrar
    /// el siteRoot "Test Site" con content de smoke-test) y las APIs de
    /// demo que sirven un seed en memoria (<c>/api/ehr</c>, vía
    /// <c>DevSeedOnlyAttribute</c>). Con el flag off responden 404.
    /// Default <c>false</c>. Solo habilitar en dev/local.
    /// </summary>
    public bool Enabled { get; init; } = false;
}
