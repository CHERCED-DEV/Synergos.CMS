namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lectura PÚBLICA de comentarios: lo que se le muestra a cualquiera que visite un nodo.
/// </summary>
/// <remarks>
/// <para><b>Por qué esta cara está sola.</b> Es la única que consume la renderización del blog,
/// y es la única que nunca puede devolver un comentario sin aprobar. Un componente que solo
/// pinta el hilo no necesita —ni debería poder— aprobar, borrar ni leer la cola de moderación.
/// Separarlo hace que eso sea una propiedad del tipo y no una convención.</para>
///
/// <para>Es el mismo corte de <see cref="IMemberRosterReader"/> /
/// <see cref="IMemberRosterWriter"/>, y sale de medir quién usa qué (auditoría, backlog Medio
/// #5): de los cinco consumidores del repositorio de comentarios, <b>ninguno</b> usaba las tres
/// caras.</para>
/// </remarks>
public interface ICommentReader
{
    /// <summary>
    /// Devuelve los comentarios aprobados para el nodo, ordenados
    /// por <see cref="Comment.CreatedAtUtc"/> ascendente (más viejo
    /// primero — orden natural de hilo).
    /// </summary>
    IReadOnlyList<Comment> GetApprovedForNode(int nodeId);
}
