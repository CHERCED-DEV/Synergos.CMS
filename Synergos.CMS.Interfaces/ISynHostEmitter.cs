using System.Globalization;

namespace Synergos.CMS.Interfaces;

/// <summary>
/// Emits the server-side HTML that mounts a SynHost block on a page:
/// the module script tags + the custom element tag with its JSON
/// config. Framework-agnostic — it does not know nor care if the bundle
/// is Angular, React, Vanilla, etc.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 and part of the contract in ADR 0015
/// draft (SynHost framework-agnostic integration). Consumers are Razor
/// partials under <c>Views/Partials/SynHost/</c>. The default
/// implementation in <c>Synergos.CMS.Application.Services.Impl</c>
/// delegates bundle URL resolution to <see cref="IBundleRegistryClient"/>
/// — it never cables CDN paths directly (ADR 0012). The custom element
/// tag follows the convention <c>&lt;synergos-{block-kebab-case}&gt;</c>.
/// </remarks>
public interface ISynHostEmitter
{
    /// <summary>
    /// Produces the HTML fragments needed to mount a SynHost block.
    /// </summary>
    Task<SynHostEmitResult> EmitAsync(
        SynHostEmitRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Input for an emit call.
/// </summary>
/// <param name="BlockAlias">Alias of the block (e.g. <c>"avatar"</c>,
/// <c>"countdownClock"</c>). Used as the key to query the bundle
/// registry AND — kebab-cased — as the custom element tag suffix.
/// Required.</param>
/// <param name="Props">Typed properties read from the Umbraco element,
/// serialised into the config JSON sent to the bundle. Keys should be
/// camelCase; values must be JSON-serialisable. <c>null</c> is treated
/// as an empty dictionary.</param>
/// <param name="ConfigOverrideJson">Optional JSON string from the
/// editor (<c>compIntegration.configOverride</c>). When valid JSON,
/// its top-level keys are merged onto <paramref name="Props"/> and
/// take precedence. When not valid JSON, the override is skipped and
/// a warning is logged.</param>
/// <param name="Culture">Culture used to stamp the config JSON so
/// the bundle can pick its translations / locale-specific assets.</param>
public sealed record SynHostEmitRequest(
    string BlockAlias,
    IReadOnlyDictionary<string, object?>? Props,
    string? ConfigOverrideJson,
    CultureInfo Culture);

/// <summary>
/// Output of an emit call. Razor partials render both fragments with
/// <c>@Html.Raw</c> — content is encoded/escaped by the emitter.
/// </summary>
/// <param name="ScriptHtml">The <c>&lt;script type="module"&gt;</c>
/// tags (dependencies first, main entry last). When the registry did
/// not resolve the alias, this is an HTML comment explaining the
/// fallback.</param>
/// <param name="ElementHtml">The custom element tag with the config
/// JSON attribute, e.g.
/// <c>&lt;synergos-avatar config='...'&gt;&lt;/synergos-avatar&gt;</c>.
/// Always emitted even when the registry did not resolve — the tag
/// will simply not hydrate until a bundle registers it, which is the
/// expected graceful degradation.</param>
/// <param name="RegistryResolved"><c>true</c> when the registry
/// returned a descriptor; <c>false</c> when the stub/registry returned
/// null. Used by tests and potential diagnostics surfaces.</param>
public sealed record SynHostEmitResult(
    string ScriptHtml,
    string ElementHtml,
    bool RegistryResolved);
