using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Salud;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El pre-registro, el resumen y la validación por <c>/api/healthcare</c> (003-salud-cita, I2 T058–T060). Lo que se
/// protege es el orden de siempre del portal: la historia sale de la SESIÓN, la guardia decide y audita antes de tocar
/// nada, y lo que el paciente escribe es sólo lo declarado. El repositorio es el de verdad, sobre un almacén en memoria.
/// </summary>
public sealed class HistoriaDelPacienteTests : IDisposable
{
    public void Dispose() => _mensajeria.Dispose();

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

    // ── Órdenes y resultados (I4) ───────────────────────────────────────────────────────────────────────

    private readonly ArchivosEnMemoria _archivos = new();
    private FileSystemOrdenesMedicas? _ordenesReales;
    private FileSystemOrdenesMedicas Ordenes => _ordenesReales ??= new FileSystemOrdenesMedicas(_store);

    private HealthcareApiController ConOrdenes() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Substitute.For<IPrescriptionService>(),
        Substitute.For<IConsentLedger>(), _gate, new EditorDeLaHistoriaClinica(_patients), ordenes: Ordenes, archivos: _archivos);

    private static readonly byte[] UnPdf = "%PDF-1.4 informe"u8.ToArray();

    private static ResultadoCargado HbA1c(string examen, InformeAdjunto? informe = null) =>
        new(examen, "6,2", "%", "4,0 – 5,6", "alto", "En ayunas", informe);

    [Fact]
    public async Task Orden_El_paciente_no_ve_un_resultado_hasta_que_el_medico_lo_libera_y_entonces_descarga_su_informe()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConOrdenes().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c", "Perfil lipídico"], "Control de diabetes"), CancellationToken.None)).Value);

        var cargada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConOrdenes().CargarResultado(
            key, orden.Id, HbA1c("e1", new InformeAdjunto("hba1c.pdf", "application/pdf", Convert.ToBase64String(UnPdf))),
            CancellationToken.None)).Value);
        var resultadoId = cargada.Examenes[0].Resultado!.Id;

        SesionDe(Miembro);
        var antes = Assert.IsType<List<OrdenParaLeer>>(Assert.IsType<OkObjectResult>(await ConOrdenes().MisOrdenes(CancellationToken.None)).Value);
        Assert.IsType<NotFoundResult>(await ConOrdenes().MiInforme(resultadoId, CancellationToken.None));

        SesionDe(Medico);
        var liberada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConOrdenes().LiberarResultado(key, resultadoId, CancellationToken.None)).Value);

        SesionDe(Miembro);
        var despues = Assert.IsType<List<OrdenParaLeer>>(Assert.IsType<OkObjectResult>(await ConOrdenes().MisOrdenes(CancellationToken.None)).Value);
        var informe = Assert.IsType<FileContentResult>(await ConOrdenes().MiInforme(resultadoId, CancellationToken.None));

        Assert.False(cargada.Examenes[0].Resultado!.Liberado);
        Assert.Null(Assert.Single(antes).Examenes[0].Resultado);
        Assert.Equal(("6,2", "alto", true), (despues[0].Examenes[0].Resultado!.Valor, despues[0].Examenes[0].Resultado!.Marca,
            despues[0].Examenes[0].Resultado!.Liberado));
        Assert.Equal("application/pdf", informe.ContentType);
        Assert.Equal(UnPdf, informe.FileContents);
        Assert.Equal("abierta", liberada.Estado);
    }

    [Fact]
    public async Task Orden_Con_todos_sus_examenes_liberados_queda_cumplida_y_liberar_dos_veces_da_lo_mismo()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConOrdenes().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value);
        var cargada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConOrdenes().CargarResultado(key, orden.Id, HbA1c("e1"), CancellationToken.None)).Value);
        var id = cargada.Examenes[0].Resultado!.Id;

        await ConOrdenes().LiberarResultado(key, id, CancellationToken.None);
        var otraVez = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConOrdenes().LiberarResultado(key, id, CancellationToken.None)).Value);

        Assert.Equal("cumplida", otraVez.Estado);
        Assert.Equal("cumplida", (await Ordenes.GetOrdenAsync(orden.Id, CancellationToken.None))!.Estado);
    }

    [Theory]
    [InlineData("e9", "alto", "application/pdf", true)]   // un examen que no es de la orden
    [InlineData("e1", "rarito", "application/pdf", true)] // una marca que no existe
    [InlineData("e1", "alto", "text/html", true)]         // un informe que no es PDF
    [InlineData("e1", "alto", "application/pdf", false)]  // dice PDF y no lo es
    public async Task Orden_Un_resultado_que_no_sirve_es_400_y_no_se_guarda(string examen, string marca, string tipo, bool esPdf)
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConOrdenes().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value);
        var contenido = Convert.ToBase64String(esPdf ? UnPdf : "<html>"u8.ToArray());

        var r = await ConOrdenes().CargarResultado(
            key, orden.Id, HbA1c(examen, new InformeAdjunto("x", tipo, contenido)) with { Marca = marca }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Empty(await Ordenes.ListResultadosAsync(key, CancellationToken.None));
    }

    // ── Recetas firmadas y su renovación (I5) ───────────────────────────────────────────────────────────

    private FileSystemPrescriptionService? _recetasReales;
    private FileSystemPrescriptionService Recetas => _recetasReales ??= new FileSystemPrescriptionService(_store);
    private FileSystemRenovaciones? _renovacionesReales;
    private FileSystemRenovaciones Renovaciones => _renovacionesReales ??= new FileSystemRenovaciones(_store);

    private HealthcareApiController ConRecetas() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Recetas, Substitute.For<IConsentLedger>(), _gate,
        new EditorDeLaHistoriaClinica(_patients), consulta: _consulta, renovaciones: Renovaciones);

    private static readonly RecetaEscrita Metformina = new("Metformina 850 mg", "1 tableta", "cada 12 horas", "Con las comidas", 30);

    private async Task<(Guid Key, RecetaParaLeer Receta)> UnaRecetaEmitida()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        ElOrquestadorSella();
        var receta = Assert.IsType<RecetaParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().EmitirReceta(key, Metformina, CancellationToken.None)).Value);
        return (key, receta);
    }

    [Fact]
    public async Task Receta_Sale_firmada_el_paciente_la_ve_vigente_descarga_su_PDF_y_anulada_deja_de_estar_vigente()
    {
        var (key, receta) = await UnaRecetaEmitida();
        var guardada = (await Recetas.GetAsync(receta.Id, CancellationToken.None))!;

        SesionDe(Miembro);
        var mias = Assert.IsType<List<RecetaParaLeer>>(Assert.IsType<OkObjectResult>(await ConRecetas().MisRecetas(CancellationToken.None)).Value);
        var pdf = Assert.IsType<FileContentResult>(await ConRecetas().MiPdfDeLaReceta(receta.Id, CancellationToken.None));
        SesionDe(Medico);
        var anulada = Assert.IsType<RecetaParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().AnularReceta(key, receta.Id, CancellationToken.None)).Value);

        Assert.True(receta.Firmada && receta.Vigente);
        Assert.Equal(RecetasClinicas.Huella(guardada), guardada.Firma!.Huella);
        Assert.True(Assert.Single(mias).Vigente);
        Assert.Equal("application/pdf", pdf.ContentType);
        var texto = System.Text.Encoding.Latin1.GetString(pdf.FileContents);
        Assert.StartsWith("%PDF-1.4", texto);
        Assert.Contains("Metformina 850 mg", texto);
        Assert.Contains("RECETA MÉDICA", texto);
        Assert.False(anulada.Vigente);
    }

    [Fact]
    public async Task Receta_Sin_la_llave_del_consultorio_no_hay_receta()
    {
        var key = await HistoriaDe(Miembro);
        SesionDe(Medico);
        GuardiaDice(true);
        _consulta.SellarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoDeLaConsulta(EstadoDeLaConsulta.Rechazado, Codigo: "signing.no_active_key"));

        var r = await ConRecetas().EmitirReceta(key, Metformina, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(r);
        Assert.Empty(await Recetas.ListForPatientAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task Renovacion_Pedirla_dos_veces_es_una_aprobarla_dos_veces_es_una_receta_nueva_firmada_y_ya_no_se_rechaza()
    {
        var (key, receta) = await UnaRecetaEmitida();

        SesionDe(Miembro);
        var pedida = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().PedirRenovacion(receta.Id, new PedidoDeRenovacion("Se me acaban el viernes"), CancellationToken.None)).Value);
        var otraVez = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().PedirRenovacion(receta.Id, null, CancellationToken.None)).Value);

        SesionDe(Medico);
        var aprobada = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().AprobarRenovacion(key, pedida.Id, null, CancellationToken.None)).Value);
        var dosVeces = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConRecetas().AprobarRenovacion(key, pedida.Id, null, CancellationToken.None)).Value);
        var rechazo = await ConRecetas().RechazarRenovacion(key, pedida.Id, new PedidoDeRenovacion(Motivo: "No"), CancellationToken.None);

        Assert.Equal(pedida.Id, otraVez.Id);
        Assert.Equal(("aprobada", aprobada.NuevaRecetaId), (dosVeces.Estado, dosVeces.NuevaRecetaId));
        var todas = await Recetas.ListForPatientAsync(key, CancellationToken.None);
        Assert.Equal(2, todas.Count);
        var nueva = todas.Single(r => r.PrescriptionId == aprobada.NuevaRecetaId);
        Assert.Equal((receta.Id, "Metformina 850 mg", true), (nueva.RenuevaA!.Value, nueva.MedicationName, nueva.Firma is not null));
        Assert.IsType<ConflictObjectResult>(rechazo);
    }

    [Fact]
    public async Task Renovacion_Una_receta_anulada_no_se_renueva()
    {
        var (key, receta) = await UnaRecetaEmitida();
        await ConRecetas().AnularReceta(key, receta.Id, CancellationToken.None);

        SesionDe(Miembro);
        var r = await ConRecetas().PedirRenovacion(receta.Id, null, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(r);
        Assert.Empty(await Renovaciones.ListAsync(key, CancellationToken.None));
    }

    [Fact]
    public void PdfDeTexto_Es_un_PDF_con_su_tabla_de_referencias_bien_contada_y_las_tildes_en_WinAnsi()
    {
        var bytes = PdfDeTexto.Generar(["Receta médica — señora Núñez", new string('x', 200)]);
        var texto = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        var xref = int.Parse(texto[(texto.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", texto[xref..]);
        var primero = int.Parse(texto[(xref + texto[xref..].IndexOf("0000000000 65535 f \n", StringComparison.Ordinal) + 20)..][..10],
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("1 0 obj", texto[primero..]);
        Assert.Contains("(Receta médica - señora Núñez) Tj", texto);
    }

    // ── Mensajes, bandeja y estado de cuenta (I7) ───────────────────────────────────────────────────────

    private static readonly Guid OtroMiembro = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private readonly StubMessagingService _mensajeria = new(null);
    private readonly IDoctorDirectory _directorio = UnDirectorio();

    private static IDoctorDirectory UnDirectorio()
    {
        var dra = new MedicalDoctor("dra-1", "Dra. Ana Gómez", "Medicina general", "RM-1", 5, 10, null, [], 8, 17, 30);
        var directorio = Substitute.For<IDoctorDirectory>();
        directorio.GetAsync("dra-1", Arg.Any<CancellationToken>()).Returns(dra);
        directorio.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new[] { dra });
        return directorio;
    }

    private HealthcareApiController ConMensajes() => new(
        _guard, _patients, Substitute.For<IAppointmentScheduler>(), Recetas, Substitute.For<IConsentLedger>(), _gate,
        new EditorDeLaHistoriaClinica(_patients), consulta: _consulta, ordenes: Ordenes, archivos: _archivos,
        renovaciones: Renovaciones, mensajeria: _mensajeria, directorio: _directorio);

    private void ComoMedico()
    {
        SesionDe(Medico);
        _gate.HasAnyRole("doctor").Returns(true);
    }

    private async Task<HiloDelPaciente> ElPacienteEscribe(string texto)
    {
        SesionDe(Miembro);
        return Assert.IsType<HiloDelPaciente>(Assert.IsType<OkObjectResult>(
            await ConMensajes().EscribirMensaje(new MensajeEscrito("dra-1", texto), CancellationToken.None)).Value);
    }

    [Fact]
    public async Task Mensajes_El_paciente_escribe_a_su_profesional_y_el_medico_contesta_en_el_mismo_hilo()
    {
        await HistoriaDe(Miembro);
        GuardiaDice(true);
        var primero = await ElPacienteEscribe("Doctora, ¿puedo tomar la metformina en ayunas?");
        var segundo = await ElPacienteEscribe("Olvidé decirle que me mareo.");

        ComoMedico();
        var leido = Assert.IsType<HiloDelConsultorio>(Assert.IsType<OkObjectResult>(
            await ConMensajes().HiloDelConsultorio(primero.Id, CancellationToken.None)).Value);
        var contestado = Assert.IsType<HiloDelConsultorio>(Assert.IsType<OkObjectResult>(await ConMensajes().ResponderMensaje(
            primero.Id, new MensajeEscrito(Texto: "Mejor con las comidas. Venga a control."), CancellationToken.None)).Value);

        SesionDe(Miembro);
        var mios = Assert.IsType<List<HiloDelPaciente>>(Assert.IsType<OkObjectResult>(await ConMensajes().MisMensajes(CancellationToken.None)).Value);

        Assert.Equal(primero.Id, segundo.Id);
        Assert.True(segundo.Esperando);
        Assert.Equal(("Camila Restrepo", "Dra. Ana Gómez", 2), (leido.Paciente, leido.Profesional, leido.Mensajes.Count));
        Assert.False(contestado.Esperando);
        var hilo = Assert.Single(mios);
        Assert.Equal(new[] { true, true, false }, hilo.Mensajes.Select(m => m.DelPaciente));
        Assert.False(hilo.Esperando);
    }

    [Fact]
    public async Task Mensajes_Sin_el_permiso_del_paciente_el_medico_no_lee_ni_contesta_su_hilo()
    {
        await HistoriaDe(Miembro);
        GuardiaDice(true);
        var hilo = await ElPacienteEscribe("¿Me puede ver el viernes?");

        ComoMedico();
        GuardiaDice(false);
        var leer = await ConMensajes().HiloDelConsultorio(hilo.Id, CancellationToken.None);
        var contestar = await ConMensajes().ResponderMensaje(hilo.Id, new MensajeEscrito(Texto: "Sí"), CancellationToken.None);

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(leer).StatusCode);
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(contestar).StatusCode);
        Assert.Single((await _mensajeria.GetThreadAsync(hilo.Id, CancellationToken.None))!.Messages);
    }

    [Theory]
    [InlineData("dra-9", "Hola")]
    [InlineData("dra-1", "   ")]
    public async Task Mensajes_A_quien_no_esta_en_el_directorio_o_sin_texto_es_400_y_no_se_guarda(string profesional, string texto)
    {
        await HistoriaDe(Miembro);
        GuardiaDice(true);
        SesionDe(Miembro);

        var r = await ConMensajes().EscribirMensaje(new MensajeEscrito(profesional, texto), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(r);
        Assert.Empty(await _mensajeria.GetInboxAsync(MensajesClinicos.BuzonDelPaciente(
            (await _patients.FindKeyByMemberAsync(Miembro, CancellationToken.None))!.Value), CancellationToken.None));
    }

    [Fact]
    public async Task Bandeja_Reune_el_resultado_la_renovacion_y_el_mensaje_y_cada_uno_sale_al_atenderlo()
    {
        var (key, receta) = await UnaRecetaEmitida();
        ComoMedico();
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConMensajes().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value);
        var cargada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConMensajes().CargarResultado(key, orden.Id, HbA1c("e1"), CancellationToken.None)).Value);
        SesionDe(Miembro);
        var pedida = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConMensajes().PedirRenovacion(receta.Id, null, CancellationToken.None)).Value);
        var hilo = await ElPacienteEscribe("¿Ya salió mi examen?");

        ComoMedico();
        var antes = Assert.IsType<BandejaDelConsultorio>(Assert.IsType<OkObjectResult>(await ConMensajes().Bandeja(CancellationToken.None)).Value);
        await ConMensajes().LiberarResultado(key, cargada.Examenes[0].Resultado!.Id, CancellationToken.None);
        await ConMensajes().AprobarRenovacion(key, pedida.Id, null, CancellationToken.None);
        await ConMensajes().ResponderMensaje(hilo.Id, new MensajeEscrito(Texto: "Sí, ya está liberado."), CancellationToken.None);
        var despues = Assert.IsType<BandejaDelConsultorio>(Assert.IsType<OkObjectResult>(await ConMensajes().Bandeja(CancellationToken.None)).Value);

        Assert.Equal(new[] { "mensaje", "renovacion", "resultado" }, antes.Items.Select(i => i.Tipo).Order());
        Assert.All(antes.Items, i => Assert.Equal((key, "Camila Restrepo"), (i.PacienteKey, i.Paciente)));
        Assert.Contains(antes.Items, i => i.Tipo == "resultado" && i.Resumen == "HbA1c");
        Assert.Contains(antes.Items, i => i.Tipo == "renovacion" && i.Resumen == "Metformina 850 mg");
        Assert.Empty(despues.Items);
    }

    [Fact]
    public async Task Bandeja_Un_paciente_sin_permiso_no_sale_solo_se_cuenta_y_sin_rol_de_medico_es_403()
    {
        var conPermiso = await HistoriaDe(Miembro);
        var sinPermiso = await HistoriaDe(OtroMiembro);
        GuardiaDice(true);
        _guard.CheckAccessAsync(Arg.Is<AccessCheckRequest>(r => r.TargetPatientKey == sinPermiso), Arg.Any<CancellationToken>())
            .Returns(new AccessDecision(false, "no-active-consent", "audit-id"));
        ComoMedico();
        await ConMensajes().CargarResultado(conPermiso, Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConMensajes().Ordenar(conPermiso, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value).Id,
            HbA1c("e1"), CancellationToken.None);

        var bandeja = Assert.IsType<BandejaDelConsultorio>(Assert.IsType<OkObjectResult>(await ConMensajes().Bandeja(CancellationToken.None)).Value);
        _gate.HasAnyRole("doctor").Returns(false);
        GuardiaDice(false);
        var sinRol = await ConMensajes().Bandeja(CancellationToken.None);

        Assert.Equal((1, conPermiso), (bandeja.SinPermiso, Assert.Single(bandeja.Items).PacienteKey));
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(sinRol).StatusCode);
    }

    [Fact]
    public async Task Cuenta_Son_las_lineas_del_orquestador_con_sus_totales_y_sin_respuesta_es_503()
    {
        SesionDe(Miembro);
        GuardiaDice(true);
        _consulta.EstadoDeCuentaAsync("salud.paciente", Miembro.ToString("N"), Arg.Any<CancellationToken>()).Returns(new EstadoDeCuenta([
            new LineaDeLaCuenta("c1", "CITA1", new DateTimeOffset(2027, 3, 1, 15, 0, 0, TimeSpan.Zero), 50_000m, 25_000m, "COP"),
            new LineaDeLaCuenta("c2", "CITA2", new DateTimeOffset(2027, 3, 8, 15, 0, 0, TimeSpan.Zero), 50_000m, 0m, "COP"),
        ]));

        var cuenta = Assert.IsType<CuentaParaLeer>(Assert.IsType<OkObjectResult>(await ConMensajes().MiEstadoDeCuenta(CancellationToken.None)).Value);
        _consulta.EstadoDeCuentaAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((EstadoDeCuenta?)null);
        var caida = await ConMensajes().MiEstadoDeCuenta(CancellationToken.None);

        Assert.Equal(new[] { "c2", "c1" }, cuenta.Lineas.Select(l => l.CitaId));
        Assert.Equal(new TotalDeLaCuenta("COP", 100_000m, 25_000m, 75_000m), Assert.Single(cuenta.Totales));
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(caida).StatusCode);
    }

    // ── Los avisos del consultorio al paciente (I8 P18) ─────────────────────────────────────────────────

    private readonly ITransactionalNotifier _avisos = Substitute.For<ITransactionalNotifier>();

    private HealthcareApiController ConAvisos()
    {
        var miembros = Substitute.For<IBuscadorDeMiembros>();
        miembros.ContactoAsync(Miembro, Arg.Any<CancellationToken>()).Returns(new ContactoDelMiembro("camila@ejemplo.co", "Camila"));
        return new(
            _guard, _patients, Substitute.For<IAppointmentScheduler>(), Recetas, Substitute.For<IConsentLedger>(), _gate,
            new EditorDeLaHistoriaClinica(_patients), miembros: miembros, consulta: _consulta, ordenes: Ordenes, archivos: _archivos,
            renovaciones: Renovaciones, mensajeria: _mensajeria, directorio: _directorio, avisos: _avisos);
    }

    [Fact]
    public async Task Avisos_Liberar_un_resultado_responder_una_renovacion_y_contestar_un_mensaje_le_avisan_al_paciente_sin_el_dato_clinico()
    {
        var (key, receta) = await UnaRecetaEmitida();
        ComoMedico();
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConAvisos().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value);
        var cargada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConAvisos().CargarResultado(key, orden.Id, HbA1c("e1"), CancellationToken.None)).Value);
        SesionDe(Miembro);
        var pedida = Assert.IsType<RenovacionParaLeer>(Assert.IsType<OkObjectResult>(
            await ConAvisos().PedirRenovacion(receta.Id, null, CancellationToken.None)).Value);
        var hilo = await ElPacienteEscribe("¿Ya salió mi examen?");

        ComoMedico();
        await ConAvisos().LiberarResultado(key, cargada.Examenes[0].Resultado!.Id, CancellationToken.None);
        await ConAvisos().LiberarResultado(key, cargada.Examenes[0].Resultado!.Id, CancellationToken.None);
        await ConAvisos().AprobarRenovacion(key, pedida.Id, null, CancellationToken.None);
        await ConAvisos().ResponderMensaje(hilo.Id, new MensajeEscrito(Texto: "Sí, ya está liberado."), CancellationToken.None);

        var enviados = _avisos.ReceivedCalls().Select(c => (NotificationEvent)c.GetArguments()[0]!).ToList();
        Assert.Equal(new[] { NotificationTypes.SaludResultadoLiberado, NotificationTypes.SaludRenovacionRespondida, NotificationTypes.SaludMensajeRespondido },
            enviados.Select(e => e.Type));
        Assert.All(enviados, e =>
        {
            Assert.Equal(("camila@ejemplo.co", "/healthcare/"), (e.ToEmail, e.ActionPath));
            var todo = System.Text.Json.JsonSerializer.Serialize(e);
            Assert.DoesNotContain("HbA1c", todo, StringComparison.Ordinal);
            Assert.DoesNotContain("Metformina", todo, StringComparison.Ordinal);
            Assert.DoesNotContain("liberado.", todo, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Avisos_Si_el_aviso_no_sale_el_resultado_queda_liberado_igual()
    {
        var key = await HistoriaDe(Miembro);
        ComoMedico();
        GuardiaDice(true);
        _avisos.DispatchAsync(Arg.Any<NotificationEvent>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("sin correo")));
        var orden = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(await ConAvisos().Ordenar(
            key, new OrdenEscrita("laboratorio", ["HbA1c"]), CancellationToken.None)).Value);
        var cargada = Assert.IsType<OrdenParaLeer>(Assert.IsType<OkObjectResult>(
            await ConAvisos().CargarResultado(key, orden.Id, HbA1c("e1"), CancellationToken.None)).Value);

        var r = await ConAvisos().LiberarResultado(key, cargada.Examenes[0].Resultado!.Id, CancellationToken.None);

        Assert.IsType<OkObjectResult>(r);
        Assert.True((await Ordenes.GetResultadoAsync(cargada.Examenes[0].Resultado!.Id, CancellationToken.None))!.Liberado);
    }

    private sealed class ArchivosEnMemoria : IPrivateFileStore
    {
        private readonly Dictionary<string, PrivateFileContent> _datos = new();

        public Task<string> SaveAsync(string scope, byte[] content, string contentType, string fileName, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid().ToString("N");
            _datos[scope + "/" + id] = new PrivateFileContent(content, contentType, fileName);
            return Task.FromResult(id);
        }

        public Task<PrivateFileContent?> ReadAsync(string scope, string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_datos.TryGetValue(scope + "/" + fileId, out var v) ? v : null);

        public Task<bool> DeleteAsync(string scope, string fileId, CancellationToken cancellationToken = default)
            => Task.FromResult(_datos.Remove(scope + "/" + fileId));
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
