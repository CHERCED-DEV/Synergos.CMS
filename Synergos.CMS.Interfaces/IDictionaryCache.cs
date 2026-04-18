using System.Diagnostics.CodeAnalysis;

namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lookup cache for Umbraco Dictionary items (i18n labels).
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory). The
/// default implementation <c>DictionaryCache</c> in
/// <c>Synergos.CMS.Application</c> is a plain thread-safe in-memory
/// store with no population logic; the Web layer (Ola 3) adds a
/// notification handler <c>DictionaryCacheInvalidator</c> that clears
/// affected entries when Umbraco publishes a dictionary change. The
/// contract is intentionally minimal — neither Get nor Invalidate
/// requires knowledge of where data comes from.
/// </remarks>
public interface IDictionaryCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/> (optionally
    /// scoped by <paramref name="culture"/>), or <c>null</c> when not
    /// present. Implementations MUST NOT fall through to any origin at
    /// this layer — lookup is local-cache-only.
    /// </summary>
    [SuppressMessage(
        "Naming", "CA1716:Identifiers should not match keywords",
        Justification = "'Get' is idiomatic for cache lookup (parity with IMemoryCache, IDistributedCache).")]
    string? Get(string key, string? culture = null);

    /// <summary>
    /// Invalidates a single key (all cultures) when <paramref name="key"/>
    /// is supplied; otherwise clears the entire cache.
    /// </summary>
    void Invalidate(string? key = null);
}
