using System.Globalization;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynHeroBanner</c> → <see cref="HeroBannerProps"/>, y el respaldo SSR del mismo bloque.
/// </summary>
/// <remarks>
/// El medio viaja como su URL absoluta y el enlace como su destino. El respaldo SSR sale de los
/// MISMOS valores que el record: armado en la vista, el SSR y lo que hidrata volvían a poder leer
/// cosas distintas — que es exactamente lo que pasaba (el SSR pintaba la foto y el botón que el
/// elemento tiraba).
/// </remarks>
public sealed class HeroBannerResolutor : IResolutorSynHost<HeroBannerProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly IPublishedUrlProvider _urls;
    private readonly ICultureDictionaryFactory _diccionarios;
    private readonly ILogger<HeroBannerResolutor> _log;

    public HeroBannerResolutor(
        IPublishedValueFallback fallback,
        IPublishedUrlProvider urls,
        ICultureDictionaryFactory diccionarios,
        ILogger<HeroBannerResolutor> log)
    {
        _fallback = fallback;
        _urls = urls;
        _diccionarios = diccionarios;
        _log = log;
    }

    public ElementoResuelto<HeroBannerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log, _urls);
        var imagen = editor.Medio("media");
        var cta = editor.Enlace("ctaLink");

        var props = new HeroBannerProps(
            Title: editor.Texto("title"),
            Subtitle: editor.Texto("subtitle"),
            Media: imagen?.Url,
            MediaAlt: imagen?.Alt,
            CtaLabel: editor.Texto("ctaLabel") ?? cta?.Nombre,
            CtaLink: cta?.Url);

        var respaldo = SynHostFallbackBuilder.HeroBanner(
            props.Title, props.Subtitle, props.Media, props.MediaAlt, props.CtaLabel, props.CtaLink, NombreDeLaSeccion());

        return new ElementoResuelto<HeroBannerProps>(props, respaldo);
    }

    /// <summary>
    /// El nombre de la sección para el respaldo SSR cuando el editor deja el título vacío, del
    /// diccionario (<c>Synhost.Hero.Aria</c>).
    /// </summary>
    private string NombreDeLaSeccion()
    {
        var (clave, respaldo) = ("Synhost.Hero.Aria", "Sección destacada");

        // Umbraco devuelve cadena vacía cuando la clave no existe, y algunos proveedores la clave
        // misma: ninguna de las dos es un nombre.
        var traducido = _diccionarios.CreateDictionary(CultureInfo.CurrentUICulture)[clave];
        return string.IsNullOrWhiteSpace(traducido) || string.Equals(traducido, clave, StringComparison.Ordinal)
            ? respaldo
            : traducido;
    }
}
