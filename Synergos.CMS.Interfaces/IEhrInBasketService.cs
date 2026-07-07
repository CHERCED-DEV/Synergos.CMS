namespace Synergos.CMS.Interfaces;

/// <summary>
/// In Basket del proveedor — cola tipada del dashboard EHR-lite (OLA 7, portal
/// clínico; SH-7 v3, plan doc 21 §2.5): reúne en una sola bandeja del médico los
/// ítems accionables de tipos distintos (resultado liberado / solicitud de refill /
/// mensaje del paciente). Capa de DEMO aditiva que alimenta la app Angular
/// <c>module-clinical-ehr</c> vía <c>/api/ehr/inbasket</c>.
/// </summary>
/// <remarks>
/// <strong>DERIVA, no duplica (DIP):</strong> el In Basket COMPONE los seams vivos
/// —resultados (<see cref="IClinicalResultsProvider"/>), refills
/// (<see cref="IClinicalMedicationService"/>) y mensajes
/// (<see cref="IMessagingService"/>, contexto 'clinical')— y los proyecta a una cola
/// tipada ordenada por recencia; NO mantiene un store paralelo (misma técnica que
/// <c>StubNotificationFeed</c>/<c>StubContentStream</c>). Stub-first (ADR 0002). Tipo
/// prefijado <c>Ehr*</c> para no colisionar.
/// </remarks>
public interface IEhrInBasketService
{
    /// <summary>
    /// Devuelve la cola del proveedor: ítems tipados (resultado/refill/mensaje)
    /// accionables, del más reciente al más antiguo. Vacío si no hay. Opcionalmente
    /// filtra por <paramref name="type"/> (result | refill | message).
    /// </summary>
    Task<IReadOnlyList<EhrInBasketItem>> GetForProviderAsync(string providerId, string? type = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Un ítem tipado del In Basket. <see cref="Type"/>: result | refill | message.
/// <see cref="RefId"/> apunta al objeto de origen (resultId/refillId/threadId) para
/// que la UI abra el detalle. <see cref="PatientId"/> puede venir vacío para mensajes
/// cuyo remitente no es un paciente del padrón.
/// </summary>
public sealed record EhrInBasketItem(
    string Id,
    string Type,
    string ProviderId,
    string PatientId,
    string PatientName,
    string Title,
    string Preview,
    string Priority,
    DateTime OccurredAtUtc,
    string RefId);
