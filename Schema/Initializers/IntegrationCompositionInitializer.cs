using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 5b — Crea la composición Integration dentro de Compositions/Integration.
///
/// Propósito:
/// Centralizar la configuración de integraciones externas (analytics, CRM,
/// webhooks, APIs) en una composición reutilizable. El CMS actúa como fuente
/// de verdad para los parámetros de integración — el frontend los consume
/// sin hardcodear valores.
///
/// Principios:
///   - No contiene lógica de integración — solo configuración declarativa
///   - Compatible con Angular HttpClient, fetch y SDKs externos
///   - Los valores sensibles (apiKey) deben manejarse con cuidado en el backoffice
/// </summary>
internal sealed class IntegrationCompositionInitializer : CompositionInitializerBase
{
    public IntegrationCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId        = EnsureRootFolder("Compositions");
        var integrationFolderId = EnsureChildFolder(rootFolderId, "Integration");

        EnsureCompIntegration(integrationFolderId);
        EnsureCompAngularMount(integrationFolderId);
        EnsureCompMfMount(integrationFolderId);
    }

    // ── Comp.Integration ──────────────────────────────────────────────────
    // Configuración declarativa para integraciones externas. El editor
    // selecciona el proveedor y proporciona los parámetros de conexión.
    // El frontend consume estos valores para inicializar el SDK o realizar
    // las llamadas HTTP necesarias.
    private void EnsureCompIntegration(int folderId)
    {
        if (Exists(ContentTypeKeys.CompIntegration)) return;

        var ct  = CreateComposition("Comp.Integration", "compIntegration", ContentTypeKeys.CompIntegration, folderId, icon: "icon-wand");
        var tab = Tab("Integration", "integration", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "integrationProvider",
            name:        "Integration Provider",
            dataTypeKey: DataTypeKeys.SelectIntegrationProvider,
            sortOrder:   0,
            description: "Proveedor de la integración. Determina cómo el frontend inicializa el SDK o cliente HTTP correspondiente."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "integrationId",
            name:        "Integration ID",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Identificador único de la integración en el proveedor. Ej: ID de propiedad de GA, ID de cuenta de HubSpot, ID de lista de Mailchimp."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "apiKey",
            name:        "API Key",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   20,
            description: "Clave de API pública del proveedor. No usar para claves secretas — estas deben ir en variables de entorno del servidor, nunca en el CMS."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "webhookUrl",
            name:        "Webhook URL",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   30,
            description: "URL del webhook al que el frontend enviará notificaciones de eventos. El receptor debe validar la firma o token de autenticación."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "apiEndpoint",
            name:        "API Endpoint",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   40,
            description: "URL base del API del proveedor. Si está vacío, el frontend usa el endpoint por defecto del SDK. Útil para entornos sandbox o proxies."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.AngularMount ─────────────────────────────────────────────────
    // Configuración de montaje para elementos Angular desde el catálogo.
    // El editor selecciona el elemento de un dropdown — sin teclear aliases.
    // Los parámetros adicionales se pasan como pares clave/valor al componente.
    private void EnsureCompAngularMount(int folderId)
    {
        if (Exists(ContentTypeKeys.CompAngularMount)) return;

        var ct  = CreateComposition("Comp.AngularMount", "compAngularMount", ContentTypeKeys.CompAngularMount, folderId, icon: "icon-application-window");
        var tab = Tab("Angular Mount", "angularMount", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "angularElement",
            name:        "Angular Element",
            dataTypeKey: DataTypeKeys.SelectAngularElement,
            sortOrder:   0,
            mandatory:   true,
            description: "Elemento Angular del catálogo que se montará en este punto de la página. El bundle se carga desde CDN automáticamente."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "mountParams",
            name:        "Mount Parameters",
            dataTypeKey: DataTypeKeys.BlockListMountParams,
            sortOrder:   10,
            description: "Parámetros adicionales que se pasan al elemento montado como inputs. Cada fila es un par clave/valor. Útil para configurar variantes, temas o endpoints específicos."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.MfMount ──────────────────────────────────────────────────────
    // Configuración de montaje para microfrontends externos (Module Federation).
    // El editor proporciona la URL del bundle remoto y el módulo que se expone.
    // Los parámetros se pasan como pares clave/valor al MF montado.
    private void EnsureCompMfMount(int folderId)
    {
        if (Exists(ContentTypeKeys.CompMfMount)) return;

        var ct  = CreateComposition("Comp.MfMount", "compMfMount", ContentTypeKeys.CompMfMount, folderId, icon: "icon-merge");
        var tab = Tab("MF Mount", "mfMount", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "remoteEntry",
            name:        "Remote Entry URL",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   0,
            mandatory:   true,
            description: "URL del bundle remoto del microfrontend (remoteEntry.js). Ej: https://mf.ejemplo.com/remoteEntry.js. Debe ser accesible desde el navegador del usuario."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "exposedModule",
            name:        "Exposed Module",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Módulo expuesto por el microfrontend. Ej: ./Feature o ./MiComponente. Debe coincidir con la clave definida en el webpack.config.js del MF remoto."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "mountParams",
            name:        "Mount Parameters",
            dataTypeKey: DataTypeKeys.BlockListMountParams,
            sortOrder:   20,
            description: "Parámetros adicionales que se pasan al microfrontend montado como inputs. Cada fila es un par clave/valor."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
