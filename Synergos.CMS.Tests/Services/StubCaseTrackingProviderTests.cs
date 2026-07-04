using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCaseTrackingProvider"/> (seam <c>ICaseTrackingProvider</c>,
/// seguimiento + bandejas del portal Gobierno): empty (expediente/actor desconocido) /
/// happy (expediente + estado + timeline; también por radicado) / filter (el caso
/// central del dominio: ciudadano ve SOLO los suyos por email, funcionario/admin ve
/// toda la cola, orden por fecha de radicación desc) / idempotent (leer dos veces =
/// mismo resultado). Lee el agregado sembrado de <see cref="StubApplicationService"/>
/// por composición (DIP), sin duplicar estado. ADR 0075.
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

    [Fact] // empty: ciudadano sin expedientes ve bandeja vacía (no lanza)
    public async Task GetInbox_UnknownCitizen_ReturnsEmpty()
    {
        var (tracking, _) = Make();
        var inbox = await tracking.GetInboxAsync("nadie@correo.co", "citizen");
        Assert.Empty(inbox);
    }

    [Fact] // happy: expediente sembrado trae estado + timeline + documentos
    public async Task GetCase_Seeded_ReturnsStatusAndTimeline()
    {
        var (tracking, _) = Make();

        var detail = await tracking.GetCaseAsync("case-1003");

        Assert.NotNull(detail);
        Assert.Equal("RAD-2026-1003", detail!.Radicado);
        Assert.Equal(CaseStatus.Subsanacion, detail.Status);
        Assert.Equal(3, detail.Timeline.Count); // radicado → en-revisión → subsanación
        Assert.Single(detail.Documents);
        // El timeline es el espejo del ciclo: el último evento es el estado actual.
        Assert.Equal(detail.Status, detail.Timeline[^1].Status);
    }

    [Fact] // happy: el expediente también se consulta por número de radicado
    public async Task GetCase_ByRadicado_ReturnsSameCase()
    {
        var (tracking, _) = Make();

        var byId = await tracking.GetCaseAsync("case-1003");
        var byRadicado = await tracking.GetCaseAsync("RAD-2026-1003");

        Assert.NotNull(byRadicado);
        Assert.Equal(byId!.CaseId, byRadicado!.CaseId);
    }

    [Fact] // filter: el ciudadano ve SOLO sus expedientes (por email)
    public async Task GetInbox_Citizen_SeesOnlyTheirCases()
    {
        var (tracking, _) = Make();

        var inbox = await tracking.GetInboxAsync("maria.lopez@correo.co", "citizen");

        var item = Assert.Single(inbox);
        Assert.Equal("case-1001", item.CaseId);
        Assert.Equal("María Fernanda López", item.CitizenName);
    }

    [Fact] // filter: funcionario/admin ven toda la cola, ordenada por radicación desc
    public async Task GetInbox_Officer_SeesFullQueueOrdered()
    {
        var (tracking, _) = Make();

        var officer = await tracking.GetInboxAsync("funcionario@entidad.gov.co", "officer");
        var admin = await tracking.GetInboxAsync("root@entidad.gov.co", "admin");

        Assert.Equal(5, officer.Count);
        Assert.Equal(officer.Select(i => i.CaseId), admin.Select(i => i.CaseId));
        // Orden: fecha de radicación descendente (el más reciente primero).
        Assert.Equal(
            officer.OrderByDescending(i => i.RadicadoAt).Select(i => i.CaseId),
            officer.Select(i => i.CaseId));
        Assert.Equal("case-1001", officer[0].CaseId);
    }

    [Fact] // filter: los expedientes radicados nuevos aparecen en la bandeja del ciudadano
    public async Task GetInbox_AfterRadicar_IncludesNewCase()
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

        var inbox = await tracking.GetInboxAsync("ana.torres@correo.co", "citizen");

        var item = Assert.Single(inbox);
        Assert.Equal(result.CaseId, item.CaseId);
        Assert.Equal(CaseStatus.Radicado, item.Status);
    }

    [Fact] // idempotent: leer la bandeja dos veces devuelve lo mismo
    public async Task GetInbox_Twice_IsIdempotent()
    {
        var (tracking, _) = Make();

        var first = await tracking.GetInboxAsync("x", "officer");
        var second = await tracking.GetInboxAsync("x", "officer");

        Assert.Equal(first.Select(i => i.CaseId), second.Select(i => i.CaseId));
    }
}
