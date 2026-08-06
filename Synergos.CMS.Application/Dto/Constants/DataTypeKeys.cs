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
    /// <c>DTBlockGridSections</c> — Block Grid canónico (Layout
    /// Composer, Ola 42.5). Único Block Grid del producto; expone los
    /// 14 Layout Presets como bloques root + cualquier Element Type
    /// dentro de las áreas. Reemplazó a los pilots Basic/Editorial/
    /// SynPilot (eliminados en Ola 50.1.2).
    /// </summary>
    /// <remarks>
    /// Source: <c>uSync/v9/DataTypes/DTBlockGridSections.config</c>.
    /// Consumido por <c>siteRoot.sections</c>, <c>pageBase.sections</c>,
    /// <c>pageBase.sectionsAfterBody</c>, <c>pageBasic.sections</c>,
    /// <c>pageBare.sections</c>, <c>pageLanding.sections</c>,
    /// <c>reusableBlock.sections</c> y <c>elementMemberGate.gatedBlocks</c>.
    /// </remarks>
    public static readonly Guid BlockGridSections =
        Guid.Parse("bdef3027-193b-4334-b3ee-738eded72215");

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

    // ── Ola 22 BlockList DataType (Comp container backing) ─────────

    /// <summary>
    /// <c>DT.BlockList.FeatureItems</c> — BlockList Data Type
    /// allowing 1+ elementInfoFeature items for
    /// <c>elementCompFeatureGrid.features</c>.
    /// </summary>
    public static readonly Guid BlockListFeatureItems =
        Guid.Parse("be345841-2853-49f6-8301-e6fc7a281ab7");

    // ── Ola 23 BlockList DataType (Form container backing) ─────────

    /// <summary>
    /// <c>DT.BlockList.FormFields</c> — BlockList Data Type allowing
    /// 1+ elementFormField items as form rows for
    /// <c>elementFormContainer.fields</c>. Inline editing enabled.
    /// </summary>
    public static readonly Guid BlockListFormFields =
        Guid.Parse("9ca3dab9-57cf-405e-93a4-5aa3f0cd5a51");

    // ── Ola 24 BlockList DataTypes (Nav + Tabs container backings) ─

    /// <summary>
    /// <c>DT.BlockList.NavItems</c> — BlockList Data Type allowing
    /// 1+ elementNavItem rows for <c>elementNavGroup.navItems</c>.
    /// Inline editing enabled.
    /// </summary>
    public static readonly Guid BlockListNavItems =
        Guid.Parse("5e4e9abf-2bbd-4331-a92b-11765a653c9c");

    /// <summary>
    /// <c>DT.BlockList.TabPanels</c> — BlockList Data Type allowing
    /// 1+ elementCorpTabPanel for <c>elementCorpTabGroup.tabs</c>.
    /// </summary>
    public static readonly Guid BlockListTabPanels =
        Guid.Parse("bdd760c5-1a59-4788-95ac-41554384578a");
}
