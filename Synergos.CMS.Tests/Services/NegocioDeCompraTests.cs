using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Si un sitio vende en línea (<c>Synergos:Features:Compra</c>, 016-contenido-piezas): por defecto sí; Propiedades y
/// Booking lo apagan por la Key de su siteRoot, y el carrito sale de sus fichas, tarjetas y encabezado.
/// </summary>
public sealed class NegocioDeCompraTests
{
    private static readonly Guid Otro = Guid.Parse("0b0e7a52-6a3e-4c1f-9d2b-5f1a2c3d4e5f");

    private static CompraFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<CompraFeatureSettings>()
           ?? new CompraFeatureSettings();

    [Fact]
    public void Sin_declarar_nada_todo_sitio_vende_en_linea()
    {
        var seccion = new CompraFeatureSettings();

        Assert.Equal(new NegocioDeCompra(true), seccion.Para(null));
        Assert.Equal(new NegocioDeCompra(true), seccion.Para(Otro));
        Assert.Empty(seccion.Problemas());
    }

    [Fact]
    public void Un_sitio_la_apaga_por_su_key_y_los_demas_la_heredan()
    {
        var seccion = Seccion(new Dictionary<string, string?>
        {
            [$"Sitios:{DevContenidoDeVerticales.Propiedades}:EnLinea"] = "false",
        });

        Assert.False(seccion.Para(DevContenidoDeVerticales.Propiedades).EnLinea);
        Assert.True(seccion.Para(Otro).EnLinea);
        Assert.True(seccion.Para(null).EnLinea);
    }

    [Fact]
    public void La_configuracion_que_viaja_apaga_Propiedades_y_Booking_y_deja_la_Tienda()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "Synergos.CMS.sln"))) { raiz = raiz.Parent; }
        Assert.NotNull(raiz);

        var seccion = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(raiz!.FullName, "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build()
            .GetSection(CompraFeatureSettings.Seccion)
            .Get<CompraFeatureSettings>()!;

        Assert.False(seccion.Para(DevContenidoDeVerticales.Propiedades).EnLinea);
        Assert.False(seccion.Para(DevContenidoDeVerticales.Booking).EnLinea);
        Assert.True(seccion.Para(DevTiendaPorPaginas.Tienda).EnLinea);
        Assert.Empty(seccion.Problemas());
    }
}
