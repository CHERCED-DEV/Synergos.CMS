namespace Synergos.CMS.Application.Dto.Constants;

/// <summary>
/// Stable GUID keys for Umbraco Media Types. Entries are added here only
/// when a matching uSync XML has been imported into
/// <c>Synergos.CMS.Web/uSync/v9/MediaTypes/</c>.
/// </summary>
/// <remarks>
/// Per ADR 0008, uSync XMLs are the source of truth; this class is a
/// typed reference for code that needs to resolve Media Types by GUID.
///
/// Naming convention (inherited from the Epic Fail 2 registry; see
/// <c>history_guid_registry_epicfail2</c> in agent memory):
/// <list type="bullet">
///   <item><c>e1*</c> — Media Types</item>
/// </list>
///
/// Quadruple GUID verification required (<c>MIGRATION_GUARDRAILS §6.2</c>).
/// </remarks>
public static class MediaTypeKeys
{
    // ── Synergos Media Types (Ola 25) ──────────────────────────────
    // Editorial Media Types with richer metadata than Umbraco stock.
    // Existing stock (Image, File, Article, etc.) remain available;
    // editors choose `syn*` when editorial metadata matters.

    /// <summary>
    /// <c>synImage</c> — Editorial image with metadata (altDefault,
    /// creditLabel, creditUrl, copyright, license). Uses ImageCropper
    /// storage. Two tabs: Image (file + auto-extracted Width/Height/
    /// Size/Type labels), Metadata (editorial props).
    /// </summary>
    public static readonly Guid SynImage =
        Guid.Parse("bcc6d08c-509e-4ab6-8d8b-c00c6199253f");

    /// <summary>
    /// <c>synDocument</c> — Editorial document (PDF / DOCX / XLSX /
    /// PPTX). UploadField storage + creditLabel/creditUrl/copyright/
    /// license metadata. No alt (documents are linked, not embedded).
    /// </summary>
    public static readonly Guid SynDocument =
        Guid.Parse("8b7b05af-475f-42be-84aa-5700e3bf96e2");

    /// <summary>
    /// <c>synIcon</c> — Editorial icon (SVG preferred, PNG accepted).
    /// ImageCropper storage + altDefault only (icons don't carry
    /// editorial credit in practice). Square aspect-ratio guidance
    /// in the editorial description.
    /// </summary>
    public static readonly Guid SynIcon =
        Guid.Parse("9538369b-de58-454a-aaa3-888827dee25c");
}
