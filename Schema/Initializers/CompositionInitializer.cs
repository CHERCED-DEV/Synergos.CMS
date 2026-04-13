using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Creates the Synergos core compositions inside Compositions/Core.
/// </summary>
internal sealed class CompositionInitializer : CompositionInitializerBase
{
    public CompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId = EnsureRootFolder("Compositions");
        var coreFolderId = EnsureChildFolder(rootFolderId, "Core");

        EnsureCoreLifecycle(coreFolderId);
        EnsureCoreBase(coreFolderId);
        EnsureCoreOwnership(coreFolderId);
        EnsureCoreTenant(coreFolderId);
        EnsureCoreAccess(coreFolderId);
        EnsureCoreVersioning(coreFolderId);
        EnsureCoreAudit(coreFolderId);
    }

    private void EnsureCoreLifecycle(int folderId)
    {
        const string typeDescription = "Estados y vigencia del contenido para procesos editoriales y de gobierno.";
        const string statusDescription = "Estado del elemento dentro del flujo editorial. No controla la visibilidad publica; para eso use Visibility.";
        const string validFromDescription = "Fecha desde la cual este contenido debe considerarse vigente.";
        const string validToDescription = "Fecha hasta la cual este contenido debe considerarse vigente.";
        const string isExperimentalDescription = "Marque esta opcion si el contenido o bloque aun esta en prueba.";
        const string isDeprecatedHiddenDescription = "Si esta activo, el contenido se ocultara cuando su estado sea deprecated.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreLifecycle);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "status", statusDescription);
            dirty |= PatchPropertyDescription(existing, "validFrom", validFromDescription);
            dirty |= PatchPropertyDescription(existing, "validTo", validToDescription);
            dirty |= PatchPropertyDescription(existing, "isExperimental", isExperimentalDescription);
            dirty |= PatchPropertyDescription(existing, "isDeprecatedHidden", isDeprecatedHiddenDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreLifecycle", "compCoreLifecycle", ContentTypeKeys.CompCoreLifecycle, folderId, typeDescription, "icon-settings");

        var stateTab = Tab("State", "state", 0);
        stateTab.PropertyTypes!.Add(Prop("status", "Status", DataTypeKeys.SelectLifecycleStatus, 0, mandatory: true, description: statusDescription));
        ct.PropertyGroups.Add(stateTab);

        var validityTab = Tab("Validity", "validity", 10);
        validityTab.PropertyTypes!.Add(Prop("validFrom", "Valid From", DataTypeKeys.DateTimePicker, 0, description: validFromDescription));
        validityTab.PropertyTypes!.Add(Prop("validTo", "Valid To", DataTypeKeys.DateTimePicker, 10, description: validToDescription));
        ct.PropertyGroups.Add(validityTab);

        var flagsTab = Tab("Behavior Flags", "behaviorFlags", 20);
        flagsTab.PropertyTypes!.Add(Prop("isExperimental", "Is Experimental", DataTypeKeys.ToggleBoolean, 0, description: isExperimentalDescription));
        flagsTab.PropertyTypes!.Add(Prop("isDeprecatedHidden", "Hide When Deprecated", DataTypeKeys.ToggleBoolean, 10, description: isDeprecatedHiddenDescription));
        ct.PropertyGroups.Add(flagsTab);

        Cts.Save(ct);
    }

    private void EnsureCoreBase(int folderId)
    {
        const string typeDescription = "Base comun para composiciones de gobierno editorial.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreBase);
        if (existing is not null)
        {
            var dirty = PatchTypeDescription(existing, typeDescription);

            var compositions = new List<IContentTypeComposition>();
            var coreLifecycle = Cts.Get(ContentTypeKeys.CompCoreLifecycle);
            if (coreLifecycle is not null) compositions.Add(coreLifecycle);

            if (!existing.ContentTypeComposition.Select(x => x.Key).SequenceEqual(compositions.Select(x => x.Key)))
            {
                existing.ContentTypeComposition = compositions;
                dirty = true;
            }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreBase", "compCoreBase", ContentTypeKeys.CompCoreBase, folderId, typeDescription, "icon-settings");

        var linkedCompositions = new List<IContentTypeComposition>();
        var linkedCoreLifecycle = Cts.Get(ContentTypeKeys.CompCoreLifecycle);
        if (linkedCoreLifecycle is not null) linkedCompositions.Add(linkedCoreLifecycle);

        ct.ContentTypeComposition = linkedCompositions;
        Cts.Save(ct);
    }

    private void EnsureCoreOwnership(int folderId)
    {
        const string typeDescription = "Responsables internos del contenido o componente.";
        const string ownerTeamDescription = "Equipo responsable de mantener este contenido. Use el identificador acordado internamente.";
        const string ownerRoleDescription = "Rol responsable de aprobar o mantener este contenido.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreOwnership);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "ownerTeam", ownerTeamDescription);
            dirty |= PatchPropertyDescription(existing, "ownerRole", ownerRoleDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreOwnership", "compCoreOwnership", ContentTypeKeys.CompCoreOwnership, folderId, typeDescription, "icon-settings");
        var tab = Tab("Ownership", "ownership", 0);

        tab.PropertyTypes!.Add(Prop("ownerTeam", "Owner Team", DataTypeKeys.TextIdentifier, 0, description: ownerTeamDescription));
        tab.PropertyTypes!.Add(Prop("ownerRole", "Owner Role", DataTypeKeys.SelectOwnerRole, 10, mandatory: true, description: ownerRoleDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureCoreTenant(int folderId)
    {
        const string typeDescription = "Alcance del contenido dentro de la plataforma multi-sitio.";
        const string tenantKeyDescription = "Clave del tenant al que pertenece este contenido. Deje vacio si aplica de forma global.";
        const string siteScopeDescription = "Define si el contenido es global, del tenant actual o compartido.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreTenant);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "tenantKey", tenantKeyDescription);
            dirty |= PatchPropertyDescription(existing, "siteScope", siteScopeDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreTenant", "compCoreTenant", ContentTypeKeys.CompCoreTenant, folderId, typeDescription, "icon-settings");
        var tab = Tab("Tenant", "tenant", 0);

        tab.PropertyTypes!.Add(Prop("tenantKey", "Tenant Key", DataTypeKeys.TextIdentifier, 0, description: tenantKeyDescription));
        tab.PropertyTypes!.Add(Prop("siteScope", "Site Scope", DataTypeKeys.SelectSiteScope, 10, mandatory: true, description: siteScopeDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureCoreAccess(int folderId)
    {
        const string typeDescription = "Restricciones de acceso para experiencias privadas o restringidas.";
        const string accessLevelDescription = "Nivel de acceso requerido para este contenido.";
        const string requiresAuthDescription = "Active esta opcion si el contenido requiere autenticacion para mostrarse o consumirse.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreAccess);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "accessLevel", accessLevelDescription);
            dirty |= PatchPropertyDescription(existing, "requiresAuth", requiresAuthDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreAccess", "compCoreAccess", ContentTypeKeys.CompCoreAccess, folderId, typeDescription, "icon-settings");
        var tab = Tab("Access", "access", 0);

        tab.PropertyTypes!.Add(Prop("accessLevel", "Access Level", DataTypeKeys.SelectAccessLevel, 0, mandatory: true, description: accessLevelDescription));
        tab.PropertyTypes!.Add(Prop("requiresAuth", "Requires Auth", DataTypeKeys.ToggleBoolean, 10, description: requiresAuthDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureCoreVersioning(int folderId)
    {
        const string typeDescription = "Datos de version interna para contenidos o integraciones gobernadas.";
        const string contractVersionDescription = "Version interna o contractual de esta pieza. Usela solo si su equipo lleva control de versiones.";
        const string replacesKeyDescription = "Clave de la version anterior que esta pieza reemplaza.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreVersioning);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "contractVersion", contractVersionDescription);
            dirty |= PatchPropertyDescription(existing, "replacesKey", replacesKeyDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreVersioning", "compCoreVersioning", ContentTypeKeys.CompCoreVersioning, folderId, typeDescription, "icon-settings");
        var tab = Tab("Versioning", "versioning", 0);

        tab.PropertyTypes!.Add(Prop("contractVersion", "Contract Version", DataTypeKeys.TextIdentifier, 0, description: contractVersionDescription));
        tab.PropertyTypes!.Add(Prop("replacesKey", "Replaces Key", DataTypeKeys.TextIdentifier, 10, description: replacesKeyDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    private void EnsureCoreAudit(int folderId)
    {
        const string typeDescription = "Notas y trazabilidad editorial adicional.";
        const string createdByDescription = "Persona o sistema que creo originalmente esta entidad.";
        const string updatedByDescription = "Persona o sistema que realizo la ultima actualizacion importante.";
        const string notesDescription = "Notas internas para contexto editorial, decisiones o seguimiento.";

        var existing = Cts.Get(ContentTypeKeys.CompCoreAudit);
        if (existing is not null)
        {
            var dirty = false;
            dirty |= PatchTypeDescription(existing, typeDescription);
            dirty |= PatchPropertyDescription(existing, "createdBy", createdByDescription);
            dirty |= PatchPropertyDescription(existing, "updatedBy", updatedByDescription);
            dirty |= PatchPropertyDescription(existing, "notes", notesDescription);
            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = CreateComposition("Comp.CoreAudit", "compCoreAudit", ContentTypeKeys.CompCoreAudit, folderId, typeDescription, "icon-settings");
        var tab = Tab("Audit", "audit", 0);

        tab.PropertyTypes!.Add(Prop("createdBy", "Created By", DataTypeKeys.TextAuthor, 0, description: createdByDescription));
        tab.PropertyTypes!.Add(Prop("updatedBy", "Updated By", DataTypeKeys.TextAuthor, 10, description: updatedByDescription));
        tab.PropertyTypes!.Add(Prop("notes", "Notes", DataTypeKeys.TextMetaDesc, 20, description: notesDescription));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
