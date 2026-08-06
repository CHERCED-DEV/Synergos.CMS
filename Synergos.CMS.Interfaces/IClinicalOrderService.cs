namespace Synergos.CMS.Interfaces;

/// <summary>
/// Order-entry clínico del dashboard EHR-lite (OLA 7, portal clínico): el médico
/// coloca órdenes (laboratorio / e-Rx / imagen / referido) sobre un paciente. Capa
/// de DEMO aditiva que alimenta la app Angular <c>module-clinical-ehr</c> vía
/// <c>/api/ehr/order</c> y cierra el bucle cross-portal (una orden de lab alimenta
/// los resultados del paciente; el ítem entra al In Basket del proveedor).
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting:</strong> cada orden se audita vía
/// <see cref="IAuditTrailWriter"/> (ADR 0037, reuso directo) — best-effort en la demo.
/// El bucle order→result lo cierra el controller componiendo este seam con
/// <see cref="IClinicalResultsProvider"/> (una orden de lab libera un resultado
/// sembrado coherente); no se duplica estado. Stub-first (ADR 0002): el default
/// <c>StubClinicalOrderService</c> mantiene las órdenes en memoria; un adapter real
/// (CPOE / eRx gateway) reemplaza el seam sin tocar el controller. Tipos prefijados
/// <c>Ehr*</c> para no colisionar.
/// </remarks>
public interface IClinicalOrderService
{
    /// <summary>
    /// Registra una orden clínica nueva (estado <c>placed</c>). Devuelve la orden
    /// creada. Audita la escritura como acceso a PHI. Lanza si el paciente, el
    /// proveedor o el tipo son inválidos.
    /// </summary>
    Task<EhrClinicalOrder> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista las órdenes de un paciente, de la más reciente a la más antigua. Vacío
    /// si no hay. Audita la lectura como acceso a PHI.
    /// </summary>
    Task<IReadOnlyList<EhrClinicalOrder>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Una orden clínica registrada. <see cref="Type"/>: lab | eRx | imaging | referral.
/// <see cref="Status"/>: placed | in-progress | resulted | completed.
/// </summary>
public sealed record EhrClinicalOrder(
    string Id,
    string PatientId,
    string ProviderId,
    string ProviderName,
    string Type,
    string Detail,
    DateTime PlacedAtUtc,
    string Status);

/// <summary>Solicitud de alta de una orden clínica (la coloca el médico).</summary>
public sealed record PlaceOrderRequest(
    string PatientId,
    string ProviderId,
    string Type,
    string Detail);
