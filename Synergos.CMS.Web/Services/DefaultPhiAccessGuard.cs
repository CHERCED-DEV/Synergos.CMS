using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPhiAccessGuard"/> (ADR 0098). Decide acceso a PHI por
/// rol + pertenencia + consentimiento, y AUDITA cada intento. Fail-closed: si
/// la auditoría no se puede escribir, deniega aunque la política permitiera.
/// </summary>
/// <remarks>
/// Política (estricta por defecto): el paciente accede a lo suyo
/// (TargetOwnerMemberKey == actor); el staff (doctor/nurse/reception) agenda
/// citas; los datos clínicos (historia/recetas/consentimiento) requieren rol
/// doctor CON consentimiento vigente del paciente. Cualquier otro caso: denegado.
/// </remarks>
public sealed class DefaultPhiAccessGuard : IPhiAccessGuard
{
    private const string DoctorRole = "doctor";
    private const string SchedulingRolesCsv = "doctor,nurse,reception";
    private const string AperturaRolesCsv = "doctor,reception";
    private const string CargaDeResultadosRolesCsv = "doctor,nurse";

    private readonly IMemberAccessGate _gate;
    private readonly IConsentLedger _consent;
    private readonly IAuditTrailWriter _audit;
    private readonly ILogger<DefaultPhiAccessGuard> _logger;
    private readonly IPatientRepository? _patients;
    private readonly IConsentReader? _permisos;

    /// <summary>
    /// El propósito del permiso con que el consultorio entra a la historia (Q7 de 003): lo da el paciente con el mismo
    /// «Autorizo» de la cita (D-003-I2-2), y se guarda en <c>Api.Consent</c> como propósito propio.
    /// </summary>
    public const string PropositoDeLaHistoria = "salud.historia";

    /// <summary>
    /// La clase del sujeto con que la puerta escribe ese permiso: el <c>SujetoKind</c> de los flujos de Salud
    /// (<c>Synergos:Puerta:Flujos</c>), con el <c>MemberKey</c> del paciente en formato <c>n</c>.
    /// </summary>
    public const string SujetoDelPaciente = "salud.paciente";

    /// <param name="patients">Opcional. Resuelve el Member DUEÑO de un paciente
    /// para la rama de auto-acceso. Nullable a propósito: sin él el guard sigue
    /// funcionando exactamente como antes —decidiendo sólo por rol y
    /// consentimiento— y los tests que lo construyen a mano no cambian.</param>
    /// <param name="permisos">Opcional (003-salud-cita, I2 T054). Con él, el médico entra con el permiso del paciente
    /// para el consultorio, leído en <c>Api.Consent</c> (Q4 y Q7), y el libro local deja de decidir: se queda, sin
    /// borrarse. Sin él, el libro local de antes (<c>Synergos:Consent:Mode = Local</c>).</param>
    public DefaultPhiAccessGuard(
        IMemberAccessGate gate,
        IConsentLedger consent,
        IAuditTrailWriter audit,
        ILogger<DefaultPhiAccessGuard> logger,
        IPatientRepository? patients = null,
        IConsentReader? permisos = null)
    {
        _gate = gate;
        _consent = consent;
        _audit = audit;
        _logger = logger;
        _patients = patients;
        _permisos = permisos;
    }

    /// <summary>Con qué base se dejó pasar: lo que la auditoría dice como el «por qué» (I2 T064).</summary>
    internal static class Bases
    {
        /// <summary>El paciente sobre lo suyo, sólo para leer.</summary>
        public const string Propio = "propio";

        /// <summary>El personal sobre la agenda, que no pide el permiso clínico.</summary>
        public const string AgendaDelPersonal = "agenda-del-personal";

        /// <summary>El médico con el consentimiento del libro local, médico por médico.</summary>
        public const string PermisoLocal = "permiso-local";

        /// <summary>El médico con el permiso del paciente para el consultorio, leído en <c>Api.Consent</c> (T054).</summary>
        public const string PermisoDelConsultorio = "permiso:" + PropositoDeLaHistoria;

        /// <summary>Recepción o el médico abren la historia de un miembro, sólo con lo demográfico (T056).</summary>
        public const string AperturaDelPersonal = "apertura-del-personal";

        /// <summary>No se dejó pasar.</summary>
        public const string Ninguna = "ninguna";
    }

