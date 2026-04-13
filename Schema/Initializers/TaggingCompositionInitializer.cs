using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Creates the reusable structured tagging composition used by pages and blog posts.
/// </summary>
internal sealed class TaggingCompositionInitializer : CompositionInitializerBase
{
    public TaggingCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId    = EnsureRootFolder("Compositions");
        var contentFolderId = EnsureChildFolder(rootFolderId, "Content");

        EnsureTagging(contentFolderId);
    }

    private void EnsureTagging(int folderId)
    {
        const string description = "Clasificacion editorial reutilizable mediante nodos Page Tag.";
        var existing = Cts.Get(ContentTypeKeys.CompTagging);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, "Comp.Tagging", StringComparison.Ordinal))
            {
                existing.Name = "Comp.Tagging";
                dirty = true;
            }

            if (existing.ParentId != folderId)
            {
                existing.ParentId = folderId;
                dirty = true;
            }

            dirty |= PatchTypeDescription(existing, description);

            var pageTags = existing.PropertyTypes.FirstOrDefault(p => p.Alias == "pageTags");
            if (pageTags is not null &&
                !string.Equals(pageTags.Description, "Etiquetas estructuradas para clasificar la pagina o publicacion. Seleccione nodos 'Page Tag' del repositorio compartido.", StringComparison.Ordinal))
            {
                pageTags.Description = "Etiquetas estructuradas para clasificar la pagina o publicacion. Seleccione nodos 'Page Tag' del repositorio compartido.";
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key         = ContentTypeKeys.CompTagging,
            Name        = "Comp.Tagging",
            Alias       = "compTagging",
            Description = description,
            IsElement   = true,
            Icon        = "icon-tags"
        };

        var tab = Tab("Tagging", "tagging", 0);
        tab.PropertyTypes!.Add(Prop(
            alias:       "pageTags",
            name:        "Page Tags",
            dataTypeKey: DataTypeKeys.PageTagsPicker,
            sortOrder:   0,
            description: "Etiquetas estructuradas para clasificar la pagina o publicacion. Seleccione nodos 'Page Tag' del repositorio compartido."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
