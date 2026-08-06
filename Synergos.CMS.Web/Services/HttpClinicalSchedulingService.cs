using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IClinicalSchedulingService"/> que reserva cupo de verdad — contra
/// <c>Synergos.Bff.Salud</c> (HU #25).
/// </summary>
/// <remarks>
/// <para><b>Contra el orquestador, por la misma razón que la tienda.</b> Agendar es apartar el
/// cupo <i>y</i> cobrar el copago <i>y</i> avisar; si el aviso falla el cupo tiene que quedar
/// tomado igual, y si el copago falla hay que soltarlo. Ese orden y esas compensaciones ya están
/// decididos en <c>Bff.Salud</c>. El motor en proceso los <b>reimplementa</b> —hace
/// <c>HoldItemAsync</c> → sesión de pago → <c>ConfirmAsync</c> con un <c>CancelAsync</c> de
/// respaldo— y ésa, no la durabilidad, es la razón de cablearlo.</para>
///
/// <para><b>Acá se manda el MÉDICO, no su agenda.</b> <c>Bff.Salud</c> pedía un
/// <c>resourceId</c> —el identificador interno que <c>Api.Booking</c> genera al registrar el
/// recurso— y <b>nadie río arriba puede conocer ese valor</b>: el CMS solo tiene el id de su
/// propio directorio. Exigirlo obligaba a que un identificador interno de una capacidad viajara
/// hasta la UI, que es justo lo que el contrato del orquestador dice que no debe pasar. Ahora el
/// BFF lo resuelve desde el profesional, y <c>Api.Booking</c> ganó la búsqueda por sujeto que
/// <c>Api.Inventory</c> ya tenía. Lo destapó levantar los procesos, no un test.</para>
///
/// <para><b><c>Api.Booking</c> sigue sin saber que el recurso es un médico</b>, y no puede
/// saberlo (<c>CLAUDE.md</c> §12: la capacidad es dueña del CUÁNDO; el orquestador, del QUÉ). El
/// sustantivo «doctor» vive acá y en <c>Bff.Salud</c>; lo que la capacidad guarda es un
/// <c>Ref</c> opaco que devuelve sin ramificar.</para>
///
/// <para><b>Los nombres NO vienen del BFF, y está bien.</b> La respuesta del orquestador lleva
/// referencias opacas —<c>patientId</c>, <c>professionalId</c>— porque una saga no es un
/// directorio. El nombre del paciente, el del médico y la especialidad se componen desde
/// <see cref="IPatientRegistry"/> y <see cref="IDoctorDirectory"/>, igual que hace el motor en
/// proceso. Pedirle esos datos al BFF sería meterle un sustantivo clínico a una máquina de
/// sagas.</para>
///
/// <para><b>Esto NO fortalece la identidad de quien accede.</b> El acuse de la HU #13 sigue
/// diciendo <c>CmsSession</c> —nuestro propio sistema dando fe— y esta HU no lo cambia. Cablear
/// <c>Api.Identity</c> como puerta es la HU #14, y aparentar lo contrario acá sería peor que no
/// hacerlo.</para>
/// </remarks>
public sealed class HttpClinicalSchedulingService : IClinicalSchedulingService
{
    /// <summary>Cabecera de la llave compartida.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string BffClientName = "synergos-bff-salud";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<SaludSettings> _settings;
    private readonly IPatientRegistry _patients;
    private readonly IDoctorDirectory _doctors;
    private readonly ILogger<HttpClinicalSchedulingService> _log;

    public HttpClinicalSchedulingService(
        IHttpClientFactory clients,
        IOptionsMonitor<SaludSettings> settings,
        IPatientRegistry patients,
        IDoctorDirectory doctors,
        ILogger<HttpClinicalSchedulingService> log)
    {
        _clients = clients;
        _settings = settings;
        _patients = patients;
        _doctors = doctors;
        _log = log;
    }

