using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Smoke tests de la capa EHR-lite de OLA 7 (doc 21 §2.5 — dos portales de un mismo
/// grafo clínico). Verifica que los 5 seams nuevos rinden el happy-path end-to-end y
/// que los bucles cross-portal cierran (order→result, refill→In Basket) con datos
/// sembrados coherentes. NO exhaustivo — tests SOLO smoke por instrucción de la ola.
/// </summary>
public class Ola7EhrPortalsSmokeTests
{
    private static readonly Func<DateTime> Clock = () => new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc);

    // ── 1. Resultados de laboratorio ────────────────────────────────────

    [Fact] // happy: labs sembrados se listan con flag coherente (Jorge diabético → HbA1c alta)
    public async Task Results_Seeded_ListWithFlag_AndAudits()
    {
        var audit = new RecordingAuditTrailWriter();
        var results = new StubClinicalResultsProvider(audit, Clock);

        var labs = await results.GetForPatientAsync("pat-jorge-medina");

        Assert.NotEmpty(labs);
        Assert.Contains(labs, r => r.TestName.Contains("HbA1c") && r.Flag == "high");
        Assert.Contains(audit.Events, e => e.Action == "lab-result.read");
    }

    // ── 2. Medicamentos activos derivados de recetas vivas ──────────────

    [Fact] // happy: medicación activa = proyección de recetas active (DIP, sin store paralelo)
    public async Task Medications_DerivedFromActivePrescriptions()
    {
        var (meds, _, _, _) = MakeMedication();

        var active = await meds.GetActiveForPatientAsync("pat-jorge-medina"); // tiene rx sembrada active

        Assert.NotEmpty(active);
        Assert.All(active, m => Assert.Equal("active", m.Status));
        Assert.Contains(active, m => m.MedicationName == "Losartán");
    }

    // ── 3. Refill enruta al In Basket del médico + audita ───────────────

    [Fact] // happy: refill crea solicitud pending, la manda al In Basket del prescriptor y audita
    public async Task Refill_RoutesToProviderInBasket_AndAudits()
    {
        var (meds, messaging, audit, _) = MakeMedication();
        var active = await meds.GetActiveForPatientAsync("pat-jorge-medina");
        var med = active.First();

        var refill = await meds.RequestRefillAsync(new RefillRequest("pat-jorge-medina", med.MedicationId, "Se me acaba"));

        Assert.Equal("pending", refill.Status);
        Assert.Equal(med.PrescribedByDoctorId, refill.ProviderId);
        Assert.Contains(audit.Events, e => e.Action == "refill.request");
        // El hilo cayó al inbox del proveedor (contexto clinical).
        var inbox = await messaging.GetInboxAsync(med.PrescribedByDoctorId);
        Assert.Contains(inbox, t => t.ContextRef.StartsWith("clinical:refill:", StringComparison.Ordinal));
    }

    [Fact] // inválido: refill de una medicación que no está activa lanza
    public async Task Refill_UnknownMedication_Throws()
    {
        var (meds, _, _, _) = MakeMedication();
        await Assert.ThrowsAsync<ArgumentException>(
            () => meds.RequestRefillAsync(new RefillRequest("pat-jorge-medina", "no-existe:0")));
    }

    // ── 4. Order-entry cierra el bucle order→result ─────────────────────

    [Fact] // happy: una orden de lab libera un resultado coherente para el paciente
    public async Task Order_Lab_ReleasesResult_ClosingLoop()
    {
        var audit = new RecordingAuditTrailWriter();
        var doctors = new StubDoctorDirectory();
        var results = new StubClinicalResultsProvider(audit, Clock);
        var orders = new StubClinicalOrderService(doctors, results, audit, Clock);

        var before = (await results.GetForPatientAsync("pat-andres-pardo")).Count;
        var order = await orders.PlaceAsync(new PlaceOrderRequest("pat-andres-pardo", "doc-diego-soto", "lab", "Hemograma completo"));
        var after = (await results.GetForPatientAsync("pat-andres-pardo")).Count;

        Assert.Equal("lab", order.Type);
        Assert.Equal(after, before + 1); // el resultado se liberó
        Assert.Contains(audit.Events, e => e.Action == "order.place");
    }

    // ── 5. Facturación sembrada ─────────────────────────────────────────

    [Fact] // happy: statement + saldo + plan del paciente
    public async Task Billing_Seeded_ReturnsStatementAndPlan()
    {
        var audit = new RecordingAuditTrailWriter();
        var billing = new StubClinicalBillingService(audit, Clock);

        var statement = await billing.GetForPatientAsync("pat-jorge-medina");

        Assert.NotNull(statement);
        Assert.NotEmpty(statement!.Statement);
        Assert.True(statement.Balance > 0);
        Assert.False(string.IsNullOrWhiteSpace(statement.Plan.PlanName));
        Assert.Contains(audit.Events, e => e.Action == "billing.read");
    }

    [Fact] // empty: paciente inexistente → null (no lanza)
    public async Task Billing_UnknownPatient_ReturnsNull()
    {
        var billing = new StubClinicalBillingService(new RecordingAuditTrailWriter(), Clock);
        Assert.Null(await billing.GetForPatientAsync("pat-no-existe"));
    }

    // ── 6. In Basket tipado del proveedor (deriva de 3 fuentes) ─────────

    [Fact] // happy: el In Basket reúne resultado anormal + refill del proveedor, tipados
    public async Task InBasket_AggregatesTypedItems_ForProvider()
    {
        var audit = new RecordingAuditTrailWriter();
        var patients = new StubPatientRegistry();
        var messaging = new StubMessagingService();
        var prescriptions = new StubClinicalPrescriptionService(audit, Clock);
        var results = new StubClinicalResultsProvider(audit, Clock);
        var meds = new StubClinicalMedicationService(prescriptions, messaging, audit, Clock);
        var inBasket = new StubEhrInBasketService(results, meds, messaging, patients);

        // Genera un refill dirigido a Carlos Mejía (prescriptor de Jorge).
        var active = await meds.GetActiveForPatientAsync("pat-jorge-medina");
        await meds.RequestRefillAsync(new RefillRequest("pat-jorge-medina", active.First().MedicationId));

        var queue = await inBasket.GetForProviderAsync("doc-carlos-mejia");

        Assert.NotEmpty(queue);
        // Resultado anormal ordenado por Carlos (HbA1c/glucosa altas de Jorge).
        Assert.Contains(queue, i => i.Type == "result");
        // Refill que acabamos de enrutar.
        Assert.Contains(queue, i => i.Type == "refill");
        // Orden por recencia (desc).
        var times = queue.Select(i => i.OccurredAtUtc).ToList();
        Assert.Equal(times.OrderByDescending(t => t).ToList(), times);
    }

    [Fact] // filter: type=refill devuelve solo refills
    public async Task InBasket_TypeFilter_ScopesToType()
    {
        var audit = new RecordingAuditTrailWriter();
        var patients = new StubPatientRegistry();
        var messaging = new StubMessagingService();
        var prescriptions = new StubClinicalPrescriptionService(audit, Clock);
        var results = new StubClinicalResultsProvider(audit, Clock);
        var meds = new StubClinicalMedicationService(prescriptions, messaging, audit, Clock);
        var inBasket = new StubEhrInBasketService(results, meds, messaging, patients);

        var active = await meds.GetActiveForPatientAsync("pat-jorge-medina");
        await meds.RequestRefillAsync(new RefillRequest("pat-jorge-medina", active.First().MedicationId));

        var onlyRefills = await inBasket.GetForProviderAsync("doc-carlos-mejia", "refill");

        Assert.NotEmpty(onlyRefills);
        Assert.All(onlyRefills, i => Assert.Equal("refill", i.Type));
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private static (StubClinicalMedicationService Meds, IMessagingService Messaging, RecordingAuditTrailWriter Audit, IClinicalPrescriptionService Rx) MakeMedication()
    {
        var audit = new RecordingAuditTrailWriter();
        var messaging = new StubMessagingService();
        var prescriptions = new StubClinicalPrescriptionService(audit, Clock);
        var meds = new StubClinicalMedicationService(prescriptions, messaging, audit, Clock);
        return (meds, messaging, audit, prescriptions);
    }
}
