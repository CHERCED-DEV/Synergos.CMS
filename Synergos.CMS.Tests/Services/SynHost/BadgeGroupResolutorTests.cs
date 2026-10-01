using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="BadgeGroupResolutor"/>: el TEXTO <c>badgesJson</c> (<c>label</c>/<c>color</c>/
/// <c>iconKey</c>) llega como la LISTA <c>badges</c> (<c>label</c>/<c>tone</c>) que el elemento lee
/// — con el texto, el grupo hidrataba vacío (D1).
/// </summary>
public sealed class BadgeGroupResolutorTests
{
    private const string Insignias =
        """[{"label":"Envío gratis","color":"Success","iconKey":"truck"},{"label":"Nuevo","tone":"brand"}]""";

    private readonly ILogger<BadgeGroupResolutor> _log = Substitute.For<ILogger<BadgeGroupResolutor>>();

    private BadgeGroupResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

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

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("badgesJson", Insignias), ("layout", "inline"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Badges, Resolutor().Resolver(elemento).Props.Badges);
        Assert.Equal(Resolutor().Resolver(elemento).Props.Layout, Resolutor().Resolver(elemento).Props.Layout);
    }
}
