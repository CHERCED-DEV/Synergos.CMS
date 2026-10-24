using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La entrada de <c>tienda.compra</c> en la puerta (004-tienda-compra): la del <c>appsettings.json</c> COMITEADO,
/// por el validador real de la puerta y contra la tabla que sale de los contratos incrustados.
/// </summary>
/// <remarks>
/// Hasta esta prueba, un campo de negocio mal escrito en <c>Synergos:Puerta:Flujos</c> sólo se veía al
/// arrancar el CMS: ninguna suite pasaba el fichero por el validador (medido con un mutante, «Carrer»).
/// </remarks>
public sealed class PuertaDeLaTiendaTests
{
    private const string Flujo = "tienda.compra";

    private static PuertaSettings LaComiteada()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "Synergos.CMS.sln"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);

        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(raiz!.FullName, "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build();
        return config.GetSection(PuertaSettings.Seccion).Get<PuertaSettings>() ?? new PuertaSettings();
    }

    /// <summary>
    /// Las secciones que el CMS registra, leídas de sus composers: el <c>appsettings.json</c> comiteado abre los flujos de
    /// TODAS las líneas, y una lista escrita aquí se rompía cada vez que una abría uno con su propia sección.
    /// </summary>
    private static SeccionesDelSitio Secciones() => SeccionesDeNegocioDelArbol.Todas();

    [Fact]
    public void La_puerta_del_appsettings_comiteado_pasa_el_validador()
    {
        var ajustes = LaComiteada();
        Assert.True(ajustes.Flujos.ContainsKey(Flujo), $"appsettings.json no abre {Flujo} en la puerta.");

        var resultado = new ValidadorDeLaPuerta(TablaDeLaPuerta.Incrustada(), Secciones()).Validate(null, ajustes);

        Assert.True(resultado.Succeeded, string.Join(" | ", resultado.Failures ?? []));
    }

    [Fact]
    public void La_compra_de_la_tienda_es_de_miembros_con_la_transportadora_del_negocio_y_el_enlace_a_la_tienda()
    {
        var entrada = LaComiteada().Flujos[Flujo];

        Assert.Equal(AccesoDeLaPuerta.Miembro, entrada.Acceso);
        Assert.Equal("tienda.comprador", entrada.SujetoKind);
        Assert.Equal("Storefront", entrada.Negocio!.Seccion);
        Assert.Equal(["Carrier"], entrada.Negocio.Campos);
        Assert.Equal("/tienda/?compra={id}", entrada.Aviso!.Ruta);
    }
}
