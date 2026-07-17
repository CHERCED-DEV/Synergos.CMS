using System;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubClinicalPrescriptionService"/> (seam
/// <see cref="IClinicalPrescriptionService"/>, recetas del EHR-lite — OLA 5): los 4
/// casos canónicos (ADR 0075) — empty / happy / filter / idempotent — más el rastro
/// PHI vía <see cref="IAuditTrailWriter"/> (reuso ADR 0037).
/// </summary>
public class StubClinicalPrescriptionServiceTests
{
    private static readonly Func<DateTime> Clock = () => new DateTime(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc);

    private static (IClinicalPrescriptionService Svc, RecordingAuditTrailWriter Audit) Make()
    {
        var audit = new RecordingAuditTrailWriter();
        return (new StubClinicalPrescriptionService(audit, Clock), audit);
    }

    private static AddPrescriptionRequest Req(string patientId = "pat-camila-restrepo")
        => new(
            PatientId: patientId,
            DoctorId: "doc-ana-rios",
            Items: new[] { new EhrPrescriptionItem("Sumatriptán", "50 mg", "PRN", 30, "Máx 2/día") });

    [Fact] // empty: paciente sin recetas → lista vacía (no lanza) + audita la lectura
    public async Task GetForPatient_NoPrescriptions_ReturnsEmpty_AndAudits()
    {
        var (svc, audit) = Make();

        var list = await svc.GetForPatientAsync("pat-andres-pardo"); // sin recetas sembradas

        Assert.Empty(list);
        Assert.Contains(audit.Events, e => e.Action == "prescription.read");
    }

    [Fact] // happy: recetas sembradas se listan
    public async Task GetForPatient_Seeded_ReturnsPrescriptions()
    {
        var (svc, _) = Make();

        var list = await svc.GetForPatientAsync("pat-jorge-medina");

        Assert.NotEmpty(list);
        Assert.All(list, p => Assert.Equal("pat-jorge-medina", p.PatientId));
    }

    [Fact] // happy: Add registra una receta active + audita la escritura
    public async Task Add_Persists_AndAuditsWrite()
    {
        var (svc, audit) = Make();

        var rx = await svc.AddAsync(Req());

        Assert.False(string.IsNullOrWhiteSpace(rx.Id));
        Assert.Equal("active", rx.Status);
        Assert.Equal("Dra. Ana Ríos", rx.DoctorName); // resuelto del directorio sembrado
        Assert.Single(rx.Items);
        Assert.Contains(audit.Events, e => e.Action == "prescription.create" && e.Resource.Contains("pat-camila-restrepo"));
    }

    [Fact] // inválido: receta sin ítems lanza
    public async Task Add_NoItems_Throws()
    {
        var (svc, _) = Make();
        var bad = new AddPrescriptionRequest("pat-camila-restrepo", "doc-ana-rios", Array.Empty<EhrPrescriptionItem>());
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddAsync(bad));
    }

    [Fact] // filter: las recetas son por paciente
    public async Task GetForPatient_IsScopedPerPatient()
    {
        var (svc, _) = Make();
        await svc.AddAsync(Req("pat-camila-restrepo"));

        var other = await svc.GetForPatientAsync("pat-valentina-cruz");

        Assert.DoesNotContain(other, p => p.PatientId == "pat-camila-restrepo");
    }

    [Fact] // idempotent-ish: cada Add crea un id único; ambas quedan en el listado
    public async Task Add_Twice_BothListed_DistinctIds()
    {
        var (svc, _) = Make();

        var first = await svc.AddAsync(Req());
        var second = await svc.AddAsync(Req());

        Assert.NotEqual(first.Id, second.Id);
        var list = await svc.GetForPatientAsync("pat-camila-restrepo");
        Assert.Contains(list, p => p.Id == first.Id);
        Assert.Contains(list, p => p.Id == second.Id);
        // Orden: más reciente primero (mismo instante → ambos presentes).
        Assert.True(list.Count >= 2);
    }
}
