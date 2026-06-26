using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests para <see cref="HealthcareApiController"/> (ADR 0098 H2d). Verifica que
/// el guard se invoca PRIMERO (anónimo → 401, denegado → 403, permitido → 200) y
/// la frontera RECORD-KEEPER (sin verbos clínicos en los endpoints).
/// </summary>
public sealed class HealthcareApiControllerTests
{
    private readonly IPhiAccessGuard _guard = Substitute.For<IPhiAccessGuard>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IAppointmentScheduler _appointments = Substitute.For<IAppointmentScheduler>();
    private readonly IPrescriptionService _prescriptions = Substitute.For<IPrescriptionService>();
    private readonly IConsentLedger _consent = Substitute.For<IConsentLedger>();

    private HealthcareApiController BuildSut() =>
        new(_guard, _patients, _appointments, _prescriptions, _consent);

    private void GuardReturns(bool allowed, string? reason) =>
        _guard.CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AccessDecision(allowed, reason, "audit-id"));

    [Fact]
    public async Task GetPatient_Anonymous_Returns401()
    {
        GuardReturns(false, "not-authenticated");

        var result = await BuildSut().GetPatient(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await _patients.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPatient_Denied_Returns403()
    {
        GuardReturns(false, "no-active-consent");

        var result = await BuildSut().GetPatient(Guid.NewGuid(), CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
        await _patients.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPatient_Allowed_Returns200_AfterGuard()
    {
        GuardReturns(true, null);
        var key = Guid.NewGuid();
        var record = new PatientRecord(key, Guid.NewGuid(), "Ana", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, null, DateTime.UtcNow, Guid.NewGuid(), 1, false);
        _patients.GetAsync(key, Arg.Any<CancellationToken>()).Returns(record);

        var result = await BuildSut().GetPatient(key, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(record, ok.Value);
    }

    [Fact]
    public async Task BookAppointment_Allowed_Conflict_Returns409()
    {
        GuardReturns(true, null);
        _appointments.BookAsync(Arg.Any<AppointmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AppointmentBookResult(false, null, "overbooking-conflict"));

        var req = new AppointmentRequest(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1), Guid.NewGuid());
        var result = await BuildSut().BookAppointment(req, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public void HealthcareController_HasNoClinicalVerbs()
    {
        var forbidden = new[] { "suggest", "diagnos", "recommend", "advise", "advice", "contraindic", "interaction" };
        var methods = typeof(HealthcareApiController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name.ToLowerInvariant());

        foreach (var name in methods)
        {
            foreach (var verb in forbidden)
            {
                Assert.False(name.Contains(verb, StringComparison.Ordinal),
                    $"Endpoint con verbo clínico prohibido: '{name}' (frontera RECORD-KEEPER, ADR 0098).");
            }
        }
    }
}
