using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Un contexto de mensajes que reconoce los hilos de su vertical por el prefijo de su contexto (012-blogs-social, I4):
/// el prefijo solo, o el prefijo, dos puntos y lo demás (<c>dm</c>, <c>tienda:pedido:ord_1</c>). Era la regla de la
/// tienda; lo de cada vertical es el asunto y cómo nombra a quien participa.
/// </summary>
/// <remarks>
/// Por defecto, el asunto de un hilo no depende de con quién es, y quien participa sale tal como lo guarda la
/// mensajería: es lo que hacía la tienda. Un vertical cuyos participantes son miembros los nombra por su cara pública
/// (<see cref="ContextoDeMensajesDeBlogs"/>).
/// </remarks>
public abstract class ContextoDeMensajesPorPrefijo : IContextoDeMensajes
{
    private readonly string _prefijo;

    /// <summary>Los hilos de <paramref name="vertical"/> son los de contexto <paramref name="prefijo"/>.</summary>
    protected ContextoDeMensajesPorPrefijo(string vertical, string prefijo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vertical);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefijo);
        Vertical = vertical.Trim().ToLowerInvariant();
        _prefijo = prefijo.Trim().TrimEnd(':');
    }

    /// <inheritdoc />
    public string Vertical { get; }

    /// <inheritdoc />
    public virtual bool EsDelVertical(string contexto)
        => !string.IsNullOrWhiteSpace(contexto)
            && (string.Equals(contexto, _prefijo, StringComparison.OrdinalIgnoreCase)
                || contexto.StartsWith(_prefijo + ":", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public abstract Task<string> AsuntoAsync(string contexto, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task<string> AsuntoAsync(
        string contexto,
        string contraparte,
        CancellationToken cancellationToken = default)
        => AsuntoAsync(contexto, cancellationToken);

    /// <inheritdoc />
    public virtual Task<string> ParticipanteAsync(string participante, CancellationToken cancellationToken = default)
        => Task.FromResult(participante);
}
