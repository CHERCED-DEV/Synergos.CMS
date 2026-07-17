namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resultados de laboratorio del dashboard EHR-lite (OLA 7, portal paciente +
/// clínico): panel de labs por paciente con valor + rango de referencia + flag
/// (normal/alto/bajo/crítico). Capa de DEMO aditiva sobre datos sembrados que
/// alimenta la app Angular <c>module-mychart</c> / <c>module-clinical-ehr</c> vía
/// <c>/api/ehr/results</c>.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting:</strong> cada lectura audita acceso a PHI vía
/// <see cref="IAuditTrailWriter"/> (ADR 0037, reuso directo) — best-effort en la
/// demo (no fail-closed como ADR 0098). Stub-first (ADR 0002): el default
/// <c>StubClinicalResultsProvider</c> sirve un panel sembrado en memoria; un adapter
/// real (LIS / HL7-ORU) reemplaza el seam sin tocar el controller. Tipos prefijados
/// <c>Ehr*</c> para no colisionar con los records del núcleo PHI de producción en el
/// mismo namespace.
/// </remarks>
public interface IClinicalResultsProvider
{
    /// <summary>
    /// Devuelve los resultados de laboratorio de un paciente, del más reciente al
    /// más antiguo. Vacío si no hay. Audita la lectura como acceso a PHI.
    /// </summary>
    Task<IReadOnlyList<EhrLabResult>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica (libera) un resultado nuevo para un paciente — lo alimenta el
    /// order-entry clínico cuando una orden de lab se resuelve. Devuelve el
    /// resultado creado. Audita la escritura como acceso a PHI.
    /// </summary>
    Task<EhrLabResult> ReleaseResultAsync(ReleaseLabResultRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Un resultado de laboratorio liberado. <see cref="Flag"/>:
/// normal | high | low | critical. <see cref="ReferenceLow"/>/<see cref="ReferenceHigh"/>
/// delimitan el rango de referencia; nulos si la prueba es cualitativa.
/// </summary>
public sealed record EhrLabResult(
    string Id,
    string PatientId,
    string PanelName,
    string TestName,
    string Value,
    string Unit,
    double? ReferenceLow,
    double? ReferenceHigh,
    string Flag,
    DateTime ResultedAtUtc,
    string? OrderId,
    string? OrderedByDoctorId,
    string? Notes);

/// <summary>Solicitud de liberación de un resultado (lo emite el order-entry clínico).</summary>
public sealed record ReleaseLabResultRequest(
    string PatientId,
    string PanelName,
    string TestName,
    string Value,
    string Unit,
    double? ReferenceLow = null,
    double? ReferenceHigh = null,
    string? OrderId = null,
    string? OrderedByDoctorId = null,
    string? Notes = null);
