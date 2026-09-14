using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Los seams de lectura de <see cref="EhrController"/> y — sobre todo — la propiedad que la
/// HU #106 vino a recuperar: <b>este borde no fabrica datos clínicos</b>.
/// </summary>
/// <remarks>
/// <para><b>Qué NO hay aquí.</b> Los cuerpos de petición los cubre
/// <see cref="EhrContractDriftTests"/> (#105); no se repiten. Y no hay ni un test que
/// construya un DTO del controller y compruebe sus campos: eso afirma lo que el controller
/// decidió poner, no lo que el consumidor lee (<c>CLAUDE.md</c> §5,
/// <c>feedback_contract_shape_needs_its_own_test</c>). Todo lo que sea contrato se serializa
/// con <see cref="JsonSerializerDefaults.Web"/> —lo que usa MVC— y se afirma sobre la
/// <b>clave serializada</b>.</para>
///
/// <para><b>El defecto.</b> El resumen de salud derivaba las vacunas y el estado del cuidado
/// preventivo de <c>person.Id.GetHashCode()</c>, y el tablero del día sacaba
/// <c>checkedInAhead</c> de <c>a.Id.GetHashCode() % 2</c> — bajo un comentario que los llamaba
/// «deterministas». En .NET Core el hash de string está <b>aleatorizado por proceso</b>: el
/// mismo paciente pasaba de «Influenza al día» a «vencida» en cada reinicio del servidor sin
/// que nadie tocara nada. Es la forma de #72 y #82: la propiedad que el código anuncia como su
/// razón de ser es justamente la que no cumple.</para>
///
/// <para><b>Cómo se afirma, y qué alcance tiene.</b> No se puede reiniciar el proceso dentro de
/// un test, así que la propiedad se mira por dos lados y hace falta decir qué caza cada uno:</para>
/// <list type="number">
/// <item><b>Ausencia de clave</b> (<c>checkedInAhead</c>, <c>status</c>/<c>dueDate</c> del
///   preventivo, y <c>immunizations</c> vacío). Es <b>exacto</b>: reintroducir el defecto tal
///   cual lo pone rojo el 100 % de las veces. Lo que NO caza es que alguien vuelva a rellenar
///   el campo con una derivación distinta del id.</item>
/// <item><b>Invariancia respecto al id</b>: 16 sujetos idénticos salvo el identificador tienen
///   que dar la MISMA respuesta una vez sustituido el id. Caza con certeza cualquier
///   fabricación derivada del id que no sea el id mismo —longitud, dígitos, lo que sea— y caza
///   la derivada del hash con probabilidad 1−2⁻¹⁵ (los 16 hashes tendrían que coincidir todos
///   en paridad). Queda escrito porque un gate que se cree más exacto de lo que es es peor que
///   no tenerlo.</item>
/// </list>
/// </remarks>
public sealed class EhrControllerTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IPatientRegistry _patients = Substitute.For<IPatientRegistry>();
    private readonly IDoctorDirectory _doctors = Substitute.For<IDoctorDirectory>();
    private readonly IClinicalRecordService _records = Substitute.For<IClinicalRecordService>();
    private readonly IClinicalPrescriptionService _prescriptions = Substitute.For<IClinicalPrescriptionService>();
    private readonly IClinicalSchedulingService _scheduling = Substitute.For<IClinicalSchedulingService>();
    private readonly IClinicalResultsProvider _results = Substitute.For<IClinicalResultsProvider>();
    private readonly IClinicalMedicationService _medications = Substitute.For<IClinicalMedicationService>();
    private readonly IClinicalOrderService _orders = Substitute.For<IClinicalOrderService>();
    private readonly IClinicalBillingService _billing = Substitute.For<IClinicalBillingService>();
    private readonly IEhrInBasketService _inBasket = Substitute.For<IEhrInBasketService>();
    private readonly IMessagingService _messaging = Substitute.For<IMessagingService>();

    private EhrController BuildSut() => new(
        _patients, _doctors, _records, _prescriptions, _scheduling,
        _results, _medications, _orders, _billing, _inBasket, _messaging);

    private static JsonElement Json(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, Web);
    }

    private static string Raw(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.Serialize(ok.Value, Web);
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────────

    private static EhrPatient Paciente(
        string id = "pat-1",
        string gender = "M",
        int age = 33,
        IReadOnlyList<string>? alergias = null,
        IReadOnlyList<string>? cronicas = null) => new(
        Id: id, FullName: "Jorge Medina", DocumentId: "CC 1", Gender: gender,
        DateOfBirth: new DateOnly(1993, 1, 1), AgeYears: age, Phone: "3001112233",
        Email: "jorge@correo.co", City: "Bogotá", BloodType: "O+",
        Allergies: alergias ?? Array.Empty<string>(),
        ChronicConditions: cronicas ?? Array.Empty<string>(),
        PrimaryDoctorId: "doc-1", AvatarUrl: null);

    private static ClinicalAppointment Cita(
        string id = "appt-1",
        string patientId = "pat-1",
        DateTime? startUtc = null,
        string status = "booked")
    {
        var inicio = startUtc ?? DateTime.UtcNow.AddDays(10).Date.AddHours(9);
        return new ClinicalAppointment(
            Id: id, PatientId: patientId, PatientName: "Jorge Medina",
            DoctorId: "doc-1", DoctorName: "Dra. Ana Rojas", Specialty: "Medicina interna",
            StartUtc: inicio, EndUtc: inicio.AddMinutes(30), Status: status, ReservationId: "res-1");
    }

    private static MedicalDoctor Medico(string id = "doc-1") => new(
        Id: id, FullName: "Dra. Ana Rojas", Specialty: "Medicina interna",
        LicenseNumber: "RM-1234", Rating: 4.8, YearsExperience: 12, AvatarUrl: null,
        WorkingDays: new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
        SlotStartHour: 8, SlotEndHour: 16, SlotMinutes: 30);

    private static EhrMedication Medicamento(string id = "med-1") => new(
        MedicationId: id, PatientId: "pat-1", MedicationName: "Losartán",
        Dosage: "50 mg", Frequency: "cada 12 h", Instructions: "Con alimento.",
        PrescribedByDoctorId: "doc-1", PrescribedByDoctorName: "Dra. Ana Rojas",
        PrescribedAtUtc: new DateTime(2026, 1, 2), Status: "active", RefillsRemaining: 2);

    private void PadronDevuelve(EhrPatient? p) =>
        _patients.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(p);

    private void AgendaDelDiaDevuelve(params ClinicalAppointment[] citas) =>
        _scheduling.GetByDateAsync(Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(citas);

    private void CitasDelPacienteDevuelven(params ClinicalAppointment[] citas) =>
        _scheduling.GetForPatientAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(citas);

    /// <summary>
    /// 16 identificadores distintos <b>y de longitudes distintas</b>. Lo segundo importa: con
    /// ids del mismo largo, una fabricación «determinista» a partir de <c>id.Length</c> daría
    /// el mismo valor para los 16 y el test pasaría con el defecto puesto. El fixture tiene que
    /// exigir la regla, no acompañarla.
    /// </summary>
    private static IReadOnlyList<string> DieciseisIds() =>
        Enumerable.Range(1, 16).Select(i => "pac-" + new string('x', i)).ToList();

    // ══════════════════════════════════════════════════════════════════════════════
    // 1 · El defecto de #106 — nada clínico se fabrica
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact] // El carné de vacunas estaba inventado entero: no hay seam de vacunación.
    public async Task Health_NoEmiteVacunas_PorqueNoHayRegistroDeVacunacion()
    {
        PadronDevuelve(Paciente(age: 62, gender: "F"));

        var body = Json(await BuildSut().Health("pat-1", default));

        // La clave sigue ahí y dice la verdad —el EHR no guarda vacunas de nadie, así que para
        // todo paciente hay cero— y la UI la pinta como «Sin vacunas registradas».
        Assert.Equal(JsonValueKind.Array, body.GetProperty("immunizations").ValueKind);
        Assert.Equal(0, body.GetProperty("immunizations").GetArrayLength());
    }

    [Fact] // La recomendación se emite; el estado y la fecha NO, porque exigen saber si se lo hizo.
    public async Task Health_LaRecomendacionPreventiva_NoLlevaEstadoNiFecha()
    {
        PadronDevuelve(Paciente(age: 52, gender: "F"));

        var body = Json(await BuildSut().Health("pat-1", default));

        var items = body.GetProperty("maintenance").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            // Se afirma sobre la CLAVE SERIALIZADA: el normalizador de la app defaultea
            // `status` a 'due' cuando falta, así que emitir un estado vacío sería peor que no
            // emitirlo — diría «pendiente» sobre algo que nadie miró.
            Assert.False(item.TryGetProperty("status", out _), "el preventivo no puede declarar estado");
            Assert.False(item.TryGetProperty("dueDate", out _), "el preventivo no puede declarar fecha");
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("detail").GetString()));
        }
    }

    [Fact] // La propiedad de verdad: la respuesta no puede depender del id más allá del id.
    public async Task Health_ElResumen_NoDependeDelIdentificadorDelPaciente()
    {
        var respuestas = new List<string>();
        foreach (var id in DieciseisIds())
        {
            _patients.GetAsync(id, Arg.Any<CancellationToken>())
                .Returns(Paciente(id: id, age: 52, gender: "F"));
            respuestas.Add(Raw(await BuildSut().Health(id, default)).Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    [Fact] // Si el paciente llegó antes es un HECHO; salía de una moneda al aire.
    public async Task Schedule_NoEmiteCheckedInAhead()
    {
        AgendaDelDiaDevuelve(Cita());

        var body = Json(await BuildSut().Schedule("2026-09-20", default));

        var slot = body.GetProperty("slots")[0];
        Assert.False(slot.TryGetProperty("checkedInAhead", out _), "la llegada anticipada no la sabe este borde");
    }

    [Fact] // Misma propiedad sobre el tablero: la fila no puede depender del id de la cita.
    public async Task Schedule_LaFilaDelTablero_NoDependeDelIdDeLaCita()
    {
        var inicio = DateTime.UtcNow.AddDays(10).Date.AddHours(9);
        var respuestas = new List<string>();
        foreach (var id in Enumerable.Range(1, 16).Select(i => "appt-" + new string('x', i)))
        {
            AgendaDelDiaDevuelve(Cita(id: id, startUtc: inicio));
            respuestas.Add(Raw(await BuildSut().Schedule("2026-09-20", default)).Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    [Theory] // filtro: QUÉ le corresponde SÍ se deriva — de edad y sexo, que son datos del padrón.
    [InlineData(30, "M", "pm-bp")]
    [InlineData(38, "F", "pm-bp")]                          // mamografía arranca a los 40
    [InlineData(44, "F", "pm-bp,pm-mammo")]                 // colon arranca a los 45
    [InlineData(46, "M", "pm-colon,pm-bp")]
    [InlineData(52, "F", "pm-colon,pm-bp,pm-mammo")]
    [InlineData(52, "Masculino", "pm-colon,pm-bp")]         // el padrón modela el sexo como texto libre
    public async Task Health_LaRecomendacion_SeDerivaDeEdadYSexo(int edad, string sexo, string esperados)
    {
        PadronDevuelve(Paciente(id: "pat-1", age: edad, gender: sexo));

        var body = Json(await BuildSut().Health("pat-1", default));

        var ids = body.GetProperty("maintenance").EnumerateArray()
            .Select(m => m.GetProperty("id").GetString()!.Replace("-pat-1", string.Empty, StringComparison.Ordinal))
            .ToList();
        Assert.Equal(esperados.Split(','), ids);
    }

    [Fact] // happy: condiciones y alergias sí son datos reales — y ganan los del registro clínico.
    public async Task Health_CondicionesYAlergias_SalenDelRegistro_YNoDelPadronSiLasHay()
    {
        PadronDevuelve(Paciente(alergias: new[] { "polen" }, cronicas: new[] { "obesidad" }));
        _records.GetHistoryAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new ClinicalHistory(
            PatientId: "pat-1", ChiefComplaint: "control",
            ActiveProblems: new[] { "Hipertensión" }, PastMedicalHistory: Array.Empty<string>(),
            Allergies: new[] { "penicilina" }, CurrentMedications: Array.Empty<string>(),
            BaselineVitals: null, LastVisitUtc: null, TotalEncounters: 3));

        var body = Json(await BuildSut().Health("pat-1", default));

        Assert.Equal(new[] { "Hipertensión" }, body.GetProperty("conditions").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(new[] { "penicilina" }, body.GetProperty("allergies").EnumerateArray().Select(c => c.GetString()));
    }

    [Fact] // vacío: sin historia clínica se cae al padrón, no a una lista inventada.
    public async Task Health_SinHistoria_CaeAlPadron()
    {
        PadronDevuelve(Paciente(alergias: new[] { "polen" }, cronicas: new[] { "obesidad" }));
        _records.GetHistoryAsync("pat-1", Arg.Any<CancellationToken>()).Returns((ClinicalHistory?)null);

        var body = Json(await BuildSut().Health("pat-1", default));

        Assert.Equal(new[] { "obesidad" }, body.GetProperty("conditions").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(new[] { "polen" }, body.GetProperty("allergies").EnumerateArray().Select(c => c.GetString()));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 1.bis · #111 — las cuatro CONSTANTES que afirmaban un hecho
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Mismo criterio que #106 —si el valor entero de un campo es ser cierto, no se rellena— y
    // distinta forma del defecto, que es lo que cambia los tests. En #106 el valor VARIABA
    // (`GetHashCode()`, aleatorio por proceso); aquí no variaba nada: cuatro literales escritos
    // a mano. Una constante no se delata reiniciando el servidor, así que hay que mirarla de
    // dos maneras:
    //
    //  1. LA CLAVE ESTÁ Y VALE `null`. No basta con «no es la constante»: `null` y la
    //     afirmación contraria (`0` sin-leer, `false` no-acepta-pacientes) no son lo mismo, y
    //     si se ven iguales el arreglo no sirve de nada. Por eso se afirma sobre el
    //     `ValueKind` de la clave SERIALIZADA y no sobre el DTO.
    //  2. NO SE REFABRICA DE LO QUE HAY A MANO. Quitar una constante deja un hueco, y el hueco
    //     tienta a rellenarlo con lo primero que esté cerca: el identificador (lo de #106) o
    //     —en el contador de sin-leer— el número de mensajes del hilo. Contra lo primero va la
    //     invariancia respecto al id, con ids de LONGITUDES DISTINTAS (si no, una derivación de
    //     `id.Length` pasa en verde); contra lo segundo, un fixture con mensajes de verdad: con
    //     un hilo vacío, `0`, `MessageCount` y «no se sabe» valen todos lo mismo y el test no
    //     prueba nada.

    [Fact] // El peor de los cuatro: una constante que manda a una persona a un sitio.
    public async Task Medications_NoDeclaraFarmacia_PorqueEsteBordeNoSabeDondeSeDispensa()
    {
        _medications.GetActiveForPatientAsync("pat-1", Arg.Any<CancellationToken>())
            .Returns(new[] { Medicamento() });

        var m = Json(await BuildSut().Medications("pat-1", default)).GetProperty("medications")[0];

        // Decía «Farmacia Synergos» al lado de un medicamento real, en la pantalla desde la que
        // alguien sale a recogerlo. Ningún seam trae la farmacia dispensadora.
        Assert.True(m.TryGetProperty("pharmacy", out var farmacia));
        Assert.Equal(JsonValueKind.Null, farmacia.ValueKind);
    }

    [Fact] // …y el hueco tampoco se rellena con el identificador.
    public async Task Medications_LaFarmacia_NoSeDerivaDelIdentificador()
    {
        var respuestas = new List<string>();
        foreach (var id in DieciseisIds())
        {
            _medications.GetActiveForPatientAsync("pat-1", Arg.Any<CancellationToken>())
                .Returns(new[] { Medicamento(id: id) });
            respuestas.Add(Raw(await BuildSut().Medications("pat-1", default))
                .Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    [Fact] // «Este médico acepta pacientes nuevos», dicho por quien no tiene cómo saberlo.
    public async Task Doctors_NoDeclaraSiAceptaPacientesNuevos()
    {
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Medico() });

        var d = Json(await BuildSut().Doctors(null, default)).GetProperty("doctors")[0];

        // `null` y `false` no son lo mismo: cerrarle la lista a un médico que sí recibe es tan
        // falso como abrírsela al que no.
        Assert.True(d.TryGetProperty("acceptingPatients", out var acepta));
        Assert.Equal(JsonValueKind.Null, acepta.ValueKind);
    }

    [Fact]
    public async Task Doctors_LaFichaDelMedico_NoSeDerivaDelIdentificador()
    {
        var respuestas = new List<string>();
        foreach (var id in DieciseisIds())
        {
            _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new[] { Medico(id: id) });
            respuestas.Add(Raw(await BuildSut().Doctors(null, default))
                .Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    [Fact] // El contador de sin-leer no podía mostrar nada nunca: era la constante 0.
    public async Task Messages_NoDeclaraCuantosSinLeer_PorqueElSeamNoRegistraLectura()
    {
        // EL FIXTURE TIENE QUE EXIGIR LA REGLA: el hilo trae TRES mensajes. Con un hilo vacío,
        // `0` (la constante de antes), `MessageCount` (la refabricación que tienta) y «no se
        // sabe» darían todos lo mismo y el test pasaría con el defecto puesto.
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>())
            .Returns(new[] { Resumen("t-1", "clinical:advice:pat-1", mensajes: 3) });
        _messaging.GetThreadAsync("t-1", Arg.Any<CancellationToken>()).Returns(new MessageThread(
            ThreadId: "t-1", ContextRef: "clinical:advice:pat-1",
            Participants: new[] { "pat-1", "doc-1" },
            Messages: new[]
            {
                new ThreadMessage("m-1", "doc-1", "Sus resultados están listos.", DateTimeOffset.UnixEpoch),
                new ThreadMessage("m-2", "pat-1", "Gracias, ¿los reviso con usted?", DateTimeOffset.UnixEpoch.AddHours(1)),
                new ThreadMessage("m-3", "doc-1", "Sí, en la cita del jueves.", DateTimeOffset.UnixEpoch.AddHours(2)),
            },
            CreatedAt: DateTimeOffset.UnixEpoch, LastMessageAt: DateTimeOffset.UnixEpoch.AddHours(2)));

        var hilo = Json(await BuildSut().Messages("pat-1", default)).GetProperty("threads")[0];

        // `0` no decía «no sé»: decía «no tienes mensajes sin leer», que es la afirmación
        // contraria y es la que hace que el paciente no abra el mensaje de su médico.
        Assert.True(hilo.TryGetProperty("unread", out var sinLeer));
        Assert.Equal(JsonValueKind.Null, sinLeer.ValueKind);
    }

    [Fact] // El mismo hueco por el otro mapper — el del hilo que ya no se rehidrata.
    public async Task Messages_HiloNoRehidratable_TampocoDeclaraCuantosSinLeer()
    {
        // Aquí la refabricación está aún más a mano: el resumen TRAE `MessageCount` (7).
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>())
            .Returns(new[] { Resumen("t-1", "clinical:result:pat-1", mensajes: 7) });
        _messaging.GetThreadAsync("t-1", Arg.Any<CancellationToken>()).Returns((MessageThread?)null);

        var hilo = Json(await BuildSut().Messages("pat-1", default)).GetProperty("threads")[0];

        Assert.True(hilo.TryGetProperty("unread", out var sinLeer));
        Assert.Equal(JsonValueKind.Null, sinLeer.ValueKind);
    }

    [Fact] // «Este paciente está activo» — un episodio de atención abierto que nadie registró.
    public async Task Patients_NoDeclaraSiElPacienteEstaActivo()
    {
        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Paciente() });

        var p = Json(await BuildSut().Patients(null, default)).GetProperty("patients")[0];

        Assert.True(p.TryGetProperty("active", out var activo));
        Assert.Equal(JsonValueKind.Null, activo.ValueKind);
    }

    [Fact]
    public async Task Patients_LaFichaDelPaciente_NoSeDerivaDelIdentificador()
    {
        var respuestas = new List<string>();
        foreach (var id in DieciseisIds())
        {
            _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new[] { Paciente(id: id) });
            respuestas.Add(Raw(await BuildSut().Patients(null, default))
                .Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 1.ter · #111 — una pregunta, no noventa y una
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // `CollectPatientAppointmentsAsync` barría −30/+60 días llamando a `GetByDateAsync` UN DÍA
    // A LA VEZ y descartando de este lado casi todo lo que traía: 91 llamadas por carga, y lo
    // llaman la ficha del paciente Y el home del portal. Contra el stub en memoria no se nota
    // —por eso vivió tanto—; contra `HttpClinicalSchedulingService` son 91 viajes para traer lo
    // mismo. Lo que se mide aquí no es el resultado (no cambia) sino CUÁNTAS VECES se pregunta,
    // que es justo lo que ningún test miraba.

    [Fact]
    public async Task Chart_LasCitasDelPaciente_SePidenDeUnaVez_YNoDiaPorDia()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven(Cita());

        await BuildSut().Patient("pat-1", default);

        await _scheduling.Received(1).GetForPatientAsync(
            "pat-1", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _scheduling.DidNotReceive().GetByDateAsync(
            Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PortalHome_LasCitasDelPaciente_SePidenDeUnaVez_YNoDiaPorDia()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven(Cita());

        await BuildSut().PortalHome("pat-1", default);

        await _scheduling.Received(1).GetForPatientAsync(
            "pat-1", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await _scheduling.DidNotReceive().GetByDateAsync(
            Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // La ventana es la MISMA que barría el bucle: esto no estrecha ni ensancha nada.
    public async Task Chart_LaVentanaDeCitas_SigueSiendoLaDeAntes()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        await BuildSut().Patient("pat-1", default);

        await _scheduling.Received(1).GetForPatientAsync(
            "pat-1", hoy.AddDays(-30), hoy.AddDays(60), Arg.Any<CancellationToken>());
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 2 · Los caminos de rechazo (el borde es anónimo: lo mínimo es no inventarse el sujeto)
    // ══════════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> SinSujeto() => new() { "", "   " };

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Health_SinPaciente_400(string patient)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Health(patient, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task PortalHome_SinPaciente_400(string patient)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().PortalHome(patient, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Results_SinPaciente_400(string patient)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Results(patient, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Medications_SinPaciente_400(string patient)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Medications(patient, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Billing_SinPaciente_400(string patient)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Billing(patient, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Messages_SinUsuario_400(string user)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Messages(user, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task InBasket_SinProveedor_400(string provider)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().InBasket(provider, null, default));

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Patient_SinId_400(string id)
        => Assert.IsType<BadRequestObjectResult>(await BuildSut().Patient(id, default));

    [Fact]
    public async Task Patient_Inexistente_404()
    {
        PadronDevuelve(null);
        Assert.IsType<NotFoundObjectResult>(await BuildSut().Patient("pat-fantasma", default));
    }

    [Fact]
    public async Task Health_PacienteInexistente_404_YNoConsultaLaHistoria()
    {
        PadronDevuelve(null);

        Assert.IsType<NotFoundObjectResult>(await BuildSut().Health("pat-fantasma", default));
        await _records.DidNotReceive().GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PortalHome_PacienteInexistente_404()
    {
        PadronDevuelve(null);
        Assert.IsType<NotFoundObjectResult>(await BuildSut().PortalHome("pat-fantasma", default));
    }

    [Fact] // sin estado de cuenta NO se devuelve un saldo 0 — eso diría «no debe nada».
    public async Task Billing_SinEstadoDeCuenta_404()
    {
        _billing.GetForPatientAsync("pat-1", Arg.Any<CancellationToken>()).Returns((EhrBillingStatement?)null);
        Assert.IsType<NotFoundObjectResult>(await BuildSut().Billing("pat-1", default));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 3 · Los seams de lectura: vacío / happy / filtro
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact] // vacío: un padrón sin resultados es una lista vacía, no un null que la app descarta.
    public async Task Patients_PadronVacio_DevuelveListaVacia()
    {
        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPatient>());

        var body = Json(await BuildSut().Patients("nadie", default));

        Assert.Equal(0, body.GetProperty("patients").GetArrayLength());
    }

    [Fact] // filtro: el término de búsqueda llega TAL CUAL al padrón (la caja de búsqueda no era decorativa).
    public async Task Patients_ElTerminoDeBusqueda_LlegaAlPadron()
    {
        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPatient>());

        await BuildSut().Patients("medina", default);

        await _patients.Received(1).SearchAsync("medina", Arg.Any<CancellationToken>());
    }

    [Fact] // happy: el sexo del padrón es texto libre y la app lee el código 'M'|'F'|'X'.
    public async Task Patients_ElSexo_SaleComoCodigo()
    {
        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Paciente(id: "p1", gender: "Femenino"), Paciente(id: "p2", gender: "") });

        var body = Json(await BuildSut().Patients(null, default));

        var sexos = body.GetProperty("patients").EnumerateArray()
            .Select(p => p.GetProperty("sex").GetString()).ToList();
        Assert.Equal(new[] { "F", "X" }, sexos);
    }

    [Fact] // filtro: la especialidad llega al directorio.
    public async Task Doctors_LaEspecialidad_LlegaAlDirectorio()
    {
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MedicalDoctor>());

        await BuildSut().Doctors("cardiología", default);

        await _doctors.Received(1).ListAsync("cardiología", Arg.Any<CancellationToken>());
    }

    [Fact] // filtro: fecha y médico llegan los DOS a la agenda.
    public async Task Appointments_FechaYMedico_LleganALaAgenda()
    {
        AgendaDelDiaDevuelve();

        await BuildSut().Appointments("2026-09-20", "doc-7", default);

        await _scheduling.Received(1).GetByDateAsync(
            new DateOnly(2026, 9, 20), "doc-7", Arg.Any<CancellationToken>());
    }

    [Fact] // vacío: una fecha ilegible cae a HOY, no vacía el tablero ni revienta.
    public async Task Appointments_FechaIlegible_CaeAHoy()
    {
        AgendaDelDiaDevuelve();

        await BuildSut().Appointments("no-es-una-fecha", null, default);

        await _scheduling.Received(1).GetByDateAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), null, Arg.Any<CancellationToken>());
    }

    [Fact] // filtro: el tablero del día ordena por hora, que es como se lee de arriba abajo.
    public async Task Schedule_OrdenaPorHora()
    {
        var dia = DateTime.UtcNow.AddDays(10).Date;
        AgendaDelDiaDevuelve(
            Cita(id: "a-tarde", startUtc: dia.AddHours(15)),
            Cita(id: "a-manana", startUtc: dia.AddHours(8)));

        var body = Json(await BuildSut().Schedule("2026-09-20", default));

        var ids = body.GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("appointmentId").GetString()).ToList();
        Assert.Equal(new[] { "a-manana", "a-tarde" }, ids);
    }

    [Fact] // happy: el estado sale del RELOJ (cita ya terminada), no del identificador.
    public async Task Schedule_ElEstado_SaleDelReloj()
    {
        var ayer = DateTime.UtcNow.AddDays(-1);
        AgendaDelDiaDevuelve(Cita(id: "a-1", startUtc: ayer));

        var body = Json(await BuildSut().Schedule(null, default));

        Assert.Equal("checked-out", body.GetProperty("slots")[0].GetProperty("state").GetString());
    }

    [Fact] // filtro: la bandeja clínica sólo trae hilos del contexto 'clinical'.
    public async Task Messages_SoloDevuelveHilosClinicos()
    {
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("t-clinico", "clinical:refill:pat-1"),
            Resumen("t-tienda", "shop:order:o-9"),
        });
        _messaging.GetThreadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((MessageThread?)null);

        var body = Json(await BuildSut().Messages("pat-1", default));

        var ids = body.GetProperty("threads").EnumerateArray()
            .Select(t => t.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "t-clinico" }, ids);
    }

    [Fact] // happy: el hilo se hidrata con sus mensajes, y 'outgoing' se resuelve contra QUIEN consulta.
    public async Task Messages_HidrataElHilo_YMarcaLoPropioComoSaliente()
    {
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>())
            .Returns(new[] { Resumen("t-1", "clinical:advice:pat-1") });
        _messaging.GetThreadAsync("t-1", Arg.Any<CancellationToken>()).Returns(new MessageThread(
            ThreadId: "t-1", ContextRef: "clinical:advice:pat-1",
            Participants: new[] { "pat-1", "doc-1" },
            Messages: new[]
            {
                new ThreadMessage("m-1", "pat-1", "¿Puedo tomarlo en ayunas?", DateTimeOffset.UnixEpoch),
                new ThreadMessage("m-2", "doc-1", "Sí, sin problema.", DateTimeOffset.UnixEpoch.AddHours(1)),
            },
            CreatedAt: DateTimeOffset.UnixEpoch, LastMessageAt: DateTimeOffset.UnixEpoch.AddHours(1)));

        var hilo = Json(await BuildSut().Messages("pat-1", default)).GetProperty("threads")[0];

        Assert.Equal("doc-1", hilo.GetProperty("participant").GetString());
        Assert.Equal("Consejo médico", hilo.GetProperty("subject").GetString());
        var salientes = hilo.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("outgoing").GetBoolean()).ToList();
        Assert.Equal(new[] { true, false }, salientes);
    }

    [Fact] // vacío: un hilo que ya no se puede rehidratar cae al resumen y NO se pierde de la bandeja.
    public async Task Messages_HiloNoRehidratable_CaeAlResumen()
    {
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>())
            .Returns(new[] { Resumen("t-1", "clinical:result:pat-1", preview: "Su hemograma está listo.") });
        _messaging.GetThreadAsync("t-1", Arg.Any<CancellationToken>()).Returns((MessageThread?)null);

        var hilo = Json(await BuildSut().Messages("pat-1", default)).GetProperty("threads")[0];

        Assert.Equal("Su hemograma está listo.", hilo.GetProperty("lastMessage").GetString());
        Assert.Equal(0, hilo.GetProperty("messages").GetArrayLength());
    }

    [Fact] // filtro: el tipo llega a la cola del proveedor.
    public async Task InBasket_ElTipo_LlegaALaCola()
    {
        _inBasket.GetForProviderAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrInBasketItem>());

        await BuildSut().InBasket("doc-1", "refill", default);

        await _inBasket.Received(1).GetForProviderAsync("doc-1", "refill", Arg.Any<CancellationToken>());
    }

    [Fact] // happy: 'message' del seam es 'advice' en la bandeja del clínico (lo que la app conoce).
    public async Task InBasket_UnMensaje_LlegaComoAdvice()
    {
        _inBasket.GetForProviderAsync("doc-1", null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new EhrInBasketItem("i-1", "message", "doc-1", "pat-1", "Jorge Medina",
                "Consulta del paciente", "¿Puedo…?", "normal", DateTime.UnixEpoch, "t-1"),
        });

        var body = Json(await BuildSut().InBasket("doc-1", null, default));

        Assert.Equal("advice", body.GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact] // happy: la app lee el statement bajo su propia clave, con los montos en entero.
    public async Task Billing_ElEstadoDeCuenta_SaleBajoStatement()
    {
        _billing.GetForPatientAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new EhrBillingStatement(
            PatientId: "pat-1",
            Statement: new[]
            {
                new EhrBillingLine("l-1", new DateTime(2026, 3, 4), "Consulta de control", 180_000m, 36_000m, "due"),
            },
            Balance: 36_000m, Currency: "COP",
            Plan: new EhrInsurancePlan("Sura Clásico", "M-1", "80%", 36_000m)));

        var statement = Json(await BuildSut().Billing("pat-1", default)).GetProperty("statement");

        Assert.Equal(36_000L, statement.GetProperty("balanceMinor").GetInt64());
        Assert.True(statement.GetProperty("planActive").GetBoolean());
        // amountMinor = responsabilidad del paciente, no el cargo bruto: cobrar 180.000 donde
        // el paciente debe 36.000 es el defecto que no se ve hasta que alguien paga.
        Assert.Equal(36_000L, statement.GetProperty("lines")[0].GetProperty("amountMinor").GetInt64());
    }

    [Fact] // filtro: el home sólo cuenta como no leídos los mensajes CLÍNICOS.
    public async Task PortalHome_LosNoLeidos_SoloCuentanLosClinicos()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("t-1", "clinical:advice:pat-1", mensajes: 3),
            Resumen("t-2", "shop:order:o-9", mensajes: 5),
        });

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        Assert.Equal(3, body.GetProperty("unreadMessages").GetInt32());
    }

    [Fact] // filtro: el e-Check-In aparece dentro de 48 h y NO antes.
    public async Task PortalHome_ECheckIn_SoloDentroDe48h()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven(Cita(startUtc: DateTime.UtcNow.AddHours(20)));

        var cerca = Json(await BuildSut().PortalHome("pat-1", default));
        Assert.Equal(1, cerca.GetProperty("pendingCheckins").GetInt32());

        CitasDelPacienteDevuelven(Cita(startUtc: DateTime.UtcNow.AddDays(9)));
        var lejos = Json(await BuildSut().PortalHome("pat-1", default));
        Assert.Equal(0, lejos.GetProperty("pendingCheckins").GetInt32());
    }

    [Fact] // filtro: una cita cancelada no es «tu próxima cita».
    public async Task PortalHome_LaCitaCancelada_NoEsLaProxima()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven(
            Cita(id: "a-cancelada", startUtc: DateTime.UtcNow.AddDays(1), status: "cancelled"),
            Cita(id: "a-viva", startUtc: DateTime.UtcNow.AddDays(2)));

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        Assert.Equal("a-viva", body.GetProperty("nextAppointment").GetProperty("id").GetString());
    }

    [Fact] // vacío: sin citas, `nextAppointment` es null — no una cita de relleno.
    public async Task PortalHome_SinCitas_NoInventaLaProxima()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextAppointment").ValueKind);
        Assert.Equal(0, body.GetProperty("pendingCheckins").GetInt32());
    }

    [Fact] // happy: la app lee el resultado por `name`/`value`/`flag`, no por los nombres del seam.
    public async Task Results_SalenConLasClavesQueLeeLaApp()
    {
        _results.GetForPatientAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new EhrLabResult("r-1", "pat-1", "Perfil lipídico", "Colesterol total", "232", "mg/dL",
                0, 200, "high", new DateTime(2026, 3, 4), "o-1", "doc-1", "Repetir en ayunas."),
        });

        var r = Json(await BuildSut().Results("pat-1", default)).GetProperty("results")[0];

        Assert.Equal("Colesterol total", r.GetProperty("name").GetString());
        Assert.Equal("Perfil lipídico", r.GetProperty("panel").GetString());
        Assert.Equal("232", r.GetProperty("value").GetString());
        Assert.Equal("high", r.GetProperty("flag").GetString());
        Assert.Equal("2026-03-04", r.GetProperty("date").GetString());
    }

    [Fact] // happy: `drug`/`dose` son las claves de la app; el seam las llama de otra forma.
    public async Task Medications_SalenConLasClavesQueLeeLaApp()
    {
        _medications.GetActiveForPatientAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new EhrMedication("m-1", "pat-1", "Losartán", "50 mg", "cada 12 h", "Con alimento.",
                "doc-1", "Dra. Ana Rojas", new DateTime(2026, 1, 2), "active", 2),
        });

        var m = Json(await BuildSut().Medications("pat-1", default)).GetProperty("medications")[0];

        Assert.Equal("Losartán", m.GetProperty("drug").GetString());
        Assert.Equal("50 mg", m.GetProperty("dose").GetString());
        Assert.Equal(2, m.GetProperty("refillsLeft").GetInt32());
    }

    private static MessageThreadSummary Resumen(
        string threadId, string contextRef, string preview = "…", int mensajes = 1) => new(
        ThreadId: threadId, ContextRef: contextRef,
        Participants: new[] { "pat-1", "doc-1" },
        LastMessagePreview: preview,
        LastMessageAt: DateTimeOffset.UnixEpoch, MessageCount: mensajes);
}
