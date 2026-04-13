using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Sends a form submission notification to the form's configured recipient.
/// The implementation is framework-specific (see Infrastructure/Email).
/// Silently no-ops when the form has no recipient email configured.
/// </summary>
public interface IFormEmailService
{
    Task SendFormSubmissionAsync(
        FormDefinitionModel form,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default);
}
