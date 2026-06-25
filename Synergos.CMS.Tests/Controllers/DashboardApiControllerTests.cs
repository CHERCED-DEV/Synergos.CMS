using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests para <see cref="DashboardApiController"/> (ADR 0097 D2). Verifica el
/// auth-gate de dos capas (anónimo → 401, sin rol admin → 403, admin → 200)
/// y la emisión del evento de auditoría al servir.
/// </summary>
public sealed class DashboardApiControllerTests
{
    private readonly IDashboardReadModel _readModel = Substitute.For<IDashboardReadModel>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IAnalyticsTracker _analytics = Substitute.For<IAnalyticsTracker>();
    private readonly IMetricsExporter _exporter = Substitute.For<IMetricsExporter>();

    private DashboardApiController BuildSut() => new(_readModel, _gate, _analytics, _exporter);

    [Fact]
    public void GetSales_Anonymous_Returns401()
    {
        _gate.IsAuthenticated.Returns(false);

        var result = BuildSut().GetSales(null, null, null);

        Assert.IsType<UnauthorizedResult>(result);
        _readModel.DidNotReceive().GetSalesOverview(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>());
    }

    [Fact]
    public void GetSales_AuthenticatedNonAdmin_Returns403()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);

        var result = BuildSut().GetSales(null, null, null);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
        _readModel.DidNotReceive().GetSalesOverview(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>());
    }

    [Fact]
    public void GetSales_Admin_Returns200_AndAudits()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        var vm = new SalesOverviewVm(0m, 0, 0m, "COP", 0, 0, Array.Empty<MetricDataPoint>(), MetricGranularity.Day);
        _readModel.GetSalesOverview(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<MetricGranularity>()).Returns(vm);

        var result = BuildSut().GetSales(null, null, null);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(vm, ok.Value);
        _analytics.Received(1).Track("dashboard.viewed", Arg.Any<IReadOnlyDictionary<string, object?>>());
    }

    [Fact]
    public void GetInsights_Admin_Returns200()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        _readModel.GetAgentInsights().Returns(new AgentInsightsVm(false, "n/a"));

        var result = BuildSut().GetInsights();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetWebhooks_Anonymous_Returns401_NoAudit()
    {
        _gate.IsAuthenticated.Returns(false);

        var result = BuildSut().GetWebhooks();

        Assert.IsType<UnauthorizedResult>(result);
        _analytics.DidNotReceive().Track(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>());
    }

    [Fact]
    public void ExportCsv_Admin_ReturnsCsvFile_AndAuditsExported()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
        _exporter.ExportTotalsCsv(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns("slug,sum,count\r\n");

        var result = BuildSut().ExportCsv(null, null, null, null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        _analytics.Received(1).Track("dashboard.exported", Arg.Any<IReadOnlyDictionary<string, object?>>());
    }
}