    public async Task<AccessDecision> CheckAccessAsync(AccessCheckRequest request, CancellationToken cancellationToken)
    {
        bool allowed;
        string? reason;
        string basis;
        try
        {
            (allowed, reason, basis) = await EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Un fallo al evaluar (p.ej. IO del consent ledger) NO debe saltarse
            // la auditoría: se deniega y se audita igual.
            _logger.LogError(ex,
                "DefaultPhiAccessGuard: error evaluando política — acceso DENEGADO a {Resource}",
                request.ResourceType);
            allowed = false;
            reason = "evaluation-error";
            basis = Bases.Ninguna;
        }

        // Auditar SIEMPRE (permiso o negación). Fail-closed: si el audit falla,
        // se deniega aunque la política permitiera.
        //
        // El detalle dice quién, qué y POR QUÉ (P21 de 003, I2 T064): la base con que se dejó
        // pasar —la decide la guardia, no el llamador— y, si el llamador lo declaró, para qué se
        // entró. El prefijo `action=…; reason=…` es el de siempre: quien ya lo lee no cambia.
        var detalle = $"action={request.Action}; reason={reason ?? "ok"}; basis={basis}";
        if (Limpio(request.Proposito) is { } proposito)
        {
            detalle += $"; purpose={proposito}";
        }
        var evt = new AuditEvent(
            Guid.NewGuid().ToString("N"),
            DateTime.UtcNow,
            _gate.CurrentMemberEmail ?? string.Empty,
            _gate.CurrentMemberDisplayName ?? string.Empty,
            allowed ? "phi.access-granted" : "phi.access-denied",
            $"{request.ResourceType}:{request.TargetPatientKey?.ToString("N") ?? "-"}",
            allowed ? "success" : "failure",
            detalle);

        try
        {
            await _audit.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DefaultPhiAccessGuard: fallo de auditoría — acceso DENEGADO (fail-closed) a {Resource}",
                request.ResourceType);
            return new AccessDecision(false, "audit-unavailable", evt.Id);
        }

