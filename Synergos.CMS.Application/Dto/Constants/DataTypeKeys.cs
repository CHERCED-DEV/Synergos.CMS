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

    /// <summary>
    /// <c>DT.BlockGrid.Editorial</c> — Block Grid Data Type exposing
    /// all 21 SSR Element Types (elementText* × 6 + elementAction* × 2
    /// + elementMedia* × 6 + elementInfo* × 7) organised in 4 groups:
    /// Text, Action, Media, Info.
    /// </summary>
    /// <remarks>
    /// Source: <c>uSync/v9/DataTypes/DTBlockGridEditorial.config</c>.
    /// Used as the <c>bodyBlocks</c> field on <c>pageBase</c> (Ola 16).
    /// Companion of:
    /// <list type="bullet">
    ///   <item><c>DT.BlockGrid.Basic</c> (Ola 13, text+action subset)</item>
    ///   <item><c>DT.BlockGrid.SynPilot</c> (Ola 8.5, CDN-hosted pilots)</item>
    /// </list>
    /// </remarks>
    public static readonly Guid BlockGridEditorial =
        Guid.Parse("f975c194-5696-4d7c-9829-ff018fb6995a");

    /// <summary>
    /// <c>DT.BlockList.CtaItems</c> — BlockList Data Type allowing
    /// 1-4 elementActionButton or elementActionLink items. First
    /// container mechanism in the product; used by
    /// <c>elementActionCtaGroup.ctaItems</c> property.
    /// </summary>
    /// <remarks>
    /// Source: <c>uSync/v9/DataTypes/DTBlockListCtaItems.config</c>.
    /// Ola 17. Sets the pattern for future BlockList DataTypes
    /// (DT.BlockList.FaqItems, DT.BlockList.LogoItems, etc.).
    /// </remarks>
    public static readonly Guid BlockListCtaItems =
        Guid.Parse("972ccc3d-ec95-4dd1-88f1-c9d8b8c1fc66");

    // ── Ola 19 BlockList DataTypes (5 container backings) ──────────

    public static readonly Guid BlockListFaqItems =
        Guid.Parse("ba6b1246-e950-4928-97fc-005940585960");

    public static readonly Guid BlockListLogoItems =
        Guid.Parse("825c0f90-c2f3-4004-9f2e-000bdf6dd4be");

    public static readonly Guid BlockListTestimonialItems =
        Guid.Parse("cc4c7d79-8c79-4dfa-a7cc-7793d570b166");

    public static readonly Guid BlockListGalleryItems =
        Guid.Parse("d1decddc-cda8-4d1a-bdbb-0cd9b80edeb6");

    public static readonly Guid BlockListTimelineItems =
        Guid.Parse("78fec9c6-ff38-48c6-9a42-2078415d674c");

    // ── Ola 21 BlockList DataType (Corp container backing) ─────────

    /// <summary>
    /// <c>DT.BlockList.BannerSlides</c> — BlockList Data Type
    /// allowing 1+ elementMediaImage items as slides for
    /// <c>elementCorpBannerSlider.slides</c>.
    /// </summary>
    public static readonly Guid BlockListBannerSlides =
        Guid.Parse("0b74e032-48cc-4225-af6f-38358f204b98");
}
