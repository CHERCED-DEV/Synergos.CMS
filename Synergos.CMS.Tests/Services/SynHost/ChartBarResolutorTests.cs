using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="ChartBarResolutor"/>: el TEXTO <c>dataJson</c> llega como la LISTA <c>data</c>
/// con <c>value</c> NUMÉRICO y el título como <c>title</c> —la vista mandaba el texto y
/// <c>chartTitle</c>, y el gráfico salía vacío (D1)—.
/// </summary>
public sealed class ChartBarResolutorTests
{
    private readonly ILogger<ChartBarResolutor> _log = Substitute.For<ILogger<ChartBarResolutor>>();

    private ChartBarResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Las_barras_viajan_como_lista_con_valores_numericos_y_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("chartTitle", "Ventas por mes"),
            ("dataJson", """[{"label":"Ene","value":120},{"label":"Feb","value":"1.234.567"},{"label":"Mar","value":"12,5"}]"""),
            ("orientation", "horizontal"))).Props);

        Assert.Equal(new[] { "title", "orientation", "data" }, cable.Keys);
        var data = (JsonElement)cable["data"]!;
        Assert.Equal(new[] { 120m, 1234567m, 12.5m }, data.EnumerateArray().Select(b => b.GetProperty("value").GetDecimal()));
        Assert.Equal(JsonValueKind.Number, data[1].GetProperty("value").ValueKind);
        Assert.Equal("Ventas por mes", cable["title"]!.ToString());
    }

    [Fact]
    public void Una_barra_ambigua_o_incompleta_no_viaja_y_se_anota_una_vez()
    {
        var data = Resolutor().Resolver(ElementoFalso.Con(
            ("dataJson", """[{"label":"Ene","value":"500.000"},{"value":3},{"label":"Mar"},{"label":"Abr","value":4}]"""))).Props.Data;

        Assert.Equal(new[] { new ChartBarEntry("Abr", 4) }, data);
        Assert.Equal(3, Anotados());
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("dataJson", """[{"label":"Ene","value":1}]"""));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Data, Resolutor().Resolver(elemento).Props.Data);
    }
}
