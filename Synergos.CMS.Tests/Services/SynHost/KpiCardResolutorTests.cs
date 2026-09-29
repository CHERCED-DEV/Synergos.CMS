using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="KpiCardResolutor"/>: los alias del ElementType (<c>kpiLabel</c>…) llegan al
/// elemento con los nombres que el elemento LEE (<c>label</c>…).
/// </summary>
/// <remarks>
/// Las claves se verifican SERIALIZANDO, no leyendo el record: la regresión que importa es D1 —la
/// vista mandaba <c>kpiLabel</c> y el elemento leía <c>label</c>— y esa sólo se ve en el cable.
/// </remarks>
public sealed class KpiCardResolutorTests
{
    private static KpiCardResolutor Resolutor(params (string, string)[] diccionario)
        => new(ElementoFalso.Fallback, ElementoFalso.Diccionario(diccionario));

    private static readonly (string, object?)[] Autorado =
    {
        ("kpiLabel", "Ventas del mes"),
        ("kpiValue", "1.234"),
        ("kpiTrend", "up"),
        ("kpiDelta", "+12 %"),
        ("kpiPeriod", "vs. agosto"),
    };

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave_ni_respaldo()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con());

        Assert.Equal(new KpiCardProps(null, null, null, null, null), resuelto.Props);
        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.RespaldoHtml);
    }

    [Fact]
    public void Lo_que_el_editor_escribio_viaja_con_los_nombres_que_lee_el_elemento()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(Autorado));

        var cable = SolicitudSynHost.Props(resuelto.Props);

        Assert.Equal(new[] { "label", "value", "trend", "deltaLabel", "period" }, cable.Keys);
        Assert.Equal("Ventas del mes", cable["label"]?.ToString());
        Assert.Equal("1.234", cable["value"]?.ToString());
        Assert.Equal("up", cable["trend"]?.ToString());
        Assert.Equal("+12 %", cable["deltaLabel"]?.ToString());
        Assert.Equal("vs. agosto", cable["period"]?.ToString());
        Assert.DoesNotContain(cable.Keys, k => k.StartsWith("kpi", StringComparison.Ordinal));
    }

    [Fact]
    public void El_respaldo_SSR_pinta_lo_mismo_que_viaja_con_la_frase_del_diccionario()
    {
        var resuelto = Resolutor(("Synhost.Kpi.Trend.Up", "Al alza")).Resolver(ElementoFalso.Con(Autorado));

        Assert.NotNull(resuelto.RespaldoHtml);
        Assert.Contains("data-syn-ssr-fallback=\"true\"", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("Ventas del mes", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("+12 %", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("Al alza", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("syn-ssr-kpi__trend--up", resuelto.RespaldoHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_tendencia_fuera_del_selector_y_un_texto_en_blanco_no_viajan()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(
            ("kpiLabel", "   "),
            ("kpiValue", " 95 % "),
            ("kpiTrend", "neutral")));

        Assert.Null(resuelto.Props.Label);
        Assert.Equal("95 %", resuelto.Props.Value);
        Assert.Null(resuelto.Props.Trend);
        Assert.Equal(new[] { "value" }, SolicitudSynHost.Props(resuelto.Props).Keys);
    }

    [Fact]
    public void Sin_la_clave_en_el_diccionario_el_respaldo_usa_su_frase_de_respaldo()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(("kpiValue", "7"), ("kpiTrend", "down")));

        Assert.Contains("Tendencia a la baja", resuelto.RespaldoHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var resolutor = Resolutor();
        var elemento = ElementoFalso.Con(Autorado);

        Assert.Equal(resolutor.Resolver(elemento), resolutor.Resolver(elemento));
    }
}
