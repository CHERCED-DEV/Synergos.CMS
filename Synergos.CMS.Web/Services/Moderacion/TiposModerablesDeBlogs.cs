using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Moderacion;

namespace Synergos.CMS.Web.Services.Moderacion;

/// <summary>
/// Una publicación de la red como objetivo de moderación (012-blogs-social, I5): se denuncia lo que se ve, la cola la
/// resume con su autor por su cara pública, y retirarla la saca del stream para todos (<see cref="StreamSinRetirados"/>).
/// </summary>
public sealed class PublicacionesModerables : ITipoModerable
{
    private readonly IContentStream _stream;
    private readonly IRegistroDeRetirados _retirados;
    private readonly IIdentidadPublica _identidad;

    /// <summary>Sobre el stream de la red (ya sin lo retirado), el registro de lo retirado y la cara pública.</summary>
    public PublicacionesModerables(IContentStream stream, IRegistroDeRetirados retirados, IIdentidadPublica identidad)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _retirados = retirados ?? throw new ArgumentNullException(nameof(retirados));
        _identidad = identidad ?? throw new ArgumentNullException(nameof(identidad));
    }

    /// <inheritdoc />
    public string Tipo => StreamSinRetirados.TipoDeLaPublicacion;

    /// <inheritdoc />
    public async Task<ContenidoDenunciable?> LeerAsync(
        string id,
        string quienDenuncia,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var publicacion = await _stream.GetItemAsync(id.Trim(), cancellationToken).ConfigureAwait(false);
        if (publicacion is null)
        {
            return null;
        }

        var autor = CatalogoDeBlogs.Autor(publicacion.Author, _identidad).Nombre;
        var resumen = PublicacionesDeLaRed.Resumen(publicacion.CmsTitle ?? publicacion.Body);
        return new ContenidoDenunciable($"{autor}: {resumen}", publicacion.CmsUrl);
    }

    /// <inheritdoc />
    public Task RetirarAsync(string id, CancellationToken cancellationToken = default)
        => _retirados.RetirarAsync(new ObjetivoModerable(Tipo, id), cancellationToken);
}

/// <summary>
/// Un comentario de una publicación de la red como objetivo de moderación (012-blogs-social, I5). Su id es el de la
/// publicación y el del comentario, <c>{publicación}:{comentario}</c>: el hilo es el de la publicación
/// (<see cref="HiloDeUnaPublicacion"/>), y el cliente no tiene por qué saber en qué nodo vive. Retirarlo es la moderación
/// de comentarios de hoy, extendida: deja de mostrarse y sigue guardado.
/// </summary>
public sealed class ComentariosModerables : ITipoModerable
{
    /// <summary>El tipo moderable de un comentario de la red.</summary>
    public const string TipoDelComentario = "blogs.comentario";

    private readonly IContentStream _stream;
    private readonly ICommentReader _comentarios;
    private readonly ICommentModeration _moderacionDeComentarios;

    /// <summary>Sobre el stream de la red y los comentarios.</summary>
    public ComentariosModerables(
        IContentStream stream,
        ICommentReader comentarios,
        ICommentModeration moderacionDeComentarios)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _comentarios = comentarios ?? throw new ArgumentNullException(nameof(comentarios));
        _moderacionDeComentarios = moderacionDeComentarios
            ?? throw new ArgumentNullException(nameof(moderacionDeComentarios));
    }

    /// <inheritdoc />
    public string Tipo => TipoDelComentario;

    /// <inheritdoc />
    public async Task<ContenidoDenunciable?> LeerAsync(
        string id,
        string quienDenuncia,
        CancellationToken cancellationToken = default)
    {
        var nodo = await NodoAsync(id, cancellationToken).ConfigureAwait(false);
        if (nodo is not { } hilo)
        {
            return null;
        }

        var comentario = _comentarios.GetApprovedForNode(hilo.Nodo)
            .FirstOrDefault(c => string.Equals(c.Id, hilo.Comentario, StringComparison.Ordinal));
        return comentario is null
            ? null
            : new ContenidoDenunciable($"{comentario.AuthorName}: {PublicacionesDeLaRed.Resumen(comentario.Body)}", null);
    }

    /// <inheritdoc />
    public async Task RetirarAsync(string id, CancellationToken cancellationToken = default)
    {
        if (await NodoAsync(id, cancellationToken).ConfigureAwait(false) is { } hilo)
        {
            await _moderacionDeComentarios.RetirarAsync(hilo.Nodo, hilo.Comentario, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(int Nodo, string Comentario)?> NodoAsync(string id, CancellationToken cancellationToken)
    {
        var partes = (id ?? string.Empty).Split(':', 2, StringSplitOptions.TrimEntries);
        if (partes.Length != 2 || partes[0].Length == 0 || partes[1].Length == 0)
        {
            return null;
        }

        var publicacion = await _stream.GetItemAsync(partes[0], cancellationToken).ConfigureAwait(false);
        return publicacion is null ? null : (HiloDeUnaPublicacion.Nodo(publicacion), partes[1]);
    }
}
