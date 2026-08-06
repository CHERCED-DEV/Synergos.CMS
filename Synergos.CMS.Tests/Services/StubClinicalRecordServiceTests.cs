using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubClinicalRecordService"/> (seam <see cref="IClinicalRecordService"/>,
/// historia + encuentros SOAP del EHR-lite — OLA 5): los 4 casos canónicos
/// (ADR 0075) — empty / happy / filter / idempotent — más la verificación del
/// rastro PHI: CADA lectura/escritura emite un evento a <see cref="IAuditTrailWriter"/>
/// (reuso del seam de auditoría ADR 0037, no se reinventa).
/// </summary>
public class StubClinicalRecordServiceTests
{
    private static readonly Func<DateTime> Clock = () => new DateTime(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc);

    private static (IClinicalRecordService Svc, RecordingAuditTrailWriter Audit) Make()
    {
        var audit = new RecordingAuditTrailWriter();
        return (new StubClinicalRecordService(audit, Clock), audit);
    }

    private static AddEncounterRequest Req(string patientId = "pat-camila-restrepo")
        => new(
            PatientId: patientId,
            DoctorId: "doc-ana-rios",
            ReasonForVisit: "Control",
            Soap: new SoapNote("Síntomas", "Examen normal", "Estable", "Continuar",
                new ClinicalVitals(SystolicMmHg: 120, DiastolicMmHg: 80, HeartRateBpm: 70)));

    [Fact] // empty: paciente sin historia → null (no lanza) + audita la lectura
    public async Task GetHistory_UnknownPatient_ReturnsNull_AndAudits()
    {
        var (svc, audit) = Make();

        var history = await svc.GetHistoryAsync("pat-no-existe");

        Assert.Null(history);
        Assert.Contains(audit.Events, e => e.Action == "clinical-record.read");
    }

    [Fact] // happy: historia sembrada se devuelve con problemas activos
    public async Task GetHistory_SeededPatient_ReturnsHistory()
    {
        var (svc, _) = Make();

        var history = await svc.GetHistoryAsync("pat-jorge-medina");

        Assert.NotNull(history);
        Assert.Contains("Hipertensión arterial esencial", history!.ActiveProblems);
        Assert.True(history.TotalEncounters >= 1); // tiene encuentros sembrados
    }

    [Fact] // happy: encuentros sembrados se listan del más reciente al más antiguo
    public async Task GetEncounters_Seeded_ReturnsNewestFirst()
    {
        var (svc, audit) = Make();

        var encounters = await svc.GetEncountersAsync("pat-jorge-medina");

        Assert.True(encounters.Count >= 2);
        Assert.True(encounters[0].OccurredAtUtc >= encounters[1].OccurredAtUtc);
        Assert.Contains(audit.Events, e => e.Action == "clinical-record.read");
    }

    [Fact] // filter: los encuentros son por paciente (otro paciente no ve los de Jorge)
    public async Task GetEncounters_IsScopedPerPatient()
    {
        var (svc, _) = Make();

        var camila = await svc.GetEncountersAsync("pat-camila-restrepo");
        var jorge = await svc.GetEncountersAsync("pat-jorge-medina");

        Assert.All(camila, e => Assert.Equal("pat-camila-restrepo", e.PatientId));
        Assert.All(jorge, e => Assert.Equal("pat-jorge-medina", e.PatientId));
        Assert.DoesNotContain(camila, e => e.PatientId == "pat-jorge-medina");
    }

    [Fact] // happy: AddEncounter registra y audita la ESCRITURA como acceso a PHI
    public async Task AddEncounter_Persists_AndAuditsWrite()
    {
        var (svc, audit) = Make();

        var created = await svc.AddEncounterAsync(Req());

        Assert.False(string.IsNullOrWhiteSpace(created.Id));
        Assert.True(created.SignedByClinician);
        Assert.Equal("Dra. Ana Ríos", created.DoctorName); // resuelto del directorio sembrado
        Assert.Contains(audit.Events, e => e.Action == "encounter.create" && e.Resource.Contains("pat-camila-restrepo"));

        // El encuentro queda en el timeline (lo más reciente).
        var encounters = await svc.GetEncountersAsync("pat-camila-restrepo");
        Assert.Equal(created.Id, encounters[0].Id);
    }

    [Fact] // inválido: AddEncounter sin paciente lanza
    public async Task AddEncounter_NoPatient_Throws()
    {
        var (svc, _) = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddEncounterAsync(Req(patientId: "")));
    }

    [Fact] // happy: tras AddEncounter, la historia refleja el nuevo total + última visita
    public async Task AddEncounter_UpdatesHistoryDerivedFields()
    {
        var (svc, _) = Make();
        var before = await svc.GetHistoryAsync("pat-camila-restrepo");

        await svc.AddEncounterAsync(Req());
        var after = await svc.GetHistoryAsync("pat-camila-restrepo");

        Assert.Equal(before!.TotalEncounters + 1, after!.TotalEncounters);
        Assert.NotNull(after.LastVisitUtc);
    }

    [Fact] // idempotent-ish: cada AddEncounter crea un id único (no pisa)
    public async Task AddEncounter_Twice_CreatesTwoDistinct()
    {
        var (svc, _) = Make();

        var first = await svc.AddEncounterAsync(Req());
        var second = await svc.AddEncounterAsync(Req());

        Assert.NotEqual(first.Id, second.Id);
        var encounters = await svc.GetEncountersAsync("pat-camila-restrepo");
        Assert.Equal(2, encounters.Count(e => e.Id == first.Id || e.Id == second.Id));
    }
}
