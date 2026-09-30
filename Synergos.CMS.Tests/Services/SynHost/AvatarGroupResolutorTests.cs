using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="AvatarGroupResolutor"/>: el TEXTO <c>avatarsJson</c> (<c>url</c>/<c>name</c>/
/// <c>role</c>) llega como la LISTA <c>avatars</c> (<c>name</c>/<c>src</c>) y el tope como número —la
/// vista mandaba el texto y el grupo decía «No hay integrantes» (D1)—.
/// </summary>
public sealed class AvatarGroupResolutorTests
{
    private const string Integrantes =
        """[{"url":"/media/ana.jpg","name":"Ana Gómez","role":"CEO"},{"name":"Luis Pardo"},{"url":"/media/sin-nombre.jpg"}]""";

    private readonly ILogger<AvatarGroupResolutor> _log = Substitute.For<ILogger<AvatarGroupResolutor>>();

    private AvatarGroupResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Los_integrantes_viajan_como_lista_con_la_foto_como_src()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("avatarsJson", Integrantes),
            ("maxVisible", "4"),
            ("ariaLabel", "Equipo directivo"))).Props);

        Assert.Equal(new[] { "avatars", "maxVisible", "label" }, cable.Keys);
        var avatars = (JsonElement)cable["avatars"]!;
        Assert.Equal(3, avatars.GetArrayLength());
        Assert.Equal("/media/ana.jpg", avatars[0].GetProperty("src").GetString());
        Assert.Equal("Ana Gómez", avatars[0].GetProperty("name").GetString());
        Assert.False(avatars[0].TryGetProperty("url", out _));
        Assert.False(avatars[0].TryGetProperty("role", out _));
        Assert.False(avatars[1].TryGetProperty("src", out _));
        Assert.Equal(4, ((JsonElement)cable["maxVisible"]!).GetInt32());
    }

    [Fact]
    public void Un_integrante_sin_nombre_ni_foto_no_viaja_y_se_anota()
    {
        var avatars = Resolutor().Resolver(ElementoFalso.Con(
            ("avatarsJson", """[{"role":"CTO"},{"name":"Ana"}]"""))).Props.Avatars;

        Assert.Equal(new[] { new AvatarGroupMember("Ana") }, avatars);
        Assert.Equal(1, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("avatarsJson", Integrantes));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Avatars, Resolutor().Resolver(elemento).Props.Avatars);
    }
}