        return new AccessDecision(allowed, allowed ? null : reason, evt.Id);
    }

    /// <summary>
    /// El propósito declarado, sin lo que rompería el detalle (<c>;</c> y <c>=</c> separan sus campos,
    /// y un salto de línea partiría el registro). <c>null</c> si no queda nada.
    /// </summary>
    private static string? Limpio(string? proposito)
    {
        if (string.IsNullOrWhiteSpace(proposito))
        {
            return null;
        }
        var limpio = new string(proposito.Trim().Where(c => c is not (';' or '=') && !char.IsControl(c)).ToArray());
        return limpio.Length == 0 ? null : limpio;
    }

    private async Task<(bool allowed, string? reason, string basis)> EvaluateAsync(AccessCheckRequest request, CancellationToken ct)
    {
        if (!_gate.IsAuthenticated || _gate.CurrentMemberKey is not Guid actorKey)
        {
            return (false, "not-authenticated", Bases.Ninguna);
        }

        // ── Auto-acceso: el paciente sobre su propio recurso, y SÓLO PARA LEER.
        //
        // Este bloque estaba MUERTO. El único llamante (HealthcareApiController)
        // construía el AccessCheckRequest con tres argumentos posicionales, así
        // que TargetOwnerMemberKey era null siempre: ningún paciente podía leer
        // su propio expediente, sus citas ni sus recetas — todo caía a la rama
        // de roles y devolvía 403. El portal del paciente que ADR 0098 describe
        // no existía. El test que cubría esta rama la probaba en aislamiento,
        // nunca a través del controller.
        //
        // Ahora el dueño lo resuelve el GUARD, no el controller, por dos
        // razones: (1) quien decide debe reunir lo que necesita para decidir, y
        // (2) va DESPUÉS del chequeo de sesión de arriba, así que un anónimo no
        // provoca ni una lectura del repositorio — la propiedad "no se toca PHI
        // antes de autorizar" que verifica GetPatient_Anonymous_Returns401.
        //
        // El "read" es deliberado y NO estaba en el diseño original: sin él, un
        // paciente podría reescribir su propia historia clínica — las notas de
        // hallazgos y de valoración son campos de PatientRecord. Un paciente
        // tiene derecho a VER lo suyo; el criterio clínico lo firma quien lo
        // emite. Como la rama nunca se alcanzaba, estrecharla no le quita
        // permiso a nadie que hoy lo tenga.
        //
        // «declare» (003-salud-cita, I2 T058) es la otra cosa que el paciente hace sobre lo suyo: su
        // pre-registro. Sólo sobre su historia, y quien escribe lo confina a lo DECLARADO
        // (HistoriaClinica.Declarar no toca lo validado ni las notas). No es «write»: ese sigue siendo
        // del médico con el permiso.
        if (string.Equals(request.Action, "read", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(request.Action, "declare", StringComparison.OrdinalIgnoreCase)
                && string.Equals(request.ResourceType, "patient-record", StringComparison.OrdinalIgnoreCase)))
        {
            var owner = request.TargetOwnerMemberKey;

            // PatientKey ≠ MemberKey (IPatientRepository.cs:34-35). Si el
            // llamante no nos dio el dueño, lo resolvemos por la clave clínica.
            if (owner is null && request.TargetPatientKey is Guid patientKey && _patients is not null)
            {
                var patient = await _patients.GetAsync(patientKey, ct);
                owner = patient?.MemberKey;
            }

            if (owner is Guid ownerKey && ownerKey == actorKey)
            {
                return (true, null, Bases.Propio);
            }
        }

        // Abrir la historia (I2 T056, rompe el círculo de H5): la abre el consultorio, no el paciente. Es crear una
        // historia nueva sólo con lo demográfico; no da lectura ni escritura de nada clínico, que siguen pidiendo el
        // permiso del paciente.
        if (string.Equals(request.Action, "open", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.ResourceType, "patient-record", StringComparison.OrdinalIgnoreCase))
        {
            return _gate.HasAnyRole(AperturaRolesCsv)
                ? (true, null, Bases.AperturaDelPersonal)
                : (false, "insufficient-role", Bases.Ninguna);
        }

        // Agenda: operación de staff (no requiere consentimiento clínico).
        if (string.Equals(request.ResourceType, "appointment", StringComparison.OrdinalIgnoreCase)
            && _gate.HasAnyRole(SchedulingRolesCsv))
        {
            return (true, null, Bases.AgendaDelPersonal);
        }

        // Cargar el resultado de un examen (003-salud-cita, I4 P12): el médico o enfermería, con el mismo permiso del
        // paciente que pide el médico. Cargar no es liberar: el paciente lo ve cuando el médico lo libera («write»).
        if (string.Equals(request.Action, "load", StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.ResourceType, "lab-result", StringComparison.OrdinalIgnoreCase))
        {
            if (!_gate.HasAnyRole(CargaDeResultadosRolesCsv))
            {
                return (false, "insufficient-role", Bases.Ninguna);
            }
            return request.TargetPatientKey is Guid paciente
                ? await PermisoClinicoAsync(paciente, actorKey, ct).ConfigureAwait(false)
                : (false, "no-target-patient", Bases.Ninguna);
        }

        // Datos clínicos (historia/recetas/consentimiento): doctor CON consentimiento.
        if (_gate.HasAnyRole(DoctorRole))
        {
            if (request.TargetPatientKey is not Guid patientKey)
            {
                return (false, "no-target-patient", Bases.Ninguna);
            }

            return await PermisoClinicoAsync(patientKey, actorKey, ct).ConfigureAwait(false);
        }

        return (false, "insufficient-role", Bases.Ninguna);
    }

    /// <summary>
    /// El permiso clínico del paciente: con lector, el del consultorio en <c>Api.Consent</c>; sin él, el libro local de
    /// antes, médico por médico.
    /// </summary>
    private async Task<(bool allowed, string? reason, string basis)> PermisoClinicoAsync(Guid patientKey, Guid actorKey, CancellationToken ct)
    {
        if (_permisos is not null)
        {
            return await PermisoDelConsultorioAsync(patientKey, ct).ConfigureAwait(false);
        }

        var hasConsent = await _consent.HasActiveConsentAsync(patientKey, actorKey, ct).ConfigureAwait(false);
        return hasConsent ? (true, null, Bases.PermisoLocal) : (false, "no-active-consent", Bases.Ninguna);
    }

    /// <summary>
    /// El permiso del paciente para el consultorio, en <c>Api.Consent</c> (T054). Lo que no se pudo leer NIEGA: nunca se
    /// abre una historia sin un permiso comprobado (D-003-I2-1). Una historia sin miembro —desligada por una supresión,
    /// o abierta sin cuenta— no tiene quién dé el permiso, y tampoco se abre.
    /// </summary>
    private async Task<(bool allowed, string? reason, string basis)> PermisoDelConsultorioAsync(Guid patientKey, CancellationToken ct)
    {
        var paciente = _patients is null ? null : await _patients.GetAsync(patientKey, ct).ConfigureAwait(false);
        if (paciente is null || paciente.MemberKey == Guid.Empty)
        {
            return (false, "no-active-consent", Bases.Ninguna);
        }

        var permiso = await _permisos!.LeerAsync(SujetoDelPaciente, paciente.MemberKey.ToString("N"), PropositoDeLaHistoria, ct)
            .ConfigureAwait(false);
        return permiso.Estado switch
        {
            EstadoDelPermiso.Vigente => (true, null, Bases.PermisoDelConsultorio),
            EstadoDelPermiso.NoVigente => (false, "no-active-consent", Bases.Ninguna),
            _ => (false, "consent-unavailable", Bases.Ninguna),
        };
    }
}
