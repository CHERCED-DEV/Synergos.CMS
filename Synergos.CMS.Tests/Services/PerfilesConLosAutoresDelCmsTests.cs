using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Los perfiles de la red con los autores del CMS (012-blogs-social, vuelta de valor): quien firma artículos publicados
/// tiene perfil editorial, por su alias y por su id; lo que conoce la proyección de siempre gana.
/// </summary>
public sealed class PerfilesConLosAutoresDelCmsTests
{
    private sealed class Articulos(params AuthoredPost[] posts) : ICatalogSource<AuthoredPost>
    {
        public Task<IReadOnlyList<AuthoredPost>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AuthoredPost>>(posts);
    }

    private static AuthoredPost Articulo(string slug, DateTime? fecha = null) => new(
        Id: slug,
        Title: slug,
        Excerpt: "El resumen de " + slug,
        Url: "/blogs/" + slug + "/",
        HeroImageUrl: null,
        HeroImageAlt: null,
        PublishedUtc: fecha,
        Kind: "article",
        Author: new AuthoredPostAuthor(
            "autor-equipo-synergoslabs", "equipo-synergoslabs", "Equipo SynergosLabs", "/media/equipo.png", "Redacción"));

    [Fact]
    public async Task Quien_firma_los_articulos_tiene_perfil_editorial_por_su_alias_y_por_su_id()
    {
        var perfiles = new PerfilesConLosAutoresDelCms(new StubSocialProfileProjection(), new Articulos(
            Articulo("uno", new DateTime(2026, 6, 20, 0, 0, 0, DateTimeKind.Utc)),
            Articulo("dos", new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc))));

        var porAlias = await perfiles.GetByHandleAsync("Equipo-SynergosLabs");

        Assert.NotNull(porAlias);
        Assert.Equal(
            ("autor-equipo-synergoslabs", "Equipo SynergosLabs", "Redacción", "/media/equipo.png", true),
            (porAlias!.ActorId, porAlias.DisplayName, porAlias.Bio, porAlias.AvatarUrl, porAlias.Editorial));
        Assert.Equal(new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc), porAlias.JoinedUtc);
        Assert.Equal(porAlias, await perfiles.GetByActorIdAsync("autor-equipo-synergoslabs"));
        Assert.Null(await perfiles.GetByHandleAsync("nadie"));
    }

    [Fact]
    public async Task Lo_que_conoce_la_proyeccion_de_siempre_gana()
    {
        var deSiempre = new StubSocialProfileProjection();
        var mateo = await deSiempre.GetByHandleAsync("mateo.design");
        var perfiles = new PerfilesConLosAutoresDelCms(deSiempre, new Articulos(Articulo("uno")));

        Assert.NotNull(mateo);
        Assert.Equal(mateo, await perfiles.GetByHandleAsync("mateo.design"));
        Assert.False(mateo!.Editorial);
    }
}
