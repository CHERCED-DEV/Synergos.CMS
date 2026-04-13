namespace Synergos.CMS.Application.Rendering;

/// <summary>
/// Resolves the browser URL for an element bundle.
///
/// Implementations may consult dev-server overrides, static configuration, and
/// a canonical CDN URL — callers depend only on this abstraction.
/// </summary>
public interface IElementUrlResolver
{
    /// <summary>
    /// Returns the full bundle URL (…/main.js) for the given element name,
    /// applying whatever override strategy the implementation defines.
    /// </summary>
    string ResolveBundle(string elementName);
}