    public async Task<ClinicalAppointment> BookAsync(
        BookAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentException("Hace falta la solicitud.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.PatientId)) throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DoctorId)) throw new ArgumentException("El médico es obligatorio.", nameof(request));

        // El directorio se consulta ANTES de llamar. No es sólo para el nombre: la duración del
        // slot sale de acá, y el BFF exige una ventana con fin. Un médico que no existe se
        // rechaza sin haber tocado el cupo de nadie.
        var patient = await _patients.GetAsync(request.PatientId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"Paciente '{request.PatientId}' no encontrado.", nameof(request));
        var doctor = await _doctors.GetAsync(request.DoctorId, cancellationToken).ConfigureAwait(false)
            ?? throw new ArgumentException($"Médico '{request.DoctorId}' no encontrado.", nameof(request));

        var s = _settings.CurrentValue;
        var startUtc = DateTime.SpecifyKind(request.Slot, DateTimeKind.Utc);
        var endUtc = startUtc.AddMinutes(doctor.SlotMinutes > 0 ? doctor.SlotMinutes : 30);

        // La llave es determinista sobre QUÉ se agenda: paciente + médico + slot. Es lo que hace
        // que reintentar tras un timeout devuelva la cita que ya existía en vez de agendar una
        // segunda sobre el mismo cupo.
        var key = IdempotencyKeyFor(request.PatientId, request.DoctorId, startUtc);

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/appointments")
        {
            Content = JsonContent.Create(new
            {
                patientKind = s.PatientKind,
                patientId = request.PatientId,
                professionalKind = s.ProfessionalKind,
                professionalId = request.DoctorId,
                serviceKind = s.ServiceKind,
                serviceId = s.ServiceId,
                start = new DateTimeOffset(startUtc, TimeSpan.Zero),
                end = new DateTimeOffset(endUtc, TimeSpan.Zero),
            }),
        };
        req.Headers.Add("Idempotency-Key", key);

        var cita = await EnviarAsync(req, key, cancellationToken).ConfigureAwait(false);

        return new ClinicalAppointment(
            Id: cita.Id,
            PatientId: request.PatientId,
            PatientName: patient.FullName,
            DoctorId: request.DoctorId,
            DoctorName: doctor.FullName,
            Specialty: doctor.Specialty,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Status: Estado(cita.Status),
            ReservationId: cita.ReservationId ?? cita.Id);
    }

    /// <summary>
    /// La agenda de un día. <b>Devuelve vacío, y es una decisión anotada, no un olvido.</b>
    /// </summary>
    /// <remarks>
    /// <para><c>Bff.Salud</c> resuelve una cita por id y <b>no lista por fecha</b> — es el hueco
    /// que la propia HU #25 señala. La otra vía sería que el CMS consultara <c>Api.Booking</c>
    /// de frente, y la HU la deja abierta a refinamiento con un argumento correcto: <i>una
    /// lectura de agenda no es una saga, y al BFF se le llama cuando hay algo que deshacer</i>.
    /// </para>
    ///
    /// <para>Aun así <b>no se abre acá</b>, por consistencia con la misma decisión tomada en
    /// «mis compras» (HU #24): sería el primer sitio donde el CMS habla con una capacidad
    /// saltándose al orquestador, y esa puerta se abre una vez y ya no se cierra. La decisión es
    /// del arquitecto y está escrita en el ticket. <b>Vacío degrada; una agenda equivocada
    /// miente.</b></para>
    /// </remarks>
    public Task<IReadOnlyList<ClinicalAppointment>> GetByDateAsync(
        DateOnly date, string? doctorId = null, CancellationToken cancellationToken = default)
    {
        _log.LogDebug("Bff.Salud no lista citas por fecha todavía; la agenda sale vacía.");
        return Task.FromResult<IReadOnlyList<ClinicalAppointment>>(Array.Empty<ClinicalAppointment>());
    }

    // ── El cable ────────────────────────────────────────────────────────────

    private async Task<AppointmentDto> EnviarAsync(HttpRequestMessage req, string key, CancellationToken ct)
    {
        var http = _clients.CreateClient(BffClientName);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // el paciente cerró la pestaña
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Un timeout no dice «no se agendó»: dice «no sé». Y una cita a medias es peor que
            // ninguna, así que se PREGUNTA antes de rendirse — la llave es el identificador de
            // la saga.
            _log.LogWarning(ex, "La cita {Key} no respondió; se consulta si llegó a existir.", key);

            var existente = await BuscarAsync(key, ct).ConfigureAwait(false);
            if (existente is not null)
            {
                _log.LogWarning("La cita {Key} SÍ existía: se sigue con ella en vez de crear otra.", key);
                return existente;
            }

            _log.LogError(ex, "No se pudo agendar: el orquestador de Salud no respondió.");
            throw new InvalidOperationException("No pudimos agendar la cita. No quedó reservada.", ex);
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<AppointmentDto>(Json, ct).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("No pudimos agendar la cita: la respuesta vino vacía.");
            }

            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);

