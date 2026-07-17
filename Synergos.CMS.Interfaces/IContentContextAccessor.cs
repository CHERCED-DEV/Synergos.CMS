namespace Synergos.CMS.Interfaces;

/// <summary>
/// Exposes the identity of content associated with the current request
/// without leaking Umbraco types into the Application layer.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory). The
/// Web-layer implementation wraps <c>IUmbracoContextAccessor</c>;
/// <c>Synergos.CMS.Application</c> consumes only neutral identity
/// (GUID keys), never <c>IPublishedContent</c> or any Umbraco type.
/// This contract is deliberately minimal — grow it by adding one method
/// per real consumer need, never speculatively.
/// </remarks>
public interface IContentContextAccessor
{
    /// <summary>
    /// Returns the key (Umbraco content GUID) of the current request's
    /// content node, or <c>null</c> if no current content exists (e.g.
    /// non-content request like a backoffice API or a static asset).
    /// </summary>
    Guid? GetCurrentContentKey();
}
