namespace Synergos.CMS.Application.Dto.Constants;

/// <summary>
/// Stable GUID keys for Umbraco Data Types. Entries are added here only
/// when a matching uSync XML has been imported into
/// <c>Synergos.CMS.Web/uSync/v9/DataTypes/</c>.
/// </summary>
/// <remarks>
/// Per ADR 0008, uSync XMLs are the source of truth; this class is a
/// typed reference for code that needs to resolve Data Types by GUID.
///
/// Naming convention (inherited from the Epic Fail 2 registry; see
/// <c>history_guid_registry_epicfail2</c> in agent memory):
/// <list type="bullet">
///   <item><c>a1*</c> — system / standard Data Types</item>
///   <item><c>a2*</c> — Block List / Block Grid configurations</item>
/// </list>
///
/// Quadruple GUID verification required (<c>MIGRATION_GUARDRAILS §6.2</c>).
/// </remarks>
public static class DataTypeKeys
{
    // Constants are added as uSync XMLs materialise Data Types.
    // Do not invent keys here — they must exist in uSync first.
}
