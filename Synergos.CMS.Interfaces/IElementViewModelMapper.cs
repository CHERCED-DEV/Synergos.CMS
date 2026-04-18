namespace Synergos.CMS.Interfaces;

/// <summary>
/// Maps an input source (typically an Umbraco published element) to a
/// ViewModel response for rendering.
/// </summary>
/// <typeparam name="TInput">Source type. Specialised at registration time; the seam itself stays neutral.</typeparam>
/// <typeparam name="TOutput">Destination ViewModel type (usually a response DTO).</typeparam>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory). This
/// contract replaces the 74 element-specific mappers of the legacy
/// project (<c>_archive/fails/Synergos.CMS.epicfail2/Application/Mapping/Elements/</c>).
/// Concrete implementations are created one at a time, in response to
/// real Document Types materialising in uSync (Ola 7+). Brand/vertical
/// specific mappers live in the future custom layer, not in the core.
/// </remarks>
public interface IElementViewModelMapper<in TInput, out TOutput>
{
    /// <summary>Produces the ViewModel for a given source element.</summary>
    TOutput Map(TInput source);
}
