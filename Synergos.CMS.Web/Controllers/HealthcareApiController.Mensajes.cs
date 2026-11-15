using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Salud;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Mensajes, bandeja y estado de cuenta (003-salud-cita, I7 P15–P17), con la misma guardia que el resto de la historia.
/// </summary>
/// <remarks>
/// <para><b>Los mensajes</b> van por la mensajería común, cifrada (Q12): el paciente escribe sobre su historia como lo que
/// declara (como el pre-registro), y el médico lee y contesta con el permiso del paciente para el consultorio. Un hilo de
/// otro paciente no existe para quien lo pide.</para>
///
/// <para><b>La bandeja</b> se arma con lo que espera al médico en la historia cifrada —un resultado sin liberar, una
/// renovación pedida— y con los mensajes sin responder. La guardia decide paciente por paciente, y lo que no deja ver no
/// sale: sólo se cuenta.</para>
///
/// <para><b>La cuenta</b> la da el orquestador con las cifras de Payments: el paciente es el sujeto con que agendó.</para>
/// </remarks>
public sealed partial class HealthcareApiController
{
    private const string RolDelMedico = "doctor";
    private const int PacientesDeLaBandeja = 500;

    /// <summary>La mensajería común, que guarda cifrado (I7 P15).</summary>
    private readonly IMessagingService? _mensajeria;

    /// <summary>El directorio de profesionales: a quién se le escribe y cómo se llama.</summary>
    private readonly IDoctorDirectory? _directorio;

    /// <summary>Los avisos al paciente (I8 P18): un resultado liberado, una renovación respondida, una respuesta nueva.</summary>
    private readonly ITransactionalNotifier? _avisos;

    private readonly ILogger<HealthcareApiController>? _log;

    /// <summary>A dónde lleva el enlace de un aviso del consultorio: el portal, donde el paciente entra con su sesión.</summary>
    private const string RutaDelPortal = "/healthcare/";

    // ── Mensajes (P15) ─────────────────────────────────────────

