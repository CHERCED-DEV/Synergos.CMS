using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Salud;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical Healthcare (ADR 0098). La consume la app clínica
/// Angular <c>&lt;synergos-healthcare&gt;</c>. <strong>RECORD-KEEPER</strong>: registra
/// historia/agenda/recetas/consentimiento — NO diagnostica ni aconseja (sin
/// endpoints con verbos clínicos: suggest/diagnose/recommend).
/// </summary>
/// <remarks>
/// <see cref="IPhiAccessGuard"/> se invoca PRIMERO en cada endpoint (audita +
/// decide). Fail-closed: si el guard niega, no se toca ningún dato. 401 si no
/// autenticado, 403 si autenticado sin permiso.
/// </remarks>
[ApiController]
[Route("api/healthcare")]
public sealed class HealthcareApiController : ControllerBase
{
    private readonly IPhiAccessGuard _guard;
    private readonly IPatientRepository _patients;
    private readonly IAppointmentScheduler _appointments;
    private readonly IPrescriptionService _prescriptions;
    private readonly IConsentLedger _consent;
    private readonly IMemberAccessGate _gate;

    /// <summary>Lee, cambia y guarda la historia bajo su candado (I2 T057).</summary>
    private readonly EditorDeLaHistoriaClinica _editor;

    /// <summary>El reloj y la zona del sitio: de ahí sale el «hoy» de una vacuna declarada.</summary>
    private readonly TimeProvider _reloj;
    private readonly TimeZoneInfo _zona;

    /// <summary>Para qué se entra a la historia en cada operación de I2: va a la auditoría (T064).</summary>
    internal static class Propositos
    {
        public const string CheckIn = "salud.check-in";
        public const string ResumenPropio = "salud.resumen";
        public const string Historia = "salud.historia";
        public const string Apertura = "salud.apertura";
        public const string Busqueda = "salud.busqueda";
        public const string Agenda = "salud.agenda-del-dia";
        public const string Llegada = "salud.llegada";
        public const string Firma = "salud.firma";
        public const string Ordenes = "salud.orden";
        public const string Resultado = "salud.resultado";
    }

    /// <summary>El ámbito de los informes del laboratorio en el almacén privado cifrado (I4 P12).</summary>
    public const string AmbitoDeLosInformes = "lab-results";

    /// <summary>Las órdenes médicas y sus resultados, en el almacén PHI (I4).</summary>
    private readonly IOrdenesMedicas? _ordenes;

    /// <summary>El PDF de cada informe, cifrado (T6).</summary>
    private readonly IPrivateFileStore? _archivos;

    /// <summary>La consulta en el orquestador: la agenda del día, la llegada y el sello de la nota (I3).</summary>
    private readonly IConsultaDeSalud? _consulta;

    /// <summary>Las notas de la consulta, en el almacén PHI (I3 P6).</summary>
    private readonly INotasDeLaConsulta? _notas;

    /// <summary>El propósito de la llave del consultorio en <c>Api.Signing</c> (Q14).</summary>
    private readonly string _llaveDeFirma;

    /// <summary>La clave del miembro por su correo, para abrirle la historia (T056).</summary>
    private readonly IBuscadorDeMiembros? _miembros;

    /// <summary>El permiso del paciente para el consultorio, en <c>Api.Consent</c>: «Mi salud» le pide darlo si falta (T062).</summary>
    private readonly IConsentReader? _permisos;

    /// <param name="editor">Opcional: sin él se usa uno sobre el mismo repositorio (las pruebas que construyen el
    ///   controlador a mano no cambian). En el sitio es el singleton, con su candado compartido.</param>
    public HealthcareApiController(
        IPhiAccessGuard guard,
        IPatientRepository patients,
        IAppointmentScheduler appointments,
        IPrescriptionService prescriptions,
        IConsentLedger consent,
        IMemberAccessGate gate,
        EditorDeLaHistoriaClinica? editor = null,
        TimeProvider? reloj = null,
        IOptions<ListadosSettings>? listados = null,
        IBuscadorDeMiembros? miembros = null,
        IConsentReader? permisos = null,
        IConsultaDeSalud? consulta = null,
        INotasDeLaConsulta? notas = null,
        IOptions<HealthcareSettings>? salud = null,
        IOrdenesMedicas? ordenes = null,
        IPrivateFileStore? archivos = null)
    {
        _ordenes = ordenes;
        _archivos = archivos;
        _miembros = miembros;
        _permisos = permisos;
        _consulta = consulta;
        _notas = notas;
        _llaveDeFirma = salud?.Value.LlaveDeFirma ?? new HealthcareSettings().LlaveDeFirma;
        _guard = guard;
        _patients = patients;
        _appointments = appointments;
        _prescriptions = prescriptions;
        _consent = consent;
        _gate = gate;
        _editor = editor ?? new EditorDeLaHistoriaClinica(patients);
        _reloj = reloj ?? TimeProvider.System;
        _zona = listados?.Value.Zona() ?? TimeZoneInfo.Utc;
    }

