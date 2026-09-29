using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynCarousel</c> → <see cref="CarouselProps"/>. Parsea en el servidor el JSON de
/// diapositivas y traduce el intervalo del editor a las dos decisiones del elemento.
/// </summary>
/// <remarks>
/// Las claves de cada diapositiva son las que documenta el ElementType (<c>imageUrl</c>,
/// <c>alt</c>, <c>caption</c>) y salen con los nombres que el elemento lee (<c>src</c>,
/// <c>alt</c>, <c>label</c>). Una diapositiva sin imagen no viaja y se anota.
/// </remarks>
public sealed class CarouselResolutor : IResolutorSynHost<CarouselProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<CarouselResolutor> _log;

    public CarouselResolutor(IPublishedValueFallback fallback, ILogger<CarouselResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<CarouselProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var intervalo = editor.Entero("autoplayInterval");
        var conAutoplay = intervalo is > 0;

        return new ElementoResuelto<CarouselProps>(new CarouselProps(
            Slides: Diapositivas(editor, editor.Texto("slidesJson")),
            Autoplay: conAutoplay ? true : null,
            Interval: conAutoplay ? intervalo : null));
    }

    private static IReadOnlyList<CarouselSlide>? Diapositivas(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("slidesJson", json);
        if (lista is null)
        {
            return null;
        }

        var diapositivas = new List<CarouselSlide>();
        foreach (var entrada in lista)
        {
            var imagen = LectorDelEditor.Cadena(entrada, "imageUrl");
            if (imagen is null)
            {
                editor.NoEsValido("slidesJson", entrada.GetRawText(), "una diapositiva con imageUrl");
                continue;
            }

            diapositivas.Add(new CarouselSlide(
                imagen,
                LectorDelEditor.Cadena(entrada, "alt"),
                LectorDelEditor.Cadena(entrada, "caption")));
        }

        return diapositivas.Count > 0 ? diapositivas : null;
    }
}
