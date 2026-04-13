using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Creates taxonomy document types for controlled page tagging.
/// </summary>
internal sealed class TaxonomyInitializer : SchemaInitializerBase
{
    public TaxonomyInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService    dataTypeService,
        IShortStringHelper  shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper)
    { }

    public override void Initialize()
    {
        var taxonomyFolderId = EnsureRootFolder("Taxonomies");

        EnsurePageTagsFolder(taxonomyFolderId);
        EnsurePageTag(taxonomyFolderId);
        PatchAllowedChildren();
    }

    private void EnsurePageTagsFolder(int folderId)
    {
        const string description = "Contenedor de etiquetas reutilizables para clasificar paginas y publicaciones.";
        var existing = Cts.Get(ContentTypeKeys.PageTagsFolder);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Page Tags Folder", StringComparison.Ordinal))
            { existing.Name = "Page Tags Folder"; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            dirty |= PatchTypeDescription(existing, description);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key         = ContentTypeKeys.PageTagsFolder,
            Name        = "Page Tags Folder",
            Alias       = ContentTypeKeys.Aliases.PageTagsFolder,
            Description = description,
            Icon        = "icon-folder"
        };

        Cts.Save(ct);
    }

    private void EnsurePageTag(int folderId)
    {
        const string description = "Etiqueta reutilizable para clasificar paginas y publicaciones con taxonomia controlada.";
        var existing = Cts.Get(ContentTypeKeys.PageTag);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Page Tag", StringComparison.Ordinal))
            { existing.Name = "Page Tag"; dirty = true; }
            if (existing.ParentId != folderId)
            { existing.ParentId = folderId; dirty = true; }
            dirty |= PatchTypeDescription(existing, description);
            dirty |= PatchPropertyDescription(existing, "tagName", "Nombre visible de la etiqueta. Uselo como termino editorial corto y consistente.");
            dirty |= PatchPropertyDescription(existing, "tagSlug", "Slug opcional para filtros, URLs o integraciones. Si se deja vacio puede derivarse del nombre.");
            dirty |= PatchPropertyDescription(existing, "tagDescription", "Descripcion breve para documentar cuando usar esta etiqueta.");
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key         = ContentTypeKeys.PageTag,
            Name        = "Page Tag",
            Alias       = ContentTypeKeys.Aliases.PageTag,
            Description = description,
            Icon        = "icon-tag"
        };

        var tab = Tab("Tag", "tag", 0);
        tab.PropertyTypes!.Add(Prop("tagName", "Tag Name", DataTypeKeys.TextTitle, 0, mandatory: true,
            description: "Nombre visible de la etiqueta. Uselo como termino editorial corto y consistente."));
        tab.PropertyTypes!.Add(Prop("tagSlug", "Slug", DataTypeKeys.TextIdentifier, 10,
            description: "Slug opcional para filtros, URLs o integraciones. Si se deja vacio puede derivarse del nombre."));
        tab.PropertyTypes!.Add(Prop("tagDescription", "Description", DataTypeKeys.TextSummary, 20,
            description: "Descripcion breve para documentar cuando usar esta etiqueta."));
        ct.PropertyGroups.Add(tab);

        Cts.Save(ct);
    }

    private void PatchAllowedChildren()
    {
        SetAllowedChildren(ContentTypeKeys.SharedContentFolder, ContentTypeKeys.PageTagsFolder);
        SetAllowedChildren(ContentTypeKeys.PageTagsFolder, ContentTypeKeys.PageTag);
    }

    private void SetAllowedChildren(Guid parentKey, params Guid[] childKeys)
    {
        var parent = Cts.Get(parentKey);
        if (parent is null) return;

        var allowed = childKeys
            .Select(k => Cts.Get(k))
            .Where(ct => ct is not null)
            .Select((ct, i) => new ContentTypeSort(ct!.Id, i))
            .ToArray();

        var existing   = parent.AllowedContentTypes?.ToList() ?? [];
        var existingIds = existing.Select(a => a.Id.Value).ToHashSet();

        foreach (var item in allowed)
        {
            if (!existingIds.Contains(item.Id.Value))
                existing.Add(item);
        }

        parent.AllowedContentTypes = existing;
        Cts.Save(parent);
    }
}
