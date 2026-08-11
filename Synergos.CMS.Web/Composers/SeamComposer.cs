using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Proxies.Impl;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Wires the extension seams declared in
/// <c>Synergos.CMS.Interfaces</c> to their Ola 1 defaults and Ola 3
/// adapters, and registers the dictionary notification handler.
/// </summary>
/// <remarks>
/// Per ADR 0005 all <see cref="IComposer"/> implementations live in
/// <c>Synergos.CMS.Web/Composers/</c>. This composer extracts
/// <c>IOptions&lt;T&gt;.Value</c> and injects the POCO into defaults,
/// honouring the decision taken in Ola 1 not to add
/// <c>Microsoft.Extensions.Options</c> as a reference of
/// <c>Synergos.CMS.Application</c>.
/// </remarks>
[ComposeAfter(typeof(OptionsComposer))]
public sealed partial class SeamComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        ComposePlatform(builder);
        ComposeTracking(builder);
        ComposePaymentEngine(builder);
        ComposeTravelAndBooking(builder);
        ComposeShop(builder);
        ComposeSocial(builder);
        ComposeAcademy(builder);
        ComposeFormsSearchAndMemberAdmin(builder);
        ComposePlatformServicesAndHealthcare(builder);
        ComposeEventsPropertiesAndGov(builder);
        ComposeModerationDevAndNotifications(builder);
    }

    /// <summary>
    /// Si el catálogo de un vertical se sirve del CONTENIDO del CMS o del seed de demo.
    /// </summary>
    /// <remarks>
    /// Una sola lectura del flag para los tres verticales que ya tienen fuente Umbraco-backed
    /// (Tienda, Eventos, Inmobiliaria). El default es <c>demo</c> — un vertical al que se le
    /// olvide la clave sirve la demo, no un catálogo vacío.
    /// </remarks>
    private static bool IsCmsSource(IServiceProvider sp, string vertical)
    {
        var settings = sp.GetRequiredService<IOptionsMonitor<CatalogSettings>>().CurrentValue;
        var source = settings.Sources.TryGetValue(vertical, out var s) ? s : "demo";
        return string.Equals(source, "cms", StringComparison.OrdinalIgnoreCase);
    }
}