    /// <summary>
    /// El expediente del paciente que está pidiendo. Es la puerta del portal del paciente.
    /// </summary>
    /// <remarks>
    /// <b>Sin este endpoint el portal no podía existir</b>, y no por falta de permiso: el
    /// auto-acceso del guard ya deja que un paciente lea lo suyo, pero cada endpoint se
    /// direcciona por <c>patientKey</c>, que es una clave clínica DISTINTA del
    /// <c>MemberKey</c> —así lo exige el RTBF— y que nadie le dice al paciente. El permiso
    /// estaba concedido y era inalcanzable: había que adivinar un GUID.
    ///
    /// <para><b>El orden de las tres operaciones es la parte de seguridad.</b>
    /// (1) Se corta al anónimo ANTES de tocar el repositorio, que es la propiedad que
    /// <c>GetPatient_Anonymous_Returns401</c> ya fija para el resto del controller.
    /// (2) Se resuelve la clave a partir del miembro de la sesión —nunca de un parámetro—,
    /// así que no hay forma de preguntar por el expediente de otro: no existe entrada donde
    /// escribir un ajeno.
    /// (3) Y aun así se pasa por el guard, que es quien AUDITA. Saltárselo porque "es su
    /// propio expediente" dejaría el único acceso sin rastro, justo el que más lo necesita.</para>
    ///
    /// <para>Un miembro sin expediente recibe 404 y no un cuerpo vacío: es la respuesta
    /// honesta, y no distingue "no tienes" de "no existe" para nadie más, porque solo se
    /// puede preguntar por uno mismo.</para>
    /// </remarks>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }

        // Solo la CLAVE, nunca el expediente: es lo que hace segura esta lectura antes del
        // guard. Sin ella no se puede nombrar el recurso sobre el que se pide permiso.
        var patientKey = await _patients.FindKeyByMemberAsync(memberKey, ct);
        if (patientKey is null)
        {
            return NotFound();
        }

        if (await DenyAsync("patient-record", "read", patientKey, ct) is { } denied)
        {
            return denied;
        }

