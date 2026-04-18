namespace Synergos.CMS.Application.Dto.Constants;

/// <summary>
/// Stable GUID keys for Umbraco Content Types (Element Types and
/// Document Types). Entries are added here only when a matching uSync
/// XML has been imported into <c>Synergos.CMS.Web/uSync/v9/ContentTypes/</c>.
/// </summary>
/// <remarks>
/// Per ADR 0008, uSync XMLs are the source of truth; this class is a
/// typed reference for code that needs to resolve types by GUID (e.g.
/// resolvers in the Application layer).
///
/// Naming convention (inherited from the Epic Fail 2 registry; see
/// <c>history_guid_registry_epicfail2</c> in agent memory): prefix
/// families group semantically-related types.
/// <list type="bullet">
///   <item><c>b1*-b8*</c> — Compositions</item>
///   <item><c>c3*-ce*</c> — Element Types</item>
///   <item><c>d2*-d6*</c> — Document Types (pages, platform, blog, forms, tags)</item>
/// </list>
///
/// Before declaring a new key here, run the quadruple GUID verification
/// from <c>MIGRATION_GUARDRAILS §6.2</c>: C# constants, uSync XMLs,
/// <c>umbracoNode</c> table, and Block Grid JSON payloads.
/// </remarks>
public static class ContentTypeKeys
{
    // Constants are added as uSync XMLs materialise Content Types.
    // Do not invent keys here — they must exist in uSync first.
}
