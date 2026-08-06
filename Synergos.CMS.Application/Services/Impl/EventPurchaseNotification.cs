using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El hecho «entradas confirmadas»: UN SOLO correo al COMPRADOR con TODAS las entradas.
/// </summary>
/// <remarks>
/// <para><b>Está afuera del motor de compra porque el comprador tiene que enterarse igual</b>,
/// haya comprado por el motor en proceso o contra el orquestador (HU #35, rebanada 2b).
/// Duplicarlo habría dejado que un camino avisara y el otro no, que es la clase de diferencia
/// que solo se descubre cuando alguien reclama que no le llegó nada.</para>
///
/// <para><b>Nunca uno por asistente.</b> Razón dura: el motor solo VALIDA el correo del
/// comprador —el primer asistente—; a los demás apenas les hace <c>Trim()</c>, así que notificar
/// por-asistente dispararía contra cadenas vacías. El comprador es la unidad original
/// (<c>AttendeeEmail</c>, no <c>HolderEmail</c>: transferir una entrada cambia el portador, no a
/// quién se le confirmó la compra).</para>
///
/// <para>Si el destinatario persistido no es usable NO se emite basura. El dispatcher filtra
/// inválidos, pero no le inventamos un placeholder.</para>
/// </remarks>
public static class EventPurchaseNotification
{
    /// <summary>
    /// Emite el aviso, si hay a quién. Best-effort: un correo caído JAMÁS tumba una compra ya
    /// pagada y persistida.
    /// </summary>
    /// <remarks>
    /// Los dos caminos de compra emiten por acá, y por eso existe: dejar que cada uno llamara al
    /// dispatcher por su cuenta es lo que permitiría que uno avisara y el otro no.
    /// </remarks>
    public static Task EmitAsync(
        ITransactionalNotifier? notifier,
        PersistedEventOrder order,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        var aviso = Build(order, occurredAt);
        return aviso is null
            ? Task.CompletedTask
            : NotificationEmission.SafeDispatchAsync(notifier, aviso, cancellationToken);
    }

    /// <summary>
    /// El aviso de la compra, o <c>null</c> si no hay a quién mandárselo.
    /// </summary>
    /// <remarks>
    /// La llave de deduplicación por defecto es <c>events.tickets.confirmed:{orderRef}</c> — el
    /// <c>orderRef</c> identifica el hecho, así que re-emitir es inofensivo: el libro del
    /// dispatcher deduplica. Eso es lo que rescata el caso en que la primera confirmación no
    /// llegó a notificar (avisos apagados entonces, destinatario inválido, etc.).
    /// </remarks>
    public static NotificationEvent? Build(PersistedEventOrder order, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(order);

        var buyer = order.Units.Count > 0 ? order.Units[0] : null;
        if (buyer is null
            || string.IsNullOrWhiteSpace(buyer.AttendeeEmail)
            || string.IsNullOrWhiteSpace(buyer.AttendeeName))
        {
            return null;   // sin destinatario usable no se emite (nada de placeholders)
        }

        return new NotificationEvent(
            Type: NotificationTypes.EventTicketsConfirmed,
            SubjectId: order.OrderRef,
            ToEmail: buyer.AttendeeEmail,
            ToName: buyer.AttendeeName,
            Code: OrderNumber(order.OrderRef),
            OccurredAt: occurredAt,
            Amount: order.Total,
            Currency: order.Currency,
            Lines: order.Units
                .Select(u => new NotificationLine(
                    Label: u.Seat is null ? u.TierName : $"{u.TierName} (asiento {u.Seat})",
                    Quantity: 1,
                    Amount: u.Price,
                    Currency: u.Currency,
                    Detail: u.Seat ?? EventTicketIssuer.TicketIdOf(u.ReservationId)))
                .ToList(),
            ActionPath: $"/eventos/entradas/{order.OrderRef}");
    }

    /// <summary>
    /// El número de orden que ve una persona, derivado determinísticamente del <c>orderRef</c>.
    /// </summary>
    /// <remarks>
    /// Re-confirmar la misma compra da el mismo número, así que el código que el asistente
    /// guarda es estable entre confirmaciones y entre reinicios. El recorte de <c>evord_</c> es
    /// del motor en proceso; un identificador con otra forma sale entero, que es correcto.
    /// </remarks>
    public static string OrderNumber(string orderRef)
    {
        var raw = orderRef.Replace("evord_", string.Empty, StringComparison.Ordinal);
        return "SYN-EVT-" + (raw.Length >= 8 ? raw[..8] : raw).ToUpperInvariant();
    }
}
