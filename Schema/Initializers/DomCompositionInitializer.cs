using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 3 - Creates the DOM compositions used by element types.
/// These compositions expose advanced visual configuration without changing aliases.
/// </summary>
internal sealed class DomCompositionInitializer : CompositionInitializerBase
{
    public DomCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId = EnsureRootFolder("Compositions");
        var domFolderId = EnsureChildFolder(rootFolderId, "Dom");

        EnsureDomClass(domFolderId);
        EnsureDomAttributes(domFolderId);
        EnsureDomLayout(domFolderId);
        EnsureDomSpacing(domFolderId);
        EnsureDomVisibility(domFolderId);
        EnsureDomVariant(domFolderId);
        EnsureDomLayoutPreset(domFolderId);
        EnsureCompDomLayoutProfile(domFolderId);
    }

    private void EnsureCompDomLayoutProfile(int folderId)
    {
        const string typeDescription         = "Control de layout por página. Permite elegir un perfil de layout personalizado (nav, header, footer) o usar el layout por defecto del sitio.";
        const string useDefaultLayoutDesc    = "Cuando está activo, la página usa el header, footer y navegación configurados en Site Settings. Desactívalo para asignar un perfil específico.";
        const string showHeaderDesc          = "Muestra el header global en esta página. Aplica solo cuando useDefaultLayout está activo.";
        const string showFooterDesc          = "Muestra el footer global en esta página. Aplica solo cuando useDefaultLayout está activo.";
        const string layoutProfileDesc       = "Perfil de layout personalizado para esta página. Se usa cuando useDefaultLayout está desactivado. Configura nav, header/footer y estilos propios.";

        var existing = Cts.Get(ContentTypeKeys.CompDomLayoutProfile);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "useDefaultLayout", useDefaultLayoutDesc);
            dirty |= PatchPropertyDescription(existing, "showHeader",       showHeaderDesc);
            dirty |= PatchPropertyDescription(existing, "showFooter",       showFooterDesc);
            dirty |= PatchPropertyDescription(existing, "layoutProfile",    layoutProfileDesc);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct  = CreateComposition("Comp.DomLayoutProfile", "compDomLayoutProfile", ContentTypeKeys.CompDomLayoutProfile, folderId, typeDescription);
        var tab = Tab("Layout", "layout", 0);

        tab.PropertyTypes!.Add(Prop("useDefaultLayout", "Usar layout por defecto", DataTypeKeys.ToggleBoolean, 0,  description: useDefaultLayoutDesc));
        tab.PropertyTypes!.Add(Prop("showHeader",       "Mostrar header",          DataTypeKeys.ToggleBoolean, 10, description: showHeaderDesc));
        tab.PropertyTypes!.Add(Prop("showFooter",       "Mostrar footer",          DataTypeKeys.ToggleBoolean, 20, description: showFooterDesc));
        tab.PropertyTypes!.Add(Prop("layoutProfile",    "Perfil de layout",        DataTypeKeys.ContentPicker, 30, description: layoutProfileDesc));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomLayoutPreset(int folderId)
    {
        const string typeDescription     = "Controles de espaciado granular para presets de layout de pagina.";
        const string noPaddingTopDesc    = "Elimina el espaciado superior del layout. Util cuando el bloque anterior ya provee separacion visual.";
        const string noPaddingBottomDesc = "Elimina el espaciado inferior del layout. Util cuando el bloque siguiente ya provee separacion visual.";
        const string noMarginTopDesc     = "Elimina el margen superior. Usar combinado con un valor de margin para ajustar solo un lado.";
        const string noMarginBottomDesc  = "Elimina el margen inferior. Usar combinado con un valor de margin para ajustar solo un lado.";

        var existing = Cts.Get(ContentTypeKeys.CompDomLayoutPreset);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "noPaddingTop",    noPaddingTopDesc);
            dirty |= PatchPropertyDescription(existing, "noPaddingBottom", noPaddingBottomDesc);
            dirty |= PatchPropertyDescription(existing, "noMarginTop",     noMarginTopDesc);
            dirty |= PatchPropertyDescription(existing, "noMarginBottom",  noMarginBottomDesc);

            if (!existing.PropertyTypes.Any(p => p.Alias == "noMarginTop"))
            {
                var domTab = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "dom");
                domTab?.PropertyTypes?.Add(Prop("noMarginTop", "Sin margen superior", DataTypeKeys.ToggleBoolean, 50, description: noMarginTopDesc));
                dirty = true;
            }
            if (!existing.PropertyTypes.Any(p => p.Alias == "noMarginBottom"))
            {
                var domTab = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "dom");
                domTab?.PropertyTypes?.Add(Prop("noMarginBottom", "Sin margen inferior", DataTypeKeys.ToggleBoolean, 60, description: noMarginBottomDesc));
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct  = CreateComposition("Comp.DomLayoutPreset", "compDomLayoutPreset", ContentTypeKeys.CompDomLayoutPreset, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop("noPaddingTop",    "Sin espaciado superior", DataTypeKeys.ToggleBoolean, 30, description: noPaddingTopDesc));
        tab.PropertyTypes!.Add(Prop("noPaddingBottom", "Sin espaciado inferior", DataTypeKeys.ToggleBoolean, 40, description: noPaddingBottomDesc));
        tab.PropertyTypes!.Add(Prop("noMarginTop",     "Sin margen superior",    DataTypeKeys.ToggleBoolean, 50, description: noMarginTopDesc));
        tab.PropertyTypes!.Add(Prop("noMarginBottom",  "Sin margen inferior",    DataTypeKeys.ToggleBoolean, 60, description: noMarginBottomDesc));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomClass(int folderId)
    {
        const string typeDescription = "Opciones visuales avanzadas basadas en clases CSS acordadas por el equipo de diseno.";
        const string classListDescription = "Clases CSS adicionales separadas por espacio. Uselo solo si el equipo de diseno indico una clase concreta. Ejemplo: is-highlighted full-width";

        var existing = Cts.Get(ContentTypeKeys.CompDomClass);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "classList", classListDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.DomClass", "compDomClass", ContentTypeKeys.CompDomClass, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop(
            alias: "classList",
            name: "Class List",
            dataTypeKey: DataTypeKeys.TextClassList,
            sortOrder: 0,
            description: classListDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomAttributes(int folderId)
    {
        const string typeDescription = "Atributos HTML avanzados para accesibilidad, identificacion o integraciones.";
        const string elementIdDescription = "ID unico del bloque dentro de la pagina. Uselo cuando necesite anclas, etiquetas accesibles o scripts que apunten a este elemento.";
        const string dataAttributesDescription = "Atributos data-* en formato JSON. Uselos solo para integraciones o seguimiento previamente definidos. Ejemplo: {\"data-component\":\"hero\"}";
        const string ariaLabelDescription = "Texto adicional para lectores de pantalla cuando el contenido visible no explica bien la funcion del bloque.";
        const string roleDescription = "Rol ARIA del elemento. Uselo solo cuando accesibilidad o el equipo tecnico lo soliciten.";

        var existing = Cts.Get(ContentTypeKeys.CompDomAttributes);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "elementId", elementIdDescription);
            dirty |= PatchPropertyDescription(existing, "dataAttributes", dataAttributesDescription);
            dirty |= PatchPropertyDescription(existing, "ariaLabel", ariaLabelDescription);
            dirty |= PatchPropertyDescription(existing, "role", roleDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.DomAttributes", "compDomAttributes", ContentTypeKeys.CompDomAttributes, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop(
            alias: "elementId",
            name: "Element ID",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder: 0,
            description: elementIdDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "dataAttributes",
            name: "Data Attributes",
            dataTypeKey: DataTypeKeys.TextDataAttributes,
            sortOrder: 10,
            description: dataAttributesDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "ariaLabel",
            name: "ARIA Label",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder: 20,
            description: ariaLabelDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "role",
            name: "Role",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder: 30,
            description: roleDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomLayout(int folderId)
    {
        const string typeDescription = "Controles de layout para ancho, alineacion y direccion del contenido.";
        const string containerTypeDescription = "Define como se comporta el ancho del bloque dentro de la pagina: fijo, fluido o de ancho completo.";
        const string alignmentDescription = "Alineacion general del contenido dentro del bloque.";
        const string directionDescription = "Orden visual de los elementos hijos: horizontal o vertical.";

        var existing = Cts.Get(ContentTypeKeys.CompDomLayout);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "containerType", containerTypeDescription);
            dirty |= PatchPropertyDescription(existing, "alignment", alignmentDescription);
            dirty |= PatchPropertyDescription(existing, "direction", directionDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.DomLayout", "compDomLayout", ContentTypeKeys.CompDomLayout, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop(
            alias: "containerType",
            name: "Container Type",
            dataTypeKey: DataTypeKeys.SelectContainerType,
            sortOrder: 0,
            description: containerTypeDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "alignment",
            name: "Alignment",
            dataTypeKey: DataTypeKeys.SelectAlignmentLayout,
            sortOrder: 10,
            description: alignmentDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "direction",
            name: "Direction",
            dataTypeKey: DataTypeKeys.SelectDirection,
            sortOrder: 20,
            description: directionDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomSpacing(int folderId)
    {
        const string typeDescription    = "Espaciado visual del bloque usando tokens aprobados por el sistema de diseno.";
        const string marginDescription  = "Espacio exterior alrededor del bloque. Seleccione un valor de la escala definida por diseno.";
        const string paddingDescription = "Espacio interior dentro del bloque. Seleccione un valor de la escala definida por diseno.";
        const string gapDescription     = "Separacion entre elementos hijos del bloque.";

        var existing = Cts.Get(ContentTypeKeys.CompDomSpacing);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchSpacingProperty(existing, "margin",  DataTypeKeys.SelectSpacingMargin);
            dirty |= PatchSpacingProperty(existing, "padding", DataTypeKeys.SelectSpacingPadding);
            dirty |= PatchSpacingProperty(existing, "gap",     DataTypeKeys.SelectSpacingGap);
            dirty |= PatchPropertyDescription(existing, "margin",  marginDescription);
            dirty |= PatchPropertyDescription(existing, "padding", paddingDescription);
            dirty |= PatchPropertyDescription(existing, "gap",     gapDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct  = CreateComposition("Comp.DomSpacing", "compDomSpacing", ContentTypeKeys.CompDomSpacing, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop("margin",  "Margin",  DataTypeKeys.SelectSpacingMargin,  0,  description: marginDescription));
        tab.PropertyTypes!.Add(Prop("padding", "Padding", DataTypeKeys.SelectSpacingPadding, 10, description: paddingDescription));
        tab.PropertyTypes!.Add(Prop("gap",     "Gap",     DataTypeKeys.SelectSpacingGap,     20, description: gapDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private bool PatchSpacingProperty(IContentType ct, string alias, Guid newDataTypeKey)
    {
        var newDt = Dts.GetDataType(newDataTypeKey);
        if (newDt is null) return false;

        var prop = ct.PropertyTypes.FirstOrDefault(p => p.Alias == alias);
        if (prop is null || prop.DataTypeId == newDt.Id) return false;

        prop.DataTypeId = newDt.Id;
        return true;
    }

    private void EnsureDomVisibility(int folderId)
    {
        const string typeDescription = "Opciones de visibilidad responsive para mostrar u ocultar el bloque segun el dispositivo.";
        const string hideOnMobileDescription = "Oculta este bloque en pantallas moviles.";
        const string hideOnDesktopDescription = "Oculta este bloque en pantallas de escritorio.";

        var existing = Cts.Get(ContentTypeKeys.CompDomVisibility);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "hideOnMobile", hideOnMobileDescription);
            dirty |= PatchPropertyDescription(existing, "hideOnDesktop", hideOnDesktopDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.DomVisibility", "compDomVisibility", ContentTypeKeys.CompDomVisibility, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop(
            alias: "hideOnMobile",
            name: "Hide on Mobile",
            dataTypeKey: DataTypeKeys.ToggleBoolean,
            sortOrder: 0,
            description: hideOnMobileDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "hideOnDesktop",
            name: "Hide on Desktop",
            dataTypeKey: DataTypeKeys.ToggleBoolean,
            sortOrder: 10,
            description: hideOnDesktopDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureDomVariant(int folderId)
    {
        const string typeDescription = "Variantes visuales del bloque como tema o estilo.";
        const string variantDescription = "Variante visual del bloque: cambia colores, bordes o tratamiento visual segun el diseno.";
        const string themeDescription = "Esquema de color aplicado localmente a este bloque. Puede anular el tema general de la pagina.";

        var existing = Cts.Get(ContentTypeKeys.CompDomVariant);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "variant", variantDescription);
            dirty |= PatchPropertyDescription(existing, "theme", themeDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.DomVariant", "compDomVariant", ContentTypeKeys.CompDomVariant, folderId, typeDescription);
        var tab = Tab("DOM", "dom", 0);

        tab.PropertyTypes!.Add(Prop(
            alias: "variant",
            name: "Variant",
            dataTypeKey: DataTypeKeys.SelectVariant,
            sortOrder: 0,
            description: variantDescription));
        tab.PropertyTypes!.Add(Prop(
            alias: "theme",
            name: "Theme",
            dataTypeKey: DataTypeKeys.SelectTheme,
            sortOrder: 10,
            description: themeDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

}
