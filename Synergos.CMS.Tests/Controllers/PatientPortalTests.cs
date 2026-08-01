using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cubre <c>GET /api/healthcare/me</c>, la puerta del portal del paciente (ADR 0120).
/// </summary>
/// <remarks>
/// <para>El inventario funcional decía "no hay portal del paciente" y apuntaba al guard. El
/// guard ya estaba arreglado: un paciente PUEDE leer lo suyo. Lo que faltaba era más simple y
/// más difícil de ver — <b>todos los endpoints se direccionan por <c>patientKey</c>, que es
/// una clave clínica distinta del <c>MemberKey</c>, y nadie se la dice al paciente</b>. El
/// permiso estaba concedido y era inalcanzable: había que adivinar un GUID.</para>
///
/// <para>Lo que estas pruebas protegen no es que el endpoint devuelva datos: es el ORDEN de
/// sus tres operaciones. Cortar al anónimo antes de tocar PHI, resolver la clave SOLO desde la
/// sesión, y pasar igual por el guard para que quede el rastro de auditoría.</para>
/// </remarks>
public sealed class PatientPortalTests
{
    private readonly IPhiAccessGuard _guard = Substitute.For<IPhiAccessGuard>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IAppointmentScheduler _appointments = Substitute.For<IAppointmentScheduler>();
    private readonly IPrescriptionService _prescriptions = Substitute.For<IPrescriptionService>();
    private readonly IConsentLedger _consent = Substitute.For<IConsentLedger>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private HealthcareApiController BuildSut() =>
        new(_guard, _patients, _appointments, _prescriptions, _consent, _gate);

    private void SignedInAs(Guid memberKey)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(memberKey);
    }

    private void GuardReturns(bool allowed, string? reason = null) =>
        _guard.CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AccessDecision(allowed, reason, "audit-id"));

    private static PatientRecord Record(Guid patientKey, Guid memberKey) => new(
        PatientKey: patientKey,
        MemberKey: memberKey,
        DisplayName: "Camila Restrepo",
        DateOfBirth: new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ChiefComplaint: "Control anual",
        FindingsNotes: "Sin hallazgos",
        AssessmentNotes: "Paciente estable",
        CreatedAtUtc: DateTime.UtcNow,
        CreatedByDoctorKey: Guid.NewGuid(),
        VersionNumber: 1,
        IsDeleted: false);

    [Fact]
    public async Task Un_anonimo_recibe_401_y_NO_toca_el_repositorio()
    {
        // La propiedad que el resto del controller ya fija: no se consulta PHI antes de
        // autorizar. Aquí importa el doble, porque este endpoint resuelve una clave clínica.
        _gate.IsAuthenticated.Returns(false);

        var result = await BuildSut().Me(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await _patients.DidNotReceive().FindKeyByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _patients.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Una_sesion_sin_clave_de_miembro_tambien_se_corta()
    {
        // IsAuthenticated true con CurrentMemberKey null es un estado posible del gate.
        // Sin este corte, Guid.Empty entraría al repositorio como si fuera un miembro.
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns((Guid?)null);

        var result = await BuildSut().Me(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        await _patients.DidNotReceive().FindKeyByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_clave_se_resuelve_SOLO_desde_la_sesion()
    {
        // No hay parámetro donde escribir un miembro ajeno: la clave sale del gate y de nada
        // más. Es lo que hace que este endpoint no necesite defenderse de un IDOR.
        var memberKey = Guid.NewGuid();
        var patientKey = Guid.NewGuid();
        SignedInAs(memberKey);
        _patients.FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>()).Returns(patientKey);
        _patients.GetAsync(patientKey, Arg.Any<CancellationToken>()).Returns(Record(patientKey, memberKey));
        GuardReturns(true);

        await BuildSut().Me(CancellationToken.None);

        await _patients.Received(1).FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task El_acceso_pasa_por_el_guard_aunque_sea_su_propio_expediente()
    {
        // Saltarse el guard "porque es suyo" dejaría sin rastro de auditoría justo el acceso
        // que más lo necesita. El guard es quien audita, no solo quien decide.
        var memberKey = Guid.NewGuid();
        var patientKey = Guid.NewGuid();
        SignedInAs(memberKey);
        _patients.FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>()).Returns(patientKey);
        _patients.GetAsync(patientKey, Arg.Any<CancellationToken>()).Returns(Record(patientKey, memberKey));
        GuardReturns(true);

        await BuildSut().Me(CancellationToken.None);

        await _guard.Received(1).CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r =>
                r.ResourceType == "patient-record"
                && r.Action == "read"
                && r.TargetPatientKey == patientKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_permiso_devuelve_el_expediente()
    {
        var memberKey = Guid.NewGuid();
        var patientKey = Guid.NewGuid();
        SignedInAs(memberKey);
        _patients.FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>()).Returns(patientKey);
        _patients.GetAsync(patientKey, Arg.Any<CancellationToken>()).Returns(Record(patientKey, memberKey));
        GuardReturns(true);

        var result = await BuildSut().Me(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(patientKey, Assert.IsType<PatientRecord>(ok.Value).PatientKey);
    }

    [Fact]
    public async Task Si_el_guard_niega_NO_se_lee_el_expediente()
    {
        // Fail-closed: que la clave sea suya no basta si el guard dice que no. Es la única
        // forma de que una futura regla (una suspensión, un menor de edad) tenga efecto.
        var memberKey = Guid.NewGuid();
        var patientKey = Guid.NewGuid();
        SignedInAs(memberKey);
        _patients.FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>()).Returns(patientKey);
        GuardReturns(false, "insufficient-role");

        var result = await BuildSut().Me(CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        await _patients.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_miembro_sin_expediente_recibe_404_sin_llegar_al_guard()
    {
        // No hay nada que auditar: no se llegó a nombrar ningún recurso clínico.
        SignedInAs(Guid.NewGuid());
        _patients.FindKeyByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var result = await BuildSut().Me(CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _guard.DidNotReceive().CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Una_clave_que_apunta_a_nada_devuelve_404_y_no_un_cuerpo_vacio()
    {
        // El vínculo existe pero el expediente ya no: dato inconsistente. 404 es honesto;
        // un 200 con null obligaría a cada cliente a distinguir "sin datos" de "sin permiso".
        var memberKey = Guid.NewGuid();
        var patientKey = Guid.NewGuid();
        SignedInAs(memberKey);
        _patients.FindKeyByMemberAsync(memberKey, Arg.Any<CancellationToken>()).Returns(patientKey);
        _patients.GetAsync(patientKey, Arg.Any<CancellationToken>()).Returns((PatientRecord?)null);
        GuardReturns(true);

        Assert.IsType<NotFoundResult>(await BuildSut().Me(CancellationToken.None));
    }
}
