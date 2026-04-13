using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 7 — Crea los Media Types de Synergos.
///
/// Media Types (e1*):
///   MediaImageAsset    — imagen con alt, copyright, notas
///   MediaVideoAsset    — video con transcripción
///   MediaIconAsset     — icono/svg con clave de variante
///   MediaDocumentAsset — documento descargable
///   MediaSocialImage   — imagen de redes 1200×630
/// </summary>
internal sealed class MediaTypeInitializer : ISchemaInitializer
{
    private readonly IMediaTypeService  _mediaTypeService;
    private readonly IDataTypeService   _dataTypeService;
    private readonly IShortStringHelper _shortStringHelper;

    public MediaTypeInitializer(
        IMediaTypeService  mediaTypeService,
        IDataTypeService   dataTypeService,
        IShortStringHelper shortStringHelper)
    {
        _mediaTypeService  = mediaTypeService;
        _dataTypeService   = dataTypeService;
        _shortStringHelper = shortStringHelper;
    }

    public void Initialize()
    {
        EnsureMediaImageAsset();
        EnsureMediaVideoAsset();
        EnsureMediaIconAsset();
        EnsureMediaDocumentAsset();
        EnsureMediaSocialImage();
    }

    private void EnsureMediaImageAsset()
    {
        if (Exists(ContentTypeKeys.MediaImageAsset)) return;

        var mt = MediaType(ContentTypeKeys.MediaImageAsset, "Media.ImageAsset", "mediaImageAsset", "icon-picture");

        var tab = Tab("Asset", "asset", 0);
        tab.PropertyTypes!.Add(Prop("altText",       "Alt Text",       DataTypeKeys.TextTitle,    0,  mandatory: true,  description: "Texto alternativo para accesibilidad."));
        tab.PropertyTypes!.Add(Prop("copyright",     "Copyright",      DataTypeKeys.TextSubtitle, 10, description: "Crédito o licencia del asset."));
        tab.PropertyTypes!.Add(Prop("usageNotes",    "Usage Notes",    DataTypeKeys.TextMetaDesc, 20, description: "Instrucciones editoriales de uso."));
        tab.PropertyTypes!.Add(Prop("assetCategory", "Asset Category", DataTypeKeys.TextIdentifier, 30, description: "Categoría del asset. Ej: hero, thumbnail, icon."));

        mt.PropertyGroups.Add(tab);
        _mediaTypeService.Save(mt);
    }

    private void EnsureMediaVideoAsset()
    {
        if (Exists(ContentTypeKeys.MediaVideoAsset)) return;

        var mt = MediaType(ContentTypeKeys.MediaVideoAsset, "Media.VideoAsset", "mediaVideoAsset", "icon-video");

        var tab = Tab("Asset", "asset", 0);
        tab.PropertyTypes!.Add(Prop("title",         "Title",          DataTypeKeys.TextTitle,    0));
        tab.PropertyTypes!.Add(Prop("transcript",    "Transcript",     DataTypeKeys.TextMetaDesc, 10, description: "Transcripción de accesibilidad del video."));
        tab.PropertyTypes!.Add(Prop("assetCategory", "Asset Category", DataTypeKeys.TextIdentifier, 20));

        mt.PropertyGroups.Add(tab);
        _mediaTypeService.Save(mt);
    }

    private void EnsureMediaIconAsset()
    {
        if (Exists(ContentTypeKeys.MediaIconAsset)) return;

        var mt = MediaType(ContentTypeKeys.MediaIconAsset, "Media.IconAsset", "mediaIconAsset", "icon-favorite");

        var tab = Tab("Asset", "asset", 0);
        tab.PropertyTypes!.Add(Prop("iconKey",       "Icon Key",       DataTypeKeys.TextIdentifier, 0,  mandatory: true,  description: "Clave del icono. Ej: arrow-right, checkmark."));
        tab.PropertyTypes!.Add(Prop("variantKey",    "Variant Key",    DataTypeKeys.TextIdentifier, 10, description: "Variante del icono. Ej: outline, filled, solid."));
        tab.PropertyTypes!.Add(Prop("assetCategory", "Asset Category", DataTypeKeys.TextIdentifier, 20));

        mt.PropertyGroups.Add(tab);
        _mediaTypeService.Save(mt);
    }

    private void EnsureMediaDocumentAsset()
    {
        if (Exists(ContentTypeKeys.MediaDocumentAsset)) return;

        var mt = MediaType(ContentTypeKeys.MediaDocumentAsset, "Media.DocumentAsset", "mediaDocumentAsset", "icon-document");

        var tab = Tab("Asset", "asset", 0);
        tab.PropertyTypes!.Add(Prop("displayName",   "Display Name",   DataTypeKeys.TextTitle,      0, mandatory: true));
        tab.PropertyTypes!.Add(Prop("assetCategory", "Asset Category", DataTypeKeys.TextIdentifier, 10));

        mt.PropertyGroups.Add(tab);
        _mediaTypeService.Save(mt);
    }

    private void EnsureMediaSocialImage()
    {
        if (Exists(ContentTypeKeys.MediaSocialImage)) return;

        var mt = MediaType(ContentTypeKeys.MediaSocialImage, "Media.SocialImage", "mediaSocialImage", "icon-share");

        var tab = Tab("Asset", "asset", 0);
        tab.PropertyTypes!.Add(Prop("caption",       "Caption",        DataTypeKeys.TextSubtitle,   0, description: "Descripción de la imagen de redes. 1200×630px recomendado."));
        tab.PropertyTypes!.Add(Prop("assetCategory", "Asset Category", DataTypeKeys.TextIdentifier, 10));

        mt.PropertyGroups.Add(tab);
        _mediaTypeService.Save(mt);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private MediaType MediaType(Guid key, string name, string alias, string icon) =>
        new MediaType(_shortStringHelper, -1)
        {
            Key           = key,
            Name          = name,
            Alias         = alias,
            Icon          = icon,
            AllowedAsRoot = true
        };

    private static PropertyGroup Tab(string name, string alias, int sortOrder) =>
        new PropertyGroup(new PropertyTypeCollection(true))
        {
            Name      = name,
            Alias     = alias,
            Type      = PropertyGroupType.Tab,
            SortOrder = sortOrder
        };

    private PropertyType Prop(
        string alias, string name, Guid dataTypeKey, int sortOrder,
        bool mandatory = false, string description = "")
    {
        var dt = _dataTypeService.GetDataType(dataTypeKey)
            ?? throw new InvalidOperationException($"DataType {dataTypeKey} not found.");
        return new PropertyType(_shortStringHelper, dt)
        {
            Alias       = alias,
            Name        = name,
            Description = description,
            Mandatory   = mandatory,
            SortOrder   = sortOrder
        };
    }

    private bool Exists(Guid key) => _mediaTypeService.Get(key) is not null;
}
