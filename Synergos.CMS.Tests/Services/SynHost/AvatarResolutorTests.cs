using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="AvatarResolutor"/>: la foto del editor llega como <c>src</c> —la vista la
/// mandaba como <c>avatarSrc</c> y el elemento pintaba el icono genérico (D1)— y su nombre
/// accesible como <c>alt</c>.
/// </summary>
public sealed class AvatarResolutorTests
{
    private readonly ILogger<AvatarResolutor> _log = Substitute.For<ILogger<AvatarResolutor>>();

    private AvatarResolutor Resolutor() => new(ElementoFalso.Fallback, ElementoFalso.Urls(), _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void La_foto_viaja_como_src_con_el_texto_alternativo_del_medio()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("avatarImage", ElementoFalso.Medio("/media/equipo/ana.jpg", "Ana Gómez")))).Props);

        Assert.Equal(new[] { "src", "alt" }, cable.Keys);
        Assert.Equal("/media/equipo/ana.jpg", cable["src"]!.ToString());
        Assert.Equal("Ana Gómez", cable["alt"]!.ToString());
    }

    [Fact]
    public void El_ariaLabel_del_bloque_gana_al_texto_alternativo_del_medio()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("avatarImage", ElementoFalso.Medio("/media/equipo/ana.jpg", "Ana Gómez")),
            ("ariaLabel", "Ana Gómez, directora de producto"))).Props;

        Assert.Equal(new AvatarProps("/media/equipo/ana.jpg", "Ana Gómez, directora de producto"), props);
    }

    [Fact]
    public void Un_medio_sin_fichero_no_manda_foto_y_se_anota()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("avatarImage", ElementoFalso.Medio("#", "Ana")))).Props;

        Assert.Null(props.Src);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("avatarImage", ElementoFalso.Medio("/media/a.jpg", "A")));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
