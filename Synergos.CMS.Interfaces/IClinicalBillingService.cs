namespace Synergos.CMS.Interfaces;

/// <summary>
/// Facturación del dashboard EHR-lite (OLA 7, portal paciente): estado de cuenta
/// (statement) + saldo + plan/aseguradora del paciente. Capa de DEMO aditiva sobre
/// datos sembrados que alimenta la app Angular <c>module-mychart</c> vía
/// <c>/api/ehr/billing</c>.
/// </summary>
/// <remarks>
/// Solo LEE — un adapter real (billing / RCM) reemplaza el seam sin tocar el
/// controller. Cada lectura audita acceso a PHI vía <see cref="IAuditTrailWriter"/>
/// (ADR 0037, reuso directo) — best-effort en la demo. Stub-first (ADR 0002): el
/// default <c>StubClinicalBillingService</c> sirve un estado de cuenta sembrado
/// coherente con el padrón. Tipos prefijados <c>Ehr*</c> para no colisionar.
/// </remarks>
public interface IClinicalBillingService
{
    /// <summary>
    /// Devuelve el estado de cuenta del paciente (líneas + saldo + plan), o
    /// <c>null</c> si el paciente no existe. Audita la lectura como acceso a PHI.
    /// </summary>
    Task<EhrBillingStatement?> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Estado de cuenta del paciente: líneas del statement + saldo pendiente + plan.
/// Montos en <see cref="Currency"/> (COP en la demo).
/// </summary>
public sealed record EhrBillingStatement(
    string PatientId,
    IReadOnlyList<EhrBillingLine> Statement,
    decimal Balance,
    string Currency,
    EhrInsurancePlan Plan);

/// <summary>Una línea del estado de cuenta. <see cref="Status"/>: paid | due | pending-insurance.</summary>
public sealed record EhrBillingLine(
    string Id,
    DateTime ServiceDateUtc,
    string Description,
    decimal Amount,
    decimal PatientResponsibility,
    string Status);

/// <summary>Plan/aseguradora del paciente (cobertura de la demo).</summary>
public sealed record EhrInsurancePlan(
    string PlanName,
    string MemberId,
    string Coverage,
    decimal CopayAmount);
