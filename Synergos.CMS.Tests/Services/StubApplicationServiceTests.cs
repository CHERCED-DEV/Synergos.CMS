using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubApplicationService"/> (seam <c>IApplicationService</c>,
/// agregado raíz del expediente del portal Gobierno): empty (trámite desconocido /
/// campos requeridos vacíos / solicitante incompleto) / happy (radicar con tasa abre
/// sesión de pago + audita; radicar gratuito apaga el paso de pago) / filter (el
/// formulario dinámico seccionado exige SOLO los campos required de la definición) /
/// idempotencia (NO aplica por diseño a radicar — cada radicación crea un expediente
/// nuevo; se verifica explícitamente) + el seed de expedientes en varios estados.
/// ADR 0075.
/// </summary>
public class StubApplicationServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static StubApplicationService Make(IAuditTrailWriter? audit = null, IJsonEntityStore? store = null)
        => new(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            audit,
            () => Clock,
            store);

    private static GovCitizen Citizen() => new("Ana Torres", "ana.torres@correo.co", "1030567890", "+57 300 555 1234");

    // Formulario completo del trámite CON tasa (pasaporte).
    private static Dictionary<string, string> PasaporteForm() => new()
    {
        ["nombreCompleto"] = "Ana Torres",
        ["cedula"] = "1030567890",
        ["fechaNacimiento"] = "1991-05-20",
        ["ciudad"] = "Bogotá",
        ["correo"] = "ana.torres@correo.co",
    };

    // Formulario completo del trámite GRATUITO (certificado de residencia).
    private static Dictionary<string, string> CertificadoForm() => new()
    {
        ["nombreCompleto"] = "Ana Torres",
        ["cedula"] = "1030567890",
        ["direccion"] = "Cll 45 # 13-25",
        ["correo"] = "ana.torres@correo.co",
    };

    [Fact] // empty: trámite desconocido lanza ArgumentException
    public async Task Radicar_UnknownTramite_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().RadicarAsync("trm-que-no-existe", PasaporteForm(), Citizen()));
    }

    [Fact] // empty: solicitante sin nombre/email lanza
    public async Task Radicar_IncompleteCitizen_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().RadicarAsync("trm-pasaporte", PasaporteForm(), new GovCitizen("", "")));
    }

    [Fact] // filter: el formulario dinámico exige los campos required de la definición
    public async Task Radicar_MissingRequiredField_Throws()
    {
        var form = PasaporteForm();
        form.Remove("cedula");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Make().RadicarAsync("trm-pasaporte", form, Citizen()));
        Assert.Contains("cedula", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // filter: un campo opcional ausente NO bloquea la radicación
    public async Task Radicar_MissingOptionalField_Succeeds()
    {
        // "barrio" es optional en certificado de residencia — no viaja y aun así radica.
        var result = await Make().RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());
        Assert.StartsWith("case_", result.Case.CaseId);
    }

    [Fact] // happy: trámite con tasa crea expediente + abre sesión de pago + audita
    public async Task Radicar_PaidTramite_OpensPaymentSessionAndAudits()
    {
        var audit = new RecordingAuditTrailWriter();
        var svc = Make(audit);

        var result = await svc.RadicarAsync("trm-pasaporte", PasaporteForm(), Citizen());

        Assert.StartsWith("case_", result.Case.CaseId);
        Assert.StartsWith("SG-2026-", result.Case.Radicado);
        Assert.Equal(189_000m, result.Case.FeeMinor);
        Assert.Equal("COP", result.Case.Currency);
        Assert.NotNull(result.PaymentSessionId); // tasa > 0 ⇒ el motor abrió el pago

        // El expediente quedó en estado inicial con su primer hito del timeline.
        Assert.Equal(CaseStatus.Radicado, result.Case.Status);
        Assert.Equal("Radicado", result.Case.CurrentStage);
        Assert.Contains(result.Case.Timeline, t => t.Id == "radicado" && t.State == "current");

        // Rastro forense append-only del primer estado (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-radicado", evt.Action);
        Assert.Equal(result.Case.Radicado, evt.Resource);
    }

    [Fact] // happy: trámite gratuito radica SIN sesión de pago (paso de pago OFF)
    public async Task Radicar_FreeTramite_NoPaymentSession()
    {
        var result = await Make().RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());

        Assert.Equal(0m, result.Case.FeeMinor);
        Assert.Null(result.PaymentSessionId);
    }

    [Fact] // no-idempotente POR DISEÑO: cada radicación crea un expediente nuevo
    public async Task Radicar_Twice_CreatesTwoCases()
    {
        var svc = Make();
        var first = await svc.RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());
        var second = await svc.RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());

        Assert.NotEqual(first.Case.CaseId, second.Case.CaseId);
        Assert.NotEqual(first.Case.Radicado, second.Case.Radicado);
    }

    [Fact] // seed: expedientes sembrados en varios estados (demo del ciclo de vida)
    public async Task Seed_CoversAllLifecycleStates()
    {
        var cases = Make().ListCases();

        Assert.True(cases.Count >= 5);
        foreach (var status in Enum.GetValues<CaseStatus>())
        {
            Assert.Contains(cases, c => c.Status == status);
        }

        // El expediente se resuelve por caseId Y por número de radicado.
        var svc = Make();
        Assert.NotNull(svc.FindCase("case-1003"));
        Assert.NotNull(svc.FindCase("SG-2026-001003"));
        Assert.Equal(svc.FindCase("case-1003")!.CaseId, svc.FindCase("SG-2026-001003")!.CaseId);
        await Task.CompletedTask;
    }

    [Fact] // durabilidad (T3): el expediente radicado sobrevive el reinicio del proceso
    public async Task Case_SurvivesServiceReplacement_ViaSharedStore()
    {
        // Un store compartido entre dos instancias del motor es el proxy de un reinicio del
        // CMS: la instancia nueva arranca sin nada en memoria y solo tiene el store.
        var store = new InMemoryJsonEntityStore();

        var radicado = await Make(store: store)
            .RadicarAsync("trm-pasaporte", PasaporteForm(), Citizen());

        // ── REINICIO ──
        var restarted = Make(store: store);

        // El expediente se resuelve por caseId Y por radicado en la instancia nueva…
        var byId = restarted.FindCase(radicado.Case.CaseId);
        Assert.NotNull(byId);
        Assert.Equal(radicado.Case.Radicado, byId!.Radicado);
        Assert.Equal(CaseStatus.Radicado, byId.Status);
        Assert.Equal(189_000m, byId.FeeMinor);
        Assert.NotNull(restarted.FindCase(radicado.Case.Radicado));

        // …el lookup sigue siendo case-insensitive tras el round-trip por el store…
        Assert.NotNull(restarted.FindCase(radicado.Case.CaseId.ToUpperInvariant()));

        // …y la operación siguiente (decidir) funciona sobre el estado rehidratado.
        restarted.ApplyDecision(
            radicado.Case.CaseId, CaseStatus.EnRevision, "funcionario@entidad.gov.co", "Asignado.", Clock, null);
        Assert.Equal(CaseStatus.EnRevision, restarted.FindCase(radicado.Case.CaseId)!.Status);

        // El seed NO pisa el expediente sembrado que ya fue mutado (re-seed idempotente).
        var reseeded = Make(store: store);
        Assert.Equal(CaseStatus.EnRevision, reseeded.FindCase(radicado.Case.CaseId)!.Status);

        // Y la secuencia del radicado NO reinicia: el radicado siguiente no colisiona.
        var next = await reseeded.RadicarAsync("trm-pasaporte", PasaporteForm(), Citizen());
        Assert.NotEqual(radicado.Case.Radicado, next.Case.Radicado);
    }
}
