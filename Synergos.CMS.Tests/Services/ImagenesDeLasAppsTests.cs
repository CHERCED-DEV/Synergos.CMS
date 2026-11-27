using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La tarjeta de una app pide la foto a su ancho, en WebP (D-016-Y2); la ficha de detalle conserva la grande.
/// </summary>
public sealed class ImagenesDeLasAppsTests
{
    private static RentalEquipment Equipo(string? cover) => new(
        "planta", "Planta eléctrica", "Energía", "5 kVA", "Ficha", cover, ["/media/a/planta-2.jpg"],
        Units: 2, DailyRate: 90_000m, Deposit: 300_000m, MinDays: 1, MaxDays: 30,
        Includes: [], Requirements: [], Rates: [], Specs: [], Currency: "COP");

    [Fact]
    public void La_tarjeta_del_equipo_pide_la_foto_a_su_ancho_y_la_ficha_la_grande()
    {
        Assert.Equal("/media/a/planta.jpg?width=640&format=webp&quality=80", EquipmentCardDto.From(Equipo("/media/a/planta.jpg")).CoverUrl);
        Assert.Equal("/media/a/planta.jpg", EquipmentDetailDto.From(Equipo("/media/a/planta.jpg")).CoverUrl);
    }

    [Fact]
    public void Un_equipo_sin_foto_sigue_sin_foto()
    {
        Assert.Null(EquipmentCardDto.From(Equipo(null)).CoverUrl);
    }
}
