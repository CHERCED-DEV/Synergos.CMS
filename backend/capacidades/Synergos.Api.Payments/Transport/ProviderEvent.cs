using System.Text.Json;
using Synergos.Api.Payments.Domain;
using Synergos.Core;

namespace Synergos.Api.Payments.Transport;

/// <summary>Lo que un evento de Wompi dice, ya traducido a nuestro vocabulario.</summary>
/// <param name="Reference">La referencia del comercio — la que nosotros generamos al autorizar.</param>
/// <param name="TransactionId">El identificador de Wompi, para conciliar.</param>
/// <param name="Status">A qué estado dice que llegó, o <c>null</c> si no resuelve nada todavía.</param>
/// <param name="AmountInCents">Por cuánto, en centavos.</param>
public sealed record ProviderEvent(string? Reference, string? TransactionId, PaymentStatus? Status, long AmountInCents);

/// <summary>
/// Traduce el cuerpo de un evento de Wompi a un <see cref="ProviderEvent"/>.
/// </summary>
/// <remarks>
/// <b>Un evento que no resuelve nada no es un error.</b> Wompi también avisa de transacciones que
/// siguen <c>PENDING</c>; esta capacidad no tiene nada que anotar con eso, pero tampoco puede
/// contestar un 4xx, o el proveedor reintentaría durante días algo que decidimos ignorar a
/// propósito.
/// </remarks>
public static class ProviderEventReader
{
    public static Result<ProviderEvent> Read(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return Rejection.Invalid($"{PaymentRules.CodePrefix}.webhook_unreadable",
                $"El cuerpo del evento no es JSON: {ex.Message}");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("data", out var datos)
                || datos.ValueKind != JsonValueKind.Object
                || !datos.TryGetProperty("transaction", out var tx)
                || tx.ValueKind != JsonValueKind.Object)
            {
                return Rejection.Invalid($"{PaymentRules.CodePrefix}.webhook_unreadable",
                    "El evento no trae una transacción.");
            }

            var centavos = tx.TryGetProperty("amount_in_cents", out var monto)
                && monto.ValueKind == JsonValueKind.Number
                && monto.TryGetInt64(out var v) ? v : 0L;

            return Result.Ok(new ProviderEvent(
                Texto(tx, "reference"),
                Texto(tx, "id"),
                Traducir(Texto(tx, "status")),
                centavos));
        }
    }

    /// <summary>
    /// El diccionario de estados, y es corto a propósito.
    /// </summary>
    /// <remarks>
    /// <c>PENDING</c> —y cualquier cosa que Wompi agregue mañana— se traduce a <c>null</c>, o sea
    /// «esto no cambia nada»: dar por perdida una compra viva porque llegó un estado que no
    /// conocíamos es el modo de fallo que no se ve, porque el pedido simplemente no sale.
    /// </remarks>
    private static PaymentStatus? Traducir(string? estado) => (estado ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "APPROVED" => PaymentStatus.Captured,
        "DECLINED" or "ERROR" => PaymentStatus.Failed,
        "VOIDED" => PaymentStatus.Voided,
        _ => null,
    };

    private static string? Texto(JsonElement objeto, string propiedad)
        => objeto.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
