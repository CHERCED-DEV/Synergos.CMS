using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// En qué hilo de comentarios (<see cref="ICommentReader"/> y <see cref="ICommentWriter"/>, que se indexan por nodo)
/// vive la conversación de una publicación de la red.
/// </summary>
/// <remarks>
/// <para><b>Un post que alguien autoró en el CMS tiene UN hilo: el de su página</b> (012-blogs-social, I0, P4 A).
/// Antes la app usaba siempre un nodo derivado del id del item, así que el mismo artículo tenía dos conversaciones,
/// la de su página y la de la app, y ninguna veía a la otra.</para>
///
/// <para><b>Lo que se publicó desde la app no tiene nodo</b>, y se le deriva uno estable de su id (FNV-1a de 32 bits,
/// forzado a positivo) para reusar el almacén de comentarios sin schema nuevo: es lo que hacía
/// <c>BlogsController</c>, ahora en un solo sitio porque leen y escriben dos controladores.</para>
/// </remarks>
internal static class HiloDeUnaPublicacion
{
    /// <summary>El nodo del hilo de <paramref name="item"/>.</summary>
    public static int Nodo(ContentStreamItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.CmsNodeId ?? NodoDerivado(item.Id);
    }

    /// <summary>El nodo derivado del id de un item publicado desde la app.</summary>
    internal static int NodoDerivado(string postId)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var ch in postId)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }
        return (int)(hash & 0x7FFFFFFF);
    }
}
