using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Los créditos de las fotos (016-contenido-piezas): una CC BY se lista con autor, origen y licencia; CC0 y dominio
/// público no piden crédito y no se listan.
/// </summary>
public sealed class CreditosDeFotosTests
{
    [Theory]
    [InlineData("CC BY 2.0", true)]
    [InlineData("CC BY-SA 4.0", true)]
    [InlineData("cc by 3.0", true)]
    [InlineData("CC0 1.0", false)]
    [InlineData("Dominio público (PDM 1.0)", false)]
    [InlineData("", false)]
    public void Sabe_que_licencia_pide_credito(string licencia, bool pide)
    {
        Assert.Equal(pide, CreditosDeFotos.PideAtribucion(licencia));
    }

    [Fact]
    public void Enlaza_el_texto_de_la_licencia_en_espanol()
    {
        Assert.Equal("https://creativecommons.org/licenses/by/2.0/deed.es", CreditosDeFotos.UrlDeLicencia("CC BY 2.0"));
        Assert.Equal("https://creativecommons.org/licenses/by-sa/4.0/deed.es", CreditosDeFotos.UrlDeLicencia("CC BY-SA 4.0"));
        Assert.Null(CreditosDeFotos.UrlDeLicencia("CC0 1.0"));
    }

    [Fact]
    public void Lee_autor_titulo_y_origen_con_o_sin_titulo()
    {
        Assert.Equal(("mrbichel", "Leather Laptop Sleeves", "https://www.flickr.com/photos/x/1"),
            CreditosDeFotos.Leer("Foto: mrbichel · «Leather Laptop Sleeves» · https://www.flickr.com/photos/x/1"));
        Assert.Equal(("Photo Mix", (string?)null, "https://stocksnap.io/photo/a"),
            CreditosDeFotos.Leer("Foto: Photo Mix · https://stocksnap.io/photo/a"));
    }

    [Fact] // D-016-C27: CC BY pide decir si la obra se cambió; el título y el origen siguen leyéndose igual.
    public void Dice_lo_que_se_le_hizo_a_la_foto()
    {
        const string credito = "Foto: Julian Renom · «Deluxe Room» · recortada · https://commons.wikimedia.org/wiki/File:X.jpg";

        Assert.Equal(("Julian Renom", "Deluxe Room", "https://commons.wikimedia.org/wiki/File:X.jpg"), CreditosDeFotos.Leer(credito));
        Assert.Contains("«Deluxe Room»</a>, de Julian Renom, recortada. Licencia",
            CreditosDeFotos.Html([("Habitación deluxe", "CC BY 4.0", credito)]), StringComparison.Ordinal);
        Assert.Empty(CreditosDeFotos.Cambios("Foto: mrbichel · «Leather Laptop Sleeves» · https://www.flickr.com/photos/x/1"));
    }

    [Fact]
    public void La_lista_muestra_sólo_las_que_piden_crédito_con_autor_origen_y_licencia()
    {
        var html = CreditosDeFotos.Html(
        [
            ("Funda de cuero café", "CC BY 2.0", "Foto: mrbichel · «Leather Laptop Sleeves» · https://www.flickr.com/photos/x/1"),
            ("Taza negra", "CC0 1.0", "Foto: alguien · https://ejemplo.org/2"),
            ("Funda de cuero café", "CC BY 2.0", "Foto: mrbichel · «Leather Laptop Sleeves» · https://www.flickr.com/photos/x/1"),
        ]);

        Assert.Contains("Funda de cuero café", html, StringComparison.Ordinal);
        Assert.Contains("mrbichel", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://www.flickr.com/photos/x/1\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://creativecommons.org/licenses/by/2.0/deed.es\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Taza negra", html, StringComparison.Ordinal);
        // La misma foto en la tarjeta y en la ficha se acredita una vez.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "<li>"));
    }
}
