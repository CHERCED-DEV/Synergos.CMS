namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consumes the bundle registry published by the CDN to resolve the
/// entry point and dependencies for a given element.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory) and
/// ADR 0012 (CDN contract is consumed, not owned). The CMS never
/// hardcodes the shape of CDN paths; it asks this client. Concrete
/// HTTP adapter (<c>HttpBundleRegistryClient</c>) is blocked until
/// the CDN team publishes the registry contract; meanwhile a
/// <c>StubBundleRegistryClient</c> is registered in dev and returns
/// <c>null</c>. See ADR 0012 for activation pre-requisites.
/// </remarks>
public interface IBundleRegistryClient
{
    /// <summary>
    /// Tries to resolve the bundle descriptor for the given element key.
    /// Returns <c>null</c> when the element is unknown to the registry,
    /// the registry is unavailable, or a stub is in use.
    /// </summary>
    Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default);

    /// <summary>
    /// Resuelve <b>cualquier</b> elemento que el registry sepa servir. Devuelve <c>null</c> si el
    /// registry no está disponible, está vacío, o no logra servir ninguno de los que lista.
    /// </summary>
    /// <remarks>
    /// <para><b>Existe para poder preguntar «¿el registry sirve?» sin nombrar un elemento.</b>
    /// Un chequeo de salud atado a un tag concreto no vigila el registry: vigila que <i>ese</i>
    /// elemento siga publicado, que es otra cosa y que además no es decisión del CMS.</para>
    ///
    /// <para>Lo destapó un caso real (defecto #39): el probe sondeaba <c>synergos-column</c> y el
    /// CDN lo retiró a propósito junto con otros ocho. El probe se puso rojo, el registry estaba
    /// perfecto, y el rojo era indistinguible del de un CDN caído.</para>
    ///
    /// <para><b>Qué NO es.</b> No es «listame el registry» — eso invitaría al CMS a razonar sobre
    /// el catálogo del CDN, que es justo lo que ADR 0012 prohíbe. Devuelve <b>uno</b>, y quien
    /// pregunta sólo puede concluir que el registry responde.</para>
    /// </remarks>
    Task<BundleDescriptor?> TryResolveAnyAsync(CancellationToken ct = default);
}

/// <summary>
/// Immutable descriptor of a CDN bundle entry. The record grows only
/// when CDN publishes new mandatory fields — never speculatively.
/// </summary>
/// <param name="MainEntryUri">Absolute URI of the main JS entry point.</param>
/// <param name="Dependencies">Absolute URIs of dependent bundles, in load order.</param>
/// <param name="Version">Opaque version string (format owned by CDN).</param>
/// <param name="Tag">Custom element DOM tag (e.g. <c>synergos-column</c>).
///   Optional — null cuando el registry no expone el dato.</param>
/// <param name="Alias">CMS schema DocType alias (e.g.
///   <c>elementStructColumn</c>). Optional.</param>
/// <param name="Tier">Bundle tier <c>primitive</c> | <c>composition</c> |
///   <c>module</c> | <c>experience</c>. Optional — útil para budgets.</param>
/// <param name="Integrity">SRI hash <c>sha384-{base64}</c> emitido al
///   <c>&lt;script integrity="..."&gt;</c> del HTML. Optional — null
///   cuando el adapter no calcula hashes.</param>
/// <param name="Framework">Framework del bundle (<c>angular</c>,
///   <c>react</c>, <c>svelte</c>, <c>vanilla</c>). Optional.</param>
public sealed record BundleDescriptor(
    Uri MainEntryUri,
    IReadOnlyList<Uri> Dependencies,
    string Version,
    string? Tag = null,
    string? Alias = null,
    string? Tier = null,
    string? Integrity = null,
    string? Framework = null);
