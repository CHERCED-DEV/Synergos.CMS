using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Binds typed POCOs from <c>appsettings.*.json</c> into the DI
/// container so that Application services can consume them via
/// <c>IOptions&lt;T&gt;</c> / <c>IOptionsMonitor&lt;T&gt;</c> at the Web
/// composition boundary.
/// </summary>
/// <remarks>
/// Per ADR 0005 all composers live in
/// <c>Synergos.CMS.Web/Composers/</c>. Per ADR 0002 the Application
/// project does not reference <c>Microsoft.Extensions.Options</c>;
/// bindings are resolved here, and Web-level wiring (done in a future
/// composer — see Ola 3) extracts <c>.Value</c> and injects the POCO
/// into defaults such as <c>DefaultBrandingProvider</c>.
///
/// Option sections:
/// <list type="bullet">
///   <item><c>Synergos:Branding</c> → <see cref="BrandingSettings"/> (ADR 0010)</item>
///   <item><c>Synergos:FeatureFlags</c> → <see cref="FeatureFlagsSettings"/> (ADR 0011)</item>
/// </list>
/// </remarks>
public sealed class OptionsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<BrandingSettings>(
            builder.Config.GetSection("Synergos:Branding"));

        builder.Services.Configure<FeatureFlagsSettings>(
            builder.Config.GetSection("Synergos:FeatureFlags"));

        builder.Services.Configure<LayoutComposerSettings>(
            builder.Config.GetSection("Synergos:LayoutComposer"));

        builder.Services.Configure<CdnSettings>(
            builder.Config.GetSection("Synergos:Cdn"));

        builder.Services.Configure<DevSeedSettings>(
            builder.Config.GetSection("Synergos:DevSeed"));

        builder.Services.Configure<MembersSettings>(
            builder.Config.GetSection("Synergos:Members"));

        builder.Services.Configure<CartSettings>(
            builder.Config.GetSection("Synergos:Cart"));

        builder.Services.Configure<CartAbandonmentSettings>(
            builder.Config.GetSection("Synergos:CartAbandonment"));

        builder.Services.Configure<FormsSettings>(
            builder.Config.GetSection("Synergos:Forms"));

        builder.Services.Configure<SearchSettings>(
            builder.Config.GetSection("Synergos:Search"));

        builder.Services.Configure<EmailSettings>(
            builder.Config.GetSection("Synergos:Email"));

        builder.Services.Configure<OutputCacheSettings>(
            builder.Config.GetSection("Synergos:OutputCache"));

        builder.Services.Configure<CommentsSettings>(
            builder.Config.GetSection("Synergos:Comments"));

        builder.Services.Configure<AdminSettings>(
            builder.Config.GetSection("Synergos:Admin"));
    }
}
