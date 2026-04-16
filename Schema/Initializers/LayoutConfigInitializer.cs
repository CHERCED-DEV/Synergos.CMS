using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 9b (Layout Config) — crea el árbol de tipos de documento para
/// <c>Config/Layout/</c>:
///
///   <c>LayoutFolder</c>
///   ├── <c>HeaderConfigFolder</c>   → <c>HeaderConfig</c>
///   ├── <c>FooterConfigFolder</c>   → <c>FooterConfig</c>
///   ├── <c>AlertBarConfigFolder</c> → <c>AlertBarConfig</c>
///   ├── <c>BannerConfigFolder</c>   → <c>BannerConfig</c>
///   └── <c>LayoutProfileFolder</c>  → <c>LayoutProfile</c>   (reutiliza doctype existente)
///
///   <c>NavigationFolder</c>          → <c>NavigationGroup</c> (reutiliza doctype existente)
///
/// Estas carpetas son intencionalmente sin propiedades — existen sólo para
/// restringir <c>AllowedContentTypes</c> y ofrecer al editor una UX limpia
/// ("Agregar Header", "Agregar Footer") en lugar de un genérico "Agregar Carpeta".
///
/// Ninguno de estos tipos es enrutable: todos se crean sin templates para que
/// Umbraco no intente servir <c>/synergos/config/…</c> como URL pública.
///
/// Las 4 hojas de config (Header/Footer/AlertBar/Banner) contienen los campos
/// que antes vivían como tabs en SiteSettings. SiteSettings quedó sólo con
/// identidad del sitio (Identity, Contact, Social, SEO, Scripts, Forms, Layout).
///
/// Llamado desde <see cref="PlatformInitializer.Initialize"/> después de
/// <see cref="SiteSettingsInitializer"/>. No es una fase independiente del pipeline.
/// </summary>
internal sealed class LayoutConfigInitializer : SchemaInitializerBase
{
    private const string TabNavigation = "navigation";
    private const string TabContent    = "content";
    private const string TabBehavior   = "behavior";
    private const string TabBranding   = "branding";
    private const string TabStyle      = "style";
    private const string TabSchedule   = "schedule";
    private const string TabNewsletter = "newsletter";
    private const string TabCta        = "cta";

    private readonly int _settingsFolderId;

    public LayoutConfigInitializer(
        IContentTypeService cts,
        IDataTypeService    dts,
        IShortStringHelper  ssh,
        int                 settingsFolderId)
        : base(cts, dts, ssh)
    {
        _settingsFolderId = settingsFolderId;
    }

