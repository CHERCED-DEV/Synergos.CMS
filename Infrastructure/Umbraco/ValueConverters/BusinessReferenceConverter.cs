using System.Text.Json;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Services;
using Synergos.CMS.Domain.Integration;

namespace Synergos.CMS.Infrastructure.Umbraco.ValueConverters;

/// <summary>
/// Converter de infraestructura que deserializa el JSON almacenado en Umbraco
/// hacia el tipo de dominio BusinessReference.
///
/// Discrimina por editor alias (Umbraco.TextBox) + nombre del Data Type
/// (prefijo "DT.Business."). El nombre se obtiene via IDataTypeService
/// porque PublishedDataType no expone Name directamente.
/// </summary>
[DefaultPropertyValueConverter]
public sealed class BusinessReferenceConverter : PropertyValueConverterBase
{
    private const string EditorAlias    = "Umbraco.TextBox";
    private const string DataTypePrefix = "DT.Business.";

    private readonly IDataTypeService _dataTypeService;

    public BusinessReferenceConverter(IDataTypeService dataTypeService)
    {
        _dataTypeService = dataTypeService;
    }

    public override bool IsConverter(IPublishedPropertyType propertyType)
    {
        if (propertyType.EditorAlias != EditorAlias)
            return false;

        // PublishedDataType does not expose Name — look it up via service.
        var dataType = _dataTypeService.GetDataType(propertyType.DataType.Id);
        return dataType?.Name?.StartsWith(DataTypePrefix, StringComparison.OrdinalIgnoreCase) == true;
    }

    public override Type GetPropertyValueType(IPublishedPropertyType propertyType)
        => typeof(BusinessReference);

    public override PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType)
        => PropertyCacheLevel.Element;

    public override object? ConvertSourceToIntermediate(
        IPublishedElement owner,
        IPublishedPropertyType propertyType,
        object? source,
        bool preview)
        => source?.ToString();

    public override object? ConvertIntermediateToObject(
        IPublishedElement owner,
        IPublishedPropertyType propertyType,
        PropertyCacheLevel referenceCacheLevel,
        object? inter,
        bool preview)
    {
        if (inter is not string json || string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<BusinessReference>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}
