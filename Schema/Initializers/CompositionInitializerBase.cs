using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Base class for all composition initializers (Phase 1b–6d).
/// Inherits shared helpers from SchemaInitializerBase and adds
/// composition-specific factory methods.
/// </summary>
internal abstract class CompositionInitializerBase : SchemaInitializerBase
{
    protected CompositionInitializerBase(
        IContentTypeService contentTypeService,
        IDataTypeService    dataTypeService,
        IShortStringHelper  shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    // ── ContentType existence ─────────────────────────────────────────────────

    protected bool Exists(Guid key) => Cts.Get(key) is not null;

    // ── Composition factory ───────────────────────────────────────────────────

    protected ContentType CreateComposition(
        string name, string alias, Guid key, int folderId,
        string? description = null,
        string icon = "icon-item-arrangement")
    {
        return new ContentType(Ssh, folderId)
        {
            Key         = key,
            Name        = name,
            Alias       = alias,
            Description = description ?? string.Empty,
            IsElement   = true,
            Icon        = icon
        };
    }
}
