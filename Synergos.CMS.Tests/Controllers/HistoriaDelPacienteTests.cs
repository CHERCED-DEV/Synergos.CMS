using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Salud;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El pre-registro, el resumen y la validación por <c>/api/healthcare</c> (003-salud-cita, I2 T058–T060). Lo que se
/// protege es el orden de siempre del portal: la historia sale de la SESIÓN, la guardia decide y audita antes de tocar
/// nada, y lo que el paciente escribe es sólo lo declarado. El repositorio es el de verdad, sobre un almacén en memoria.
/// </summary>
public sealed class HistoriaDelPacienteTests
{
    private readonly IPhiAccessGuard _guard = Substitute.For<IPhiAccessGuard>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly InMemoryPhiStore _store = new();
    private readonly FileSystemPatientRepository _patients;

    private static readonly Guid Miembro = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Medico = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public HistoriaDelPacienteTests() => _patients = new FileSystemPatientRepository(_store);

    private HealthcareApiController BuildSut() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Substitute.For<IPrescriptionService>(),
        Substitute.For<IConsentLedger>(), _gate, new EditorDeLaHistoriaClinica(_patients));

    private void SesionDe(Guid miembro)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(miembro);
    }

    private void GuardiaDice(bool pasa) =>
        _guard.CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AccessDecision(pasa, pasa ? null : "insufficient-role", "audit-id"));

    private async Task<Guid> HistoriaDe(Guid miembro) => await _patients.UpsertAsync(
        new PatientRecord(Guid.Empty, miembro, "Camila Restrepo", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "Control anual", "Sin hallazgos", "Paciente estable", default, Medico, 0, false),
        CancellationToken.None);

    private static readonly CheckInDelPaciente UnCheckIn = new(
        Documento: "CC 52.123.456", Aseguradora: "EPS Sura", Alergias: new[] { "Penicilina" });

    [Fact]
    public async Task CheckIn_Un_anonimo_recibe_401_y_no_toca_el_repositorio_ni_la_guardia()
    {
        var repo = Substitute.For<IPatientRepository>();
        _gate.IsAuthenticated.Returns(false);
        var sut = new HealthcareApiController(_guard, repo, Substitute.For<IAppointmentScheduler>(),
            Substitute.For<IPrescriptionService>(), Substitute.For<IConsentLedger>(), _gate);

        Assert.IsType<UnauthorizedResult>(await sut.CheckIn(UnCheckIn, CancellationToken.None));
        await repo.DidNotReceive().FindKeyByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _guard.DidNotReceive().CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckIn_Escribe_lo_declarado_en_la_historia_de_la_SESION_y_la_guardia_lo_audita_como_declarar_lo_suyo()
    {
        var key = await HistoriaDe(Miembro);
        await HistoriaDe(Guid.NewGuid()); // la de otro miembro: no se toca
        SesionDe(Miembro);
        GuardiaDice(true);

        var ok = Assert.IsType<OkObjectResult>(await BuildSut().CheckIn(UnCheckIn, CancellationToken.None));

        await _guard.Received(1).CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.ResourceType == "patient-record" && r.Action == "declare"
                && r.TargetPatientKey == key && r.TargetOwnerMemberKey == Miembro && r.Proposito == "salud.check-in"),
            Arg.Any<CancellationToken>());
        var historia = await _patients.GetAsync(key, CancellationToken.None);
        Assert.Equal("CC 52.123.456", historia!.PreRegistro!.Documento);
        Assert.Equal(("Penicilina", EstadosDelDato.Declarado), (historia.Resumen!.Datos[0].Descripcion, historia.Resumen.Datos[0].Estado));
        Assert.Equal("Paciente estable", historia.AssessmentNotes);
        Assert.Equal("Penicilina", Assert.IsType<ResumenParaLeer>(ok.Value).Datos.Single().Descripcion);
    }

    [Fact]
    public async Task CheckIn_Si_la_guardia_niega_no_se_escribe_nada()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(false);

        var respuesta = await BuildSut().CheckIn(UnCheckIn, CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(respuesta).StatusCode);
        Assert.Null((await _patients.GetAsync(key, CancellationToken.None))!.PreRegistro);
    }

    [Fact]
    public async Task CheckIn_Uno_que_no_se_puede_guardar_dice_el_campo_y_no_escribe_nada()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(true);

        var respuesta = await BuildSut().CheckIn(
            new CheckInDelPaciente(Vacunas: new[] { new VacunaDeclarada("Influenza", "2999-01-01") }), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(respuesta);
        Assert.Null((await _patients.GetAsync(key, CancellationToken.None))!.PreRegistro);
    }

    [Fact]
    public async Task CheckIn_Sin_historia_abierta_es_404_sin_llegar_a_la_guardia()
    {
        SesionDe(Miembro);

        Assert.IsType<NotFoundResult>(await BuildSut().CheckIn(UnCheckIn, CancellationToken.None));
        await _guard.DidNotReceive().CheckAccessAsync(Arg.Any<AccessCheckRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MiResumen_Lee_el_de_la_sesion_con_lo_declarado_como_pendiente()
    {
        await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(true);
        await BuildSut().CheckIn(UnCheckIn, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(await BuildSut().MiResumen(CancellationToken.None));

        var leido = Assert.IsType<ResumenParaLeer>(ok.Value);
        Assert.True(leido.HayResumen);
        Assert.Equal(EstadosDelDato.Declarado, leido.Datos.Single().Estado);
        await _guard.Received().CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.Action == "read" && r.TargetOwnerMemberKey == Miembro && r.Proposito == "salud.resumen"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidarDato_El_medico_lo_deja_validado_con_el_proposito_de_la_historia()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(true);
        await BuildSut().CheckIn(UnCheckIn, CancellationToken.None);
        var datoId = (await _patients.GetAsync(key, CancellationToken.None))!.Resumen!.Datos.Single().Id;
        SesionDe(Medico);

        var ok = Assert.IsType<OkObjectResult>(await BuildSut().ValidarDato(key, datoId, CancellationToken.None));

        Assert.Equal(EstadosDelDato.Validado, Assert.IsType<ResumenParaLeer>(ok.Value).Datos.Single().Estado);
        var dato = (await _patients.GetAsync(key, CancellationToken.None))!.Resumen!.Datos.Single();
        Assert.Equal((Guid?)Medico, dato.ValidadoPor);
        await _guard.Received().CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.Action == "write" && r.TargetPatientKey == key && r.Proposito == "salud.historia"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidarDato_Uno_que_no_esta_es_404_y_si_la_guardia_niega_no_se_toca()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        Assert.IsType<NotFoundResult>(await BuildSut().ValidarDato(key, "no-esta", CancellationToken.None));

        GuardiaDice(false);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await BuildSut().ValidarDato(key, "no-esta", CancellationToken.None)).StatusCode);
        Assert.Equal(1, (await _patients.GetAsync(key, CancellationToken.None))!.VersionNumber);
    }

    [Fact]
    public async Task UpsertPatient_Una_historia_mandada_sin_resumen_no_le_borra_el_resumen()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(true);
        await BuildSut().CheckIn(UnCheckIn, CancellationToken.None);
        var sinResumen = (await _patients.GetAsync(key, CancellationToken.None))! with
        {
            Resumen = null,
            PreRegistro = null,
            AssessmentNotes = "Control en tres meses",
        };
        SesionDe(Medico);

        await BuildSut().UpsertPatient(sinResumen, CancellationToken.None);

        var historia = await _patients.GetAsync(key, CancellationToken.None);
        Assert.Equal("Control en tres meses", historia!.AssessmentNotes);
        Assert.Equal("Penicilina", historia.Resumen!.Datos.Single().Descripcion);
        Assert.Equal("EPS Sura", historia.PreRegistro!.Aseguradora);
    }

    private sealed class InMemoryPhiStore : IPhiStore
    {
        private readonly Dictionary<string, string> _data = new();
        private static string K(string rt, Guid key) => rt + "/" + key.ToString("N");

        public Task WriteAsync(string resourceType, Guid key, string json, CancellationToken ct)
        {
            _data[K(resourceType, key)] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.TryGetValue(K(resourceType, key), out var v) ? v : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value).ToList());

        public Task<IReadOnlyList<KeyValuePair<Guid, string>>> ListEntriesAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<KeyValuePair<Guid, string>>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => new KeyValuePair<Guid, string>(Guid.Parse(kv.Key[(resourceType.Length + 1)..]), kv.Value))
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