    public override void Initialize()
    {
        // 1. Hojas de config (deben existir antes de que las carpetas las referencien en AllowedChildren).
        EnsureHeaderConfig();
        EnsureFooterConfig();
        EnsureAlertBarConfig();
        EnsureBannerConfig();

        // 2. Carpetas (sólo estructura; restringen AllowedContentTypes, sin propiedades, sin template).
        EnsureFolder(ContentTypeKeys.HeaderConfigFolder,    ContentTypeKeys.Aliases.HeaderConfigFolder,
            "Headers",          "icon-navigation-top",
            "Carpeta que agrupa todas las configuraciones de Header del sitio. Cada Header define cómo se renderiza la cabecera (navegación, CTA, logo, variante visual).");
        EnsureFolder(ContentTypeKeys.FooterConfigFolder,    ContentTypeKeys.Aliases.FooterConfigFolder,
            "Footers",          "icon-navigation-bottom",
            "Carpeta que agrupa todas las configuraciones de Footer del sitio. Cada Footer define navegación, copyright, newsletter y enlaces sociales.");
        EnsureFolder(ContentTypeKeys.AlertBarConfigFolder,  ContentTypeKeys.Aliases.AlertBarConfigFolder,
            "Alertas",          "icon-alert",
            "Carpeta de barras de alerta agendables. Múltiples alertas pueden apilarse verticalmente vía el Layout Profile del sitio.");
        EnsureFolder(ContentTypeKeys.BannerConfigFolder,    ContentTypeKeys.Aliases.BannerConfigFolder,
            "Banners",          "icon-picture",
            "Carpeta de banners promocionales con ventana de activación (fecha inicio/fin). Cada Banner selecciona un ReusableBlock como contenido.");
        EnsureFolder(ContentTypeKeys.LayoutProfileFolder,   ContentTypeKeys.Aliases.LayoutProfileFolder,
            "Perfiles",         "icon-layers",
            "Carpeta de Layout Profiles. Un profile compone un Header, un Footer, N Alertas y un Banner, y se asigna al sitio o a páginas específicas.");
        EnsureFolder(ContentTypeKeys.LayoutFolder,          ContentTypeKeys.Aliases.LayoutFolder,
            "Layout",           "icon-layout",
            "Contenedor principal de todas las configuraciones de layout del sitio: Headers, Footers, Alertas, Banners y Perfiles. Organiza el ensamblaje visual del sitio sin ser navegable desde la web.");
        EnsureFolder(ContentTypeKeys.NavigationFolder,      ContentTypeKeys.Aliases.NavigationFolder,
            "Navegaciones",     "icon-menu",
            "Carpeta de grupos de navegación (Main, Footer, Blog, etc.). Los Headers y Footers seleccionan estos grupos desde su picker de navegación.");

        // 3. Wire AllowedChildren top-down.
        SetAllowedChildren(ContentTypeKeys.LayoutFolder,
            ContentTypeKeys.HeaderConfigFolder,
            ContentTypeKeys.FooterConfigFolder,
            ContentTypeKeys.AlertBarConfigFolder,
            ContentTypeKeys.BannerConfigFolder,
            ContentTypeKeys.LayoutProfileFolder);

        SetAllowedChildren(ContentTypeKeys.HeaderConfigFolder,   ContentTypeKeys.HeaderConfig);
        SetAllowedChildren(ContentTypeKeys.FooterConfigFolder,   ContentTypeKeys.FooterConfig);
        SetAllowedChildren(ContentTypeKeys.AlertBarConfigFolder, ContentTypeKeys.AlertBarConfig);
        SetAllowedChildren(ContentTypeKeys.BannerConfigFolder,   ContentTypeKeys.BannerConfig);
        SetAllowedChildren(ContentTypeKeys.LayoutProfileFolder,  ContentTypeKeys.LayoutProfile);
        SetAllowedChildren(ContentTypeKeys.NavigationFolder,     ContentTypeKeys.NavigationGroup);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Factory de carpetas (contenedores estructurales sin propiedades, sin template)
    // ══════════════════════════════════════════════════════════════════════════

    private void EnsureFolder(Guid key, string alias, string name, string icon, string description)
    {
        var existing = Cts.Get(key);
        if (existing is not null)
        {
            var dirty = false;
            if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
            { existing.Name = name; dirty = true; }
            if (existing.ParentId != _settingsFolderId)
            { existing.ParentId = _settingsFolderId; dirty = true; }
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; dirty = true; }
            if (!string.Equals(existing.Icon, icon, StringComparison.Ordinal))
            { existing.Icon = icon; dirty = true; }

            // Garantiza no-enrutable: sin templates.
            if (existing.AllowedTemplates?.Any() == true)
            { existing.AllowedTemplates = []; dirty = true; }
            if (existing.DefaultTemplate is not null)
            { existing.SetDefaultTemplate(null); dirty = true; }

            if (dirty) Cts.Save(existing);
            return;
        }

        var ct = new ContentType(Ssh, _settingsFolderId)
        {
            Key             = key,
            Name            = name,
            Alias           = alias,
            Description     = description,
            Icon            = icon,
            IsContainer     = false,
            AllowedTemplates = []
        };

        // Tab mínimo para satisfacer Umbraco (sin properties).
        ct.PropertyGroups.Add(Tab("Carpeta", "folder", 0));
        Cts.Save(ct);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Hojas de config
    // ══════════════════════════════════════════════════════════════════════════

    private void EnsureHeaderConfig()
    {
        const string description = "Configuración de Header reutilizable. Define qué navegación, CTA, logo y estilo visual tiene la cabecera. Un Layout Profile referencia un Header Config para renderizarlo en las páginas que lo usen.";

        var existing = Cts.Get(ContentTypeKeys.HeaderConfig);
        if (existing is not null)
        {
            ClearTemplates(existing);
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; Cts.Save(existing); }
            return;
        }

        var ct = new ContentType(Ssh, _settingsFolderId)
        {
            Key              = ContentTypeKeys.HeaderConfig,
            Name             = "Header Config",
            Alias            = ContentTypeKeys.Aliases.HeaderConfig,
            Description      = description,
            Icon             = "icon-navigation-top",
            AllowedTemplates = []
        };

        var tabNav = Tab("Navegación", TabNavigation, 0);
        tabNav.PropertyTypes!.Add(Prop("headerNavigation", "Navegación del Header", DataTypeKeys.ContentPicker, 0,
            description: "Grupo de navegación (NavigationGroup) que se muestra como menú principal en la cabecera. Selecciona uno desde Config/Navigations."));
        tabNav.PropertyTypes!.Add(Prop("showPlatformNav",  "Mostrar Platform Strip", DataTypeKeys.ToggleBoolean, 10,
            description: "Activa la barra cross-world en la parte superior (Synergos · Flow Demo · Shop · Blog). Útil para sitios que forman parte de una plataforma multi-marca."));
        ct.PropertyGroups.Add(tabNav);

        var tabCta = Tab("CTA", TabCta, 1);
        tabCta.PropertyTypes!.Add(Prop("showCta",  "Mostrar CTA",        DataTypeKeys.ToggleBoolean, 0,
            description: "Activa el botón de acción principal en la cabecera (ej. 'Contactar', 'Empezar')."));
        tabCta.PropertyTypes!.Add(Prop("ctaLabel", "Texto del CTA",      DataTypeKeys.TextTitle,    10,
            description: "Texto visible del botón CTA. Ejemplo: 'Contáctanos', 'Probar gratis'."));
        tabCta.PropertyTypes!.Add(Prop("ctaUrl",   "URL del CTA",        DataTypeKeys.LinkUrl,      20,
            description: "Enlace al que lleva el botón CTA. Puede ser interno (/contacto) o externo (https://…)."));
        ct.PropertyGroups.Add(tabCta);

        var tabBrand = Tab("Marca", TabBranding, 2);
        tabBrand.PropertyTypes!.Add(Prop("logoOverride", "Logo (override)", DataTypeKeys.MediaImage, 0,
            description: "Logo específico para este Header. Si se deja vacío, se usa el logo del Theme Settings del sitio."));
        tabBrand.PropertyTypes!.Add(Prop("logoAltText",  "Texto alternativo del logo", DataTypeKeys.TextTitle, 10,
            description: "Texto alternativo para accesibilidad. Se lee en screen readers cuando el logo es imagen."));
        ct.PropertyGroups.Add(tabBrand);

        var tabStyle = Tab("Estilo", TabStyle, 3);
        tabStyle.PropertyTypes!.Add(Prop("headerStyle", "Estilo Visual", DataTypeKeys.SelectHeaderStyle, 0,
            description: "Variante visual del Header (default, compact, transparent, etc.). Cada variante aplica una clase CSS distinta."));
        tabStyle.PropertyTypes!.Add(Prop("sticky",      "Fijo al hacer scroll", DataTypeKeys.ToggleBoolean, 10,
            description: "Si está activo, el Header permanece pegado a la parte superior cuando el usuario hace scroll."));
        ct.PropertyGroups.Add(tabStyle);

        Cts.Save(ct);
    }

