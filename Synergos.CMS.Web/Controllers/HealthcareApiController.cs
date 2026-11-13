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
    }

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
        IConsentReader? permisos = null)
    {
        _miembros = miembros;
        _permisos = permisos;
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
        if (await _patients.FindKeyByMemberAsync(memberKey, ct) is not Guid patientKey
            || await _patients.GetAsync(patientKey, ct) is not { IsDeleted: false } historia)
        {
            return NotFound(new { error = "Ese miembro todavía no tiene historia." });
        }
        return Ok(new { patientKey, nombre = historia.DisplayName, preRegistrado = historia.PreRegistro is not null });
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
