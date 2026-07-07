using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Semilla compartida de la demo del dashboard clínico EHR-lite (OLA 5). Centraliza
/// pacientes, médicos, historias clínicas, encuentros (SOAP) y recetas sembrados
/// para que los stubs (<see cref="StubPatientRegistry"/>, <see cref="StubDoctorDirectory"/>,
/// <see cref="StubClinicalRecordService"/>, <see cref="StubClinicalPrescriptionService"/>,
/// <see cref="StubClinicalSchedulingService"/>) rindan un padrón COHERENTE
/// end-to-end (buscar paciente → chart → historia → encuentros → recetas → agenda).
/// Datos puros/deterministas (ADR 0002) — el adapter real reemplaza cada seam por
/// su store sin tocar esta semilla.
/// </summary>
/// <remarks>
/// Las fechas relativas (encuentros/recetas/última visita) se derivan de un instante
/// base inyectable para que la demo sea estable y testeable. Todo es data de DEMO,
/// NO PHI de producción (esa vive en el repositorio cifrado de ADR 0098).
/// </remarks>
internal static class EhrDemoSeed
{
    // ── Médicos ────────────────────────────────────────────────────────
    public static IReadOnlyList<MedicalDoctor> Doctors() => new[]
    {
        new MedicalDoctor(
            Id: "doc-ana-rios", FullName: "Dra. Ana Ríos", Specialty: "Medicina Interna",
            LicenseNumber: "RM-48213", Rating: 4.9, YearsExperience: 14,
            AvatarUrl: "/media/ehr/doctors/ana-rios.webp",
            WorkingDays: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            SlotStartHour: 8, SlotEndHour: 16, SlotMinutes: 30),
        new MedicalDoctor(
            Id: "doc-carlos-mejia", FullName: "Dr. Carlos Mejía", Specialty: "Cardiología",
            LicenseNumber: "RM-50917", Rating: 4.8, YearsExperience: 21,
            AvatarUrl: "/media/ehr/doctors/carlos-mejia.webp",
            WorkingDays: new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            SlotStartHour: 9, SlotEndHour: 15, SlotMinutes: 40),
        new MedicalDoctor(
            Id: "doc-laura-vega", FullName: "Dra. Laura Vega", Specialty: "Pediatría",
            LicenseNumber: "RM-46108", Rating: 5.0, YearsExperience: 9,
            AvatarUrl: "/media/ehr/doctors/laura-vega.webp",
            WorkingDays: new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday },
            SlotStartHour: 8, SlotEndHour: 13, SlotMinutes: 20),
        new MedicalDoctor(
            Id: "doc-diego-soto", FullName: "Dr. Diego Soto", Specialty: "Dermatología",
            LicenseNumber: "RM-52740", Rating: 4.7, YearsExperience: 12,
            AvatarUrl: "/media/ehr/doctors/diego-soto.webp",
            WorkingDays: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday },
            SlotStartHour: 10, SlotEndHour: 18, SlotMinutes: 30),
    };

    // ── Pacientes ──────────────────────────────────────────────────────
    public static IReadOnlyList<EhrPatient> Patients() => new[]
    {
        new EhrPatient(
            Id: "pat-camila-restrepo", FullName: "Camila Restrepo", DocumentId: "CC 1.012.448.901",
            Gender: "Femenino", DateOfBirth: new DateOnly(1991, 3, 18), AgeYears: 35,
            Phone: "+57 311 4480 901", Email: "camila.restrepo@example.co", City: "Medellín",
            BloodType: "O+",
            Allergies: new[] { "Penicilina" },
            ChronicConditions: new[] { "Migraña" },
            PrimaryDoctorId: "doc-ana-rios", AvatarUrl: "/media/ehr/patients/camila.webp"),
        new EhrPatient(
            Id: "pat-jorge-medina", FullName: "Jorge Medina", DocumentId: "CC 79.521.336",
            Gender: "Masculino", DateOfBirth: new DateOnly(1968, 11, 5), AgeYears: 57,
            Phone: "+57 320 7711 045", Email: "jorge.medina@example.co", City: "Bogotá",
            BloodType: "A+",
            Allergies: Array.Empty<string>(),
            ChronicConditions: new[] { "Hipertensión arterial", "Diabetes tipo 2" },
            PrimaryDoctorId: "doc-carlos-mejia", AvatarUrl: "/media/ehr/patients/jorge.webp"),
        new EhrPatient(
            Id: "pat-sara-gomez", FullName: "Sara Gómez", DocumentId: "TI 1.028.776.140",
            Gender: "Femenino", DateOfBirth: new DateOnly(2015, 7, 22), AgeYears: 10,
            Phone: "+57 315 9930 218", Email: "familia.gomez@example.co", City: "Cali",
            BloodType: "B+",
            Allergies: new[] { "Maní", "Polvo" },
            ChronicConditions: new[] { "Asma" },
            PrimaryDoctorId: "doc-laura-vega", AvatarUrl: "/media/ehr/patients/sara.webp"),
        new EhrPatient(
            Id: "pat-andres-pardo", FullName: "Andrés Pardo", DocumentId: "CC 1.094.220.515",
            Gender: "Masculino", DateOfBirth: new DateOnly(1985, 1, 30), AgeYears: 41,
            Phone: "+57 301 2204 884", Email: "andres.pardo@example.co", City: "Barranquilla",
            BloodType: "AB-",
            Allergies: Array.Empty<string>(),
            ChronicConditions: Array.Empty<string>(),
            PrimaryDoctorId: "doc-diego-soto", AvatarUrl: "/media/ehr/patients/andres.webp"),
        new EhrPatient(
            Id: "pat-valentina-cruz", FullName: "Valentina Cruz", DocumentId: "CC 1.001.337.620",
            Gender: "Femenino", DateOfBirth: new DateOnly(1998, 9, 12), AgeYears: 27,
            Phone: "+57 318 6650 273", Email: "valentina.cruz@example.co", City: "Bucaramanga",
            BloodType: "O-",
            Allergies: new[] { "Ibuprofeno" },
            ChronicConditions: new[] { "Hipotiroidismo" },
            PrimaryDoctorId: "doc-ana-rios", AvatarUrl: "/media/ehr/patients/valentina.webp"),
    };

    /// <summary>
    /// Historia clínica resumida por paciente (overview del chart). Las medidas base
    /// y la última visita las usa el stub de encuentros para mantener la coherencia.
    /// </summary>
    public static IReadOnlyDictionary<string, ClinicalHistory> Histories() => new Dictionary<string, ClinicalHistory>(StringComparer.Ordinal)
    {
        ["pat-camila-restrepo"] = new ClinicalHistory(
            PatientId: "pat-camila-restrepo",
            ChiefComplaint: "Cefalea recurrente con aura visual",
            ActiveProblems: new[] { "Migraña con aura" },
            PastMedicalHistory: new[] { "Apendicectomía (2012)" },
            Allergies: new[] { "Penicilina" },
            CurrentMedications: new[] { "Sumatriptán 50 mg PRN" },
            BaselineVitals: new ClinicalVitals(SystolicMmHg: 118, DiastolicMmHg: 76, HeartRateBpm: 72, TemperatureC: 36.6, WeightKg: 61, HeightCm: 165),
            LastVisitUtc: null, TotalEncounters: 0),
        ["pat-jorge-medina"] = new ClinicalHistory(
            PatientId: "pat-jorge-medina",
            ChiefComplaint: "Control de hipertensión y glicemia",
            ActiveProblems: new[] { "Hipertensión arterial esencial", "Diabetes mellitus tipo 2" },
            PastMedicalHistory: new[] { "Infarto agudo de miocardio (2019)", "Dislipidemia" },
            Allergies: Array.Empty<string>(),
            CurrentMedications: new[] { "Losartán 50 mg c/12h", "Metformina 850 mg c/12h", "Atorvastatina 20 mg noche" },
            BaselineVitals: new ClinicalVitals(SystolicMmHg: 148, DiastolicMmHg: 92, HeartRateBpm: 80, TemperatureC: 36.5, WeightKg: 88, HeightCm: 174, GlucoseMgDl: 162),
            LastVisitUtc: null, TotalEncounters: 0),
        ["pat-sara-gomez"] = new ClinicalHistory(
            PatientId: "pat-sara-gomez",
            ChiefComplaint: "Tos nocturna y sibilancias",
            ActiveProblems: new[] { "Asma persistente leve" },
            PastMedicalHistory: new[] { "Bronquiolitis (lactante)" },
            Allergies: new[] { "Maní", "Polvo" },
            CurrentMedications: new[] { "Salbutamol inhalador PRN", "Budesonida inhalada" },
            BaselineVitals: new ClinicalVitals(HeartRateBpm: 96, TemperatureC: 36.8, WeightKg: 32, HeightCm: 138, OxygenSaturationPct: 97),
            LastVisitUtc: null, TotalEncounters: 0),
        ["pat-andres-pardo"] = new ClinicalHistory(
            PatientId: "pat-andres-pardo",
            ChiefComplaint: "Lesión cutánea pruriginosa en antebrazo",
            ActiveProblems: new[] { "Dermatitis de contacto" },
            PastMedicalHistory: Array.Empty<string>(),
            Allergies: Array.Empty<string>(),
            CurrentMedications: Array.Empty<string>(),
            BaselineVitals: new ClinicalVitals(SystolicMmHg: 122, DiastolicMmHg: 78, HeartRateBpm: 68, TemperatureC: 36.7, WeightKg: 79, HeightCm: 180),
            LastVisitUtc: null, TotalEncounters: 0),
        ["pat-valentina-cruz"] = new ClinicalHistory(
            PatientId: "pat-valentina-cruz",
            ChiefComplaint: "Fatiga y aumento de peso",
            ActiveProblems: new[] { "Hipotiroidismo primario" },
            PastMedicalHistory: Array.Empty<string>(),
            Allergies: new[] { "Ibuprofeno" },
            CurrentMedications: new[] { "Levotiroxina 75 mcg/día" },
            BaselineVitals: new ClinicalVitals(SystolicMmHg: 110, DiastolicMmHg: 70, HeartRateBpm: 64, TemperatureC: 36.4, WeightKg: 68, HeightCm: 168),
            LastVisitUtc: null, TotalEncounters: 0),
    };

    /// <summary>
    /// Encuentros (notas SOAP) sembrados, con fechas relativas al instante base.
    /// Cada encuentro referencia paciente + médico reales del padrón.
    /// </summary>
    public static IReadOnlyList<ClinicalEncounter> Encounters(DateTime nowUtc) => new[]
    {
        new ClinicalEncounter(
            Id: "enc-seed-camila-1", PatientId: "pat-camila-restrepo",
            DoctorId: "doc-ana-rios", DoctorName: "Dra. Ana Ríos",
            OccurredAtUtc: nowUtc.AddDays(-21),
            ReasonForVisit: "Episodio de migraña con aura",
            Soap: new SoapNote(
                Subjective: "Refiere cefalea pulsátil hemicraneal con fotofobia y aura visual previa, 3 episodios en el último mes.",
                Objective: "Paciente álgica, sin signos de focalización neurológica. Fondo de ojo normal.",
                Assessment: "Migraña con aura, sin signos de alarma.",
                Plan: "Sumatriptán 50 mg al inicio del aura. Diario de cefalea. Control en 4 semanas.",
                Vitals: new ClinicalVitals(SystolicMmHg: 120, DiastolicMmHg: 78, HeartRateBpm: 74, TemperatureC: 36.6)),
            DiagnosisCode: "G43.1", SignedByClinician: true),
        new ClinicalEncounter(
            Id: "enc-seed-jorge-1", PatientId: "pat-jorge-medina",
            DoctorId: "doc-carlos-mejia", DoctorName: "Dr. Carlos Mejía",
            OccurredAtUtc: nowUtc.AddDays(-40),
            ReasonForVisit: "Control cardiometabólico",
            Soap: new SoapNote(
                Subjective: "Asintomático cardiovascular. Adherencia parcial a la dieta.",
                Objective: "PA 150/94, IMC 29. Sin edemas. Ruidos cardíacos rítmicos sin soplos.",
                Assessment: "HTA mal controlada. DM2 con glicemia elevada.",
                Plan: "Aumentar Losartán a 100 mg/día. Reforzar Metformina. HbA1c y perfil lipídico. Control en 6 semanas.",
                Vitals: new ClinicalVitals(SystolicMmHg: 150, DiastolicMmHg: 94, HeartRateBpm: 82, WeightKg: 89, GlucoseMgDl: 178)),
            DiagnosisCode: "I10", SignedByClinician: true),
        new ClinicalEncounter(
            Id: "enc-seed-jorge-2", PatientId: "pat-jorge-medina",
            DoctorId: "doc-carlos-mejia", DoctorName: "Dr. Carlos Mejía",
            OccurredAtUtc: nowUtc.AddDays(-5),
            ReasonForVisit: "Reevaluación de PA",
            Soap: new SoapNote(
                Subjective: "Mejoría de cifras tensionales en casa. Niega mareo.",
                Objective: "PA 138/86, FC 78. Buena tolerancia al ajuste.",
                Assessment: "HTA en mejor control. DM2 estable.",
                Plan: "Mantener esquema. Control en 8 semanas.",
                Vitals: new ClinicalVitals(SystolicMmHg: 138, DiastolicMmHg: 86, HeartRateBpm: 78, WeightKg: 88, GlucoseMgDl: 148)),
            DiagnosisCode: "I10", SignedByClinician: true),
        new ClinicalEncounter(
            Id: "enc-seed-sara-1", PatientId: "pat-sara-gomez",
            DoctorId: "doc-laura-vega", DoctorName: "Dra. Laura Vega",
            OccurredAtUtc: nowUtc.AddDays(-12),
            ReasonForVisit: "Crisis asmática leve",
            Soap: new SoapNote(
                Subjective: "Tos nocturna y sibilancias tras exposición a polvo.",
                Objective: "SatO2 96%, sibilancias espiratorias leves bilaterales.",
                Assessment: "Exacerbación leve de asma.",
                Plan: "Salbutamol PRN, continuar budesonida. Evitar alérgenos. Control si empeora.",
                Vitals: new ClinicalVitals(HeartRateBpm: 98, TemperatureC: 36.9, OxygenSaturationPct: 96)),
            DiagnosisCode: "J45.9", SignedByClinician: true),
    };

    /// <summary>
    /// Resultados de laboratorio sembrados (OLA 7), fechas relativas al instante base.
    /// Coherentes con la historia de cada paciente (Jorge diabético/HTA → glicemia y
    /// perfil lipídico altos; Valentina hipotiroidea → TSH alta; etc.). Cada uno
    /// referencia paciente + médico ordenante reales del padrón.
    /// </summary>
    public static IReadOnlyList<EhrLabResult> LabResults(DateTime nowUtc) => new[]
    {
        // Jorge Medina — control cardiometabólico (Dr. Carlos Mejía).
        new EhrLabResult(
            Id: "lab-seed-jorge-hba1c", PatientId: "pat-jorge-medina",
            PanelName: "Perfil metabólico", TestName: "Hemoglobina glicosilada (HbA1c)",
            Value: "8.1", Unit: "%", ReferenceLow: 4.0, ReferenceHigh: 5.7,
            Flag: "high", ResultedAtUtc: nowUtc.AddDays(-4),
            OrderId: null, OrderedByDoctorId: "doc-carlos-mejia",
            Notes: "Control glicémico subóptimo. Reforzar adherencia."),
        new EhrLabResult(
            Id: "lab-seed-jorge-glucose", PatientId: "pat-jorge-medina",
            PanelName: "Perfil metabólico", TestName: "Glucosa en ayunas",
            Value: "148", Unit: "mg/dL", ReferenceLow: 70, ReferenceHigh: 100,
            Flag: "high", ResultedAtUtc: nowUtc.AddDays(-4),
            OrderId: null, OrderedByDoctorId: "doc-carlos-mejia", Notes: null),
        new EhrLabResult(
            Id: "lab-seed-jorge-ldl", PatientId: "pat-jorge-medina",
            PanelName: "Perfil lipídico", TestName: "Colesterol LDL",
            Value: "132", Unit: "mg/dL", ReferenceLow: 0, ReferenceHigh: 100,
            Flag: "high", ResultedAtUtc: nowUtc.AddDays(-4),
            OrderId: null, OrderedByDoctorId: "doc-carlos-mejia", Notes: null),
        new EhrLabResult(
            Id: "lab-seed-jorge-creat", PatientId: "pat-jorge-medina",
            PanelName: "Función renal", TestName: "Creatinina sérica",
            Value: "0.9", Unit: "mg/dL", ReferenceLow: 0.7, ReferenceHigh: 1.3,
            Flag: "normal", ResultedAtUtc: nowUtc.AddDays(-4),
            OrderId: null, OrderedByDoctorId: "doc-carlos-mejia", Notes: null),
        // Camila Restrepo — control migraña (Dra. Ana Ríos).
        new EhrLabResult(
            Id: "lab-seed-camila-hb", PatientId: "pat-camila-restrepo",
            PanelName: "Hemograma", TestName: "Hemoglobina",
            Value: "13.4", Unit: "g/dL", ReferenceLow: 12.0, ReferenceHigh: 16.0,
            Flag: "normal", ResultedAtUtc: nowUtc.AddDays(-19),
            OrderId: null, OrderedByDoctorId: "doc-ana-rios", Notes: null),
        // Valentina Cruz — hipotiroidismo (Dra. Ana Ríos).
        new EhrLabResult(
            Id: "lab-seed-valentina-tsh", PatientId: "pat-valentina-cruz",
            PanelName: "Perfil tiroideo", TestName: "TSH",
            Value: "6.8", Unit: "mUI/L", ReferenceLow: 0.4, ReferenceHigh: 4.0,
            Flag: "high", ResultedAtUtc: nowUtc.AddDays(-9),
            OrderId: null, OrderedByDoctorId: "doc-ana-rios",
            Notes: "TSH elevada — considerar ajuste de levotiroxina."),
        new EhrLabResult(
            Id: "lab-seed-valentina-t4", PatientId: "pat-valentina-cruz",
            PanelName: "Perfil tiroideo", TestName: "T4 libre",
            Value: "0.7", Unit: "ng/dL", ReferenceLow: 0.8, ReferenceHigh: 1.8,
            Flag: "low", ResultedAtUtc: nowUtc.AddDays(-9),
            OrderId: null, OrderedByDoctorId: "doc-ana-rios", Notes: null),
    };

    /// <summary>
    /// Estados de cuenta sembrados por paciente (OLA 7), coherentes con las citas y
    /// el copago nominal de consulta. Montos en COP.
    /// </summary>
    public static IReadOnlyDictionary<string, EhrBillingStatement> Billing(DateTime nowUtc) => new Dictionary<string, EhrBillingStatement>(StringComparer.Ordinal)
    {
        ["pat-jorge-medina"] = new EhrBillingStatement(
            PatientId: "pat-jorge-medina",
            Statement: new[]
            {
                new EhrBillingLine("bill-jorge-1", nowUtc.AddDays(-40), "Consulta cardiología — control", 180_000m, 80_000m, "paid"),
                new EhrBillingLine("bill-jorge-2", nowUtc.AddDays(-5), "Consulta cardiología — reevaluación", 180_000m, 80_000m, "due"),
                new EhrBillingLine("bill-jorge-3", nowUtc.AddDays(-4), "Perfil metabólico + lipídico", 145_000m, 43_500m, "pending-insurance"),
            },
            Balance: 123_500m, Currency: "COP",
            Plan: new EhrInsurancePlan("EPS Sura — Plan Complementario", "SURA-79521336", "Cobertura 70%", 80_000m)),
        ["pat-camila-restrepo"] = new EhrBillingStatement(
            PatientId: "pat-camila-restrepo",
            Statement: new[]
            {
                new EhrBillingLine("bill-camila-1", nowUtc.AddDays(-21), "Consulta medicina interna", 160_000m, 80_000m, "paid"),
            },
            Balance: 0m, Currency: "COP",
            Plan: new EhrInsurancePlan("Nueva EPS — Contributivo", "NEPS-1012448901", "Cobertura 50%", 80_000m)),
        ["pat-valentina-cruz"] = new EhrBillingStatement(
            PatientId: "pat-valentina-cruz",
            Statement: new[]
            {
                new EhrBillingLine("bill-valentina-1", nowUtc.AddDays(-9), "Perfil tiroideo", 95_000m, 28_500m, "due"),
            },
            Balance: 28_500m, Currency: "COP",
            Plan: new EhrInsurancePlan("Compensar EPS — Contributivo", "COMP-1001337620", "Cobertura 70%", 80_000m)),
        ["pat-sara-gomez"] = new EhrBillingStatement(
            PatientId: "pat-sara-gomez",
            Statement: new[]
            {
                new EhrBillingLine("bill-sara-1", nowUtc.AddDays(-12), "Consulta pediatría — crisis asmática", 150_000m, 0m, "paid"),
            },
            Balance: 0m, Currency: "COP",
            Plan: new EhrInsurancePlan("Sanitas EPS — Contributivo", "SAN-1028776140", "Cobertura 100% (menor)", 0m)),
        ["pat-andres-pardo"] = new EhrBillingStatement(
            PatientId: "pat-andres-pardo",
            Statement: Array.Empty<EhrBillingLine>(),
            Balance: 0m, Currency: "COP",
            Plan: new EhrInsurancePlan("Medicina prepagada Colsanitas", "COLS-1094220515", "Cobertura 90%", 60_000m)),
    };

    /// <summary>Recetas sembradas, fechas relativas al instante base.</summary>
    public static IReadOnlyList<EhrPrescription> Prescriptions(DateTime nowUtc) => new[]
    {
        new EhrPrescription(
            Id: "rx-seed-jorge-1", PatientId: "pat-jorge-medina",
            DoctorId: "doc-carlos-mejia", DoctorName: "Dr. Carlos Mejía",
            IssuedAtUtc: nowUtc.AddDays(-40),
            Items: new[]
            {
                new EhrPrescriptionItem("Losartán", "100 mg", "Cada 24 horas", 90, "Tomar en la mañana."),
                new EhrPrescriptionItem("Metformina", "850 mg", "Cada 12 horas", 90, "Con las comidas."),
            },
            Status: "active", EncounterId: "enc-seed-jorge-1"),
        new EhrPrescription(
            Id: "rx-seed-camila-1", PatientId: "pat-camila-restrepo",
            DoctorId: "doc-ana-rios", DoctorName: "Dra. Ana Ríos",
            IssuedAtUtc: nowUtc.AddDays(-21),
            Items: new[]
            {
                new EhrPrescriptionItem("Sumatriptán", "50 mg", "PRN al inicio del aura", 30, "Máximo 2 dosis en 24 horas."),
            },
            Status: "active", EncounterId: "enc-seed-camila-1"),
    };
}
