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
    // Constants are added as uSync XMLs materialise Media Types.
    // Do not invent keys here — they must exist in uSync first.
}