        var record = await _patients.GetAsync(patientKey.Value, ct);
        return record is null ? NotFound() : Ok(record);
    }

    // ── Pacientes ──────────────────────────────────────────────
    [HttpGet("patients")]
    public async Task<IActionResult> ListPatients([FromQuery] Guid? doctorKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", null, ct) is { } denied) return denied;
        return Ok(await _patients.ListAsync(new PatientQuery(DoctorKey: doctorKey), ct));
    }

    [HttpGet("patients/{patientKey:guid}")]
    public async Task<IActionResult> GetPatient(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct) is { } denied) return denied;
        var patient = await _patients.GetAsync(patientKey, ct);
        return patient is null ? NotFound() : Ok(patient);
    }

    [HttpPost("patients")]
    public async Task<IActionResult> UpsertPatient([FromBody] PatientRecord record, CancellationToken ct)
    {
        if (record is null) return BadRequest();
        if (await DenyAsync("patient-record", "write", record.PatientKey, ct) is { } denied) return denied;

        // Una historia que ya existe se cambia con su candado, y lo que el cuerpo no trae no se borra: con el
        // resumen y el pre-registro dentro (I2 T057), un cliente que manda la historia de antes —sin esos dos—
        // dejaba la versión vigente sin las alergias del paciente.
        if (record.PatientKey != Guid.Empty)
        {
            var cambiada = await _editor.ModificarAsync(
                record.PatientKey,
                actual => record with
                {
                    Resumen = record.Resumen ?? actual.Resumen,
                    PreRegistro = record.PreRegistro ?? actual.PreRegistro,
                },
                ct);
            if (cambiada is not null)
            {
                return Ok(new { patientKey = cambiada.PatientKey });
            }
        }

        var key = await _patients.UpsertAsync(record, ct);
        return Ok(new { patientKey = key });
    }

    /// <summary>
    /// Recepción (o el médico) abre la historia de un miembro por su correo, sólo con lo demográfico (I2 T056). Si ya
    /// tiene una, devuelve esa y no crea otra.
    /// </summary>
    /// <remarks>La guardia va primero: sin el rol no se busca ni el correo.</remarks>
    [HttpPost("patients/abrir")]
    public async Task<IActionResult> AbrirHistoria([FromBody] AperturaDeLaHistoria body, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "open", null, ct, proposito: Propositos.Apertura) is { } denied)
        {
            return denied;
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Correo))
        {
            return BadRequest(new { error = "correo: falta." });
        }
        if (_miembros is null || _gate.CurrentMemberKey is not Guid quienAbre)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var ahora = _reloj.GetUtcNow();
        if (await _miembros.ClaveDelCorreoAsync(body.Correo, ct) is not Guid memberKey)
        {
            return NotFound(new { error = "No hay un miembro con ese correo." });
        }
        var (nueva, error) = HistoriaClinica.Abrir(body, memberKey, quienAbre, ahora.UtcDateTime, Hoy(ahora));
        if (nueva is null)
        {
            return BadRequest(new { error });
        }

        var (patientKey, yaExistia) = await _editor.AbrirAsync(memberKey, nueva, ct);
        return yaExistia
            ? Ok(new { patientKey, yaExistia })
            : StatusCode(StatusCodes.Status201Created, new { patientKey, yaExistia });
    }

    // ── El resumen de salud y el pre-registro (003-salud-cita, I2 T058–T060) ──────────

    /// <summary>
    /// El pre-registro del paciente de la sesión: lo que declara de sí mismo antes de la cita. Entra como DECLARADO; el
    /// médico lo valida (T059).
    /// </summary>
    /// <remarks>
    /// La historia sale de la SESIÓN, como en <see cref="Me"/>: no hay parámetro donde escribir la de otro. Sin
    /// historia abierta, 404 (la abre la recepción del consultorio). La guardia audita con el propósito del check-in.
    /// </remarks>
    [HttpPost("me/check-in")]
    public async Task<IActionResult> CheckIn([FromBody] CheckInDelPaciente body, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }
        if (body is null)
        {
            return BadRequest(new { error = "Falta el pre-registro." });
        }

        var patientKey = await _patients.FindKeyByMemberAsync(memberKey, ct);
        if (patientKey is null)
        {
            return NotFound();
        }

        if (await DenyAsync("patient-record", "declare", patientKey, ct, memberKey, Propositos.CheckIn) is { } denied)
        {
            return denied;
        }

        var ahora = _reloj.GetUtcNow();
        if (HistoriaClinica.ErrorDelCheckIn(body, Hoy(ahora)) is { } error)
        {
            return BadRequest(new { error });
        }

        var historia = await _editor.ModificarAsync(
            patientKey.Value,
            actual => HistoriaClinica.Declarar(actual, body, memberKey, ahora.UtcDateTime),
            ct);
        return historia is null ? NotFound() : Ok(HistoriaClinica.ParaLeer(historia));
    }

    /// <summary>El resumen de salud del paciente de la sesión: lo validado y lo suyo declarado, como pendiente.</summary>
    [HttpGet("me/resumen")]
    public async Task<IActionResult> MiResumen(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }

        var patientKey = await _patients.FindKeyByMemberAsync(memberKey, ct);
        if (patientKey is null)
        {
            return NotFound();
        }

        if (await DenyAsync("patient-record", "read", patientKey, ct, memberKey, Propositos.ResumenPropio) is { } denied)
        {
            return denied;
        }

        var historia = await _patients.GetAsync(patientKey.Value, ct);
        if (historia is null)
        {
            return NotFound();
        }

        var resumen = HistoriaClinica.ParaLeer(historia);
        if (_permisos is not null)
        {
            var permiso = await _permisos.LeerAsync(
                DefaultPhiAccessGuard.SujetoDelPaciente, memberKey.ToString("N"), DefaultPhiAccessGuard.PropositoDeLaHistoria, ct);
            resumen = resumen with
            {
                PermisoDeLaHistoria = permiso.Estado switch
                {
                    EstadoDelPermiso.Vigente => "vigente",
                    EstadoDelPermiso.NoVigente => "falta",
                    _ => "desconocido",
                },
            };
        }
        return Ok(resumen);
    }

    /// <summary>
    /// Recepción (o el médico) encuentra la historia de un miembro por su correo (I2 T063): sólo su clave y si ya hizo el
    /// pre-registro, nada clínico. El correo va en el cuerpo, no en la dirección, para que no quede en ningún registro.
    /// </summary>
    [HttpPost("patients/buscar")]
    public async Task<IActionResult> BuscarHistoria([FromBody] BusquedaDeLaHistoria body, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "open", null, ct, proposito: Propositos.Busqueda) is { } denied)
        {
            return denied;
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Correo))
        {
            return BadRequest(new { error = "correo: falta." });
        }
        if (_miembros is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (await _miembros.ClaveDelCorreoAsync(body.Correo, ct) is not Guid memberKey)
        {
            return NotFound(new { error = "No hay un miembro con ese correo." });
        }
        // El miembro existe y no tiene historia: 200 sin clave, que es «ábrasela», no un 404 que se confunda con un
        // correo que no es de nadie.
        if (await _patients.FindKeyByMemberAsync(memberKey, ct) is not Guid patientKey
            || await _patients.GetAsync(patientKey, ct) is not { IsDeleted: false } historia)
        {
            return Ok(new { patientKey = (Guid?)null, nombre = (string?)null, preRegistrado = false });
        }
        return Ok(new { patientKey = (Guid?)patientKey, nombre = (string?)historia.DisplayName, preRegistrado = historia.PreRegistro is not null });
    }

    /// <summary>El resumen de salud de un paciente, para el médico con su permiso.</summary>
    [HttpGet("patients/{patientKey:guid}/resumen")]
    public async Task<IActionResult> Resumen(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct, proposito: Propositos.Historia) is { } denied)
        {
            return denied;
        }

        var historia = await _patients.GetAsync(patientKey, ct);
        return historia is null || historia.IsDeleted ? NotFound() : Ok(HistoriaClinica.ParaLeer(historia));
    }

    /// <summary>
    /// El médico valida un dato que declaró el paciente (T059). Idempotente: validar uno ya validado contesta lo mismo
    /// y no crea versión.
    /// </summary>
    [HttpPost("patients/{patientKey:guid}/resumen/{datoId}/validar")]
    public async Task<IActionResult> ValidarDato(Guid patientKey, string datoId, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "write", patientKey, ct, proposito: Propositos.Historia) is { } denied)
        {
            return denied;
        }
        if (_gate.CurrentMemberKey is not Guid medico)
        {
            return Unauthorized();
        }

        var resultado = ResultadoDeValidar.NoExiste;
        var historia = await _editor.ModificarAsync(
            patientKey,
            actual =>
            {
                var (cambiada, r) = HistoriaClinica.Validar(actual, datoId, medico, _reloj.GetUtcNow().UtcDateTime);
                resultado = r;
                return cambiada;
            },
            ct);

        return historia is null || resultado == ResultadoDeValidar.NoExiste
            ? NotFound()
            : Ok(HistoriaClinica.ParaLeer(historia));
    }

    // ── La consulta (003-salud-cita, I3: P9 la agenda del día, P10 la llegada, P6 la nota firmada) ──────────

    /// <summary>
    /// La agenda del día de un profesional, desde Booking por el orquestador, con el nombre de cada paciente desde su
    /// historia (P9). Es del personal: la guardia lo decide como agenda, sin el permiso clínico.
    /// </summary>
    [HttpGet("agenda")]
    public async Task<IActionResult> AgendaDelDia([FromQuery] string? profesional, [FromQuery] string? dia, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "read", null, ct, proposito: Propositos.Agenda) is { } denied)
        {
            return denied;
        }
        if (string.IsNullOrWhiteSpace(profesional)
            || !DateOnly.TryParseExact(dia, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var fecha))
        {
            return BadRequest(new { error = "Hacen falta el profesional y el día (aaaa-mm-dd)." });
        }

        var medianoche = fecha.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var desde = new DateTimeOffset(medianoche, _zona.GetUtcOffset(medianoche));
        var citas = _consulta is null ? null : await _consulta.AgendaAsync(profesional.Trim(), desde, desde.AddDays(1), ct);
        if (citas is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var filas = new List<object>(citas.Count);
        foreach (var cita in citas)
        {
            Guid? patientKey = null;
            string? nombre = null;
            if (cita.PacienteKind == DefaultPhiAccessGuard.SujetoDelPaciente
                && Guid.TryParseExact(cita.PacienteId, "N", out var miembro)
                && await _patients.FindKeyByMemberAsync(miembro, ct) is Guid key
                && await _patients.GetAsync(key, ct) is { IsDeleted: false } historia)
            {
                patientKey = key;
                nombre = historia.DisplayName;
            }
            filas.Add(new { cita.ReservaId, cita.Inicio, cita.Fin, cita.Llegada, paciente = new { nombre, patientKey } });
        }
        return Ok(new { citas = filas });
    }

    /// <summary>Marca un paso de la llegada de una cita: llegó, en sala, atendido o no vino (P10).</summary>
    [HttpPost("agenda/{reservaId}/llegada")]
    public async Task<IActionResult> MarcarLlegada(string reservaId, [FromBody] PasoDeLlegada body, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "write", null, ct, proposito: Propositos.Llegada) is { } denied)
        {
            return denied;
        }
        if (body?.Paso is not { } paso || !LlegadaALaConsulta.Pasos.Contains(paso))
        {
            return BadRequest(new { error = "El paso es llegar, pasar, atender o no-vino." });
        }
        if (_consulta is null || _gate.CurrentMemberKey is not Guid quien)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var r = await _consulta.MarcarLlegadaAsync(reservaId, paso, quien, ct);
        return r.Estado switch
        {
            EstadoDeLaConsulta.Hecho => Ok(new { reservaId, llegada = r.Valor }),
            EstadoDeLaConsulta.Rechazado => Conflict(new { error = r.Codigo }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    /// <summary>Las notas de la consulta de un paciente: el médico con el permiso, o el paciente sobre lo suyo (P6).</summary>
    [HttpGet("patients/{patientKey:guid}/notas")]
    public async Task<IActionResult> Notas(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct, proposito: Propositos.Historia) is { } denied)
        {
            return denied;
        }
        if (_notas is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Ok((await _notas.ListAsync(patientKey, ct)).Select(NotaClinica.ParaLeer).ToList());
    }

    /// <summary>
    /// El médico escribe o cambia su nota mientras no la firma (P6). Firmada no se cambia: una corrección es otra nota.
    /// </summary>
    [HttpPost("patients/{patientKey:guid}/notas")]
    public async Task<IActionResult> EscribirNota(Guid patientKey, [FromBody] NotaEscrita body, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "write", patientKey, ct, proposito: Propositos.Historia) is { } denied)
        {
            return denied;
        }
        if (body is null)
        {
            return BadRequest(new { error = "Falta la nota." });
        }
        if (NotaClinica.Error(body) is { } error)
        {
            return BadRequest(new { error });
        }
        if (_notas is null || _gate.CurrentMemberKey is not Guid medico)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (await _patients.GetAsync(patientKey, ct) is not { IsDeleted: false })
        {
            return NotFound();
        }

        var actual = body.Id is Guid id ? await _notas.GetAsync(id, ct) : null;
        if (body.Id is not null && (actual is null || actual.PatientKey != patientKey))
        {
            return NotFound();
        }
        if (actual is not null && actual.AutorKey != medico)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var nueva = NotaClinica.Escribir(actual, body, patientKey, medico, _reloj.GetUtcNow().UtcDateTime);
        if (nueva is null)
        {
            return Conflict(new { error = "La nota está firmada: una corrección es otra nota." });
        }
        await _notas.GuardarAsync(nueva, ct);
        return Ok(NotaClinica.ParaLeer(nueva));
    }

    /// <summary>
    /// El autor firma su nota: su huella, sellada con la llave del consultorio en <c>Api.Signing</c> (P6, Q14). Firmar dos
    /// veces da la misma firma. Sin la llave del consultorio, el no de la capacidad y la nota sigue en borrador.
    /// </summary>
    [HttpPost("patients/{patientKey:guid}/notas/{notaId:guid}/firmar")]
    public async Task<IActionResult> FirmarNota(Guid patientKey, Guid notaId, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "write", patientKey, ct, proposito: Propositos.Firma) is { } denied)
        {
            return denied;
        }
        if (_notas is null || _consulta is null || _gate.CurrentMemberKey is not Guid medico)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var nota = await _notas.GetAsync(notaId, ct);
        if (nota is null || nota.PatientKey != patientKey)
        {
            return NotFound();
        }
        if (nota.Firma is not null)
        {
            return Ok(NotaClinica.ParaLeer(nota));
        }
        if (nota.AutorKey != medico)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var huella = NotaClinica.Huella(nota);
        var sello = await _consulta.SellarAsync(_llaveDeFirma, huella, ct);
        if (sello.Estado == EstadoDeLaConsulta.Rechazado)
        {
            return Conflict(new { error = sello.Codigo });
        }
        if (sello.Estado != EstadoDeLaConsulta.Hecho || sello.Valor is null || sello.Extra is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var firmada = nota with
        {
            Firma = new FirmaDeLaNota(huella, sello.Valor, sello.Extra, _llaveDeFirma, medico, _reloj.GetUtcNow().UtcDateTime),
        };
        await _notas.GuardarAsync(firmada, ct);
        return Ok(NotaClinica.ParaLeer(firmada));
    }

    /// <summary>
    /// Si la firma de una nota sigue valiendo: la huella de lo que hay hoy en el almacén, contra el sello (P6). Una nota
    /// cambiada después de firmar no cuadra.
    /// </summary>
    [HttpGet("patients/{patientKey:guid}/notas/{notaId:guid}/comprobar")]
    public async Task<IActionResult> ComprobarNota(Guid patientKey, Guid notaId, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct, proposito: Propositos.Historia) is { } denied)
        {
            return denied;
        }
        if (_notas is null || _consulta is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var nota = await _notas.GetAsync(notaId, ct);
        if (nota is null || nota.PatientKey != patientKey)
        {
            return NotFound();
        }
        if (nota.Firma is not { } firma)
        {
            return Ok(new { firmada = false, valida = false });
        }

        var huella = NotaClinica.Huella(nota);
        if (!string.Equals(huella, firma.Huella, StringComparison.Ordinal))
        {
            return Ok(new { firmada = true, valida = false });
        }
        var r = await _consulta.ComprobarSelloAsync(firma.Llave, huella, firma.Sello, ct);
        return r.Estado switch
        {
            EstadoDeLaConsulta.Hecho => Ok(new { firmada = true, valida = true, llaveId = r.Valor }),
            EstadoDeLaConsulta.Rechazado => Ok(new { firmada = true, valida = false }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable),
        };
    }

    // ── Órdenes y resultados (003-salud-cita, I4: P11 la orden, P12 el resultado) ─────────────────────────────

    /// <summary>Las órdenes de un paciente, con el último resultado de cada examen: el médico con el permiso los ve todos.</summary>
    [HttpGet("patients/{patientKey:guid}/ordenes")]
    public async Task<IActionResult> Ordenes(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct, proposito: Propositos.Ordenes) is { } denied)
        {
            return denied;
        }
        if (_ordenes is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (await _patients.GetAsync(patientKey, ct) is not { IsDeleted: false } historia)
        {
            return NotFound();
        }

        // El paciente que pide lo suyo por esta ruta ve lo mismo que por «me»: sólo lo liberado.
        var esElPaciente = _gate.CurrentMemberKey is Guid yo && historia.MemberKey == yo;
        var resultados = await _ordenes.ListResultadosAsync(patientKey, ct);
        return Ok((await _ordenes.ListOrdenesAsync(patientKey, ct))
            .Select(o => OrdenesClinicas.ParaLeer(o, resultados, verSinLiberar: !esElPaciente)).ToList());
    }

    /// <summary>Las órdenes del paciente de la sesión: sus resultados, sólo los que el médico ya liberó.</summary>
    [HttpGet("me/ordenes")]
    public async Task<IActionResult> MisOrdenes(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }
        var patientKey = await _patients.FindKeyByMemberAsync(memberKey, ct);
        if (patientKey is null)
        {
            return NotFound();
        }
        if (await DenyAsync("patient-record", "read", patientKey, ct, memberKey, Propositos.Ordenes) is { } denied)
        {
            return denied;
        }
        if (_ordenes is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var resultados = await _ordenes.ListResultadosAsync(patientKey.Value, ct);
        return Ok((await _ordenes.ListOrdenesAsync(patientKey.Value, ct))
            .Select(o => OrdenesClinicas.ParaLeer(o, resultados, verSinLiberar: false)).ToList());
    }

    /// <summary>El médico con el permiso ordena exámenes de laboratorio, imágenes o una remisión (P11).</summary>
    [HttpPost("patients/{patientKey:guid}/ordenes")]
    public async Task<IActionResult> Ordenar(Guid patientKey, [FromBody] OrdenEscrita body, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "write", patientKey, ct, proposito: Propositos.Ordenes) is { } denied)
        {
            return denied;
        }
        if (body is null)
        {
            return BadRequest(new { error = "Falta la orden." });
        }
        if (OrdenesClinicas.ErrorDeLaOrden(body) is { } error)
        {
            return BadRequest(new { error });
        }
        if (_ordenes is null || _gate.CurrentMemberKey is not Guid medico)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (await _patients.GetAsync(patientKey, ct) is not { IsDeleted: false })
        {
            return NotFound();
        }

        var orden = OrdenesClinicas.Ordenar(body, patientKey, medico, _reloj.GetUtcNow().UtcDateTime);
        await _ordenes.GuardarOrdenAsync(orden, ct);
        return Ok(OrdenesClinicas.ParaLeer(orden, [], verSinLiberar: true));
    }

    /// <summary>
    /// El consultorio carga el resultado de un examen de la orden, con el PDF del informe si lo hay (P12). Queda sin
    /// liberar: el paciente no lo ve hasta que el médico lo revise.
    /// </summary>
    [HttpPost("patients/{patientKey:guid}/ordenes/{ordenId:guid}/resultados")]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> CargarResultado(Guid patientKey, Guid ordenId, [FromBody] ResultadoCargado body, CancellationToken ct)
    {
        if (await DenyAsync("lab-result", "load", patientKey, ct, proposito: Propositos.Resultado) is { } denied)
        {
            return denied;
        }
        if (_ordenes is null || _archivos is null || _gate.CurrentMemberKey is not Guid quien)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (await _ordenes.GetOrdenAsync(ordenId, ct) is not { } orden || orden.PatientKey != patientKey)
        {
            return NotFound();
        }
        if (body is null)
        {
            return BadRequest(new { error = "Falta el resultado." });
        }
        if (OrdenesClinicas.ErrorDelResultado(orden, body) is { } error)
        {
            return BadRequest(new { error });
        }

        string? archivoId = null;
        string? archivoNombre = null;
        if (body.Informe is { } informe && OrdenesClinicas.BytesDe(informe) is { } bytes)
        {
            archivoNombre = string.IsNullOrWhiteSpace(informe.Nombre) ? "informe.pdf" : Path.GetFileName(informe.Nombre.Trim());
            archivoId = await _archivos.SaveAsync(AmbitoDeLosInformes, bytes, "application/pdf", archivoNombre, ct);
        }

        var resultado = new ResultadoDeExamen(
            Guid.NewGuid(), orden.Id, patientKey, body.ExamenId!, body.Valor!.Trim(), Limpio(body.Unidad), Limpio(body.Rango),
            body.Marca!, Limpio(body.Nota), archivoId, archivoNombre, quien, _reloj.GetUtcNow().UtcDateTime);
        await _ordenes.GuardarResultadoAsync(resultado, ct);
        return Ok(OrdenesClinicas.ParaLeer(orden, await _ordenes.ListResultadosAsync(patientKey, ct), verSinLiberar: true));
    }

    /// <summary>
    /// El médico revisa el resultado y lo libera al paciente (P12). Liberar dos veces da lo mismo; con todos los exámenes
    /// liberados, la orden queda cumplida.
    /// </summary>
    [HttpPost("patients/{patientKey:guid}/resultados/{resultadoId:guid}/liberar")]
    public async Task<IActionResult> LiberarResultado(Guid patientKey, Guid resultadoId, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "write", patientKey, ct, proposito: Propositos.Resultado) is { } denied)
        {
            return denied;
        }
        if (_ordenes is null || _gate.CurrentMemberKey is not Guid medico)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (await _ordenes.GetResultadoAsync(resultadoId, ct) is not { } resultado || resultado.PatientKey != patientKey
            || await _ordenes.GetOrdenAsync(resultado.OrdenId, ct) is not { } orden)
        {
            return NotFound();
        }

        if (!resultado.Liberado)
        {
            await _ordenes.GuardarResultadoAsync(
                resultado with { LiberadoPor = medico, LiberadoUtc = _reloj.GetUtcNow().UtcDateTime }, ct);
        }
        var resultados = await _ordenes.ListResultadosAsync(patientKey, ct);
        var conEstado = OrdenesClinicas.ConSuEstado(orden, resultados);
        if (!ReferenceEquals(conEstado, orden))
        {
            await _ordenes.GuardarOrdenAsync(conEstado, ct);
        }
        return Ok(OrdenesClinicas.ParaLeer(conEstado, resultados, verSinLiberar: true));
    }

    /// <summary>El PDF del informe, para el médico con el permiso (P12).</summary>
    [HttpGet("patients/{patientKey:guid}/resultados/{resultadoId:guid}/informe")]
    public async Task<IActionResult> InformeDelResultado(Guid patientKey, Guid resultadoId, CancellationToken ct)
    {
        if (await DenyAsync("patient-record", "read", patientKey, ct, proposito: Propositos.Resultado) is { } denied)
        {
            return denied;
        }
        var esElPaciente = _gate.CurrentMemberKey is Guid yo
            && await _patients.GetAsync(patientKey, ct) is { } historia && historia.MemberKey == yo;
        return await InformeAsync(patientKey, resultadoId, soloLiberado: esElPaciente, ct);
    }

    /// <summary>El PDF del informe del paciente de la sesión: sólo de un resultado liberado.</summary>
    [HttpGet("me/resultados/{resultadoId:guid}/informe")]
    public async Task<IActionResult> MiInforme(Guid resultadoId, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }
        if (await _patients.FindKeyByMemberAsync(memberKey, ct) is not Guid patientKey)
        {
            return NotFound();
        }
        if (await DenyAsync("patient-record", "read", patientKey, ct, memberKey, Propositos.Resultado) is { } denied)
        {
            return denied;
        }
        return await InformeAsync(patientKey, resultadoId, soloLiberado: true, ct);
    }

    private async Task<IActionResult> InformeAsync(Guid patientKey, Guid resultadoId, bool soloLiberado, CancellationToken ct)
    {
        if (_ordenes is null || _archivos is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        // Uno sin liberar no existe para el paciente: el mismo 404 que uno que no es suyo.
        if (await _ordenes.GetResultadoAsync(resultadoId, ct) is not { ArchivoId: { } archivoId } resultado
            || resultado.PatientKey != patientKey || (soloLiberado && !resultado.Liberado))
        {
            return NotFound();
        }
        var archivo = await _archivos.ReadAsync(AmbitoDeLosInformes, archivoId, ct);
        return archivo is null ? NotFound() : File(archivo.Content, archivo.ContentType, archivo.FileName);
    }

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>«Hoy» en el sitio: con el de UTC, desde las siete de la noche en Bogotá una vacuna de hoy sería futura.</summary>
    private DateOnly Hoy(DateTimeOffset ahora) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, _zona).DateTime);

    // ── Citas ──────────────────────────────────────────────────
    [HttpGet("appointments")]
    public async Task<IActionResult> ListAppointments([FromQuery] Guid? doctorKey, [FromQuery] Guid? patientKey, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _appointments.ListAsync(new AppointmentQuery(DoctorKey: doctorKey, PatientKey: patientKey), ct));
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> BookAppointment([FromBody] AppointmentRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();
        if (await DenyAsync("appointment", "write", request.PatientKey, ct) is { } denied) return denied;
        var result = await _appointments.BookAsync(request, ct);
        return result.Booked ? Ok(result.Slot) : Conflict(new { reason = result.ConflictReason });
    }

    [HttpPost("appointments/{appointmentId:guid}/cancel")]
    public async Task<IActionResult> CancelAppointment(Guid appointmentId, [FromQuery] string? reason, CancellationToken ct)
    {
        if (await DenyAsync("appointment", "write", null, ct) is { } denied) return denied;
        return await _appointments.CancelAsync(appointmentId, reason ?? string.Empty, ct) ? Ok() : NotFound();
    }

    // ── Recetas ────────────────────────────────────────────────
    [HttpGet("patients/{patientKey:guid}/prescriptions")]
    public async Task<IActionResult> ListPrescriptions(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("prescription", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _prescriptions.ListForPatientAsync(patientKey, ct));
    }

    [HttpPost("prescriptions")]
    public async Task<IActionResult> IssuePrescription([FromBody] IssuePrescriptionRequest request, CancellationToken ct)
    {
        if (request is null) return BadRequest();
        if (await DenyAsync("prescription", "write", request.PatientKey, ct) is { } denied) return denied;
        return Ok(await _prescriptions.IssueAsync(request, ct));
    }

    // ── Consentimiento ─────────────────────────────────────────
    [HttpGet("patients/{patientKey:guid}/consent")]
    public async Task<IActionResult> ConsentHistory(Guid patientKey, CancellationToken ct)
    {
        if (await DenyAsync("consent", "read", patientKey, ct) is { } denied) return denied;
        return Ok(await _consent.GetConsentHistoryAsync(patientKey, ct));
    }

    [HttpPost("consent")]
    public async Task<IActionResult> GrantConsent([FromBody] ConsentGrant grant, CancellationToken ct)
    {
        if (grant is null) return BadRequest();
        if (await DenyAsync("consent", "write", grant.PatientKey, ct) is { } denied) return denied;
        var consentId = await _consent.GrantAsync(grant, ct);
        return Ok(new { consentId });
    }

    [HttpPost("consent/{consentId:guid}/revoke")]
    public async Task<IActionResult> RevokeConsent(Guid consentId, CancellationToken ct)
    {
        if (await DenyAsync("consent", "write", null, ct) is { } denied) return denied;
        await _consent.RevokeAsync(consentId, ct);
        return Ok();
    }

    // Guard-first: audita + decide. Devuelve el IActionResult de rechazo (401
    // si no autenticado, 403 si denegado) o null si pasa.
    //
    // `owner` y `proposito` son de I2 (T058 y T064): el dueño sólo lo pasa quien lo sacó de la SESIÓN (el portal
    // del paciente), nunca de un parámetro; el propósito va a la auditoría como el «por qué».
    private async Task<IActionResult?> DenyAsync(
        string resourceType,
        string action,
        Guid? targetPatientKey,
        CancellationToken ct,
        Guid? owner = null,
        string? proposito = null)
    {
        // El dueño NO se resuelve acá. Este controller mantiene la propiedad
        // "guard primero, sin tocar PHI antes de autorizar" — que el test
        // GetPatient_Anonymous_Returns401 verifica exigiendo que el repositorio
        // no reciba ni una llamada cuando el llamante es anónimo.
        //
        // Quien decide es el guard, así que es el guard el que resuelve el
        // Member dueño del paciente, y sólo DESPUÉS de comprobar que hay sesión
        // (DefaultPhiAccessGuard.EvaluateAsync). Un anónimo sigue sin provocar
        // una sola lectura.
        var decision = await _guard.CheckAccessAsync(
            new AccessCheckRequest(resourceType, action, targetPatientKey, owner, proposito), ct);
        if (decision.IsAllowed) return null;
        return string.Equals(decision.DenyReason, "not-authenticated", StringComparison.Ordinal)
            ? Unauthorized()
            : StatusCode(StatusCodes.Status403Forbidden);
    }
}