    private void EnsureFooterConfig()
    {
        const string description = "Configuración de Footer reutilizable. Define navegación, copyright, bloque de newsletter y toggle de redes sociales. Un Layout Profile referencia un Footer Config para renderizarlo al pie de página.";

        var existing = Cts.Get(ContentTypeKeys.FooterConfig);
        if (existing is not null)
        {
            ClearTemplates(existing);
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; Cts.Save(existing); }
            return;
        }

        var ct = new ContentType(Ssh, _settingsFolderId)
        {
            Key              = ContentTypeKeys.FooterConfig,
            Name             = "Footer Config",
            Alias            = ContentTypeKeys.Aliases.FooterConfig,
            Description      = description,
            Icon             = "icon-navigation-bottom",
            AllowedTemplates = []
        };

        var tabNav = Tab("Navegación", TabNavigation, 0);
        tabNav.PropertyTypes!.Add(Prop("footerNavigation", "Navegación del Footer", DataTypeKeys.ContentPicker, 0,
            description: "Grupo de navegación (NavigationGroup) que se muestra en el pie. Normalmente contiene enlaces legales, recursos, mapa del sitio."));
        ct.PropertyGroups.Add(tabNav);

        var tabContent = Tab("Contenido", TabContent, 1);
        tabContent.PropertyTypes!.Add(Prop("copyrightText",   "Texto de Copyright", DataTypeKeys.TextTitle,    0,
            description: "Texto legal al pie del Footer. Ejemplo: '© 2026 Synergos. Todos los derechos reservados.'"));
        tabContent.PropertyTypes!.Add(Prop("footerTagline",   "Tagline del Footer", DataTypeKeys.TextSubtitle, 10,
            description: "Frase breve descriptiva de la marca (opcional). Aparece normalmente junto al logo."));
        tabContent.PropertyTypes!.Add(Prop("showSocialLinks", "Mostrar iconos sociales", DataTypeKeys.ToggleBoolean, 20,
            description: "Renderiza los iconos de redes sociales leyendo las URLs configuradas en Site Settings (Facebook, Instagram, LinkedIn, etc.)."));
        ct.PropertyGroups.Add(tabContent);

