using NSubstitute;
using Synergos.CMS.Web.Services.Diccionario;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services.Diccionario;

/// <summary>
/// Cubre <see cref="DiccionarioDelBridge"/>: qué claves publica el bridge para las secciones de la
/// página (ADR 0136 §1) y el <b>fallback por clave</b> a la cultura por defecto (§3).
/// </summary>
/// <remarks>
/// El diccionario falso reproduce la forma de Umbraco: raíces sin texto (los contenedores) y sus
/// descendientes con una traducción por idioma. Cada ítem se arma ENTERO antes de engancharlo
/// (memoria <c>feedback_nsubstitute_returns_inside_returns</c>).
/// </remarks>
public sealed class DiccionarioDelBridgeTests
{
    private const string Es = "es-CO";
    private const string En = "en-US";

    private static DiccionarioDelBridge Diccionario(params (string Alias, string? Es, string? En)[] items)
    {
        var porRaiz = items
            .Where(i => i.Alias.Contains('.'))
            .GroupBy(i => i.Alias.Split('.')[0])
            .ToDictionary(g => g.Key, g => g.Select(i => Item(i.Alias, i.Es, i.En)).ToList());

        var raices = porRaiz.Keys.Select(r => (Alias: r, Clave: Guid.NewGuid())).ToList();
        var contenedores = raices.Select(r => Item(r.Alias, null, null, r.Clave)).ToList();
        var localizacion = Substitute.For<ILocalizationService>();
        localizacion.GetRootDictionaryItems().Returns(contenedores);
        foreach (var (alias, clave) in raices)
        {
            var hijos = porRaiz[alias];
            localizacion.GetDictionaryItemDescendants(clave).Returns(hijos);
        }

        localizacion.GetDefaultLanguageIsoCode().Returns(Es);
        return new DiccionarioDelBridge(localizacion);
    }

    private static IDictionaryItem Item(string alias, string? es, string? en, Guid? key = null)
    {
        var traducciones = new List<IDictionaryTranslation>();
        if (es is not null) traducciones.Add(Traduccion(Es, es));
        if (en is not null) traducciones.Add(Traduccion(En, en));

        var clave = key ?? Guid.NewGuid();
        var item = Substitute.For<IDictionaryItem>();
        item.ItemKey.Returns(alias);
        item.Key.Returns(clave);
        item.Translations.Returns(traducciones);
        return item;
    }

    private static IDictionaryTranslation Traduccion(string cultura, string valor)
    {
        var traduccion = Substitute.For<IDictionaryTranslation>();
        traduccion.LanguageIsoCode.Returns(cultura);
        traduccion.Value.Returns(valor);
        return traduccion;
    }

    private static readonly (string, string?, string?)[] Base =
    {
        ("Slider.Next", "Siguiente diapositiva", "Next slide"),
        ("Slider.Previous", "Diapositiva anterior", "Previous slide"),
        ("Rating.Stars.Aria", "{n} de {max} estrellas", "{n} out of {max} stars"),
        ("Common.States.NoResults", "Sin resultados.", "No results found."),
        ("Common.Buttons.Search", "Buscar", null),
        ("Tagline.Hero", "No es de Tag", "Not Tag's"),
    };

    [Fact]
    public void Sin_secciones_no_se_publica_nada()
    {
        Assert.Empty(Diccionario(Base).Claves(Array.Empty<string>(), Es, Es));
    }

    [Fact]
    public void Se_publican_las_claves_de_las_secciones_declaradas_en_la_cultura_activa()
    {
        var claves = Diccionario(Base).Claves(new[] { "Slider", "Rating" }, En, Es);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Slider.Next"] = "Next slide",
                ["Slider.Previous"] = "Previous slide",
                ["Rating.Stars.Aria"] = "{n} out of {max} stars",
            },
            claves);
    }

    [Fact]
    public void Una_clave_sin_traduccion_en_la_cultura_activa_sale_con_la_de_la_cultura_por_defecto()
    {
        // ADR 0136 §3, criterio 3 del piloto: pedida en en-US, la clave que sólo existe en es-CO se
        // publica en es-CO — no desaparece del bridge para que t() devuelva la clave cruda.
        var claves = Diccionario(Base).Claves(new[] { "Common.Buttons" }, En, Es);

        Assert.Equal("Buscar", claves["Common.Buttons.Search"]);
    }

    [Fact]
    public void Una_seccion_casa_por_prefijo_entero_sin_mayusculas_y_no_publica_los_contenedores()
    {
        var claves = Diccionario(Base).Claves(new[] { "common.states", "Tag" }, Es, Es);

        // `common.states` casa `Common.States.NoResults` (Umbraco resuelve sin mayúsculas) y NO
        // `Common.Buttons.Search`; `Tag` no casa `Tagline.Hero` (el prefijo es la sección ENTERA).
        Assert.Equal(new[] { "Common.States.NoResults" }, claves.Keys);
        Assert.DoesNotContain("Common", claves.Keys);
    }

    [Fact]
    public void Una_clave_sin_texto_en_ninguna_cultura_no_se_publica()
    {
        var claves = Diccionario(("Slider.Vacia", null, null), ("Slider.Next", "Siguiente", "Next"))
            .Claves(new[] { "Slider" }, En, Es);

        Assert.Equal(new[] { "Slider.Next" }, claves.Keys);
    }

    [Fact]
    public void Pedir_dos_veces_las_mismas_secciones_da_lo_mismo()
    {
        var diccionario = Diccionario(Base);

        Assert.Equal(
            diccionario.Claves(new[] { "Slider", "Common.Buttons" }, En, Es),
            diccionario.Claves(new[] { "Common.Buttons", "Slider", "Slider" }, En, Es));
    }

    [Fact]
    public void La_cultura_por_defecto_la_dice_Umbraco_no_un_literal()
    {
        Assert.Equal(Es, Diccionario(Base).CulturaPorDefecto());
    }
}
