using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Los perfiles de la red, con los autores del CMS (012-blogs-social, vuelta de valor): quien firma los artículos tiene
/// perfil, con su nombre, su avatar, su rol como bio y sus artículos. Antes, abrir el de «Equipo SynergosLabs» decía «No
/// pudimos cargar el perfil».
/// </summary>
/// <remarks>
/// <para>Es un decorador, como <see cref="CatalogContentStream"/>: lo que la proyección de siempre conoce (la semilla, los
/// miembros) gana. Un autor del CMS sale de las páginas de autor que firman artículos publicados, así que uno sin
/// artículos no tiene perfil.</para>
/// <para>El perfil es <see cref="SocialProfile.Editorial"/>: se le sigue, pero no recibe mensajes directos.</para>
/// </remarks>
public sealed class PerfilesConLosAutoresDelCms : ISocialProfileProjection
{
    private readonly ISocialProfileProjection _interna;
    private readonly ICatalogSource<AuthoredPost> _articulos;

    /// <summary>Sobre la proyección de siempre y los artículos que el editor publicó.</summary>
    public PerfilesConLosAutoresDelCms(ISocialProfileProjection interna, ICatalogSource<AuthoredPost> articulos)
    {
        _interna = interna ?? throw new ArgumentNullException(nameof(interna));
        _articulos = articulos ?? throw new ArgumentNullException(nameof(articulos));
    }

    /// <inheritdoc />
    public async Task<SocialProfile?> GetByHandleAsync(string handle, CancellationToken cancellationToken = default)
        => await _interna.GetByHandleAsync(handle, cancellationToken).ConfigureAwait(false)
            ?? await DelCmsAsync(a => Igual(a.Handle, handle), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<SocialProfile?> GetByActorIdAsync(string actorId, CancellationToken cancellationToken = default)
        => await _interna.GetByActorIdAsync(actorId, cancellationToken).ConfigureAwait(false)
            ?? await DelCmsAsync(a => Igual(a.ActorId, actorId), cancellationToken).ConfigureAwait(false);

    private async Task<SocialProfile?> DelCmsAsync(Func<AuthoredPostAuthor, bool> es, CancellationToken cancellationToken)
    {
        var suyos = (await _articulos.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(p => es(p.Author))
            .ToList();
        if (suyos.Count == 0)
        {
            return null;
        }

        var autor = suyos[0].Author;
        // Desde cuándo escribe en la red: su primer artículo con fecha.
        var desde = suyos.Select(p => p.PublishedUtc).Where(f => f is not null).Min() ?? DateTime.MinValue;
        return new SocialProfile(
            ActorId: autor.ActorId,
            Handle: autor.Handle,
            DisplayName: autor.DisplayName,
            Bio: autor.Role,
            AvatarUrl: autor.AvatarUrl,
            BannerUrl: null,
            Verified: false,
            JoinedUtc: DateTime.SpecifyKind(desde, DateTimeKind.Utc),
            Editorial: true);
    }

    private static bool Igual(string? a, string? b)
        => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
