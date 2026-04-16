using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Base class for all Synergos schema pipeline initializers that work with
/// ContentTypes and DataTypes.
///
/// Provides the shared helpers that were previously duplicated across
/// PlatformInitializer, DocumentTypeInitializer, BlogInitializer,
/// ElementTypeInitializer, TaxonomyInitializer, and FlowEngineInitializer.
///
/// MediaTypeInitializer is intentionally excluded — it operates on IMediaTypeService,
/// a separate domain that does not share folder or ContentType management.
/// </summary>
internal abstract class SchemaInitializerBase : ISchemaInitializer
{
    protected readonly IContentTypeService Cts;
    protected readonly IDataTypeService    Dts;
    protected readonly IShortStringHelper  Ssh;

    protected SchemaInitializerBase(
        IContentTypeService contentTypeService,
        IDataTypeService    dataTypeService,
        IShortStringHelper  shortStringHelper)
    {
        Cts = contentTypeService;
        Dts = dataTypeService;
        Ssh = shortStringHelper;
    }

    public abstract void Initialize();

    // ── Folder management ─────────────────────────────────────────────────────

    protected int EnsureRootFolder(string name)
    {
        var existing = Cts.GetContainers(name, 1)
            .FirstOrDefault(c => c.ParentId == -1);
        if (existing is not null) return existing.Id;

        var attempt = Cts.CreateContainer(-1, Guid.NewGuid(), name);
        return attempt.Success ? attempt.Result!.Entity!.Id : -1;
    }

    protected int EnsureChildFolder(int parentId, string name)
    {
        var existing = Cts.GetContainers(name, 2);
        var match    = existing.FirstOrDefault(c => c.ParentId == parentId);
        if (match is not null) return match.Id;

        var attempt = Cts.CreateContainer(parentId, Guid.NewGuid(), name);
        return attempt.Success ? attempt.Result!.Entity!.Id : parentId;
    }

    // ── Property group & type factories ──────────────────────────────────────

    protected static PropertyGroup Tab(string name, string alias, int sortOrder) =>
        new PropertyGroup(new PropertyTypeCollection(true))
        {
            Name      = name,
            Alias     = alias,
            Type      = PropertyGroupType.Tab,
            SortOrder = sortOrder
        };

    protected PropertyType Prop(
        string alias, string name, Guid dataTypeKey, int sortOrder,
        bool mandatory = false, string description = "")
    {
        var dt = Dts.GetDataType(dataTypeKey)
            ?? throw new InvalidOperationException($"DataType {dataTypeKey} not found. Ensure DataTypeInitializer ran first.");
        return new PropertyType(Ssh, dt)
        {
            Alias       = alias,
            Name        = name,
            Description = description,
            Mandatory   = mandatory,
            SortOrder   = sortOrder
        };
    }

    // ── Composition sync ──────────────────────────────────────────────────────

    protected bool SyncCompositions(IContentType ct, params Guid[] compositionKeys)
    {
        var desired = compositionKeys
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var current     = ct.ContentTypeComposition?.ToList() ?? [];
        var currentKeys = current.Select(c => c.Key).ToHashSet();
        var dirty       = false;

        foreach (var composition in desired)
        {
            if (!currentKeys.Add(composition.Key)) continue;
            current.Add(composition);
            dirty = true;
        }

        if (!dirty) return false;
        ct.ContentTypeComposition = current;
        return true;
    }

    // ── Patch helpers ─────────────────────────────────────────────────────────

    protected static bool PatchTypeDescription(IContentType ct, string description)
    {
        if (string.Equals(ct.Description, description, StringComparison.Ordinal)) return false;
        ct.Description = description;
        return true;
    }

    /// <summary>
    /// Updates the ContentType icon when it drifted from the expected value.
    /// Returns true when a change was applied. Umbraco caches the icon on each
    /// content instance at creation; the initializer icon only takes effect for
    /// newly created nodes, so patching the ContentType is enough for tree display.
    /// </summary>
    protected static bool PatchIcon(IContentType ct, string icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return false;
        if (string.Equals(ct.Icon, icon, StringComparison.Ordinal)) return false;
        ct.Icon = icon;
        return true;
    }

    protected static bool PatchPropertyDescription(IContentType ct, string alias, string description)
    {
        var prop = ct.PropertyTypes.FirstOrDefault(p => p.Alias == alias);
        if (prop is null || string.Equals(prop.Description, description, StringComparison.Ordinal)) return false;
        prop.Description = description;
        return true;
    }

    /// <summary>
    /// Ensures the ContentType and the listed property aliases all have
    /// ContentVariation.Culture set. Returns true when anything changed.
    /// </summary>
    protected static bool PatchCultureVariation(IContentType ct, params string[] propertyAliases)
    {
        var dirty = false;
        if (ct.Variations != ContentVariation.Culture)
        {
            ct.Variations = ContentVariation.Culture;
            dirty = true;
        }
        foreach (var alias in propertyAliases)
        {
            var prop = ct.PropertyTypes.FirstOrDefault(p => p.Alias == alias);
            if (prop is not null && prop.Variations != ContentVariation.Culture)
            {
                prop.Variations = ContentVariation.Culture;
                dirty = true;
            }
        }
        return dirty;
    }

    /// <summary>
    /// If the ContentType already exists: patches name, folderId, description and
    /// (optionally) icon if needed, then returns true. Callers should return
    /// immediately on true.
    ///
    /// Returns false when the type does not exist yet (caller should create it).
    ///
    /// <paramref name="icon"/> is optional so legacy callers stay compatible;
    /// passing it ensures the icon stays in sync when an editor or earlier
    /// schema left a different icon on the ContentType.
    /// </summary>
    protected bool TryPatchExistingContentType(Guid key, string name, int folderId, string description, string? icon = null)
    {
        var existing = Cts.Get(key);
        if (existing is null) return false;

        var dirty = false;
        if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
        { existing.Name = name; dirty = true; }
        if (existing.ParentId != folderId)
        { existing.ParentId = folderId; dirty = true; }
        if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
        { existing.Description = description; dirty = true; }
        if (!string.IsNullOrWhiteSpace(icon) && !string.Equals(existing.Icon, icon, StringComparison.Ordinal))
        { existing.Icon = icon; dirty = true; }
        if (dirty) Cts.Save(existing);
        return true;
    }
}
