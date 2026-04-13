using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 5c — Crea la composición Visibility dentro de Compositions/Visibility.
///
/// Propósito:
/// Controlar la visibilidad editorial del contenido: ocultado manual,
/// publicación programada y condiciones de display. Distinto de
/// CompDomVisibility (que controla responsive/CSS a nivel de DOM).
///
/// Principios:
///   - Visibility editorial ≠ Visibility DOM
///   - isHidden: oculta el item del render sin despublicarlo
///   - visibilityStart/End: scheduling declarativo — el renderer decide cómo aplicarlo
///   - visibilityCondition: clave para lógica condicional en el frontend (feature flags, roles)
/// </summary>
internal sealed class VisibilityCompositionInitializer : CompositionInitializerBase
{
    public VisibilityCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId       = EnsureRootFolder("Compositions");
        var visibilityFolderId = EnsureChildFolder(rootFolderId, "Visibility");

        EnsureCompVisibility(visibilityFolderId);
    }

    // ── Comp.Visibility ───────────────────────────────────────────────────
    // Visibilidad editorial del contenido. Controla si un item se renderiza,
    // cuándo aparece y bajo qué condición. El frontend interpreta estos
    // valores en runtime — no requiere republication del CMS.
    private void EnsureCompVisibility(int folderId)
    {
        if (Exists(ContentTypeKeys.CompVisibility)) return;

        var ct  = CreateComposition("Comp.Visibility", "compVisibility", ContentTypeKeys.CompVisibility, folderId, icon: "icon-eye");
        var tab = Tab("Visibility", "visibility", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "isHidden",
            name:        "Is Hidden",
            dataTypeKey: DataTypeKeys.ToggleBoolean,
            sortOrder:   0,
            description: "Oculta este elemento del render sin despublicarlo. Útil para desactivar temporalmente un bloque sin perder su contenido."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "visibilityStart",
            name:        "Visible From",
            dataTypeKey: DataTypeKeys.DateTimePicker,
            sortOrder:   10,
            description: "Fecha y hora desde la que este elemento debe mostrarse. Si está vacío, se muestra inmediatamente. El renderer evalúa esta condición en cada request."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "visibilityEnd",
            name:        "Visible Until",
            dataTypeKey: DataTypeKeys.DateTimePicker,
            sortOrder:   20,
            description: "Fecha y hora hasta la que este elemento debe mostrarse. Si está vacío, no tiene fecha de expiración. Permite programar campañas con caducidad automática."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "visibilityCondition",
            name:        "Visibility Condition",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   30,
            description: "Clave de condición evaluada por el frontend. Ej: 'user.isLoggedIn', 'feature.newCheckout', 'segment.premium'. El CMS declara la condición — el frontend implementa la lógica."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