        var tabNewsletter = Tab("Newsletter", TabNewsletter, 2);
        tabNewsletter.PropertyTypes!.Add(Prop("showNewsletter",      "Mostrar bloque de Newsletter", DataTypeKeys.ToggleBoolean, 0,
            description: "Renderiza el formulario de suscripción al newsletter dentro del Footer."));
        tabNewsletter.PropertyTypes!.Add(Prop("newsletterHeading",   "Título del Newsletter", DataTypeKeys.TextTitle, 10,
            description: "Encabezado del bloque de newsletter. Ejemplo: 'Suscríbete a nuestras novedades'."));
        tabNewsletter.PropertyTypes!.Add(Prop("newsletterActionUrl", "Endpoint del formulario", DataTypeKeys.TextUrl, 20,
            description: "URL donde se envían los emails de suscripción. Ejemplo: /api/newsletter/subscribe."));
        ct.PropertyGroups.Add(tabNewsletter);

        Cts.Save(ct);
    }

    private void EnsureAlertBarConfig()
    {
        const string description = "Barra de alerta agendable. Define un mensaje con ventana de activación (fecha inicio/fin) y una variante visual (info, warning, success, error). Un Layout Profile puede apilar múltiples Alert Bars.";

        var existing = Cts.Get(ContentTypeKeys.AlertBarConfig);
        if (existing is not null)
        {
            ClearTemplates(existing);
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; Cts.Save(existing); }
            return;
        }

        var ct = new ContentType(Ssh, _settingsFolderId)
        {
            Key              = ContentTypeKeys.AlertBarConfig,
            Name             = "Alert Bar Config",
            Alias            = ContentTypeKeys.Aliases.AlertBarConfig,
            Description      = description,
            Icon             = "icon-alert",
            AllowedTemplates = []
        };

        var tabContent = Tab("Contenido", TabContent, 0);
        tabContent.PropertyTypes!.Add(Prop("alertTitle",       "Título",     DataTypeKeys.TextTitle,      0,
            description: "Texto en negrita al inicio de la alerta. Ejemplo: 'Atención:', 'Nuevo:'."));
        tabContent.PropertyTypes!.Add(Prop("alertDescription", "Mensaje",    DataTypeKeys.TextAreaNotes, 10,
            description: "Mensaje principal que se muestra al usuario. Puede ser un párrafo corto."));
        tabContent.PropertyTypes!.Add(Prop("alertCtaLabel",    "Texto del CTA", DataTypeKeys.TextTitle,  20,
            description: "Texto del enlace de acción opcional. Ejemplo: 'Ver más', 'Entendido'."));
        tabContent.PropertyTypes!.Add(Prop("alertCtaUrl",      "URL del CTA",   DataTypeKeys.TextUrl,    30,
            description: "URL del enlace de acción opcional."));
        ct.PropertyGroups.Add(tabContent);

        var tabBehavior = Tab("Comportamiento", TabBehavior, 1);
        tabBehavior.PropertyTypes!.Add(Prop("variant",     "Variante Visual", DataTypeKeys.SelectAlertVariant, 0,
            description: "Estilo visual de la alerta: info (azul), warning (amarillo), success (verde) o error (rojo)."));
        tabBehavior.PropertyTypes!.Add(Prop("dismissible", "Cerrable por el usuario", DataTypeKeys.ToggleBoolean, 10,
            description: "Si está activo, el usuario puede cerrar la alerta con un botón de 'X'. La preferencia se guarda en su navegador."));
        ct.PropertyGroups.Add(tabBehavior);

        var tabSchedule = Tab("Programación", TabSchedule, 2);
        tabSchedule.PropertyTypes!.Add(Prop("startDate", "Fecha de inicio", DataTypeKeys.DateTimePicker, 0,
            description: "Fecha/hora desde la que la alerta empieza a mostrarse. Deja vacío para mostrarla siempre."));
        tabSchedule.PropertyTypes!.Add(Prop("endDate",   "Fecha de fin",    DataTypeKeys.DateTimePicker, 10,
            description: "Fecha/hora hasta la que la alerta se muestra. Deja vacío para que nunca expire."));
        ct.PropertyGroups.Add(tabSchedule);

        Cts.Save(ct);
    }

    private void EnsureBannerConfig()
    {
        const string description = "Banner promocional agendable. Selecciona un ReusableBlock como contenido y opcionalmente una ventana de fechas para activarlo automáticamente.";

        var existing = Cts.Get(ContentTypeKeys.BannerConfig);
        if (existing is not null)
        {
            ClearTemplates(existing);
            if (!string.Equals(existing.Description, description, StringComparison.Ordinal))
            { existing.Description = description; Cts.Save(existing); }
            return;
        }

        var ct = new ContentType(Ssh, _settingsFolderId)
        {
            Key              = ContentTypeKeys.BannerConfig,
            Name             = "Banner Config",
            Alias            = ContentTypeKeys.Aliases.BannerConfig,
            Description      = description,
            Icon             = "icon-picture",
            AllowedTemplates = []
        };

        var tabContent = Tab("Contenido", TabContent, 0);
        tabContent.PropertyTypes!.Add(Prop("bannerBlock", "Bloque del Banner", DataTypeKeys.ContentPicker, 0,
            description: "Selecciona un ReusableBlock (desde Shared Content) que será el contenido visual del banner."));
        ct.PropertyGroups.Add(tabContent);

        var tabSchedule = Tab("Programación", TabSchedule, 1);
        tabSchedule.PropertyTypes!.Add(Prop("startDate", "Fecha de inicio", DataTypeKeys.DateTimePicker, 0,
            description: "Fecha/hora desde la que el banner empieza a mostrarse. Deja vacío para mostrarlo siempre."));
        tabSchedule.PropertyTypes!.Add(Prop("endDate",   "Fecha de fin",    DataTypeKeys.DateTimePicker, 10,
            description: "Fecha/hora hasta la que el banner se muestra. Deja vacío para que nunca expire."));
        ct.PropertyGroups.Add(tabSchedule);

        Cts.Save(ct);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// v11.0.1 — los doctypes de Layout Config no deben ser enrutables.
    /// Limpia cualquier template asignado por migraciones previas.
    /// </summary>
    private void ClearTemplates(IContentType ct)
    {
        var dirty = false;
        if (ct.AllowedTemplates?.Any() == true)
        { ct.AllowedTemplates = []; dirty = true; }
        if (ct.DefaultTemplate is not null)
        { ct.SetDefaultTemplate(null); dirty = true; }
        if (dirty) Cts.Save(ct);
    }

    private void SetAllowedChildren(Guid parentKey, params Guid[] childKeys)
    {
        var parent = Cts.Get(parentKey);
        if (parent is null) return;

        var allowed = childKeys
            .Select(k => Cts.Get(k))
            .Where(ct => ct is not null)
            .Select((ct, i) => new ContentTypeSort(ct!.Id, i))
            .ToArray();

        var existing    = parent.AllowedContentTypes?.ToList() ?? [];
        var existingIds = existing.Select(a => a.Id.Value).ToHashSet();

        foreach (var a in allowed)
        {
            if (!existingIds.Contains(a.Id.Value))
                existing.Add(a);
        }

        parent.AllowedContentTypes = existing;
        Cts.Save(parent);
    }
}