    /// <summary>Los hilos del paciente de la sesión con los profesionales del consultorio, del más reciente al más antiguo.</summary>
    [HttpGet("me/mensajes")]
    public async Task<IActionResult> MisMensajes(CancellationToken ct)
    {
        var (miPaciente, negada) = await MiHistoriaAsync("patient-record", "read", Propositos.Mensajes, ct);
        if (negada is not null || miPaciente is not Guid patientKey)
        {
            return negada ?? NotFound();
        }
        if (_mensajeria is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var hilos = new List<HiloDelPaciente>();
        foreach (var hilo in await HilosDeAsync(MensajesClinicos.BuzonDelPaciente(patientKey), ct))
        {
            hilos.Add(MensajesClinicos.DelPaciente(hilo, await NombreDelProfesionalAsync(MensajesClinicos.ProfesionalDe(hilo.Participants), ct)));
        }
        return Ok(hilos);
    }

    /// <summary>
    /// El paciente le escribe a un profesional del directorio: el hilo con él, o uno nuevo. Escribir dos veces son dos
    /// mensajes en el MISMO hilo.
    /// </summary>
    [HttpPost("me/mensajes")]
    public async Task<IActionResult> EscribirMensaje([FromBody] MensajeEscrito? body, CancellationToken ct)
    {
        var (miPaciente, negada) = await MiHistoriaAsync("patient-record", "declare", Propositos.Mensajes, ct);
        if (negada is not null || miPaciente is not Guid patientKey)
        {
            return negada ?? NotFound();
        }
        if (_mensajeria is null || _directorio is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (MensajesClinicos.Error(body?.Texto) is { } error)
        {
            return BadRequest(new { error });
        }
        var profesionalId = Limpio(body?.Profesional);
        if (profesionalId is null || await _directorio.GetAsync(profesionalId, ct) is not { } profesional)
        {
            return BadRequest(new { error = "Ese profesional no está en el directorio." });
        }

        var hilo = await _mensajeria.StartThreadAsync(
            MensajesClinicos.Contexto(profesionalId), MensajesClinicos.BuzonDelPaciente(patientKey),
            MensajesClinicos.BuzonDelProfesional(profesionalId), body!.Texto!.Trim(), ct);
        return Ok(MensajesClinicos.DelPaciente(hilo, profesional.FullName));
    }

    /// <summary>Un hilo clínico para el consultorio, con el permiso de su paciente.</summary>
    [HttpGet("mensajes/{hiloId}")]
    public async Task<IActionResult> HiloDelConsultorio(string hiloId, CancellationToken ct)
    {
        var (hilo, patientKey, negada) = await HiloConPermisoAsync(hiloId, "read", ct);
        if (negada is not null)
        {
            return negada;
        }
        return Ok(await DelConsultorioAsync(hilo!, patientKey, ct));
    }

    /// <summary>El consultorio contesta en el hilo, como el profesional del hilo, con el permiso del paciente.</summary>
    [HttpPost("mensajes/{hiloId}")]
    public async Task<IActionResult> ResponderMensaje(string hiloId, [FromBody] MensajeEscrito? body, CancellationToken ct)
    {
        var (hilo, patientKey, negada) = await HiloConPermisoAsync(hiloId, "write", ct);
        if (negada is not null)
        {
            return negada;
        }
        if (MensajesClinicos.Error(body?.Texto) is { } error)
        {
            return BadRequest(new { error });
        }
        var profesional = MensajesClinicos.ProfesionalDe(hilo!.Participants)!;
        var respondido = await _mensajeria!.ReplyAsync(hilo.ThreadId, MensajesClinicos.BuzonDelProfesional(profesional), body!.Texto!.Trim(), ct);
        await AvisarAlPacienteAsync(patientKey, NotificationTypes.SaludMensajeRespondido, respondido.Messages[^1].MessageId, RutaDelPortal, ct);
        return Ok(await DelConsultorioAsync(respondido, patientKey, ct));
    }

    // ── La bandeja del consultorio (P16) ───────────────────────

    /// <summary>
    /// Lo que espera al médico, de lo más reciente a lo más antiguo: los resultados cargados sin liberar, las renovaciones
    /// pedidas y los mensajes sin responder. Cada paciente pasa por la guardia; el que no da su permiso, sólo se cuenta.
    /// </summary>
    [HttpGet("bandeja")]
    public async Task<IActionResult> Bandeja(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid)
        {
            return Unauthorized();
        }
        if (!_gate.HasAnyRole(RolDelMedico))
        {
            // La guardia lo niega y lo deja auditado: la bandeja es del médico.
            return await DenyAsync("patient-record", "read", null, ct, proposito: Propositos.Bandeja) ?? StatusCode(StatusCodes.Status403Forbidden);
        }

        var hilosPorPaciente = await HilosEsperandoPorPacienteAsync(ct);
        var items = new List<ItemDeLaBandeja>();
        var sinPermiso = 0;
        foreach (var resumen in await _patients.ListAsync(new PatientQuery(Limit: PacientesDeLaBandeja), ct))
        {
            var key = resumen.PatientKey;
            if (await DenyAsync("patient-record", "read", key, ct, proposito: Propositos.Bandeja) is not null)
            {
                sinPermiso++;
                continue;
            }
            items.AddRange(await PendientesDeAsync(key, resumen.DisplayName, hilosPorPaciente.GetValueOrDefault(key), ct));
        }
        return Ok(new BandejaDelConsultorio(items.OrderByDescending(i => i.Fecha).ToList(), sinPermiso));
    }

    // ── El estado de cuenta (P17) ──────────────────────────────

    /// <summary>Lo cobrado y lo devuelto de cada cita del paciente de la sesión, con las cifras de Payments.</summary>
    [HttpGet("me/estado-de-cuenta")]
    public async Task<IActionResult> MiEstadoDeCuenta(CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid memberKey)
        {
            return Unauthorized();
        }
        if (await DenyAsync("billing", "read", null, ct, memberKey, Propositos.Cuenta) is { } denied)
        {
            return denied;
        }
        if (_consulta is null
            || await _consulta.EstadoDeCuentaAsync(DefaultPhiAccessGuard.SujetoDelPaciente, memberKey.ToString("N"), ct) is not { } cuenta)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return Ok(CuentaParaLeer.De(cuenta));
    }

    // ── Ayudantes ──────────────────────────────────────────────

    /// <summary>Los hilos clínicos de un buzón, completos, del más reciente al más antiguo.</summary>
    private async Task<List<MessageThread>> HilosDeAsync(string buzon, CancellationToken ct)
    {
        var hilos = new List<MessageThread>();
        foreach (var resumen in await _mensajeria!.GetInboxAsync(buzon, ct))
        {
            if (MensajesClinicos.EsClinico(resumen.ContextRef) && await _mensajeria.GetThreadAsync(resumen.ThreadId, ct) is { } hilo)
            {
                hilos.Add(hilo);
            }
        }
        return hilos;
    }

