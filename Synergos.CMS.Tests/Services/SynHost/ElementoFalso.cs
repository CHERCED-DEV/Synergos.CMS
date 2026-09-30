using System.Globalization;
using NSubstitute;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Un bloque autorado sin arrancar Umbraco: un <see cref="IPublishedElement"/> cuyas propiedades
/// devuelven lo que el test le dice, y lo que un resolver necesita alrededor.
/// </summary>
/// <remarks>
/// Cada propiedad se arma ENTERA en una variable antes de engancharla al elemento: un
/// <c>Returns</c> que construye otro sustituto adentro deja la configuración a medias
/// (memoria <c>feedback_nsubstitute_returns_inside_returns</c>).
/// </remarks>
internal static class ElementoFalso
{
    /// <summary>El fallback que no inventa nada: sin valor, <c>default</c>.</summary>
    public static IPublishedValueFallback Fallback { get; } = new NoopPublishedValueFallback();

    /// <summary>Un bloque con estas propiedades; un valor <c>null</c> es «el editor no lo tocó».</summary>
    public static IPublishedElement Con(params (string Alias, object? Valor)[] valores)
    {
        var elemento = Substitute.For<IPublishedElement>();
        foreach (var (alias, valor) in valores)
        {
            var propiedad = Propiedad(alias, valor);
            elemento.GetProperty(alias).Returns(propiedad);
        }

        return elemento;
    }

    /// <summary>
    /// Un medio de la biblioteca con su fichero en <paramref name="archivo"/> (lo que Umbraco
    /// guarda en <c>umbracoFile</c>) y, si se da, su texto alternativo (<c>altDefault</c>).
    /// </summary>
    public static IPublishedContent Medio(string archivo, string? alt = null)
    {
        var fichero = Propiedad("umbracoFile", archivo);
        var textoAlternativo = Propiedad("altDefault", alt);
        var medio = Substitute.For<IPublishedContent>();
        medio.Key.Returns(Guid.NewGuid());
        medio.GetProperty("umbracoFile").Returns(fichero);
        medio.GetProperty("altDefault").Returns(textoAlternativo);
        return medio;
    }

    /// <summary>
    /// El proveedor de URLs: la de un medio es su <c>umbracoFile</c>, tal cual, en cualquier modo
    /// (los tests que miran el modo lo comprueban con <c>Received</c>).
    /// </summary>
    public static IPublishedUrlProvider Urls()
    {
        var urls = Substitute.For<IPublishedUrlProvider>();
        urls.GetMediaUrl(Arg.Any<IPublishedContent>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<Uri?>())
            .Returns(llamada => llamada.ArgAt<IPublishedContent>(0).GetProperty("umbracoFile")?.GetValue() as string ?? string.Empty);
        return urls;
    }

    /// <summary>
    /// Un enlace de un <c>Umbraco.MultiUrlPicker</c> como lo entrega Umbraco al convertirlo: el
    /// destino ya resuelto, el texto que escribió el editor y dónde abre.
    /// </summary>
    public static Link Enlace(string url, string? nombre = null, string? destino = null)
        => new() { Url = url, Name = nombre, Target = destino, Type = LinkType.External };

    private static IPublishedProperty Propiedad(string alias, object? valor)
    {
        var propiedad = Substitute.For<IPublishedProperty>();
        propiedad.Alias.Returns(alias);
        propiedad.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor is not null);
        propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);
        return propiedad;
    }

    /// <summary>Un diccionario de uSync con estas traducciones; lo demás, vacío (como Umbraco).</summary>
    public static ICultureDictionaryFactory Diccionario(params (string Clave, string Texto)[] traducciones)
    {
        var diccionario = Substitute.For<ICultureDictionary>();
        diccionario[Arg.Any<string>()].Returns(string.Empty);
        foreach (var (clave, texto) in traducciones)
        {
            diccionario[clave].Returns(texto);
        }

        var fabrica = Substitute.For<ICultureDictionaryFactory>();
        fabrica.CreateDictionary(Arg.Any<CultureInfo>()).Returns(diccionario);
        return fabrica;
    }
}
