using System.Globalization;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Pre-popula <see cref="RequestLocalizationOptions"/> con todas las
/// Languages registradas en la DB Umbraco al boot, para neutralizar
/// el race condition de <c>DynamicRequestCultureProviderBase.TryAddLocked</c>.
/// </summary>
/// <remarks>
/// Bug upstream documentado en review de Umbraco PR #14064 (introducido
/// en v11.4, presente en todas las versiones 12/13/14/15+, sin fix
/// shipped en 13.x). El método nativo enumera <c>supportedCultures.Any(...)</c>
/// fuera del lock mientras otro thread está adentro mutando la misma
/// lista — bajo tráfico concurrente en el primer request post-boot
/// (típico tras <c>uSync Import full all</c>) se dispara
/// <see cref="System.InvalidOperationException"/> "Collection was
/// modified".
///
/// Mitigación: si las cultures de DB ya están en la lista al primer
/// request, <c>TryAddLocked</c> nunca tiene que mutar y el race no
/// tiene oportunidad de manifestarse. Cambios en Languages requieren
/// restart para entrar al pool — aceptable, no es escenario hot-reload.
///
/// Ola post-135 fix.
/// </remarks>
public sealed class LocalizationComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<RequestLocalizationOptions>()
            .Configure<ILocalizationService>((opts, localization) =>
            {
                foreach (var lang in localization.GetAllLanguages())
                {
                    CultureInfo ci;
                    try
                    {
                        ci = CultureInfo.GetCultureInfo(lang.IsoCode);
                    }
                    catch (CultureNotFoundException)
                    {
                        continue;
                    }

                    var supported = opts.SupportedCultures;
                    if (supported is not null &&
                        !supported.Any(c => c.Name.Equals(ci.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        supported.Add(ci);
                    }

                    var supportedUi = opts.SupportedUICultures;
                    if (supportedUi is not null &&
                        !supportedUi.Any(c => c.Name.Equals(ci.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        supportedUi.Add(ci);
                    }
                }
            });
    }
}
