using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lee la definición de un formulario del published cache de Umbraco (ADR 0002: el adapter
/// con Umbraco vive en Web; el seam, en Interfaces).
/// </summary>
/// <remarks>
/// <para>
/// <c>elementFormContainer</c> es <c>IsElement: true</c>: NO es un nodo del árbol, vive dentro
/// del BlockGrid <c>sections</c> de una página. Por eso no vale <c>DescendantsOfType</c> — hay
/// que recorrer las páginas y mirar dentro de sus bloques.
/// </para>
/// <para>
/// Coste: el published cache es EN MEMORIA y el sitio tiene ~95 páginas, así que el barrido es
/// barato; además el envío de formularios ya está limitado por rate-limit. Se recorre desde la
/// raíz a propósito —no desde el siteRoot del request— porque el POST llega a
/// <c>/api/forms/{key}/submit</c>, que no tiene página en contexto: filtrar por siteRoot ahí
/// dejaría fuera formularios legítimos.
/// </para>
/// </remarks>
public sealed class UmbracoFormDefinitionReader : IFormDefinitionReader
{
    private const string ContainerAlias = "elementFormContainer";
    private const string FieldsAlias = "fields";
    private const string KeyAlias = "formInternalKey";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public UmbracoFormDefinitionReader(IUmbracoContextAccessor umbracoContextAccessor)
        => _umbracoContextAccessor = umbracoContextAccessor;

    public FormDefinition? GetByKey(string formKey)
    {
        if (string.IsNullOrWhiteSpace(formKey)) { return null; }
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext)) { return null; }
        if (umbracoContext.Content is null) { return null; }

        foreach (var page in umbracoContext.Content.GetAtRoot().SelectMany(r => r.DescendantsOrSelf()))
        {
            foreach (var container in EnumerateFormContainers(page))
            {
                var key = container.Value<string>(KeyAlias);
                if (!string.Equals(key, formKey, StringComparison.OrdinalIgnoreCase)) { continue; }

                return new FormDefinition(formKey, ReadFields(container));
            }
        }

        return null;
    }

    /// <summary>
    /// Saca los contenedores de formulario de CUALQUIER propiedad BlockGrid/BlockList de la
    /// página. No se asume la propiedad `sections`: el mismo elemento se puede colocar en otras
    /// (page-basic, reusableblock…), y buscar solo en una dejaría formularios sin validar
    /// exactamente igual que ahora, pero en silencio.
    /// </summary>
    private static IEnumerable<IPublishedElement> EnumerateFormContainers(IPublishedContent page)
    {
        foreach (var prop in page.Properties)
        {
            var value = prop.GetValue();

            if (value is BlockGridModel grid)
            {
                foreach (var el in Flatten(grid).Where(IsContainer)) { yield return el; }
            }
            else if (value is BlockListModel list)
            {
                foreach (var item in list)
                {
                    if (IsContainer(item.Content)) { yield return item.Content; }
                }
            }
        }
    }

    /// <summary>Aplana el grid incluyendo las áreas anidadas: un formulario puede vivir dentro de un layout.</summary>
    private static IEnumerable<IPublishedElement> Flatten(BlockGridModel grid)
    {
        foreach (var item in grid)
        {
            yield return item.Content;
            foreach (var area in item.Areas)
            {
                foreach (var inner in area)
                {
                    yield return inner.Content;
                }
            }
        }
    }

    private static bool IsContainer(IPublishedElement el)
        => string.Equals(el.ContentType.Alias, ContainerAlias, StringComparison.Ordinal);

    private static IReadOnlyList<FormFieldDefinition> ReadFields(IPublishedElement container)
    {
        var fields = container.Value<BlockListModel>(FieldsAlias);
        if (fields is null) { return Array.Empty<FormFieldDefinition>(); }

        var result = new List<FormFieldDefinition>();
        foreach (var block in fields)
        {
            var name = block.Content.Value<string>("fieldName");
            if (string.IsNullOrWhiteSpace(name)) { continue; }

            result.Add(new FormFieldDefinition(
                Name: name,
                Label: block.Content.Value<string>("fieldLabel") ?? name,
                Required: block.Content.Value<bool>("fieldRequired")));
        }

        return result;
    }
}
