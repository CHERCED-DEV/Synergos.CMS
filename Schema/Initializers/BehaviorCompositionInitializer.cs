using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 4 — Crea las 6 composiciones Behavior dentro de Compositions/Behavior.
///
/// Propósito de la capa Behavior:
/// Definir comportamiento declarativo que el frontend interpreta en runtime.
/// El CMS declara QUÉ debe ocurrir — el frontend decide CÓMO ejecutarlo.
///
/// Principios:
///   - No contiene contenido ni estilos
///   - No contiene lógica compleja — solo flags y configuración
///   - Compatible con Angular, SSR y Web Components
///   - Cada propiedad es un contrato entre CMS y renderer
/// </summary>
internal sealed class BehaviorCompositionInitializer : CompositionInitializerBase
{
    public BehaviorCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId     = EnsureRootFolder("Compositions");
        var behaviorFolderId = EnsureChildFolder(rootFolderId, "Behavior");

        EnsureBehaviorTracking(behaviorFolderId);
        EnsureBehaviorInteraction(behaviorFolderId);
        EnsureBehaviorNavigation(behaviorFolderId);
        EnsureBehaviorFeatureFlag(behaviorFolderId);
        EnsureBehaviorAsync(behaviorFolderId);
        EnsureBehaviorScript(behaviorFolderId);
    }

    // ── Comp.BehaviorTracking ─────────────────────────────────────────────
    // Tracking básico de eventos de analytics. El renderer emite el evento
    // cuando el componente alcanza el estado relevante (view, click, etc.).
    private void EnsureBehaviorTracking(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorTracking)) return;

        var ct  = CreateComposition("Comp.BehaviorTracking", "compBehaviorTracking", ContentTypeKeys.CompBehaviorTracking, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "eventName",
            name:        "Event Name",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   0,
            description: "Nombre del evento de analytics. Ej: cta_click, hero_view, form_submit. Usar snake_case consistente con la plataforma de analytics."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "eventCategory",
            name:        "Event Category",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Categoría para agrupación en analytics. Ej: engagement, conversion, navigation. Permite filtrar y analizar eventos por dominio funcional."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "eventLabel",
            name:        "Event Label",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   20,
            description: "Etiqueta descriptiva del evento. Proporciona contexto adicional para diferenciar instancias del mismo evento en diferentes contextos."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.BehaviorInteraction ──────────────────────────────────────────
    // Define interacciones declarativas. El CMS configura QUÉ interacción
    // dispara QUÉ acción — el frontend implementa la lógica real.
    private void EnsureBehaviorInteraction(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorInteraction)) return;

        var ct  = CreateComposition("Comp.BehaviorInteraction", "compBehaviorInteraction", ContentTypeKeys.CompBehaviorInteraction, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "interactionType",
            name:        "Interaction Type",
            dataTypeKey: DataTypeKeys.SelectInteractionType,
            sortOrder:   0,
            description: "Tipo de interacción del usuario que dispara la acción configurada. El renderer escucha este evento en el elemento o componente."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "interactionAction",
            name:        "Interaction Action",
            dataTypeKey: DataTypeKeys.SelectInteractionAction,
            sortOrder:   10,
            description: "Acción que se ejecuta cuando ocurre la interacción declarada. El frontend implementa cada acción como un handler desacoplado."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.BehaviorNavigation ───────────────────────────────────────────
    // Control de navegación declarativa. Define destino y tipo de navegación
    // sin asumir implementación — compatible con SPA router o navegación nativa.
    private void EnsureBehaviorNavigation(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorNavigation)) return;

        var ct  = CreateComposition("Comp.BehaviorNavigation", "compBehaviorNavigation", ContentTypeKeys.CompBehaviorNavigation, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "navigateTo",
            name:        "Navigate To",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   0,
            description: "URL o ruta de destino. Puede ser relativa para navegación interna (/about) o absoluta para externa (https://...) o un ID de sección (#hero)."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "navigationType",
            name:        "Navigation Type",
            dataTypeKey: DataTypeKeys.SelectNavigationType,
            sortOrder:   10,
            description: "Tipo de navegación: internal usa el router SPA, external abre en nueva pestaña con rel=noopener, anchor realiza scroll suave al ID especificado."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.BehaviorFeatureFlag ──────────────────────────────────────────
    // Activación condicional de funcionalidad sin redeploy.
    // El CMS actúa como fuente de verdad para estados de feature flags.
    private void EnsureBehaviorFeatureFlag(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorFeatureFlag)) return;

        var ct  = CreateComposition("Comp.BehaviorFeatureFlag", "compBehaviorFeatureFlag", ContentTypeKeys.CompBehaviorFeatureFlag, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "featureKey",
            name:        "Feature Key",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   0,
            mandatory:   true,
            description: "Clave única del feature flag. Debe coincidir exactamente con la clave registrada en el sistema de feature flags del frontend."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "isEnabled",
            name:        "Is Enabled",
            dataTypeKey: DataTypeKeys.ToggleBoolean,
            sortOrder:   10,
            description: "Estado del feature flag. Permite activar o desactivar funcionalidad desde el CMS sin redeploy del frontend."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.BehaviorAsync ────────────────────────────────────────────────
    // Comportamientos asincrónicos declarativos. Define el contrato de
    // comunicación con APIs externas sin implementar la lógica HTTP.
    private void EnsureBehaviorAsync(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorAsync)) return;

        var ct  = CreateComposition("Comp.BehaviorAsync", "compBehaviorAsync", ContentTypeKeys.CompBehaviorAsync, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "apiEndpoint",
            name:        "API Endpoint",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   0,
            description: "URL del endpoint que provee o recibe datos para este componente. El renderer ejecuta la llamada en el momento apropiado del ciclo de vida."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "method",
            name:        "Method",
            dataTypeKey: DataTypeKeys.SelectHttpMethod,
            sortOrder:   10,
            description: "Método HTTP para la operación asincrónica. Determina si el componente lee (GET) o envía datos (POST, PUT, PATCH, DELETE) al endpoint."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.BehaviorScript ───────────────────────────────────────────────
    // Scripts controlados desde CMS. Solo para casos donde el script es
    // parte del contenido editorial — nunca para lógica de aplicación.
    private void EnsureBehaviorScript(int folderId)
    {
        if (Exists(ContentTypeKeys.CompBehaviorScript)) return;

        var ct  = CreateComposition("Comp.BehaviorScript", "compBehaviorScript", ContentTypeKeys.CompBehaviorScript, folderId);
        var tab = Tab("Behavior", "behavior", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "scriptType",
            name:        "Script Type",
            dataTypeKey: DataTypeKeys.SelectScriptType,
            sortOrder:   0,
            description: "Tipo de script: inline embebe el contenido directamente en la página, external referencia una URL de script remoto."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "scriptContent",
            name:        "Script Content",
            dataTypeKey: DataTypeKeys.TextScriptContent,
            sortOrder:   10,
            description: "Contenido del script (si inline) o URL del script externo (si external). Nunca incluir datos sensibles, credenciales ni lógica de negocio."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
