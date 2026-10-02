using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

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
/// <para>
/// <b>Un formulario por pasos también es un formulario</b> (#196): <c>elementSynFormStepper</c>
/// declara su clave con el mismo alias y sus campos son <c>elementFormField</c> repartidos en pasos.
/// Su definición es la unión de los campos de todos sus pasos, así que el servidor exige sus
/// obligatorios igual que los de un contenedor. Antes no los veía: el envío del stepper no llegaba
/// nunca, pero en cuanto llegara habría pasado sin chequeo.
/// </para>
/// </remarks>
public sealed class UmbracoFormDefinitionReader : IFormDefinitionReader
{
    private const string ContainerAlias = "elementFormContainer";
    private const string StepperAlias = "elementSynFormStepper";
    private const string StepsAlias = "steps";
    private const string FieldsAlias = "fields";
    private const string KeyAlias = "formInternalKey";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IPublishedValueFallback _fallback;

    /// <param name="umbracoContextAccessor">El published cache de la petición.</param>
    /// <param name="fallback">
    /// El fallback de valores, inyectado: la forma «amigable» <c>Value&lt;T&gt;(alias)</c> lo saca del
    /// proveedor estático y deja la lectura sin poder probarse fuera de Umbraco.
    /// </param>
    public UmbracoFormDefinitionReader(IUmbracoContextAccessor umbracoContextAccessor, IPublishedValueFallback fallback)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _fallback = fallback;
    }

    public FormDefinition? GetByKey(string formKey)
    {
        if (string.IsNullOrWhiteSpace(formKey)) { return null; }
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext)) { return null; }
        if (umbracoContext.Content is null) { return null; }

        foreach (var page in umbracoContext.Content.GetAtRoot().SelectMany(r => r.DescendantsOrSelf()))
        {
            foreach (var container in EnumerateFormContainers(page))
            {
                var key = container.Value<string>(_fallback, KeyAlias);
                if (!string.Equals(key, formKey, StringComparison.OrdinalIgnoreCase)) { continue; }

                return new FormDefinition(formKey, ReadFields(container, _fallback));
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

    /// <summary>
    /// ¿Este bloque declara un formulario? Un contenedor o un formulario por pasos. Público para
    /// probarlo: es lo que decide si el servidor encuentra la definición y exige los obligatorios.
    /// </summary>
    public static bool IsContainer(IPublishedElement el)
        => string.Equals(el.ContentType.Alias, ContainerAlias, StringComparison.Ordinal)
        || IsStepper(el);

    private static bool IsStepper(IPublishedElement el)
        => string.Equals(el.ContentType.Alias, StepperAlias, StringComparison.Ordinal);

    /// <summary>
    /// Los campos de un formulario: los del contenedor, o los de todos los pasos del stepper en
    /// orden. Público para que se pruebe sin el published cache.
    /// </summary>
    public static IReadOnlyList<FormFieldDefinition> ReadFields(IPublishedElement container, IPublishedValueFallback fallback)
    {
        var listas = IsStepper(container)
            ? (container.Value<BlockListModel>(fallback, StepsAlias) ?? Enumerable.Empty<BlockListItem>())
                .Select(paso => paso.Content.Value<BlockListModel>(fallback, FieldsAlias))
            : [container.Value<BlockListModel>(fallback, FieldsAlias)];

        var result = new List<FormFieldDefinition>();
        foreach (var block in listas.OfType<BlockListModel>().SelectMany(l => l))
        {
            var name = block.Content.Value<string>(fallback, "fieldName");
            if (string.IsNullOrWhiteSpace(name)) { continue; }

            result.Add(new FormFieldDefinition(
                Name: name,
                Label: block.Content.Value<string>(fallback, "fieldLabel") ?? name,
                Required: block.Content.Value<bool>(fallback, "fieldRequired")));
        }

        return result;
    }
}
