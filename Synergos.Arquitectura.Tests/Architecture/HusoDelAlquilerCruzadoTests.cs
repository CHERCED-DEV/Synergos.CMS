using System.Text.Json;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El huso del alquiler tiene el MISMO valor por defecto en los dos árboles (línea 005, T020; research R7): el CMS
/// (<c>Synergos:Alquiler:TimeZone</c>) y el orquestador (<c>Alquiler:TimeZone</c> de su <c>appsettings.json</c>).
/// </summary>
/// <remarks>
/// Con dos valores distintos, el motor en proceso y el orquestador dirían cosas distintas de si el inquilino todavía
/// puede cancelar a las 23:30, según el interruptor. Se compara el TEXTO del orquestador, que no se referencia desde
/// aquí: los dos árboles sólo comparten nombres.
/// </remarks>
public sealed class HusoDelAlquilerCruzadoTests
{
    [Fact]
    public void El_CMS_y_el_orquestador_cuentan_hoy_en_el_mismo_huso_por_defecto()
    {
        using var ajustes = JsonDocument.Parse(File.ReadAllText(Proyectos.Dir("Synergos.Bff.Alquiler", "appsettings.json")));
        var delOrquestador = ajustes.RootElement.GetProperty("Alquiler").GetProperty("TimeZone").GetString();

        Assert.Equal(AlquilerSettings.HusoPorDefecto, delOrquestador);
        Assert.Equal(AlquilerSettings.HusoPorDefecto, new AlquilerSettings().TimeZone);
    }
}
