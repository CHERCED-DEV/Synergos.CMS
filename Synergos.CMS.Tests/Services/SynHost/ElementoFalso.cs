using System.Globalization;
using NSubstitute;
using Umbraco.Cms.Core.Dictionary;
using Umbraco.Cms.Core.Models.PublishedContent;

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
            var propiedad = Substitute.For<IPublishedProperty>();
            propiedad.Alias.Returns(alias);
            propiedad.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor is not null);
            propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(valor);
            elemento.GetProperty(alias).Returns(propiedad);
        }

        return elemento;
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
