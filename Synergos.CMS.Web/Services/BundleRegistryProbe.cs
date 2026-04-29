using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Health probe que reporta el estado del bundle registry. Cap-280
/// Batch C (Olas 287-288). Visible vía <c>/_health</c> endpoint
/// agregado por el <c>HealthController</c> existente.
/// </summary>
/// <remarks>
/// Comportamiento por Mode:
/// - <c>Stub</c>: healthy con mensaje "stub mode active". El operador
///   sabe que el CDN no está activo pero NO es un fallo.
/// - <c>FileSystem</c>: intenta resolver un test tag (primero el
///   <c>ProbeTag</c> configurado, sino cualquier element del registry).
///   Si retorna BundleDescriptor → healthy. Si null → unhealthy con
///   mensaje del fallo (registry missing, manifest broken, etc.).
/// - <c>Http</c>: deferred (Batch B no implementa Http aún).
///
/// El probe NO lanza excepciones — convierte todo a IsHealthy=false
/// con Message descriptivo. El consumidor (HealthController) agrega
/// el verdict al overall response.
/// </remarks>
public sealed class BundleRegistryProbe : ISchemaHealthProbe
{
    public const string ProbeName = "bundle_registry";

    private readonly IBundleRegistryClient _client;
    private readonly IOptionsMonitor<BundleRegistrySettings> _settings;

    public BundleRegistryProbe(
        IBundleRegistryClient client,
        IOptionsMonitor<BundleRegistrySettings> settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<SchemaHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var s = _settings.CurrentValue;
        var mode = s.Mode ?? "Stub";

        if (string.Equals(mode, "Stub", StringComparison.OrdinalIgnoreCase))
        {
            return new SchemaHealthResult(
                Name: ProbeName,
                IsHealthy: true,
                Message: "stub mode active (no CDN configured)");
        }

        if (string.Equals(mode, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            // Intenta resolver el test tag canónico. El registry siempre
            // tiene "synergos-column" si está saludable (es el primer
            // primitive del catalog en la mayoría de los CDNs Synergos).
            // Si el operador tiene un setup distinto, puede sobreescribir
            // via setting futuro ProbeTag.
            var probeTag = "synergos-column";
            try
            {
                var descriptor = await _client.TryResolveAsync(probeTag, ct);
                if (descriptor is null)
                {
                    return new SchemaHealthResult(
                        Name: ProbeName,
                        IsHealthy: false,
                        Message: $"FileSystem mode but probe tag '{probeTag}' did not resolve. Verify registry.json + manifest.json + main.js exist at {s.LocalPath}/{s.BundlesNamespace}/.");
                }

                var hasIntegrity = !string.IsNullOrWhiteSpace(descriptor.Integrity);
                return new SchemaHealthResult(
                    Name: ProbeName,
                    IsHealthy: true,
                    Message: $"FileSystem mode OK. Probe tag '{probeTag}' resolved (framework={descriptor.Framework}, version={descriptor.Version}, integrity={(hasIntegrity ? "present" : "missing")}).");
            }
            catch (Exception ex)
            {
                return new SchemaHealthResult(
                    Name: ProbeName,
                    IsHealthy: false,
                    Message: $"FileSystem mode probe failed with exception: {ex.Message}");
            }
        }

        return new SchemaHealthResult(
            Name: ProbeName,
            IsHealthy: false,
            Message: $"Unknown mode '{mode}'. Valid: Stub | FileSystem | Http.");
    }
}
