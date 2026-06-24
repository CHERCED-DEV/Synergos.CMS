using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Calcula valores por-defecto editor-safe para cada propiedad de un
/// ElementType, recorriendo su cadena completa de compositions. Resuelve
/// el motivo por el que el BlockGrid JSON construido server-side rompía el
/// editor del backoffice: los editores multi-value (DropDown.Flexible,
/// MediaPicker3, MultiUrlPicker, Tags, CheckBoxList, MultiNodeTreePicker)
/// lanzan <c>JsonReaderException</c> en <c>MultipleValueEditor.ToEditor</c>
/// cuando su valor almacenado falta o es string vacío. Sembrar esas props
/// con un array JSON vacío <c>"[]"</c> mantiene el editor estable.
/// </summary>
/// <remarks>
/// Dev-only, consumido por el path de autoría server-side gated por
/// <c>Synergos:DevSeed:Enabled=true</c> (ADR 0013). No es API pública.
/// La resolución por alias evita hardcodear GUIDs (ADR 0008 /
/// feedback_no_preassigned_guids_usync).
/// </remarks>
public sealed class SchemaBlockDefaults
{
    // Editores cuyo valor almacenado es un array JSON; un valor ausente o
    // string-vacío rompe MultipleValueEditor.ToEditor. Se siembran con "[]".
    private static readonly HashSet<string> JsonArrayEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Umbraco.MediaPicker3",
        "Umbraco.MultiUrlPicker",
        "Umbraco.Tags",
        "Umbraco.DropDown.Flexible",
        "Umbraco.CheckBoxList",
        "Umbraco.MultiNodeTreePicker",
    };

    private readonly IContentTypeService _contentTypeService;

    public SchemaBlockDefaults(IContentTypeService contentTypeService)
        => _contentTypeService = contentTypeService;

    /// <summary>
    /// Resuelve el Key de un ElementType/ContentType por su alias.
    /// Devuelve null si no existe (corre uSync Import primero).
    /// </summary>
    public Guid? KeyFor(string alias) => _contentTypeService.Get(alias)?.Key;

    /// <summary>
    /// Defaults editor-safe para todas las props multi-value del
    /// ElementType (incluyendo las heredadas vía composition).
    /// </summary>
    public IReadOnlyDictionary<string, object> DefaultsFor(Guid elementTypeKey)
    {
        var ct = _contentTypeService.Get(elementTypeKey);
        return ct is null ? Empty : Compute(ct);
    }

    /// <inheritdoc cref="DefaultsFor(System.Guid)"/>
    public IReadOnlyDictionary<string, object> DefaultsFor(string elementTypeAlias)
    {
        var ct = _contentTypeService.Get(elementTypeAlias);
        return ct is null ? Empty : Compute(ct);
    }

    private static readonly IReadOnlyDictionary<string, object> Empty =
        new Dictionary<string, object>(0);

    private static IReadOnlyDictionary<string, object> Compute(IContentType ct)
    {
        var defaults = new Dictionary<string, object>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Unir props PROPIAS (PropertyTypes) + HEREDADAS de compositions
        // (CompositionPropertyTypes). CompositionPropertyTypes por sí solo
        // NO incluye las props propias del tipo (ej. ctaItems en Hero).
        foreach (var pt in ct.PropertyTypes.Concat(ct.CompositionPropertyTypes))
        {
            if (!seen.Add(pt.Alias)) { continue; }
            if (JsonArrayEditors.Contains(pt.PropertyEditorAlias))
            {
                defaults[pt.Alias] = "[]";
            }
            // NOTA: BlockList/BlockGrid anidados NO se siembran aquí —
            // un contenedor vacío no satisface la validación de publish.
            // Bloques con BlockList propio (Hero.ctaItems, FeatureGrid.features)
            // requieren un BlockList NO vacío (con ≥1 item real) para
            // autorarse server-side. Limitación conocida (ver ADR 0093).
        }
        return defaults;
    }
}
