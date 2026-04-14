namespace Synergos.CMS.Schema.Constants;

/// <summary>
/// Stable GUIDs for the named areas inside Block Grid layout-preset Element Types.
///
/// Each Layout Preset (1Col / 2ColEqual / 3ColEqual / 4ColEqual / MainSidebar)
/// declares one or more areas where editors drop content blocks. The area key
/// is part of the BlockGrid editor configuration and is referenced by the
/// JSON content layout. Both the Block Grid configuration (created by
/// <see cref="Initializers.DocumentTypeInitializer"/>) and the seeders
/// (<see cref="Seeders.PageBlockGridSeeder"/>, <see cref="Seeders.FlowDemoSiteSeeder"/>)
/// must agree on these GUIDs — that is why they live in one centralized
/// constants file rather than being duplicated.
///
/// GUID range reservation: <c>fa01****</c> — Layout Preset areas.
/// Add new areas using the next free slot; never reuse a retired area key.
/// </summary>
public static class BlockGridAreaKeys
{
    /// <summary>Single-column layout — main content area.</summary>
    public static readonly Guid Preset1ColMain     = new("fa010001-0000-0000-0000-000000000000");

    /// <summary>Two-column equal-width layout — left area.</summary>
    public static readonly Guid Preset2ColLeft     = new("fa010002-0000-0000-0000-000000000000");

    /// <summary>Two-column equal-width layout — right area.</summary>
    public static readonly Guid Preset2ColRight    = new("fa010003-0000-0000-0000-000000000000");

    /// <summary>Three-column equal-width layout — first column.</summary>
    public static readonly Guid Preset3Col1        = new("fa010004-0000-0000-0000-000000000000");

    /// <summary>Three-column equal-width layout — second column.</summary>
    public static readonly Guid Preset3Col2        = new("fa010005-0000-0000-0000-000000000000");

    /// <summary>Three-column equal-width layout — third column.</summary>
    public static readonly Guid Preset3Col3        = new("fa010006-0000-0000-0000-000000000000");

    /// <summary>Four-column equal-width layout — first column.</summary>
    public static readonly Guid Preset4Col1        = new("fa010007-0000-0000-0000-000000000000");

    /// <summary>Four-column equal-width layout — second column.</summary>
    public static readonly Guid Preset4Col2        = new("fa010008-0000-0000-0000-000000000000");

    /// <summary>Four-column equal-width layout — third column.</summary>
    public static readonly Guid Preset4Col3        = new("fa010009-0000-0000-0000-000000000000");

    /// <summary>Four-column equal-width layout — fourth column.</summary>
    public static readonly Guid Preset4Col4        = new("fa010010-0000-0000-0000-000000000000");

    /// <summary>Main + sidebar layout — primary content area.</summary>
    public static readonly Guid PresetMainContent  = new("fa010011-0000-0000-0000-000000000000");

    /// <summary>Main + sidebar layout — sidebar area.</summary>
    public static readonly Guid PresetSidebar      = new("fa010012-0000-0000-0000-000000000000");

    // ── Structural element areas (fa00****) ─────────────────────────────────
    // Each structural element type (Section, Container, Grid, Column, Stack)
    // exposes an area where editors drop child blocks.

    /// <summary>Section element — content area.</summary>
    public static readonly Guid SectionContent     = new("fa000001-0000-0000-0000-000000000000");

    /// <summary>Grid element — columns area (children must be Column elements).</summary>
    public static readonly Guid GridColumns        = new("fa000002-0000-0000-0000-000000000000");

    /// <summary>Column element — content area.</summary>
    public static readonly Guid ColumnContent      = new("fa000003-0000-0000-0000-000000000000");

    /// <summary>Container element — content area.</summary>
    public static readonly Guid ContainerContent   = new("fa000004-0000-0000-0000-000000000000");

    /// <summary>Stack element — content area.</summary>
    public static readonly Guid StackContent       = new("fa000005-0000-0000-0000-000000000000");
}
