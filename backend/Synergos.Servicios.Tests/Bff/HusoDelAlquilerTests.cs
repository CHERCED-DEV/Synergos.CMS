using Synergos.Bff.Alquiler.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El huso del despliegue (línea 005, T019): qué día es hoy para la regla de cancelar, y el día de inicio de una
/// ventana que viaja a medianoche UTC.
/// </summary>
public sealed class HusoDelAlquilerTests
{
    private static readonly HusoDelAlquiler Bogota = HusoDelAlquiler.De(null);

    /// <summary>Del 5 al 12 de septiembre, como la manda el CMS: medianoche UTC.</summary>
    private static readonly TimeWindow DelCinco = TimeWindow.Of(
        new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void El_dia_de_inicio_es_la_fecha_UTC_de_la_ventana()
    {
        Assert.Equal(new DateOnly(2026, 9, 5), HusoDelAlquiler.DiaDeInicio(DelCinco));
    }

    [Theory]
    [InlineData(2026, 9, 4, 23, 30, false)]  // 23:30 en Bogotá del día anterior: 04:30 UTC del 5
    [InlineData(2026, 9, 5, 0, 10, true)]    // 00:10 en Bogotá del día de inicio
    [InlineData(2026, 9, 3, 12, 0, false)]
    public void Empieza_el_dia_de_inicio_en_el_huso_del_despliegue(int a, int m, int d, int h, int min, bool empezo)
    {
        var enBogota = new DateTimeOffset(a, m, d, h, min, 0, TimeSpan.FromHours(-5));

        Assert.Equal(empezo, Bogota.YaEmpezo(DelCinco, enBogota));
    }

    [Fact]
    public void Un_huso_que_no_existe_no_arranca_y_dice_cual()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HusoDelAlquiler.De("Tierra/Media"));

        Assert.Contains("Tierra/Media", ex.Message, StringComparison.Ordinal);
    }
}
