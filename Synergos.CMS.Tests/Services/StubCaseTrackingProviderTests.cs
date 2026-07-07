using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCaseTrackingProvider"/> (seam <c>ICaseTrackingProvider</c>,
/// seguimiento + bandejas del portal Gobierno): empty (expediente/ciudadano desconocido) /
/// happy (expediente + estado + timeline done/current/pending; también por radicado) /
/// filter (el caso central del dominio: ciudadano ve SOLO los suyos por email; cola del
/// funcionario filtra por entidad + estado y ordena por prioridad/SLA) / idempotent
/// (leer dos veces = mismo resultado). Lee el agregado sembrado de
/// <see cref="StubApplicationService"/> por composición (DIP), sin duplicar estado.
/// ADR 0075.
/// </summary>
public class StubCaseTrackingProviderTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static (StubCaseTrackingProvider Tracking, StubApplicationService Cases) Make()
    {
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            null,
            () => Clock);
        return (new StubCaseTrackingProvider(cases), cases);
    }

    [Fact] // empty: expediente desconocido devuelve null (no lanza)
    public async Task GetCase_Unknown_ReturnsNull()
    {
        var (tracking, _) = Make();
        Assert.Null(await tracking.GetCaseAsync("case-que-no-existe"));
        Assert.Null(await tracking.GetCaseAsync(string.Empty));
    }

    [Fact] // empty: ciudadano sin expedientes ve carpeta vacía (no lanza)
    public async Task ListForCitizen_Unknown_ReturnsEmpty()
    {
        var (tracking, _) = Make();
        var inbox = await tracking.ListForCitizenAsync("nadie@correo.co");
        Assert.Empty(inbox);
    }

    [Fact] // happy: expediente sembrado trae estado + timeline done/current/pending + docs
    public async Task GetCase_Seeded_ReturnsStatusAndTimeline()
    {
        var (tracking, _) = Make();

        var detail = await tracking.GetCaseAsync("case-1003");

        Assert.NotNull(detail);
        Assert.Equal("SG-2026-001003", detail!.Radicado);
        Assert.Equal(CaseStatus.Subsanacion, detail.Status);
        Assert.Equal("Información solicitada", detail.CurrentStage);
        Assert.Single(detail.Documents);
        // Timeline tiene un hito current (el estado actual) y estados válidos.
        Assert.Contains(detail.Timeline, t => t.State == "current");
        Assert.All(detail.Timeline, t => Assert.Contains(t.State, new[] { "done", "current", "pending" }));
    }

    [Fact] // happy: el expediente también se consulta por número de radicado
    public async Task GetCase_ByRadicado_ReturnsSameCase()
    {
        var (tracking, _) = Make();

        var byId = await tracking.GetCaseAsync("case-1003");
        var byRadicado = await tracking.GetCaseAsync("SG-2026-001003");

        Assert.NotNull(byRadicado);
        Assert.Equal(byId!.CaseId, byRadicado!.CaseId);
    }

    [Fact] // filter: el ciudadano ve SOLO sus expedientes (por email)
    public async Task ListForCitizen_SeesOnlyTheirCases()
    {
        var (tracking, _) = Make();

        var inbox = await tracking.ListForCitizenAsync("maria.lopez@correo.co");

        var item = Assert.Single(inbox);
        Assert.Equal("case-1001", item.CaseId);
        Assert.Equal("María Fernanda López", item.CitizenName);
    }

    [Fact] // filter: la cola completa del funcionario ordena por prioridad y SLA
    public async Task GetQueue_NoFilters_ReturnsFullQueueOrdered()
    {
        var (tracking, _) = Make();

        var queue = await tracking.GetQueueAsync(null, null);

        Assert.Equal(6, queue.Count);
        // Orden: prioridad descendente (High primero), luego SLA ascendente.
        Assert.Equal(
            queue.OrderByDescending(i => i.Priority).ThenBy(i => i.SlaDaysLeft).Select(i => i.CaseId),
            queue.Select(i => i.CaseId));
        Assert.Equal(CasePriority.High, queue[0].Priority);
    }

    [Fact] // filter: la cola filtra por entidad y por estado (slug público)
    public async Task GetQueue_ByAgencyAndStatus_Filters()
    {
        var (tracking, _) = Make();

        var byAgency = await tracking.GetQueueAsync("Cancillería", null);
        Assert.All(byAgency, i => Assert.Equal("Expedición de pasaporte", i.TramiteName));

        var submitted = await tracking.GetQueueAsync(null, "submitted");
        Assert.NotEmpty(submitted);
        Assert.All(submitted, i => Assert.Equal(CaseStatus.Radicado, i.Status));

        var approved = await tracking.GetQueueAsync(null, "approved");
        Assert.All(approved, i => Assert.Equal(CaseStatus.Resuelto, i.Status));
    }

    [Fact] // filter: los expedientes radicados nuevos aparecen en la carpeta del ciudadano
    public async Task ListForCitizen_AfterRadicar_IncludesNewCase()
    {
        var (tracking, cases) = Make();
        var citizen = new GovCitizen("Ana Torres", "ana.torres@correo.co");
        var result = await cases.RadicarAsync(
            "trm-certificado-residencia",
            new System.Collections.Generic.Dictionary<string, string>
            {
                ["nombreCompleto"] = "Ana Torres",
                ["cedula"] = "1030567890",
                ["direccion"] = "Cll 45 # 13-25",
                ["correo"] = "ana.torres@correo.co",
            },
            citizen);

        var inbox = await tracking.ListForCitizenAsync("ana.torres@correo.co");

        var item = Assert.Single(inbox);
        Assert.Equal(result.Case.CaseId, item.CaseId);
        Assert.Equal(CaseStatus.Radicado, item.Status);
    }

    [Fact] // idempotent: leer la cola dos veces devuelve lo mismo
    public async Task GetQueue_Twice_IsIdempotent()
    {
        var (tracking, _) = Make();

        var first = await tracking.GetQueueAsync(null, null);
        var second = await tracking.GetQueueAsync(null, null);

        Assert.Equal(first.Select(i => i.CaseId), second.Select(i => i.CaseId));
    }
}
