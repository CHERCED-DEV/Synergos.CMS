using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.PropertyEditors;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="BadgeGroupResolutor"/>: el TEXTO <c>badgesJson</c> (<c>label</c>/<c>color</c>/
/// <c>iconKey</c>) llega como la LISTA <c>badges</c> (<c>label</c>/<c>tone</c>/<c>icon</c>) que el
/// elemento lee — con el texto, el grupo hidrataba vacío (D1). El icono viaja sólo si está en el set
/// del sitio (#192, caso 3).
/// </summary>
public sealed class BadgeGroupResolutorTests
{
    private const string Insignias =
        """[{"label":"Envío gratis","color":"Success","iconKey":"truck"},{"label":"Nuevo","tone":"brand"}]""";

    private readonly ILogger<BadgeGroupResolutor> _log = Substitute.For<ILogger<BadgeGroupResolutor>>();

    /// <summary>La caché de configuraciones de Umbraco, con el set de iconos que se le dé.</summary>
    private readonly IDataTypeConfigurationCache _configuraciones = Substitute.For<IDataTypeConfigurationCache>();

    public BadgeGroupResolutorTests() => ConElSet("truck", "star");

    private void ConElSet(params string[] nombres)
    {
        var configuracion = new ValueListConfiguration();
        configuracion.Items.AddRange(nombres.Select((n, i) => new ValueListConfiguration.ValueListItem { Id = i + 1, Value = n }));
        _configuraciones.GetConfigurationAs<ValueListConfiguration>(IconosDelSistema.DataType).Returns(configuracion);
    }

    private BadgeGroupResolutor Resolutor() => new(ElementoFalso.Fallback, _log, new IconosDelSistema(_configuraciones));

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_insignias_viajan_como_lista_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("badgesJson", Insignias),
            ("layout", "stack"))).Props);

        Assert.Equal(new[] { "badges", "layout" }, cable.Keys);
        var badges = (JsonElement)cable["badges"]!;
        Assert.Equal(2, badges.GetArrayLength());
        Assert.Equal("Envío gratis", badges[0].GetProperty("label").GetString());
        Assert.Equal("success", badges[0].GetProperty("tone").GetString());
        Assert.False(badges[0].TryGetProperty("iconKey", out _));
        Assert.Equal("truck", badges[0].GetProperty("icon").GetString());
        Assert.Equal("brand", badges[1].GetProperty("tone").GetString());
        Assert.Equal("stack", cable["layout"]?.ToString());
    }

    /// <summary>
    /// <c>cluster</c> —la fila que salta de línea— es lo que el elemento llama <c>wrap</c> (#181);
    /// lo que no tiene equivalente viaja como está y el elemento decide.
    /// </summary>
    [Theory]
    [InlineData("cluster", "wrap")]
    [InlineData("Cluster", "wrap")]
    [InlineData("stack", "stack")]
    [InlineData("grid", "grid")]
    public void La_disposicion_viaja_con_el_nombre_que_le_da_el_elemento(string delEditor, string viaja)
    {
        Assert.Equal(viaja, Resolutor().Resolver(ElementoFalso.Con(("layout", delEditor))).Props.Layout);
    }

    [Fact]
    public void Una_insignia_sin_texto_no_viaja_y_se_anota()
    {
        var badges = Resolutor().Resolver(ElementoFalso.Con(
            ("badgesJson", """[{"color":"brand"},{"label":"Oferta"}]"""))).Props.Badges;

        Assert.Equal(new[] { new BadgeGroupItem("Oferta") }, badges);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact] // happy: el icono del set viaja como «icon», en minúsculas; «iconKey» se sigue leyendo.
    public void El_icono_del_set_viaja_con_el_nombre_que_lee_el_elemento()
    {
        var badges = Resolutor().Resolver(ElementoFalso.Con(
            ("badgesJson", """[{"label":"Envío gratis","icon":"Truck"},{"label":"Top","iconKey":"star"},{"label":"Nuevo"}]"""))).Props.Badges;

        Assert.Equal(
            new[] { new BadgeGroupItem("Envío gratis", Icon: "truck"), new BadgeGroupItem("Top", Icon: "star"), new BadgeGroupItem("Nuevo") },
            badges);
        Assert.DoesNotContain(_log.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(ILogger.Log));
    }

    [Fact] // filter: un nombre fuera del set no viaja y se anota; la insignia sí viaja.
    public void Un_icono_fuera_del_set_no_viaja_y_se_anota()
    {
        var badges = Resolutor().Resolver(ElementoFalso.Con(
            ("badgesJson", """[{"label":"Nuevo","icon":"sparkles"}]"""))).Props.Badges;

        Assert.Equal(new[] { new BadgeGroupItem("Nuevo") }, badges);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact] // empty: sin set legible no se sabe qué falta; el nombre viaja y el elemento lo filtra.
    public void Sin_el_set_el_icono_viaja_como_esta()
    {
        _configuraciones.GetConfigurationAs<ValueListConfiguration>(IconosDelSistema.DataType).Returns((ValueListConfiguration?)null);

        var badges = Resolutor().Resolver(ElementoFalso.Con(
            ("badgesJson", """[{"label":"Nuevo","icon":"Sparkles"}]"""))).Props.Badges;

        Assert.Equal(new[] { new BadgeGroupItem("Nuevo", Icon: "sparkles") }, badges);
    }

    /// <summary>
    /// El set es el del desplegable de icono: la Key que lee <see cref="IconosDelSistema"/> es la
    /// de <c>DTSelectIcono</c> en uSync. Si el DataType se regenerara con otra, el set saldría
    /// vacío en silencio —sin set, todo icono pasa—.
    /// </summary>
    [Fact]
    public void El_set_es_el_del_desplegable_de_icono()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);

        var dataType = System.Xml.Linq.XDocument.Load(Path.Combine(
            dir!.FullName, "Synergos.CMS.Web", "uSync", "v9", "DataTypes", "DTSelectIcono.config")).Root!;

        Assert.Equal("DTSelectIcono", (string?)dataType.Attribute("Alias"));
        Assert.Equal(IconosDelSistema.DataType, Guid.Parse((string)dataType.Attribute("Key")!));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("badgesJson", Insignias), ("layout", "inline"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Badges, Resolutor().Resolver(elemento).Props.Badges);
        Assert.Equal(Resolutor().Resolver(elemento).Props.Layout, Resolutor().Resolver(elemento).Props.Layout);
    }
}
