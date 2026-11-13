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

    // ── Recepción abre la historia (I2 T056) ──────────────────────────────────────

    private readonly IBuscadorDeMiembros _buscador = Substitute.For<IBuscadorDeMiembros>();
    private static readonly Guid Recepcion = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string Correo = "camila@ejemplo.co";

    private HealthcareApiController ConBuscador() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Substitute.For<IPrescriptionService>(),
        Substitute.For<IConsentLedger>(), _gate, new EditorDeLaHistoriaClinica(_patients), miembros: _buscador);

    private static AperturaDeLaHistoria Apertura(string? nombre = "Camila Restrepo", string? fecha = "1990-04-12") =>
        new(Correo, nombre, fecha);

    [Fact]
    public async Task AbrirHistoria_Si_la_guardia_niega_no_busca_el_correo_ni_crea_nada()
    {
        SesionDe(Miembro);
        GuardiaDice(false);

        var r = await ConBuscador().AbrirHistoria(Apertura(), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(r).StatusCode);
        await _buscador.DidNotReceive().ClaveDelCorreoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(await _store.ListAsync("patients", CancellationToken.None));
    }

    [Fact]
    public async Task AbrirHistoria_Crea_la_del_miembro_solo_con_lo_demografico_y_otra_vez_devuelve_la_misma()
    {
        SesionDe(Recepcion);
        GuardiaDice(true);
        _buscador.ClaveDelCorreoAsync(Correo, Arg.Any<CancellationToken>()).Returns(Miembro);

        var creada = Assert.IsType<ObjectResult>(await ConBuscador().AbrirHistoria(Apertura(), CancellationToken.None));
        var otraVez = Assert.IsType<OkObjectResult>(await ConBuscador().AbrirHistoria(Apertura("Otro Nombre"), CancellationToken.None));

        Assert.Equal(201, creada.StatusCode);
        var key = (Guid)creada.Value!.GetType().GetProperty("patientKey")!.GetValue(creada.Value)!;
        Assert.Equal(key, (Guid)otraVez.Value!.GetType().GetProperty("patientKey")!.GetValue(otraVez.Value)!);
        Assert.Equal(key, await _patients.FindKeyByMemberAsync(Miembro, CancellationToken.None));
        var historia = (await _patients.GetAsync(key, CancellationToken.None))!;
        Assert.Equal(("Camila Restrepo", new DateTime(1990, 4, 12), Recepcion, 1),
            (historia.DisplayName, historia.DateOfBirth.Date, historia.CreatedByDoctorKey, historia.VersionNumber));
        Assert.True(historia.ChiefComplaint is null && historia.FindingsNotes is null && historia.AssessmentNotes is null
            && historia.Resumen is null && historia.PreRegistro is null);
        await _guard.Received().CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.ResourceType == "patient-record" && r.Action == "open"
                && r.TargetPatientKey == null && r.Proposito == "salud.apertura"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "1990-04-12")]
    [InlineData("  ", "1990-04-12")]
    [InlineData("Camila Restrepo", null)]
    [InlineData("Camila Restrepo", "12/04/1990")]
    [InlineData("Camila Restrepo", "2999-01-01")]
    public async Task AbrirHistoria_Sin_nombre_o_con_una_fecha_que_no_sirve_es_400_y_no_se_crea(string? nombre, string? fecha)
    {
        SesionDe(Recepcion);
        GuardiaDice(true);
        _buscador.ClaveDelCorreoAsync(Correo, Arg.Any<CancellationToken>()).Returns(Miembro);

        Assert.IsType<BadRequestObjectResult>(await ConBuscador().AbrirHistoria(Apertura(nombre, fecha), CancellationToken.None));
        Assert.Null(await _patients.FindKeyByMemberAsync(Miembro, CancellationToken.None));
    }

    [Fact]
    public async Task AbrirHistoria_Un_correo_sin_miembro_es_404()
    {
        SesionDe(Recepcion);
        GuardiaDice(true);
        _buscador.ClaveDelCorreoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Guid?)null);

        Assert.IsType<NotFoundObjectResult>(await ConBuscador().AbrirHistoria(Apertura(), CancellationToken.None));
        Assert.Empty(await _store.ListAsync("patients", CancellationToken.None));
    }

    // ── «Mi salud» dice si falta el permiso del consultorio; recepción busca la historia (I2 T062–T063) ─────────

    [Theory]
    [InlineData(EstadoDelPermiso.Vigente, "vigente")]
    [InlineData(EstadoDelPermiso.NoVigente, "falta")]
    [InlineData(EstadoDelPermiso.NoSePudoLeer, "desconocido")]
    public async Task MiResumen_Dice_si_el_paciente_dio_el_permiso_de_la_historia_leido_en_Api_Consent(
        EstadoDelPermiso estado, string dice)
    {
        await HistoriaDe(Miembro);
        SesionDe(Miembro);
        GuardiaDice(true);
        var lector = Substitute.For<IConsentReader>();
        lector.LeerAsync("salud.paciente", Miembro.ToString("N"), "salud.historia", Arg.Any<CancellationToken>())
            .Returns(new PermisoLeido(estado));
        var sut = new HealthcareApiController(_guard, _patients, Substitute.For<IAppointmentScheduler>(),
            Substitute.For<IPrescriptionService>(), Substitute.For<IConsentLedger>(), _gate, permisos: lector);

        var ok = Assert.IsType<OkObjectResult>(await sut.MiResumen(CancellationToken.None));

        Assert.Equal(dice, Assert.IsType<ResumenParaLeer>(ok.Value).PermisoDeLaHistoria);
    }

    [Fact]
    public async Task BuscarHistoria_Si_la_guardia_niega_no_busca_el_correo()
    {
        SesionDe(Miembro);
        GuardiaDice(false);

        var r = await ConBuscador().BuscarHistoria(new BusquedaDeLaHistoria(Correo), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(r).StatusCode);
        await _buscador.DidNotReceive().ClaveDelCorreoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuscarHistoria_Da_la_clave_y_si_ya_hizo_el_pre_registro_y_sin_historia_ninguna_clave()
    {
        SesionDe(Recepcion);
        GuardiaDice(true);
        _buscador.ClaveDelCorreoAsync(Correo, Arg.Any<CancellationToken>()).Returns(Miembro);
        var sinHistoria = Assert.IsType<OkObjectResult>(await ConBuscador().BuscarHistoria(new BusquedaDeLaHistoria(Correo), CancellationToken.None));
        Assert.Null(sinHistoria.Value!.GetType().GetProperty("patientKey")!.GetValue(sinHistoria.Value));
        _buscador.ClaveDelCorreoAsync("nadie@ejemplo.co", Arg.Any<CancellationToken>()).Returns((Guid?)null);
        Assert.IsType<NotFoundObjectResult>(await ConBuscador().BuscarHistoria(new BusquedaDeLaHistoria("nadie@ejemplo.co"), CancellationToken.None));

        var key = await HistoriaDe(Miembro);
        var antes = Assert.IsType<OkObjectResult>(await ConBuscador().BuscarHistoria(new BusquedaDeLaHistoria(Correo), CancellationToken.None));
        SesionDe(Miembro);
        await BuildSut().CheckIn(UnCheckIn, CancellationToken.None);
        SesionDe(Recepcion);
        var despues = Assert.IsType<OkObjectResult>(await ConBuscador().BuscarHistoria(new BusquedaDeLaHistoria(Correo), CancellationToken.None));

        Assert.Equal((key, false), (Leer<Guid>(antes, "patientKey"), Leer<bool>(antes, "preRegistrado")));
        Assert.True(Leer<bool>(despues, "preRegistrado"));
        await _guard.Received().CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.Action == "open" && r.Proposito == "salud.busqueda"), Arg.Any<CancellationToken>());
    }

    private static T Leer<T>(OkObjectResult ok, string campo) => (T)ok.Value!.GetType().GetProperty(campo)!.GetValue(ok.Value)!;

    // ── La consulta (I3): la agenda del día, la llegada y la nota firmada ───────────────────────────────

    private readonly IConsultaDeSalud _consulta = Substitute.For<IConsultaDeSalud>();
    private FileSystemNotasDeLaConsulta? _notasReales;
    private FileSystemNotasDeLaConsulta Notas => _notasReales ??= new FileSystemNotasDeLaConsulta(_store);

    private HealthcareApiController EnLaConsulta() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Substitute.For<IPrescriptionService>(),
        Substitute.For<IConsentLedger>(), _gate, new EditorDeLaHistoriaClinica(_patients), consulta: _consulta, notas: Notas);

    private static readonly NotaEscrita UnaNota = new(
        Subjetivo: "Cefalea de tres días", Objetivo: "PA 120/80", Analisis: "Cefalea tensional", Plan: "Control en un mes");

    private void ElOrquestadorSella() =>
        _consulta.SellarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ResultadoDeLaConsulta(EstadoDeLaConsulta.Hecho, "sello-de-" + (string)ci[1], "llave-1"));

    [Fact]
    public async Task AgendaDelDia_Pone_el_nombre_de_la_historia_a_quien_la_tiene_y_nada_a_quien_no()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Recepcion);
        GuardiaDice(true);
        var lunes = new DateTimeOffset(2027, 3, 15, 14, 0, 0, TimeSpan.Zero);
        _consulta.AgendaAsync("dra-1", Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new CitaDelDia("r-1", lunes, lunes.AddMinutes(30), "salud.paciente", Miembro.ToString("N"), "agendada"),
                new CitaDelDia("r-2", lunes.AddHours(1), lunes.AddHours(1.5), "salud.paciente", Guid.NewGuid().ToString("N"), "llego"),
            });

        var ok = Assert.IsType<OkObjectResult>(await EnLaConsulta().AgendaDelDia("dra-1", "2027-03-15", CancellationToken.None));

        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        var citas = json.GetProperty("citas").EnumerateArray().ToList();
        Assert.Equal(("Camila Restrepo", key.ToString()), (citas[0].GetProperty("paciente").GetProperty("nombre").GetString(),
            citas[0].GetProperty("paciente").GetProperty("patientKey").GetString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, citas[1].GetProperty("paciente").GetProperty("nombre").ValueKind);
        await _guard.Received().CheckAccessAsync(
            Arg.Is<AccessCheckRequest>(r => r.ResourceType == "appointment" && r.Action == "read" && r.Proposito == "salud.agenda-del-dia"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarcarLlegada_Un_paso_que_no_existe_es_400_y_el_no_del_proceso_es_409()
    {
        SesionDe(Recepcion);
        GuardiaDice(true);
        _consulta.MarcarLlegadaAsync("r-1", "atender", Recepcion, Arg.Any<CancellationToken>())
            .Returns(new ResultadoDeLaConsulta(EstadoDeLaConsulta.Rechazado, Codigo: "workflow.transition_not_allowed"));

        Assert.IsType<BadRequestObjectResult>(await EnLaConsulta().MarcarLlegada("r-1", new PasoDeLlegada("volar"), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(await EnLaConsulta().MarcarLlegada("r-1", new PasoDeLlegada("atender"), CancellationToken.None));
    }

    [Fact]
    public async Task Nota_El_medico_la_escribe_la_firma_con_la_llave_del_consultorio_y_firmarla_otra_vez_da_lo_mismo()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        ElOrquestadorSella();

        var escrita = Assert.IsType<NotaParaLeer>(Assert.IsType<OkObjectResult>(
            await EnLaConsulta().EscribirNota(key, UnaNota, CancellationToken.None)).Value);
        var firmada = Assert.IsType<NotaParaLeer>(Assert.IsType<OkObjectResult>(
            await EnLaConsulta().FirmarNota(key, escrita.Id, CancellationToken.None)).Value);
        await EnLaConsulta().FirmarNota(key, escrita.Id, CancellationToken.None);

        Assert.True(firmada.Firmada);
        var guardada = (await Notas.GetAsync(escrita.Id, CancellationToken.None))!;
        Assert.Equal(NotaClinica.Huella(guardada), guardada.Firma!.Huella);
        Assert.Equal(("sello-de-" + guardada.Firma.Huella, "salud.consultorio"), (guardada.Firma.Sello, guardada.Firma.Llave));
        await _consulta.Received(1).SellarAsync("salud.consultorio", guardada.Firma.Huella, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nota_Firmada_no_se_cambia_y_si_alguien_la_cambia_en_el_almacen_la_firma_deja_de_valer()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        ElOrquestadorSella();
        _consulta.ComprobarSelloAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoDeLaConsulta(EstadoDeLaConsulta.Hecho, "llave-1"));
        var nota = Assert.IsType<NotaParaLeer>(Assert.IsType<OkObjectResult>(
            await EnLaConsulta().EscribirNota(key, UnaNota, CancellationToken.None)).Value);
        await EnLaConsulta().FirmarNota(key, nota.Id, CancellationToken.None);

        var cambio = await EnLaConsulta().EscribirNota(key, UnaNota with { Id = nota.Id, Plan = "Sin control" }, CancellationToken.None);
        var vale = Assert.IsType<OkObjectResult>(await EnLaConsulta().ComprobarNota(key, nota.Id, CancellationToken.None));
        var guardada = (await Notas.GetAsync(nota.Id, CancellationToken.None))!;
        await Notas.GuardarAsync(guardada with { Plan = "Sin control" }, CancellationToken.None);
        var noVale = Assert.IsType<OkObjectResult>(await EnLaConsulta().ComprobarNota(key, nota.Id, CancellationToken.None));

        Assert.IsType<ConflictObjectResult>(cambio);
        Assert.Equal("Control en un mes", guardada.Plan);
        Assert.True(Leer<bool>(vale, "valida"));
        Assert.False(Leer<bool>(noVale, "valida"));
    }

    [Fact]
    public async Task Nota_Sin_la_guardia_no_se_escribe_ni_se_firma_y_otro_medico_no_firma_la_ajena()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        ElOrquestadorSella();
        var nota = Assert.IsType<NotaParaLeer>(Assert.IsType<OkObjectResult>(
            await EnLaConsulta().EscribirNota(key, UnaNota, CancellationToken.None)).Value);

        SesionDe(Guid.NewGuid());
        var ajena = await EnLaConsulta().FirmarNota(key, nota.Id, CancellationToken.None);
        GuardiaDice(false);
        var negada = await EnLaConsulta().EscribirNota(key, UnaNota, CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(ajena).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(negada).StatusCode);
        Assert.Single(await Notas.ListAsync(key, CancellationToken.None));
        Assert.Null((await Notas.GetAsync(nota.Id, CancellationToken.None))!.Firma);
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
