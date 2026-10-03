using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

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
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private EhrController BuildSut() => new(
        _patients, _doctors, _records, _prescriptions, _scheduling,
        _results, _medications, _orders, _billing, _inBasket, _messaging,
        new EsCoPriceFormatter(new CartSettings()), _gate);

    /// <summary>
    /// La sesión de partida (#197): un PACIENTE —un miembro sin rol clínico cuyo correo lleva a la
    /// historia <c>pat-1</c>—, que es quien usa el portal. Lo clínico lo pide cada test con
    /// <see cref="Clinico"/>, y lo anónimo con <see cref="Anonimo"/>.
    /// </summary>
    public EhrControllerTests()
    {
        SesionDe("jorge@correo.co");
        _patients.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Paciente());
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<MedicalDoctor>());
    }

    /// <summary>
    /// Un miembro con sesión y estos roles. <c>HasAnyRole</c> se responde como el gate de verdad
    /// —el CSV pedido contra los roles del miembro—, así que un test con el rol equivocado sale 403
    /// aunque el controlador pida otra lista.
    /// </summary>
    private void SesionDe(string correo, params string[] roles)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns(correo);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(ci => (ci.Arg<string?>() ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)));
    }

    private void Clinico(string rol = "medico", string correo = "medica@clinica.co") => SesionDe(correo, rol);

    private void Anonimo()
    {
        _gate.IsAuthenticated.Returns(false);
        _gate.CurrentMemberEmail.Returns((string?)null);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

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

    private static MedicalDoctor Medico(
        string id = "doc-1",
        string phone = "",
        string email = "",
        bool? admitePacientes = null) => new(
        Id: id, FullName: "Dra. Ana Rojas", Specialty: "Medicina interna",
        LicenseNumber: "RM-1234", Rating: 4.8, YearsExperience: 12, AvatarUrl: null,
        WorkingDays: new[] { DayOfWeek.Monday, DayOfWeek.Wednesday },
        SlotStartHour: 8, SlotEndHour: 16, SlotMinutes: 30,
        Phone: phone, Email: email, AcceptingPatients: admitePacientes);

    private static EhrMedication Medicamento(string id = "med-1") => new(
        MedicationId: id, PatientId: "pat-1", MedicationName: "Losartán",
        Dosage: "50 mg", Frequency: "cada 12 h", Instructions: "Con alimento.",
        PrescribedByDoctorId: "doc-1", PrescribedByDoctorName: "Dra. Ana Rojas",
        PrescribedAtUtc: new DateTime(2026, 1, 2), Status: "active", RefillsRemaining: 2);

    /// <summary>
    /// Lo que devuelve el padrón: por id (la clínica) y por el correo de la sesión (el portal, #197).
    /// <c>null</c> es, para el portal, un miembro cuya cuenta no tiene historia vinculada.
    /// </summary>
    private void PadronDevuelve(EhrPatient? p)
    {
        _patients.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(p);
        _patients.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(p);
    }

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
            // El paciente es el de la sesión (#197): cada vuelta, una sesión vinculada a otro id.
            PadronDevuelve(Paciente(id: id, age: 52, gender: "F"));
            respuestas.Add(Raw(await BuildSut().Health(id, default)).Replace(id, "{ID}", StringComparison.Ordinal));
        }

        Assert.Single(respuestas.Distinct(StringComparer.Ordinal));
    }

    [Fact] // Si el paciente llegó antes es un HECHO; salía de una moneda al aire.
    public async Task Schedule_NoEmiteCheckedInAhead()
    {
        Clinico();

        AgendaDelDiaDevuelve(Cita());

        var body = Json(await BuildSut().Schedule("2026-09-20", default));

        var slot = body.GetProperty("slots")[0];
        Assert.False(slot.TryGetProperty("checkedInAhead", out _), "la llegada anticipada no la sabe este borde");
    }

    [Fact] // Misma propiedad sobre el tablero: la fila no puede depender del id de la cita.
    public async Task Schedule_LaFilaDelTablero_NoDependeDelIdDeLaCita()
    {
        Clinico();

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

    [Fact] // De quien no consta, no se afirma: sigue viajando `null` y no `false`.
    public async Task Doctors_DeQuienNoConsta_NoDeclaraSiAceptaPacientesNuevos()
    {
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Medico() });

        var d = Json(await BuildSut().Doctors(null, default)).GetProperty("doctors")[0];

        // `null` y `false` no son lo mismo: cerrarle la lista a un médico que sí recibe es tan
        // falso como abrírsela al que no. El staff sembrado no lo sabe, así que dice que no lo
        // sabe — y el que SÍ lo sabe lo dice, que es el test de abajo.
        Assert.True(d.TryGetProperty("acceptingPatients", out var acepta));
        Assert.Equal(JsonValueKind.Null, acepta.ValueKind);
    }

    /// <summary>
    /// Lo que el seam sí sabe, el borde lo dice — y no lo vuelve a escribir a mano (#118).
    /// </summary>
    /// <remarks>
    /// <b>El fixture lleva el caso que el default NO produce.</b> Hasta el #118 el borde
    /// escribía <c>null</c> y dos cadenas vacías porque <c>MedicalDoctor</c> no traía contacto;
    /// ahora lo trae cuando el profesional se autoró en el CMS. Con un solo médico «no consta»,
    /// pasar el dato del seam o volver a fabricarlo da EXACTAMENTE el mismo JSON, así que la
    /// regla sólo se exige con uno que diga que NO admite y con un teléfono escrito: son los
    /// valores que ninguna constante produce.
    /// </remarks>
    [Fact]
    public async Task Doctors_ElContactoYLaListaAbierta_SalenDelSeamYNoDelBorde()
    {
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                Medico(phone: "+57 604 448 0901", email: "ana.rios@example.co", admitePacientes: false),
            });

        var d = Json(await BuildSut().Doctors(null, default)).GetProperty("doctors")[0];

        Assert.Equal("+57 604 448 0901", d.GetProperty("phone").GetString());
        Assert.Equal("ana.rios@example.co", d.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.False, d.GetProperty("acceptingPatients").ValueKind);
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
        Clinico();

        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Paciente() });

        var p = Json(await BuildSut().Patients(null, default)).GetProperty("patients")[0];

        Assert.True(p.TryGetProperty("active", out var activo));
        Assert.Equal(JsonValueKind.Null, activo.ValueKind);
    }

    [Fact]
    public async Task Patients_LaFichaDelPaciente_NoSeDerivaDelIdentificador()
    {
        Clinico();

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
        Clinico();

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
        Clinico();

        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        await BuildSut().Patient("pat-1", default);

        await _scheduling.Received(1).GetForPatientAsync(
            "pat-1", hoy.AddDays(-30), hoy.AddDays(60), Arg.Any<CancellationToken>());
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // 2 · Quién llama (#197): el paciente sale de la sesión y la clínica exige rol
    // ══════════════════════════════════════════════════════════════════════════════
    //
    // Antes el borde era anónimo y el sujeto lo ponía el navegador: `?patient=` de otro era leer
    // su historia, y un 400 con el parámetro en blanco era todo lo que había que probar. Ahora
    // el paciente del portal es el de la historia vinculada al correo del miembro, y lo que mande
    // el navegador se IGNORA. Los tests del IDOR miran el ARGUMENTO que llega a los seams, no el
    // código de respuesta: un 200 sale igual con el hueco abierto
    // (`feedback_guard_must_rewrite_the_usage`).

    /// <summary>Lo que se pide al portal, con el paciente que manda la petición —que se ignora—.</summary>
    private Task<IActionResult> Portal(string accion, string pacienteDeLaPeticion) => accion switch
    {
        "portal/home" => BuildSut().PortalHome(pacienteDeLaPeticion, default),
        "results" => BuildSut().Results(pacienteDeLaPeticion, default),
        "medications" => BuildSut().Medications(pacienteDeLaPeticion, default),
        "billing" => BuildSut().Billing(pacienteDeLaPeticion, default),
        "health" => BuildSut().Health(pacienteDeLaPeticion, default),
        "refill" => BuildSut().Refill(
            new EhrController.RefillBody(PatientId: pacienteDeLaPeticion, MedicationId: "med-1"), default),
        "messages" => BuildSut().Messages(pacienteDeLaPeticion, default),
        _ => throw new ArgumentOutOfRangeException(nameof(accion), accion, null),
    };

    public static TheoryData<string> AccionesDelPortal() => new()
    {
        "portal/home", "results", "medications", "billing", "health", "refill", "messages",
    };

    /// <summary>Todo lo que recibieron los seams que leen datos de un paciente, como texto.</summary>
    /// <remarks>
    /// Los argumentos se miran por su <c>ToString()</c>: un id llega suelto o dentro de un record
    /// (<c>RefillRequest { PatientId = … }</c>), y así una sola regla cubre las dos formas.
    /// </remarks>
    private List<string> LoQueRecibieronLosSeams()
        => new object[] { _patients, _records, _scheduling, _results, _medications, _billing, _messaging }
            .SelectMany(s => s.ReceivedCalls())
            .SelectMany(c => c.GetArguments())
            .Select(a => a?.ToString() ?? string.Empty)
            .ToList();

    [Theory, MemberData(nameof(AccionesDelPortal))] // EL IDOR: la sesión de A y el paciente de B.
    public async Task Portal_ConLaSesionDeA_YElPacienteDeB_LosSeamsRecibenA(string accion)
    {
        SesionDe("ana@correo.co");
        PadronDevuelve(Paciente(id: "pat-A"));
        _medications.RequestRefillAsync(Arg.Any<RefillRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new EhrRefillRequest("rf-1", ci.Arg<RefillRequest>().PatientId, "med-1", "Losartán",
                "doc-1", DateTime.UnixEpoch, "pending", null));

        await Portal(accion, "pat-B");

        var recibido = LoQueRecibieronLosSeams();
        Assert.DoesNotContain(recibido, a => a.Contains("pat-B", StringComparison.Ordinal));
        Assert.Contains(recibido, a => a.Contains("pat-A", StringComparison.Ordinal));
        await _patients.Received().FindByEmailAsync("ana@correo.co", Arg.Any<CancellationToken>());
    }

    [Theory, MemberData(nameof(AccionesDelPortal))] // anónimo: 401, y ni se pregunta por nadie.
    public async Task Portal_Anonimo_401_YNoTocaLosSeams(string accion)
    {
        Anonimo();

        var respuesta = await Portal(accion, "pat-1");

        var rechazo = Assert.IsType<UnauthorizedObjectResult>(respuesta);
        Assert.Contains("error", JsonSerializer.Serialize(rechazo.Value, Web), StringComparison.Ordinal);
        Assert.Empty(LoQueRecibieronLosSeams());
    }

    [Theory, MemberData(nameof(AccionesDelPortal))] // un miembro sin historia: 404 con el motivo, no la del de la URL.
    public async Task Portal_MiembroSinHistoriaVinculada_404(string accion)
    {
        PadronDevuelve(null);

        var respuesta = await Portal(accion, "pat-1");

        var rechazo = Assert.IsType<NotFoundObjectResult>(respuesta);
        Assert.Equal("Tu cuenta no tiene una historia clínica vinculada.",
            JsonSerializer.SerializeToElement(rechazo.Value, Web).GetProperty("error").GetString());
        Assert.DoesNotContain(LoQueRecibieronLosSeams(), a => a.Contains("pat-1", StringComparison.Ordinal));
    }

    [Fact] // el paciente se agenda a SÍ MISMO: el `patientId` del cuerpo no cuenta.
    public async Task Appointment_ElPaciente_SeAgendaASiMismo()
    {
        SesionDe("ana@correo.co");
        PadronDevuelve(Paciente(id: "pat-A"));
        _scheduling.BookAsync(Arg.Any<BookAppointmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Cita(patientId: ci.Arg<BookAppointmentRequest>().PatientId));

        await BuildSut().BookAppointment(new EhrController.BookAppointmentBody(
            "pat-B", "doc-1", JsonSerializer.SerializeToElement("2026-11-02T14:00:00Z")), default);

        await _scheduling.Received(1).BookAsync(
            Arg.Is<BookAppointmentRequest>(r => r.PatientId == "pat-A"), Arg.Any<CancellationToken>());
    }

    [Fact] // …y el clínico agenda a nombre del paciente que dice el cuerpo.
    public async Task Appointment_ElClinico_AgendaAlPacienteDelCuerpo()
    {
        Clinico("enfermeria");
        _scheduling.BookAsync(Arg.Any<BookAppointmentRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => Cita(patientId: ci.Arg<BookAppointmentRequest>().PatientId));

        await BuildSut().BookAppointment(new EhrController.BookAppointmentBody(
            "pat-B", "doc-1", JsonSerializer.SerializeToElement("2026-11-02T14:00:00Z")), default);

        await _scheduling.Received(1).BookAsync(
            Arg.Is<BookAppointmentRequest>(r => r.PatientId == "pat-B"), Arg.Any<CancellationToken>());
        await _patients.DidNotReceive().FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // escribir con el nombre de otro era hablarle a su médico por él.
    public async Task Message_ElRemitente_EsElPacienteDeLaSesion()
    {
        SesionDe("ana@correo.co");
        PadronDevuelve(Paciente(id: "pat-A"));
        _messaging.StartThreadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new MessageThread("t-1", ci.ArgAt<string>(0), new[] { ci.ArgAt<string>(1), "doc-1" },
                Array.Empty<ThreadMessage>(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

        await BuildSut().SendMessage(new EhrController.SendMessageBody(
            From: "pat-B", To: "doc-1", Body: "Hola", User: "pat-B"), default);

        await _messaging.Received(1).StartThreadAsync(
            Arg.Any<string>(), "pat-A", "doc-1", "Hola", Arg.Any<CancellationToken>());
        Assert.DoesNotContain(LoQueRecibieronLosSeams(), a => a.Contains("pat-B", StringComparison.Ordinal));
    }

    /// <summary>La superficie clínica, con lo mínimo que cada acción necesita para llegar a su seam.</summary>
    private Task<IActionResult> Clinica(string accion) => accion switch
    {
        "patients" => BuildSut().Patients(null, default),
        "patient" => BuildSut().Patient("pat-1", default),
        "appointments" => BuildSut().Appointments("2026-09-20", null, default),
        "schedule" => BuildSut().Schedule("2026-09-20", default),
        "encounter" => BuildSut().AddEncounter(new EhrController.AddEncounterBody(
            "pat-1", new EhrController.SoapBody("s", null, "a", "p", null), "doc-1", null, null), default),
        "prescription" => BuildSut().AddPrescription(new EhrController.AddPrescriptionBody(
            "pat-1", new[] { new EhrController.PrescriptionItemBody(Drug: "Losartán", Dose: "50 mg") }, "doc-1", null), default),
        "inbasket" => BuildSut().InBasket("doc-1", null, default),
        "order" => BuildSut().PlaceOrder(new EhrController.PlaceOrderBody(
            PatientId: "pat-1", Kind: "lab", Detail: "Hemograma", Provider: "doc-1"), default),
        _ => throw new ArgumentOutOfRangeException(nameof(accion), accion, null),
    };

    public static TheoryData<string> AccionesClinicas() => new()
    {
        "patients", "patient", "appointments", "schedule", "encounter", "prescription", "inbasket", "order",
    };

    /// <summary>Si algún seam clínico recibió algo.</summary>
    private bool AlgunSeamClinicoRecibio()
        => new object[] { _patients, _records, _prescriptions, _scheduling, _inBasket, _orders }
            .Any(s => s.ReceivedCalls().Any());

    [Theory, MemberData(nameof(AccionesClinicas))]
    public async Task Clinica_Anonimo_401(string accion)
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await Clinica(accion));
        Assert.False(AlgunSeamClinicoRecibio());
    }

    [Theory, MemberData(nameof(AccionesClinicas))] // tener una historia no hace a nadie clínico.
    public async Task Clinica_SinRolClinico_403_YNoTocaLosSeams(string accion)
    {
        var respuesta = await Clinica(accion);

        // StatusCode(403) y no Forbid(): con auth de miembros, Forbid redirige al login.
        var rechazo = Assert.IsType<ObjectResult>(respuesta);
        Assert.Equal(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden, rechazo.StatusCode);
        Assert.False(AlgunSeamClinicoRecibio());
    }

    [Theory] // quién entra a la clínica: médico, enfermería, admin — y nadie más.
    [InlineData("medico", true)]
    [InlineData("enfermeria", true)]
    [InlineData("admin", true)]
    [InlineData("organizador", false)]
    // El vocabulario del núcleo PHI (`DefaultPhiAccessGuard`: doctor,nurse,reception) NO entra
    // acá: queda fijado para que unificarlos sea una decisión y no un accidente.
    [InlineData("doctor", false)]
    public async Task Clinica_LosRolesQueEntran(string rol, bool entra)
    {
        Clinico(rol);
        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<EhrPatient>());

        var respuesta = await BuildSut().Patients(null, default);

        Assert.Equal(entra, respuesta is OkObjectResult);
    }

    [Fact] // la bandeja es la del médico de la sesión, no la que diga la URL.
    public async Task InBasket_ElMedicoVinculado_LeeSuBandeja_YNoLaDeLaUrl()
    {
        Clinico(correo: "Ana.Rios@Clinica.co");
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Medico(id: "doc-ana", email: "ana.rios@clinica.co"), Medico(id: "doc-otro", email: "otro@clinica.co") });
        _inBasket.GetForProviderAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrInBasketItem>());

        await BuildSut().InBasket("doc-otro", null, default);

        await _inBasket.Received(1).GetForProviderAsync("doc-ana", null, Arg.Any<CancellationToken>());
        await _inBasket.DidNotReceive().GetForProviderAsync("doc-otro", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // la nota la firma el médico de la sesión, aunque el cuerpo diga otro.
    public async Task Encounter_LoFirmaElMedicoDeLaSesion()
    {
        Clinico(correo: "ana.rios@clinica.co");
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Medico(id: "doc-ana", email: "ana.rios@clinica.co") });
        _records.AddEncounterAsync(Arg.Any<AddEncounterRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ClinicalEncounter("enc-1", "pat-1", ci.Arg<AddEncounterRequest>().DoctorId, "Dra. Ana Ríos",
                DateTime.UnixEpoch, "Control", ci.Arg<AddEncounterRequest>().Soap, null, true));

        await BuildSut().AddEncounter(new EhrController.AddEncounterBody(
            "pat-1", new EhrController.SoapBody("s", null, "a", "p", null), "doc-otro", null, null), default);

        await _records.Received(1).AddEncounterAsync(
            Arg.Is<AddEncounterRequest>(r => r.DoctorId == "doc-ana"), Arg.Any<CancellationToken>());
    }

    [Fact] // el clínico lee los mensajes como su médico vinculado; `?user=` no cuenta.
    public async Task Messages_ElClinico_LeeComoSuMedicoVinculado()
    {
        Clinico(correo: "ana.rios@clinica.co");
        _doctors.ListAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Medico(id: "doc-ana", email: "ana.rios@clinica.co") });

        await BuildSut().Messages("pat-1", default);

        await _messaging.Received(1).GetInboxAsync("doc-ana", Arg.Any<CancellationToken>());
        await _messaging.DidNotReceive().GetInboxAsync("pat-1", Arg.Any<CancellationToken>());
    }

    [Fact] // `doctors` y `copay` no son datos de nadie: siguen abiertos.
    public async Task Doctors_Y_Copago_SiguenPublicos()
    {
        Anonimo();
        _scheduling.CopayAsync(Arg.Any<CancellationToken>()).Returns(new ClinicalCopay(80_000m, "COP"));

        Assert.IsType<OkObjectResult>(await BuildSut().Doctors(null, default));
        Assert.IsType<OkObjectResult>(await BuildSut().Copay(default));
    }

    [Theory, MemberData(nameof(SinSujeto))] // un clínico sin médico vinculado tiene que decir de quién es la bandeja.
    public async Task InBasket_SinProveedor_400(string provider)
    {
        Clinico("enfermeria");
        Assert.IsType<BadRequestObjectResult>(await BuildSut().InBasket(provider, null, default));
    }

    public static TheoryData<string> SinSujeto() => new() { "", "   " };

    [Theory, MemberData(nameof(SinSujeto))]
    public async Task Patient_SinId_400(string id)
    {
        Clinico();
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Patient(id, default));
    }

    [Fact]
    public async Task Patient_Inexistente_404()
    {
        Clinico();

        PadronDevuelve(null);
        Assert.IsType<NotFoundObjectResult>(await BuildSut().Patient("pat-fantasma", default));
    }

    [Fact]
    public async Task Health_SinHistoriaVinculada_404_YNoConsultaLaHistoria()
    {
        PadronDevuelve(null);

        Assert.IsType<NotFoundObjectResult>(await BuildSut().Health("pat-fantasma", default));
        await _records.DidNotReceive().GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        Clinico();

        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPatient>());

        var body = Json(await BuildSut().Patients("nadie", default));

        Assert.Equal(0, body.GetProperty("patients").GetArrayLength());
    }

    [Fact] // filtro: el término de búsqueda llega TAL CUAL al padrón (la caja de búsqueda no era decorativa).
    public async Task Patients_ElTerminoDeBusqueda_LlegaAlPadron()
    {
        Clinico();

        _patients.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrPatient>());

        await BuildSut().Patients("medina", default);

        await _patients.Received(1).SearchAsync("medina", Arg.Any<CancellationToken>());
    }

    [Fact] // happy: el sexo del padrón es texto libre y la app lee el código 'M'|'F'|'X'.
    public async Task Patients_ElSexo_SaleComoCodigo()
    {
        Clinico();

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
        Clinico();

        AgendaDelDiaDevuelve();

        await BuildSut().Appointments("2026-09-20", "doc-7", default);

        await _scheduling.Received(1).GetByDateAsync(
            new DateOnly(2026, 9, 20), "doc-7", Arg.Any<CancellationToken>());
    }

    [Fact] // vacío: una fecha ilegible cae a HOY, no vacía el tablero ni revienta.
    public async Task Appointments_FechaIlegible_CaeAHoy()
    {
        Clinico();

        AgendaDelDiaDevuelve();

        await BuildSut().Appointments("no-es-una-fecha", null, default);

        await _scheduling.Received(1).GetByDateAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), null, Arg.Any<CancellationToken>());
    }

    [Fact] // filtro: el tablero del día ordena por hora, que es como se lee de arriba abajo.
    public async Task Schedule_OrdenaPorHora()
    {
        Clinico();

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
        Clinico();

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
        Clinico();

        _inBasket.GetForProviderAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EhrInBasketItem>());

        await BuildSut().InBasket("doc-1", "refill", default);

        await _inBasket.Received(1).GetForProviderAsync("doc-1", "refill", Arg.Any<CancellationToken>());
    }

    [Fact] // happy: 'message' del seam es 'advice' en la bandeja del clínico (lo que la app conoce).
    public async Task InBasket_UnMensaje_LlegaComoAdvice()
    {
        Clinico();

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

        Assert.Equal(3_600_000L, statement.GetProperty("balanceMinor").GetInt64()); // 36.000 pesos en centavos (#196, G-13)
        Assert.True(statement.GetProperty("planActive").GetBoolean());
        // amountMinor = responsabilidad del paciente, no el cargo bruto: cobrar 180.000 donde
        // el paciente debe 36.000 es el defecto que no se ve hasta que alguien paga.
        Assert.Equal(3_600_000L, statement.GetProperty("lines")[0].GetProperty("amountMinor").GetInt64());
    }

    // El home NO cuenta «sin leer»: no hay read-receipts en `IMessagingService`, así que
    // no hay nada que contar. Lo que había era una DERIVACIÓN —la suma de `MessageCount`
    // de los hilos clínicos— que se leía como un dato: contaba como sin leer los mensajes
    // que el propio paciente escribió. El fixture lo exige con hilos de VARIOS mensajes,
    // porque con un mensaje por hilo la suma y el conteo de hilos dan lo mismo y la
    // fabricación pasaría en verde.
    [Fact] // #116 — nulo es «no consta», y NO es «no tienes mensajes sin leer».
    public async Task PortalHome_LosSinLeer_NoSeFabrican()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("t-1", "clinical:advice:pat-1", mensajes: 3),
            Resumen("t-2", "shop:order:o-9", mensajes: 5),
        });

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        // La clave se conserva DECLARADA —quitarla dejaría al normalizador del cliente
        // reponiendo su propio default— y sale nula.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("unreadMessages").ValueKind);
    }

    [Fact] // filtro: la tarjeta de mensajes cuenta CONVERSACIONES clínicas, que sí es un hecho.
    public async Task PortalHome_LaTarjetaDeMensajes_CuentaHilosClinicos()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("t-1", "clinical:advice:pat-1", mensajes: 3),
            Resumen("t-2", "clinical:advice:pat-1", mensajes: 4),
            Resumen("t-3", "shop:order:o-9", mensajes: 5),
        });

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        var tarjeta = body.GetProperty("cards").EnumerateArray()
            .Single(c => c.GetProperty("kind").GetString() == "message");
        // DOS conversaciones clínicas, no SIETE mensajes ni los doce de la bandeja entera.
        Assert.Equal("Tienes 2 conversaciones con tu equipo de salud.",
            tarjeta.GetProperty("detail").GetString());
    }

    [Fact] // vacío: sin hilos clínicos no hay tarjeta de mensajes que enseñar.
    public async Task PortalHome_SinHilosClinicos_NoHayTarjetaDeMensajes()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        _messaging.GetInboxAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new[]
        {
            Resumen("t-2", "shop:order:o-9", mensajes: 5),
        });

        var body = Json(await BuildSut().PortalHome("pat-1", default));

        Assert.DoesNotContain(body.GetProperty("cards").EnumerateArray(),
            c => c.GetProperty("kind").GetString() == "message");
    }

    /// <summary>
    /// El importe de la tarjeta de saldo sale en es-CO —«$ 123.500»—, sea cual sea la cultura del
    /// hilo que atiende la petición.
    /// </summary>
    /// <remarks>
    /// <para>Se formateaba con <c>{saldo:N0}</c>, o sea con la cultura del HILO: una API no pasa por
    /// el ruteo de Umbraco y no tiene la del sitio, así que en vivo, en un sitio es-CO, salía
    /// «123,500 COP» —el separador de miles en-US, que en Colombia se lee como decimal—.</para>
    ///
    /// <para><b>El hilo se fija en en-US a propósito</b>, que es lo que tenía el servidor medido. Con
    /// la cultura de la máquina de quien corre los tests —es-CO en la del arquitecto— el defecto
    /// daría «123.500 COP» y un test que solo buscara «123.500» pasaría en verde con él puesto.</para>
    /// </remarks>
    [Fact]
    public async Task PortalHome_ElSaldo_SeEscribeEnEsCo_YNoEnLaCulturaDelHilo()
    {
        PadronDevuelve(Paciente());
        CitasDelPacienteDevuelven();
        _billing.GetForPatientAsync("pat-1", Arg.Any<CancellationToken>()).Returns(new EhrBillingStatement(
            PatientId: "pat-1",
            Statement: Array.Empty<EhrBillingLine>(),
            Balance: 123_500m, Currency: "COP",
            Plan: new EhrInsurancePlan("Sura Clásico", "M-1", "80%", 36_000m)));

        var antes = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var body = Json(await BuildSut().PortalHome("pat-1", default));

            var tarjeta = body.GetProperty("cards").EnumerateArray()
                .Single(c => c.GetProperty("kind").GetString() == "balance");
            Assert.Equal("Tienes un saldo de $ 123.500 por pagar.", tarjeta.GetProperty("detail").GetString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = antes;
        }
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

    // ── El copago (CMS#196) ─────────────────────────────────────────────────

    [Fact] // Lo que cuesta agendar, en mayores y en menores (las del carrito), de la fuente que cobra.
    public async Task El_copago_sale_del_motor_que_lo_cobra()
    {
        _scheduling.CopayAsync(Arg.Any<CancellationToken>()).Returns(new ClinicalCopay(80_000m, "COP"));

        var ok = Assert.IsType<OkObjectResult>(await BuildSut().Copay(CancellationToken.None));

        Assert.Equal(new EhrController.CopayDto(80_000m, 8_000_000, "COP"), ok.Value);
    }

    [Fact] // Sin saberlo, 503 y no un cero: un cero diría «no se cobra».
    public async Task Sin_copago_conocido_el_borde_no_inventa_un_cero()
    {
        _scheduling.CopayAsync(Arg.Any<CancellationToken>()).Returns((ClinicalCopay?)null);

        var resultado = Assert.IsType<ObjectResult>(await BuildSut().Copay(CancellationToken.None));

        Assert.Equal(503, resultado.StatusCode);
    }

}
