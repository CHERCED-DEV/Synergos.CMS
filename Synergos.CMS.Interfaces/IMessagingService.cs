namespace Synergos.CMS.Interfaces;

/// <summary>
/// Hilos de mensajes 1:1 sobre un contexto — seam GENÉRICO (plan doc 21 §1.4
/// P7, v1): comprador↔vendedor post-venta (Tienda), huésped↔host (Booking),
/// interesado↔agente (Propiedades), DM (Blogs), paciente↔clínico In Basket
/// (Healthcare) son el MISMO contrato: hilo con contexto + 2 participantes +
/// mensajes ordenados. v1 simple a propósito: sin typing, sin grupos, sin
/// read-receipts — SH-7 v2/v3 agregan encima, no en paralelo.
/// </summary>
/// <remarks>
/// Seam stub-first (ADR 0075): el default <c>StubMessagingService</c>
/// (Application, lógica pura — ADR 0002) guarda los hilos en memoria del
/// proceso; un adapter real delega a DB/chat backend sin tocar los
/// consumidores. <c>contextRef</c> es la referencia opaca a la entidad que
/// motiva el hilo (orderRef, listingId, bookingRef…) — el seam no la
/// interpreta. Los participantes son identificadores estables (email del
/// Member, igual que <see cref="IShopOrderService"/>).
/// <see cref="StartThreadAsync"/> es idempotente por (contexto +
/// participantes): re-iniciar el mismo hilo agrega el mensaje al hilo
/// existente en vez de duplicarlo (el ThreadId es determinista).
/// </remarks>
public interface IMessagingService
{
    /// <summary>
    /// Inicia (o retoma) el hilo entre <paramref name="from"/> y
    /// <paramref name="to"/> sobre <paramref name="contextRef"/>, agregando
    /// <paramref name="body"/> como mensaje. Idempotente por (contexto +
    /// par de participantes): si el hilo ya existe, agrega el mensaje ahí —
    /// no crea un hilo paralelo. Lanza <see cref="ArgumentException"/> si
    /// falta algún dato o si from == to.
    /// </summary>
    Task<MessageThread> StartThreadAsync(
        string contextRef,
        string from,
        string to,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega una respuesta de <paramref name="from"/> al hilo. Lanza
    /// <see cref="ArgumentException"/> si el hilo no existe, el cuerpo viene
    /// vacío o <paramref name="from"/> no es participante del hilo.
    /// </summary>
    Task<MessageThread> ReplyAsync(
        string threadId,
        string from,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>Devuelve el hilo completo (mensajes en orden cronológico) o <c>null</c> si no existe.</summary>
    Task<MessageThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la bandeja del participante: un resumen por hilo en el que
    /// participa, del más recientemente activo al más antiguo. Lista vacía si
    /// no tiene hilos.
    /// </summary>
    Task<IReadOnlyList<MessageThreadSummary>> GetInboxAsync(
        string participant,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Un hilo de conversación 1:1: contexto que lo motiva + los 2 participantes
/// + los mensajes en orden cronológico.
/// </summary>
public sealed record MessageThread(
    string ThreadId,
    string ContextRef,
    IReadOnlyList<string> Participants,
    IReadOnlyList<ThreadMessage> Messages,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastMessageAt);

/// <summary>Un mensaje dentro de un hilo: quién lo mandó, qué dice, cuándo.</summary>
public sealed record ThreadMessage(
    string MessageId,
    string From,
    string Body,
    DateTimeOffset SentAt);

/// <summary>
/// Resumen de un hilo para la bandeja (inbox): contexto + participantes +
/// preview del último mensaje + actividad, sin cargar el hilo entero.
/// </summary>
public sealed record MessageThreadSummary(
    string ThreadId,
    string ContextRef,
    IReadOnlyList<string> Participants,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    int MessageCount);
