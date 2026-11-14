using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La cabecera de las apps dentro de un sitio (D-016-V2): secundaria por defecto, la completa si el sitio la pide, y
/// la completa siempre que la página no pinte la cabecera del sitio.
/// </summary>
public sealed class NegocioDeCabeceraTests
{
    private static readonly Guid Tienda = Guid.Parse("cd460fe9-fac2-4d01-b6c7-5c23c4b7c38b");
    private static readonly Guid Otro = Guid.Parse("0b0e7a52-6a3e-4c1f-9d2b-5f1a2c3d4e5f");

    private static CabeceraFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<CabeceraFeatureSettings>()
           ?? new CabeceraFeatureSettings();

    [Fact]
    public void Sin_declarar_nada_dentro_de_un_sitio_es_secundaria()
    {
        var seccion = new CabeceraFeatureSettings();

        Assert.Equal(new NegocioDeCabecera(ModoDeCabecera.Secundaria), seccion.Para(null));
        Assert.Equal(ModoDeCabecera.Secundaria, seccion.Para(Otro).Modo);
        Assert.Empty(seccion.Problemas());
    }

    [Fact]
    public void Un_sitio_pide_la_completa_por_su_key_y_los_demas_heredan()
    {
        var seccion = Seccion(new Dictionary<string, string?> { [$"Sitios:{Tienda}:Modo"] = " Completa " });

        Assert.Equal(ModoDeCabecera.Completa, seccion.Para(Tienda).Modo);
        Assert.Equal(ModoDeCabecera.Secundaria, seccion.Para(Otro).Modo);
    }

    [Fact]
    public void Una_palabra_que_no_es_ninguna_de_las_dos_no_deja_arrancar()
    {
        var seccion = Seccion(new Dictionary<string, string?> { [$"Sitios:{Tienda}:Modo"] = "oculta" });

        Assert.Single(seccion.Problemas());
    }

    [Theory]
    [InlineData(true, "secundaria", "secundaria")]
    [InlineData(true, "completa", "completa")]
    [InlineData(true, "", "secundaria")]
    [InlineData(false, "secundaria", "completa")]
    [InlineData(false, "completa", "completa")]
    public void Sin_la_cabecera_del_sitio_en_la_pagina_la_app_pinta_la_completa(bool pintaLaDelSitio, string declarado, string esperado)
    {
        Assert.Equal(esperado, ModoDeCabecera.Decidir(pintaLaDelSitio, declarado));
    }
}
