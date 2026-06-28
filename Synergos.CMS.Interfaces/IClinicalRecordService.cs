namespace Synergos.CMS.Interfaces;

/// <summary>
/// Historia clínica del dashboard EHR-lite (OLA 5): historia/antecedentes +
/// timeline de encuentros (nota SOAP) + evolución de medidas. Capa de DEMO
/// aditiva sobre datos sembrados que alimenta la app Angular
/// <c>module-healthcare-ehr</c> vía <c>/api/ehr</c>.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting (spec §4):</strong> cada lectura/escritura de datos
/// clínicos emite un evento append-only a <see cref="IAuditTrailWriter"/> (ADR 0037,
/// reuso directo, sin seam nuevo) — el rastro de acceso a PHI es requisito del EHR.
/// El stub lo cablea internamente; un decorator sobre el seam mantiene el patrón
/// si se quiere mover la auditoría fuera de la implementación.
///
/// Stub-first (ADR 0002): el default <c>StubClinicalRecordService</c> mantiene la
/// historia + encuentros en memoria del proceso; un adapter real (PHI store / FHIR)
/// se enchufa sin tocar el controller. Tipos prefijados <c>Clinical*</c> para no
/// colisionar con <see cref="AppointmentSlot"/>/<see cref="PrescriptionRecord"/>.
/// </remarks>
public interface IClinicalRecordService
{
    /// <summary>
    /// Devuelve la historia clínica resumida de un paciente (motivo, antecedentes,
    /// problemas activos, alergias, medidas base), o null si no existe. Audita la
    /// lectura como acceso a PHI.
    /// </summary>
    Task<ClinicalHistory?> GetHistoryAsync(string patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista los encuentros (notas SOAP) de un paciente, del más reciente al más
    /// antiguo. Vacío si no hay. Audita la lectura como acceso a PHI.
    /// </summary>
    Task<IReadOnlyList<ClinicalEncounter>> GetEncountersAsync(string patientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra un encuentro nuevo (nota SOAP). Devuelve el encuentro creado.
    /// Audita la escritura como acceso a PHI. Lanza si el paciente es inválido.
    /// </summary>
    Task<ClinicalEncounter> AddEncounterAsync(AddEncounterRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Nota SOAP de un encuentro clínico: Subjetivo / Objetivo (vitales) / Análisis /
/// Plan. Forma canónica de documentación clínica (spec §2).
/// </summary>
/// <param name="Subjective">Relato del paciente (motivo, síntomas).</param>
/// <param name="Objective">Hallazgos del examen + vitales medidos.</param>
/// <param name="Assessment">Valoración / diagnóstico (impresión clínica).</param>
/// <param name="Plan">Plan: tratamiento, órdenes, seguimiento.</param>
/// <param name="Vitals">Signos vitales capturados en el Objective (opcional).</param>
public sealed record SoapNote(
    string Subjective,
    string Objective,
    string Assessment,
    string Plan,
    ClinicalVitals? Vitals = null);

/// <summary>Signos vitales de un encuentro — serie temporal de la evolución.</summary>
public sealed record ClinicalVitals(
    double? SystolicMmHg = null,
    double? DiastolicMmHg = null,
    double? HeartRateBpm = null,
    double? TemperatureC = null,
    double? WeightKg = null,
    double? HeightCm = null,
    double? GlucoseMgDl = null,
    double? OxygenSaturationPct = null);

/// <summary>Encuentro clínico registrado (nota SOAP + firma del clínico).</summary>
public sealed record ClinicalEncounter(
    string Id,
    string PatientId,
    string DoctorId,
    string DoctorName,
    DateTime OccurredAtUtc,
    string ReasonForVisit,
    SoapNote Soap,
    string? DiagnosisCode,
    bool SignedByClinician);

/// <summary>Solicitud de alta de un encuentro (nota SOAP).</summary>
public sealed record AddEncounterRequest(
    string PatientId,
    string DoctorId,
    string ReasonForVisit,
    SoapNote Soap,
    string? DiagnosisCode = null);

/// <summary>
/// Historia clínica resumida del paciente: lo que el chart muestra como overview
/// (problemas activos, antecedentes, alergias, medidas base). Datos de DEMO.
/// </summary>
public sealed record ClinicalHistory(
    string PatientId,
    string ChiefComplaint,
    IReadOnlyList<string> ActiveProblems,
    IReadOnlyList<string> PastMedicalHistory,
    IReadOnlyList<string> Allergies,
    IReadOnlyList<string> CurrentMedications,
    ClinicalVitals? BaselineVitals,
    DateTime? LastVisitUtc,
    int TotalEncounters);
