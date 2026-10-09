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
/// comprador; a los asistentes apenas les hace <c>Trim()</c>, así que notificar por-asistente
/// dispararía contra cadenas vacías.</para>
///
/// <para><b>El comprador es el que guardó el checkout</b>
/// (<see cref="PersistedEventOrder.BuyerEmail"/>), que no tiene por qué ir al evento (#107). Se
/// tomaba la primera unidad, y como el checkout ya abría la sesión de pago a nombre del comprador,
/// el cobro le llegaba a uno y el aviso a otro. Sólo una orden anotada antes de guardarlo cae a la
/// primera unidad —lo que hacía siempre—, y de ésa la original (<c>AttendeeEmail</c>, no
/// <c>HolderEmail</c>: transferir una entrada cambia el portador, no a quién se le confirmó la
/// compra).</para>
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

        var (toName, toEmail) = Comprador(order);
        if (string.IsNullOrWhiteSpace(toEmail) || string.IsNullOrWhiteSpace(toName))
        {
            return null;   // sin destinatario usable no se emite (nada de placeholders)
        }

        return new NotificationEvent(
            Type: NotificationTypes.EventTicketsConfirmed,
            SubjectId: order.OrderRef,
            ToEmail: toEmail,
            ToName: toName,
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
            ActionPath: $"/eventos/entradas/{order.OrderRef}",
            Variant: Asiste(order, toEmail) ? null : NotificationVariants.EventBuyerNotAttending);
    }

    /// <summary>
    /// Si quien recibe el aviso tiene una de las entradas a su nombre.
    /// </summary>
    /// <remarks>
    /// Decide el texto, no el destinatario: «estas son tus entradas, llévalas contigo» sólo es
    /// cierto si va. Se mira el asistente ORIGINAL (<c>AttendeeEmail</c>) y no el portador, así
    /// que una re-emisión después de transferir dice lo mismo que la primera. Por correo y sin
    /// distinguir mayúsculas: es la misma persona aunque lo escriba distinto en dos campos.
    /// </remarks>
    private static bool Asiste(PersistedEventOrder order, string correo)
        => order.Units.Any(u => string.Equals(
            u.AttendeeEmail?.Trim(), correo.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A quién va el aviso: el comprador guardado; sin él, el primer asistente.
    /// </summary>
    /// <remarks>
    /// Se cae a la primera unidad sólo si NO hay correo de comprador guardado, no si lo hay y le
    /// falta otra cosa: mezclar el nombre de uno con el correo de otro sería mandarle a alguien un
    /// saludo ajeno.
    /// </remarks>
    private static (string? Name, string? Email) Comprador(PersistedEventOrder order)
    {
        if (!string.IsNullOrWhiteSpace(order.BuyerEmail))
        {
            return (order.BuyerName, order.BuyerEmail);
        }

        var primera = order.Units.Count > 0 ? order.Units[0] : null;
        return (primera?.AttendeeName, primera?.AttendeeEmail);
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
