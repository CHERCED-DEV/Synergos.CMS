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

    /// <summary>
    /// <c>DT.BlockGrid.SynPilot</c> — Block Grid Data Type exposing
    /// the 3 SynHost pilot blocks (elementSynAvatar, elementSynBadge,
    /// elementSynDivider) under the "SynHost Pilot" group.
    /// </summary>
    /// <remarks>
    /// Source: <c>uSync/v9/DataTypes/DTBlockGridSynPilot.config</c>.
    /// Created in Ola 8.5 to smoke-test the SynHost contract end-to-end
    /// in a Block Grid editor UI. Later olas will add larger Block
    /// Grid configurations per page surface.
    /// </remarks>
    public static readonly Guid BlockGridSynPilot =
        Guid.Parse("5772232e-e431-4918-bfda-c56eec963b33");

    /// <summary>
    /// <c>DT.BlockGrid.Basic</c> — Block Grid Data Type exposing the
    /// 8 first-wave SSR Element Types (elementText* × 6 + elementAction*
    /// × 2) under a "Basic Text &amp; Action" group.
    /// </summary>
    /// <remarks>
    /// Source: <c>uSync/v9/DataTypes/DTBlockGridBasic.config</c>.
    /// Ola 13. The companion <c>DT.BlockGrid.SynPilot</c> (Ola 8.5)
    /// exposes the CDN-hosted SynHost blocks; both DataTypes can be
    /// used independently or side-by-side on different pages.
    /// </remarks>
    public static readonly Guid BlockGridBasic =
        Guid.Parse("40e118ec-2e66-4939-9bbd-106b8d50c5a7");
}
