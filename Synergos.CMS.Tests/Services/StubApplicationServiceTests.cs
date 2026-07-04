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
/// formulario dinámico exige SOLO los campos required de la definición) / idempotencia
/// (NO aplica por diseño a radicar — cada radicación crea un expediente nuevo; se
/// verifica explícitamente) + el seed de expedientes en varios estados. ADR 0075.
/// </summary>
public class StubApplicationServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static StubApplicationService Make(IAuditTrailWriter? audit = null)
        => new(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            audit,
            () => Clock);

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
        Assert.StartsWith("case_", result.CaseId);
    }

    [Fact] // happy: trámite con tasa crea expediente + abre sesión de pago + audita
    public async Task Radicar_PaidTramite_OpensPaymentSessionAndAudits()
    {
        var audit = new RecordingAuditTrailWriter();
        var svc = Make(audit);

        var result = await svc.RadicarAsync("trm-pasaporte", PasaporteForm(), Citizen());

        Assert.StartsWith("case_", result.CaseId);
        Assert.StartsWith("RAD-2026-", result.Radicado);
        Assert.Equal(189_000m, result.Fee);
        Assert.Equal("COP", result.Currency);
        Assert.NotNull(result.PaymentSessionId); // tasa > 0 ⇒ el motor abrió el pago

        // El expediente quedó en estado inicial con su primer evento del timeline.
        var detail = svc.FindCase(result.CaseId);
        Assert.NotNull(detail);
        Assert.Equal(CaseStatus.Radicado, detail!.Status);
        Assert.Single(detail.Timeline);
        Assert.Equal(CaseStatus.Radicado, detail.Timeline[0].Status);

        // Rastro forense append-only del primer estado (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-radicado", evt.Action);
        Assert.Equal(result.Radicado, evt.Resource);
    }

    [Fact] // happy: trámite gratuito radica SIN sesión de pago (paso de pago OFF)
    public async Task Radicar_FreeTramite_NoPaymentSession()
    {
        var result = await Make().RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());

        Assert.Equal(0m, result.Fee);
        Assert.Null(result.PaymentSessionId);
    }

    [Fact] // no-idempotente POR DISEÑO: cada radicación crea un expediente nuevo
    public async Task Radicar_Twice_CreatesTwoCases()
    {
        var svc = Make();
        var first = await svc.RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());
        var second = await svc.RadicarAsync("trm-certificado-residencia", CertificadoForm(), Citizen());

        Assert.NotEqual(first.CaseId, second.CaseId);
        Assert.NotEqual(first.Radicado, second.Radicado);
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
        Assert.NotNull(svc.FindCase("RAD-2026-1003"));
        Assert.Equal(svc.FindCase("case-1003")!.CaseId, svc.FindCase("RAD-2026-1003")!.CaseId);
        await Task.CompletedTask;
    }
}
