using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Puerta;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La configuración de negocio de la tienda (ADR 0137) en lo que suma la compra por la puerta
/// (004-tienda-compra): la transportadora que pone la puerta y el camino de compra que declara el CMS.
/// </summary>
/// <remarks>
/// El camino sigue la regla de cualquier flujo (<see cref="CaminoDeLosFlujos"/>, núcleo N3): el override del
/// negocio si lo hay; si no, el del flujo en la puerta; si no, la puerta cuando abre <c>tienda.compra</c> y el
/// orquestador tiene destino; si no, la directa. Y el país de envío es del despliegue, por sitio.
/// </remarks>
public sealed class NegocioDeStorefrontTests
{
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");

    [Fact]
    public void Sin_sitio_rigen_los_valores_base_y_la_transportadora_es_la_de_la_ruta_directa()
    {
        Assert.Equal(new NegocioDeStorefront("/api/shop", "default", null), new StorefrontFeatureSettings().Para(null));
    }

    [Fact]
    public void Un_sitio_cambia_la_transportadora_y_hereda_lo_demas()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:Carrier"] = "servientrega" });

        Assert.Equal(new NegocioDeStorefront("/api/shop", "servientrega", null), seccion.Para(Bogota));
        Assert.Equal("default", seccion.Para(null).Carrier);
    }

    [Theory]
    [InlineData("Carrier", " ", "Carrier")]
    [InlineData("PurchasePath", "directo", "PurchasePath")]
    [InlineData("Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:PurchasePath", "orquestador", "PurchasePath")]
    [InlineData("ShippingCountry", "COL", "ShippingCountry")]
    [InlineData("Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:ShippingCountry", "c1", "ShippingCountry")]
    public void Una_clave_mal_escrita_no_deja_arrancar(string clave, string valor, string seDice)
    {
        using var sp = Opciones(new() { [clave] = valor });

        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(seDice, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_seccion_bien_escrita_arranca()
    {
        using var sp = Opciones(new()
        {
            ["Carrier"] = "servientrega",
            ["PurchasePath"] = "puerta",
            [$"Sitios:{Bogota}:PurchasePath"] = "directa",
            [$"Sitios:{Bogota}:ShippingCountry"] = " mx ",
        });

        sp.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void El_pais_de_envio_es_CO_si_nadie_dice_otro_y_un_sitio_lo_cambia_normalizado()
    {
        var seccion = Seccion(new() { [$"Sitios:{Bogota}:ShippingCountry"] = " mx " });

        Assert.Equal("CO", seccion.Para(null).ShippingCountry);
        Assert.Equal("MX", seccion.Para(Bogota).ShippingCountry);
    }

    // ── El camino de compra: lo declara el CMS ───────────────────────────────────────────────

    [Fact]
    public void Sin_override_y_sin_puerta_la_tienda_compra_por_la_ruta_directa()
    {
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: false, conDestino: false)).PurchasePath);
    }

    [Fact]
    public void Con_la_puerta_abierta_para_la_compra_y_el_orquestador_con_destino_compra_por_la_puerta()
    {
        Assert.Equal(CaminoDeCompra.Puerta, Resuelto(Fijo(null), Puerta(abierto: true, conDestino: true)).PurchasePath);
    }

    /// <summary>El clon limpio abre <c>tienda.compra</c> sin <c>Synergos:Tienda:BaseUrl</c>: gana la directa.</summary>
    [Fact]
    public void Sin_orquestador_la_tienda_compra_por_la_ruta_directa_aunque_el_flujo_este_abierto()
    {
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: true, conDestino: false)).PurchasePath);
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: false, conDestino: true)).PurchasePath);
    }

    [Theory]
    [InlineData(CaminoDeCompra.Directa, true)]
    [InlineData(CaminoDeCompra.Puerta, false)]
    public void El_camino_declarado_en_el_negocio_gana_a_la_regla(string declarado, bool puertaAbierta)
    {
        Assert.Equal(declarado, Resuelto(Fijo(declarado), Puerta(puertaAbierta, puertaAbierta)).PurchasePath);
    }

    /// <summary>Desde N3 el flujo también declara su camino en la puerta; el del negocio del sitio gana.</summary>
    [Fact]
    public void El_camino_que_declara_el_flujo_en_la_puerta_va_antes_que_la_regla_y_despues_del_sitio()
    {
        Assert.Equal(CaminoDeCompra.Directa, Resuelto(Fijo(null), Puerta(abierto: true, conDestino: true, caminoDelFlujo: "directa")).PurchasePath);
        Assert.Equal(CaminoDeCompra.Puerta, Resuelto(Fijo("puerta"), Puerta(abierto: true, conDestino: true, caminoDelFlujo: "directa")).PurchasePath);
    }

    [Fact]
    public void El_resolver_lleva_el_pais_de_envio_del_sitio()
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeStorefront>>();
        negocio.Actual().Returns(new NegocioDeStorefront("/api/shop", "default", null, "MX"));

        Assert.Equal("MX", Resuelto(negocio, Puerta(abierto: true, conDestino: true)).ShippingCountry);
    }

    [Fact]
    public void El_resolver_copia_lo_del_editor_y_la_configuracion_del_sitio()
    {
        var props = Resuelto(Fijo(null), Puerta(abierto: true, conDestino: true));

        Assert.Equal(new StorefrontProps("Tienda", null, "/api/shop", CaminoDeCompra.Puerta, "CO"), props);
    }

    private static StorefrontFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<StorefrontFeatureSettings>()!;

    private static ServiceProvider Opciones(Dictionary<string, string?> claves)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(claves.ToDictionary(c => $"{StorefrontFeatureSettings.Seccion}:{c.Key}", c => c.Value))
            .Build();
        var sinUmbraco = Substitute.For<IUmbracoContextAccessor>();
        sinUmbraco.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(false);

        return new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddLogging()
            .AddSingleton(new SitioDeLaPeticion(sinUmbraco))
            .AddSeccionDeNegocio<StorefrontFeatureSettings, NegocioDeStorefront>(config.GetSection(StorefrontFeatureSettings.Seccion))
            .BuildServiceProvider();
    }

    private static INegocioDelSitio<NegocioDeStorefront> Fijo(string? camino)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeStorefront>>();
        negocio.Actual().Returns(new NegocioDeStorefront("/api/shop", "default", camino));
        return negocio;
    }

    /// <summary>
    /// La puerta de verdad, con los contratos incrustados: <paramref name="abierto"/> si
    /// <c>Synergos:Puerta:Flujos</c> trae <c>tienda.compra</c>, y <paramref name="conDestino"/> si
    /// <c>Synergos:Tienda:BaseUrl</c> le da destino al orquestador.
    /// </summary>
    /// <param name="caminoDelFlujo">Lo que declara <c>Synergos:Puerta:Flujos:tienda.compra:Camino</c>, si algo
    /// (sólo con el flujo abierto: declararlo en la puerta ya lo abre).</param>
    private static ICaminoDeLosFlujos Puerta(bool abierto, bool conDestino, string? caminoDelFlujo = null)
    {
        var ajustes = new PuertaSettings();
        if (abierto)
        {
            ajustes.Flujos[StorefrontResolutor.FlujoDeLaCompra] = new FlujoDeLaPuertaSettings
            {
                Acceso = AccesoDeLaPuerta.Miembro,
                Camino = caminoDelFlujo,
            };
        }

        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(ajustes);
        var puerta = new PuertaDeLosFlujos(TablaDeLaPuerta.Incrustada(), monitor, new DestinosDeLaPuerta(conDestino ? ["Tienda"] : []));
        return new CaminoDeLosFlujos(puerta, monitor, NullLogger<CaminoDeLosFlujos>.Instance);
    }

    private static StorefrontProps Resuelto(INegocioDelSitio<NegocioDeStorefront> negocio, ICaminoDeLosFlujos caminos)
        => new StorefrontResolutor(ElementoFalso.Fallback, negocio, caminos, NullLogger<StorefrontResolutor>.Instance)
            .Resolver(ElementoFalso.Con(("heading", "Tienda"))).Props;
}
