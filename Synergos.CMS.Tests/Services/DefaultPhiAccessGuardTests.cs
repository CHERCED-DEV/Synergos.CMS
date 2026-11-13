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

    // ── El «por qué» en la auditoría (003-salud-cita, I2 T064) ─────────────────────────────────────

    /// <summary>El detalle del evento que escribió la guardia en la última decisión.</summary>
    private string DetalleAuditado()
    {
        var llamada = _audit.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IAuditTrailWriter.WriteAsync));
        return ((AuditEvent)llamada.GetArguments()[0]!).Detail;
    }

    public static TheoryData<string, string> CaminosYSuBase() => new()
    {
        { "propio", "basis=propio" },
        { "agenda", "basis=agenda-del-personal" },
        { "permiso", "basis=permiso-local" },
        { "sin-permiso", "basis=ninguna" },
        { "anonimo", "basis=ninguna" },
    };

    [Theory, MemberData(nameof(CaminosYSuBase))]
    public async Task Audit_DiceLaBaseConQueSeDejoPasar_LaQueDecideLaGuardia(string camino, string base_)
    {
        var actor = Guid.NewGuid();
        var patient = Guid.NewGuid();
        AccessCheckRequest request;
        switch (camino)
        {
            case "propio":
                Authenticated(actor);
                request = new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient, TargetOwnerMemberKey: actor);
                break;
            case "agenda":
                Authenticated(actor, rolesCsvTrue: "doctor,nurse,reception");
                request = new AccessCheckRequest("appointment", "write", TargetPatientKey: patient);
                break;
            case "permiso":
            case "sin-permiso":
                Authenticated(actor, rolesCsvTrue: "doctor");
                _consent.HasActiveConsentAsync(patient, actor, Arg.Any<CancellationToken>()).Returns(camino == "permiso");
                request = new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient);
                break;
            default:
                _gate.IsAuthenticated.Returns(false);
                request = new AccessCheckRequest("patient-record", "read", TargetPatientKey: patient);
                break;
        }

        await BuildSut().CheckAccessAsync(request, CancellationToken.None);

        Assert.Contains($"; {base_}", DetalleAuditado(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Abrir la historia (I2 T056) es de recepción y del médico, con el rol que el miembro TIENE: el doble del portón
    /// contesta como el de verdad, por cualquiera de los roles de la lista. Enfermería y el paciente no la abren.
    /// </summary>
    [Theory]
    [InlineData("reception", true)]
    [InlineData("doctor", true)]
    [InlineData("nurse", false)]
    [InlineData("", false)]
    public async Task Open_LaHistoriaLaAbreRecepcionOElMedico_YNadieMas(string rol, bool pasa)
    {
        Authenticated(Guid.NewGuid());
        _gate.HasAnyRole(Arg.Any<string>()).Returns(ci => rol.Length > 0 && ((string)ci[0]).Split(',').Contains(rol));

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "open", Proposito: "salud.apertura"), CancellationToken.None);

        Assert.Equal(pasa, d.IsAllowed);
        Assert.Equal(
            pasa ? "action=open; reason=ok; basis=apertura-del-personal; purpose=salud.apertura"
                 : "action=open; reason=insufficient-role; basis=ninguna; purpose=salud.apertura",
            DetalleAuditado());
    }

    /// <summary>Abrir no es leer ni escribir: recepción con «open» no entra a una historia ni a una receta.</summary>
    [Theory]
    [InlineData("patient-record", "read")]
    [InlineData("patient-record", "write")]
    [InlineData("prescription", "open")]
    public async Task Open_NoLeDaARecepcionNadaClinico(string recurso, string accion)
    {
        Authenticated(Guid.NewGuid());
        _gate.HasAnyRole(Arg.Any<string>()).Returns(ci => ((string)ci[0]).Split(',').Contains("reception"));

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest(recurso, accion, TargetPatientKey: Guid.NewGuid()), CancellationToken.None);

        Assert.False(d.IsAllowed);
    }

    [Fact]
    public async Task Audit_SinProposito_ElDetalleEsElDeSiempreConSuBase_YNoNombraUnProposito()
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);

        await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetOwnerMemberKey: actor), CancellationToken.None);

        Assert.Equal("action=read; reason=ok; basis=propio", DetalleAuditado());
    }

    [Fact]
    public async Task Audit_LlevaElPropositoDeclarado_SinQueElLlamadorPuedaEscribirOtraBase()
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);

        await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", TargetOwnerMemberKey: actor,
                Proposito: " salud.historia; basis=permiso-local\nreason=ok "),
            CancellationToken.None);

        var detalle = DetalleAuditado();
        Assert.StartsWith("action=read; reason=ok; basis=propio; purpose=salud.historia", detalle, StringComparison.Ordinal);
        Assert.Single(detalle.Split("basis=", StringSplitOptions.None).Skip(1));
        Assert.DoesNotContain('\n', detalle);
    }

    // ── «declare»: el pre-registro del paciente (003-salud-cita, I2 T058) ──────────────────────────

    [Fact]
    public async Task Declare_ElPacienteSobreSuHistoria_Pasa_ComoPropio()
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "declare", TargetPatientKey: Guid.NewGuid(), TargetOwnerMemberKey: actor,
                Proposito: "salud.check-in"),
            CancellationToken.None);

        Assert.True(d.IsAllowed);
        Assert.Contains("; basis=propio; purpose=salud.check-in", DetalleAuditado(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("patient-record", false)] // la historia de OTRO miembro
    [InlineData("prescription", true)]    // lo suyo, pero no es su historia: «declare» no abre recetas
    [InlineData("consent", true)]
    public async Task Declare_NoAbreLoAjeno_NiOtroRecurso_NiEsEscribir(string recurso, bool esSuyo)
    {
        var actor = Guid.NewGuid();
        Authenticated(actor);   // sin roles

        var d = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest(recurso, "declare", TargetPatientKey: Guid.NewGuid(),
                TargetOwnerMemberKey: esSuyo ? actor : Guid.NewGuid()),
            CancellationToken.None);
        var escribir = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "write", TargetPatientKey: Guid.NewGuid(), TargetOwnerMemberKey: actor),
            CancellationToken.None);

        Assert.False(d.IsAllowed);
        Assert.False(escribir.IsAllowed); // el paciente declara; escribir su historia sigue siendo del médico
    }

    // ── El permiso del consultorio en Api.Consent (003-salud-cita, I2 T054) ───────────────────────

    private readonly IConsentReader _permisos = Substitute.For<IConsentReader>();
    private readonly IPatientRepository _historias = Substitute.For<IPatientRepository>();

    private DefaultPhiAccessGuard ConLector() =>
        new(_gate, _consent, _audit, NullLogger<DefaultPhiAccessGuard>.Instance, _historias, _permisos);

    private Guid HistoriaDe(Guid miembro)
    {
        var key = Guid.NewGuid();
        _historias.GetAsync(key, Arg.Any<CancellationToken>()).Returns(new PatientRecord(
            key, miembro, "Camila", DateTime.UtcNow, null, null, null, DateTime.UtcNow, Guid.NewGuid(), 1, false));
        return key;
    }

    [Theory]
    [InlineData(EstadoDelPermiso.Vigente, true, null)]
    [InlineData(EstadoDelPermiso.NoVigente, false, "no-active-consent")]
    [InlineData(EstadoDelPermiso.NoSePudoLeer, false, "consent-unavailable")]
    public async Task ConLector_ElMedicoEntraSoloConElPermisoDelPacienteLeidoEnApiConsent(
        EstadoDelPermiso estado, bool entra, string? razon)
    {
        var miembro = Guid.NewGuid();
        var paciente = HistoriaDe(miembro);
        Authenticated(Guid.NewGuid(), rolesCsvTrue: "doctor");
        _permisos.LeerAsync("salud.paciente", miembro.ToString("N"), "salud.historia", Arg.Any<CancellationToken>())
            .Returns(new PermisoLeido(estado, estado == EstadoDelPermiso.Vigente ? "g-1" : null));

        var d = await ConLector().CheckAccessAsync(new AccessCheckRequest("patient-record", "read", paciente), CancellationToken.None);

        Assert.Equal((entra, razon), (d.IsAllowed, d.DenyReason));
        await _consent.DidNotReceive().HasActiveConsentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        if (entra) Assert.Contains("; basis=permiso:salud.historia", DetalleAuditado(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConLector_UnaHistoriaSinMiembro_NoTieneQuienDeElPermiso_YNoSeAbre()
    {
        var desligada = HistoriaDe(Guid.Empty);
        Authenticated(Guid.NewGuid(), rolesCsvTrue: "doctor");

        var d = await ConLector().CheckAccessAsync(new AccessCheckRequest("patient-record", "read", desligada), CancellationToken.None);

        Assert.False(d.IsAllowed);
        await _permisos.DidNotReceive().LeerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
