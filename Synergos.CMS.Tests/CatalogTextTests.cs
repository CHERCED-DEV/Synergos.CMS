using Synergos.CMS.Application.Services.Impl;
using Xunit;

namespace Synergos.CMS.Tests;

/// <summary>
/// El bug real que cierra <see cref="CatalogText"/>: verificado EN VIVO contra el CMS antes
/// de escribir el fix — <c>"Tecnología"</c> devolvía 2 productos y <c>"tecnologia"</c> cero,
/// en los 5 verticales a la vez.
/// </summary>
public class CatalogTextTests
{
    private const char CombiningAcute = (char)0x0301;   // ´ suelta, se pega a la letra previa
    private const char CombiningTilde = (char)0x0303;   // ~ suelta (la de la ñ)

    [Theory]
    [InlineData("Bogotá", "bogota")]
    [InlineData("Tecnología", "tecnologia")]
    [InlineData("Medellín", "medellin")]
    [InlineData("Registraduría", "registraduria")]
    [InlineData("José García", "jose garcia")]
    [InlineData("Educación", "educacion")]
    [InlineData("Pingüino", "pinguino")]   // la diéresis SÍ se pliega
    [InlineData("Diseño", "diseño")]       // la eñe NO
    public void Fold_quita_tildes_y_baja_a_minusculas(string input, string expected)
        => Assert.Equal(expected, CatalogText.Fold(input));

    [Fact]
    public void Fold_no_confunde_la_eñe_con_una_n()
    {
        // Si el plegado tratara la ñ como diacrítico, "año" y "ano" colisionarían — y esa
        // es una colisión que en es-CO no se puede permitir.
        Assert.NotEqual(CatalogText.Fold("año"), CatalogText.Fold("ano"));
    }

    [Fact]
    public void Fold_preserva_la_eñe_escrita_como_combinante()
    {
        // La ñ también llega descompuesta (n + U+0303) según la fuente del dato. El
        // centinela solo aparta la forma precompuesta, así que ESTA es la que revelaría
        // que el plegado la degrada a "ano".
        var combinante = "an" + CombiningTilde + "o";
        Assert.Equal("año", CatalogText.Fold(combinante));
    }

    [Fact]
    public void Fold_unifica_la_tilde_precompuesta_y_la_combinante()
    {
        // "á" puede llegar como U+00E1 (precompuesta) o como "a" + U+0301 (combinante):
        // fuentes distintas (teclado macOS vs Windows vs JSON) producen formas distintas y
        // como strings crudos NO son iguales. Ambas deben plegar a lo mismo.
        var precompuesta = "Bogotá";
        var combinante = "Bogota" + CombiningAcute;
        Assert.NotEqual(precompuesta, combinante);              // distintas como strings...
        Assert.Equal("bogota", CatalogText.Fold(precompuesta));
        Assert.Equal("bogota", CatalogText.Fold(combinante));   // ...idénticas al plegar.
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Fold_tolera_vacio(string? input)
        => Assert.Equal(string.Empty, CatalogText.Fold(input));

    [Fact]
    public void Fold_es_idempotente()
    {
        // Importa porque un lado de la comparación puede venir ya plegado y el otro no.
        var once = CatalogText.Fold("Bogotá");
        Assert.Equal(once, CatalogText.Fold(once));
    }

    [Theory]
    // Sin tilde encuentra con tilde — EL bug.
    [InlineData("Laptop Pro 14 Tecnología", "tecnologia", true)]
    // Con tilde encuentra sin tilde — la simétrica.
    [InlineData("Tecnologia sin tilde en el dato", "tecnología", true)]
    // Sigue siendo case-insensitive (no se pierde lo que ya funcionaba).
    [InlineData("Camiseta Esencial Negra", "CAMISETA", true)]
    // Sigue siendo substring, no prefijo.
    [InlineData("Concierto en Bogotá", "bogota", true)]
    // Y sigue sin encontrar lo que no está.
    [InlineData("Concierto en Bogotá", "cali", false)]
    public void Contains_ignora_tildes_en_ambas_direcciones(string haystack, string needle, bool expected)
        => Assert.Equal(expected, CatalogText.Contains(haystack, needle));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contains_con_needle_vacio_no_filtra(string? needle)
    {
        // Preserva el comportamiento previo: los catálogos ya hacían short-circuit con
        // IsNullOrWhiteSpace antes de filtrar. Si esto devolviera false, un catálogo sin
        // búsqueda saldría VACÍO.
        Assert.True(CatalogText.Contains("cualquier cosa", needle));
    }

    [Fact]
    public void Contains_con_haystack_nulo_no_lanza()
        => Assert.False(CatalogText.Contains(null, "bogota"));

    [Theory]
    [InlineData("Tecnología", "tecnologia", true)]
    [InlineData("Tecnología", "TECNOLOGÍA", true)]
    // AreEqual es exacto: a diferencia de Contains, un prefijo NO iguala.
    [InlineData("Tecnología", "tecno", false)]
    [InlineData("Hogar", "Deportes", false)]
    public void AreEqual_es_igualdad_exacta_plegada(string a, string b, bool expected)
        => Assert.Equal(expected, CatalogText.AreEqual(a, b));
}
