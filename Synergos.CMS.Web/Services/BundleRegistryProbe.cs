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
/// <para>Comportamiento por Mode:</para>
/// <list type="bullet">
///   <item><c>Stub</c>: <b>sano</b>, con «no hay CDN configurado». El operador sabe que el CDN no
///   está activo, y eso no es un fallo — confundir «no lo montamos» con «se cayó» arruina el
///   único indicador que hay.</item>
///   <item><c>FileSystem</c> y <c>Http</c>: se resuelve algo de verdad. Si sale un descriptor →
///   sano. Si no → no sano, diciendo dónde se buscó.</item>
///   <item>Cualquier otro valor → no sano, nombrando el modo y los válidos.</item>
/// </list>
///
/// <para><b>Qué se sondea, y por qué ya no es un tag fijo (defecto #39).</b> Esto sondeaba
/// <c>synergos-column</c> por defecto. El CDN retiró ese elemento a propósito —junto con otros
/// ocho que nadie podía colocar— y el probe se puso rojo con el registry perfecto. Peor: ese rojo
/// era <b>indistinguible</b> del de un CDN caído.</para>
///
/// <para>Ahora se pregunta por <b>cualquier</b> elemento
/// (<see cref="IBundleRegistryClient.TryResolveAnyAsync"/>), que es la pregunta que corresponde:
/// un chequeo atado a un tag concreto no vigila el registry, vigila que <i>ese</i> elemento siga
/// publicado — y qué se publica no lo decide el CMS. <c>ProbeTag</c> sigue existiendo como
/// <b>override explícito</b> para el operador que sí quiera vigilar uno concreto, y su default
/// pasó a vacío.</para>
///
/// <para><b>Y el modo <c>Http</c> faltaba desde que existe.</b> Caía al «modo desconocido» y
/// respondía <i>«Unknown mode 'Http'. Valid: Stub | FileSystem | Http»</i> — contradiciéndose en
/// la misma frase. Con <c>/_health</c> devolviendo 503 ante cualquier probe roja, eso convertía en
/// permanente un rojo que además tenía coartada («es normal sin CDN»), y el día que el CDN se
/// cayera de verdad el síntoma habría sido idéntico.</para>
///
/// <para>El probe NO lanza excepciones — convierte todo a <c>IsHealthy=false</c> con mensaje
/// descriptivo. El consumidor (<c>HealthController</c>) agrega el verdict a la respuesta.</para>
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
                Message: "stub mode active (no CDN configured)",
                Details: new Dictionary<string, object?> { ["mode"] = "Stub" });
        }

        var esFileSystem = string.Equals(mode, "FileSystem", StringComparison.OrdinalIgnoreCase);
        var esHttp = string.Equals(mode, "Http", StringComparison.OrdinalIgnoreCase);

        // Los dos modos comparten rama porque comparten la pregunta: «¿el registry sirve algo?».
        // Lo único que cambia es DÓNDE se buscó, y eso es una línea del mensaje — no otro camino.
        // Escritos por separado, uno de los dos se queda atrás: es exactamente lo que pasó con
        // `Http`, que nunca se escribió y caía al «modo desconocido».
        if (esFileSystem || esHttp)
        {
            // Con `ProbeTag` puesto se sondea ESE, porque el operador lo pidió explícitamente.
            // Vacío —el default desde el defecto #39— se pregunta por cualquiera.
            var probeTag = s.ProbeTag?.Trim();
            var porTagConcreto = !string.IsNullOrWhiteSpace(probeTag);
            var donde = esHttp ? s.PublicBaseUrl : $"{s.LocalPath}/{s.BundlesNamespace}/";

            try
            {
                var descriptor = porTagConcreto
                    ? await _client.TryResolveAsync(probeTag!, ct)
                    : await _client.TryResolveAnyAsync(ct);

                var queSeBusco = porTagConcreto ? $"el tag '{probeTag}'" : "cualquier elemento";

                if (descriptor is null)
                {
                    return new SchemaHealthResult(
                        Name: ProbeName,
                        IsHealthy: false,
                        Message: $"Modo {mode}: no se pudo resolver {queSeBusco} desde {donde}. "
                               + (porTagConcreto
                                    ? "Ojo: hay un ProbeTag configurado, así que esto también sale rojo si ESE elemento dejó de publicarse y el registry está bien."
                                    : "El registry no respondió, vino vacío, o ninguno de los primeros elementos que lista se pudo servir."),
                        Details: new Dictionary<string, object?>
                        {
                            ["mode"] = mode,
                            ["probeTag"] = porTagConcreto ? probeTag : "(cualquiera)",
                            ["origen"] = donde,
                            ["resolved"] = false,
                        });
                }

                // Qué se resolvió, en orden de lo más informativo a lo menos: el tag que el
                // registry reporta, el que el operador pidió, o nada.
                //
                // `descriptor.Tag` es OPCIONAL en el contrato —hay registries que no lo exponen— y
                // usarlo a secas dejaba el mensaje diciendo «un elemento» justo cuando alguien
                // había configurado un ProbeTag y quería verlo nombrado. Lo destapó un test.
                var queResolvio = descriptor.Tag
                    ?? (porTagConcreto ? probeTag : null)
                    ?? "un elemento";

                var hasIntegrity = !string.IsNullOrWhiteSpace(descriptor.Integrity);
                return new SchemaHealthResult(
                    Name: ProbeName,
                    IsHealthy: true,
                    Message: $"Modo {mode} OK. Resolvió {queResolvio} "
                           + $"(framework={descriptor.Framework}, version={descriptor.Version}, "
                           + $"integrity={(hasIntegrity ? "present" : "missing")}).",
                    Details: new Dictionary<string, object?>
                    {
                        ["mode"] = mode,
                        ["probeTag"] = porTagConcreto ? probeTag : "(cualquiera)",
                        ["origen"] = donde,
                        ["resuelto"] = queResolvio,
                        ["framework"] = descriptor.Framework,
                        ["version"] = descriptor.Version,
                        ["integrity"] = hasIntegrity ? "present" : "missing",
                        ["resolved"] = true,
                    });
            }
            catch (Exception ex)
            {
                return new SchemaHealthResult(
                    Name: ProbeName,
                    IsHealthy: false,
                    Message: $"Modo {mode}: el sondeo lanzó {ex.GetType().Name}: {ex.Message}",
                    Details: new Dictionary<string, object?>
                    {
                        ["mode"] = mode,
                        ["origen"] = donde,
                        ["exception"] = ex.GetType().Name,
                    });
            }
        }

        return new SchemaHealthResult(
            Name: ProbeName,
            IsHealthy: false,
            Message: $"Unknown mode '{mode}'. Valid: Stub | FileSystem | Http.",
            Details: new Dictionary<string, object?> { ["mode"] = mode });
    }
}
