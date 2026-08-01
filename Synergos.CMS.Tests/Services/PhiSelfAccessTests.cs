using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el auto-acceso del paciente a su propio PHI — la rama que ADR 0098
/// documenta como parte del diseño y que estaba MUERTA.
/// </summary>
/// <remarks>
/// <para>El único llamante (<c>HealthcareApiController</c>) construía el
/// <see cref="AccessCheckRequest"/> con tres argumentos posicionales, así que
/// <c>TargetOwnerMemberKey</c> quedaba <c>null</c> siempre: ningún paciente
/// podía leer su expediente, sus citas ni sus recetas. Todo caía a la rama de
/// roles y devolvía 403. El portal del paciente no existía.</para>
///
/// <para>El test que cubría esa rama (<c>DefaultPhiAccessGuardTests</c>) la
/// probaba en AISLAMIENTO, poblando el campo a mano — nunca a través del
/// camino real. De ahí que el hueco sobreviviera con la suite en verde.</para>
///
/// <para>Estos tests atacan lo que faltaba: que el guard resuelva el dueño por
/// sí mismo a partir de la clave clínica (<c>PatientKey ≠ MemberKey</c>), que
/// el auto-acceso sea SÓLO de lectura, y que un anónimo no provoque ni una
/// lectura del repositorio.</para>
/// </remarks>
public sealed class PhiSelfAccessTests
{
    private static readonly Guid PacienteMember = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid PacienteClinico = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid OtroMember = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IConsentLedger _consent = Substitute.For<IConsentLedger>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();

    private DefaultPhiAccessGuard BuildSut() => new(
        _gate, _consent, _audit,
        NullLogger<DefaultPhiAccessGuard>.Instance,
        _patients);

    private void SignedInAs(Guid? memberKey)
    {
        _gate.IsAuthenticated.Returns(memberKey is not null);
        _gate.CurrentMemberKey.Returns(memberKey);
        // Sin roles: lo que se prueba es el auto-acceso, no la rama clínica.
        _gate.HasAnyRole(Arg.Any<string>()).Returns(false);
    }

    private void PatientOwnedBy(Guid ownerMemberKey) =>
        _patients.GetAsync(PacienteClinico, Arg.Any<CancellationToken>())
            .Returns(new PatientRecord(
                PatientKey: PacienteClinico,
                MemberKey: ownerMemberKey,
                DisplayName: "Ana Torres",
                DateOfBirth: new DateTime(1990, 5, 3),
                ChiefComplaint: null,
                FindingsNotes: null,
                AssessmentNotes: null,
                CreatedAtUtc: DateTime.UtcNow,
                CreatedByDoctorKey: Guid.NewGuid(),
                VersionNumber: 1,
                IsDeleted: false));

    [Fact]
    public async Task El_paciente_SI_puede_leer_lo_suyo_aunque_el_llamante_no_pase_el_dueno()
    {
        // El corazón del arreglo: el guard resuelve el dueño por la clave
        // clínica. PatientKey ≠ MemberKey, así que no vale reusar una por otra.
        SignedInAs(PacienteMember);
        PatientOwnedBy(PacienteMember);

        var decision = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", PacienteClinico),
            CancellationToken.None);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public async Task El_paciente_NO_puede_escribir_lo_suyo()
    {
        // Sin esto, arreglar el auto-acceso habría abierto un agujero peor que
        // el que cerraba: FindingsNotes y AssessmentNotes son campos de
        // PatientRecord. Un paciente tiene derecho a VER su historia; el
        // criterio clínico lo firma quien lo emite.
        SignedInAs(PacienteMember);
        PatientOwnedBy(PacienteMember);

        var decision = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "write", PacienteClinico),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public async Task Otro_member_NO_entra_al_expediente_ajeno()
    {
        SignedInAs(OtroMember);
        PatientOwnedBy(PacienteMember);

        var decision = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", PacienteClinico),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public async Task Un_anonimo_no_provoca_NI_UNA_lectura_del_repositorio()
    {
        // La propiedad "no se toca PHI antes de autorizar", que
        // HealthcareApiControllerTests.GetPatient_Anonymous_Returns401 exige del
        // controller. Por eso el dueño lo resuelve el guard DESPUÉS del chequeo
        // de sesión, y no el controller antes de llamarlo.
        SignedInAs(null);

        var decision = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", PacienteClinico),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Equal("not-authenticated", decision.DenyReason);
        await _patients.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task Un_paciente_que_no_existe_no_abre_la_puerta()
    {
        // GetAsync devuelve null → dueño null → no hay auto-acceso. Fail-closed.
        SignedInAs(PacienteMember);
        _patients.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((PatientRecord?)null);

        var decision = await BuildSut().CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", PacienteClinico),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public async Task Sin_repositorio_el_guard_sigue_decidiendo_como_antes()
    {
        // El parámetro es opcional a propósito: sin él, el guard no pierde
        // ninguna capacidad previa — simplemente no resuelve dueños.
        SignedInAs(PacienteMember);
        var sut = new DefaultPhiAccessGuard(
            _gate, _consent, _audit, NullLogger<DefaultPhiAccessGuard>.Instance);

        var decision = await sut.CheckAccessAsync(
            new AccessCheckRequest("patient-record", "read", PacienteClinico),
            CancellationToken.None);

        Assert.False(decision.IsAllowed);
    }
}
