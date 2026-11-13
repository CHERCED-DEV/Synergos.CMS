namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Qué hilos de <see cref="IMessagingService"/> son de un vertical y cómo se titulan (014-despiece-tienda, I4). La
/// mensajería ya era común; la bandeja trae TODOS los hilos del correo (los mensajes directos de Blogs, los radicados de
/// Gobierno), así que cada vertical declara los suyos por su contexto, como ya hacían Salud y Gobierno.
/// </summary>
public interface IContextoDeMensajes : ILecturaDeUnVertical
{
    /// <summary>Si el hilo con ese contexto es del vertical.</summary>
    bool EsDelVertical(string contexto);

    /// <summary>El asunto del hilo, leído de lo que lo motivó (un pedido, un producto).</summary>
    Task<string> AsuntoAsync(string contexto, CancellationToken cancellationToken = default);

    /// <summary>
    /// El asunto del hilo con <paramref name="contraparte"/> (tal como la guarda la mensajería): el de un mensaje
    /// directo es el nombre público de con quién es (012-blogs-social, I4). Quien no lo necesita da el de su contexto.
    /// </summary>
    Task<string> AsuntoAsync(string contexto, string contraparte, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cómo sale en la bandeja quien participa en un hilo del vertical (la contraparte y quien escribió cada mensaje).
    /// La mensajería guarda a un miembro por su correo, y un vertical donde los participantes son miembros lo nombra
    /// por su cara pública, nunca por el correo (012-blogs-social, I0 e I4).
    /// </summary>
    Task<string> ParticipanteAsync(string participante, CancellationToken cancellationToken = default);
}

/// <summary>Un mensaje del hilo. <see cref="Mio"/>: lo escribió quien lee.</summary>
public sealed record MensajeDelHilo(string Id, string De, bool Mio, string Texto, DateTimeOffset Fecha);

/// <summary>
/// Un hilo de la bandeja del vertical: su asunto, con quién es, el último mensaje y los mensajes en orden.
/// </summary>
public sealed record HiloDeLaBandeja(
    string Id,
    string Contexto,
    string Asunto,
    string Contraparte,
    string Ultimo,
    DateTimeOffset Fecha,
    int Cantidad,
    IReadOnlyList<MensajeDelHilo> Mensajes);

/// <summary>La bandeja de quien lee en un vertical, del hilo más reciente al más antiguo.</summary>
public sealed record BandejaDelVertical(IReadOnlyList<HiloDeLaBandeja> Hilos);
