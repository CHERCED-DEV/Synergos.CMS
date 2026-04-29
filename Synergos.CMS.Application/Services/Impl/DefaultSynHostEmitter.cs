using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default implementation of <see cref="ISynHostEmitter"/> per ADR 0015
/// draft. Resolves bundle URLs via <see cref="IBundleRegistryClient"/>
/// (ADR 0012) and formats the server-side HTML fragments that mount a
/// Synergos web component on a page. Framework-agnostic.
/// </summary>
/// <remarks>
/// Deliberate omissions (aligned with <see cref="Proxies.Impl.StubBundleRegistryClient"/>):
/// <list type="bullet">
///   <item><b>No logging</b>. Adding <c>ILogger{T}</c> would pull
///   <c>Microsoft.Extensions.Logging.Abstractions</c> into
///   <c>Synergos.CMS.Application</c>, which ADR 0002 does not
///   authorise. Malformed <c>configOverride</c> JSON is silently
///   skipped and the base props are used as-is. Editors get feedback
///   through the backoffice (preview shows the default config) rather
///   than through logs.</item>
///   <item><b>No caching</b>. Resolving per request is cheap against
///   the in-process bundle registry; caching is a concern of
///   <see cref="IBundleRegistryClient"/> implementations.</item>
/// </list>
/// </remarks>
public sealed class DefaultSynHostEmitter : ISynHostEmitter
{
    private readonly IBundleRegistryClient _registry;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public DefaultSynHostEmitter(IBundleRegistryClient registry)
    {
        _registry = registry;
    }

    public async Task<SynHostEmitResult> EmitAsync(
        SynHostEmitRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BlockAlias))
            throw new ArgumentException("BlockAlias is required.", nameof(request));

        var descriptor = await _registry.TryResolveAsync(request.BlockAlias, ct);

        var mergedConfig = BuildMergedConfig(
            request.Props,
            request.ConfigOverrideJson,
            request.Culture);
        var configJson = JsonSerializer.Serialize(mergedConfig, JsonOpts);

        var customTag = $"synergos-{ToKebabCase(request.BlockAlias)}";

        var scriptHtml = descriptor is null
            ? $"<!-- synHost: bundle registry did not resolve '{EncodeForComment(request.BlockAlias)}'; custom element will not hydrate until a bundle registers -->"
            : BuildScriptTags(descriptor);

        var elementHtml = $"<{customTag} config='{EncodeAttributeSingleQuoted(configJson)}'></{customTag}>";

        return new SynHostEmitResult(scriptHtml, elementHtml, descriptor is not null);
    }

    private static Dictionary<string, object?> BuildMergedConfig(
        IReadOnlyDictionary<string, object?>? props,
        string? configOverrideJson,
        System.Globalization.CultureInfo culture)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["culture"] = culture.Name,
        };

        if (props is not null)
        {
            foreach (var kv in props)
                merged[kv.Key] = kv.Value;
        }

        if (!string.IsNullOrWhiteSpace(configOverrideJson))
        {
            try
            {
                var overrideDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    configOverrideJson,
                    JsonOpts);
                if (overrideDoc is not null)
                {
                    foreach (var kv in overrideDoc)
                        merged[kv.Key] = kv.Value;
                }
            }
            catch (JsonException)
            {
                // Malformed editor JSON — silently fall back to base props.
                // Editor sees default config in preview (visual feedback).
            }
        }

        return merged;
    }

    private static string BuildScriptTags(BundleDescriptor descriptor)
    {
        var sb = new StringBuilder();
        foreach (var dep in descriptor.Dependencies)
        {
            sb.Append("<script src=\"")
              .Append(EncodeAttributeDoubleQuoted(dep.ToString()))
              .Append("\" type=\"module\" defer></script>");
        }
        sb.Append("<script src=\"")
          .Append(EncodeAttributeDoubleQuoted(descriptor.MainEntryUri.ToString()))
          .Append('"');
        // Cap-280: SRI integrity emitido cuando el descriptor lo provee
        // (FileSystemBundleRegistryClient lo computa lazy del main.js).
        // Defensa contra tampering del bundle (CDN compromised, MITM,
        // cache poisoning). Browser rechaza ejecución si hash no matchea.
        if (!string.IsNullOrWhiteSpace(descriptor.Integrity))
        {
            sb.Append(" integrity=\"")
              .Append(EncodeAttributeDoubleQuoted(descriptor.Integrity))
              .Append('"')
              .Append(" crossorigin=\"anonymous\"");
        }
        sb.Append(" type=\"module\" defer></script>");
        return sb.ToString();
    }

    internal static string ToKebabCase(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return string.Empty;
        var sb = new StringBuilder(alias.Length + 4);
        for (int i = 0; i < alias.Length; i++)
        {
            var c = alias[i];
            if (i > 0 && char.IsUpper(c))
                sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static string EncodeAttributeSingleQuoted(string value) =>
        value.Replace("&", "&amp;")
             .Replace("'", "&#39;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;");

    private static string EncodeAttributeDoubleQuoted(string value) =>
        value.Replace("&", "&amp;")
             .Replace("\"", "&quot;")
             .Replace("<", "&lt;")
             .Replace(">", "&gt;");

    private static string EncodeForComment(string value) =>
        value.Replace("-", "_").Replace("<", "(").Replace(">", ")");
}