            // SOLO 401. El 403 NO es un fallo de llave: `SharedKeyAuth` responde 401 cuando la
            // llave falta o no cuadra, y nunca 403. Un 403 acá es un rechazo de negocio con todas
            // las letras —el que aparece de verdad es `consent.not_granted`, «no hay
            // consentimiento para salud.agenda»— y su motivo es exactamente lo que hay que
            // mostrar: lleva a pedir el consentimiento, mientras que un error genérico no lleva a
            // ninguna parte.
            //
            // Esto lo destapó levantar los procesos, no un test: el doble solo devolvía 403 para
            // el caso de la llave, así que el test confirmaba la misma suposición equivocada que
            // el código.
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError(
                    "Salud respondió 401 al agendar: la llave compartida es inválida o falta. "
                    + "Revisar Synergos:Salud:ApiKey.");
                throw new InvalidOperationException("No pudimos agendar la cita. No quedó reservada.");
            }

            _log.LogWarning("Salud rechazó agendar con {Status} ({Code}): {Detalle}",
                (int)res.StatusCode, problema.Code ?? "-", problema.Detail ?? "-");

            // El cupo tomado es el rechazo que el paciente TIENE que ver con su motivo: «ese
            // horario ya no está» lleva a elegir otro, y «error» no lleva a nada.
            //
            // Va como InvalidOperationException y no ArgumentException porque es lo que el motor
            // en proceso lanza para el mismo caso, y el controller ya lo traduce. Cambiar el tipo
            // según el origen haría que encender el modo Bff cambiara los códigos HTTP.
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(problema.Detail) ? "No pudimos agendar la cita." : problema.Detail!);
        }
    }

    private async Task<AppointmentDto?> BuscarAsync(string sagaId, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/appointments/{Uri.EscapeDataString(sagaId)}");
            using var res = await _clients.CreateClient(BffClientName).SendAsync(req, ct).ConfigureAwait(false);
            return res.IsSuccessStatusCode
                ? await res.Content.ReadFromJsonAsync<AppointmentDto>(Json, ct).ConfigureAwait(false)
                : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "No se pudo consultar la cita {Key}.", sagaId);
            return null;
        }
    }

    private static async Task<ProblemDto> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try
        {
            return await res.Content.ReadFromJsonAsync<ProblemDto>(Json, ct).ConfigureAwait(false) ?? new ProblemDto();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            return new ProblemDto();
        }
    }

    // ── Traducciones ────────────────────────────────────────────────────────

    /// <summary>La llave: determinista sobre paciente + médico + slot.</summary>
    /// <remarks>
    /// Que dos intentos del mismo paciente sobre el mismo cupo compartan llave es intencional: es
    /// indistinguible de un reintento, y ante la duda se prefiere no agendar dos veces.
    /// </remarks>
    internal static string IdempotencyKeyFor(string patientId, string doctorId, DateTime startUtc)
    {
        var semilla = $"{patientId}|{doctorId}|{startUtc:O}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(semilla));
        return "cita-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>Del estado de la saga al vocabulario del EHR-lite.</summary>
    private static string Estado(string? sagaStatus) => sagaStatus switch
    {
        "Completed" => "booked",
        "Compensated" or "Failed" => "cancelled",
        _ => "booked",
    };

    // Los DTO viven acá y NO en Synergos.CMS.Interfaces: son la forma del contrato HTTP con otro
    // servicio, no vocabulario del dominio del CMS.

    private sealed record MoneyDto(decimal Amount, string Currency);

    private sealed record AppointmentDto(
        string Id, string? PatientKind, string? PatientId, string? ProfessionalKind, string? ProfessionalId,
        DateTimeOffset Start, DateTimeOffset End, string? Status, MoneyDto? Total,
        string? ReservationId, int PendingCompensations, string? LastError);

    private sealed record ProblemDto
    {
        public string? Title { get; init; }
        public string? Detail { get; init; }
        public string? Code { get; init; }
    }
}
