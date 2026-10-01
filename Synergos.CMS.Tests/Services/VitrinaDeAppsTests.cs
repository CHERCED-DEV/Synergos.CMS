using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La vitrina del hub enlaza <c>/synergos/apps/&lt;slug&gt;</c> para cada vertical y arma la página
/// de cada recorrido desde otra tabla. Hoteles estaba en la primera y no en la segunda: dos páginas
/// lo enlazaban a un 404 (Synergos.CMS#187).
/// </summary>
public sealed class VitrinaDeAppsTests
{
    [Fact]
    public void Cada_vertical_que_la_vitrina_enlaza_tiene_su_recorrido_y_ningun_recorrido_sobra()
    {
        var enlaza = DevContentFiller.SlugsQueLaVitrinaEnlaza;
        var recorre = DevContentFiller.SlugsQueLaVitrinaRecorre;

        // Dos conjuntos vacíos son iguales: sin este piso, un descubrimiento roto pasaría en verde.
        Assert.NotEmpty(enlaza);
        Assert.Equal(
            enlaza.Order(StringComparer.Ordinal),
            recorre.Order(StringComparer.Ordinal));
    }
}