    /// <summary>
    /// El hilo y su paciente, si quien pide puede <paramref name="accion"/> sobre la historia de ese paciente. Un hilo que no
    /// es clínico, o sin paciente, no existe.
    /// </summary>
    private async Task<(MessageThread? Hilo, Guid PatientKey, IActionResult? Negada)> HiloConPermisoAsync(
        string hiloId, string accion, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid)
        {
            return (null, Guid.Empty, Unauthorized());
        }
        if (_mensajeria is null)
        {
            return (null, Guid.Empty, StatusCode(StatusCodes.Status503ServiceUnavailable));
        }
        if (await _mensajeria.GetThreadAsync(hiloId, ct) is not { } hilo || !MensajesClinicos.EsClinico(hilo.ContextRef)
            || MensajesClinicos.PacienteDe(hilo.Participants) is not Guid patientKey || MensajesClinicos.ProfesionalDe(hilo.Participants) is null)
        {
            return (null, Guid.Empty, NotFound());
        }
        return await DenyAsync("patient-record", accion, patientKey, ct, proposito: Propositos.Mensajes) is { } denied
            ? (null, Guid.Empty, denied)
            : (hilo, patientKey, null);
    }

    private async Task<HiloDelConsultorio> DelConsultorioAsync(MessageThread hilo, Guid patientKey, CancellationToken ct)
        => MensajesClinicos.DelConsultorio(hilo, patientKey, (await _patients.GetAsync(patientKey, ct))?.DisplayName ?? string.Empty,
            await NombreDelProfesionalAsync(MensajesClinicos.ProfesionalDe(hilo.Participants), ct));

    private async Task<string> NombreDelProfesionalAsync(string? profesionalId, CancellationToken ct)
        => profesionalId is null || _directorio is null ? string.Empty
            : (await _directorio.GetAsync(profesionalId, ct))?.FullName ?? string.Empty;

    /// <summary>Los hilos que esperan respuesta del consultorio, por paciente, de todos los profesionales del directorio.</summary>
    private async Task<Dictionary<Guid, List<MessageThread>>> HilosEsperandoPorPacienteAsync(CancellationToken ct)
    {
        var porPaciente = new Dictionary<Guid, List<MessageThread>>();
        if (_mensajeria is null || _directorio is null)
        {
            return porPaciente;
        }
        foreach (var profesional in await _directorio.ListAsync(cancellationToken: ct))
        {
            foreach (var hilo in await HilosDeAsync(MensajesClinicos.BuzonDelProfesional(profesional.Id), ct))
            {
                if (MensajesClinicos.Esperando(hilo) && MensajesClinicos.PacienteDe(hilo.Participants) is Guid key)
                {
                    (porPaciente.TryGetValue(key, out var lista) ? lista : porPaciente[key] = []).Add(hilo);
                }
            }
        }
        return porPaciente;
    }

    /// <summary>Lo que espera al médico de un paciente que ya pasó la guardia.</summary>
    private async Task<List<ItemDeLaBandeja>> PendientesDeAsync(
        Guid key, string paciente, List<MessageThread>? hilos, CancellationToken ct)
    {
        var items = new List<ItemDeLaBandeja>();
        if (_ordenes is not null)
        {
            var ordenes = (await _ordenes.ListOrdenesAsync(key, ct)).ToDictionary(o => o.Id);
            foreach (var r in (await _ordenes.ListResultadosAsync(key, ct)).Where(r => !r.Liberado))
            {
                var examen = ordenes.GetValueOrDefault(r.OrdenId)?.Examenes.FirstOrDefault(e => e.Id == r.ExamenId)?.Nombre ?? r.ExamenId;
                items.Add(new ItemDeLaBandeja("resultado", r.Id.ToString(), key, paciente, examen, Utc(r.CargadoUtc)));
            }
        }
        if (_renovaciones is not null)
        {
            foreach (var s in (await _renovaciones.ListAsync(key, ct)).Where(s => s.Estado == RecetasClinicas.Pedida))
            {
                var medicamento = (await _prescriptions.GetAsync(s.RecetaId, ct))?.MedicationName ?? string.Empty;
                items.Add(new ItemDeLaBandeja("renovacion", s.Id.ToString(), key, paciente, medicamento, Utc(s.PedidaUtc)));
            }
        }
        foreach (var hilo in hilos ?? [])
        {
            items.Add(new ItemDeLaBandeja("mensaje", hilo.ThreadId, key, paciente, StubMessagingServicePreview(hilo.Messages[^1].Body),
                hilo.LastMessageAt, MensajesClinicos.ProfesionalDe(hilo.Participants)));
        }
        return items;
    }

    /// <summary>
    /// Le avisa al paciente de la historia que hay algo nuevo (I8 P18), por el notificador transaccional, que decide si los
    /// avisos están encendidos y no manda dos veces el mismo hecho. Sin el dato clínico: el correo dice qué pasó y el enlace
    /// lleva al portal. Lo que ya se hizo no depende del correo: si no se puede avisar, se registra y sigue.
    /// </summary>
    private async Task AvisarAlPacienteAsync(Guid patientKey, string tipo, string hecho, string ruta, CancellationToken ct)
    {
        if (_avisos is null || _miembros is null)
        {
            return;
        }
        try
        {
            if (await _patients.GetAsync(patientKey, ct) is not { MemberKey: var miembro } || miembro == Guid.Empty
                || await _miembros.ContactoAsync(miembro, ct) is not { } contacto)
            {
                return;
            }
            var ahora = _reloj.GetUtcNow();
            await _avisos.DispatchAsync(new NotificationEvent(
                Type: tipo, SubjectId: hecho, ToEmail: contacto.Correo, ToName: contacto.Nombre,
                Code: TimeZoneInfo.ConvertTime(ahora, _zona).ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
                OccurredAt: ahora, ActionPath: ruta, DedupeKey: $"{tipo}:{hecho}"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log?.LogWarning(ex, "No se pudo avisar al paciente ({Tipo}); lo hecho queda hecho.", tipo);
        }
    }

    private static string StubMessagingServicePreview(string texto)
        => Synergos.CMS.Application.Services.Impl.StubMessagingService.Preview(texto);

    private static DateTimeOffset Utc(DateTime fecha) => new(DateTime.SpecifyKind(fecha, DateTimeKind.Utc));
}
