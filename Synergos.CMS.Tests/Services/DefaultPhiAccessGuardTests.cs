using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="DefaultPhiAccessGuard"/> (ADR 0098). Verifica la
/// política (auth/pertenencia/rol/consentimiento), que TODO intento se audite,
/// y el comportamiento fail-closed cuando la auditoría falla.
/// </summary>
public sealed class DefaultPhiAccessGuardTests
{
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IConsentLedger _consent = Substitute.For<IConsentLedger>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();

    private DefaultPhiAccessGuard BuildSut() =>
        new(_gate, _consent, _audit, NullLogger<DefaultPhiAccessGuard>.Instance);

    private void Authenticated(Guid key, string? rolesCsvTrue = null)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(key);
        if (rolesCsvTrue is not null)
        {
            _gate.HasAnyRole(rolesCsvTrue).Returns(true);
        }
    }

    [Fact]
    public async Task NotAuthenticated_Denied_ButAudited()
    {
        _gate.IsAuthenticated.Returns(false);

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", Guid.NewGuid()), CancellationToken.None);

        Assert.False(d.IsAllowed);
        Assert.Equal("not-authenticated", d.DenyReason);
        await _audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SelfOwner_Allowed()
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetPatientKey: Guid.NewGuid(), TargetOwnerMemberKey: actor),
            CancellationToken.None);

        Assert.True(d.IsAllowed);
    }

    [Fact]
    public async Task Appointment_SchedulingRole_Allowed()
    {
        Authenticated(Guid.NewGuid(), rolesCsvTrue: "doctor,nurse,reception");

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("appointment", "write", TargetPatientKey: Guid.NewGuid()), CancellationToken.None);

        Assert.True(d.IsAllowed);
    }

    [Fact]
    public async Task Doctor_WithConsent_Allowed()
    {
        var doctor = Guid.NewGuid();
        var patient = Guid.NewGuid();
        Authenticated(doctor, rolesCsvTrue: "doctor");
        _consent.HasActiveConsentAsync(patient, doctor, Arg.Any<CancellationToken>()).Returns(true);

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient), CancellationToken.None);

        Assert.True(d.IsAllowed);
    }

    [Fact]
    public async Task Doctor_NoConsent_Denied()
    {
        var doctor = Guid.NewGuid();
        var patient = Guid.NewGuid();
        Authenticated(doctor, rolesCsvTrue: "doctor");
        _consent.HasActiveConsentAsync(patient, doctor, Arg.Any<CancellationToken>()).Returns(false);

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient), CancellationToken.None);

        Assert.False(d.IsAllowed);
        Assert.Equal("no-active-consent", d.DenyReason);
    }

    [Fact]
    public async Task NoRole_InsufficientRole_Denied()
    {
        Authenticated(Guid.NewGuid());   // autenticado pero sin roles

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetPatientKey: Guid.NewGuid()), CancellationToken.None);

        Assert.False(d.IsAllowed);
        Assert.Equal("insufficient-role", d.DenyReason);
    }

    [Fact]
    public async Task AuditWriteFails_FailClosed_DeniesEvenWhenPolicyAllows()
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);   // self-owner → la política permitiría
        _audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("disco caído")));

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetOwnerMemberKey: actor), CancellationToken.None);

        Assert.False(d.IsAllowed);                       // fail-closed
        Assert.Equal("audit-unavailable", d.DenyReason);
    }

    [Fact]
    public async Task EvaluationThrows_Denied_ButStillAudited()
    {
        var doctor = Guid.NewGuid();
        var patient = Guid.NewGuid();
        Authenticated(doctor, rolesCsvTrue: "doctor");
        _consent.HasActiveConsentAsync(patient, doctor, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new IOException("ledger caído")));

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient), CancellationToken.None);

        Assert.False(d.IsAllowed);                          // fail-closed ante error de evaluación
        Assert.Equal("evaluation-error", d.DenyReason);
        await _audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());   // auditó igual
    }
}
